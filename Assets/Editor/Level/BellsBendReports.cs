using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using WashedAshore.Gameplay;
using WashedAshore.Level;
using WashedAshore.World;

// Level-designer evidence for the Bells Bend build: L1 overlay + shoreline deviation (measured on
// the live Terrain tiles), L4 slope histogram + bluff check, L6 road overlay, and look-check shots.
public static class BellsBendReports
{
    // ---------------- L1 ----------------

    /// <summary>Mean distance from the scaled inner-bank polyline (every 10 m) to the built WaterLevelY contour.</summary>
    public static string L1Deviation(BellsBendData.Vectors v, MapConfig cfg, out float mean, out float p95, out float max)
    {
        float water = cfg.WaterLevelY;
        var devs = new List<float>(); int noCross = 0;
        var bank = BellsBendRoads.Resample(v.innerBank, 10f);
        float northEdge = Mathf.Min(v.westCrossing.y, v.eastCrossing.y) - 20f; // the straight north edge is not shoreline
        for (int i = 0; i < bank.Count; i++)
        {
            var p = bank[i];
            if (p.y > northEdge) continue;
            var dir = (bank[Mathf.Min(i + 1, bank.Count - 1)] - bank[Mathf.Max(i - 1, 0)]).normalized;
            var nrm = new Vector2(-dir.y, dir.x);
            float best = float.MaxValue, prev = Height(p - nrm * 60f) - water;
            for (float t = -59.5f; t <= 60f; t += 0.5f)
            {
                float cur = Height(p + nrm * t) - water;
                if (prev * cur <= 0f && Mathf.Abs(t - 0.25f) < Mathf.Abs(best)) best = t - 0.25f;
                prev = cur;
            }
            if (best == float.MaxValue) { noCross++; devs.Add(60f); } else devs.Add(Mathf.Abs(best));
        }
        devs.Sort();
        mean = devs.Average(); p95 = devs[(int)(devs.Count * 0.95f)]; max = devs[devs.Count - 1];
        return $"L1 samples={devs.Count} mean={mean:F2}m p95={p95:F2}m max={max:F2}m noCrossingWithin60m={noCross} (counted as 60 m) pass(<=15)={mean <= 15f}";
    }

    static float Height(Vector2 p) => TerrainQuery.TryGroundHeight(new Vector3(p.x, 0f, p.y), out float h) ? h : float.NaN;

