using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using WashedAshore.Level;
using WashedAshore.World;

// Level-designer (L6): levels the ground along the four named roads from the scaled OSM
// centrelines (no snapping) and rasterises the road id / paint weight / centreline distance
// on the 2 m level grid. Design brief rev 2 section (b).
public static class BellsBendRoads
{
    public class Chain { public ZoneConfig.RoadSpec spec; public string name; public List<Vector2> pts; }

    public class Result
    {
        public byte[] id, weight;   // 2 m grid
        public float[] dist;        // 2 m grid, metres to the nearest painted centreline (capped)
        public List<Chain> chains = new List<Chain>();
        public string report;
        public string cappedLog;
    }

    /// <summary>Joins same-spec OSM ways that share endpoints into continuous centrelines.</summary>
    public static List<Chain> Chains(BellsBendData.Vectors v, ZoneConfig zc)
    {
        var chains = new List<Chain>();
        foreach (var spec in zc.roads)
        {
            var parts = v.roads.Where(r => spec.osmNames.Contains(r.name)).Select(r => new List<Vector2>(r.points)).ToList();
            while (parts.Count > 0)
            {
                var cur = parts[0]; parts.RemoveAt(0);
                for (bool joined = true; joined;)
                {
                    joined = false;
                    for (int i = 0; i < parts.Count && !joined; i++)
                    {
                        var p = parts[i];
                        if (Near(cur[cur.Count - 1], p[0])) { cur.AddRange(p.Skip(1)); joined = true; }
                        else if (Near(cur[cur.Count - 1], p[p.Count - 1])) { p.Reverse(); cur.AddRange(p.Skip(1)); joined = true; }
                        else if (Near(cur[0], p[p.Count - 1])) { cur.InsertRange(0, p.Take(p.Count - 1)); joined = true; }
                        else if (Near(cur[0], p[0])) { p.Reverse(); cur.InsertRange(0, p.Take(p.Count - 1)); joined = true; }
                        if (joined) parts.RemoveAt(i);
                    }
                }
                chains.Add(new Chain { spec = spec, name = spec.osmNames[0], pts = cur });
            }
        }
        return chains;
    }

    static bool Near(Vector2 a, Vector2 b) => (a - b).sqrMagnitude < 1f;

    public static List<Vector2> Resample(List<Vector2> pts, float step)
    {
        var o = new List<Vector2> { pts[0] };
        float carry = 0f;
        for (int i = 1; i < pts.Count; i++)
        {
            Vector2 a = pts[i - 1], b = pts[i]; float len = Vector2.Distance(a, b), s = step - carry;
            for (; s <= len; s += step) o.Add(Vector2.Lerp(a, b, s / len));
            carry = len - (s - step);
        }
        if ((o[o.Count - 1] - pts[pts.Count - 1]).sqrMagnitude > 0.01f) o.Add(pts[pts.Count - 1]);
        return o;
    }

