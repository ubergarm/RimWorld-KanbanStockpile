using HarmonyLib;
using UnityEngine;
using Verse;

namespace KanbanStockpile
{
    public class KanbanStockpileSettings : ModSettings
    {
        public bool aggressiveSimilarStackChecking = true;
        public bool considerDifferentMaterialSimilar = true;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref aggressiveSimilarStackChecking, "aggressiveSimilarStackChecking", true, true);
            Scribe_Values.Look(ref considerDifferentMaterialSimilar, "considerDifferentMaterialSimilar", true, true);
        }

        public static void DoWindowContents(Rect canvas)
        {
            var columnWidth = (canvas.width - 30)/2 - 2;
            var list = new Listing_Standard { ColumnWidth = columnWidth };
            list.Begin(canvas);
            list.Gap(4);

            list.CheckboxLabeled("KS.AggressiveSimilarStackChecking".Translate(),
                                 ref KanbanStockpile.Settings.aggressiveSimilarStackChecking,
                                 "KS.AggressiveSimilarStackCheckingTip".Translate());

            list.CheckboxLabeled("KS.ConsiderDifferentMaterialSimilar".Translate(),
                                 ref KanbanStockpile.Settings.considerDifferentMaterialSimilar,
                                 "KS.ConsiderDifferentMaterialSimilarTip".Translate());

            list.End();
        }
    }
}
