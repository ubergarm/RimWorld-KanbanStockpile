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
        public struct ExtraConfig
        {
            public int srt; // stack refill threshold [0,100]
            public int ssl; // similar stack limit [0,256] (higher values in big stockpiles will eat CPU)
        }

        private static int defaultStackRefillThreshold = 100;
        private static int defaultSimilarStackLimit = 0;

        // key is a string from Verse.Zone.label (or pass in "___clipboard" as clipboard's owner is null)
        // val is an int whole number in range [0,100]
        private static Dictionary<string, ExtraConfig> db = new Dictionary<string, ExtraConfig>();

        public static bool Exists(string label) {
            return db.ContainsKey(label);
        }

        public static ExtraConfig Get(string label) {
            if(db.ContainsKey(label)) {
                return db[label];
            }
            ExtraConfig result;
            result.srt = defaultStackRefillThreshold;
            result.ssl = defaultSimilarStackLimit;
            return result;
        }

        [SyncMethod]
        public static void Set(string label, ExtraConfig ec) {
            db[label] = ec;
        }

        [SyncMethod]
        public static void Del(string label) {
            db.Remove(label);
        }
    }
}
