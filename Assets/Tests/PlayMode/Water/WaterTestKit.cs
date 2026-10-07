using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using WashedAshore.Gameplay;
using WashedAshore.World;

namespace WashedAshore.Tests.PlayMode
{
    /// <summary>Shared lookups for the water PlayMode tests (spec W6, W8, W9).</summary>
    static class WaterTestKit
    {
        public const string StationsPath = "Data/terrain/build/bank_stations.json";

#pragma warning disable 0649 // filled by JsonUtility
        [Serializable]
        public class Station
        {
            public int i;
            public float x, z, preBankMaxY, maxSlope;
            public bool lowBank;
            public string excluded;
            public Vector3 Position => new Vector3(x, 0f, z);
            public override string ToString() => $"station {i} ({x:F1},{z:F1})";
        }

        [Serializable]
        class StationList { public Station[] s; }
#pragma warning restore 0649

        public static WaterBody Water()
        {
            var water = WaterBody.Active;
            Assert.IsNotNull(water, "No active WaterBody in World");
            Assert.IsNotNull(water.Config, "WaterBody has no MapConfig");
            return water;
        }

        /// <summary>The terrain pipeline's shore stations, in shoreline order.</summary>
        public static Station[] Stations()
        {
            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", StationsPath));
            Assert.IsTrue(File.Exists(path), $"No bank stations at {path}");
            var list = JsonUtility.FromJson<StationList>("{\"s\":" + File.ReadAllText(path) + "}");
            Assert.IsNotNull(list?.s, $"Could not read {path}");
            return list.s;
        }

        /// <summary>Low-bank stations not excluded (bluff or north-line seam) and at least <paramref name="southOfLine"/> m south of the line.</summary>
        public static List<Station> LowBank(Station[] all, WorldBoundsRule rule, float southOfLine)
        {
            var low = new List<Station>();
            foreach (var s in all)
                if (s.lowBank && string.IsNullOrEmpty(s.excluded) && rule.NorthOfLine(s.Position) < -southOfLine) low.Add(s);
            return low;
        }

        /// <summary>The nearest low-bank station on each side of every run of bluff-excluded stations (qa: 2 per bluff).</summary>
        public static List<Station> BluffNeighbours(Station[] all, List<Station> low)
        {
            var lowIds = new HashSet<int>();
            foreach (var s in low) lowIds.Add(s.i);
            var picks = new List<Station>();
            for (int k = 0; k < all.Length; k++)
            {
                bool bluff = all[k].excluded != null && all[k].excluded.Contains("bluff");
                bool startsRun = bluff && (k == 0 || !(all[k - 1].excluded ?? "").Contains("bluff"));
                bool endsRun = bluff && (k == all.Length - 1 || !(all[k + 1].excluded ?? "").Contains("bluff"));
                if (startsRun) for (int j = k - 1; j >= 0; j--) if (lowIds.Contains(all[j].i)) { picks.Add(all[j]); break; }
                if (endsRun) for (int j = k + 1; j < all.Length; j++) if (lowIds.Contains(all[j].i)) { picks.Add(all[j]); break; }
            }
            return picks;
        }

        /// <summary>The low-bank station nearest <paramref name="xz"/>.</summary>
        public static Station Nearest(List<Station> low, Vector3 xz)
        {
            Station best = null;
            float bestD = float.MaxValue;
            foreach (var s in low)
            {
                float d = (new Vector2(s.x - xz.x, s.z - xz.z)).sqrMagnitude;
                if (d < bestD) { bestD = d; best = s; }
            }
            return best;
        }

        /// <summary><paramref name="count"/> distinct stations drawn with a fixed seed (logged by the caller).</summary>
        public static List<Station> Pick(List<Station> low, int count, int seed)
        {
            var rng = new System.Random(seed);
            var pool = new List<Station>(low);
            var picks = new List<Station>();
            while (picks.Count < count && pool.Count > 0)
            {
                int k = rng.Next(pool.Count);
                picks.Add(pool[k]);
                pool.RemoveAt(k);
            }
            return picks;
        }

        public static bool Ground(Vector3 p, out float h) => TerrainQuery.TryGroundHeight(p, out h);

        /// <summary>
        /// Offshore bearing (unit XZ) from a shore point: of 72 bearings, the one whose ground stays
        /// below the surface from 2 m out and is deepest at <paramref name="probe"/> m. False if none.
        /// </summary>
        public static bool Offshore(Vector3 shore, float water, float probe, out Vector3 dir)
        {
            dir = Vector3.zero;
            float best = float.MaxValue;
            for (int b = 0; b < 72; b++)
            {
                var d = Quaternion.Euler(0f, b * 5f, 0f) * Vector3.forward;
                bool wet = true;
                for (float r = 2f; r <= probe && wet; r += 1f)
                    wet = Ground(shore + d * r, out float h) && h < water;
                if (!wet || !Ground(shore + d * probe, out float end) || end >= best) continue;
                best = end;
                dir = d;
            }
            return best < float.MaxValue;
        }

        /// <summary>First point along <paramref name="dir"/> from <paramref name="from"/> where the water is at least <paramref name="depth"/> deep.</summary>
        public static bool FirstDepth(Vector3 from, Vector3 dir, float water, float depth, float maxDist, out float dist)
        {
            for (dist = 0f; dist <= maxDist; dist += 0.5f)
                if (Ground(from + dir * dist, out float h) && water - h >= depth) return true;
            return false;
        }

        /// <summary>Puts the player at <paramref name="xz"/> with feet at <paramref name="feetY"/>, facing <paramref name="yaw"/>.</summary>
        public static void Place(PlayerController player, Vector3 xz, float feetY, float yaw)
        {
            var cc = player.GetComponent<CharacterController>();
            cc.enabled = false;
            player.transform.SetPositionAndRotation(new Vector3(xz.x, feetY, xz.z), Quaternion.Euler(0f, yaw, 0f));
            cc.enabled = true;
        }

        public static float Yaw(Vector3 dir) => Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;

        public static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        public static float Median(List<float> xs)
        {
            if (xs.Count == 0) return float.NaN;
            var s = new List<float>(xs);
            s.Sort();
            return s[s.Count / 2];
        }

        /// <summary>
        /// Swim speed graded on the stroke mean (water-swim-spec.md §7): per-frame speeds at 1/60 s, the first
        /// <paramref name="settle"/> seconds dropped, then the mean over as many whole strokes as fit.
        /// </summary>
        public static float StrokeMean(List<float> frameSpeeds, float strokePeriod, float settle, float dt = 1f / 60f)
        {
            int skip = Mathf.CeilToInt(settle / dt), perStroke = Mathf.RoundToInt(strokePeriod / dt);
            int strokes = perStroke > 0 ? (frameSpeeds.Count - skip) / perStroke : 0;
            if (strokes <= 0) return float.NaN;
            float sum = 0f;
            for (int i = skip; i < skip + strokes * perStroke; i++) sum += frameSpeeds[i];
            return sum / (strokes * perStroke);
        }

        /// <summary>Appends rows under a header to TestResults/<paramref name="file"/> (never deleted).</summary>
        public static void AppendCsv(string file, string header, List<string> rows)
        {
            string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "TestResults"));
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, file);
            if (!File.Exists(path)) File.WriteAllText(path, header + "\n");
            foreach (var row in rows) File.AppendAllText(path, row + "\n");
        }
    }
}
