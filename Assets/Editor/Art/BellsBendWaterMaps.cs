using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;
using WashedAshore.Level;
using WashedAshore.World;

// Technical-artist bake for the Bells Bend water look (spec W5, research D5, w-td ruling 1).
// WaterMaps.png, RG8 on the LevelMaps road grid (2 m cells, same origin and extent):
//   R = LevelMaps shore distance byte, copied as-is (128 + signed m; + inland). No shoreline is recomputed.
//   G = bed depth below WaterLevelY, clamp(W - terrain height, 0, 4 m) / 4 * 255, from the built tiles.
// Bilinear and non-readable, because inline samplers don't work on WebGL2 (GLES3).
// Also writes the tileable ripple texture once and points BellsBendWater.mat at the water shader.
// Called by w-level's BellsBendWater.Build() (by reflection) after the quads exist, behind its hash and line guards.
// Deterministic: same tiles + LevelMaps + MapConfig give the same bytes.
public static class BellsBendWaterMaps
{
    public const string Dir = "Assets/World/BellsBend/Water";
    public const string MapsPath = Dir + "/WaterMaps.png";
    public const string RipplePath = Dir + "/WaterRipple.png";
    public const string MaterialPath = Dir + "/BellsBendWater.mat";
    public const string ShaderName = "WashedAshore/BellsBendWater";
    public const float MaxDepth = 4f;   // metres encoded by G = 255
    const int RippleSize = 256;
    const int RippleSeed = 1987;

    public static string Bake(MapConfig cfg)
    {
        if (!cfg) throw new System.ArgumentNullException(nameof(cfg));
        var sw = Stopwatch.StartNew();
        var maps = AssetDatabase.LoadAssetAtPath<BellsBendLevelMaps>(BellsBendLevelMaps.AssetPath);
        if (!maps || !maps.roadTex) throw new FileNotFoundException("LevelMaps or its road texture missing; run BellsBendLevel.BuildAll() first", BellsBendLevelMaps.AssetPath);
        int w = maps.roadW, h = maps.roadH;
        var road = maps.roadTex.GetRawTextureData();
        if (road.Length != w * h * 3) throw new InvalidDataException($"LevelMaps road texture is {road.Length} bytes, expected RGB24 {w}x{h}");

        var tiles = BellsBendGround.Tiles();
        if (tiles.Length == 0) throw new System.InvalidOperationException("No Terrain_<col>_<row> tiles under 'Terrain'; open the World scene");
        var grid = new TileGrid(tiles);

        float water = cfg.WaterLevelY, cell = maps.roadCell;
        Vector2 origin = maps.originXZ;
        var rgb = new byte[w * h * 3];
        int uncovered = 0, wet = 0;
        for (int z = 0; z < h; z++)
            for (int x = 0; x < w; x++)
            {
                int i = z * w + x;
                float wx = origin.x + (x + 0.5f) * cell, wz = origin.y + (z + 0.5f) * cell;
                float depth;
                if (grid.TryHeight(wx, wz, out float ground)) depth = water - ground;
                else { depth = MaxDepth; uncovered++; }
                if (depth > 0f) wet++;
                rgb[i * 3] = road[i * 3 + 2];
                rgb[i * 3 + 1] = (byte)Mathf.RoundToInt(Mathf.Clamp(depth, 0f, MaxDepth) / MaxDepth * 255f);
            }
        Directory.CreateDirectory(Path.Combine(ProjectRoot, Dir));
        WritePng(MapsPath, w, h, rgb);
        Import(MapsPath, TextureImporterFormat.RG16, false, TextureWrapMode.Clamp, FilterMode.Bilinear);
        var mapsTex = AssetDatabase.LoadAssetAtPath<Texture2D>(MapsPath);
        if (!mapsTex || mapsTex.width != w || mapsTex.height != h || mapsTex.format != TextureFormat.RG16)
            throw new InvalidDataException($"WaterMaps import check failed: {(mapsTex ? $"{mapsTex.width}x{mapsTex.height} {mapsTex.format}" : "missing")}");

        // Always rewritten (deterministic, 256 px), so a change to RippleBytes can't leave a stale texture behind.
        WritePng(RipplePath, RippleSize, RippleSize, RippleBytes(), 4);
        Import(RipplePath, TextureImporterFormat.RGBA32, true, TextureWrapMode.Repeat, FilterMode.Trilinear);
        var ripple = AssetDatabase.LoadAssetAtPath<Texture2D>(RipplePath);

        var mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (!mat) throw new FileNotFoundException("Water material missing; BellsBendWater.Build() creates it before calling Bake", MaterialPath);
        var shader = Shader.Find(ShaderName);
        if (!shader) throw new FileNotFoundException("Water shader not found: " + ShaderName);
        bool switched = mat.shader != shader;
        if (switched) { mat.shader = shader; mat.shaderKeywords = new string[0]; }
        Vector2 size = new Vector2(w, h) * cell;
        mat.SetTexture("_WaterMaps", mapsTex);
        mat.SetVector("_WaterMapsST", new Vector4(1f / size.x, 1f / size.y, -origin.x / size.x, -origin.y / size.y));
        mat.SetFloat("_NorthLineZ", cfg.northLineZ);
        var vectors = BellsBendData.LoadVectors(cfg);
        float neckX = (vectors.westCrossing.x + vectors.eastCrossing.x) * 0.5f;
        mat.SetFloat("_NeckX", neckX);
        mat.SetTexture("_RippleTex", ripple);
        EditorUtility.SetDirty(mat);
        AssetDatabase.SaveAssets();

        return $"WaterMaps {w}x{h} @{cell} m RG8 wetCells={wet} uncovered={uncovered} W={water:F3} northLineZ={cfg.northLineZ} neckX={neckX:F1} " +
               $"ripple=written material={(switched ? "shader set" : "updated")} {sw.ElapsedMilliseconds} ms";
    }

