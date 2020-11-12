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
		public const float extraHeight = 24f;
		//private float TopAreaHeight
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

           	//Slider
			rect.x += buttonMargin;
			rect.width -= buttonMargin * 4;
			Text.Font = GameFont.Small;

            int srt = State.Get(settings.owner.ToString());

            string label = "LD.StackRefillThreshold".Translate(srt);

            int tmp = (int)Widgets.HorizontalSlider(new Rect(0f, rect.yMin, rect.width, buttonMargin), srt, 0f, 100f, false, label, null, null, 1f);
            if(tmp != srt) {
                Log.Message("[KanbanStockpile] Changed Stack Refill Threshold for settings with haulDestination named: " + settings.owner.ToString());
                State.Set(settings.owner.ToString(), tmp);
            }
		}
	}


    //********************
    //StoreUtility Patches
    [HarmonyPatch(typeof(StoreUtility), "NoStorageBlockersIn")]
    public class StoreUtility_NoStorageBlockersIn_Patch
    {

        // TODO: add duplicate stacks limit
        public static bool NewStackAllowed(SlotGroup slotGroup, IntVec3 c, Map map, Thing t)
        {
            // Log.Message("Checking for duplicates");
            //int dupStackLimit = StorageLimits.GetLimitSettings(slotGroup.Settings).dupStackLimit;
            // FIXME TESTING
            int dupStackLimit = 1;
            int numDuplicates = 0;
            foreach (IntVec3 cell in slotGroup.CellsList)
            {
                // TODO: print out this list of ivec3's to figure out what its scannin'
                Log.Message("[KanbanStockpile] slotGroup.CellsList cell: = " + cell);
                //if (cell != c)
                //{
                    // TODO FIXME HACK: Maybe gotta iterate over all things at cell not just 2 w/ deep storage
                    //Thing checkThings = map.thingGrid.ThingAt(cell, t.def);
                    List<Thing> checkThings = map.thingGrid.ThingsListAt(cell);
                    foreach (Thing tmpth in checkThings)
                    {
                        //if (tmpth != null)
                        // check categories: if (list[i].def.category == cat)
                        Log.Message("[KanbanStockpile] thing t: " + t + "and tmpth " + tmpth);
                        if (tmpth.CanStackWith(t) ||
                           (t.def.stackLimit == 1 && t.def.defName == tmpth.def.defName))
                        {
                            numDuplicates++;
                            if (numDuplicates == dupStackLimit)
                                Log.Message("[KanbanStockpile] DEEP DUPE FOUND thing: " + t + "and thing2: " + tmpth);
                                return false;
                        }
                    }

                    //
                    //
                    //Thing thing2 = map.thingGrid.ThingAt(cell, t.def);
                    //if (thing2 != null)
                    //{
                    //    if (thing2.CanStackWith(t) ||
                    //       (t.def.stackLimit == 1 && t.def.defName == thing2.def.defName))
                    //    {
                    //        numDuplicates++;
                    //        if (numDuplicates == dupStackLimit)
                    //            return false;
                    //    }
                    //}
                //}
            }
            return true;
        }


        public static void Postfix(ref bool __result, IntVec3 c, Map map, Thing thing)
        {
            //returning false means storage is considered full
            //returning true means storage still has space available

            //its already full
            if (__result == false) return;

            int srt = 100;
            SlotGroup slotGroup=c.GetSlotGroup(map);
            if( (slotGroup != null) && (slotGroup.Settings != null) )
            {
                srt = State.Get(slotGroup.Settings.owner.ToString());
            }

            // This code mostly from https://github.com/hooap/SatisfiedStorage
            // Handle LWM Deep Storage components which use Mehni's PickUpAndHaul IHoldMultipleThings
            if (KanbanStockpileLoader.IsLWMDeepStorageLoaded)
            {
                foreach(ThingWithComps twc in map.thingGrid.ThingsListAt(c).OfType<ThingWithComps>())
                {
                    foreach (IHoldMultipleThings.IHoldMultipleThings comp in twc.AllComps.OfType<IHoldMultipleThings.IHoldMultipleThings>())
                    {
                        // how many *more* of a thing that can fit into this cell of deep storage
                        // (how much capacity is still available)
                        int capacity = 0;

                        //bool CapacityAt(Thing thing, IntVec3 storeCell, Map map, out int capacity);
                        comp.CapacityAt(thing, c, map, out capacity);
                        // if total capacity is larger than the stackLimit (full stack available)
                        //    Allow hauling (other choices are valid)
                        // if (capacity > thing.def.stackLimit) return true;
                        // only haul if count is below threshold
                        //   which is equivalent to availability being above threshold:
                        //            Log.Message("capacity = " + capacity);
                        Log.Message("[KanbanStockpile] Deep Storage Capacity = " + capacity);
                        //            Log.Message("thing.def.stackLimit = " +thing.def.stackLimit);
                        float fill = (100f * (float)capacity / thing.def.stackLimit);

                        //100 - num is necessary because capacity gives empty space not full space
                        __result = fill > (100 - srt);
                        if (__result == false) return;
                        //      if (__result == false){
                        //          Log.Message("ITS TOO FULL stop yey");
                        //      }

                        // TODO: add duplicate stack limit for deep storage
                        if (!NewStackAllowed(slotGroup, c, map, thing))
                        {
                            __result = false;
                        }

                        return;
                    }
                }
            }

            // TODO: add duplicate stacks limit
            int dupStackLimit = 1;
            if (dupStackLimit > 0)
            {
                //Log.Message("Check if more stacks are allowed.")
                if (!NewStackAllowed(slotGroup, c, map, thing))
                {
                    __result = false;
                    return;
                }
            }

            // Send back the results modified by KanbanStockpile StackRefillThreshold
            __result &= !map.thingGrid.ThingsListAt(c).Any(t => t.def.EverStorable(false) && t.stackCount >= thing.def.stackLimit * (srt / 100f));
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
            Log.Message("[KanbanStockpile] ExposeData() with owner name: " + label);
            int srt = State.Get(label);

            if (Scribe.mode == LoadSaveMode.Saving)
            {
                // this mode implicitly takes the value currently in srt and saves it out
                Scribe_Values.Look(ref srt, "stackRefillThreshold", 100, true);
            }
            else if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                // this mode implicitly loads some other value into this instance of srt
                Scribe_Values.Look(ref srt, "stackRefillThreshold", 100, false);
                State.Set(label, srt);
            }
        }
    }

    [HarmonyPatch(typeof(StorageSettings), nameof(StorageSettings.CopyFrom))]
	class StorageSettings_CopyFrom_Patch
	{
		//public void CopyFrom(StorageSettings other)
        public static void CopyFrom(StorageSettings __instance, StorageSettings other)
        {
            Log.Message("[KanbanStockpile] CopyFrom()");
            string label = other?.owner?.ToString() ?? "___clipboard";
            int srt = State.Get(label);
            label = __instance?.owner?.ToString() ?? "___clipboard";
            State.Set(label, srt);
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
            Log.Message("[KanbanStockpile] Zone_Stockpile_PostDeregister_Patch.Postfix()");
            if(State.Exists(__instance.ToString())) {
                Log.Message("[KanbanStockpile] Removing " + __instance.ToString());
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

            Log.Message("[KanbanStockpile] Dialog_RenameZone.SetName() oldName: " + oldName);
            Log.Message("[KanbanStockpile] Dialog_RenameZone.SetName() newName: " + name);
            if(oldName == "N/A") return;

            State.Set(name, State.Get(oldName));
            State.Del(oldName);
        }
    }
}
