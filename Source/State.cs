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
    static class State
    {
        private static int defaultStackRefillThreshold = 100;

        private static Dictionary<string, int> db = new Dictionary<string, int>();

        public static bool Contains(string label) {
            return db.ContainsKey(label);
        }

        public static int Get(string label) {
            if(db.ContainsKey(label)) {
                return db[label];
            }
            return defaultStackRefillThreshold;
        }

        [SyncMethod]
        public static void Set(string label, int srt) {
            db[label] = srt;
        }

        [SyncMethod]
        public static void Del(string label) {
            db.Remove(label);
        }
    }
}
