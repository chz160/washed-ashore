using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace WashedAshore.Perf.Editor
{
    /// <summary>
    /// Issue #9 pose candidates for river_bank and fish_water (technical-director's addenda 2 and 3 on #25), read-only and
    /// fixed before the combined pose check. Writes TestResults/9-shore-candidates.json; never saves the scene.
    ///   river_c1..3  shore-route stations Shore[1..3] (BellsBendFpsWalk.cs, the W10 route), each facing the next station.
    ///   fish_c1..3   places built to show fish, each facing the nearest water, nearest the water first: the graded-shore
    ///                start of the fish F8 feel check (FishF8Spawn.cs ShoreStart), the BoatSlipway landmark
    ///                (Data/terrain/build/map_vectors.json, east bank) and the F8 McCord bluff crest (BluffCrest). "Facing the water" = the yaw to the nearest terrain
    ///                cell below the water surface within 60 m (2 m grid).
    /// Every candidate: ground at the point (Terrain.SampleHeight), eye 1.7 m, the water surface (top of the Water-layer
    /// quad over it) and the eye's height above it.
    ///   Unity.exe -batchmode -nographics -projectPath . -executeMethod WashedAshore.Perf.Editor.ShoreCandidates.RunBatch -logFile TestResults/9-shore-candidates.log
    /// </summary>
    public static class ShoreCandidates
    {
        const float Eye = FrustumSpots.Eye, WaterSearch = 60f, WaterGrid = 2f;
        const string Out = "TestResults/9-shore-candidates.json";

        public static void RunBatch()
        {
            try { Debug.Log("[Shore] " + Run()); if (Application.isBatchMode) EditorApplication.Exit(0); }
            catch (System.Exception e) { Debug.LogError("[Shore] FAILED " + e); if (Application.isBatchMode) EditorApplication.Exit(1); }
        }

        static string F(double v) => FrustumMath.F(v);
        static string S(string v) => "\"" + FrustumMath.Esc(v) + "\"";

        public static string Run()
        {
            var scenePath = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).First();
            var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            var roots = scene.GetRootGameObjects();
            var tiles = roots.SelectMany(r => r.GetComponentsInChildren<Terrain>(true)).ToList();
            int waterLayer = LayerMask.NameToLayer("Water");
            var water = roots.SelectMany(r => r.GetComponentsInChildren<Renderer>(true)).Where(r => r.gameObject.layer == waterLayer).ToList();

            float H(float x, float z)
            {
                foreach (var t in tiles)
                {
                    var d = t.terrainData;
                    if (!d) continue;
                    var o = t.transform.position;
                    if (x >= o.x && x <= o.x + d.size.x && z >= o.z && z <= o.z + d.size.z) return t.SampleHeight(new Vector3(x, 0f, z)) + o.y;
                }
                return float.NaN;
            }
            float W(float x, float z)
            {
                var over = water.Where(r => x >= r.bounds.min.x && x <= r.bounds.max.x && z >= r.bounds.min.z && z <= r.bounds.max.z).ToList();
                return over.Count > 0 ? over.Max(r => r.bounds.max.y) : float.NaN;
            }

            var rows = new List<string>();
            var flags = new List<string>();
            void Add(string name, int order, float x, float z, float yaw, string source)
            {
                float g = H(x, z), w = W(x, z), eye = g + Eye;
                if (float.IsNaN(g)) flags.Add($"{name}: no terrain");
                else if (!float.IsNaN(w) && eye - w < 0.5f) flags.Add($"{name}: eye {F(eye - w)} m above water (< 0.5)");
                rows.Add($"{{\"name\":{S(name)},\"order\":{order},\"x\":{F(x)},\"z\":{F(z)},\"yaw\":{F(yaw)},\"ground_y\":{F(g)},\"eye_y\":{F(eye)}," +
                         $"\"water_surface_y\":{F(w)},\"eye_above_water_m\":{F(eye - w)},\"ground_above_water_m\":{F(g - w)}," +
                         $"\"fcPose\":{S($"{name}={F(x)},{F(z)},{F(yaw)},0")},\"source\":{S(source)}}}");
            }

            // river_bank: current pose (candidate 0) and Shore[1..3], each facing the next station.
            var shore = new[] { new Vector2(1077.9f, -49.6f), new Vector2(1062.1f, -28.8f), new Vector2(1043.8f, -6.9f), new Vector2(1027.2f, 13.9f), new Vector2(1010.6f, 34.5f) };
            for (int i = 1; i <= 3; i++)
                Add($"river_c{i}", i, shore[i].x, shore[i].y, FrustumMath.YawToward(shore[i].x, shore[i].y, shore[i + 1].x, shore[i + 1].y),
                    $"W10 shore-route station Shore[{i}] (BellsBendFpsWalk.cs Shore, TestResults/water-w4/support/shore-route.json), facing Shore[{i + 1}]");

            // fish_water: places made to show fish, facing the nearest water.
            float ToWater(float x, float z, out float dist)
            {
                float best = float.MaxValue, yaw = float.NaN;
                float w = W(x, z);
                for (float dx = -WaterSearch; dx <= WaterSearch; dx += WaterGrid)
                    for (float dz = -WaterSearch; dz <= WaterSearch; dz += WaterGrid)
                    {
                        float r = Mathf.Sqrt(dx * dx + dz * dz);
                        if (r > WaterSearch || r < 1f || r >= best) continue;
                        float h = H(x + dx, z + dz);
                        if (float.IsNaN(h) || float.IsNaN(w) || h >= w) continue;
                        best = r; yaw = FrustumMath.YawToward(x, z, x + dx, z + dz);
                    }
                dist = best;
                return yaw;
            }
            // Order: nearest the water first. Fish bodies draw only within subSurfaceDrawRadius 25 m of the eye
            // (Data/fish/fish-targets.json visibility), so the bluff crest, about 27 m above the water, goes last.
            var fish = new[]
            {
                ("fish_c1", 1077.9f, -49.6f, "FishF8Spawn.cs ShoreStart: the start of the graded shore walk of the fish F8 feel check (same ground point as river_bank, facing the water instead of along the bank)"),
                ("fish_c2", 1156.86f, -1418.08f, "map_vectors.json landmark BoatSlipway (east bank)"),
                ("fish_c3", 401.8f, 717.6f, "FishF8Spawn.cs BluffCrest: the McCord bluff crest of the fish F8 feel check; last because its eye is about 27 m above the water, past the 25 m fish draw radius"),
            };
            var distances = new List<string>();
            for (int i = 0; i < fish.Length; i++)
            {
                var (name, x, z, src) = fish[i];
                float yaw = ToWater(x, z, out float d);
                if (float.IsNaN(yaw)) { flags.Add($"{name}: no water within {WaterSearch} m"); yaw = 0f; }
                Add(name, i + 1, x, z, yaw, src + $"; facing the nearest water cell, {F(d)} m away");
                distances.Add($"{S(name)}:{F(d)}");
            }

            var json = $"{{\"issue\":9,\"rules\":[\"#25 note_technical-director_plan_pose_addendum2\",\"#25 note_technical-director_plan_fish_addendum\"],\"eye_m\":{F(Eye)}," +
                       $"\"nearest_water_m\":{{{string.Join(",", distances)}}},\"candidates\":[{string.Join(",", rows)}],\"flags\":[{string.Join(",", flags.Select(S))}]}}";
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(Application.dataPath), Out), json + "\n");
            return $"written {Out}; flags: {(flags.Count == 0 ? "none" : string.Join("; ", flags))}";
        }
    }
}
