using HarmonyLib;
using UnityEngine;
using Verse;

namespace KanbanStockpile
{
    public class KanbanStockpileSettings : ModSettings
    {
        public bool aggressiveSimilarStockpileLimiting = false;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref aggressiveSimilarStockpileLimiting, "aggressiveSimilarStockpileLimiting", false, true);
        }

        public static void DoWindowContents(Rect canvas)
        {
            var columnWidth = (canvas.width - 30)/2 - 2;
            var list = new Listing_Standard { ColumnWidth = columnWidth };
            list.Begin(canvas);
            list.Gap(4);

            list.Label("KS.Experimental".Translate());
            list.CheckboxLabeled("KS.AggressiveSimilarStockpileLimiting".Translate(), ref KanbanStockpile.Settings.aggressiveSimilarStockpileLimiting);

            list.End();
        }
    }
}
