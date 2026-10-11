using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;
using WashedAshore.Level;
using WashedAshore.World;

// Level-designer: shoreline structure anchors (fish F3: bluff faces, ferry landing, slipway, Robertson Island, hollow mouths).
//   unity command eval "return BellsBendStructureAnchorsBuilder.Build();"
// Reads only Data/terrain/build (bank_stations.json, map_vectors.json, tile and DEM RAWs) and MapConfig; writes
// Assets/World/BellsBend/StructureAnchors.asset. Same inputs give the same asset. Research section:
// _bmad-output/research/domain-fish-2026-10-07/sections/level-structure-anchors.md; rulings
// fish-robertson-island-anchor (face + two chute mouths, dry chute holds nothing) and fish-depth-bands.
public static class BellsBendStructureAnchorsBuilder
{
    const float Reach = 20f;           // shelf + ramp: the bed reaches W - 10 at 20 m out (tools/terrain bed_profile)
    const float BluffMinRise = 12f;    // pre-bank height above W for a bluff station (with BluffMinSlope)
    const float BluffMinSlope = 40f;
    const float BluffMinLength = 25f;
    const float BluffNameRadius = 150f;
    const float MouthProminence = 8f;  // hollow: inland floor at least this far below the ridges either side
    const float MouthWindow = 250f;
    const int MouthHalf = 4;           // stations either side of a mouth / landing centre (about 40 m of bank)
    const int PointHalf = 5;           // ferry landing and slipway: about 50 m of bank
    // Robertson Island, landcover-landmarks-r1-1.md (S1). Not in landmarks.json: the OSM polygon makes it bank land.
    const double IslandLat = 36.1726, IslandLon = -86.8995;
    const int IslandBox = 500, IslandSearch = 700, ChuteRay = 120;

    class Station { public Vector2 p; public float preBankMaxY, maxSlope; public bool lowBank; public string excluded; }

    [MenuItem("Washed Ashore/Level/Build Structure Anchors")]
    static void BuildMenu() => Debug.Log(Build());

    public static string Build() => Build(BellsBendData.LoadConfig());

