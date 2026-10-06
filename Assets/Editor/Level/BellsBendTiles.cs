using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// Level-designer: writes the Bells Bend Terrain tile grid from a stitched height grid
// (tools/terrain RAW tiles + level edits). Replaces the retired 512 m WorldBuilder terrain.
// Writes heights only; terrain layers, splat, trees and details belong to BellsBendGround (art).
public static class BellsBendTiles
{
    public const string RootName = "Terrain";
    public const int GroupingId = 7;
    public static string TileDir => BellsBendData.WorldRoot + "/Tiles";

    /// <summary>Unity Y at world XZ from the stitched grid (bilinear).</summary>
    public static float Sample(float[,] g, BellsBendData.Manifest m, float x, float z)
    {
        float fx = (x - m.gridOrigin.x) / m.sampleSpacing, fz = (z - m.gridOrigin.y) / m.sampleSpacing;
        int nx = g.GetLength(1), nz = g.GetLength(0);
        fx = Mathf.Clamp(fx, 0, nx - 1.001f); fz = Mathf.Clamp(fz, 0, nz - 1.001f);
        int c = (int)fx, r = (int)fz; float tx = fx - c, tz = fz - r;
        return Mathf.Lerp(Mathf.Lerp(g[r, c], g[r, c + 1], tx), Mathf.Lerp(g[r + 1, c], g[r + 1, c + 1], tx), tz);
    }

    public static string Write(BellsBendData.Manifest m, float[,] grid)
    {
        Directory.CreateDirectory(TileDir);
        var old = GameObject.Find(RootName);
        if (old) Object.DestroyImmediate(old);
        var root = new GameObject(RootName) { isStatic = true };
        var mat = GraphicsSettings.currentRenderPipeline ? GraphicsSettings.currentRenderPipeline.defaultTerrainMaterial : null;

        int n = m.res - 1;
        var h = new float[m.res, m.res];
        float min = float.MaxValue, max = float.MinValue;
        var terrains = new List<Terrain>();
        foreach (var t in m.tiles.OrderBy(t => t.iz).ThenBy(t => t.ix))
        {
            for (int r = 0; r < m.res; r++)
                for (int c = 0; c < m.res; c++)
                {
                    float y = grid[t.iz * n + r, t.ix * n + c];
                    min = Mathf.Min(min, y); max = Mathf.Max(max, y);
                    h[r, c] = Mathf.Clamp01((y - m.terrainBaseY) / m.terrainHeight);
                }
            var path = $"{TileDir}/TD_{t.ix}_{t.iz}.asset";
            var data = AssetDatabase.LoadAssetAtPath<TerrainData>(path);
            if (!data) { data = new TerrainData(); AssetDatabase.CreateAsset(data, path); }
            // Only resize when needed: art's splat/trees/details live on these assets and must survive a re-run.
            if (data.heightmapResolution != m.res) data.heightmapResolution = m.res;
            var size = new Vector3(m.tileSize, m.terrainHeight, m.tileSize);
            if (data.size != size) data.size = size;
            data.SetHeights(0, 0, h);
            data.SyncHeightmap();
            EditorUtility.SetDirty(data);

            var go = Terrain.CreateTerrainGameObject(data);
            go.name = $"Terrain_{t.ix}_{t.iz}";
            go.isStatic = true;
            go.transform.SetParent(root.transform, false);
            go.transform.position = new Vector3(t.position.x, m.terrainBaseY, t.position.z);
            var terrain = go.GetComponent<Terrain>();
            terrain.groupingID = GroupingId;
            terrain.allowAutoConnect = true;
            terrain.drawInstanced = true;
            terrain.heightmapPixelError = 5f;
            if (mat) terrain.materialTemplate = mat;
            terrains.Add(terrain);
        }
        foreach (var t in terrains)
        {
            Terrain At(int dx, int dz) => terrains.FirstOrDefault(o => o.name == NameAt(t, dx, dz));
            t.SetNeighbors(At(-1, 0), At(0, 1), At(1, 0), At(0, -1));
        }
        Terrain.SetConnectivityDirty();
        AssetDatabase.SaveAssets();
        var connected = terrains.Count(t => t.leftNeighbor || t.rightNeighbor || t.topNeighbor || t.bottomNeighbor);
        return $"tiles={terrains.Count} ({m.tilesX}x{m.tilesZ}) tileSize={m.tileSize} res={m.res} spacing={m.sampleSpacing} " +
               $"extentX=[{m.gridOrigin.x},{m.gridOrigin.x + m.tilesX * m.tileSize}] extentZ=[{m.gridOrigin.y},{m.gridOrigin.y + m.tilesZ * m.tileSize}] " +
               $"baseY={m.terrainBaseY} height={m.terrainHeight} Y=[{min:F2},{max:F2}] groupingID={GroupingId} connectedTiles={connected}";
    }

    static string NameAt(Terrain t, int dx, int dz)
    {
        var p = t.name.Split('_');
        return $"Terrain_{int.Parse(p[1]) + dx}_{int.Parse(p[2]) + dz}";
    }

    /// <summary>Seam check: max height step across every shared tile edge (should be ~0).</summary>
    public static float MaxSeamError()
    {
        float worst = 0f;
        var all = Terrain.activeTerrains;
        foreach (var a in all)
        {
            var b = a.rightNeighbor;
            if (!b) continue;
            var da = a.terrainData; var db = b.terrainData;
            int res = da.heightmapResolution;
            var ea = da.GetHeights(res - 1, 0, 1, res); var eb = db.GetHeights(0, 0, 1, res);
            for (int i = 0; i < res; i++) worst = Mathf.Max(worst, Mathf.Abs(ea[i, 0] - eb[i, 0]) * da.size.y);
        }
        return worst;
    }

    // --- UTM 16N (EPSG:26916) forward projection, ported from tools/terrain/geo.py (Krueger series) ---
    public static Vector2 LatLonToGame(double lat, double lon, double originE, double originN, double hs)
    {
        const double A = 6378137.0, Fl = 1 / 298.257222101, K0 = 0.9996, E0 = 500000.0;
        double n = Fl / (2 - Fl), aRect = A / (1 + n) * (1 + n * n / 4 + n * n * n * n / 64);
        double[] alpha = { n / 2 - 2 * n * n / 3 + 5 * n * n * n / 16, 13 * n * n / 48 - 3 * n * n * n / 5, 61 * n * n * n / 240 };
        double phi = lat * System.Math.PI / 180, dl = (lon - (-183 + 6 * 16)) * System.Math.PI / 180;
        double c = 2 * System.Math.Sqrt(n) / (1 + n), s = System.Math.Sin(phi);
        double t = System.Math.Sinh(Atanh(s) - c * Atanh(c * s));
        double xi = System.Math.Atan2(t, System.Math.Cos(dl)), eta = Atanh(System.Math.Sin(dl) / System.Math.Sqrt(1 + t * t));
        double es = eta, ns = xi;
        for (int j = 1; j <= 3; j++)
        {
            es += alpha[j - 1] * System.Math.Cos(2 * j * xi) * System.Math.Sinh(2 * j * eta);
            ns += alpha[j - 1] * System.Math.Sin(2 * j * xi) * System.Math.Cosh(2 * j * eta);
        }
        double e = E0 + K0 * aRect * es, no = K0 * aRect * ns;
        return new Vector2((float)((e - originE) * hs), (float)((no - originN) * hs));
    }

    static double Atanh(double x) => 0.5 * System.Math.Log((1 + x) / (1 - x));
}
