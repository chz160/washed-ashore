using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using WashedAshore.Level;
using WashedAshore.World;

// Level-designer (L5 evidence): per-zone coverage after art's pass, in the design brief rev 2 definitions.
// 5 m cells on land south of the line. Open = no tree within 8 m of the cell centre; shrub-covered = a
// shrub/briar/kudzu detail instance in the cell; open-and-bare = open and not shrub-covered.
//   unity command eval "return BellsBendZoneReport.Run();"   -> _bmad-output/poc/bells-bend-zone-report.json
public static class BellsBendZoneReport
{
    public static string[] TreePrefixes = { "CommonTree", "Pine", "TwistedTree", "DeadTree" };
    public static string[] ShrubKeys = { "shrub", "briar", "kudzu", "bush", "vine", "bramble", "sumac", "blackberry", "honeysuckle" };
    public static float OpenRadius = 8f;
    public static int Seed = 0;

    public static string Run()
    {
        var cfg = BellsBendData.LoadConfig();
        var zc = BellsBendLevel.LoadZoneConfig();
        var maps = AssetDatabase.LoadAssetAtPath<BellsBendLevelMaps>(BellsBendLevelMaps.AssetPath);
        if (!maps) return "no LevelMaps; run BellsBendLevel.BuildAll first";
        maps.InvalidateCache();
        var terrains = Terrain.activeTerrains;

        // Trees into a 8 m spatial hash; shrub detail instances counted per 5 m zone cell.
        var hash = new Dictionary<long, List<Vector2>>();
        int trees = 0; var treeProtos = new HashSet<string>(); var shrubLayers = new HashSet<string>();
        var shrubCells = new HashSet<int>();
        var cellTrees = new Dictionary<int, int>();
        int W = maps.zoneW;
        int CellOf(Vector2 p) { int c = Mathf.FloorToInt((p.x - maps.originXZ.x) / maps.zoneCell), r = Mathf.FloorToInt((p.y - maps.originXZ.y) / maps.zoneCell); return c < 0 || r < 0 || c >= W || r >= maps.zoneH ? -1 : r * W + c; }
        foreach (var t in terrains)
        {
            var d = t.terrainData; var o = t.transform.position;
            foreach (var inst in d.treeInstances)
            {
                var proto = d.treePrototypes[inst.prototypeIndex].prefab;
                if (!proto || !TreePrefixes.Any(pf => proto.name.StartsWith(pf))) continue;
                treeProtos.Add(proto.name);
                var p = new Vector2(o.x + inst.position.x * d.size.x, o.z + inst.position.z * d.size.z);
                long key = Key(p);
                if (!hash.TryGetValue(key, out var l)) hash[key] = l = new List<Vector2>();
                l.Add(p); trees++;
                int ci = CellOf(p); if (ci >= 0) cellTrees[ci] = cellTrees.TryGetValue(ci, out int n) ? n + 1 : 1;
            }
            for (int layer = 0; layer < d.detailPrototypes.Length; layer++)
            {
                var dp = d.detailPrototypes[layer];
                string name = dp.prototype ? dp.prototype.name : dp.prototypeTexture ? dp.prototypeTexture.name : "";
                if (!ShrubKeys.Any(k => name.ToLowerInvariant().Contains(k))) continue;
                shrubLayers.Add(name);
                var map = d.GetDetailLayer(0, 0, d.detailWidth, d.detailHeight, layer);
                for (int y = 0; y < d.detailHeight; y++)
                    for (int x = 0; x < d.detailWidth; x++)
                    {
                        if (map[y, x] <= 0) continue;
                        var p = new Vector2(o.x + (x + 0.5f) / d.detailWidth * d.size.x, o.z + (y + 0.5f) / d.detailHeight * d.size.z);
                        int ci = CellOf(p); if (ci >= 0) shrubCells.Add(ci);
                    }
            }
        }

        const int Z = 9;
        var cells = new int[Z]; var open = new int[Z]; var bare = new int[Z]; var shrub = new int[Z]; var tz = new int[Z];
        var splatDom = new Dictionary<string, int>[Z]; for (int i = 0; i < Z; i++) splatDom[i] = new Dictionary<string, int>();
        int cliffCells = 0;
        for (int r = 0; r < maps.zoneH; r++)
            for (int c = 0; c < W; c++)
            {
                var p = maps.originXZ + new Vector2((c + 0.5f) * maps.zoneCell, (r + 0.5f) * maps.zoneCell);
                var wp = new Vector3(p.x, 0f, p.y);
                int z = maps.Zone(wp);
                if (z == 0) continue;
                if (maps.IsCliff(wp)) cliffCells++;
                int i = r * W + c;
                cells[z]++;
                tz[z] += cellTrees.TryGetValue(i, out int n) ? n : 0;
                bool isOpen = !TreeNear(hash, p), isShrub = shrubCells.Contains(i);
                if (isOpen) open[z]++;
                if (isShrub) shrub[z]++;
                if (isOpen && !isShrub) bare[z]++;
                var dom = DominantLayer(wp);
                if (dom != null) splatDom[z][dom] = splatDom[z].TryGetValue(dom, out int k) ? k + 1 : 1;
            }

        float ha = maps.zoneCell * maps.zoneCell / 10000f;
        int land = 0; for (int z = 1; z <= 7; z++) land += cells[z];
        var sb = new StringBuilder("{\n");
        sb.Append($" \"generator\": \"BellsBendZoneReport.Run\", \"builtUtc\": \"{System.DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}\",\n");
        sb.Append($" \"params\": {{ \"zoneCell\": {F(zc.zoneCell)}, \"forestSlopeDeg\": {F(zc.forestSlopeDeg)}, \"ridgeHRel\": {F(zc.ridgeHRel)}, \"bankBuffer\": {F(zc.bankBuffer)}, \"bankHRel\": {F(zc.bankHRel)}, \"roadBuffer\": {F(zc.roadBuffer)}, \"clearingSpawnRadius\": {F(zc.clearingSpawnRadius)}, \"clearingMarkerRadius\": {F(zc.clearingMarkerRadius)}, \"openRadius\": {F(OpenRadius)}, \"verticalScale\": {F(cfg.verticalScale)}, \"seed\": {Seed} }},\n");
        sb.Append($" \"treePrototypesCounted\": [{string.Join(",", treeProtos.Select(s => $"\"{s}\""))}], \"shrubDetailLayers\": [{string.Join(",", shrubLayers.Select(s => $"\"{s}\""))}],\n");
        sb.Append(" \"zones\": {\n");
        int wOpen = 0, wBare = 0, wTrees = 0;
        for (int z = 1; z < Z; z++)
        {
            float area = cells[z] * ha;
            var dom = splatDom[z].OrderByDescending(kv => kv.Value).FirstOrDefault();
            sb.Append($"  \"{BellsBendLevelMaps.ZoneNames[z]}\": {{ \"cells\": {cells[z]}, \"areaHa\": {F(area)}, \"pctLand\": {(z <= 7 ? F(cells[z] * 100f / Mathf.Max(1, land)) : "null")}, " +
                      $"\"trees\": {tz[z]}, \"treesPerHa\": {F(tz[z] / Mathf.Max(area, 1e-4f))}, \"openPct\": {Pct(open[z], cells[z])}, \"openBarePct\": {Pct(bare[z], cells[z])}, " +
                      $"\"shrubPct\": {Pct(shrub[z], cells[z])}, \"shrubPctOfOpen\": {Pct(Mathf.Min(shrub[z], open[z] - bare[z]), open[z])}, " +
                      $"\"dominantSplat\": \"{dom.Key}\", \"dominantSplatShare\": {Pct(dom.Value, cells[z])} }}{(z < Z - 1 ? "," : "")}\n");
            if (z >= 2 && z <= 6) { wOpen += open[z]; wBare += bare[z]; }
            if (z <= 7) wTrees += tz[z];
        }
        int scored = cells[2] + cells[3] + cells[4] + cells[5] + cells[6];
        sb.Append(" },\n");
        sb.Append($" \"wholeLand\": {{ \"cells\": {land}, \"areaHa\": {F(land * ha)}, \"trees\": {wTrees}, \"openPctExclRoadAndClearings\": {Pct(wOpen, scored)}, \"openBarePctExclRoadAndClearings\": {Pct(wBare, scored)}, \"cliffCells\": {cliffCells}, \"totalTreeInstancesAllTiles\": {trees} }}\n}}\n");
        var path = BellsBendData.WriteText("bells-bend-zone-report.json", sb.ToString());
        return $"zone report -> {path}\n" + sb;
    }

    static long Key(Vector2 p) => ((long)Mathf.FloorToInt(p.x / 8f) << 32) ^ (uint)Mathf.FloorToInt(p.y / 8f);

    static bool TreeNear(Dictionary<long, List<Vector2>> hash, Vector2 p)
    {
        int cx = Mathf.FloorToInt(p.x / 8f), cz = Mathf.FloorToInt(p.y / 8f);
        for (int dz = -1; dz <= 1; dz++)
            for (int dx = -1; dx <= 1; dx++)
                if (hash.TryGetValue(((long)(cx + dx) << 32) ^ (uint)(cz + dz), out var l))
                    foreach (var t in l) if ((t - p).sqrMagnitude <= OpenRadius * OpenRadius) return true;
        return false;
    }

    static string DominantLayer(Vector3 wp)
    {
        var t = WashedAshore.Gameplay.TerrainQuery.TileAt(wp);
        if (!t || t.terrainData.alphamapLayers == 0) return null;
        var d = t.terrainData;
        int x = Mathf.Clamp((int)((wp.x - t.transform.position.x) / d.size.x * d.alphamapWidth), 0, d.alphamapWidth - 1);
        int y = Mathf.Clamp((int)((wp.z - t.transform.position.z) / d.size.z * d.alphamapHeight), 0, d.alphamapHeight - 1);
        var a = d.GetAlphamaps(x, y, 1, 1);
        int best = 0; for (int l = 1; l < d.alphamapLayers; l++) if (a[0, 0, l] > a[0, 0, best]) best = l;
        var layer = d.terrainLayers[best];
        return (layer ? layer.name : "layer" + best) + (a[0, 0, best] >= 0.6f ? "" : "(<0.6)");
    }

    static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);
    static string Pct(int a, int b) => b == 0 ? "null" : F(a * 100f / b);
}
