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
// Make sure to register SyncMethod for any method that mutates state for multiplayer syncing
namespace KanbanStockpile
{
    public struct KanbanSettings
    {
        public int srt; // stack refill threshold [0,100]
        public int ssl; // similar stack limit [0,8] (higher values in big stockpiles will eat CPU)
        public int mss; // maximum stack size feature [0,...]
    }

    static class State
    {
        private static int defaultStackRefillThreshold = 100;
        private static int defaultSimilarStackLimit = 0;
        private static int defaultMaxStackSize = 0;

        // key is a string from Verse.Zone.label (or pass in "___clipboard" as clipboard's owner is null)
        // val is an int whole number in range [0,100]
        private static Dictionary<string, KanbanSettings> db = new Dictionary<string, KanbanSettings>();

        public static bool Exists(string label) {
            return db.ContainsKey(label);
        }

        public static KanbanSettings Get(string label) {
            if(db.ContainsKey(label)) {
                return db[label];
            }
            KanbanSettings result;
            result.srt = defaultStackRefillThreshold;
            result.ssl = defaultSimilarStackLimit;
            result.mss = defaultMaxStackSize;
            return result;
        }

        //[SyncMethod]
        public static void Set(string label, KanbanSettings ks) {
            db[label] = ks;
        }

        //[SyncMethod]
        public static void Del(string label) {
            if(db.ContainsKey(label)) {
                db.Remove(label);
            }
        }

        //[SyncWorker(shouldConstruct = false)]
        public static void SyncKanbanSettings(SyncWorker sync, ref KanbanSettings ks)
        {
            sync.Bind(ref ks.srt);
            sync.Bind(ref ks.ssl);
            sync.Bind(ref ks.mss);
        }
    }
}
