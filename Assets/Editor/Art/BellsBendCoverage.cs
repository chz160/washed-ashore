using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using A = AmbientCgLayers;
using T = BellsBendArtTargets;
using V = BellsBendVegetation;

// Technical-artist L5 evidence: cover per zone on the brief's 5 m grid, graded against designer-2's
// section (a) rev 2 (via BellsBendArtTargets). Reads what is on the tiles, not what the painter meant.
// Open = no tree instance within 8 m; shrub-covered = a shrub/briar/kudzu detail instance in the cell.
// Writes _bmad-output/poc/bells-bend-coverage.md and TestResults/bells-bend-coverage.json.
// Run via: unity command eval "return BellsBendCoverage.Run();"
public static class BellsBendCoverage
{
    const string Md = "_bmad-output/poc/bells-bend-coverage.md";
    const string Json = "TestResults/bells-bend-coverage.json";

    class Stat
    {
        public long cells, open, openBare, shrub, openShrub, kudzu, roadSplat, cliffCells, cliffRock;
        public int trees;
        public int[] species = new int[5];
        public double[] layers = new double[A.Specs.Length];
        public double Ha => cells * T.CellMetres * T.CellMetres / 10000.0;
    }

    public static string Run()
    {
        var maps = BellsBendMaps.Load();
        var tiles = BellsBendGround.Tiles();
        if (tiles.Length == 0) return "error: no tiles";
        var stats = Enumerable.Range(0, T.Zones.Length + 1).Select(_ => new Stat()).ToArray(); // last = whole land
        var whole = stats[T.Zones.Length];
        var treeHash = new Dictionary<long, List<Vector2>>();
        int totalTrees = 0;
        foreach (var t in tiles)
        {
            var d = t.terrainData;
            foreach (var inst in d.treeInstances)
            {
                var w = Vector3.Scale(inst.position, d.size) + t.transform.position;
                totalTrees++;
                Add(treeHash, w);
                byte z = maps.Zone(w);
                if (z < T.Zones.Length) { stats[z].trees++; stats[z].species[Species(inst, d, w, maps)]++; }
            }
        }

        // Only layers that can actually render count as cover (an invalid prototype draws nothing).
        var protos = tiles[0].terrainData.detailPrototypes;
        var shrubLayers = V.DetailSpecs.Select((s, i) => (s, i))
            .Where(x => x.s.shrub && x.i < protos.Length && protos[x.i].Validate(out _)).Select(x => x.i).ToArray();
        var w8 = new float[A.Specs.Length];
        foreach (var t in tiles)
        {
            var d = t.terrainData;
            var o = t.transform.position;
            int ares = d.alphamapResolution, dres = d.detailResolution;
            var a = d.GetAlphamaps(0, 0, ares, ares);
            var shrubCount = new int[dres, dres];
            var kudzuCount = new int[dres, dres];
            foreach (int l in shrubLayers)
            {
                var layer = d.GetDetailLayer(0, 0, dres, dres, l);
                bool kudzu = l == V.KudzuMound || l == V.KudzuLeaf;
                for (int y = 0; y < dres; y++)
                    for (int x = 0; x < dres; x++)
                    {
                        shrubCount[y, x] += layer[y, x];
                        if (kudzu) kudzuCount[y, x] += layer[y, x];
                    }
            }
            float dc = d.size.x / dres;
            for (float cz = T.CellMetres / 2; cz < d.size.z; cz += T.CellMetres)
                for (float cx = T.CellMetres / 2; cx < d.size.x; cx += T.CellMetres)
                {
                    var p = new Vector3(o.x + cx, 0f, o.z + cz);
                    byte zone = maps.Zone(p);
                    if (zone == T.Outside || zone >= T.Zones.Length) continue;
                    var s = stats[zone];
                    int ax = Mathf.Min(ares - 1, (int)(cx / d.size.x * ares)), az = Mathf.Min(ares - 1, (int)(cz / d.size.z * ares));
                    for (int l = 0; l < w8.Length; l++) w8[l] = a[az, ax, l];
                    // Detail cells whose centres fall inside this 5 m cell.
                    bool shrub = false, kudzu = false;
                    int x0 = Mathf.Max(0, Mathf.CeilToInt((cx - T.CellMetres / 2) / dc - 0.5f)), x1 = Mathf.Min(dres - 1, Mathf.FloorToInt((cx + T.CellMetres / 2) / dc - 0.5f));
                    int y0 = Mathf.Max(0, Mathf.CeilToInt((cz - T.CellMetres / 2) / dc - 0.5f)), y1 = Mathf.Min(dres - 1, Mathf.FloorToInt((cz + T.CellMetres / 2) / dc - 0.5f));
                    for (int y = y0; y <= y1; y++)
                        for (int x = x0; x <= x1; x++) { shrub |= shrubCount[y, x] > 0; kudzu |= kudzuCount[y, x] > 0; }
                    bool open = !Near(treeHash, p, T.OpenRadius);
                    var list = zone == T.Clearing || zone == T.NorthVista ? new[] { s } : new[] { s, whole };
                    foreach (var st in list)
                    {
                        st.cells++;
                        for (int l = 0; l < w8.Length; l++) st.layers[l] += w8[l];
                        if (open) st.open++;
                        if (open && !shrub) st.openBare++;
                        if (shrub) st.shrub++;
                        if (open && shrub) st.openShrub++;
                        if (w8[A.Kudzu] >= 0.5f || kudzu) st.kudzu++;
                        if (w8[A.Asphalt] + w8[A.Gravel] >= T.RoadSplatMin) st.roadSplat++;
                        if (maps.IsCliff(p)) { st.cliffCells++; if (w8[A.Rock] >= T.CliffSplatMin) st.cliffRock++; }
                    }
                }
        }
        whole.trees = Enumerable.Range(1, T.Zones.Length - 1).Where(z => z != T.Clearing && z != T.NorthVista).Sum(z => stats[z].trees);
        return Report.Write(stats, totalTrees, tiles.Length, Md, Json);
    }