    public static string L1Overlay(BellsBendData.Vectors v, MapConfig cfg, string devText)
    {
        var poly = v.polygon;
        Vector2 min = new Vector2(poly.Min(p => p.x), poly.Min(p => p.y)) - Vector2.one * 150f;
        Vector2 max = new Vector2(poly.Max(p => p.x), poly.Max(p => p.y)) + Vector2.one * 150f;
        const int px = 2048;
        float mpp = Mathf.Max(max.x - min.x, max.y - min.y) / px;
        int W = Mathf.CeilToInt((max.x - min.x) / mpp), H = Mathf.CeilToInt((max.y - min.y) / mpp);
        var pixels = TopDown(min, W, H, mpp);
        float water = cfg.WaterLevelY;
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                float hgt = Height(min + new Vector2((x + 0.5f) * mpp, (y + 0.5f) * mpp));
                if (!(hgt < water)) continue;
                var c = pixels[y * W + x];
                pixels[y * W + x] = Color32.Lerp(c, new Color32(40, 90, 200, 255), 0.55f);
            }
        DrawPolyline(pixels, W, H, poly, min, mpp, new Color32(255, 30, 30, 255), true);
        DrawPolyline(pixels, W, H, new List<Vector2> { new Vector2(min.x, cfg.northLineZ), new Vector2(max.x, cfg.northLineZ) }, min, mpp, new Color32(255, 220, 0, 255), false);
        var path = SavePng("bells-bend-overlay.png", pixels, W, H);
        File.WriteAllText(Path.ChangeExtension(path, ".txt"),
            $"Top-down orthographic capture of the built terrain (blue = below WaterLevelY {water:F2}), red = scaled OSM playable polygon, yellow = MapConfig.northLineZ {cfg.northLineZ}. {mpp:F2} m/px, origin ({min.x:F0},{min.y:F0}).\n{devText}\n");
        return path;
    }

    // ---------------- L4 ----------------

    public static string L4(float[,] g, BellsBendData.Manifest m, BellsBendData.Vectors v, MapConfig cfg, out bool pass)
    {
        var lg = new LevelGrid(m.gridOrigin - Vector2.one * (m.sampleSpacing * 0.5f), new Vector2(g.GetLength(1), g.GetLength(0)) * m.sampleSpacing, m.sampleSpacing);
        var inside = lg.Rasterize(v.polygon);
        int nx = g.GetLength(1), nz = g.GetLength(0);
        var bins = new int[10]; int total = 0, ok = 0, floorCells = 0;
        float floor = cfg.WaterLevelY + cfg.landMinAboveWater;
        var bluffs = v.landmarks.Where(l => l.id.EndsWith("Bluff")).ToList();
        var bluffArea = new float[bluffs.Count]; var bluffMax = new float[bluffs.Count];
        float s = m.sampleSpacing;
        for (int r = 1; r < nz - 1; r++)
            for (int c = 1; c < nx - 1; c++)
            {
                int i = r * nx + c;
                var p = m.gridOrigin + new Vector2(c * s, r * s);
                if (!inside[i] || p.y > cfg.northLineZ) continue;
                if (!inside[i - 1] || !inside[i + 1] || !inside[i - nx] || !inside[i + nx]) continue; // shore falloff
                float dx = (g[r, c + 1] - g[r, c - 1]) / (2 * s), dz = (g[r + 1, c] - g[r - 1, c]) / (2 * s);
                float deg = Mathf.Atan(Mathf.Sqrt(dx * dx + dz * dz)) * Mathf.Rad2Deg;
                total++; if (deg <= 40f) ok++;
                if (g[r, c] <= floor + 0.01f) floorCells++;
                bins[Mathf.Min(9, (int)(deg / 5f))]++;
                for (int b = 0; b < bluffs.Count; b++)
                    if (Vector2.Distance(p, bluffs[b].xz) <= 60f)
                    {
                        bluffMax[b] = Mathf.Max(bluffMax[b], deg);
                        if (deg > 40f) bluffArea[b] += s * s;
                    }
            }
        float pct = ok * 100f / Mathf.Max(1, total);
        bool bluffOk = bluffArea.All(a => a >= 200f);
        pass = pct >= 90f && bluffOk;
        var csv = new StringBuilder("bin_deg,samples,pct\n");
        string[] labels = { "0-5", "5-10", "10-15", "15-20", "20-25", "25-30", "30-35", "35-40", "40-45", ">45" };
        for (int b = 0; b < 10; b++) csv.AppendLine($"{labels[b]},{bins[b]},{bins[b] * 100f / Mathf.Max(1, total):F2}");
        csv.AppendLine($"# verticalScale={cfg.verticalScale} landSamples={total} pctAtOrBelow40={pct:F2} floorClampedSamples={floorCells} ({floorCells * 100f / Mathf.Max(1, total):F2}%)");
        for (int b = 0; b < bluffs.Count; b++) csv.AppendLine($"# {bluffs[b].id}: areaOver40Within60m={bluffArea[b]:F0}m2 maxSlope={bluffMax[b]:F1}deg pass(>=200m2)={bluffArea[b] >= 200f}");
        var csvPath = BellsBendData.WriteText("bells-bend-slope-histogram.csv", csv.ToString());
        HistogramPng(bins, total);
        return $"L4 vs={cfg.verticalScale} land={total} pct<=40={pct:F2}% floorClamped={floorCells} ({floorCells * 100f / Mathf.Max(1, total):F2}%) " +
               string.Join(" ", bluffs.Select((bl, b) => $"{bl.id}={bluffArea[b]:F0}m2/max{bluffMax[b]:F1}")) + $" pass={pass} -> {csvPath}";
    }

    static void HistogramPng(int[] bins, int total)
    {
        const int W = 640, H = 360;
        var px = Enumerable.Repeat(new Color32(250, 250, 250, 255), W * H).ToArray();
        int maxBin = Mathf.Max(1, bins.Max());
        for (int b = 0; b < bins.Length; b++)
        {
            int x0 = 30 + b * 58, x1 = x0 + 48, top = 20 + (int)((H - 50) * bins[b] / (float)maxBin);
            var col = b >= 8 ? new Color32(200, 60, 40, 255) : new Color32(60, 120, 60, 255);
            for (int y = 20; y < top; y++) for (int x = x0; x < x1; x++) px[y * W + x] = col;
        }
        for (int x = 20; x < W - 10; x++) px[19 * W + x] = new Color32(0, 0, 0, 255);
        int line40 = 30 + 8 * 58 - 5;
        for (int y = 20; y < H - 10; y++) px[y * W + line40] = new Color32(0, 0, 0, 255);
        SavePng("bells-bend-slope-histogram.png", px, W, H);
    }

    // ---------------- L6 ----------------

    public static string RoadOverlay(BellsBendRoads.Result roads, LevelGrid rg, BellsBendData.Vectors v)
    {
        var pts = roads.chains.SelectMany(c => c.pts).ToList();
        Vector2 min = new Vector2(pts.Min(p => p.x), pts.Min(p => p.y)) - Vector2.one * 50f;
        Vector2 max = new Vector2(pts.Max(p => p.x), pts.Max(p => p.y)) + Vector2.one * 50f;
        const float mpp = 2f;
        int W = Mathf.CeilToInt((max.x - min.x) / mpp), H = Mathf.CeilToInt((max.y - min.y) / mpp);
        var px = TopDown(min, W, H, mpp);
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                int i = rg.Index(min + new Vector2((x + 0.5f) * mpp, (y + 0.5f) * mpp));
                if (i >= 0 && roads.weight[i] > 0) px[y * W + x] = Color32.Lerp(px[y * W + x], new Color32(255, 200, 0, 255), roads.weight[i] / 255f * 0.8f);
            }
        foreach (var ch in roads.chains) DrawPolyline(px, W, H, ch.pts, min, mpp, new Color32(0, 200, 255, 255), false);
        DrawPolyline(px, W, H, v.polygon, min, mpp, new Color32(255, 30, 30, 255), true);
        return SavePng("bells-bend-roads-overlay.png", px, W, H);
    }

    // ---------------- captures ----------------

    /// <summary>Orthographic top-down render of the live scene (fog off for the capture only).</summary>
    static Color32[] TopDown(Vector2 min, int W, int H, float mpp)
    {
        bool fog = RenderSettings.fog; RenderSettings.fog = false;
        var go = new GameObject("_L1Camera") { hideFlags = HideFlags.HideAndDontSave };
        var cam = go.AddComponent<Camera>();
        cam.orthographic = true; cam.orthographicSize = H * mpp * 0.5f; cam.aspect = W / (float)H;
        cam.transform.SetPositionAndRotation(new Vector3(min.x + W * mpp * 0.5f, 2000f, min.y + H * mpp * 0.5f), Quaternion.Euler(90f, 0f, 0f));
        cam.nearClipPlane = 1f; cam.farClipPlane = 4000f;
        cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Color.black;
        var px = Render(cam, W, H);
        Object.DestroyImmediate(go);
        RenderSettings.fog = fog;
        return px;
    }

    static Color32[] Render(Camera cam, int W, int H)
    {
        var rt = RenderTexture.GetTemporary(W, H, 24, RenderTextureFormat.ARGB32);
        cam.targetTexture = rt;
        cam.Render();
        var prev = RenderTexture.active; RenderTexture.active = rt;
        var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
        tex.ReadPixels(new Rect(0, 0, W, H), 0, 0); tex.Apply();
        RenderTexture.active = prev; cam.targetTexture = null; RenderTexture.ReleaseTemporary(rt);
        var px = tex.GetPixels32(); Object.DestroyImmediate(tex);
        return px;
    }

    /// <summary>Greenlight condition 4: Outdoor Center → ridge at eye level, and north from the line.</summary>
    public static string LookShots(MapConfig cfg, BellsBendData.Vectors v, ZoneConfig zc, string suffix = "")
    {
        var oc = v.landmarks.First(l => l.id == "OutdoorCenter").xz;
        var ridge = v.ToGame(zc.spawnFaceLat, zc.spawnFaceLon);
        var ohb = v.roads.Where(r => r.name == "Old Hickory Boulevard").SelectMany(r => r.points).ToList();
        float crossX = BarrierBuilder.CrossingX(ohb, cfg.northLineZ - BarrierBuilder.LineOffset); // the gate
        var a = Shot($"bells-bend-look-outdoorcenter-to-ridge{suffix}.png", oc, Mathf.Atan2(ridge.x - oc.x, ridge.y - oc.y) * Mathf.Rad2Deg, -2f);
        var b = Shot($"bells-bend-look-north-from-line{suffix}.png", new Vector2(crossX, cfg.northLineZ - BarrierBuilder.LineOffset - 6f), 0f, 0f); // nearest reachable spot: OHB just south of the gate (designer-2/qa-2)
        // Stand just south of the fence at its west end: walk east from the west crossing to land 1 m above water, then 15 m on.
        var wc = new Vector2(v.westCrossing.x, cfg.northLineZ - BarrierBuilder.LineOffset - 6f);
        for (int i = 0; i < 400 && !(Height(wc) > cfg.WaterLevelY + 1f); i++) wc.x += 1f;
        wc.x += 15f;
        float cornerYaw = Mathf.Atan2(v.westCrossing.x - wc.x, cfg.northLineZ + 3f - wc.y) * Mathf.Rad2Deg;
        var c = Shot($"bells-bend-look-west-river-corner{suffix}.png", wc, cornerYaw, 0f);
        // Raised view so the screened step and the fence in front of it are both in frame (ruling/bells-bend-corner-step).
        // Close raised view for the corner-step exception: from inside the barrier band ~35 m SE of the step
        // (x = westCrossing - 3..8, z = line + 1..4), so the step and the river-corner fence screening it share the frame.
        var step = new Vector2(v.westCrossing.x - 5f, cfg.northLineZ + 2.5f);
        var cam = step + new Vector2(32f, -14f);
        var d = Shot($"bells-bend-look-west-river-corner-raised{suffix}.png", cam, Mathf.Atan2(step.x - cam.x, step.y - cam.y) * Mathf.Rad2Deg, 22f, 14f);
        return $"look shots: {a} ; {b} ; {c} ; {d}";
    }

    static string Shot(string file, Vector2 xz, float yaw, float pitch, float eye = 1.7f)
    {
        TerrainQuery.TryGroundHeight(new Vector3(xz.x, 0f, xz.y), out float y);
        var go = new GameObject("_LookCamera") { hideFlags = HideFlags.HideAndDontSave };
        var cam = go.AddComponent<Camera>();
        cam.fieldOfView = 60f; cam.nearClipPlane = 0.1f; cam.farClipPlane = 6000f;
        cam.clearFlags = CameraClearFlags.Skybox;
        cam.transform.SetPositionAndRotation(new Vector3(xz.x, y + eye, xz.y), Quaternion.Euler(pitch, yaw, 0f));
        var px = Render(cam, 1920, 1080);
        Object.DestroyImmediate(go);
        return SavePng(file, px, 1920, 1080) + $" (pos {xz.x:F0},{y + eye:F1},{xz.y:F0} yaw {yaw:F0} pitch {pitch:F0})";
    }

    // ---------------- image helpers ----------------

    static void DrawPolyline(Color32[] px, int W, int H, List<Vector2> pts, Vector2 min, float mpp, Color32 col, bool closed)
    {
        int n = closed ? pts.Count : pts.Count - 1;
        for (int i = 0; i < n; i++)
        {
            Vector2 a = (pts[i] - min) / mpp, b = (pts[(i + 1) % pts.Count] - min) / mpp;
            int steps = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(a, b) * 2));
            for (int s = 0; s <= steps; s++)
            {
                var p = Vector2.Lerp(a, b, s / (float)steps);
                for (int oy = -1; oy <= 1; oy++)
                    for (int ox = -1; ox <= 1; ox++)
                    {
                        int x = (int)p.x + ox, y = (int)p.y + oy;
                        if (x >= 0 && y >= 0 && x < W && y < H) px[y * W + x] = col;
                    }
            }
        }
    }

    static string SavePng(string file, Color32[] px, int W, int H)
    {
        var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
        tex.SetPixels32(px); tex.Apply();
        Directory.CreateDirectory(BellsBendData.PocDir);
        var path = Path.Combine(BellsBendData.PocDir, file);
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        return path;
    }
}
