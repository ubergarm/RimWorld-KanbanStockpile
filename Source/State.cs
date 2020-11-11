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

// Implement a static state storage database using Set/Get/Del/Exists kind of like redis
// Make sure to SyncMethod for any method that changes internal state for multiplayer
namespace KanbanStockpile
{
    static class State
    {
        private static int defaultStackRefillThreshold = 100;

        private static Dictionary<string, int> db = new Dictionary<string, int>();

        public static bool Exists(string label) {
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