    static string ProjectRoot => Path.GetDirectoryName(Application.dataPath);

    // Height lookup over the built tile grid: bilinear on each tile's own heightmap, read once.
    class TileGrid
    {
        readonly float[][,] heights;
        readonly Vector3[] pos;
        readonly Vector3 size;
        readonly int res, nx, nz;
        readonly float minX, minZ;

        public TileGrid(Terrain[] tiles)
        {
            size = tiles[0].terrainData.size;
            res = tiles[0].terrainData.heightmapResolution;
            minX = minZ = float.MaxValue;
            float maxX = float.MinValue, maxZ = float.MinValue;
            foreach (var t in tiles)
            {
                if (t.terrainData.size != size || t.terrainData.heightmapResolution != res)
                    throw new InvalidDataException($"Tile {t.name} differs in size or resolution from {tiles[0].name}");
                var p = t.transform.position;
                minX = Mathf.Min(minX, p.x); minZ = Mathf.Min(minZ, p.z);
                maxX = Mathf.Max(maxX, p.x); maxZ = Mathf.Max(maxZ, p.z);
            }
            nx = Mathf.RoundToInt((maxX - minX) / size.x) + 1;
            nz = Mathf.RoundToInt((maxZ - minZ) / size.z) + 1;
            heights = new float[nx * nz][,];
            pos = new Vector3[nx * nz];
            foreach (var t in tiles)
            {
                var p = t.transform.position;
                int k = Mathf.RoundToInt((p.z - minZ) / size.z) * nx + Mathf.RoundToInt((p.x - minX) / size.x);
                heights[k] = t.terrainData.GetHeights(0, 0, res, res);
                pos[k] = p;
            }
        }

        public bool TryHeight(float x, float z, out float y)
        {
            y = 0f;
            int ix = Mathf.FloorToInt((x - minX) / size.x), iz = Mathf.FloorToInt((z - minZ) / size.z);
            if (ix < 0 || iz < 0 || ix >= nx || iz >= nz) return false;
            var hm = heights[iz * nx + ix];
            if (hm == null) return false;
            var p = pos[iz * nx + ix];
            float u = Mathf.Clamp((x - p.x) / size.x * (res - 1), 0f, res - 1.001f);
            float v = Mathf.Clamp((z - p.z) / size.z * (res - 1), 0f, res - 1.001f);
            int u0 = (int)u, v0 = (int)v;
            float fu = u - u0, fv = v - v0;
            float a = Mathf.Lerp(hm[v0, u0], hm[v0, u0 + 1], fu), b = Mathf.Lerp(hm[v0 + 1, u0], hm[v0 + 1, u0 + 1], fu);
            y = p.y + Mathf.Lerp(a, b, fv) * size.y;
            return true;
        }
    }

