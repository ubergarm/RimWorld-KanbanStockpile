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
        public static bool IsSameSpotInstalled;

        static KanbanStockpileLoader()
        {
            var harmony = new Harmony("net.ubergarm.rimworld.mods.kanbanstockpile");
            harmony.PatchAll();

            if (MP.enabled) {
                MP.RegisterAll();
            }
        }
    }

	public class KanbanStockpile : Mod
	{
        public KanbanStockpile(ModContentPack content) : base(content)
        {
        }
    }


    // Patch GUI
	[HarmonyPatch(typeof(ITab_Storage), "TopAreaHeight", MethodType.Getter)]
	static class TopAreaHeight
	{
		public const float extraHeight = 24f;
		//private float TopAreaHeight
		public static void Postfix(ref float __result)
		{
			__result += extraHeight;
		}
	}

	[HarmonyPatch(typeof(ITab_Storage), "FillTab")]
	static class FillTab
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

                if (inst.LoadsField(filterInfo) &&
					instList[i + 8].Calls(DoThingFilterConfigWindowInfo))
				{
					//instead of settings.filter, do RankComp.GetFilter(settings, curRank)
					//yield return new CodeInstruction(OpCodes.Ldsfld, AccessTools.Field(typeof(FillTab), "curRank"));
					//yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(RankComp), "GetFilter"));
                    yield return inst;
				}
				else
					yield return inst;

				if (firstTopAreaHeight &&
					inst.Calls(GetTopAreaHeight))
				{
					firstTopAreaHeight = false;
					yield return new CodeInstruction(OpCodes.Ldc_R4, TopAreaHeight.extraHeight);
					yield return new CodeInstruction(OpCodes.Sub);
				}

				if(inst.Calls(BeginGroupInfo))
				{
					yield return new CodeInstruction(OpCodes.Ldarg_0);//ITab_Storage this
					yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(FillTab), nameof(DrawRanking)));
				}
			}
		}

		public static PropertyInfo SelStoreInfo = AccessTools.Property(typeof(ITab_Storage), "SelStoreSettingsParent");
		public static void DrawRanking(ITab_Storage tab)
		{
			IHaulDestination haulDestination = SelStoreInfo.GetValue(tab, null) as IHaulDestination;
			if (haulDestination == null) return;
			StorageSettings settings = haulDestination.GetStoreSettings();
			if (settings == null) return;

			float buttonMargin = TopAreaHeight.extraHeight + 4;
			//ITab_Storage.WinSize = 300
			Rect rect = new Rect(0f, (float)GetTopAreaHeight.Invoke(tab, new object[] { }) - TopAreaHeight.extraHeight - 2, 280, TopAreaHeight.extraHeight);

           	//Label
			rect.x += buttonMargin;
			rect.width -= buttonMargin * 3;
			Text.Font = GameFont.Small;
            Widgets.Label(rect, "Stack Refill Threshold");
		}
	}

	[HarmonyPatch(typeof(ThingFilterUI), nameof(ThingFilterUI.DoThingFilterConfigWindow))]
	static class ThingFilterUI_DoThingFilterConfigWindow_Patch
	{
        public static void Prefix(ref Rect rect)
        {
            Log.Message("[DEBUG] ThingFilterUI_DoThingFilterConfigWindow_Patch.Prefix()");
            //ITab_Storage tab = Patch_ITab_StorageFillTabs.currentTab;
            //if (tab == null)
            //    return;
            //rect.yMin += 100f;
        }

        public static void Postfix(ref Rect rect)
        {
            Log.Message("[DEBUG] ThingFilterUI_DoThingFilterConfigWindow_Patch.Postfix()");
            //ITab_Storage tab = Patch_ITab_StorageFillTabs.currentTab;
            //if (tab == null)
            //    return;
            //IStoreSettingsParent storeSettingsParent = (IStoreSettingsParent)typeof(ITab_Storage).GetProperty("SelStoreSettingsParent", BindingFlags.NonPublic | BindingFlags.Instance).GetGetMethod(true).Invoke(tab, new object[0]);
            //StorageSettings settings = storeSettingsParent.GetStoreSettings();
            //StorageLimits limitSettings = StorageLimits.GetLimitSettings(settings);
        }

	}

}
