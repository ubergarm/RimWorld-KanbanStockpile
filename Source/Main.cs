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
using Verse.AI;

namespace KanbanStockpile
{
    [StaticConstructorOnStartup]
    public static class KanbanStockpileLoader
    {
        public static bool IsLWMDeepStorageLoaded;
        public static bool IsStockpileRankingLoaded;
        public static bool IsPickUpAndHaulLoaded;

        static KanbanStockpileLoader()
        {
            var harmony = new Harmony("net.ubergarm.rimworld.mods.kanbanstockpile");
            harmony.PatchAll();

            if (ModLister.GetActiveModWithIdentifier("LWM.DeepStorage") != null) {
                IsLWMDeepStorageLoaded = true;
                Log.Message("[KanbanStockpile] Detected LWM Deep Storage is loaded!");
            } else {
                IsLWMDeepStorageLoaded = false;
                KSLog.Message("[KanbanStockpile] Did *NOT* detect LWM Deep Storage...");
            }

            if (ModLister.GetActiveModWithIdentifier("Uuugggg.StockpileRanking") != null) {
                IsStockpileRankingLoaded = true;
                Log.Message("[KanbanStockpile] Detected Uuugggg's StockpileRanking is loaded!");
            } else {
                IsStockpileRankingLoaded = false;
                KSLog.Message("[KanbanStockpile] Did *NOT* detect Uuugggg's StockpileRanking...");
            }

            // Check for both the original and the re-uploaded one (which is basically the same)
            if ( (ModLister.GetActiveModWithIdentifier("Mehni.PickUpAndHaul") != null) ||
                 (ModLister.GetActiveModWithIdentifier("Mlie.PickUpAndHaul") != null) ) {
                IsPickUpAndHaulLoaded = true;
                Log.Message("[KanbanStockpile] Detected Mehni or Mlie PickUpAndHaul is loaded!");
                if(KanbanStockpile.Settings.PreventPickUpAndHaulOverHauling) {
                    PickUpAndHaul_WorkGiver_HaulToInventory_Patch.ApplyPatch(harmony);
                }
            } else {
                IsPickUpAndHaulLoaded = false;
                KSLog.Message("[KanbanStockpile] Did *NOT* detect Mehni or Mlie PickUpAndHaul...");
            }

            if (MP.enabled) {
                MP.RegisterSyncMethod(typeof(State), nameof(State.Set));
                MP.RegisterSyncMethod(typeof(State), nameof(State.Del));
                MP.RegisterSyncWorker<KanbanSettings>(State.SyncKanbanSettings, typeof(KanbanSettings), false, false);
            }
        }
    }

    public class KanbanStockpile : Mod
    {
        public static KanbanStockpileSettings Settings;

        public KanbanStockpile(ModContentPack content) : base(content)
        {
            Settings = GetSettings<KanbanStockpileSettings>();
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            KanbanStockpileSettings.DoWindowContents(inRect);
        }

        public override string SettingsCategory()
        {
            return "KanbanStockpile";
        }
    }

	public static class KSLog
    {
        [System.Diagnostics.Conditional("DEBUG")]
        public static void Message(string msg)
        {
            Verse.Log.Message(msg);
        }
    }

    //********************
    // Utilities
	public static class KSUtil {

        // credit to bananasss00 for this function
        public static bool TryGetKanbanSettings(this IntVec3 cell, Map map, out KanbanSettings ks, out SlotGroup slotGroup)
        {
            ks = new KanbanSettings();
            slotGroup = null;

            if (map == null) return false;

            slotGroup = cell.GetSlotGroup(map);
            if( slotGroup?.Settings == null ) return false;

            // grab latest configs for this stockpile from our state manager
            ks = State.Get(slotGroup.Settings.owner.ToString());

            // skip all this stuff now if stockpile is not configured to use at least one feature
            if (ks.srt == 100 && ks.ssl == 0 && ks.mss == 0) return false;

            return true;
        }

        // stored thing, haulable thing
        public static bool IsSimilarStack(Thing t, Thing thing) {
            // sanity check
            if (t == null || thing == null) return false;
            // don't count non-storable things as they aren't actually *in* the stockpile
            if (!t.def.EverStorable(false)) return false;
            // don't count it if it *is* itself
            if (t == thing) return false;
            // skip things that cannot stack and have a different defName (depending on settings)
            if ( !t.CanStackWith(thing) &&
                 !(KanbanStockpile.Settings.ConsiderDifferentMaterialSimilar && t.def.stackLimit == 1 && t.def.defName == thing.def.defName) ) return false;

            // even a partial stack is a dupe so count it regardless of stackCount
            return true;
        }