    public static Result Build(float[,] grid, BellsBendData.Manifest m, BellsBendData.Vectors v, MapConfig cfg, ZoneConfig zc,
        LevelGrid lg, Vector2[] bluffs, float[] shore)
    {
        var res = new Result { chains = Chains(v, zc) };
        float floor = cfg.WaterLevelY + cfg.landMinAboveWater;
        int nz = grid.GetLength(0), nx = grid.GetLength(1);
        float sp = m.sampleSpacing;
        var log = new StringBuilder();
        var rep = new StringBuilder();
        int cappedTotal = 0;

        foreach (var ch in res.chains)
        {
            var spec = ch.spec;
            var pts = Resample(ch.pts, 1f);
            int k = pts.Count;
            var h0 = pts.Select(p => BellsBendTiles.Sample(grid, m, p.x, p.y)).ToArray();
            int half = Mathf.RoundToInt(zc.roadSmoothWindow * 0.5f);
            var target = new float[k];
            int capped = 0, skipped = 0, capStart = -1;
            for (int i = 0; i < k; i++)
            {
                float sum = 0f; int cnt = 0;
                for (int j = Mathf.Max(0, i - half); j <= Mathf.Min(k - 1, i + half); j++) { sum += h0[j]; cnt++; }
                float hs = sum / cnt;
                bool nearBluff = bluffs.Any(b => Vector2.Distance(b, pts[i]) <= zc.roadBluffExclusion);
                int si = lg.Index(pts[i]);
                bool water = h0[i] < floor || (si >= 0 && shore[si] < zc.roadShoreExclusion && shore[si] > -127f && BellsBendData.InPolygon(pts[i], v.polygon));
                bool cap = Mathf.Abs(hs - h0[i]) > zc.roadCutFillCap;
                if (nearBluff || water) { target[i] = float.NaN; skipped++; }
                else target[i] = Mathf.Max(cap ? h0[i] : hs, floor);
                if (cap && !nearBluff && !water) { capped++; if (capStart < 0) capStart = i; }
                else if (capStart >= 0) { log.AppendLine($"| {ch.name} | {capStart}-{i - 1} m | ({pts[capStart].x:F0},{pts[capStart].y:F0})-({pts[i - 1].x:F0},{pts[i - 1].y:F0}) |"); capStart = -1; }
            }
            if (capStart >= 0) log.AppendLine($"| {ch.name} | {capStart}-{k - 1} m | ({pts[capStart].x:F0},{pts[capStart].y:F0})-end |");
            cappedTotal += capped;

            // Nearest-centreline distance and target per 1 m cell in the chain's band, then blend.
            float reach = spec.PaintHalf + spec.falloff;
            var best = new Dictionary<int, (float d, float t)>();
            for (int i = 1; i < k; i++)
            {
                Vector2 a = pts[i - 1], b = pts[i];
                int c0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(a.x, b.x) - reach - m.gridOrigin.x) / sp));
                int c1 = Mathf.Min(nx - 1, Mathf.CeilToInt((Mathf.Max(a.x, b.x) + reach - m.gridOrigin.x) / sp));
                int r0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(a.y, b.y) - reach - m.gridOrigin.y) / sp));
                int r1 = Mathf.Min(nz - 1, Mathf.CeilToInt((Mathf.Max(a.y, b.y) + reach - m.gridOrigin.y) / sp));
                Vector2 ab = b - a; float l2 = Mathf.Max(ab.sqrMagnitude, 1e-6f);
                for (int r = r0; r <= r1; r++)
                    for (int c = c0; c <= c1; c++)
                    {
                        var p = new Vector2(m.gridOrigin.x + c * sp, m.gridOrigin.y + r * sp);
                        float u = Mathf.Clamp01(Vector2.Dot(p - a, ab) / l2);
                        float d = Vector2.Distance(p, a + ab * u);
                        if (d > reach) continue;
                        int key = r * nx + c;
                        if (!best.TryGetValue(key, out var cur) || d < cur.d)
                        {
                            float ta = target[i - 1], tb = target[i];
                            float t = float.IsNaN(ta) || float.IsNaN(tb) ? float.NaN : Mathf.Lerp(ta, tb, u);
                            best[key] = (d, t);
                        }
                    }
            }
            float maxCut = 0f, maxFill = 0f; int cellCapped = 0;
            foreach (var kv in best)
            {
                if (float.IsNaN(kv.Value.t)) continue;
                int r = kv.Key / nx, c = kv.Key % nx;
                float w = kv.Value.d <= spec.PaintHalf ? 1f : 1f - Smooth((kv.Value.d - spec.PaintHalf) / spec.falloff);
                float before = grid[r, c], after = Mathf.Lerp(before, kv.Value.t, w);
                // Per-cell cut/fill cap: a flat cross-section on a side slope keeps the terrain grade past ±cap.
                if (Mathf.Abs(after - before) > zc.roadCutFillCap) { after = before + Mathf.Sign(after - before) * zc.roadCutFillCap; cellCapped++; }
                grid[r, c] = after;
                maxCut = Mathf.Max(maxCut, before - after); maxFill = Mathf.Max(maxFill, after - before);
            }
            float len = 0f; for (int i = 1; i < ch.pts.Count; i++) len += Vector2.Distance(ch.pts[i - 1], ch.pts[i]);
            rep.Append($"{ch.name}[{(spec.gravel ? "gravel" : "asphalt")} {spec.core}+{spec.verge}x2 f{spec.falloff}] len={len:F0}m cells={best.Count} " +
                       $"capped={capped}m cellsCapped={cellCapped} skipped(water/shore12m/bluff)={skipped}m maxCut={maxCut:F2} maxFill={maxFill:F2}; ");
        }

        // 2 m level grid: road id, paint weight, distance to the nearest painted centreline.
        int n = lg.w * lg.h;
        res.id = new byte[n]; res.weight = new byte[n]; res.dist = Enumerable.Repeat(999f, n).ToArray();
        float distCap = zc.roadBuffer + 5f;
        foreach (var ch in res.chains)
        {
            var spec = ch.spec;
            for (int i = 1; i < ch.pts.Count; i++)
            {
                Vector2 a = ch.pts[i - 1], b = ch.pts[i], ab = b - a; float l2 = Mathf.Max(ab.sqrMagnitude, 1e-6f);
                lg.Range(Vector2.Min(a, b) - Vector2.one * distCap, Vector2.Max(a, b) + Vector2.one * distCap, out int c0, out int r0, out int c1, out int r1);
                for (int r = r0; r <= r1; r++)
                    for (int c = c0; c <= c1; c++)
                    {
                        var p = lg.Center(c, r);
                        float d = Vector2.Distance(p, a + ab * Mathf.Clamp01(Vector2.Dot(p - a, ab) / l2));
                        int idx = r * lg.w + c;
                        if (d < res.dist[idx]) res.dist[idx] = d;
                        float half = spec.core * 0.5f;
                        float w = d <= half ? 1f : d <= spec.PaintHalf ? 1f - Smooth((d - half) / spec.verge) : 0f;
                        byte wb = (byte)Mathf.RoundToInt(w * 255f);
                        if (wb > res.weight[idx]) { res.weight[idx] = wb; res.id[idx] = spec.id; }
                    }
            }
        }
        res.report = rep.ToString() + $"cappedTotal={cappedTotal}m";
        res.cappedLog = log.ToString();
        return res;
    }

    /// <summary>L6: distance from the scaled GeoJSON centreline (every 10 m) to the painted core of the same road.</summary>
    public static string Deviation(Result r, LevelGrid lg, out float meanAll, out float maxAll)
    {
        var sb = new StringBuilder();
        float sumAll = 0f; int cntAll = 0; maxAll = 0f;
        foreach (var grp in r.chains.GroupBy(c => c.spec.id))
        {
            float sum = 0f, max = 0f; int cnt = 0, missing = 0;
            foreach (var ch in grp)
                foreach (var p in Resample(ch.pts, 10f))
                {
                    if (!lg.Contains(p)) continue;
                    float best = float.MaxValue;
                    lg.Range(p - Vector2.one * 30f, p + Vector2.one * 30f, out int c0, out int r0, out int c1, out int r1);
                    for (int rr = r0; rr <= r1; rr++)
                        for (int cc = c0; cc <= c1; cc++)
                        {
                            int idx = rr * lg.w + cc;
                            if (r.id[idx] != grp.Key || r.weight[idx] < 250) continue;
                            best = Mathf.Min(best, Vector2.Distance(lg.Center(cc, rr), p));
                        }
                    if (best == float.MaxValue) { missing++; continue; }
                    sum += best; cnt++; max = Mathf.Max(max, best);
                }
            sb.Append($"{BellsBendLevelMaps.RoadNames[grp.Key]}: samples={cnt} mean={sum / Mathf.Max(1, cnt):F2}m max={max:F2}m unpainted={missing}; ");
            sumAll += sum; cntAll += cnt; maxAll = Mathf.Max(maxAll, max);
        }
        meanAll = sumAll / Mathf.Max(1, cntAll);
        return sb.ToString();
    }

    static float Smooth(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }
}
