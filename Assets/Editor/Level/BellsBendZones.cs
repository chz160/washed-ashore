using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using WashedAshore.Level;
using WashedAshore.World;

// Level-designer: spawn (brief d), L7 landmark markers, L5 zone classification (brief a)
// and the LevelMaps asset (zone grid + road/shore grid) that art, gameplay and qa read.
public static class BellsBendZones
{
    public const string LandmarksRoot = "Landmarks";
    public const string SpawnName = "Spawn_BellsBendPark";
    static readonly string[] NoClearing = { "BuzzardBluff", "McCordBluff", "PotatoHill", "WestBankPonds" };

    public static float SlopeAt(float[,] g, BellsBendData.Manifest m, float x, float z)
    {
        float s = m.sampleSpacing;
        float dx = (BellsBendTiles.Sample(g, m, x + s, z) - BellsBendTiles.Sample(g, m, x - s, z)) / (2 * s);
        float dz = (BellsBendTiles.Sample(g, m, x, z + s) - BellsBendTiles.Sample(g, m, x, z - s)) / (2 * s);
        return Mathf.Atan(Mathf.Sqrt(dx * dx + dz * dz)) * Mathf.Rad2Deg;
    }

    // ---------------- spawn ----------------

    public static string PlaceSpawn(float[,] g, BellsBendData.Manifest m, BellsBendData.Vectors v, MapConfig cfg, ZoneConfig zc,
        BellsBendRoads.Result roads, LevelGrid roadGrid, out Vector3 spawnPos)
    {
        Vector2 anchor = v.ToGame(zc.spawnAnchorLat, zc.spawnAnchorLon), face = v.ToGame(zc.spawnFaceLat, zc.spawnFaceLon);
        float minY = cfg.WaterLevelY + zc.spawnMinAboveWater;
        var cands = new List<Vector2>();
        for (float dz = -zc.spawnSearchRadius; dz <= zc.spawnSearchRadius; dz += 5f)
            for (float dx = -zc.spawnSearchRadius; dx <= zc.spawnSearchRadius; dx += 5f)
                if (dx * dx + dz * dz <= zc.spawnSearchRadius * zc.spawnSearchRadius) cands.Add(anchor + new Vector2(dx, dz));
        cands.Sort((a, b) => (a - anchor).sqrMagnitude.CompareTo((b - anchor).sqrMagnitude));
        Vector2 pick = anchor; bool found = false; float range = 0f, slope = 0f;
        foreach (var p in cands)
        {
            if (p.y > cfg.northLineZ || !BellsBendData.InPolygon(p, v.polygon)) continue;
            int ri = roadGrid.Index(p);
            if (ri >= 0 && roads.weight[ri] > 0) continue;
            float lo = float.MaxValue, hi = float.MinValue, sMax = 0f; bool ok = true;
            for (float oz = -zc.spawnFlatRadius; oz <= zc.spawnFlatRadius && ok; oz += 2f)
                for (float ox = -zc.spawnFlatRadius; ox <= zc.spawnFlatRadius && ok; ox += 2f)
                {
                    if (ox * ox + oz * oz > zc.spawnFlatRadius * zc.spawnFlatRadius) continue;
                    float y = BellsBendTiles.Sample(g, m, p.x + ox, p.y + oz);
                    lo = Mathf.Min(lo, y); hi = Mathf.Max(hi, y);
                    sMax = Mathf.Max(sMax, SlopeAt(g, m, p.x + ox, p.y + oz));
                    ok = lo >= minY && hi - lo <= zc.spawnMaxRange && sMax <= zc.spawnMaxSlope;
                }
            if (!ok) continue;
            pick = p; found = true; range = hi - lo; slope = sMax; break;
        }
        float yaw = Mathf.Atan2(face.x - pick.x, face.y - pick.y) * Mathf.Rad2Deg;
        if (yaw < 0) yaw += 360f;
        spawnPos = new Vector3(pick.x, BellsBendTiles.Sample(g, m, pick.x, pick.y), pick.y);
        // Move existing spawn objects (never destroy+recreate: other components hold serialized references).
        foreach (var name in new[] { SpawnName, "PlayerSpawn" })
        {
            var go = GameObject.Find(name) ?? new GameObject(name);
            go.transform.SetPositionAndRotation(spawnPos, Quaternion.Euler(0f, yaw, 0f));
        }
        return $"spawn found={found} pos={spawnPos:F1} anchor=({anchor.x:F1},{anchor.y:F1}) offset={Vector2.Distance(anchor, pick):F1}m " +
               $"range25m={range:F2}m maxSlope25m={slope:F1}deg yaw={yaw:F1}";
    }

