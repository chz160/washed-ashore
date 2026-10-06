using System.Collections.Generic;
using UnityEngine;

// Level-designer: a regular XZ grid (cell centres) over the terrain extent, plus the raster
// helpers the Bells Bend level scripts share (polygon fill, distance to a polyline).
public class LevelGrid
{
    public Vector2 origin;
    public float cell;
    public int w, h;

    public LevelGrid(Vector2 origin, Vector2 size, float cell)
    {
        this.origin = origin; this.cell = cell;
        w = Mathf.CeilToInt(size.x / cell); h = Mathf.CeilToInt(size.y / cell);
    }

    public Vector2 Center(int c, int r) => origin + new Vector2((c + 0.5f) * cell, (r + 0.5f) * cell);
    public bool Contains(Vector2 p) => p.x >= origin.x && p.y >= origin.y && p.x < origin.x + w * cell && p.y < origin.y + h * cell;

    public int Index(Vector2 p)
    {
        int c = Mathf.FloorToInt((p.x - origin.x) / cell), r = Mathf.FloorToInt((p.y - origin.y) / cell);
        return c < 0 || r < 0 || c >= w || r >= h ? -1 : r * w + c;
    }

    public void Range(Vector2 min, Vector2 max, out int c0, out int r0, out int c1, out int r1)
    {
        c0 = Mathf.Max(0, Mathf.FloorToInt((min.x - origin.x) / cell));
        r0 = Mathf.Max(0, Mathf.FloorToInt((min.y - origin.y) / cell));
        c1 = Mathf.Min(w - 1, Mathf.FloorToInt((max.x - origin.x) / cell));
        r1 = Mathf.Min(h - 1, Mathf.FloorToInt((max.y - origin.y) / cell));
    }

    /// <summary>Scanline fill: true where the cell centre is inside the polygon.</summary>
    public bool[] Rasterize(List<Vector2> poly)
    {
        var mask = new bool[w * h];
        var xs = new List<float>();
        for (int r = 0; r < h; r++)
        {
            float z = origin.y + (r + 0.5f) * cell;
            xs.Clear();
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
                if ((poly[i].y > z) != (poly[j].y > z))
                    xs.Add(poly[i].x + (z - poly[i].y) / (poly[j].y - poly[i].y) * (poly[j].x - poly[i].x));
            xs.Sort();
            for (int k = 0; k + 1 < xs.Count; k += 2)
            {
                int c0 = Mathf.Max(0, Mathf.CeilToInt((xs[k] - origin.x) / cell - 0.5f));
                int c1 = Mathf.Min(w - 1, Mathf.FloorToInt((xs[k + 1] - origin.x) / cell - 0.5f));
                for (int c = c0; c <= c1; c++) mask[r * w + c] = true;
            }
        }
        return mask;
    }

    /// <summary>
    /// Distance from every cell centre to a polyline (seeded nearest-point propagation, two passes;
    /// exact at the seeds, within a fraction of a cell elsewhere). Values above maxDist are clamped.
    /// </summary>
    public float[] DistanceTo(List<Vector2> line, float maxDist)
    {
        int n = w * h;
        var near = new Vector2[n];
        var d2 = new float[n];
        for (int i = 0; i < n; i++) d2[i] = float.MaxValue;
        for (int i = 1; i < line.Count; i++)
        {
            Vector2 a = line[i - 1], b = line[i];
            int steps = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(a, b) / (cell * 0.5f)));
            for (int s = 0; s <= steps; s++)
            {
                var p = Vector2.Lerp(a, b, s / (float)steps);
                int idx = Index(p);
                if (idx < 0) continue;
                float dd = (Center(idx % w, idx / w) - p).sqrMagnitude;
                if (dd < d2[idx]) { d2[idx] = dd; near[idx] = p; }
            }
        }
        void Try(int idx, int from)
        {
            if (d2[from] == float.MaxValue) return;
            float dd = (Center(idx % w, idx / w) - near[from]).sqrMagnitude;
            if (dd < d2[idx]) { d2[idx] = dd; near[idx] = near[from]; }
        }
        for (int r = 0; r < h; r++)
            for (int c = 0; c < w; c++)
            {
                int i = r * w + c;
                if (c > 0) Try(i, i - 1);
                if (r > 0) { Try(i, i - w); if (c > 0) Try(i, i - w - 1); if (c < w - 1) Try(i, i - w + 1); }
            }
        for (int r = h - 1; r >= 0; r--)
            for (int c = w - 1; c >= 0; c--)
            {
                int i = r * w + c;
                if (c < w - 1) Try(i, i + 1);
                if (r < h - 1) { Try(i, i + w); if (c < w - 1) Try(i, i + w + 1); if (c > 0) Try(i, i + w - 1); }
            }
        var outD = new float[n];
        for (int i = 0; i < n; i++) outD[i] = Mathf.Min(maxDist, Mathf.Sqrt(d2[i]));
        return outD;
    }
}
