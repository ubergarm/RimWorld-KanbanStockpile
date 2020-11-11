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

            int srt = State.Get(settings.owner.ToString());
            if(srt == -1) {
                srt = 100;
            }

            string label = "TD.StackRefillThreshold".Translate(srt);

            int tmp = (int)Widgets.HorizontalSlider(new Rect(0f, rect.yMin, rect.width, buttonMargin), srt, 0f, 100f, false, label, null, null, 1f);
            if(tmp != srt) {
                Log.Message("[DEBUG] Changed Stack Refill Threshold for settings with haulDestination named: " + settings.owner.ToString());
                State.Set(settings.owner.ToString(), tmp);
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
                srt = State.Get(slotGroup.Settings.owner.ToString());
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
            // The clipboard StorageSettings has no parent, so assume a null is the clipboard...
            string key = __instance?.owner?.ToString() ?? "___clipboard";
            Log.Message("[DEBUG][MP] ExposeData() with owner name: " + key);
            int srt = State.Get(key);
            if(srt == -1) {
                srt = 100;
            }
            Scribe_Values.Look(ref srt, "stackRefillThreshold", 100, false);
        }
    }


	[HarmonyPatch(typeof(Zone_Stockpile), nameof(Zone_Stockpile.PostDeregister))]
    static class Zone_Stockpile_PostDeregister_Patch {
        public static void Postfix(Zone_Stockpile __instance) {
            Log.Message("[DEBUG] Zone_Stockpile_PostDeregister_Patch.Postfix()");
            if(State.Contains(__instance.ToString())) {
                Log.Message("[DEBUG] Removing " + __instance.ToString());
                State.Del(__instance.ToString());
            }
        }
    }

    // TODO patch changing the name of a stockpile to update State.db as well

    // TODO fixup the rest of this to update State db upon copyfrom
    [HarmonyPatch(typeof(StorageSettings), nameof(StorageSettings.CopyFrom))]
	class StorageSettings_CopyFrom_Transpiler
	{
		//public void CopyFrom(StorageSettings other)

		public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
            //private void TryNotifyChanged()
			MethodInfo TryNotifyChangedInfo = AccessTools.Method(typeof(StorageSettings), "TryNotifyChanged");

			foreach (CodeInstruction i in instructions)
			{
				if(i.Calls(TryNotifyChangedInfo))
				{
					////RankComp.CopyFrom(__instance, other);
					//yield return new CodeInstruction(OpCodes.Ldarg_0);//this
					//yield return new CodeInstruction(OpCodes.Ldarg_1);//other
					//yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(RankComp), nameof(RankComp.CopyFrom)));
				}
				yield return i;
			}
		}
	}


}
