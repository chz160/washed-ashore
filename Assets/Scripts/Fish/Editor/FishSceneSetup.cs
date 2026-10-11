using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using WashedAshore.Fish;
using WashedAshore.Level;
using WashedAshore.World;

/// <summary>
/// Puts the fish into World.unity (spec F3-F9): a "Fish" root with <see cref="FishPopulation"/> wired to the shipped
/// assets. Rerunnable: it reuses an existing root and its other components (f-artist's renderer lives on the same root),
/// adds no colliders, and saves the scene only when something changed. Run after FishTuningImport.
/// </summary>
public static class FishSceneSetup
{
    public const string ScenePath = "Assets/Scenes/World.unity";
    public const string RootName = "Fish";
    const string MapConfigPath = "Assets/World/MapConfig.asset";
    const string WaterMaterialPath = "Assets/World/BellsBend/Water/BellsBendWater.mat";
    const string WaterMotionPath = "Assets/World/Water/WaterMotion.asset";

    [MenuItem("Washed Ashore/Fish/Set Up Fish In World")]
    public static void Run() => Debug.Log(Setup());

    /// <summary>
    /// Batchmode entry (-executeMethod FishSceneSetup.Batch): import the tuning from the brief, then set up the scene.
    /// Writes TestResults/fish-setup.txt and exits 0, or 1 with the error.
    /// </summary>
    public static void Batch()
    {
        string log = Path.Combine(Path.GetDirectoryName(Application.dataPath), "TestResults", "fish-setup.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(log));
        try
        {
            string import = FishTuningImport.Import();
            string setup = Setup();
            File.WriteAllText(log, import + "\n" + setup + "\n");
            EditorApplication.Exit(0);
        }
        catch (System.Exception e)
        {
            File.WriteAllText(log, "FAILED: " + e + "\n");
            EditorApplication.Exit(1);
        }
    }

    /// <summary>
    /// Batchmode entry (-executeMethod FishSceneSetup.BatchImport): only the tuning import from the brief of record (no
    /// scene change). Writes TestResults/fish-import.txt; exits 0 or 1.
    /// </summary>
    public static void BatchImport()
    {
        string log = Path.Combine(Path.GetDirectoryName(Application.dataPath), "TestResults", "fish-import.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(log));
        try
        {
            File.WriteAllText(log, FishTuningImport.Import() + "\n");
            EditorApplication.Exit(0);
        }
        catch (System.Exception e)
        {
            File.WriteAllText(log, "FAILED: " + e + "\n");
            EditorApplication.Exit(1);
        }
    }

    /// <summary>
    /// Batchmode entry (-executeMethod FishSceneSetup.BatchWire): only the rendering wiring (FishRenderSet on the renderer
    /// AND the surface FX), no tuning import and no population changes. Writes TestResults/fish-wire.txt; exits 0 or 1.
    /// </summary>
    public static void BatchWire()
    {
        string log = Path.Combine(Path.GetDirectoryName(Application.dataPath), "TestResults", "fish-wire.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(log));
        try
        {
            if (EditorSceneManager.GetActiveScene().path != ScenePath) EditorSceneManager.OpenScene(ScenePath);
            var scene = EditorSceneManager.GetActiveScene();
            GameObject root = null;
            foreach (var go in scene.GetRootGameObjects()) if (go.name == RootName) root = go;
            if (!root) throw new System.InvalidOperationException($"no '{RootName}' root in {ScenePath}");
            string render = AddRendering(root);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            File.WriteAllText(log, render + "\n");
            EditorApplication.Exit(render.Contains("not assigned") ? 1 : 0);
        }
        catch (System.Exception e)
        {
            File.WriteAllText(log, "FAILED: " + e + "\n");
            EditorApplication.Exit(1);
        }
    }

    public static string Setup()
    {
        var tuning = Load<FishTuning>(FishTuningImport.AssetPath);
        var bodies = Load<FishBodies>(FishTuningImport.BodiesPath);
        var map = Load<MapConfig>(MapConfigPath);
        var water = Load<Material>(WaterMaterialPath);
        var motion = Load<WaterMotionSettings>(WaterMotionPath);
        var anchors = Load<BellsBendStructureAnchors>(BellsBendStructureAnchors.AssetPath);
        var levelMaps = Load<BellsBendLevelMaps>(BellsBendLevelMaps.AssetPath);

        if (EditorSceneManager.GetActiveScene().path != ScenePath) EditorSceneManager.OpenScene(ScenePath);
        var scene = EditorSceneManager.GetActiveScene();
        GameObject root = null;
        int roots = 0;
        foreach (var go in scene.GetRootGameObjects())
            if (go.name == RootName) { root = go; roots++; }
        if (roots > 1) throw new System.InvalidOperationException($"{roots} '{RootName}' roots in {ScenePath}; remove the extras by hand");
        bool created = !root;
        if (created) root = new GameObject(RootName);
        var pop = root.GetComponent<FishPopulation>();
        if (!pop) pop = root.AddComponent<FishPopulation>();
        pop.Configure(tuning, bodies, map, water, motion, anchors, levelMaps);
        string render = AddRendering(root);
        if (root.GetComponentInChildren<Collider>(true) || root.GetComponentInChildren<Rigidbody>(true))
            throw new System.InvalidOperationException("Something under the Fish root has a Collider or Rigidbody (F10: fish never collide)");
        EditorUtility.SetDirty(pop);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        return $"Fish root {(created ? "created" : "updated")} in {ScenePath}: tuning {tuning.briefRevision} ({tuning.briefSha16}), " +
               $"{bodies.bodies.Length} bodies, {anchors.anchors.Length} anchors; {render}";
    }

    const string RenderingAssembly = "WashedAshore.Fish.Rendering";
    const string RenderSetPath = "Assets/World/Fish/Art/FishRenderSet.asset";

    /// <summary>
    /// f-artist's renderer and surface FX on the same root, by type name so this assembly doesn't depend on theirs
    /// (WashedAshore.Fish.Rendering references WashedAshore.Fish, never the reverse). The FishRenderSet field of BOTH is
    /// found by its type (slot B wired only the renderer, so no surface sign ever drew). Missing types or asset are
    /// reported and skipped.
    /// </summary>
    static string AddRendering(GameObject root)
    {
        var notes = new System.Collections.Generic.List<string>();
        var renderer = Ensure(root, "WashedAshore.Fish.Rendering.FishRenderer", notes);
        var fx = Ensure(root, "WashedAshore.Fish.Rendering.FishSurfaceFx", notes);
        var set = AssetDatabase.LoadMainAssetAtPath(RenderSetPath);
        foreach (var component in new[] { renderer, fx })
        {
            if (!component) continue;
            var so = new SerializedObject(component);
            var it = so.GetIterator();
            bool assigned = false;
            for (bool enter = true; it.NextVisible(enter); enter = false)
                if (it.propertyType == SerializedPropertyType.ObjectReference && set && it.type.Contains(set.GetType().Name))
                {
                    it.objectReferenceValue = set;
                    assigned = true;
                }
            so.ApplyModifiedPropertiesWithoutUndo();
            string name = component.GetType().Name;
            notes.Add(assigned ? $"FishRenderSet assigned to {name}" : $"FishRenderSet not assigned to {name} ({(set ? "no matching field" : "asset missing: " + RenderSetPath)})");
        }
        return string.Join(", ", notes);
    }

    static Component Ensure(GameObject root, string typeName, System.Collections.Generic.List<string> notes)
    {
        var type = System.Type.GetType($"{typeName}, {RenderingAssembly}");
        if (type == null) { notes.Add($"{typeName} not found (skipped)"); return null; }
        var c = root.GetComponent(type);
        if (!c) { c = root.AddComponent(type); notes.Add($"{type.Name} added"); }
        else notes.Add($"{type.Name} present");
        return c;
    }

    static T Load<T>(string path) where T : Object
    {
        var a = AssetDatabase.LoadAssetAtPath<T>(path);
        if (!a) throw new FileNotFoundException($"{typeof(T).Name} missing", path);
        return a;
    }
}
