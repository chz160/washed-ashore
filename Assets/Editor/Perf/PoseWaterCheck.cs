using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace WashedAshore.Perf.Editor
{
    /// <summary>
    /// Issue #9 pose check (task #26), read-only: for every FrustumSpots pose, the terrain under it, the water surface there
    /// and the eye's height above both, plus the terrain round it, so a fallback height (a pose off the tiles, or on the
    /// heightmap floor) shows. Writes TestResults/9-pose-check.json. Never saves the scene.
    ///   Unity.exe -batchmode -nographics -projectPath . -executeMethod WashedAshore.Perf.Editor.PoseWaterCheck.RunBatch [-fcCabHeight m] -logFile TestResults/9-pose-check.log
    /// Water surface: the top of every Water-layer renderer whose bounds cover the pose in x and z (the 1024 m water quads).
    /// Ring: terrain heights at 8 compass points 5, 20 and 50 m out. Floor: the pose's normalised heightmap value
    /// (0 is the bottom of the tile's height range).
    /// </summary>
    public static class PoseWaterCheck
    {
        const float Margin = 0.5f;
        const string Out = "TestResults/9-pose-check.json";

        public static void RunBatch()
        {
            try { Debug.Log("[PoseCheck] " + Run()); if (Application.isBatchMode) EditorApplication.Exit(0); }
            catch (System.Exception e) { Debug.LogError("[PoseCheck] FAILED " + e); if (Application.isBatchMode) EditorApplication.Exit(1); }
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
            var args = System.Environment.GetCommandLineArgs();
            float cab = FarViewProbe.CabHeight;
            int ci = System.Array.IndexOf(args, "-fcCabHeight");
            if (ci >= 0 && ci + 1 < args.Length && float.TryParse(args[ci + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float c) && c > 0f) cab = c;

            var rows = new List<string>();
            var flags = new List<string>();
            foreach (var p in FrustumSpots.Defaults(cab))
            {
                var tile = Tile(tiles, p.x, p.z);
                float ground = tile ? tile.SampleHeight(new Vector3(p.x, 0f, p.z)) + tile.transform.position.y : float.NaN;
                float norm = float.NaN, tileMin = float.NaN, tileMax = float.NaN;
                if (tile)
                {
                    var d = tile.terrainData;
                    var o = tile.transform.position;
                    norm = d.GetInterpolatedHeight((p.x - o.x) / d.size.x, (p.z - o.z) / d.size.z) / d.size.y;
                    tileMin = o.y; tileMax = o.y + d.size.y;
                }
                var over = water.Where(r => p.x >= r.bounds.min.x && p.x <= r.bounds.max.x && p.z >= r.bounds.min.z && p.z <= r.bounds.max.z).ToList();
                float waterY = over.Count > 0 ? over.Max(r => r.bounds.max.y) : float.NaN;
                float eye = ground + p.heightAboveGround;
                var ring = new List<string>();
                float ringMin = float.PositiveInfinity, ringMax = float.NegativeInfinity;
                int ringBelowWater = 0, ringSamples = 0;
                foreach (float rad in new[] { 5f, 20f, 50f })
                    for (int k = 0; k < 8; k++)
                    {
                        float a = k * 45f * Mathf.Deg2Rad;
                        float x = p.x + Mathf.Sin(a) * rad, z = p.z + Mathf.Cos(a) * rad;
                        var t = Tile(tiles, x, z);
                        if (!t) continue;
                        float h = t.SampleHeight(new Vector3(x, 0f, z)) + t.transform.position.y;
                        ringMin = Mathf.Min(ringMin, h); ringMax = Mathf.Max(ringMax, h);
                        ringSamples++;
                        if (!float.IsNaN(waterY) && h < waterY) ringBelowWater++;
                        if (rad == 20f) ring.Add($"{{\"deg\":{k * 45},\"y\":{F(h)}}}");
                    }
                float eyeOverWater = float.IsNaN(waterY) ? float.NaN : eye - waterY;
                float groundOverWater = float.IsNaN(waterY) ? float.NaN : ground - waterY;
                bool underOrNear = !float.IsNaN(eyeOverWater) && eyeOverWater < Margin;
                bool groundUnder = !float.IsNaN(groundOverWater) && groundOverWater < 0f;
                bool offTile = !tile;
                bool onFloor = !float.IsNaN(norm) && norm <= 0.001f;
                if (underOrNear) flags.Add($"{p.name}: eye {F(eyeOverWater)} m above water (< {Margin})");
                if (groundUnder) flags.Add($"{p.name}: ground {F(groundOverWater)} m below the water surface");
                if (offTile) flags.Add($"{p.name}: no terrain tile under the pose");
                if (onFloor) flags.Add($"{p.name}: ground on the heightmap floor (normalised 0)");
                rows.Add($"{{\"spot\":{S(p.name)},\"x\":{F(p.x)},\"z\":{F(p.z)},\"tile\":{S(tile ? tile.name : "none")},\"tile_y_range\":[{F(tileMin)},{F(tileMax)}]," +
                         $"\"heightmap_normalised\":{F(norm)},\"ground_y\":{F(ground)},\"height_above_ground\":{F(p.heightAboveGround)},\"eye_y\":{F(eye)}," +
                         $"\"water_renderers_over_pose\":[{string.Join(",", over.Select(r => S(r.name)))}],\"water_surface_y\":{F(waterY)}," +
                         $"\"ground_above_water_m\":{F(groundOverWater)},\"eye_above_water_m\":{F(eyeOverWater)}," +
                         $"\"ring_5_20_50m\":{{\"samples\":{ringSamples},\"min_y\":{F(ringMin)},\"max_y\":{F(ringMax)},\"below_water\":{ringBelowWater},\"at_20m\":[{string.Join(",", ring)}]}}," +
                         $"\"under_or_within_margin\":{(underOrNear ? "true" : "false")},\"ground_on_heightmap_floor\":{(onFloor ? "true" : "false")}}}");
            }
            var json = $"{{\"issue\":9,\"task\":26,\"scene\":{S(scenePath)},\"margin_m\":{F(Margin)},\"cab_height\":{F(cab)},\"poses\":[{string.Join(",", rows)}],\"flags\":[{string.Join(",", flags.Select(S))}]}}";
            var root = Path.GetDirectoryName(Application.dataPath);
            File.WriteAllText(Path.Combine(root, Out), json + "\n");
            return $"written {Out}; flags: {(flags.Count == 0 ? "none" : string.Join("; ", flags))}";
        }

        static Terrain Tile(List<Terrain> tiles, float x, float z)
        {
            foreach (var t in tiles)
            {
                var d = t.terrainData;
                if (!d) continue;
                var o = t.transform.position;
                if (x >= o.x && x <= o.x + d.size.x && z >= o.z && z <= o.z + d.size.z) return t;
            }
            return null;
        }
    }
}
