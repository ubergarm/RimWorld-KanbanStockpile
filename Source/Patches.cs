using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;
using RimWorld;
using HarmonyLib;
using Multiplayer.API;
using Verse;
using Verse.AI;

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
            //	public static void BeginGroup(Rect position);
            MethodInfo BeginGroupInfo = AccessTools.Method(typeof(Widgets), nameof(Widgets.BeginGroup), new Type[] { typeof(Rect) });

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

            // if Stockpile Ranking is installed, scootch these widgets up so it doesn't overlap
            // https://github.com/alextd/RimWorld-StockpileRanking/blob/master/Source/RankSelection.cs#L18
            if (KanbanStockpileLoader.IsStockpileRankingLoaded) {
                rect.y -= 26f;
            }

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

                //KSLog.Message("[KanbanStockpile] Changed Stack Refill Threshold for settings with haulDestination named: " + settings.owner.ToString());
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
            if(!c.TryGetKanbanSettings(map, out var ks, out var slotGroup)) return;

            // Check Stack Refill Threshold
            if (c.TryGetStackRefillThresholdDesired(slotGroup, map, thing, ks.srt, out int numDesired)) {
                //KSLog.Message($"[KanbanStockpile] haulable {thing} going to {slotGroup} wants exactly {numDesired} units!");
                if (numDesired > 0)
                    __result = true;
                else
                    __result = false;
                return;
            }

            if (ks.ssl == 0) return;
            int numDuplicates = 0;

            // first check for duplicates already sitting there completely stored in stockpile
            numDuplicates += KSUtil.CountStoredSimilarStacks(slotGroup, map, thing, (ks.ssl - numDuplicates));
            if (numDuplicates >= ks.ssl) {
                //KSLog.Message($"[KanbanStockpile] Don't haul {thing} as {slotGroup} already contains at least {numDuplicates} stacks!");
                __result = false;
                return;
            }

            // optionally check reservations for any "in flight" hauling jobs to reduce redundant over-hauling
            if (KanbanStockpile.Settings.ReservedSimilarStackChecking == false) return;

            numDuplicates += KSUtil.CountReservedSimilarStacks(slotGroup, map, thing, (ks.ssl - numDuplicates));
            if (numDuplicates >= ks.ssl) {
                //KSLog.Message($"[KanbanStockpile] [Reserved] Don't haul {thing} as {slotGroup} already contains at least {numDuplicates} stacks!");
                __result = false;
                return;
            }

            // if we get here, haul that thing!
            return;
        }
    }



    //********************
    //HaulAIUtility Patches
    [HarmonyPatch(typeof(HaulAIUtility), nameof(HaulAIUtility.HaulToStorageJob))]
    public class HaulAIUtility_HaulToStorageJob_Patch
    {
        public static void Postfix(ref Job __result, Pawn p, Thing t)
        {
            if (__result == null) return;

            // figure out where this hauling job is going
            if (!__result.TryGetHaulingDestination(out Map map, out IntVec3 dest, out SlotGroup sg)) return;

            // make sure we have everything we need to continue
            if(!dest.TryGetKanbanSettings(t.Map, out var ks, out var slotGroup)) return;

            // Check Stack Refill Threshold
            if (dest.TryGetStackRefillThresholdDesired(slotGroup, map, t, ks.srt, out int numDesired)) {
                __result.count = Math.Min(__result.count, numDesired);
            }

            //KSLog.Message($"[KanbanStockpile] HaulToStorageJob Postfix() {p} hauling {__result.count}x {t} going to {map} {dest} {sg}");
            // cancel this haul if it turns out the stockpile wants 0
            if(__result.count <= 0) __result = null;

            return;
        }
    }

    //********************
    //PickUpAndHaul Patches
    static class PickUpAndHaul_WorkGiver_HaulToInventory_Patch
    {
        // apply patches manually at runtime using reflection to get PUAH types and methods
        public static void ApplyPatch(Harmony harmony)
        {
            var originalType = AccessTools.TypeByName("PickUpAndHaul.WorkGiver_HaulToInventory");
            if(originalType == null) {
                Log.Warning("[KanbanStockpile] ERROR: TypeByName. Unable to patch PUAH mod.");
                return;
            }

            var CapacityAtMethod = AccessTools.Method(originalType, "CapacityAt");
            if(CapacityAtMethod == null) {
                Log.Warning("[KanbanStockpile] ERROR: Unable to patch PUAH method CapacityAt");
                return;
            }

            Log.Message($"[KanbanStockpile] Patching Mehni/Mlie PickUpAndHaul mod!");

            // postfix patch
            harmony.Patch(CapacityAtMethod,
                          null,
                          new HarmonyMethod(typeof(PickUpAndHaul_WorkGiver_HaulToInventory_Patch),
                          "CapacityAtPostfix"));

        }

        public static void CapacityAtPostfix(ref int __result, Thing thing, IntVec3 storeCell, Map map)
        {
            // if there is no capacity left no need to limit it further
            if(__result <= 0) return;

            // make sure we have everything we need to continue
            if(!storeCell.TryGetKanbanSettings(map, out var ks, out var slotGroup)) return;

            // PUAH doesn't do reservations correctly: fall back to vanilla hauling to avoid redundant overhauling if srt/ssl enabled
            if (ks.srt == 100 && ks.ssl == 0) return;
            __result = 0;

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
        static readonly MethodBase Building_Storage_PostMake = AccessTools.Method(typeof(Building_Storage), nameof(Building_Storage.PostMake));

		//public void CopyFrom(StorageSettings other)
        public static void CopyFrom(StorageSettings __instance, StorageSettings other)
        {
            var st = new StackTrace();
            if (st.FrameCount > 3 && st.GetFrame(2).GetMethod() == Building_Storage_PostMake)
                return; // prevent copy settings when called from Building_Storage:PostMake, new storage - bananasss00

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
