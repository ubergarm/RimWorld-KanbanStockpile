using HarmonyLib;
using UnityEngine;
using Verse;

namespace KanbanStockpile
{
    public class KanbanStockpileSettings : ModSettings
    {
        public bool ReservedSimilarStackChecking = true;
        public bool ConsiderDifferentMaterialSimilar = true;
        public bool PreventPickUpAndHaulOverHauling = true;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref ReservedSimilarStackChecking, "ReservedSimilarStackChecking", true, true);
            Scribe_Values.Look(ref ConsiderDifferentMaterialSimilar, "ConsiderDifferentMaterialSimilar", true, true);
            Scribe_Values.Look(ref PreventPickUpAndHaulOverHauling, "PreventPickUpAndHaulOverHauling", true, true);
        }

        public static void DoWindowContents(Rect canvas)
        {
            var columnWidth = (canvas.width - 30)/2 - 2;
            var list = new Listing_Standard { ColumnWidth = columnWidth };
            list.Begin(canvas);
            list.Gap(4);

            list.CheckboxLabeled("KS.ReservedSimilarStackChecking".Translate(),
                                 ref KanbanStockpile.Settings.ReservedSimilarStackChecking,
                                 "KS.ReservedSimilarStackCheckingTip".Translate());

            list.CheckboxLabeled("KS.ConsiderDifferentMaterialSimilar".Translate(),
                                 ref KanbanStockpile.Settings.ConsiderDifferentMaterialSimilar,
                                 "KS.ConsiderDifferentMaterialSimilarTip".Translate());

            list.CheckboxLabeled("KS.PreventPickUpAndHaulOverHauling".Translate(),
                                 ref KanbanStockpile.Settings.PreventPickUpAndHaulOverHauling,
                                 "KS.PreventPickUpAndHaulOverHaulingTip".Translate());

            list.End();
        }
    }
}
