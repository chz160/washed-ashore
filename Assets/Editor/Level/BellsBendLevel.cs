using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using WashedAshore.Level;
using WashedAshore.World;

// Level-designer: one rerunnable entry for the Bells Bend level (replaces WorldBuilder's 512 m terrain).
//   unity command eval "return BellsBendLevel.BuildAll();"
// Order: RAW tiles (tools/terrain) -> road levelling -> Terrain tiles -> spawn -> markers -> zones/LevelMaps
//        -> barrier -> WorldBounds backstop -> save World.unity -> L1/L4/L6 evidence + look shots.
// Art (BellsBendGround) runs after this; then BellsBendLevel.ZoneReport() and LookShots() for L5/look checks.
public static class BellsBendLevel
{
    public const string ScenePath = "Assets/Scenes/World.unity";
    public static string LogPath => Path.Combine(BellsBendData.ProjectRoot, "Logs", "bells-bend-level-build.txt");

    [MenuItem("Washed Ashore/Level/Build Bells Bend (all)")]
    static void BuildAllMenu() => UnityEngine.Debug.Log(BuildAll());

    public static string BuildAll() => BuildAll(BellsBendData.LoadConfig());

    public static string BuildAll(MapConfig cfg, bool evidence = true)
    {
        var sw = Stopwatch.StartNew();
        var log = new StringBuilder($"BellsBendLevel.BuildAll {System.DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ} vs={cfg.verticalScale} northLineZ={cfg.northLineZ} gateOpen={cfg.gateOpen}\n");
        void Step(string s) { log.AppendLine($"[{sw.Elapsed.TotalSeconds:F1}s] {s}"); File.WriteAllText(LogPath, log.ToString()); }
        Directory.CreateDirectory(Path.GetDirectoryName(LogPath));
        try
        {
            if (EditorSceneManager.GetActiveScene().path != ScenePath) EditorSceneManager.OpenScene(ScenePath);
            var zc = LoadZoneConfig();
            var m = BellsBendData.LoadManifest(cfg);
            var v = BellsBendData.LoadVectors(cfg);
            var raw = BellsBendData.LoadRawGrid(m);
            var fin = (float[,])raw.Clone();
            Step($"tile RAW sha256_16={RawSha(m)} (sorted concat of tile RAWs)");
            Step($"loaded manifest {m.tilesX}x{m.tilesZ} res={m.res} grid={raw.GetLength(1)}x{raw.GetLength(0)} roads={v.roads.Count} landmarks={v.landmarks.Count}");

            var extent = new Vector2(m.tilesX, m.tilesZ) * m.tileSize;
            var rg = new LevelGrid(m.gridOrigin, extent, 2f);
            var bluffs = v.landmarks.Where(l => l.id.EndsWith("Bluff")).Select(l => l.xz).ToArray();
            var inPoly = rg.Rasterize(v.polygon);
            var shore = rg.DistanceTo(v.innerBank, 127f);
            for (int i = 0; i < shore.Length; i++) if (!inPoly[i]) shore[i] = -shore[i];
            var roads = BellsBendRoads.Build(fin, m, v, cfg, zc, rg, bluffs, shore);
            Step("roads: " + roads.report);
            if (roads.cappedLog.Length > 0)
                BellsBendData.WriteText("bells-bend-road-capped-segments.md", "# Road levelling: cut/fill-capped segments (terrain-following grade kept)\n\n| Road | Metres along chain | From-to (x,z) |\n|---|---|---|\n" + roads.cappedLog);

            Step(L2Touch(raw, fin, m, cfg, roads, rg));
            Step("tiles: " + BellsBendTiles.Write(m, fin));
            Step($"seam max step={BellsBendTiles.MaxSeamError():F4}m");

            Step(BellsBendZones.PlaceSpawn(fin, m, v, cfg, zc, roads, rg, out var spawn));
            Step(BellsBendZones.SyncPlayer());
            Step(BellsBendZones.PlaceMarkers(fin, m, v, cfg));
            Step(BellsBendZones.Classify(raw, BellsBendData.LoadDemGrid(m), fin, m, v, cfg, zc, roads, rg, shore, spawn, out var zones, out var zg));
            Step(BellsBendZones.WriteMaps(zg, zones, rg, roads, shore));

            if (!cfg) cfg = BellsBendData.LoadConfig(); // an import above can reload the MapConfig asset
            Step(BarrierBuilder.Build(cfg));
            Step(WorldBoundsBuilder.Build(cfg, false));
            var scene = EditorSceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Step($"scene saved {scene.path} sha256_16={FileSha(Path.Combine(BellsBendData.ProjectRoot, scene.path))}");

            if (evidence)
            {
                Step(BellsBendReports.L4(fin, m, v, cfg, out _));
                Step(BellsBendRoads.Deviation(roads, rg, out float mean, out float max) + $" L6 overall mean={mean:F2} max={max:F2} pass(<=10)={max <= 10f}");
                Step("L6 overlay -> " + BellsBendReports.RoadOverlay(roads, rg, v));
                var l1 = BellsBendReports.L1Deviation(v, cfg, out _, out _, out _);
                Step(l1);
                Step("L1 overlay -> " + BellsBendReports.L1Overlay(v, cfg, l1));
                Step(Vista(m, cfg));
                Step(BellsBendReports.LookShots(cfg, v, zc));
            }
            Step("done");
        }
        catch (System.Exception e)
        {
            Step("FAILED: " + e);
        }
        return log.ToString();
    }

