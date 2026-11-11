using System;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace WashedAshore.Character.Editor
{
    /// <summary>
    /// Builds the Survivor's one material and points the body and head FBX at it (pod 49 item 5, art-49 import standard section 4): URP Lit, metallic workflow,
    /// opaque, render face Front, base colour map + MetallicGloss map (R metallic, A smoothness, Smoothness Source = Metallic Alpha, slider 1), no normal map,
    /// white tint. One material for the body and the head. Idempotent: running it again changes nothing. Run once after the FBX and the PNGs are imported:
    /// `unity run . -- -executeMethod WashedAshore.Character.Editor.SurvivorAssetBuilder.BatchBuild`.
    /// </summary>
    public static class SurvivorAssetBuilder
    {
        public const string Folder = CharacterImportSettings.SurvivorRoot;
        public const string BodyPath = Folder + "/Survivor_Body.fbx";
        public const string HeadPath = Folder + "/Survivor_Head.fbx";
        public const string BaseColorPath = Folder + "/Survivor_BaseColor.png";
        public const string MetallicGlossPath = Folder + "/Survivor_MetallicGloss.png";
        public const string MaterialPath = Folder + "/Survivor.mat";
        public const string ShaderName = "Universal Render Pipeline/Lit";

        [MenuItem("Washed Ashore/Art/Character/Build Survivor Material")]
        static void BuildMenu() => Debug.Log(Build());

        /// <summary>Batch entry point (headless): builds, then exits 0, or 1 on an error.</summary>
        public static void BatchBuild()
        {
            try
            {
                Debug.Log(Build());
                EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogError("SurvivorAssetBuilder: " + e);
                EditorApplication.Exit(1);
            }
        }

        public static string Build()
        {
            AssetDatabase.Refresh();
            var baseColor = Require<Texture2D>(BaseColorPath);
            var gloss = Require<Texture2D>(MetallicGlossPath);
            var shader = Shader.Find(ShaderName);
            if (shader == null) throw new InvalidOperationException("shader not found: " + ShaderName);

            var mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            bool created = mat == null;
            if (created)
            {
                mat = new Material(shader) { name = "Survivor" };
                AssetDatabase.CreateAsset(mat, MaterialPath);
            }
            // Idempotent: every value is compared first and written only when it differs, and the asset is saved only when something changed. Saving an unchanged material
            // rewrites its file (Unity re-stamps the URP AssetVersion sub-object with a new random fileID), which showed as a changed hash on a second run.
            bool changed = created;
            changed |= Set(mat, mat.shader != shader, () => mat.shader = shader);
            changed |= Set(mat, mat.GetTexture("_BaseMap") != baseColor, () => mat.SetTexture("_BaseMap", baseColor));
            changed |= Set(mat, mat.GetColor("_BaseColor") != Color.white, () => mat.SetColor("_BaseColor", Color.white));
            changed |= Set(mat, mat.GetTexture("_MetallicGlossMap") != gloss, () => mat.SetTexture("_MetallicGlossMap", gloss));
            changed |= Set(mat, !mat.IsKeywordEnabled("_METALLICSPECGLOSSMAP"), () => mat.EnableKeyword("_METALLICSPECGLOSSMAP"));
            foreach (var (name, value) in new[] { ("_Metallic", 1f), ("_Smoothness", 1f), ("_SmoothnessTextureChannel", 0f), ("_Surface", 0f), ("_AlphaClip", 0f), ("_Cull", 2f),
                                                  ("_ReceiveShadows", 1f), ("_SpecularHighlights", 1f), ("_EnvironmentReflections", 1f) })   // 1/1: the map's R and A are read directly; Opaque; Render Face Front
            {
                string n = name;
                float v = value;
                changed |= Set(mat, !Mathf.Approximately(mat.GetFloat(n), v), () => mat.SetFloat(n, v));
            }
            changed |= Set(mat, mat.IsKeywordEnabled("_ALPHATEST_ON"), () => mat.DisableKeyword("_ALPHATEST_ON"));
            changed |= Set(mat, mat.GetTexture("_BumpMap") != null, () => mat.SetTexture("_BumpMap", null));      // no normal map
            changed |= Set(mat, mat.IsKeywordEnabled("_NORMALMAP"), () => mat.DisableKeyword("_NORMALMAP"));
            changed |= Set(mat, mat.GetTexture("_EmissionMap") != null, () => mat.SetTexture("_EmissionMap", null));
            changed |= Set(mat, mat.IsKeywordEnabled("_EMISSION"), () => mat.DisableKeyword("_EMISSION"));
            // the old code set renderQueue = -1 (shader default). Material.renderQueue reads the shader's queue (2000), never -1, so compare the serialized custom queue instead
            changed |= Set(mat, new SerializedObject(mat).FindProperty("m_CustomRenderQueue").intValue != -1, () => mat.renderQueue = -1);
            if (changed)
            {
                EditorUtility.SetDirty(mat);
                AssetDatabase.SaveAssets();
            }

            var sb = new StringBuilder($"Survivor material: {MaterialPath} base {baseColor.width}x{baseColor.height}, gloss {gloss.width}x{gloss.height}\n");
            int remapsAdded = 0;
            foreach (var path in new[] { BodyPath, HeadPath })
            {
                var importer = AssetImporter.GetAtPath(path) as ModelImporter ?? throw new InvalidOperationException(path + " is not imported");
                var map = importer.GetExternalObjectMap();
                int added = 0;
                foreach (var embedded in AssetDatabase.LoadAllAssetsAtPath(path).OfType<Material>().Where(m => m != mat).Select(m => m.name).Distinct().ToList())
                {
                    var id = new AssetImporter.SourceAssetIdentifier(typeof(Material), embedded);
                    if (map.TryGetValue(id, out var current) && current == mat) continue;   // already remapped: nothing to do
                    importer.AddRemap(id, mat);
                    added++;
                }
                remapsAdded += added;
                if (added > 0) importer.SaveAndReimport();   // only when a remap changed: a second run touches no file
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    sb.AppendLine($"  {path}: {smr.name} -> {string.Join(", ", smr.sharedMaterials.Select(m => m != null ? m.name : "null"))}; vertices {smr.sharedMesh.vertexCount}, shapes {smr.sharedMesh.blendShapeCount}");
            }
            sb.AppendLine($"material changed: {changed}");
            sb.AppendLine($"remaps added: {remapsAdded}");
            return sb.ToString();
        }

        /// <summary>Runs <paramref name="write"/> when <paramref name="differs"/>; returns whether it did.</summary>
        static bool Set(Material mat, bool differs, Action write)
        {
            if (differs) write();
            return differs;
        }

        static T Require<T>(string path) where T : UnityEngine.Object =>
            AssetDatabase.LoadAssetAtPath<T>(path) ?? throw new InvalidOperationException(path + " is missing or not imported");
    }
}
