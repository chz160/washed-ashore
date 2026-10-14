using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using WashedAshore.Gameplay;
using WashedAshore.Level;
using WashedAshore.World;

// Level-designer: the lookout tower site (lookout tower spec P1/P2). The site comes from the LookoutTower
// landmark's "site" block (tools/terrain/landmarks.json -> map_vectors.json): projected point + offset = tower centre.
//   LevelPad   levels the ground under the four footings (<= 1 m, inside the base square + 3 m). Part of BuildAll.
//   WriteSite  hands the centre, clearing radius and access-track centreline to art through LevelMaps.
//   Place      puts the tower prefab on the pad, idempotently.
//   Build      the narrow entry: the same steps on the built scene without rebuilding all 20 tiles, the barrier or the water.
//     unity command eval --detach "return BellsBendTowerSite.Build();"   (poll Logs/tower-site-build.txt)
//   then art's BellsBendGround.Build() repaints (trees leave the clearing, the track is painted), then Place() and Report().
// Evidence goes to TestResults/tower-level/.
public static class BellsBendTowerSite
{
    public const string LandmarkId = "LookoutTower";
    public const string TrackName = "Lookout Tower Track";
    public const string RootName = "LookoutTower";
    public const string PrefabPath = "Assets/World/Lookout/LookoutTower.prefab"; // = TowerBuilder.PrefabPath (t-tools); switch to that constant once Assets/Editor/Lookout exists
    public const byte TrackRoadId = BellsBendLevelMaps.RoadTowerTrack;

    public const float BaseSquare = 6.1024f;   // 20'0 1/4" back to back of the legs (86'3" MC-39, tower-height-decision.md)
    public const float PadFlat = 1f;           // level this far outside the base square
    public const float PadBlend = 1.9f;        // then back to the natural ground; ends inside the base square + 3 m
    public const float MaxPadDelta = 1f;       // spec P1
    public const float PadTopAboveGround = 0.15f; // tower origin (top of the concrete pads, which run 0.60 m down) above the levelled ground
    public const string SpawnName = "TowerBaseSpawn", StairFootName = "LANE_F01_Foot"; // FBX lane empty at the foot of flight 1 (tower-td-note 6, condition 8)
    public const float SpawnBack = 3f;            // t-engineer: the test spawn stands this far back from the foot of the first flight

    public class Site
    {
        public Vector2 projected, centre;
        public float yaw, padY;
        public Vector2[] footings;   // SW, SE, NE, NW at yaw 0
        public Vector3 Origin => new Vector3(centre.x, padY + PadTopAboveGround, centre.y);
        /// <summary>Tower-local XZ (x right, y forward) of a world XZ point.</summary>
        public Vector2 Local(Vector2 world)
        {
            float a = yaw * Mathf.Deg2Rad, c = Mathf.Cos(a), s = Mathf.Sin(a); var d = world - centre;
            return new Vector2(d.x * c - d.y * s, d.x * s + d.y * c);
        }
        public Vector2 World(Vector2 local)
        {
            float a = yaw * Mathf.Deg2Rad, c = Mathf.Cos(a), s = Mathf.Sin(a);
            return centre + new Vector2(local.x * c + local.y * s, -local.x * s + local.y * c);
        }
    }

    public static string EvidenceDir
    {
        get { var d = Path.Combine(BellsBendData.ProjectRoot, "TestResults", "tower-level"); Directory.CreateDirectory(d); return d; }
    }
    public static string LogPath => Path.Combine(BellsBendData.ProjectRoot, "Logs", "tower-site-build.txt");

    /// <summary>The site, or null when map_vectors.json has no sited LookoutTower landmark. Pad level = mean tools-RAW ground at the four footings, to 1 cm.</summary>
    public static Site Get(BellsBendData.Vectors v, float[,] raw, BellsBendData.Manifest m)
    {
        var lm = v.landmarks.FirstOrDefault(l => l.id == LandmarkId && l.hasSite);
        if (lm == null) return null;
        var s = new Site { projected = lm.xz, centre = lm.xz + lm.siteOffset, yaw = lm.siteYaw };
        float h = BaseSquare * 0.5f;
        s.footings = new[] { new Vector2(-h, -h), new Vector2(h, -h), new Vector2(h, h), new Vector2(-h, h) }.Select(s.World).ToArray();
        s.padY = Mathf.Round(s.footings.Average(f => BellsBendTiles.Sample(raw, m, f.x, f.y)) * 100f) / 100f;
        return s;
    }