    static int Species(TreeInstance inst, TerrainData d, Vector3 w, BellsBendMaps maps)
    {
        var name = d.treePrototypes[inst.prototypeIndex].prefab.name;
        if (name.StartsWith("Pine")) return maps.Zone(w) == T.Fields ? T.FieldCedar : T.Pine;
        if (name.StartsWith("Twisted")) return T.Twisted;
        if (name.StartsWith("Dead")) return T.Dead;
        return T.Hardwood;
    }

    static long Key(int x, int z) => ((long)x << 32) ^ (uint)z;

    static void Add(Dictionary<long, List<Vector2>> h, Vector3 w)
    {
        var k = Key(Mathf.FloorToInt(w.x / T.OpenRadius), Mathf.FloorToInt(w.z / T.OpenRadius));
        if (!h.TryGetValue(k, out var l)) h[k] = l = new List<Vector2>();
        l.Add(new Vector2(w.x, w.z));
    }

    static bool Near(Dictionary<long, List<Vector2>> h, Vector3 p, float r)
    {
        int cx = Mathf.FloorToInt(p.x / T.OpenRadius), cz = Mathf.FloorToInt(p.z / T.OpenRadius);
        var q = new Vector2(p.x, p.z);
        for (int dz = -1; dz <= 1; dz++)
            for (int dx = -1; dx <= 1; dx++)
                if (h.TryGetValue(Key(cx + dx, cz + dz), out var l))
                    foreach (var v in l) if ((v - q).sqrMagnitude <= r * r) return true;
        return false;
    }