    public static float PlayerLift = 0.05f;

    /// <summary>BB-QA-5: puts the scene's root "Player" on the PlayerSpawn pose (the player is not spawned at runtime).</summary>
    public static string SyncPlayer()
    {
        var spawn = GameObject.Find("PlayerSpawn");
        var player = GameObject.Find("Player");
        if (!spawn || !player) return $"SyncPlayer: spawn={(bool)spawn} player={(bool)player} (nothing moved)";
        var cc = player.GetComponent<CharacterController>();
        bool was = cc && cc.enabled; if (cc) cc.enabled = false;
        var from = player.transform.position;
        player.transform.SetPositionAndRotation(spawn.transform.position + Vector3.up * PlayerLift, spawn.transform.rotation);
        if (cc) cc.enabled = was;
        // Relink PlayerController.spawnPoint to the live PlayerSpawn (an older build recreated it and left a dead reference).
        string link = "no PlayerController";
        var pc = player.GetComponent<WashedAshore.Gameplay.PlayerController>();
        if (pc)
        {
            var so = new SerializedObject(pc);
            var prop = so.FindProperty("spawnPoint");
            bool wasLive = prop.objectReferenceValue == spawn.transform;
            prop.objectReferenceValue = spawn.transform;
            so.ApplyModifiedPropertiesWithoutUndo();
            link = wasLive ? "spawnPoint already linked" : "spawnPoint relinked";
        }
        // WildlifePopulation.playerSpawn: relink only if dead (engineer-2's habitat regen owns it otherwise).
        foreach (var mb in Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include))
        {
            if (mb.GetType().Name != "WildlifePopulation") continue;
            var so = new SerializedObject(mb);
            var prop = so.FindProperty("playerSpawn");
            if (prop == null || prop.objectReferenceValue) continue;
            prop.objectReferenceValue = spawn.transform;
            so.ApplyModifiedPropertiesWithoutUndo();
            link += "; WildlifePopulation.playerSpawn relinked";
        }
        return $"SyncPlayer: Player {from:F1} -> {player.transform.position:F2} yaw={spawn.transform.eulerAngles.y:F1}; {link}";
    }

    // ---------------- markers ----------------

    public static string PlaceMarkers(float[,] g, BellsBendData.Manifest m, BellsBendData.Vectors v, MapConfig cfg)
    {
        var old = GameObject.Find(LandmarksRoot);
        if (old) Object.DestroyImmediate(old);
        var root = new GameObject(LandmarksRoot);
        var md = new StringBuilder();
        md.AppendLine("# Bells Bend landmark markers (L7)");
        md.AppendLine();
        md.AppendLine($"Generated by `BellsBendLevel.BuildAll()` (BellsBendZones.PlaceMarkers) from tools' map_vectors.json, {System.DateTime.UtcNow:yyyy-MM-dd HH:mm}Z. " +
                      $"Unity space: origin = playable-polygon centroid, +X east, +Z north, 1 unit = 1 game m (1:2). Y = built terrain height. WaterLevelY = {cfg.WaterLevelY:F2}, northLineZ = {cfg.northLineZ}.");
        md.AppendLine();
        md.AppendLine("| Marker GameObject | Landmark | X | Y | Z | Inside polygon | South of line |");
        md.AppendLine("|---|---|---|---|---|---|---|");
        foreach (var lm in v.landmarks)
        {
            float y = BellsBendTiles.Sample(g, m, lm.xz.x, lm.xz.y);
            var go = new GameObject("LM_" + lm.id);
            go.transform.SetParent(root.transform, false);
            go.transform.position = new Vector3(lm.xz.x, y, lm.xz.y);
            md.AppendLine($"| LM_{lm.id} | {lm.name} | {lm.xz.x:F1} | {y:F1} | {lm.xz.y:F1} | {(lm.inside ? "yes" : "no")} | {(lm.xz.y <= cfg.northLineZ ? "yes" : "NO")} |");
        }
        var path = BellsBendData.WriteText("bells-bend-markers.md", md.ToString());
        return $"markers={v.landmarks.Count} -> {path}";
    }

    // ---------------- zones ----------------

    public static string Classify(float[,] raw, float[,] demGrid, float[,] fin, BellsBendData.Manifest m, BellsBendData.Vectors v, MapConfig cfg, ZoneConfig zc,
        BellsBendRoads.Result roads, LevelGrid rg, float[] shore, Vector3 spawn, out byte[] zones, out LevelGrid zg)
    {
        zg = new LevelGrid(m.gridOrigin, new Vector2(m.tilesX, m.tilesZ) * m.tileSize, zc.zoneCell);
        int w = zg.w, h = zg.h, n = w * h;
        var inPoly = zg.Rasterize(v.polygon);
        float pool = cfg.poolElevationMeters, vs = cfg.verticalScale, water = cfg.WaterLevelY;
        float dataNorth = Mathf.Min(v.westCrossing.y, v.eastCrossing.y);

        // DEM-space elevation (raw RAW heights inverted through MapConfig) and its box blur.
        var dem = new float[n]; var land = new bool[n];
        for (int r = 0; r < h; r++)
            for (int c = 0; c < w; c++)
            {
                var p = zg.Center(c, r); int i = r * w + c;
                float y = BellsBendTiles.Sample(raw, m, p.x, p.y);
                dem[i] = BellsBendTiles.Sample(demGrid, m, p.x, p.y);
                land[i] = inPoly[i] || (p.y > dataNorth && y > water);
            }
        int rad = Mathf.Max(0, Mathf.RoundToInt(zc.demSlopeBlurReal * cfg.horizontalScale / zc.zoneCell) / 2);
        var blur = BoxBlur(dem, w, h, rad);
        float realCell = zc.zoneCell / cfg.horizontalScale;

        zones = new byte[n];
        var counts = new int[9];
        for (int r = 0; r < h; r++)
            for (int c = 0; c < w; c++)
            {
                int i = r * w + c;
                if (!land[i]) continue;
                var p = zg.Center(c, r);
                if (p.y > cfg.northLineZ) { zones[i] = BellsBendLevelMaps.ZoneNorthVista; continue; }
                float hRel = dem[i] - pool;
                int cl = Mathf.Clamp(c, 1, w - 2), rl = Mathf.Clamp(r, 1, h - 2);
                float gx = (blur[rl * w + cl + 1] - blur[rl * w + cl - 1]) / (2 * realCell);
                float gz = (blur[(rl + 1) * w + cl] - blur[(rl - 1) * w + cl]) / (2 * realCell);
                float demSlope = Mathf.Atan(Mathf.Sqrt(gx * gx + gz * gz)) * Mathf.Rad2Deg;
                int ri = rg.Index(p);
                float dRoad = ri >= 0 ? roads.dist[ri] : 999f, dShore = ri >= 0 ? shore[ri] : 0f;
                byte z;
                if (RoadInCell(roads, rg, zg, c, r)) z = BellsBendLevelMaps.ZoneRoad;
                else if (dRoad <= zc.roadBuffer) z = BellsBendLevelMaps.ZoneRoadBuffer;
                else if (dShore <= zc.bankBuffer && hRel < zc.bankHRel) z = BellsBendLevelMaps.ZoneBank;
                else if (hRel >= zc.ridgeHRel) z = BellsBendLevelMaps.ZoneRidge;
                else if (demSlope >= zc.forestSlopeDeg) z = BellsBendLevelMaps.ZoneHollows;
                else z = BellsBendLevelMaps.ZoneFormerFields;
                zones[i] = z;
            }

        // One 3x3 majority filter over Z1-Z5 (roads, clearings, vista and water keep their cells).
        var src = (byte[])zones.Clone();
        var votes = new int[9];
        for (int r = 1; r < h - 1; r++)
            for (int c = 1; c < w - 1; c++)
            {
                int i = r * w + c;
                if (src[i] < BellsBendLevelMaps.ZoneRoadBuffer || src[i] > BellsBendLevelMaps.ZoneFormerFields) continue;
                System.Array.Clear(votes, 0, 9);
                for (int dr = -1; dr <= 1; dr++)
                    for (int dc = -1; dc <= 1; dc++)
                    {
                        byte s = src[i + dr * w + dc];
                        if (s >= BellsBendLevelMaps.ZoneRoadBuffer && s <= BellsBendLevelMaps.ZoneFormerFields) votes[s]++;
                    }
                int bestZ = src[i];
                for (int z = 2; z <= 6; z++) if (votes[z] > votes[bestZ]) bestZ = z;
                zones[i] = (byte)bestZ;
            }

        // Z6 clearings (override everything but road): spawn 40 m, markers 20 m except terrain features.
        var clear = new List<(Vector2 p, float r)> { (new Vector2(spawn.x, spawn.z), zc.clearingSpawnRadius) };
        clear.AddRange(v.landmarks.Where(l => !NoClearing.Contains(l.id)).Select(l => (l.xz, zc.clearingMarkerRadius)));
        foreach (var (cp, cr) in clear)
        {
            zg.Range(cp - Vector2.one * cr, cp + Vector2.one * cr, out int c0, out int r0, out int c1, out int r1);
            for (int r = r0; r <= r1; r++)
                for (int c = c0; c <= c1; c++)
                {
                    int i = r * w + c;
                    if (!land[i] || zones[i] == BellsBendLevelMaps.ZoneRoad || zones[i] == BellsBendLevelMaps.ZoneNorthVista) continue;
                    if (Vector2.Distance(zg.Center(c, r), cp) <= cr) zones[i] = BellsBendLevelMaps.ZoneClearing;
                }
        }

        // Z3 cliff overlay from the built (game) slope: majority of the cell's 1 m samples > 40 deg.
        int cliffs = 0;
        for (int r = 0; r < h; r++)
            for (int c = 0; c < w; c++)
            {
                int i = r * w + c;
                if (zones[i] == 0) continue;
                var p0 = zg.Center(c, r) - Vector2.one * (zc.zoneCell * 0.5f);
                int steep = 0, tot = 0;
                for (float oz = 0.5f; oz < zc.zoneCell; oz += 1f)
                    for (float ox = 0.5f; ox < zc.zoneCell; ox += 1f, tot++)
                        if (SlopeAt(fin, m, p0.x + ox, p0.y + oz) > zc.cliffSlopeDeg) steep++;
                if (steep * 2 > tot) { zones[i] |= BellsBendLevelMaps.CliffBit; cliffs++; }
            }

        int landCells = 0;
        foreach (var z in zones) { int b = z & 0x7F; counts[b]++; if (b != 0 && b != 8) landCells++; }
        var sb = new StringBuilder($"zones grid={w}x{h}@{zc.zoneCell}m demBlurRadiusCells={rad} playableCells={landCells} cliffCells={cliffs} shares[");
        for (int z = 1; z <= 7; z++) sb.Append($"{BellsBendLevelMaps.ZoneNames[z]}={counts[z] * 100f / Mathf.Max(1, landCells):F1}% ");
        sb.Append($"] vistaCells={counts[8]}");
        return sb.ToString();
    }

    static bool RoadInCell(BellsBendRoads.Result roads, LevelGrid rg, LevelGrid zg, int c, int r)
    {
        var lo = zg.Center(c, r) - Vector2.one * (zg.cell * 0.5f);
        rg.Range(lo, lo + Vector2.one * (zg.cell - 0.01f), out int c0, out int r0, out int c1, out int r1);
        for (int rr = r0; rr <= r1; rr++)
            for (int cc = c0; cc <= c1; cc++)
                if (roads.weight[rr * rg.w + cc] >= 128) return true;
        return false;
    }

    static float[] BoxBlur(float[] a, int w, int h, int rad)
    {
        if (rad <= 0) return (float[])a.Clone();
        var tmp = new float[a.Length]; var o = new float[a.Length];
        for (int r = 0; r < h; r++)
            for (int c = 0; c < w; c++)
            {
                float s = 0; int n = 0;
                for (int k = -rad; k <= rad; k++) { int cc = c + k; if (cc < 0 || cc >= w) continue; s += a[r * w + cc]; n++; }
                tmp[r * w + c] = s / n;
            }
        for (int r = 0; r < h; r++)
            for (int c = 0; c < w; c++)
            {
                float s = 0; int n = 0;
                for (int k = -rad; k <= rad; k++) { int rr = r + k; if (rr < 0 || rr >= h) continue; s += tmp[rr * w + c]; n++; }
                o[r * w + c] = s / n;
            }
        return o;
    }

    // ---------------- LevelMaps asset ----------------

    public static string WriteMaps(LevelGrid zg, byte[] zones, LevelGrid rg, BellsBendRoads.Result roads, float[] shore)
    {
        Directory.CreateDirectory(BellsBendData.WorldRoot);
        var zonePath = BellsBendData.WorldRoot + "/LevelZones.png";
        var roadPath = BellsBendData.WorldRoot + "/LevelRoads.png";
        var zt = new Texture2D(zg.w, zg.h, TextureFormat.R8, false, true);
        zt.SetPixelData(zones, 0); zt.Apply();
        File.WriteAllBytes(Path.Combine(BellsBendData.ProjectRoot, zonePath), zt.EncodeToPNG());
        var rgb = new byte[rg.w * rg.h * 3];
        for (int i = 0; i < rg.w * rg.h; i++)
        {
            rgb[i * 3] = roads.id[i]; rgb[i * 3 + 1] = roads.weight[i];
            rgb[i * 3 + 2] = (byte)Mathf.Clamp(Mathf.RoundToInt(128f + shore[i]), 0, 255);
        }
        var rt = new Texture2D(rg.w, rg.h, TextureFormat.RGB24, false, true);
        rt.SetPixelData(rgb, 0); rt.Apply();
        File.WriteAllBytes(Path.Combine(BellsBendData.ProjectRoot, roadPath), rt.EncodeToPNG());
        Object.DestroyImmediate(zt); Object.DestroyImmediate(rt);
        Import(zonePath, TextureImporterFormat.R8, TextureImporterType.SingleChannel);
        Import(roadPath, TextureImporterFormat.RGB24, TextureImporterType.Default);

        var maps = AssetDatabase.LoadAssetAtPath<BellsBendLevelMaps>(BellsBendLevelMaps.AssetPath);
        if (!maps) { maps = ScriptableObject.CreateInstance<BellsBendLevelMaps>(); AssetDatabase.CreateAsset(maps, BellsBendLevelMaps.AssetPath); }
        maps.originXZ = zg.origin; maps.zoneCell = zg.cell; maps.roadCell = rg.cell;
        maps.zoneW = zg.w; maps.zoneH = zg.h; maps.roadW = rg.w; maps.roadH = rg.h;
        maps.zoneTex = AssetDatabase.LoadAssetAtPath<Texture2D>(zonePath);
        maps.roadTex = AssetDatabase.LoadAssetAtPath<Texture2D>(roadPath);
        maps.InvalidateCache();
        EditorUtility.SetDirty(maps);
        AssetDatabase.SaveAssets();
        // Round-trip check: the imported textures must hand back the exact bytes.
        int zoneBad = 0, roadBad = 0;
        var zr = maps.zoneTex.GetRawTextureData(); var rr = maps.roadTex.GetRawTextureData();
        for (int i = 0; i < zones.Length; i++) if (zr[i] != zones[i]) zoneBad++;
        for (int i = 0; i < rgb.Length; i++) if (rr[i] != rgb[i]) roadBad++;
        return $"maps={BellsBendLevelMaps.AssetPath} zone={zg.w}x{zg.h} road={rg.w}x{rg.h} roundTripMismatch zone={zoneBad} road={roadBad}";
    }

    static void Import(string path, TextureImporterFormat fmt, TextureImporterType type)
    {
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        var imp = (TextureImporter)AssetImporter.GetAtPath(path);
        imp.textureType = type;
        if (type == TextureImporterType.SingleChannel)
        {
            var s = new TextureImporterSettings(); imp.ReadTextureSettings(s);
            s.singleChannelComponent = TextureImporterSingleChannelComponent.Red; imp.SetTextureSettings(s);
        }
        imp.sRGBTexture = false; imp.mipmapEnabled = false; imp.isReadable = true;
        imp.filterMode = FilterMode.Point; imp.npotScale = TextureImporterNPOTScale.None;
        imp.alphaSource = TextureImporterAlphaSource.None;
        imp.maxTextureSize = 8192; imp.textureCompression = TextureImporterCompression.Uncompressed;
        var ps = imp.GetDefaultPlatformTextureSettings(); ps.format = fmt; ps.maxTextureSize = 8192; ps.textureCompression = TextureImporterCompression.Uncompressed;
        imp.SetPlatformTextureSettings(ps);
        imp.SaveAndReimport();
    }
}
