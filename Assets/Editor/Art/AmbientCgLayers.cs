using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using UnityEditor;
using UnityEngine;

// Technical-artist tool: fetches the Bells Bend ground materials from ambientCG (CC0) and builds
// their TerrainLayers. Re-runnable: a texture already under Assets/ThirdParty/ambientCG is reused,
// a zip already in _staging/ambientcg is not downloaded again.
// Run via: unity command eval "return AmbientCgLayers.Build();"
public static class AmbientCgLayers
{
    public const string TexRoot = "Assets/ThirdParty/ambientCG";
    public const string LayerRoot = "Assets/World/BellsBend/Art/Layers";
    static string StagingDir => Path.Combine(Path.GetDirectoryName(Application.dataPath), "_staging", "ambientcg");

    // Splat channel order is fixed: BellsBendGround paints by these indices, and wildlife/bird
    // habitat rules read layer 0 as grass (designer-2's brief, habitat contract).
    public const int Grass = 0, ForestFloor = 1, Asphalt = 2, Gravel = 3, Bank = 4, Rock = 5, Kudzu = 6;
    public static readonly (string key, string id, Vector2 tile, float smooth)[] Specs =
    {
        ("OldFieldGrass", "Ground037", new Vector2(5f, 5f), 0.05f),   // moss, broomsedge stubble, twigs: not lawn
        ("ForestFloor", "Ground023", new Vector2(4f, 4f), 0.05f),     // leaf litter (ridge, hollows, bank)
        ("CrackedAsphalt", "Asphalt026C", new Vector2(6f, 6f), 0.15f),
        ("OvergrownGravel", "Ground078", new Vector2(6f, 3f), 0.05f),  // 2:1 source image
        ("RiverBank", "Ground054", new Vector2(5f, 5f), 0.2f),        // silt/mud
        ("BluffRock", "Rock051", new Vector2(12f, 12f), 0.1f),        // grey limestone with moss
        ("KudzuCover", "Grass001", new Vector2(3f, 3f), 0.1f),        // dense dark broadleaf cover on the verges
    };

    public static string Build()
    {
        Directory.CreateDirectory(LayerRoot);
        var names = new System.Collections.Generic.List<string>();
        foreach (var layer in Load(true)) names.Add(layer.name);
        AssetDatabase.SaveAssets();
        return $"layers=[{string.Join(",", names)}] texRoot={TexRoot}";
    }

    /// <summary>The layers in splat order; with create, (re)builds any that are missing or stale.</summary>
    public static TerrainLayer[] Load(bool create)
    {
        return Specs.Select(s =>
        {
            var path = $"{LayerRoot}/{s.key}.terrainlayer";
            var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
            if (!create) return layer;
            var color = ImportTex(s.id, "Color", false);
            var normal = ImportTex(s.id, "NormalGL", true);
            if (!layer)
            {
                layer = new TerrainLayer();
                AssetDatabase.CreateAsset(layer, path);
            }
            layer.diffuseTexture = color;
            layer.normalMapTexture = normal;
            layer.tileSize = s.tile;
            layer.normalScale = 1f;
            layer.smoothness = s.smooth;
            layer.metallic = 0f;
            EditorUtility.SetDirty(layer);
            return layer;
        }).ToArray();
    }

    static Texture2D ImportTex(string id, string map, bool normal)
    {
        var file = $"{id}_1K-JPG_{map}.jpg";
        var dst = $"{TexRoot}/{id}/{file}";
        if (!File.Exists(dst) || IsLfsPointer(dst))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(dst));
            File.Copy(Staged(id, file), dst, true);
            AssetDatabase.ImportAsset(dst, ImportAssetOptions.ForceSynchronousImport);
        }
        var imp = (TextureImporter)AssetImporter.GetAtPath(dst);
        var type = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
        // 1K, BC7/DXT5nm via Normal quality, mips on: about 1.3 MB per map on Windows.
        if (imp.textureType != type || imp.sRGBTexture == normal || imp.maxTextureSize != 1024 || !imp.mipmapEnabled)
        {
            imp.textureType = type;
            imp.sRGBTexture = !normal;
            imp.mipmapEnabled = true;
            imp.maxTextureSize = 1024;
            imp.textureCompression = TextureImporterCompression.Compressed;
            imp.anisoLevel = 4;
            imp.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(dst);
    }

    static string Staged(string id, string file)
    {
        var src = Path.Combine(StagingDir, id, file);
        if (File.Exists(src) && !IsLfsPointer(src)) return src;
        var zip = Path.Combine(StagingDir, $"{id}_1K-JPG.zip");
        if (!File.Exists(zip) || new FileInfo(zip).Length < 1024)
        {
            Directory.CreateDirectory(StagingDir);
            using var http = new HttpClient { Timeout = System.TimeSpan.FromMinutes(2) };
            var bytes = http.GetByteArrayAsync($"https://ambientcg.com/get?file={id}_1K-JPG.zip").Result;
            File.WriteAllBytes(zip, bytes);
        }
        using (var archive = ZipFile.OpenRead(zip))
        {
            var entry = archive.GetEntry(file) ?? throw new FileNotFoundException($"{file} is not in {zip}");
            Directory.CreateDirectory(Path.GetDirectoryName(src));
            entry.ExtractToFile(src, true);
        }
        return src;
    }

    static bool IsLfsPointer(string path)
    {
        if (new FileInfo(path).Length > 512) return false;
        return File.ReadAllText(path).StartsWith("version https://git-lfs");
    }
}