        // destination slotgroup, map, the haulable thing in question, and max count before returning
        public static int CountStoredSimilarStacks(SlotGroup slotGroup, Map map, Thing thing, int max, KanbanSettings ks = default) {
            int numDuplicates = 0;
            
            for (int i = 0; i < slotGroup.CellsList.Count; i++) {
                IntVec3 cell = slotGroup.CellsList[i];
                List<Thing> things = map.thingGrid.ThingsListAt(cell);

                for (int j = 0; j < things.Count; j++) {
                    Thing t = things[j];

                    if (!IsSimilarStack(t, thing)) continue;
                    
                    //prf compat if one stack adds up to multiple max stack sizes add as multiple stacks
                    if (ks.mss > 0) numDuplicates += (int)Math.Ceiling((decimal)t.stackCount / ks.mss);
                    else numDuplicates++;
                    if (numDuplicates >= max) {
                        return numDuplicates;
                    }
                }
            }

            // if we got here we didn't hit the max count, so return what we did find
            return numDuplicates;
        }

        // checks all existing stacks at c and if one needs refilled numDesired is exactly how many
        // return false on error or if no matching stacks
        // returns true on success and sets numDesired (even if it is 0)
        public static bool TryGetStackRefillThresholdDesired(this IntVec3 cell, SlotGroup slotGroup, Map map, Thing thing, KanbanSettings ks, out int numDesired)
        {
            numDesired = 0;
            Thing lowestQtyThing = null;

            List<Thing> things = map.thingGrid.ThingsListAt(cell);
            for (int i = 0; i < things.Count; i++) {
                Thing t = things[i];
                if (!t.def.EverStorable(false)) continue; // skip non-storable things as they aren't actually *in* the stockpile
                if (!t.CanStackWith(thing)) continue; // skip it if it cannot stack with thing to haul

                // at this point we have a similar stack so save it here and search the rest of things
                // and only work on the similar stack that has the lowest count (in case there are say 3x stacks in one cell)
                // which will refill the lowest stacks first then continue to the highest stacks in the cell
                if (lowestQtyThing == null)
                    lowestQtyThing = t;
                else if (t.stackCount < lowestQtyThing.stackCount)
                    lowestQtyThing = t;
            }

            // didn't find anything with similar stacks
            if (lowestQtyThing == null) return false;

            //stack limit for lowest quantaty thing
            int stackLimit = (ks.mss > 0) ? System.Math.Min(lowestQtyThing.def.stackLimit, ks.mss) : lowestQtyThing.def.stackLimit;

            // we have at least one similar stack, so figure out if it wants anything or not
            if (lowestQtyThing.stackCount > (stackLimit * ks.srt / 100f))
                numDesired = 0;
            else
                numDesired = (stackLimit - lowestQtyThing.stackCount);
            return true;
        }

        public static bool TryGetHaulingDestination(this Job job, out Map map, out IntVec3 dest, out SlotGroup slotGroup)
        {
            map = null;
            slotGroup = null;
            dest = new IntVec3();

            // Could print warnings here, hopefully these don't fail silently
            if (job == null) return false;

            if (job.def == JobDefOf.HaulToContainer) {
                Thing container = job.targetB.Thing;
                if (container == null) return false;
                map = container.Map;
                dest = container.Position;
            } else  {
                // case JobDefOf.HaulToCell
                dest = job.targetB.Cell;
                map = job.targetA.Thing.Map ?? job.targetA.Thing.MapHeld;

            }

            if (map == null) return false;
            if (dest == null) return false;

            slotGroup = dest.GetSlotGroup(map);
            if (slotGroup == null) return false;

            return true;
        }

        private static FieldInfo ReservationsListInfo = AccessTools.Field(typeof(ReservationManager), "reservations");
        public static int CountReservedSimilarStacks(SlotGroup slotGroup, Map map, Thing thing, int max, KanbanSettings ks = default) {
            int numDuplicates = 0;

            // Could print warnings here, hopefully these don't fail silently
            if (map.reservationManager == null) return 0;
            var reservations = ReservationsListInfo.GetValue(map.reservationManager) as List<ReservationManager.Reservation>;
            if (reservations == null) return 0;

            ReservationManager.Reservation r;
            for (int i = 0; i < reservations.Count; i++) {
                r = reservations[i];
                if (r == null) continue;
                if (r.Job == null) continue;
                if (!(r.Job.def == JobDefOf.HaulToCell ||
                      r.Job.def == JobDefOf.HaulToContainer) ) continue;

                if (!r.Job.TryGetHaulingDestination(out Map unusedMap, out IntVec3 unusedDest, out SlotGroup sg)) continue;

                if (sg == null) continue;
                if (sg != slotGroup) continue; // skip it this hauling reservation is going to a different stockpile

                Thing t = r.Job.targetA.Thing;

                if (!IsSimilarStack(t, thing)) continue;

                //prf compat if one stack adds up to multiple max stack sizes add as multiple stacks
                if (ks.mss > 0) numDuplicates += (int)Math.Ceiling((decimal)t.stackCount / ks.mss);
                else numDuplicates++;

                if (numDuplicates >= max) {
                    return numDuplicates;
                }
            }

            // if we got here we didn't hit the max count, so return what we did find
            return numDuplicates;
        }
    }
}