    public static Site Get()
    {
        var cfg = BellsBendData.LoadConfig(); var m = BellsBendData.LoadManifest(cfg);
        return Get(BellsBendData.LoadVectors(cfg), BellsBendData.LoadRawGrid(m), m);
    }

    /// <summary>Grid index range (inclusive) that the pad can touch: the base square + 3 m.</summary>
    public static void PadRange(Site s, BellsBendData.Manifest m, int nx, int nz, out int c0, out int r0, out int c1, out int r1)
    {
        float reach = (BaseSquare * 0.5f + 3f) * 1.4143f; // covers any yaw
        c0 = Mathf.Max(0, Mathf.FloorToInt((s.centre.x - reach - m.gridOrigin.x) / m.sampleSpacing));
        c1 = Mathf.Min(nx - 1, Mathf.CeilToInt((s.centre.x + reach - m.gridOrigin.x) / m.sampleSpacing));
        r0 = Mathf.Max(0, Mathf.FloorToInt((s.centre.y - reach - m.gridOrigin.y) / m.sampleSpacing));
        r1 = Mathf.Min(nz - 1, Mathf.CeilToInt((s.centre.y + reach - m.gridOrigin.y) / m.sampleSpacing));
    }

    /// <summary>Levels the footing pad into <paramref name="fin"/>: flat at padY to base + 1 m, blended out by base + 2.9 m.</summary>
    public static string LevelPad(float[,] fin, float[,] raw, BellsBendData.Manifest m, BellsBendData.Vectors v, out Site site)
    {
        site = Get(v, raw, m);
        if (site == null) return "tower pad: no sited LookoutTower landmark in map_vectors.json (skipped)";
        float flat = BaseSquare * 0.5f + PadFlat, maxCut = 0f, maxFill = 0f; int changed = 0;
        PadRange(site, m, fin.GetLength(1), fin.GetLength(0), out int c0, out int r0, out int c1, out int r1);
        for (int r = r0; r <= r1; r++)
            for (int c = c0; c <= c1; c++)
            {
                var l = site.Local(new Vector2(m.gridOrigin.x + c * m.sampleSpacing, m.gridOrigin.y + r * m.sampleSpacing));
                float d = Mathf.Max(Mathf.Abs(l.x), Mathf.Abs(l.y));
                float w = d <= flat ? 1f : 1f - Smooth((d - flat) / PadBlend);
                if (w <= 0f) continue;
                float before = fin[r, c], after = Mathf.Lerp(before, site.padY, w);
                if (Mathf.Abs(after - before) > MaxPadDelta)
                    throw new System.InvalidOperationException($"tower pad: sample ({c},{r}) would move {after - before:F2} m, over the {MaxPadDelta} m limit (spec P1); move the site offset");
                fin[r, c] = after; changed++;
                maxCut = Mathf.Max(maxCut, before - after); maxFill = Mathf.Max(maxFill, after - before);
            }
        return $"tower pad: centre=({site.centre.x:F2},{site.centre.y:F2}) {Vector2.Distance(site.centre, site.projected):F2}m from projected ({site.projected.x:F2},{site.projected.y:F2}) " +
               $"yaw={site.yaw:F0} padY={site.padY:F2} samples={changed} maxCut={maxCut:F3} maxFill={maxFill:F3} (limit {MaxPadDelta})";
    }