    static class Report
    {
        public static string Write(Stat[] stats, int totalTrees, int tiles, string md, string json)
        {
            var whole = stats[T.Zones.Length];
            var m = new StringBuilder();
            var j = new StringBuilder("{\"zones\":[");
            m.AppendLine("# Bells Bend coverage by zone (L5)");
            m.AppendLine();
            m.AppendLine($"Generated by `BellsBendCoverage.Run()` ({System.DateTime.UtcNow:yyyy-MM-dd HH:mm}Z) from the painted tiles ({tiles} tiles). " +
                         $"Grid {T.CellMetres} m. Open = no tree instance within {T.OpenRadius} m. Shrub-covered = an instance of a shrub detail layer " +
                         $"({string.Join(", ", V.DetailSpecs.Where(s => s.shrub).Select(s => s.key))}) in the cell. Targets: designer-2's brief (a) rev 2. " +
                         "Z6 clearings and the ZN vista are excluded from the whole-land open shares.");
            m.AppendLine();
            m.AppendLine("| Zone | Area ha (% land) | Trees/ha (target / floor-ceiling) | Open | Open-and-bare | Shrub cells | Zone check | Dominant layer | Pass |");
            m.AppendLine("|---|---|---|---|---|---|---|---|---|");
            int fails = 0;
            for (int i = 1; i <= T.Zones.Length; i++)
            {
                var s = stats[i];
                bool isWhole = i == T.Zones.Length;
                string name = isWhole ? "Whole land" : T.ZoneNames[i];
                var z = isWhole ? null : T.Zones[i];
                if (s.cells == 0) { m.AppendLine($"| {name} | 0 | – | – | – | – | – | – | n/a |"); continue; }
                double tph = s.trees / s.Ha, open = Share(s.open, s.cells), bare = Share(s.openBare, s.cells), shrub = Share(s.shrub, s.cells);
                var checks = new List<(string label, bool ok)>();
                string extra = "";
                if (isWhole)
                {
                    checks.Add(($"open ≤ {T.WholeLandMaxOpen:P0}", open <= T.WholeLandMaxOpen));
                    checks.Add(($"open-and-bare ≤ {T.WholeLandMaxOpenBare:P0}", bare <= T.WholeLandMaxOpenBare));
                }
                else
                {
                    if (z.floor > 0 || z.ceiling > 0) checks.Add(($"trees/ha {z.floor}-{z.ceiling}", tph >= z.floor && (z.ceiling <= 0 || tph <= z.ceiling)));
                    if (z.maxOpen < 1f && i != T.Clearing) checks.Add(($"open {z.minOpen:P0}-{z.maxOpen:P0}", open >= z.minOpen && open <= z.maxOpen));
                    if (z.maxOpenBare < 1f) checks.Add(($"open-and-bare ≤ {z.maxOpenBare:P0}", bare <= z.maxOpenBare));
                    if (i == T.Bank) checks.Add(("shrub ≥ 50% of cells", shrub >= z.minShrubShare));
                    if (i == T.Fields) { double os = Share(s.openShrub, s.open); extra = $"shrub in {os:P0} of open cells"; checks.Add(("shrub ≥ 60% of open cells", os >= 0.6)); }
                    if (i == T.RoadBuffer) { double k = Share(s.kudzu, s.cells); extra = $"kudzu {k:P0}"; checks.Add(("kudzu ≥ 60% of cells", k >= T.KudzuCellShare)); }
                    if (i == T.Road) { double r = Share(s.roadSplat, s.cells); extra = $"road splat ≥ 0.6 on {r:P0}"; checks.Add(("road splat ≥ 0.6 on ≥ 90%", r >= T.RoadSplatShare)); }
                    if (i == T.NorthVista) checks.Add(("trees/ha ≥ 60", tph >= 60));
                }
                bool pass = checks.All(c => c.ok);
                if (!pass) fails++;
                int dom = System.Array.IndexOf(s.layers, s.layers.Max());
                string landPct = isWhole || i == T.NorthVista ? "" : $" ({Share(s.cells, whole.cells):P0})";
                string tgt = z == null ? "" : $" ({z.treesPerHa:F0} / {z.floor:F0}-{z.ceiling:F0})";
                string failed = string.Join("; ", checks.Where(c => !c.ok).Select(c => "miss " + c.label));
                m.AppendLine($"| {name} | {s.Ha:F1}{landPct} | {tph:F0}{tgt} | {open:P0} | {bare:P0} | {shrub:P0} | {extra} | {A.Specs[dom].key} {s.layers[dom] / s.cells:P0} | {(pass ? "PASS" : "FAIL: " + failed)} |");
                j.Append($"{(i > 1 ? "," : "")}{{\"zone\":{(isWhole ? -1 : i)},\"name\":\"{name}\",\"cells\":{s.cells},\"areaHa\":{s.Ha:F2},\"trees\":{s.trees},\"treesPerHa\":{tph:F1}," +
                         $"\"openPct\":{open * 100:F2},\"openBarePct\":{bare * 100:F2},\"shrubPct\":{shrub * 100:F2},\"kudzuPct\":{Share(s.kudzu, s.cells) * 100:F2}," +
                         $"\"roadSplatPct\":{Share(s.roadSplat, s.cells) * 100:F2},\"cliffCells\":{s.cliffCells},\"cliffRockPct\":{Share(s.cliffRock, s.cliffCells) * 100:F2}," +
                         $"\"species\":[{string.Join(",", s.species)}],\"layerShares\":[{string.Join(",", s.layers.Select(l => (l / s.cells).ToString("F4")))}],\"pass\":{(pass ? "true" : "false")}}}");
            }
            double cliff = Share(whole.cliffRock, whole.cliffCells);
            bool cliffOk = whole.cliffCells == 0 || cliff >= T.CliffSplatShare;
            if (!cliffOk) fails++;
            m.AppendLine();
            m.AppendLine($"**Z3 cliff overlay:** {whole.cliffCells} cells; rock splat ≥ 0.5 on {cliff:P0} (target ≥ 80%): {(cliffOk ? "PASS" : "FAIL")}.");
            m.AppendLine($"**Total tree instances (R2):** {totalTrees}. Species columns in the JSON: {string.Join(", ", T.SpeciesNames)}.");
            m.AppendLine($"**Layers:** {string.Join(", ", A.Specs.Select((s, i) => $"{i} {s.key} ({s.id})"))}; layer 0 is the habitat grass. Sources: `Assets/ThirdParty/ambientCG/SOURCE.md` (CC0).");
            m.AppendLine($"**Plants:** Stylized Nature MegaKit (Quaternius, CC0). Tinted detail copies under `{V.PlantRoot}`.");
            m.AppendLine();
            m.AppendLine($"**Result:** {(fails == 0 ? "PASS, every zone meets the brief" : $"{fails} check group(s) FAIL")}.");
            j.Append($"],\"cliffCells\":{whole.cliffCells},\"cliffRockPct\":{cliff * 100:F2},\"totalTrees\":{totalTrees},\"seed\":{T.Seed},\"failures\":{fails}}}");
            var root = Path.GetDirectoryName(Application.dataPath);
            File.WriteAllText(Path.Combine(root, md), m.ToString());
            Directory.CreateDirectory(Path.Combine(root, "TestResults"));
            File.WriteAllText(Path.Combine(root, json), j.ToString());
            return $"failures={fails} totalTrees={totalTrees} -> {md}";
        }

        static double Share(long a, long b) => b == 0 ? 0 : a / (double)b;
    }
}
