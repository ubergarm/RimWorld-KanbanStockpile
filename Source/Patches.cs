using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;
using RimWorld;
using HarmonyLib;
using Multiplayer.API;
using Verse;

// LWM.DeepStorage and Mehni PickUpAndHaul Dependency
// https://github.com/Mehni/PickUpAndHaul/tree/master/1.1/Assemblies
// sha1sum 27c0f6a544ff7c69bad900549e96b3460c8edbc2  DLLs/IHoldMultipleThings.dll
using IHoldMultipleThings;

namespace KanbanStockpile
{
    //********************
    //ITab_Storage Patches
	[HarmonyPatch(typeof(ITab_Storage), "TopAreaHeight", MethodType.Getter)]
	static class ITab_Storage_TopAreaHeight_Patch
	{
		//private float TopAreaHeight
		public const float extraHeight = 28f;
		public static void Postfix(ref float __result)
		{
			__result += extraHeight;
		}
	}

	[HarmonyPatch(typeof(ITab_Storage), "FillTab")]
	static class ITab_Storage_FillTab_Patch
	{
		//protected override void FillTab()
		static MethodInfo GetTopAreaHeight = AccessTools.Property(typeof(ITab_Storage), "TopAreaHeight").GetGetMethod(true);
		public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			//		public static void BeginGroup(Rect position);
			MethodInfo BeginGroupInfo = AccessTools.Method(typeof(GUI), nameof(GUI.BeginGroup), new Type[] { typeof(Rect) });

			//class Verse.ThingFilter RimWorld.StorageSettings::'filter'
			FieldInfo filterInfo = AccessTools.Field(typeof(StorageSettings), "filter");
			MethodInfo DoThingFilterConfigWindowInfo = AccessTools.Method(typeof(ThingFilterUI), "DoThingFilterConfigWindow");