    /// <summary>Adds the access-track road spec (id 6, paint only) to the ZoneConfig asset if it isn't there.</summary>
    public static string EnsureRoadSpec(ZoneConfig zc)
    {
        if (zc.roads.Any(r => r.id == TrackRoadId)) return "tower track spec: present";
        zc.roads = zc.roads.Append(new ZoneConfig.RoadSpec
        {
            osmNames = new[] { TrackName }, id = TrackRoadId, core = 3f, verge = 1f, falloff = 0f, gravel = true, paintOnly = true, paintWeight = 0.4f,
        }).ToArray();
        EditorUtility.SetDirty(zc);
        AssetDatabase.SaveAssets();
        return $"tower track spec: added id {TrackRoadId} '{TrackName}' (paint only) to {ZoneConfig.AssetPath}";
    }

    /// <summary>Centre, clearing radius and track centreline for art (BellsBendMaps reads them from LevelMaps).</summary>
    public static string WriteSite(BellsBendData.Vectors v, ZoneConfig zc)
    {
        var maps = AssetDatabase.LoadAssetAtPath<BellsBendLevelMaps>(BellsBendLevelMaps.AssetPath);
        var lm = v.landmarks.FirstOrDefault(l => l.id == LandmarkId && l.hasSite);
        if (!maps || lm == null) return $"tower site in LevelMaps: maps={(bool)maps} landmark={lm != null} (skipped)";
        var track = BellsBendRoads.Chains(v, zc).Where(c => c.spec.id == TrackRoadId).OrderByDescending(c => c.pts.Count).FirstOrDefault();
        maps.towerCentre = lm.xz + lm.siteOffset;
        maps.towerClearRadius = zc.clearingTowerRadius;
        maps.towerBaseRadius = BaseSquare * 0.70711f;
        maps.towerTrack = track != null ? track.pts.ToArray() : new Vector2[0];
        EditorUtility.SetDirty(maps);
        AssetDatabase.SaveAssets();
        return $"tower site in LevelMaps: centre=({maps.towerCentre.x:F2},{maps.towerCentre.y:F2}) clearRadius={maps.towerClearRadius} trackPoints={maps.towerTrack.Length}";
    }

    [MenuItem("Washed Ashore/Level/Build lookout tower site")]
    static void BuildMenu() => Debug.Log(Build());

