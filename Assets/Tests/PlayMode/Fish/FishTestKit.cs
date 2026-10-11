using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using WashedAshore.Fish;
using WashedAshore.Gameplay;
using WashedAshore.Level;
using WashedAshore.World;

namespace WashedAshore.Tests.PlayMode.Fish
{
    /// <summary>
    /// Shared setup for the fish PlayMode suites: World.unity at a seed with the brief of record checked
    /// (FishTuning.briefSha16 == fish-targets.json on disk), the PlayerController parked (the fish see a scripted player
    /// through FishPopulation.PlayerOverride and the walk places the camera), game time pinned at 1/60 s, f-qa's probe
    /// recording to TestResults/fish-probe-&lt;suite&gt;-&lt;seed&gt;.jsonl, and the brief's routes built from the level data.
    /// </summary>
    static class FishTestKit
    {
        public const string WorldScene = "World";
        public const string StationsPath = "Data/terrain/build/bank_stations.json";
        public const float Step = 1f / 60f;
        public static readonly int[] Seeds = { 101, 202, 303 };

#pragma warning disable 0649
        [Serializable] class Station { public int i; public float x, z; }
        [Serializable] class StationList { public Station[] s; }
#pragma warning restore 0649

        static List<Vector2> route;

        public static string ResultsDir
        {
            get
            {
                string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "TestResults"));
                Directory.CreateDirectory(dir);
                return dir;
            }
        }

        /// <summary>Loads World at <paramref name="seed"/>, checks the brief of record and starts the probe for <paramref name="suite"/>.</summary>
        public static IEnumerator Load(int seed, string suite)
        {
            FishRandom.OverrideSeed(seed);
            FishSignDraws.Reset();
            // Interpolated numbers in result files use the current culture: pin it, so "1,5" can never be written.
            System.Threading.Thread.CurrentThread.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
            Time.captureDeltaTime = Step;
            SceneManager.LoadScene(WorldScene, LoadSceneMode.Single);
            yield return null;
            yield return null;
            var pop = FishPopulation.Active;
            Assert.IsNotNull(pop, "No active FishPopulation in World (run FishSceneSetup)");
            Assert.AreEqual(FishBrief.Sha16, pop.Tuning.briefSha16,
                $"FishTuning is from brief {pop.Tuning.briefRevision}, the file on disk is {FishBrief.Revision}; rerun the import");
            Assert.AreEqual(seed, pop.ActiveSeed);
            var player = UnityEngine.Object.FindAnyObjectByType<PlayerController>();
            Assert.IsNotNull(player, "No PlayerController in World");
            player.enabled = false;
            var cc = player.GetComponent<CharacterController>();
            if (cc) cc.enabled = false;
            var probe = pop.gameObject.AddComponent<FishProbe>();
            probe.Path = Path.Combine(ResultsDir, $"fish-probe-{suite}-{seed}.jsonl");
        }

        public static void Restore()
        {
            FishRandom.OverrideSeed(null);
            FishPopulation.OverrideEnabled(null);
            Time.captureDeltaTime = 0f;
            if (FishPopulation.Active)
            {
                FishPopulation.Active.PlayerOverride = null;
                var probe = FishPopulation.Active.GetComponent<FishProbe>();
                if (probe) UnityEngine.Object.Destroy(probe);
            }
        }

        static Match RouteText(string pattern)
        {
            var m = Regex.Match(FishBrief.Sighting.routeShore ?? "", pattern);
            Assert.IsTrue(m.Success, $"sightings.routeShore in {FishBrief.JsonPath} no longer matches '{pattern}'");
            return m;
        }

        /// <summary>Eye height and the yaw toward the water, from the brief's routeShore text.</summary>
        public static float Eye => float.Parse(RouteText(@"eye ([0-9.]+) m").Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        public static float YawTowardWater => float.Parse(RouteText(@"\+ ([0-9.]+) deg toward the water").Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        public static float WalkSpeed => FishBrief.WalkSpeed;

        /// <summary>
        /// The brief's shore route (routeShore: bank stations first->last every Nth, M m inland), rebuilt from the bank
        /// stations: each station moved inland along the LevelMaps shore-distance gradient until it reads M m.
        /// </summary>
        public static List<Vector2> Route()
        {
            if (route != null) return route;
            var m = RouteText(@"bank st (\d+)->(\d+) every (\d+)\w*, (\d+) m inland");
            int from = int.Parse(m.Groups[1].Value), to = int.Parse(m.Groups[2].Value), every = int.Parse(m.Groups[3].Value);
            float inland = float.Parse(m.Groups[4].Value);
            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", StationsPath));
            var list = JsonUtility.FromJson<StationList>("{\"s\":" + File.ReadAllText(path) + "}");
            var water = FishPopulation.Active.Water;
            route = new List<Vector2>();
            foreach (var s in list.s)
            {
                if (s.i < from || s.i > to || (s.i - from) % every != 0) continue;
                var p = new Vector2(s.x, s.z);
                for (int k = 0; k < 40 && water.ShoreDistance(p.x, p.y) < inland; k++)
                {
                    Vector2 g = new Vector2(water.ShoreDistance(p.x + 1f, p.y) - water.ShoreDistance(p.x - 1f, p.y),
                                            water.ShoreDistance(p.x, p.y + 1f) - water.ShoreDistance(p.x, p.y - 1f));
                    if (g.sqrMagnitude < 1e-6f) break;
                    p += g.normalized * 0.5f;
                }
                route.Add(p);
            }
            Assert.Greater(route.Count, 10, "shore route rebuilt from bank stations");
            return route;
        }

        public static float RouteLength()
        {
            var r = Route();
            float d = 0f;
            for (int i = 1; i < r.Count; i++) d += Vector2.Distance(r[i - 1], r[i]);
            return d;
        }

        /// <summary>Feet on the route at walked distance <paramref name="d"/>, facing travel + YawTowardWater.</summary>
        public static void OnRoute(float d, out Vector3 feet, out Quaternion look)
        {
            var r = Route();
            float yawOff = YawTowardWater;
            for (int i = 1; i < r.Count; i++)
            {
                Vector2 a = r[i - 1], b = r[i];
                float seg = Vector2.Distance(a, b);
                if (d > seg && i < r.Count - 1) { d -= seg; continue; }
                Vector2 p = Vector2.Lerp(a, b, Mathf.Clamp01(d / Mathf.Max(seg, 1e-3f)));
                feet = new Vector3(p.x, 0f, p.y);
                feet.y = TerrainQuery.TryGroundHeight(feet, out float h) ? h : 0f;
                Vector2 dir = (b - a).normalized;
                look = Quaternion.Euler(0f, Mathf.Atan2(dir.x, dir.y) * Mathf.Rad2Deg + yawOff, 0f);
                return;
            }
            feet = Vector3.zero;
            look = Quaternion.identity;
        }

        /// <summary>
        /// The bluff look, brief F16 sightings.routeBluff: from the landmark P0 march along the bank normal n of the anchor
        /// point nearest P0 in 0.5 m steps (TryGroundHeight throughout). E is the last sample of the CONTIGUOUS run from P0
        /// with ground >= ground(P0) - 1.0 m (the crest; a later rise further out doesn't count). F is the first sample with
        /// ground <= W, failing loudly if none within 200 m. Eye = E + 1.7 m. Yaw = n. Pitch = atan2(h, d_F) - vFOV/2 + 3 deg
        /// with h = eye height above W, d_F = |F - E| and vFOV the camera's at run time. The numbers go to <paramref name="report"/>.
        /// </summary>
        public static void BluffPose(string landmark, string anchor, Camera cam, out Vector3 feet, out Quaternion look, out string report)
        {
            var lm = GameObject.Find(landmark);
            Assert.IsNotNull(lm, $"{landmark} missing from World");
            Vector3 p0 = lm.transform.position;
            Assert.IsTrue(TerrainQuery.TryGroundHeight(p0, out float g0), $"{landmark}: no ground");
            Vector2 n = Vector2.zero;
            float best = float.MaxValue;
            foreach (var an in FishPopulation.Active.Anchors.anchors)
            {
                if (!an.name.Contains(anchor)) continue;
                for (int i = 0; i < an.points.Length; i++)
                {
                    float d = (an.points[i] - new Vector2(p0.x, p0.z)).sqrMagnitude;
                    if (d < best) { best = d; n = an.normals[i]; }
                }
            }
            Assert.Greater(n.sqrMagnitude, 0f, $"no {anchor} anchor normals");
            n.Normalize();
            var dir = new Vector3(n.x, 0f, n.y);
            float w = FishPopulation.Active.Water.WaterLevelY;
            Vector3 edge = p0;
            float edgeOut = 0f, footOut = -1f;
            bool onCrest = true;
            for (float d = 0f; d <= 200f; d += 0.5f)
            {
                var p = p0 + dir * d;
                Assert.IsTrue(TerrainQuery.TryGroundHeight(p, out float g), $"{landmark}: no tile {d} m out");
                if (onCrest && g >= g0 - 1f) { edge = new Vector3(p.x, g, p.z); edgeOut = d; }
                else onCrest = false;
                if (g <= w) { footOut = d; break; }
            }
            Assert.GreaterOrEqual(footOut, 0f, $"{landmark}: no ground at or below W within 200 m along the normal");
            feet = edge;
            float h = edge.y + Eye - w, dF = footOut - edgeOut, vfov = cam ? cam.fieldOfView : 60f;
            float pitch = Mathf.Atan2(h, dF) * Mathf.Rad2Deg - vfov / 2f + 3f;
            look = Quaternion.LookRotation(dir, Vector3.up) * Quaternion.Euler(pitch, 0f, 0f);
            report = $"{{\"landmark\":\"{landmark}\",\"EoutM\":{edgeOut:F1},\"E\":[{edge.x:F1},{edge.y:F2},{edge.z:F1}],\"eyeHeightAboveW\":{h:F2},\"dF\":{dF:F1}," +
                     $"\"vFOV\":{vfov:F1},\"pitchDeg\":{pitch:F1},\"normal\":[{n.x:F2},{n.y:F2}]}}";
        }

        /// <summary>Places the scripted player and the main camera (eye height, looking along <paramref name="look"/>).</summary>
        public static void Place(Vector3 feet, Quaternion look, WaterMode mode)
        {
            var pop = FishPopulation.Active;
            pop.PlayerOverride = new FishPlayer { position = feet, mode = mode };
            var cam = Camera.main;
            if (cam) cam.transform.SetPositionAndRotation(feet + Vector3.up * Eye, look);
        }

        public static float Surface(Vector3 p) => WaterBody.Active ? WaterBody.Active.SurfaceY(p.x, p.z) : FishPopulation.Active.Water.WaterLevelY;

        /// <summary>
        /// The brief's "not terrain-occluded": true when the ground surface rises above the eye-to-point line anywhere
        /// between them. A march over the terrain heights every 0.5 m (the same test as f-qa's offline grade), so neither
        /// tree colliders nor a landmark or prop collider around the eye (the bluff-top sign post) can hide the water.
        /// </summary>
        public static bool TerrainBlocks(Vector3 eye, Vector3 point)
        {
            float flat = Flat(eye, point);
            int steps = Mathf.Max(2, Mathf.CeilToInt(flat / 0.5f));
            for (int i = 1; i < steps; i++)
            {
                var p = Vector3.Lerp(eye, point, i / (float)steps);
                if (TerrainQuery.TryGroundHeight(p, out float g) && g > p.y) return true;
            }
            return false;
        }

        public static float Flat(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));

        /// <summary>Every result file goes through the FishJson gate: it must parse, or the test throws and nothing is written.</summary>
        public static void Write(string file, string json) => FishJson.WriteFile(Path.Combine(ResultsDir, file), json);

        public static float Length(FishPopulation pop, in FishState s) => s.sizeScale * pop.Bodies.bodies[s.variantIndex].noseToTail;

        public static float Correlation(List<float> a, List<float> b)
        {
            double ma = 0, mb = 0;
            for (int i = 0; i < a.Count; i++) { ma += a[i]; mb += b[i]; }
            ma /= a.Count; mb /= b.Count;
            double sab = 0, saa = 0, sbb = 0;
            for (int i = 0; i < a.Count; i++) { sab += (a[i] - ma) * (b[i] - mb); saa += (a[i] - ma) * (a[i] - ma); sbb += (b[i] - mb) * (b[i] - mb); }
            return (float)(sab / Math.Sqrt(saa * sbb + 1e-12));
        }
    }
}