			bool firstTopAreaHeight = true;
			List<CodeInstruction> instList = instructions.ToList();
			for(int i=0;i<instList.Count;i++)
			{
				CodeInstruction inst = instList[i];

                yield return inst;

				if (firstTopAreaHeight &&
					inst.Calls(GetTopAreaHeight))
				{
					firstTopAreaHeight = false;
					yield return new CodeInstruction(OpCodes.Ldc_R4, ITab_Storage_TopAreaHeight_Patch.extraHeight);
					yield return new CodeInstruction(OpCodes.Sub);
				}

				if(inst.Calls(BeginGroupInfo))
				{
					yield return new CodeInstruction(OpCodes.Ldarg_0);//ITab_Storage this
					yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(ITab_Storage_FillTab_Patch), nameof(DrawKanbanSettings)));
				}
			}
		}

        public static DateTime lastUpdateTime = DateTime.Now;
		public static PropertyInfo SelStoreInfo = AccessTools.Property(typeof(ITab_Storage), "SelStoreSettingsParent");
		public static void DrawKanbanSettings(ITab_Storage tab)
		{
			IHaulDestination haulDestination = SelStoreInfo.GetValue(tab, null) as IHaulDestination;
			if (haulDestination == null) return;
			StorageSettings settings = haulDestination.GetStoreSettings();
			if (settings == null) return;

			//ITab_Storage.WinSize = 300
			float buttonMargin = ITab_Storage_TopAreaHeight_Patch.extraHeight + 4;
			Rect rect = new Rect(0f, (float)GetTopAreaHeight.Invoke(tab, new object[] { }) - ITab_Storage_TopAreaHeight_Patch.extraHeight - 2, 280, ITab_Storage_TopAreaHeight_Patch.extraHeight);

            rect.x += buttonMargin;
            rect.width -= buttonMargin * 3;
            Text.Font = GameFont.Small;

            KanbanSettings ks, tmp;
            ks = State.Get(settings.owner.ToString());
            tmp.srt = ks.srt;
            tmp.ssl = ks.ssl;

            string stackRefillThresholdLabel = "KS.StackRefillThreshold".Translate(ks.srt);

            string similarStackLimitLabel;
            if (ks.ssl > 0) {
                similarStackLimitLabel  = "KS.SimilarStackLimit".Translate(ks.ssl);
            } else {
                similarStackLimitLabel  = "KS.SimilarStackLimitOff".Translate();
            }

            //Stack Refill Threshold Slider
            tmp.srt = (int)Widgets.HorizontalSlider(new Rect(0f, rect.yMin + 10f, 150f, 15f),
                                                    ks.srt, 0f, 100f, false, stackRefillThresholdLabel, null, null, 1f);

            //Similar Stack Limit Slider
            tmp.ssl = (int)Widgets.HorizontalSlider(new Rect(155, rect.yMin + 10f, 125f, 15f),
                                                    ks.ssl, 0f, 8f, false, similarStackLimitLabel, null, null, 1f);


            if( (ks.srt != tmp.srt) ||
                (ks.ssl != tmp.ssl) ) {

                // Accept slider changes no faster than 4Hz (250ms) to prevent spamming multiplayer sync lag
                DateTime curTime = DateTime.Now;
                if( (curTime - lastUpdateTime).TotalMilliseconds < 250) {
                    return;
                }
                lastUpdateTime = curTime;

                KSLog.Message("[KanbanStockpile] Changed Stack Refill Threshold for settings with haulDestination named: " + settings.owner.ToString());
                ks.srt = tmp.srt;
                ks.ssl = tmp.ssl;
                State.Set(settings.owner.ToString(), ks);
            }
		}
	}


    //********************
    //StoreUtility Patches
    [HarmonyPatch(typeof(StoreUtility), "NoStorageBlockersIn")]
    public class StoreUtility_NoStorageBlockersIn_Patch
    {
        public static void Postfix(ref bool __result, IntVec3 c, Map map, Thing thing)
        {
            // NOTE: Likely LWM Deep Storages Prefix() and Vanilla NoStorageBlockersIn() itself have already run
            // returning false means storage is "full" so do *not* try to haul the thing
            // returning true means storage still has space available for thing so try to haul it

            // storage already filled up so no need to try to limit it further
            if (__result == false) return;

            // make sure we have everything we need to continue
            SlotGroup slotGroup=c.GetSlotGroup(map);
            if( (slotGroup == null) || (slotGroup.Settings == null) ) return;

            KanbanSettings ks;
            ks = State.Get(slotGroup.Settings.owner.ToString());

            // skip all this stuff now unless stockpile is configured to use it
            if (ks.srt == 100 && ks.ssl == 0) return;

            // is there a better way to detect if a thing in this cell is a "deep storage" type?
            // maybe doing some fancy reflection or some runtime thing to reach into that mod? lol
            // not a big deal to depend on that IHoldMultipleThings.dll but there is a re-upload of
            // Pick Up And Haul which has some code changes, dunno about this specific lib though...
            bool isDeepStorage = ( (slotGroup?.parent is ThingWithComps) &&
                                   (((ThingWithComps)slotGroup.parent).AllComps.OfType<IHoldMultipleThings.IHoldMultipleThings>() != null) );

            //KSLog.Message("[KanbanStockpile] ======= Can We Haul This Thing? ======");
            //KSLog.Message("[KanbanStockpile] thing: " + thing + "thing.def.defName: " + thing.def.defName);
            //KSLog.Message("[KanbanStockpile] thing.stackCount = " + thing.stackCount + " and thing.def.stackLimit = " + thing.def.stackLimit);
            //KSLog.Message("[KanbanStockpile] Here are all similar things already in stockpile: " + slotGroup.Settings.owner);
            //KSLog.Message("[KanbanStockpile] Is this a deep storage? " + isDeepStorage);

            // TODO Refactor and clean this all up
            //    - use for loops instead of foreach for speed (like in vanilla function call)
            //    - can probably break out the refill stuff and put it before as no need to iterate over entire slotgroup for that
            //    - selectively skip code based on stockpile config



            int numDuplicates = 0;

            // StackRefillThreshold Check only here at cell c
            List<Thing> things = map.thingGrid.ThingsListAt(c);
            foreach (Thing t in things) {
                if (!t.def.EverStorable(false)) continue; // skip non-storable things as they aren't actually *in* the stockpile
                if (!t.CanStackWith(thing)) continue; // don't count it if it cannot stack

                // its okay to refill a partial stack below stack refill threshold if it is int this exact cell in question
                if ( (isDeepStorage) &&
                        (t.stackCount <= (t.def.stackLimit * ks.srt / 100f)) &&
                        (((t.stackCount + thing.stackCount) <= t.def.stackLimit)) ) {
                    // pawns seem to try to haul a full stack no matter what for deep storage unlike vanilla stockpiles
                    KSLog.Message("[KanbanStockpile] YES HAUL EXISTING PARTIAL STACK OF THING TO DEEP STORAGE!");
                    __result = true;
                    return;
                }
                if ( (!isDeepStorage) &&
                        (t.stackCount <= (t.def.stackLimit * ks.srt / 100f)) ) {
                    // pawns are smart enough to grab a partial stack for vanilla stockpile so no need to explicitly check here
                    KSLog.Message("[KanbanStockpile] YES HAUL PARTIAL STACK OF THING TO TOPOFF STACK IN STOCKPILE!");
                    __result = true;
                    return;
                }
                // TODO: possible optimization by incrementing numDuplicates here and skipping cell `c` in next check
            }

            // SimilarStackLimit Check all cells in the slotgroup (potentially CPU intensive for big zones/limits)
            if (ks.ssl == 0) return;
            foreach (IntVec3 cell in slotGroup.CellsList)
            {
                //List<Thing> things = map.thingGrid.ThingsListAt(cell);
                things = map.thingGrid.ThingsListAt(cell);
                foreach (Thing t in things) {
                    if (!t.def.EverStorable(false)) continue; // skip non-storable things as they aren't actually *in* the stockpile
                   	if (!t.CanStackWith(thing)) continue; // don't count it if it cannot stack

                    numDuplicates++;
                    if (numDuplicates >= ks.ssl) {
                        KSLog.Message("[KanbanStockpile] NO DON'T HAUL AS THERE IS ALREADY TOO MANY OF THAT KIND OF STACK!");
                        __result = false;
                        return;
                    }
                }
            }

            // if we got here, haul that thing!
            return;
        }
    }


    //********************
    //StorageSettings Patches
    [HarmonyPatch(typeof(StorageSettings), nameof(StorageSettings.ExposeData))]
    public class StorageSettings_ExposeData_Patch
    {
        public static void Postfix(StorageSettings __instance)
        {
            // The clipboard StorageSettings has no owner, so assume a null is the clipboard...
            string label = __instance?.owner?.ToString() ?? "___clipboard";
            KSLog.Message("[KanbanStockpile] ExposeData() with owner name: " + label);
            KanbanSettings ks = State.Get(label);

            if (Scribe.mode == LoadSaveMode.Saving)
            {
                // this mode implicitly takes the value currently in srt and saves it out
                Scribe_Values.Look(ref ks.srt, "stackRefillThreshold", 100, true);
                Scribe_Values.Look(ref ks.ssl, "similarStackLimit", 0, true);
            }
            else if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                // this mode implicitly loads some other value into this instance of srt
                Scribe_Values.Look(ref ks.srt, "stackRefillThreshold", 100, false);
                Scribe_Values.Look(ref ks.ssl, "similarStackLimit", 0, false);
                State.Set(label, ks);
            }
        }
    }

    [HarmonyPatch(typeof(StorageSettings), nameof(StorageSettings.CopyFrom))]
	class StorageSettings_CopyFrom_Patch
	{
		//public void CopyFrom(StorageSettings other)
        public static void CopyFrom(StorageSettings __instance, StorageSettings other)
        {
            KSLog.Message("[KanbanStockpile] CopyFrom()");
            string label = other?.owner?.ToString() ?? "___clipboard";
            KanbanSettings ks = State.Get(label);
            label = __instance?.owner?.ToString() ?? "___clipboard";
            State.Set(label, ks);
        }

		public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
            //private void TryNotifyChanged()
			MethodInfo TryNotifyChangedInfo = AccessTools.Method(typeof(StorageSettings), "TryNotifyChanged");

			foreach (CodeInstruction i in instructions)
			{
				if(i.Calls(TryNotifyChangedInfo))
				{
					//RankComp.CopyFrom(__instance, other);
					yield return new CodeInstruction(OpCodes.Ldarg_0);//this
					yield return new CodeInstruction(OpCodes.Ldarg_1);//other
					yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(StorageSettings_CopyFrom_Patch), nameof(StorageSettings_CopyFrom_Patch.CopyFrom)));
				}
				yield return i;
			}
		}
	}

    //********************
    //ZoneStockpile Patches
	[HarmonyPatch(typeof(Zone_Stockpile), nameof(Zone_Stockpile.PostDeregister))]
    static class Zone_Stockpile_PostDeregister_Patch
    {
        public static void Postfix(Zone_Stockpile __instance)
        {
            KSLog.Message("[KanbanStockpile] Zone_Stockpile_PostDeregister_Patch.Postfix()");
            if(State.Exists(__instance.ToString())) {
                KSLog.Message("[KanbanStockpile] Removing " + __instance.ToString());
                State.Del(__instance.ToString());
            }
        }
    }

    //********************
    //Dialog_RenameZone Patches
    [HarmonyPatch(typeof(Dialog_RenameZone), "SetName")]
    static class Dialog_RenameZone_SetName_Patch
    {
        public static void Prefix(Zone ___zone, string name)
        {
            //private Zone zone;
            string oldName = ___zone?.label ?? "N/A";

            KSLog.Message("[KanbanStockpile] Dialog_RenameZone.SetName() oldName: " + oldName);
            KSLog.Message("[KanbanStockpile] Dialog_RenameZone.SetName() newName: " + name);
            if(oldName == "N/A") return;

            State.Set(name, State.Get(oldName));
            State.Del(oldName);
        }
    }
}