    /// <summary>
    /// Narrow entry on the built scene: pad heights into the tile(s) under the site, the LM_LookoutTower marker, zones
    /// (the tower clearing) and the road map (the track). Refuses to write if the level build no longer reproduces the
    /// built terrain outside the pad. Re-runnable; a second run changes nothing.
    /// </summary>
    public static string Build()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var log = new StringBuilder($"BellsBendTowerSite.Build {System.DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}\n");
        void Step(string s) { log.AppendLine($"[{sw.Elapsed.TotalSeconds:F1}s] {s}"); File.WriteAllText(LogPath, log.ToString()); }
        Directory.CreateDirectory(Path.GetDirectoryName(LogPath));
        try
        {
            if (EditorSceneManager.GetActiveScene().path != BellsBendLevel.ScenePath) EditorSceneManager.OpenScene(BellsBendLevel.ScenePath);
            var cfg = BellsBendData.LoadConfig(); var zc = BellsBendLevel.LoadZoneConfig();
            var m = BellsBendData.LoadManifest(cfg); var v = BellsBendData.LoadVectors(cfg);
            Step(BellsBendWater.Guard(cfg, m)); // W3 lake mask and north line, before any write; a trip is a finding, not something to rebuild around
            Step(EnsureRoadSpec(zc));
            var raw = BellsBendData.LoadRawGrid(m); var fin = (float[,])raw.Clone();
            if (Get(v, raw, m) == null) throw new FileNotFoundException("map_vectors.json has no sited LookoutTower landmark: run tools/terrain/build_terrain.py");
            if (!v.roads.Any(r => r.name == TrackName)) throw new FileNotFoundException("map_vectors.json has no '" + TrackName + "': run tools/terrain/tower_track.py, then build_terrain.py");

            var rg = new LevelGrid(m.gridOrigin, new Vector2(m.tilesX, m.tilesZ) * m.tileSize, 2f);
            var bluffs = v.landmarks.Where(l => l.id.EndsWith("Bluff")).Select(l => l.xz).ToArray();
            var inPoly = rg.Rasterize(v.polygon);
            var shore = rg.DistanceTo(v.innerBank, 127f);
            for (int i = 0; i < shore.Length; i++) if (!inPoly[i]) shore[i] = -shore[i];
            var roads = BellsBendRoads.Build(fin, m, v, cfg, zc, rg, bluffs, shore);
            Step("roads: " + roads.report);

            var built = BellsBendTiles.ReadBuilt(m);
            Step(LevelPad(fin, raw, m, v, out var site));
            int nz = fin.GetLength(0), nx = fin.GetLength(1);
            PadRange(site, m, nx, nz, out int c0, out int r0, out int c1, out int r1);
            Step(HeightDiff(built, fin, m, site, c0, r0, c1, r1, out int outside));
            if (outside > 0) throw new System.InvalidOperationException($"{outside} samples outside the pad differ from the built tiles: the level build no longer reproduces the scene. Nothing written.");
            Step("tiles written: " + BellsBendTiles.WriteRegion(m, fin, c0, r0, c1, r1));

            Step(Marker(site));
            var spawnGo = GameObject.Find(BellsBendZones.SpawnName);
            if (!spawnGo) throw new System.InvalidOperationException("no " + BellsBendZones.SpawnName + " in the scene: run BellsBendLevel.BuildAll()");
            Step(BellsBendZones.Classify(raw, BellsBendData.LoadDemGrid(m), fin, m, v, cfg, zc, roads, rg, shore, spawnGo.transform.position, out var zones, out var zg));
            Step(MapDiff(zones, zg, roads, rg, shore, site));
            Step(BellsBendZones.WriteMaps(zg, zones, rg, roads, shore));
            Step(WriteSite(v, zc));

            var scene = EditorSceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Step("scene saved; next: BellsBendGround.Build() (art), then BellsBendTowerSite.Place() and Report()");
            Step("done");
        }
        catch (System.Exception e) { Step("FAILED: " + e); }
        return log.ToString();
    }

    /// <summary>Built tiles vs the new grid: writes terrain-diff.json; 'outside' counts changed samples outside the pad range.</summary>
    static string HeightDiff(float[,] built, float[,] fin, BellsBendData.Manifest m, Site site, int c0, int r0, int c1, int r1, out int outside)
    {
        const float Tol = 0.01f; // tile heights are 16-bit: one step is about 3 mm on this terrain height
        int nz = fin.GetLength(0), nx = fin.GetLength(1), inside = 0; outside = 0;
        float max = 0f, maxOutside = 0f, maxCheb = 0f; int bc0 = int.MaxValue, br0 = int.MaxValue, bc1 = -1, br1 = -1;
        for (int r = 0; r < nz; r++)
            for (int c = 0; c < nx; c++)
            {
                float d = Mathf.Abs(fin[r, c] - built[r, c]);
                if (d <= Tol) continue;
                if (r < r0 || r > r1 || c < c0 || c > c1) { outside++; maxOutside = Mathf.Max(maxOutside, d); continue; }
                inside++; max = Mathf.Max(max, d);
                bc0 = Mathf.Min(bc0, c); bc1 = Mathf.Max(bc1, c); br0 = Mathf.Min(br0, r); br1 = Mathf.Max(br1, r);
                var l = site.Local(new Vector2(m.gridOrigin.x + c * m.sampleSpacing, m.gridOrigin.y + r * m.sampleSpacing));
                maxCheb = Mathf.Max(maxCheb, Mathf.Max(Mathf.Abs(l.x), Mathf.Abs(l.y)));
            }
        string box = inside == 0 ? "null" : $"[{m.gridOrigin.x + bc0 * m.sampleSpacing},{m.gridOrigin.y + br0 * m.sampleSpacing},{m.gridOrigin.x + bc1 * m.sampleSpacing},{m.gridOrigin.y + br1 * m.sampleSpacing}]";
        string json = "{\n" +
            $" \"toleranceMeters\": {Tol},\n \"changedSamples\": {inside},\n \"maxChangeMeters\": {max:F4},\n \"changedBoxWorldXZ\": {box},\n" +
            $" \"maxDistanceFromCentreAlongAxes\": {maxCheb:F2},\n \"allowedDistance\": {BaseSquare * 0.5f + 3f:F2},\n" +
            $" \"changedOutsidePad\": {outside},\n \"maxChangeOutsidePad\": {maxOutside:F4},\n \"padY\": {site.padY:F2},\n \"limitMeters\": {MaxPadDelta}\n" + "}\n";
        File.WriteAllText(Path.Combine(EvidenceDir, "terrain-diff.json"), json);
        return $"terrain diff vs built tiles: changed={inside} max={max:F3}m maxAxisDist={maxCheb:F2}m (allowed {BaseSquare * 0.5f + 3f:F2}) outsidePad={outside} -> TestResults/tower-level/terrain-diff.json";
    }

    /// <summary>New zone and road grids vs the LevelMaps on disk: writes maps-diff.json before WriteMaps replaces them.</summary>
    static string MapDiff(byte[] zones, LevelGrid zg, BellsBendRoads.Result roads, LevelGrid rg, float[] shore, Site site)
    {
        var maps = AssetDatabase.LoadAssetAtPath<BellsBendLevelMaps>(BellsBendLevelMaps.AssetPath);
        if (!maps || !maps.zoneTex || !maps.roadTex) return "maps diff: no LevelMaps on disk (first build)";
        var oz = maps.zoneTex.GetRawTextureData(); var or = maps.roadTex.GetRawTextureData();
        if (oz.Length != zones.Length || or.Length != rg.w * rg.h * 3) return "maps diff: grid size changed";
        int zoneChanged = 0, zoneNotToClearing = 0, roadChanged = 0, existingRoadChanged = 0, newIdOther = 0, shoreChanged = 0;
        float zoneMaxDist = 0f; var pairs = new Dictionary<string, int>();
        for (int i = 0; i < zones.Length; i++)
        {
            if (oz[i] == zones[i]) continue;
            zoneChanged++;
            string cliff = ((oz[i] ^ zones[i]) & 0x80) != 0 ? " (cliff bit)" : "";
            string k = $"{BellsBendLevelMaps.ZoneNames[oz[i] & 0x7F]}->{BellsBendLevelMaps.ZoneNames[zones[i] & 0x7F]}{cliff}";
            pairs[k] = pairs.TryGetValue(k, out int n) ? n + 1 : 1;
            if ((zones[i] & 0x7F) != BellsBendLevelMaps.ZoneClearing) zoneNotToClearing++;
            zoneMaxDist = Mathf.Max(zoneMaxDist, Vector2.Distance(zg.Center(i % zg.w, i / zg.w), site.centre));
        }
        for (int i = 0; i < rg.w * rg.h; i++)
        {
            byte b = (byte)Mathf.Clamp(Mathf.RoundToInt(128f + shore[i]), 0, 255);
            if (or[i * 3 + 2] != b) shoreChanged++;
            if (or[i * 3] == roads.id[i] && or[i * 3 + 1] == roads.weight[i]) continue;
            roadChanged++;
            if (or[i * 3] != 0 && or[i * 3] != TrackRoadId) existingRoadChanged++;
            if (roads.id[i] != TrackRoadId && roads.id[i] != 0) newIdOther++;
        }
        int trackCells = roads.id.Count(b => b == TrackRoadId);
        var sb = new StringBuilder("{\n");
        sb.Append($" \"zoneCellsChanged\": {zoneChanged},\n \"zoneChangesNotToClearing\": {zoneNotToClearing},\n \"zoneChangeMaxDistFromCentre\": {zoneMaxDist:F1},\n");
        sb.Append(" \"zoneChanges\": {" + string.Join(", ", pairs.OrderBy(p => p.Key).Select(p => $"\"{p.Key}\": {p.Value}")) + "},\n");
        sb.Append($" \"roadCellsChanged\": {roadChanged},\n \"existingRoadCellsChanged\": {existingRoadChanged},\n \"changedToOtherRoadId\": {newIdOther},\n");
        sb.Append($" \"trackCells\": {trackCells},\n \"shoreDistanceCellsChanged\": {shoreChanged}\n" + "}\n");
        File.WriteAllText(Path.Combine(EvidenceDir, "maps-diff.json"), sb.ToString());
        return $"maps diff: zones changed={zoneChanged} (not to clearing {zoneNotToClearing}, max {zoneMaxDist:F1}m from centre) roads changed={roadChanged} " +
               $"(existing road cells {existingRoadChanged}, other ids {newIdOther}, track cells {trackCells}) shoreDistance changed={shoreChanged} -> TestResults/tower-level/maps-diff.json";
    }

    /// <summary>LM_LookoutTower under the Landmarks root, at the projected point (added in place; the other markers keep their objects).</summary>
    static string Marker(Site site)
    {
        var root = GameObject.Find(BellsBendZones.LandmarksRoot);
        if (!root) return "marker: no Landmarks root (run BellsBendLevel.BuildAll())";
        var t = root.transform.Find("LM_" + LandmarkId);
        bool added = !t;
        if (added) { t = new GameObject("LM_" + LandmarkId).transform; t.SetParent(root.transform, false); }
        TerrainQuery.TryGroundHeight(new Vector3(site.projected.x, 0f, site.projected.y), out float y);
        t.position = new Vector3(site.projected.x, y, site.projected.y);
        return $"marker: LM_{LandmarkId} {(added ? "added" : "moved")} at {t.position:F2}";
    }

    [MenuItem("Washed Ashore/Level/Place lookout tower")]
    static void PlaceMenu() => Debug.Log(Place());

    /// <summary>Instantiates the tower prefab on the pad (or moves the one already there) and saves the scene.</summary>
    public static string Place()
    {
        var site = Get();
        if (site == null) return "tower: no sited LookoutTower landmark in map_vectors.json (not placed)";
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (!prefab) return $"tower: prefab missing at {PrefabPath} (not placed)";
        var scene = EditorSceneManager.GetActiveScene();
        var roots = scene.GetRootGameObjects().Where(g => g.name == RootName).ToList();
        var go = roots.FirstOrDefault(g => PrefabUtility.GetCorrespondingObjectFromSource(g) == prefab);
        foreach (var stale in roots.Where(g => g != go)) Object.DestroyImmediate(stale);
        bool created = !go;
        if (created) { go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene); go.name = RootName; }
        go.transform.SetPositionAndRotation(site.Origin, Quaternion.Euler(0f, site.yaw, 0f));
        go.transform.localScale = Vector3.one;
        string spawn = PlaceSpawn(go, scene);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        return $"tower: {(created ? "instantiated" : "already placed, pose reset")} {PrefabPath} at {go.transform.position:F2} yaw {site.yaw:F0}; {spawn}";
    }

    /// <summary>TowerBaseSpawn: on the ground, SpawnBack metres out from the stair foot (the model's LANE_F01_Foot empty), facing the foot.</summary>
    static string PlaceSpawn(GameObject tower, UnityEngine.SceneManagement.Scene scene)
    {
        var foot = tower.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == StairFootName);
        if (!foot) return $"spawn: no '{StairFootName}' in the prefab ({SpawnName} not placed)";
        // The flight climbs from its foot across the tower's centre line, so "outside the foot" is along centre -> foot.
        var away = foot.position - tower.transform.position; away.y = 0f;
        away = away.sqrMagnitude > 0.01f ? away.normalized : -tower.transform.forward;
        // Straight back first; if a leg, pad or anything solid is in the way of the player's capsule (0.4 m radius, 1.8 m tall)
        // there or on the walk to the foot, swing round the foot in 15 degree steps until both are clear.
        Physics.SyncTransforms();
        int solid = WorldLayers.SightMask; // terrain hits are skipped below
        Vector3 p = Vector3.zero, dir = away; float turn = 0f; bool clear = false;
        foreach (float a in new[] { 0f, 15f, -15f, 30f, -30f, 45f, -45f, 60f, -60f, 75f, -75f, 90f, -90f })
        {
            dir = Quaternion.Euler(0f, a, 0f) * away;
            p = foot.position + dir * SpawnBack; p.y = TerrainQuery.Height(p);
            Vector3 lo = p + Vector3.up * 0.55f, hi = p + Vector3.up * 1.4f; // lifted 0.15 m so the ground itself isn't a hit
            bool hit = Physics.OverlapCapsule(lo, hi, 0.4f, solid, QueryTriggerInteraction.Ignore).Any(c => !(c is TerrainCollider));
            if (!hit)
                hit = Physics.CapsuleCastAll(lo, hi, 0.4f, -dir, SpawnBack - 0.5f, solid, QueryTriggerInteraction.Ignore).Any(h => !(h.collider is TerrainCollider));
            if (!hit) { turn = a; clear = true; break; }
        }
        if (!clear) return $"spawn: no clear point {SpawnBack} m from {StairFootName} ({SpawnName} not placed)";
        var go = scene.GetRootGameObjects().FirstOrDefault(g => g.name == SpawnName) ?? new GameObject(SpawnName);
        go.transform.SetPositionAndRotation(p, Quaternion.LookRotation(-dir, Vector3.up));
        return $"spawn: {SpawnName} at {p:F2} facing {StairFootName}, {SpawnBack} m back, swung {turn:F0} deg from the centre-foot line to clear the legs";
    }

    /// <summary>P1 numbers on the live scene: centre vs projected point, and the ground under each footing (centre and pad corners).</summary>
    public static string Report(float padHalf = 0.6f)
    {
        var site = Get();
        if (site == null) return "no sited LookoutTower landmark";
        var go = EditorSceneManager.GetActiveScene().GetRootGameObjects().FirstOrDefault(g => g.name == RootName);
        var sb = new StringBuilder("{\n");
        sb.Append($" \"projectedXZ\": [{site.projected.x:F2}, {site.projected.y:F2}],\n \"siteCentreXZ\": [{site.centre.x:F2}, {site.centre.y:F2}],\n \"padY\": {site.padY:F2},\n");
        if (go)
        {
            var p = go.transform.position;
            sb.Append($" \"towerRoot\": [{p.x:F3}, {p.y:F3}, {p.z:F3}],\n \"towerYaw\": {go.transform.eulerAngles.y:F2},\n");
            sb.Append($" \"centreToProjectedMeters\": {Vector2.Distance(new Vector2(p.x, p.z), site.projected):F3},\n \"limitMeters\": 10,\n");
        }
        else sb.Append(" \"towerRoot\": null,\n");
        float worst = 0f; var names = new[] { "SW", "SE", "NE", "NW" };
        sb.Append(" \"footings\": [\n");
        for (int i = 0; i < 4; i++)
        {
            var f = site.footings[i];
            var pts = new[] { Vector2.zero, new Vector2(-padHalf, -padHalf), new Vector2(padHalf, -padHalf), new Vector2(padHalf, padHalf), new Vector2(-padHalf, padHalf) };
            var hs = pts.Select(o => { var w = site.World(site.Local(f) + o); return TerrainQuery.Height(new Vector3(w.x, 0f, w.y)); }).ToArray();
            foreach (var h in hs) worst = Mathf.Max(worst, Mathf.Abs(h - site.padY));
            sb.Append($"  {{\"name\": \"{names[i]}\", \"x\": {f.x:F3}, \"z\": {f.y:F3}, \"groundCentre\": {hs[0]:F3}, \"groundCorners\": [{string.Join(", ", hs.Skip(1).Select(h => h.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)))}]}}{(i < 3 ? "," : "")}\n");
        }
        sb.Append($" ],\n \"padHalfSize\": {padHalf},\n \"maxGroundMinusPadY\": {worst:F3},\n \"gapLimit\": 0.1\n" + "}\n");
        File.WriteAllText(Path.Combine(EvidenceDir, "p1-site.json"), sb.ToString());
        return sb.ToString();
    }

    static float Smooth(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }
}
