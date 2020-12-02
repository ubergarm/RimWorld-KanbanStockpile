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
                PickUpAndHaul_WorkGiver_HaulToInventory_Patch.ApplyPatch(harmony);
            } else {
                IsPickUpAndHaulLoaded = false;
                KSLog.Message("[KanbanStockpile] Did *NOT* detect Mehni or Mlie PickUpAndHaul...");
            }

            if (MP.enabled) {
                //MP.RegisterAll();
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

    // Provide Compatibility with PickUpAndHaul
    [DefOf]
    public static class PickUpAndHaulJobDefOf
    {
        public static JobDef UnloadYourHauledInventory;
        public static JobDef HaulToInventory;
    }

    // Provide Commpatibility with PickUpAndHaul
    public class CellAllocation
    {
        public Thing allocated;
        public int capacity;

        public CellAllocation(Thing a, int c)
        {
            allocated = a;
            capacity = c;
        }
    }

    //********************
    // Utilities
	public static class KSUtil {

        // list of all stored things, the haulable thing in question, and max count before returning
        public static int CountSimilarStacks(List<Thing> things, Thing thing, int max) {
            int numDuplicates = 0;
            for (int i = 0; i < things.Count; i++) {
                Thing t = things[i];
                if (t == null) continue;
                // don't count non-storable things as they aren't actually *in* the stockpile
                if (!t.def.EverStorable(false)) continue;
                // don't count it if it *is* itself
                if (t == thing) continue;
                // skip things that cannot stack and have a different defName (depending on settings)
                if ( !t.CanStackWith(thing) &&
                     !(KanbanStockpile.Settings.considerDifferentMaterialSimilar && t.def.stackLimit == 1 && t.def.defName == thing.def.defName) ) continue;

                // even a partial stack is a dupe so count it regardless of stackCount
                numDuplicates++;
                if (numDuplicates >= max) {
                    return numDuplicates;
                }
            }

            // if we got here we didn't hit the max count, so return what we did find
            return numDuplicates;
        }

        // credit to bananasss00 for this refactor
        public static bool TryGetKanbanSettings(this IntVec3 cell, Map map, out KanbanSettings ks, out SlotGroup slotGroup)
        {
            ks = new KanbanSettings();
            slotGroup = cell.GetSlotGroup(map);
            if( slotGroup?.Settings == null ) return false;

            // grab latest configs for this stockpile from our state manager
            ks = State.Get(slotGroup.Settings.owner.ToString());

            // skip all this stuff now if stockpile is not configured to use at least one feature
            if (ks.srt == 100 && ks.ssl == 0) return false;

            return true;
        }
    }

}
