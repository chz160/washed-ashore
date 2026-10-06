using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

// Technical-artist R2 tool: gives each MegaKit tree a real LOD chain for the terrain. The kit's own prefabs
// carry a one-level LODGroup (3-10k tris drawn out to the cull distance). Each tree gets
// Assets/World/BellsBend/Art/TreeLods/<name>.prefab: LOD0 = the MegaKit mesh, LOD1 = a crossed-quad impostor
// (two vertical cards, 4 triangles) textured from orthographic captures of the tree; the terrain then cuts LOD0
// beyond ~LodSwitch of screen height. Re-runnable; captures are rebuilt each run.
// Run via: unity command eval --detach "return BellsBendTreeLods.Build();"
public static class BellsBendTreeLods
{
    public const string Root = "Assets/World/BellsBend/Art/TreeLods";
    const int Cap = 512;                 // capture resolution per view (atlas is 2 views side by side)
    public const float LodSwitch = 0.12f; // screen-relative height where LOD0 hands over to the impostor
    public const float Cull = 0.004f;

    public static string Build()
    {
        Directory.CreateDirectory(Root);
        var names = BellsBendVegetation.TreeSpecs.SelectMany(s => s.names).Distinct().ToList();
        var made = names.Select(n => Ensure(n, true)).Where(g => g).Select(g => g.name).ToList();
        AssetDatabase.SaveAssets();
        return $"lodPrefabs=[{string.Join(",", made)}] switch={LodSwitch} cull={Cull}";
    }

    /// <summary>The LOD prefab for a MegaKit tree (built if missing, or always when rebuild is set).</summary>
    public static GameObject Ensure(string name, bool rebuild = false)
    {
        var path = $"{Root}/{name}.prefab"; // same name as the MegaKit prefab: zone reports count trees by name prefix
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (existing && !rebuild) return existing;
        var src = AssetDatabase.FindAssets($"{name} t:Prefab", new[] { "Assets/ThirdParty/Quaternius/NatureMegaKit" })
            .Select(AssetDatabase.GUIDToAssetPath).Where(p => Path.GetFileNameWithoutExtension(p) == name)
            .Select(AssetDatabase.LoadAssetAtPath<GameObject>).FirstOrDefault();
        if (!src) return null;

        var mf = src.GetComponentsInChildren<MeshFilter>().First(f => f.sharedMesh);
        var mr = mf.GetComponent<MeshRenderer>();
        var b = BellsBendVegetation.MeshBounds(src);
        var tex = Capture(src, b, $"{Root}/T_{name}_Impostor.png");
        var mat = ImpostorMaterial(name, tex);
        var quad = CardMesh(name, b);

        var go = new GameObject(name);
        var lod0 = new GameObject("LOD0");
        lod0.transform.SetParent(go.transform, false);
        lod0.transform.localPosition = mf.transform.position - src.transform.position;
        lod0.transform.localRotation = mf.transform.rotation;
        lod0.transform.localScale = mf.transform.lossyScale;
        lod0.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
        var r0 = lod0.AddComponent<MeshRenderer>();
        r0.sharedMaterials = mr.sharedMaterials;
        var lod1 = new GameObject("LOD1_Impostor");
        lod1.transform.SetParent(go.transform, false);
        lod1.AddComponent<MeshFilter>().sharedMesh = quad;
        var r1 = lod1.AddComponent<MeshRenderer>();
        r1.sharedMaterial = mat;
        r1.shadowCastingMode = ShadowCastingMode.On;
        var group = go.AddComponent<LODGroup>();
        group.SetLODs(new[] { new LOD(LodSwitch, new Renderer[] { r0 }), new LOD(Cull, new Renderer[] { r1 }) });
        group.fadeMode = LODFadeMode.None;
        group.RecalculateBounds();
        var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
        Object.DestroyImmediate(go);
        return prefab;
    }

