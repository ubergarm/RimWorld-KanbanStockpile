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

		private static Dictionary<int, int> refillThreshold = new Dictionary<int, int>();
        public static int getRefillThreshold(int settingsHash) {
            if(refillThreshold.ContainsKey(settingsHash)) {
                return refillThreshold[settingsHash];
            }
            return -1;
        }
        [SyncMethod]
        public static void setRefillThreshold(int settingsHash, int rt) {
            refillThreshold[settingsHash] = rt;
        }
		public static PropertyInfo SelStoreInfo = AccessTools.Property(typeof(ITab_Storage), "SelStoreSettingsParent");
		public static void DrawRanking(ITab_Storage tab)
		{
			IHaulDestination haulDestination = SelStoreInfo.GetValue(tab, null) as IHaulDestination;
			if (haulDestination == null) return;
			StorageSettings settings = haulDestination.GetStoreSettings();
			if (settings == null) return;

			//ITab_Storage.WinSize = 300
			float buttonMargin = TopAreaHeight.extraHeight + 4;
			Rect rect = new Rect(0f, (float)GetTopAreaHeight.Invoke(tab, new object[] { }) - TopAreaHeight.extraHeight - 2, 280, TopAreaHeight.extraHeight);

           	//Slider
			rect.x += buttonMargin;
			rect.width -= buttonMargin * 4;
			Text.Font = GameFont.Small;

            int srt = getRefillThreshold(settings.GetHashCode());
            if(srt == -1) {
                srt = 100;
            }

            string label = "TD.StackRefillThreshold".Translate(srt);

            int tmp = (int)Widgets.HorizontalSlider(new Rect(0f, rect.yMin, rect.width, buttonMargin), srt, 0f, 100f, false, label, null, null, 1f);
            if(tmp != srt) {
                Log.Message("[DEBUG] Changed Stack Refill Threshold for settings w/ owner: " + settings.owner);
                Log.Message("[DEBUG] Changed Stack Refill Threshold for settings hash: " + settings.GetHashCode());
                Log.Message("[DEBUG] Changed Stack Refill Threshold for haulDestination: " + haulDestination);
                setRefillThreshold(settings.GetHashCode(), tmp);
            }
		}
	}


    [HarmonyPatch(typeof(StoreUtility), "NoStorageBlockersIn")]
    public class StoreUtility_NoStorageBlockersInPost_Patch
    {
        public static void Postfix(ref bool __result, IntVec3 c, Map map, Thing thing)
        {
            //FALSE IF ITS TOO FULL
            //TRUE IF THERE IS EMPTY SPACE

            //don't fill it if its already too full
            if (__result == false) return;

            int srt = 100;
            SlotGroup slotGroup=c.GetSlotGroup(map);
            if( (slotGroup != null) && (slotGroup.Settings != null) ) {
                srt = FillTab.getRefillThreshold(slotGroup.Settings.GetHashCode());
                if(srt == -1) {
                    srt = 100;
                }
            }

            __result &= !map.thingGrid.ThingsListAt(c).Any(t => t.def.EverStorable(false) && t.stackCount >= thing.def.stackLimit * (srt / 100f));
        }
    }


    [HarmonyPatch(typeof(StorageSettings), nameof(StorageSettings.ExposeData))]
    public class StorageSettings_ExposeData
    {
        [HarmonyPostfix]
        public static void ExposeData(StorageSettings __instance)
        {
            //StorageLimits storageLimits = StorageLimits.GetLimitSettings(__instance);
            int srt = FillTab.getRefillThreshold(__instance.GetHashCode());
            if(srt == -1) {
                srt = 100;
            }
            Scribe_Values.Look(ref srt, "refillThreshold", 100, false);

            FillTab.setRefillThreshold(__instance.GetHashCode(), srt);
        }
    }


	[HarmonyPatch(typeof(ThingFilterUI), nameof(ThingFilterUI.DoThingFilterConfigWindow))]
	static class ThingFilterUI_DoThingFilterConfigWindow_Patch
	{
        public static void Prefix(ref Rect rect)
        {
            //Log.Message("[DEBUG] ThingFilterUI_DoThingFilterConfigWindow_Patch.Prefix()");
            //ITab_Storage tab = Patch_ITab_StorageFillTabs.currentTab;
            //if (tab == null)
            //    return;
            //rect.yMin += 100f;
        }

        public static void Postfix(ref Rect rect)
        {
            //Log.Message("[DEBUG] ThingFilterUI_DoThingFilterConfigWindow_Patch.Postfix()");
            //ITab_Storage tab = Patch_ITab_StorageFillTabs.currentTab;
            //if (tab == null)
            //    return;
            //IStoreSettingsParent storeSettingsParent = (IStoreSettingsParent)typeof(ITab_Storage).GetProperty("SelStoreSettingsParent", BindingFlags.NonPublic | BindingFlags.Instance).GetGetMethod(true).Invoke(tab, new object[0]);
            //StorageSettings settings = storeSettingsParent.GetStoreSettings();
            //StorageLimits limitSettings = StorageLimits.GetLimitSettings(settings);
        }

	}

}