    /// <summary>
    /// Batchmode entry (unity run -- -executeMethod BellsBendStructureAnchorsBuilder.BuildBatch): builds twice to prove
    /// determinism, spot-checks TryNearest, writes TestResults/structure-anchors.txt and exits (0 = all checks pass).
    /// </summary>
    public static void BuildBatch()
    {
        var log = new StringBuilder();
        bool ok;
        try { ok = Verify(log); }
        catch (System.Exception e) { log.AppendLine("EXCEPTION " + e); ok = false; }
        log.AppendLine(ok ? "RESULT PASS" : "RESULT FAIL");
        var dir = Path.Combine(BellsBendData.ProjectRoot, "TestResults");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "structure-anchors.txt"), log.ToString(), new UTF8Encoding(false));
        if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
    }

    static bool Verify(StringBuilder log)
    {
        bool ok = true;
        void Check(bool c, string what) { log.AppendLine((c ? "PASS " : "FAIL ") + what); ok &= c; }
        log.AppendLine(Build());
        var asset = AssetDatabase.LoadAssetAtPath<BellsBendStructureAnchors>(BellsBendStructureAnchors.AssetPath);
        string first = JsonUtility.ToJson(asset);
        Build();
        string second = JsonUtility.ToJson(AssetDatabase.LoadAssetAtPath<BellsBendStructureAnchors>(BellsBendStructureAnchors.AssetPath));
        Check(first == second, $"deterministic: two builds serialize identically ({first.Length} chars)");
        var an = asset.anchors;
        Check(an.Length == 17, $"17 anchors (got {an.Length})");
        foreach (var (k, want) in new[] { (StructureKind.Bluff, 7), (StructureKind.FerryLanding, 1), (StructureKind.Slipway, 1),
                     (StructureKind.HollowMouth, 5), (StructureKind.ChuteMouth, 2), (StructureKind.IslandFace, 1) })
            Check(an.Count(a => a.kind == k) == want, $"{k} count {an.Count(a => a.kind == k)} == {want}");
        Check(an.All(a => a.points.Length >= 2 && a.points.Length == a.normals.Length), "points/normals present and paired");
        Check(an.All(a => a.normals.All(n => Mathf.Abs(n.magnitude - 1f) < 1e-3f)), "normals are unit length");
        Check(an.All(a => a.reach == Reach && a.length > 0f), "reach 20 m, positive length");
        Check(an.All(a => a.points.All(p => a.bounds.Contains(p)) && a.points.All(p => asset.bounds.Contains(p))), "bounds contain every point");
        // Spot checks along each anchor's middle normal: 10 m out is in that anchor (or a tie-winning overlap), inland and 30 m out are not.
        foreach (var a in an)
        {
            int i = a.points.Length / 2;
            var p = a.points[i];
            var n = a.normals[i];
            bool inOut = asset.TryNearest(p + n * 10f, out int hit, out float d);
            Check(inOut && d <= 10.5f, $"'{a.name}' 10 m out -> {(inOut ? an[hit].name : "none")} d={d:F1}");
            Check(!asset.TryNearest(p - n * 10f, out _, out _), $"'{a.name}' 10 m inland -> none");
            Check(!asset.TryNearest(p + n * 30f, out _, out _) || a.kind == StructureKind.HollowMouth || a.kind == StructureKind.ChuteMouth,
                $"'{a.name}' 30 m out -> none (mouths may sit in a neighbour's reach)");
        }
        return ok;
    }

    public static string Build(MapConfig cfg)
    {
        var m = BellsBendData.LoadManifest(cfg);
        var v = BellsBendData.LoadVectors(cfg);
        var st = LoadStations(Path.Combine(m.dir, "bank_stations.json"));
        var fin = BellsBendData.LoadRawGrid(m);
        var dem = BellsBendData.LoadDemGrid(m);
        float w = m.waterLevelY;
        var g = new Grid { y = fin, origin = m.gridOrigin };
        var S = st.Select(s => s.p).ToArray();
        var nrm = new Vector2[S.Length];
        for (int i = 0; i < S.Length; i++) nrm[i] = NormalOut(S, i, g, w);

        var anchors = new List<BellsBendStructureAnchors.Anchor>();
        Anchor Make(StructureKind k, string name, int a, int b)
        {
            a = Mathf.Clamp(a, 0, S.Length - 1); b = Mathf.Clamp(b, 0, S.Length - 1);
            var an = new BellsBendStructureAnchors.Anchor
            {
                kind = k, name = name, reach = Reach, stationFrom = a, stationTo = b,
                points = S.Skip(a).Take(b - a + 1).ToArray(), normals = nrm.Skip(a).Take(b - a + 1).ToArray(),
            };
            an.length = Len(S, a, b);
            an.bounds = Bounds(an.points, Reach);
            return new Anchor { a = an };
        }

        // Bluff faces: runs of tall + steep (or tools-excluded bluff) stations, bridging one-station gaps.
        bool Seam(int i) => st[i].excluded == "excluded: north-line seam";
        bool Tall(int i) => st[i].excluded == "excluded: bluff" || (st[i].preBankMaxY - w >= BluffMinRise && st[i].maxSlope >= BluffMinSlope);
        var bluffLm = v.landmarks.Where(l => l.id.EndsWith("Bluff")).ToList();
        var bluffs = new List<(int a, int b)>();
        for (int i = 0; i < st.Count;)
        {
            if (!Tall(i) || Seam(i)) { i++; continue; }
            int j = i;
            while (j + 1 < st.Count && (Tall(j + 1) || (j + 2 < st.Count && Tall(j + 2)))) j++;
            if (Len(S, i, j) >= BluffMinLength) bluffs.Add((i, j));
            i = j + 1;
        }
        var runs = new List<Anchor>();
        int unnamed = 0;
        foreach (var (a, b) in bluffs)
        {
            var mid = S[(a + b) / 2];
            var near = bluffLm.OrderBy(l => Vector2.Distance(mid, l.xz)).FirstOrDefault();
            string name = near != null && Vector2.Distance(mid, near.xz) < BluffNameRadius ? near.name + " face" : $"Unnamed bluff face {++unnamed}";
            runs.Add(Make(StructureKind.Bluff, name, a, b));
        }

        // Ferry landing and slipway: the landmark's nearest bank station +- PointHalf.
        var points = new List<Anchor>();
        foreach (var (id, kind) in new[] { ("CleecesFerryLanding", StructureKind.FerryLanding), ("BoatSlipway", StructureKind.Slipway) })
        {
            var l = v.landmarks.First(x => x.id == id);
            int k = Nearest(S, l.xz);
            points.Add(Make(kind, l.name, k - PointHalf, k + PointHalf));
        }

        // Hollow mouths: the mean land height 30-120 m inland is a local minimum (+-8 stations) with MouthProminence of
        // higher ground on both sides within MouthWindow. Mouths inside a bluff run are hanging hollows and fold into it.
        var inl = new float[S.Length];
        for (int k = 0; k < S.Length; k++)
            inl[k] = (g.H(S[k] - nrm[k] * 30f) + g.H(S[k] - nrm[k] * 60f) + g.H(S[k] - nrm[k] * 90f) + g.H(S[k] - nrm[k] * 120f)) / 4f;
        int win = Mathf.FloorToInt(MouthWindow / (Len(S, 0, S.Length - 1) / (S.Length - 1)));
        var mouths = new List<int>();
        for (int k = 0; k < S.Length; k++)
        {
            if (Seam(k)) continue;
            int lo = Mathf.Max(k - win, 0), hi = Mathf.Min(k + win, S.Length - 1);
            bool localMin = true;
            for (int q = Mathf.Max(k - 8, 0); q <= Mathf.Min(k + 8, S.Length - 1); q++) if (inl[q] < inl[k]) localMin = false;
            if (!localMin) continue;
            float left = inl[k], right = inl[k];
            for (int q = lo; q < k; q++) left = Mathf.Max(left, inl[q]);
            for (int q = k + 1; q <= hi; q++) right = Mathf.Max(right, inl[q]);
            if (Mathf.Min(left, right) - inl[k] < MouthProminence) continue;
            if (bluffs.Any(r => k + MouthHalf >= r.a && k - MouthHalf <= r.b)) continue;
            if (mouths.Any(o => Mathf.Abs(o - k) <= 2 * MouthHalf)) continue;
            mouths.Add(k);
        }
        foreach (int k in mouths) points.Add(Make(StructureKind.HollowMouth, $"Hollow mouth at station {k}", k - MouthHalf, k + MouthHalf));

        // Robertson Island: the old back channel (real DEM river surface, game land) as one connected component; the bank
        // stations whose inland ray crosses it are the island's river face, and the run's ends are the chute mouths.
        var (fa, fb) = IslandFace(v, m, dem, g, S, nrm, w);
        points.Add(Make(StructureKind.ChuteMouth, "Robertson Island south chute mouth", fa - MouthHalf, fa + MouthHalf));
        points.Add(Make(StructureKind.ChuteMouth, "Robertson Island north chute mouth", fb - MouthHalf, fb + MouthHalf));
        runs.Add(Make(StructureKind.IslandFace, "Robertson Island river face", fa, fb));

        var asset = AssetDatabase.LoadAssetAtPath<BellsBendStructureAnchors>(BellsBendStructureAnchors.AssetPath);
        bool created = !asset;
        if (created) asset = ScriptableObject.CreateInstance<BellsBendStructureAnchors>();
        // Order: point-like anchors (by first station), then runs (by first station), so ties favour the specific anchor.
        asset.anchors = points.OrderBy(x => x.a.stationFrom).Concat(runs.OrderBy(x => x.a.stationFrom)).Select(x => x.a).ToArray();
        asset.bounds = asset.anchors.Select(x => x.bounds).Aggregate((r, q) => Rect.MinMaxRect(Mathf.Min(r.xMin, q.xMin), Mathf.Min(r.yMin, q.yMin), Mathf.Max(r.xMax, q.xMax), Mathf.Max(r.yMax, q.yMax)));
        asset.sourceSha16 = Sha16(Path.Combine(m.dir, "bank_stations.json"), Path.Combine(m.dir, "map_vectors.json"));
        if (created) AssetDatabase.CreateAsset(asset, BellsBendStructureAnchors.AssetPath);
        EditorUtility.SetDirty(asset);
        AssetDatabase.SaveAssets();

        var sb = new StringBuilder($"StructureAnchors {asset.anchors.Length} anchors src={asset.sourceSha16} -> {BellsBendStructureAnchors.AssetPath}\n");
        foreach (var a in asset.anchors)
            sb.AppendLine($"{a.kind} '{a.name}' st {a.stationFrom}-{a.stationTo} len {a.length:F1} m from ({a.points[0].x:F1},{a.points[0].y:F1}) to ({a.points[a.points.Length - 1].x:F1},{a.points[a.points.Length - 1].y:F1})");
        return sb.ToString();
    }

    class Anchor { public BellsBendStructureAnchors.Anchor a; }

    class Grid
    {
        public float[,] y;
        public Vector2 origin;
        // Nearest 1 m sample, as tools/terrain and the census scripts read it; NaN off the grid.
        public float H(Vector2 p)
        {
            int c = Mathf.RoundToInt(p.x - origin.x), r = Mathf.RoundToInt(p.y - origin.y);
            return r >= 0 && c >= 0 && r < y.GetLength(0) && c < y.GetLength(1) ? y[r, c] : float.NaN;
        }
    }

    static (int a, int b) IslandFace(BellsBendData.Vectors v, BellsBendData.Manifest m, float[,] dem, Grid g, Vector2[] S, Vector2[] nrm, float w)
    {
        var p = v.ToGame(IslandLat, IslandLon);
        int n = 2 * IslandBox + 1;
        int r0 = Mathf.RoundToInt(p.y - m.gridOrigin.y) - IslandBox, c0 = Mathf.RoundToInt(p.x - m.gridOrigin.x) - IslandBox;
        // The hydro-flattened river surface: the most common DEM value (cm) over game water in the box.
        var counts = new Dictionary<int, int>();
        for (int r = 0; r < n; r++)
            for (int c = 0; c < n; c++)
                if (g.y[r0 + r, c0 + c] < w)
                {
                    int key = Mathf.RoundToInt(dem[r0 + r, c0 + c] * 100f);
                    counts[key] = counts.TryGetValue(key, out int k) ? k + 1 : 1;
                }
        float surf = counts.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key).First().Key / 100f;
        var mask = new bool[n, n];
        for (int r = 0; r < n; r++)
            for (int c = 0; c < n; c++)
                mask[r, c] = dem[r0 + r, c0 + c] <= surf + 0.1f && g.y[r0 + r, c0 + c] >= w;
        // Largest 4-connected component (scan order, so ties keep the first found).
        var label = new int[n, n];
        int best = 0, bestSize = 0, next = 0;
        var queue = new Queue<(int, int)>();
        for (int r = 0; r < n; r++)
            for (int c = 0; c < n; c++)
            {
                if (!mask[r, c] || label[r, c] != 0) continue;
                next++; int size = 0;
                label[r, c] = next; queue.Enqueue((r, c));
                while (queue.Count > 0)
                {
                    var (a, b) = queue.Dequeue(); size++;
                    foreach (var (dr, dc) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                    {
                        int rr = a + dr, cc = b + dc;
                        if (rr < 0 || cc < 0 || rr >= n || cc >= n || !mask[rr, cc] || label[rr, cc] != 0) continue;
                        label[rr, cc] = next; queue.Enqueue((rr, cc));
                    }
                }
                if (size > bestSize) { bestSize = size; best = next; }
            }
        bool Hits(int k)
        {
            for (int t = 4; t <= ChuteRay; t++)
            {
                var q = S[k] - nrm[k] * t;
                int r = Mathf.RoundToInt(q.y - m.gridOrigin.y) - r0, c = Mathf.RoundToInt(q.x - m.gridOrigin.x) - c0;
                if (r >= 0 && c >= 0 && r < n && c < n && label[r, c] == best) return true;
            }
            return false;
        }
        int seed = Nearest(S, p);
        if (Vector2.Distance(S[seed], p) > IslandSearch || !Hits(seed))
            throw new InvalidDataException($"Robertson Island chute not found behind station {seed} (island point {p}, component {bestSize} cells)");
        int fa = seed, fb = seed;
        while (fa > 0 && Vector2.Distance(S[fa - 1], p) < IslandSearch && Hits(fa - 1)) fa--;
        while (fb < S.Length - 1 && Vector2.Distance(S[fb + 1], p) < IslandSearch && Hits(fb + 1)) fb++;
        return (fa, fb);
    }

    static Vector2 NormalOut(Vector2[] S, int i, Grid g, float w)
    {
        Vector2 a = S[Mathf.Max(i - 2, 0)], b = S[Mathf.Min(i + 2, S.Length - 1)];
        var t = (b - a) / Mathf.Max((b - a).magnitude, 1e-6f);
        var n = new Vector2(t.y, -t.x);
        return g.H(S[i] + n * 15f) < w ? n : -n;
    }

    static Rect Bounds(Vector2[] p, float grow)
    {
        Vector2 min = p[0], max = p[0];
        foreach (var q in p) { min = Vector2.Min(min, q); max = Vector2.Max(max, q); }
        return Rect.MinMaxRect(min.x - grow, min.y - grow, max.x + grow, max.y + grow);
    }

    static int Nearest(Vector2[] S, Vector2 p)
    {
        int best = 0;
        for (int i = 1; i < S.Length; i++) if ((S[i] - p).sqrMagnitude < (S[best] - p).sqrMagnitude) best = i;
        return best;
    }

    static float Len(Vector2[] S, int a, int b)
    {
        float d = 0f;
        for (int i = a + 1; i <= b; i++) d += Vector2.Distance(S[i - 1], S[i]);
        return d;
    }

    static List<Station> LoadStations(string path)
    {
        var list = new List<Station>();
        foreach (Dictionary<string, object> s in (List<object>)BellsBendData.Json.Parse(File.ReadAllText(path)))
            list.Add(new Station
            {
                p = new Vector2(BellsBendData.F(s["x"]), BellsBendData.F(s["z"])),
                preBankMaxY = BellsBendData.F(s["preBankMaxY"]), maxSlope = BellsBendData.F(s["maxSlope"]),
                lowBank = s["lowBank"] is bool b && b, excluded = s["excluded"] as string,
            });
        return list;
    }

    static string Sha16(params string[] files)
    {
        using (var sha = SHA256.Create())
        {
            foreach (var f in files) { var bytes = File.ReadAllBytes(f); sha.TransformBlock(bytes, 0, bytes.Length, null, 0); }
            sha.TransformFinalBlock(new byte[0], 0, 0);
            return string.Concat(sha.Hash.Take(8).Select(x => x.ToString("x2")));
        }
    }
}
