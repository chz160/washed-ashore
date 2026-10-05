using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace WashedAshore.Wildlife.Editor
{
/// <summary>
/// Technical-artist tool: render/animation cost settings for the wildlife (A8).
/// Re-runnable and idempotent. Edits the four Quaternius source prefabs, so the Wildlife_*
/// variants (rebuilt by WildlifePrefabBuilder) and scene instances inherit; any override of
/// these fields on a variant or scene instance is reverted.
/// Run via: unity command eval "return WashedAshore.Wildlife.Editor.WildlifePerfSetup.Apply();"
/// Read-only check: unity command eval "return WashedAshore.Wildlife.Editor.WildlifePerfSetup.Report();"
/// </summary>
public static class WildlifePerfSetup
{
    const string PrefabDir = "Assets/ThirdParty/Quaternius/Animals/Prefabs";
    const string VariantDir = "Assets/World/Wildlife/Prefabs";
    public static readonly string[] Species = { "Deer", "Stag", "Fox", "Wolf" };

    // Offscreen animals skip transform writes but keep their state machine time, so they
    // don't pop back mid-stride. Root motion is not used (NavMeshAgent drives position).
    public const AnimatorCullingMode Culling = AnimatorCullingMode.CullUpdateTransforms;

    public static string Apply()
    {
        var log = new List<string>();
        foreach (var s in Species)
        {
            string path = $"{PrefabDir}/Animal_{s}.prefab";
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                foreach (var a in root.GetComponentsInChildren<Animator>(true))
                {
                    a.cullingMode = Culling;
                    a.applyRootMotion = false;
                }
                foreach (var r in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    // Small, slow animals: per-object motion vectors buy nothing without
                    // motion blur/TAA and cost an extra skinning pass.
                    r.skinnedMotionVectors = false;
                    r.updateWhenOffscreen = false;
                }
                PrefabUtility.SaveAsPrefabAsset(root, path);
                log.Add($"{s}: ok");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        log.Add(FixVariants());
        log.Add(RevertSceneOverrides());
        AssetDatabase.SaveAssets();
        return string.Join("\n", log) + "\n" + Report();
    }

    // Variants normally inherit; a variant that overrides the fields is set back in line.
    static string FixVariants()
    {
        int fixedCount = 0;
        foreach (var s in Species)
        {
            string path = $"{VariantDir}/Wildlife_{s}.prefab";
            if (!AssetDatabase.LoadAssetAtPath<GameObject>(path)) continue;
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                bool dirty = false;
                foreach (var a in root.GetComponentsInChildren<Animator>(true))
                    if (a.cullingMode != Culling) { a.cullingMode = Culling; dirty = true; }
                foreach (var r in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    if (r.skinnedMotionVectors) { r.skinnedMotionVectors = false; dirty = true; }
                if (dirty) { PrefabUtility.SaveAsPrefabAsset(root, path); fixedCount++; }
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        return $"variants needing a fix: {fixedCount}";
    }

    // Scene instances must not override the prefab cost settings.
    static string RevertSceneOverrides()
    {
        int reverted = 0;
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            var scene = SceneManager.GetSceneAt(i);
            foreach (var go in scene.GetRootGameObjects())
            {
                foreach (var a in go.GetComponentsInChildren<Animator>(true))
                {
                    if (!PrefabUtility.IsPartOfPrefabInstance(a) || a.cullingMode == Culling) continue;
                    var so = new SerializedObject(a);
                    PrefabUtility.RevertPropertyOverride(so.FindProperty("m_CullingMode"), InteractionMode.AutomatedAction);
                    reverted++;
                }
                foreach (var r in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (!PrefabUtility.IsPartOfPrefabInstance(r) || !r.skinnedMotionVectors) continue;
                    var so = new SerializedObject(r);
                    PrefabUtility.RevertPropertyOverride(so.FindProperty("m_SkinnedMotionVectors"), InteractionMode.AutomatedAction);
                    reverted++;
                }
            }
            if (reverted > 0) UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
        }
        return $"scene overrides reverted: {reverted}";
    }

    public static string Report()
    {
        var lines = new List<string>();
        foreach (var path in Species.Select(s => $"{PrefabDir}/Animal_{s}.prefab")
                     .Concat(Species.Select(s => $"{VariantDir}/Wildlife_{s}.prefab")))
        {
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (!root) { lines.Add($"{path}: missing"); continue; }
            string s = System.IO.Path.GetFileNameWithoutExtension(path);
            var anims = root.GetComponentsInChildren<Animator>(true);
            var smrs = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            int tris = smrs.Where(r => r.sharedMesh).Sum(r => r.sharedMesh.triangles.Length / 3);
            lines.Add($"{s}: animators={anims.Length} culling=[{string.Join(",", anims.Select(a => a.cullingMode))}] " +
                      $"smr={smrs.Length} motionVectors=[{string.Join(",", smrs.Select(r => r.skinnedMotionVectors))}] " +
                      $"shadows=[{string.Join(",", smrs.Select(r => r.shadowCastingMode))}] tris={tris} " +
                      $"bones={smrs.Sum(r => r.bones.Length)}");
        }
        int sceneAnims = 0, culled = 0;
        for (int i = 0; i < SceneManager.sceneCount; i++)
            foreach (var go in SceneManager.GetSceneAt(i).GetRootGameObjects())
                foreach (var a in go.GetComponentsInChildren<Animator>(true))
                {
                    sceneAnims++;
                    if (a.cullingMode == Culling) culled++;
                }
        lines.Add($"scene animators: {culled}/{sceneAnims} at {Culling}");
        return string.Join("\n", lines);
    }
}
}
