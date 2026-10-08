using System;
using System.IO;
using System.Security.Cryptography;
using UnityEngine;
using WashedAshore.Fish;

namespace WashedAshore.Tests.PlayMode.Fish
{
    /// <summary>
    /// The brief of record for the fish suites: _bmad-output/poc/fish-targets.json, read at test time so no target is a
    /// constant in a test (f-qa review 6). Its SHA-256 prefix must equal the shipped FishTuning's briefSha16.
    /// </summary>
    static class FishBrief
    {
        public const string JsonPath = "_bmad-output/poc/fish-targets.json";

#pragma warning disable 0649 // filled by JsonUtility
        [Serializable] public class Sightings
        {
            public float sightingsPerMinShoreWalk, spontaneousSurfaceEventsPerMin, surfaceEventViewRadius, jumpsPerMin, flushSignsPer100mWalked;
            public float[] sightingsBand, surfaceEventsBand, jumpsBand, flushBand, bluffBand;
            public float longestGapSec, firstSightingSec, bluffSurfaceEventsPerMin, bluffViewRadius, bodyRecountSec, minSightingSec;
            public int maxSightingsIn15s;
            public string routeShore, routeBluff;
            public float bluffDurationSec;
            public LegibleKind[] legibleDistanceByKind;   // F17: a sign counts only within this distance for the eye (0 = never)
            public string legibilityRule;
        }
        [Serializable] public class LegibleKind { public string kind; public float shore, McCord, Buzzard; }
        [Serializable] public class Visibility { public int maxVisibleBodiesShore, maxVisibleBodiesShoreHard, maxVisibleBodiesBluff; }
        [Serializable] class Root { public string briefRevision; public Sightings sightings; public Visibility visibility; public float walkSpeedForTargets; }
#pragma warning restore 0649

        static Root root;
        static string sha16;

        static void Load()
        {
            if (root != null) return;
            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", JsonPath));
            byte[] bytes = File.ReadAllBytes(path);
            using (var sha = SHA256.Create())
                sha16 = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").Substring(0, 16).ToLowerInvariant();
            root = JsonUtility.FromJson<Root>(System.Text.Encoding.UTF8.GetString(bytes));
        }

        public static string Sha16 { get { Load(); return sha16; } }
        public static string Revision { get { Load(); return root.briefRevision; } }
        public static Sightings Sighting { get { Load(); return root.sightings; } }
        public static Visibility Visible { get { Load(); return root.visibility; } }
        public static float WalkSpeed { get { Load(); return root.walkSpeedForTargets; } }

        public static Vector2 Band(float[] v) => new Vector2(v[0], v[1]);

        /// <summary>
        /// F17 legibleDistanceByKind for a sign and an eye ("shore", "McCord", "Buzzard"): metres, 0 = never; -1 if the brief
        /// has no row for the sign (the caller reports it, never guesses). Enum kinds map onto the brief's rows by name;
        /// Roll shares RollOrTail's row, FleeWake shares Wake's (a flee V-wake), and GarGulpOrBask splits by the bask flag.
        /// </summary>
        public static float LegibleDistance(FishSurfaceKind kind, bool bask, string eye)
        {
            string row = kind == FishSurfaceKind.Roll ? "RollOrTail" : kind == FishSurfaceKind.FleeWake ? "Wake"
                       : kind == FishSurfaceKind.GarGulpOrBask ? (bask ? "GarBask" : "GarGulp") : kind.ToString();
            var rows = Sighting.legibleDistanceByKind;
            if (rows == null) return -1f;
            foreach (var r in rows)
                if (r.kind == row) return eye == "shore" ? r.shore : eye == "McCord" ? r.McCord : eye == "Buzzard" ? r.Buzzard : -1f;
            return -1f;
        }
    }
}