    // Tileable ripple texture, linear RGBA8:
    //   RG = micro-ripple slope (x, z), normalised to the strongest slope, 0.5 = flat.
    //   B, A = foam patch noise (flat band-limited spectra); both histogram-equalised to uniform 0..1,
    //        so a coverage threshold of c keeps about c of the area.
    // Built from integer-frequency waves, so every channel tiles exactly.
    public static byte[] RippleBytes()
    {
        int n = RippleSize, px = n * n;
        var rng = new System.Random(RippleSeed);
        float[] sx = new float[px], sz = new float[px];
        AddWaves(rng, n, 48, 3, 22, 1.5f, null, sx, sz);
        float[] b = new float[px], a = new float[px];
        // Foam noise: flat spectra in a narrow band, so thresholded patches are about 0.5-2 m long on the
        // shader's 13 m / 16 m foam tiles (ruling water-look-scum-horizon 1(b)).
        AddWaves(rng, n, 32, 9, 14, 0f, b, null, null);
        AddWaves(rng, n, 32, 8, 13, 0f, a, null, null);
        float max = 1e-6f;
        for (int i = 0; i < px; i++) max = Mathf.Max(max, Mathf.Abs(sx[i]), Mathf.Abs(sz[i]));
        Equalise(b); Equalise(a);
        var bytes = new byte[px * 4];
        for (int i = 0; i < px; i++)
        {
            bytes[i * 4] = (byte)Mathf.RoundToInt((sx[i] / max * 0.5f + 0.5f) * 255f);
            bytes[i * 4 + 1] = (byte)Mathf.RoundToInt((sz[i] / max * 0.5f + 0.5f) * 255f);
            bytes[i * 4 + 2] = (byte)Mathf.RoundToInt(b[i] * 255f);
            bytes[i * 4 + 3] = (byte)Mathf.RoundToInt(a[i] * 255f);
        }
        return bytes;
    }

    // Sum of random periodic waves with integer wave numbers in [kMin, kMax] cycles per tile, amplitude ~ 1/k^falloff.
    // Writes the value (height) or its x/z derivatives, whichever arrays are given.
    static void AddWaves(System.Random rng, int n, int count, int kMin, int kMax, float falloff, float[] value, float[] dx, float[] dz)
    {
        for (int c = 0; c < count; c++)
        {
            int kx, kz;
            do { kx = rng.Next(-kMax, kMax + 1); kz = rng.Next(-kMax, kMax + 1); }
            while (kx * kx + kz * kz < kMin * kMin || kx * kx + kz * kz > kMax * kMax);
            float k = Mathf.Sqrt(kx * kx + kz * kz), amp = 1f / Mathf.Pow(k, falloff), phase = (float)(rng.NextDouble() * 2.0 * System.Math.PI);
            float w = 2f * Mathf.PI / n;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float arg = w * (kx * x + kz * y) + phase;
                    int i = y * n + x;
                    if (value != null) value[i] += amp * Mathf.Sin(arg);
                    if (dx != null) { float cs = amp * Mathf.Cos(arg) * w; dx[i] += cs * kx; dz[i] += cs * kz; }
                }
        }
    }

    static void Equalise(float[] v)
    {
        var idx = new int[v.Length];
        for (int i = 0; i < idx.Length; i++) idx[i] = i;
        var keys = (float[])v.Clone();
        System.Array.Sort(keys, idx);
        for (int r = 0; r < idx.Length; r++) v[idx[r]] = r / (float)(idx.Length - 1);
    }

    static void WritePng(string path, int w, int h, byte[] data, int channels = 3)
    {
        var fmt = channels == 4 ? TextureFormat.RGBA32 : TextureFormat.RGB24;
        var t = new Texture2D(w, h, fmt, false, true);
        t.SetPixelData(data, 0); t.Apply();
        File.WriteAllBytes(Path.Combine(ProjectRoot, path), t.EncodeToPNG());
        Object.DestroyImmediate(t);
    }

    static void Import(string path, TextureImporterFormat fmt, bool mips, TextureWrapMode wrap, FilterMode filter)
    {
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        var imp = (TextureImporter)AssetImporter.GetAtPath(path);
        imp.textureType = TextureImporterType.Default;
        imp.sRGBTexture = false; imp.mipmapEnabled = mips; imp.isReadable = false;
        imp.wrapMode = wrap; imp.filterMode = filter; imp.anisoLevel = mips ? 4 : 1;
        imp.npotScale = TextureImporterNPOTScale.None;
        imp.alphaSource = fmt == TextureImporterFormat.RGBA32 ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None;
        imp.maxTextureSize = 8192; imp.textureCompression = TextureImporterCompression.Uncompressed;
        var ps = imp.GetDefaultPlatformTextureSettings(); ps.format = fmt; ps.maxTextureSize = 8192; ps.textureCompression = TextureImporterCompression.Uncompressed;
        imp.SetPlatformTextureSettings(ps);
        foreach (var platform in new[] { "Standalone", "WebGL" })
        {
            var o = imp.GetPlatformTextureSettings(platform);
            o.overridden = true; o.format = fmt; o.maxTextureSize = 8192; o.textureCompression = TextureImporterCompression.Uncompressed;
            imp.SetPlatformTextureSettings(o);
        }
        imp.SaveAndReimport();
    }
}
