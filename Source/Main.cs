using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using System;
using System.Reflection;
using UnityEngine;
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

}
