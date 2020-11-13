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

        static KanbanStockpileLoader()
        {
            var harmony = new Harmony("net.ubergarm.rimworld.mods.kanbanstockpile");
            harmony.PatchAll();

            if (ModLister.GetActiveModWithIdentifier("LWM.DeepStorage") != null) {
                IsLWMDeepStorageLoaded = true;
                Log.Message("[KanbanStockpile] Detected LWM Deep Storage is loaded!");
            } else {
                IsLWMDeepStorageLoaded = false;
                Log.Message("[KanbanStockpile] Did *NOT* detect LWM Deep Storage...");
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
        public KanbanStockpile(ModContentPack content) : base(content)
        {
        }
    }
}