    /// <summary>Road distance and level-edit delta at every L2 sample point (they must stay untouched).</summary>
    static string L2Touch(float[,] raw, float[,] fin, BellsBendData.Manifest m, MapConfig cfg, BellsBendRoads.Result roads, LevelGrid rg)
    {
        var path = Path.Combine(BellsBendData.ProjectRoot, Path.GetDirectoryName(cfg.manifestPath), "l2_samples.json");
        if (!File.Exists(path)) return "L2 touch: no l2_samples.json";
        var j = (System.Collections.Generic.Dictionary<string, object>)BellsBendData.Json.Parse(File.ReadAllText(path));
        var sb = new StringBuilder("L2 touch (roadDist capped 25 m; delta = level edit at the point):");
        foreach (System.Collections.Generic.Dictionary<string, object> p in (System.Collections.Generic.List<object>)j["points"])
        {
            float x = BellsBendData.F(p["x"]), z = BellsBendData.F(p["z"]);
            int i = rg.Index(new Vector2(x, z));
            float d = i >= 0 ? Mathf.Min(25f, roads.dist[i]) : -1f;
            float delta = BellsBendTiles.Sample(fin, m, x, z) - BellsBendTiles.Sample(raw, m, x, z);
            sb.Append($" {p["label"]}[{p["grade"]}] road={d:F1}m delta={delta:F3};");
        }
        return sb.ToString();
    }

    static string FileSha(params string[] files)
    {
        using (var sha = System.Security.Cryptography.SHA256.Create())
        {
            foreach (var f in files) { var b = File.ReadAllBytes(f); sha.TransformBlock(b, 0, b.Length, null, 0); }
            sha.TransformFinalBlock(new byte[0], 0, 0);
            return System.BitConverter.ToString(sha.Hash).Replace("-", "").ToLowerInvariant().Substring(0, 16);
        }
    }

    static string RawSha(BellsBendData.Manifest m) =>
        FileSha(m.tiles.Select(t => t.file).OrderBy(f => f, System.StringComparer.Ordinal).Select(f => Path.Combine(m.dir, f)).ToArray());

