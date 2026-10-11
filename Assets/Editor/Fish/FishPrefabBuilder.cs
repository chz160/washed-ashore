using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace WashedAshore.Fish.Editor
{
    /// <summary>
    /// F2 census prefabs: one prefab variant of the imported Fish1/Fish2 model per brief variant id (fish-targets.json order),
    /// at its species' mean adult length with the G3 axis scale on the root, and its G3 swatches on two materials (body and
    /// fins; FishUnderwater on the mesh path, _UseVat 0). They're the per-species objects for the census, f-qa's F2 check
    /// and later fishing (hooking hands one instanced fish over to its prefab); the ambient fish are drawn instanced by
    /// FishRenderer, not from these. Only the root's scale and the renderer's materials are overridden, never the armature
    /// or mesh node, so the import facing fix holds (ModelFacingPostprocessor.CheckVariant). Rerun after the bake.
    /// Run: unity command eval "return WashedAshore.Fish.Editor.FishPrefabBuilder.Build();"
    /// </summary>
    public static class FishPrefabBuilder
    {
        public const string PrefabDir = "Assets/World/Fish/Prefabs";
        public const string MaterialDir = PrefabDir + "/Materials";

        [MenuItem("Washed Ashore/Art/Build Fish Prefab Variants")]
        static void Menu() => Debug.Log(Build());

        public static string Build()
        {
            string root = Path.GetDirectoryName(Application.dataPath);
            Directory.CreateDirectory(Path.Combine(root, MaterialDir));
            var bodies = AssetDatabase.LoadAssetAtPath<FishBodies>(FishVatBake.BodiesPath);
            if (!bodies) throw new FileNotFoundException("run FishVatBake first", FishVatBake.BodiesPath);
            var shader = Shader.Find("WashedAshore/FishUnderwater");
            var sb = new StringBuilder($"FishPrefabBuilder {System.DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}\n");
            var variants = FishLooks.Variants();
            for (int v = 0; v < variants.Count; v++)
            {
                var (id, species, length) = variants[v];
                var look = FishLooks.For(id, out var back, out var belly, out var fins);
                var body = bodies.bodies[v];
                if (body.variant != id) throw new InvalidDataException($"FishBodies[{v}] is '{body.variant}', expected '{id}': rebake");
                var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(look.model == "Fish1" ? FishImportSettings.Fish1Fbx : FishImportSettings.Fish2Fbx);
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
                try
                {
                    inst.name = id;
                    float size = 0.5f * (length.x + length.y) / body.noseToTail;
                    inst.transform.localScale = new Vector3(look.x, look.y, 1f) * size;
                    var smr = inst.GetComponentInChildren<SkinnedMeshRenderer>(true);
                    var meshBounds = smr.sharedMesh.bounds;
                    Material Mat(bool fin)
                    {
                        string path = $"{MaterialDir}/{id}_{(fin ? "Fins" : "Body")}.mat";
                        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                        if (!m) { m = new Material(shader); AssetDatabase.CreateAsset(m, path); }
                        m.shader = shader;
                        m.SetFloat("_UseVat", 0f); m.SetFloat("_IsFin", fin ? 1f : 0f);
                        m.SetColor("_BackColor", back); m.SetColor("_BellyColor", belly); m.SetColor("_FinColor", fins);
                        m.SetFloat("_Mottle", look.mottle);
                        // Mesh path: positions are in the skinned mesh's space; split at its bounds centre height.
                        m.SetVector("_BodyFrame", new Vector4(meshBounds.center.y, Mathf.Max(meshBounds.size.x, meshBounds.size.y, meshBounds.size.z), 0f, 0.03f));
                        EditorUtility.SetDirty(m);
                        return m;
                    }
                    Material bodyMat = Mat(false), finMat = Mat(true);
                    smr.sharedMaterials = smr.sharedMaterials.Select(m => m && m.name.StartsWith("Fins") ? finMat : bodyMat).ToArray();
                    smr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    string prefabPath = $"{PrefabDir}/{id}.prefab";
                    PrefabUtility.SaveAsPrefabAsset(inst, prefabPath);
                    sb.AppendLine($"  {prefabPath}: variant of {look.model}, scale ({inst.transform.localScale.x:F4}, {inst.transform.localScale.y:F4}, {inst.transform.localScale.z:F4}) " +
                                  $"= mean adult {0.5f * (length.x + length.y):F2} m; back #{ColorUtility.ToHtmlStringRGB(back)} belly #{ColorUtility.ToHtmlStringRGB(belly)} fins #{ColorUtility.ToHtmlStringRGB(fins)}");
                }
                finally { Object.DestroyImmediate(inst); }
            }
            AssetDatabase.SaveAssets();
            sb.AppendLine($"{variants.Count} prefab variants in {PrefabDir}");
            File.WriteAllText(Path.Combine(root, "TestResults", "fish-prefabs.txt"), sb.ToString());
            return sb.ToString();
        }
    }
}
