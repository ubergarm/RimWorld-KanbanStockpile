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
        private static FieldInfo ReservationsListInfo = AccessTools.Field(typeof(ReservationManager), "reservations");
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

            // grab latest configs for this stockpile from our state manager
            KanbanSettings ks;
            ks = State.Get(slotGroup.Settings.owner.ToString());

            // skip all this stuff now if stockpile is not configured to use at least one feature
            if (ks.srt == 100 && ks.ssl == 0) return;

            // Assuming JobDefOf.HaulToContainer for Building_Storage vs JobDefOf.HaulToCell otherwise
            bool isContainer = (slotGroup?.parent is Building_Storage);

            // StackRefillThreshold checks only here at cell c
            List<Thing> things = map.thingGrid.ThingsListAt(c);
            int numDuplicates = 0;

            // TODO #5 consider re-ordering to prevent refilling an accidental/leftover duplicate stack
            // Design Decision: use for loops instead of foreach as they may be faster and similar to this vanilla function
            for (int i = 0; i < things.Count; i++) {
                Thing t = things[i];
                if (!t.def.EverStorable(false)) continue; // skip non-storable things as they aren't actually *in* the stockpile
                if (!t.CanStackWith(thing)) continue; // skip it if it cannot stack with thing to haul
                if (t.stackCount > (t.def.stackLimit * ks.srt / 100f)) continue; // no need to refill until count is below threshold

                if (!isContainer) {
                    // pawns are smart enough to grab a partial stack for vanilla cell stockpiles so no need to explicitly check here
                    // maybe this is a JobDefOf.HaulToCell job?
                    KSLog.Message("[KanbanStockpile] YES HAUL PARTIAL STACK OF THING TO TOPOFF STACK IN CELL STOCKPILE!");
                    __result = true;
                    return;
                } else if (((t.stackCount + thing.stackCount) <= t.def.stackLimit)) {
                    // pawns seem to try to haul a full stack no matter what for HaulToContainer unlike HaulToCell CurJobDef's
                    // so for here when trying to haul to deep storage explicitly ensure stack to haul is partial stack
                    // maybe this is a JobDefOf.HaulToContainer job?
                    KSLog.Message("[KanbanStockpile] YES HAUL EXISTING PARTIAL STACK OF THING TO BUILDING STORAGE CONTAINER!");
                    __result = true;
                    return;
                }
            }

            if (ks.ssl == 0) return;
            // SimilarStackLimit check all cells in the slotgroup (potentially CPU intensive for big zones/limits)
            // SlotGroup.HeldThings
            for (int j = 0; j < slotGroup.CellsList.Count; j++) {
                IntVec3 cell = slotGroup.CellsList[j];
                things = map.thingGrid.ThingsListAt(cell);
                for (int i = 0; i < things.Count; i++) {
                    Thing t = things[i];
                    if (!t.def.EverStorable(false)) continue; // skip non-storable things as they aren't actually *in* the stockpile
                    // skip things that cannot stack and have a different defName (depending on settings)
                    if ( !t.CanStackWith(thing) &&
                         !(KanbanStockpile.Settings.considerDifferentMaterialSimilar && t.def.stackLimit == 1 && t.def.defName == thing.def.defName) ) continue;

                    // even a partial stack is a dupe so count it regardless
                    numDuplicates++;
                    if (numDuplicates >= ks.ssl) {
                        KSLog.Message("[KanbanStockpile] NO DON'T HAUL AS THERE IS ALREADY TOO MANY OF THAT KIND OF STACK!");
                        __result = false;
                        return;
                    }
                }
            }

            // iterate over all outstanding reserved jobs to prevent hauling duplicate similar stacks
            if (KanbanStockpile.Settings.aggressiveSimilarStackChecking == false) return;
            if (map.reservationManager == null) return;
            var reservations = ReservationsListInfo.GetValue(map.reservationManager) as List<ReservationManager.Reservation>;
            if (reservations == null) return;
            ReservationManager.Reservation r;
            for (int i = 0; i < reservations.Count; i++) {
                r = reservations[i];
                if (r == null) continue;
                if (r.Job == null) continue;
                KSLog.Message($"[KanbanStockpile] some reserved job has JobDefOf: {r.Job.def}");
                if (!(r.Job.def == JobDefOf.HaulToCell || r.Job.def == JobDefOf.HaulToContainer || r.Job.def == PickUpAndHaulJobDefOf.HaulToInventory)) continue;

                Thing t = r.Job.targetA.Thing;
                if (t == null) continue;
                if (t == thing) continue;  // no need to check against itself
                // skip things that cannot stack and have a different defName (depending on settings)
                if ( !t.CanStackWith(thing) &&
                     !(KanbanStockpile.Settings.considerDifferentMaterialSimilar && t.def.stackLimit == 1 && t.def.defName == thing.def.defName) ) continue;

                IntVec3 dest;
                if (r.Job.def == JobDefOf.HaulToCell) {
                    dest = r.Job.targetB.Cell;
                } else {
                    // case of JobDefOf.HaulToContainer or PickUpAndHaulJobDefOf.HaulToInventory
                    KSLog.Message($"[KanbanStockpile] INNER SANCTUM WITH JobDefOf: {r.Job.def}");
                    Thing container = r.Job.targetB.Thing;
                    if (container == null) continue;
                    dest = container.Position;
                }

                if (dest == null) continue;
                SlotGroup sg = dest.GetSlotGroup(map);
                if (sg == null) continue;
                if (sg != slotGroup) continue; // skip it as the similar thing is being hauled to a different stockpile

                // there is a thing that can stack with this thing and is already reserved for hauling to the desired stockpile: DUPE!
                numDuplicates++;
                if (numDuplicates >= ks.ssl) {
                    KSLog.Message("[KanbanStockpile] NO DON'T HAUL AS THERE IS ALREADY SOMEONE RESERVING JOB TO DO IT!");
                    __result = false;
                    return;
                }
            }

            // if we get here, haul that thing!
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