    // Two orthographic captures (front, and side at 90 deg) into one RGBA atlas, lit by a preview-scene sun.
    static Texture2D Capture(GameObject src, Bounds b, string path)
    {
        var scene = EditorSceneManager.NewPreviewScene();
        try
        {
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(src, scene);
            var sunGo = new GameObject("sun");
            SceneManager_Move(sunGo, scene);
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.2f;
            sun.transform.rotation = Quaternion.Euler(45f, -30f, 0f);
            var camGo = new GameObject("cam");
            SceneManager_Move(camGo, scene);
            var cam = camGo.AddComponent<Camera>();
            cam.scene = scene;
            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.3f, 0.36f, 0.22f, 0f); // foliage-ish fringe colour, alpha 0
            cam.cameraType = CameraType.Preview;
            float half = Mathf.Max(b.size.y, Mathf.Max(b.size.x, b.size.z)) * 0.5f;
            cam.orthographicSize = b.size.y * 0.5f;
            cam.nearClipPlane = 0.01f;
            cam.farClipPlane = half * 4f + 10f;
            var rt = RenderTexture.GetTemporary(Cap, Cap, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var atlas = new Texture2D(Cap * 2, Cap, TextureFormat.RGBA32, false);
            for (int v = 0; v < 2; v++)
            {
                var dir = v == 0 ? Vector3.forward : Vector3.right;
                cam.transform.position = b.center - dir * (half * 2f + 2f);
                cam.transform.rotation = Quaternion.LookRotation(dir);
                cam.targetTexture = rt;
                cam.aspect = Width(b) / b.size.y; // squeeze the w x h frame into the square capture; the card unsqueezes it
                cam.Render();
                RenderTexture.active = rt;
                atlas.ReadPixels(new Rect(0, 0, Cap, Cap), v * Cap, 0);
            }
            atlas.Apply();
            RenderTexture.active = null;
            cam.targetTexture = null;
            RenderTexture.ReleaseTemporary(rt);
            File.WriteAllBytes(path, atlas.EncodeToPNG());
            Object.DestroyImmediate(atlas);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.alphaIsTransparency = true;
            imp.mipmapEnabled = true;
            imp.mipMapsPreserveCoverage = true;   // keeps alpha-clipped cards from thinning out with distance
            imp.alphaTestReferenceValue = 0.5f;
            imp.wrapMode = TextureWrapMode.Clamp;
            imp.maxTextureSize = 1024;
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }

    static void SceneManager_Move(GameObject go, UnityEngine.SceneManagement.Scene scene) =>
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, scene);

    // The capture frames a box of Width(b) x height, centred on the bounds, from both views.
    static float Width(Bounds b) => Mathf.Max(b.size.x, b.size.z);

    static Material ImpostorMaterial(string name, Texture2D tex)
    {
        var path = $"{Root}/M_{name}_Impostor.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!mat)
        {
            mat = new Material(Shader.Find("Universal Render Pipeline/Simple Lit"));
            AssetDatabase.CreateAsset(mat, path);
        }
        mat.SetTexture("_BaseMap", tex);
        mat.SetColor("_BaseColor", new Color(0.85f, 0.85f, 0.85f)); // the capture is already lit once
        mat.SetFloat("_AlphaClip", 1f);
        mat.SetFloat("_Cutoff", 0.5f);
        mat.SetFloat("_Cull", 0f);
        mat.SetFloat("_Smoothness", 0f);
        mat.EnableKeyword("_ALPHATEST_ON");
        mat.renderQueue = (int)RenderQueue.AlphaTest;
        mat.enableInstancing = true;
        EditorUtility.SetDirty(mat);
        return mat;
    }

    // Two vertical cards crossing at the trunk: card 0 faces +Z (front capture), card 1 faces +X (side capture).
    static Mesh CardMesh(string name, Bounds b)
    {
        var path = $"{Root}/MSH_{name}_Impostor.asset";
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (!mesh) { mesh = new Mesh(); AssetDatabase.CreateAsset(mesh, path); }
        mesh.Clear();
        float w = Width(b) * 0.5f, y0 = b.min.y, y1 = b.max.y;
        var c = b.center;
        var v = new[]
        {
            new Vector3(c.x - w, y0, c.z), new Vector3(c.x + w, y0, c.z), new Vector3(c.x + w, y1, c.z), new Vector3(c.x - w, y1, c.z),
            new Vector3(c.x, y0, c.z + w), new Vector3(c.x, y0, c.z - w), new Vector3(c.x, y1, c.z - w), new Vector3(c.x, y1, c.z + w),
        };
        var uv = new[]
        {
            new Vector2(0f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 1f), new Vector2(0f, 1f),
            new Vector2(0.5f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
        };
        mesh.vertices = v;
        mesh.uv = uv;
        mesh.triangles = new[] { 0, 2, 1, 0, 3, 2, 4, 6, 5, 4, 7, 6 };
        // Up-facing normals: the cards light like a canopy instead of going dark edge-on to the sun.
        mesh.normals = Enumerable.Repeat(Vector3.up, 8).ToArray();
        mesh.RecalculateBounds();
        mesh.name = $"MSH_{name}_Impostor";
        EditorUtility.SetDirty(mesh);
        return mesh;
    }
}