    /// <summary>
    /// Zone-only classification sweep (designer-2): reclassifies with each (forestSlopeDeg, ridgeHRel) pair
    /// on the current heights, roads and spawn. Writes no assets and leaves ZoneConfig unchanged.
    /// </summary>
    public static string ZoneSweep(float[] forestSlopes, float[] ridgeHRels)
    {
        var cfg = BellsBendData.LoadConfig(); var zc = LoadZoneConfig();
        var m = BellsBendData.LoadManifest(cfg); var v = BellsBendData.LoadVectors(cfg);
        var raw = BellsBendData.LoadRawGrid(m); var dem = BellsBendData.LoadDemGrid(m); var fin = (float[,])raw.Clone();
        var rg = new LevelGrid(m.gridOrigin, new Vector2(m.tilesX, m.tilesZ) * m.tileSize, 2f);
        var inPoly = rg.Rasterize(v.polygon);
        var shore = rg.DistanceTo(v.innerBank, 127f);
        for (int i = 0; i < shore.Length; i++) if (!inPoly[i]) shore[i] = -shore[i];
        var bluffs = v.landmarks.Where(l => l.id.EndsWith("Bluff")).Select(l => l.xz).ToArray();
        var roads = BellsBendRoads.Build(fin, m, v, cfg, zc, rg, bluffs, shore);
        var spawnGo = GameObject.Find(BellsBendZones.SpawnName);
        var spawn = spawnGo ? spawnGo.transform.position : Vector3.zero;
        float fs0 = zc.forestSlopeDeg, rh0 = zc.ridgeHRel;
        var sb = new StringBuilder("forestSlopeDeg,ridgeHRel,shares\n");
        try
        {
            foreach (var f in forestSlopes)
                foreach (var r in ridgeHRels)
                {
                    zc.forestSlopeDeg = f; zc.ridgeHRel = r;
                    sb.AppendLine($"{f},{r}," + BellsBendZones.Classify(raw, dem, fin, m, v, cfg, zc, roads, rg, shore, spawn, out _, out _));
                }
        }
        finally { zc.forestSlopeDeg = fs0; zc.ridgeHRel = rh0; }
        BellsBendData.WriteText("bells-bend-zone-sweep.csv", sb.ToString());
        return sb.ToString();
    }

    public static ZoneConfig LoadZoneConfig()
    {
        var zc = AssetDatabase.LoadAssetAtPath<ZoneConfig>(ZoneConfig.AssetPath);
        if (zc) return zc;
        Directory.CreateDirectory(BellsBendData.WorldRoot);
        zc = ScriptableObject.CreateInstance<ZoneConfig>();
        AssetDatabase.CreateAsset(zc, ZoneConfig.AssetPath);
        AssetDatabase.SaveAssets();
        return zc;
    }

    /// <summary>B1: built terrain north of the line (tools keeps real DEM there; we report depth and relief).</summary>
    static string Vista(BellsBendData.Manifest m, MapConfig cfg)
    {
        float zMax = m.gridOrigin.y + m.tilesZ * m.tileSize;
        float lo = float.MaxValue, hi = float.MinValue;
        for (float z = cfg.northLineZ + 50f; z < zMax; z += 25f)
            for (float x = m.gridOrigin.x; x < m.gridOrigin.x + m.tilesX * m.tileSize; x += 25f)
                if (WashedAshore.Gameplay.TerrainQuery.TryGroundHeight(new Vector3(x, 0, z), out float h)) { lo = Mathf.Min(lo, h); hi = Mathf.Max(hi, h); }
        return $"B1 vista depth north of line={zMax - cfg.northLineZ:F0}m (min {cfg.northVistaMinMeters}) heightRange=[{lo:F1},{hi:F1}]";
    }

    [MenuItem("Washed Ashore/Level/Bells Bend look shots")]
    static void LookShotsMenu() => UnityEngine.Debug.Log(LookShots());

    /// <summary>Retake the two look-check shots (run after art's pass).</summary>
    public static string LookShots(string suffix = "") =>
        BellsBendReports.LookShots(BellsBendData.LoadConfig(), BellsBendData.LoadVectors(BellsBendData.LoadConfig()), LoadZoneConfig(), suffix);

    /// <summary>Barrier only, then save (B6 helper for qa: shift MapConfig, run this plus WorldBoundsBuilder).</summary>
    public static string RebuildBarrier()
    {
        var cfg = BellsBendData.LoadConfig();
        var r = BarrierBuilder.Build(cfg);
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
        return r;
    }
}
