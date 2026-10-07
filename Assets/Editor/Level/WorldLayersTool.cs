using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using WashedAshore.World;

// Platform-tools (water W4): moves the saved World scene's invisible barrier extensions onto the WorldBounds
// layer and sets the wildlife NavMeshSurface's layer mask to leave out Water, without rebuilding the barrier or
// re-baking the NavMesh (BarrierBuilder and WildlifePlacer do both on their next run). Re-runnable.
// Headless: unity run <project> -- -executeMethod WorldLayersTool.ApplyAndSave
public static class WorldLayersTool
{
    public const string ScenePath = "Assets/Scenes/World.unity";
    public const string ReportPath = "TestResults/water-w4-relayer.txt";

    public static string LastReport = "not run";

    public static void ApplyAndSave()
    {
        if (EditorSceneManager.GetActiveScene().path != ScenePath) EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        LastReport = Apply();
        var scene = EditorSceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene)) throw new System.InvalidOperationException("Could not save " + ScenePath);
        System.IO.File.WriteAllText(ReportPath, LastReport + " saved=true\n");
        Debug.Log("WorldLayersTool: " + LastReport);
    }

    public static string Apply()
    {
        int bounds = WorldLayers.WorldBounds, water = WorldLayers.Water;
        if (water != WorldLayers.BuiltInWater) throw new System.InvalidOperationException($"Water is layer {water}, expected {WorldLayers.BuiltInWater}");
        var barrier = GameObject.Find(BarrierBuilder.RootName);
        if (!barrier) throw new System.InvalidOperationException($"No {BarrierBuilder.RootName} root in the open scene");

        int moved = 0, already = 0;
        foreach (var t in barrier.GetComponentsInChildren<Transform>(true))
        {
            if (!BarrierBuilder.ExtensionNames.Contains(t.name)) continue;
            if (t.gameObject.layer == bounds) { already++; continue; }
            Undo.RecordObject(t.gameObject, "WorldBounds layer");
            t.gameObject.layer = bounds;
            moved++;
        }
        if (moved + already == 0) throw new System.InvalidOperationException("No barrier extensions found");

        // NavMeshSurface lives in Unity.AI.Navigation, which this assembly doesn't reference: set it by its serialized field.
        int surfaces = 0, navMask = ~(1 << water);
        foreach (var mb in Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include))
        {
            if (!mb || mb.GetType().FullName != "Unity.AI.Navigation.NavMeshSurface") continue;
            var so = new SerializedObject(mb);
            var p = so.FindProperty("m_LayerMask");
            if (p == null) throw new System.InvalidOperationException("NavMeshSurface has no m_LayerMask");
            p.intValue = navMask;
            so.ApplyModifiedPropertiesWithoutUndo();
            surfaces++;
        }
        return $"WorldLayers: water={water} worldBounds={bounds} extensionsMoved={moved} extensionsAlready={already} " +
               $"navMeshSurfaces={surfaces} navMeshLayerMask=0x{navMask:X8}";
    }
}
