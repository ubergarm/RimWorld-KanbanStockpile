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

        // key is a string from Verse.Zone.label (or pass in "___clipboard" as its parent zone is null)
        // val is an int whole number in range [0,100]
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
