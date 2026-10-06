using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using WashedAshore.Gameplay;
using WashedAshore.World;

// Spec B6 fixture: build the barrier, backstop and clamp from a MapConfig copy with the north
// line shifted, so the B5 sweep (which reads the line from the scene) can run against it,
// then rebuild from the real MapConfig. The real asset is never edited.
//   1. Unity.exe -batchmode -quit -projectPath <p> -executeMethod WorldBoundsShiftCheck.PrepareCli   (TestResults/b6-shift.json)
//   2. unity test <p> --mode PlayMode --filter WashedAshore.Tests.PlayMode.WorldBoundsSweepTests --output TestResults/bb-b6-sweep.xml
//   3. Unity.exe -batchmode -quit -projectPath <p> -executeMethod WorldBoundsShiftCheck.RestoreCli  (TestResults/b6-restore.txt)
// In an open Editor, eval Prepare(100f) / Restore() instead.
public static class WorldBoundsShiftCheck
{
    public const string ScenePath = "Assets/Scenes/World.unity";
    public const string CopyPath = "Assets/World/MapConfig_B6Shift.asset";
    static string ReportPath => Path.Combine(Path.GetDirectoryName(Application.dataPath), "TestResults", "b6-shift.json");

    [System.Serializable]
    class Report
    {
        public float shift, realLineZ, shiftedLineZ;
        public float clampLineZ, backstopCenterZ, barrierCenterZ;
        public float backstopMoved, barrierMoved, clampMoved;
        public bool ok;
        public float beforeBackstopZ, beforeBarrierZ;
        public string beforeBackstopHash, beforeBarrierHash, shiftedBackstopHash, shiftedBarrierHash;
        public bool copyDiffersOnlyInNorthLine;
        public string backstopBuild, barrierBuild, treeBand;
    }

    public static string Prepare(float shift = 100f)
    {
        OpenWorld();
        var real = AssetDatabase.LoadAssetAtPath<MapConfig>(WorldBoundsBuilder.MapConfigPath);
        if (!real) throw new FileNotFoundException("No MapConfig", WorldBoundsBuilder.MapConfigPath);
        var before = Measure();

        AssetDatabase.DeleteAsset(CopyPath);
        var copy = Object.Instantiate(real);
        copy.northLineZ += shift;
        // qa-2 (a): the copy must differ from the real config in northLineZ only.
        copy.northLineZ -= shift;
        bool sameOtherwise = JsonUtility.ToJson(copy) == JsonUtility.ToJson(real);
        copy.northLineZ += shift;
        AssetDatabase.CreateAsset(copy, CopyPath);
        AssetDatabase.SaveAssets();

        var r = new Report { shift = shift, realLineZ = real.northLineZ, shiftedLineZ = copy.northLineZ, beforeBackstopZ = before.backstop, beforeBarrierZ = before.barrier,
                           beforeBackstopHash = Fingerprint(WorldBoundsBuilder.RootName), beforeBarrierHash = Fingerprint(BarrierRootName),
                           copyDiffersOnlyInNorthLine = sameOtherwise };
        // The art clear band follows the real line only; clear the trees in the shifted band (restored by Restore).
        r.treeBand = WorldBoundsShiftTrees.ClearBand(copy.northLineZ);
        r.barrierBuild = BuildBarrier(copy);
        r.backstopBuild = WorldBoundsBuilder.Build(copy, save: false);
        var after = Measure();
        r.clampLineZ = after.clamp;
        r.backstopCenterZ = after.backstop;
        r.barrierCenterZ = after.barrier;
        r.clampMoved = after.clamp - before.clamp;
        r.backstopMoved = after.backstop - before.backstop;
        r.barrierMoved = after.barrier - before.barrier;
        r.shiftedBackstopHash = Fingerprint(WorldBoundsBuilder.RootName);
        r.shiftedBarrierHash = Fingerprint(BarrierRootName);
        // Barrier props are laid out on the terrain they land on, so allow them a little drift.
        r.ok = sameOtherwise && Near(r.clampMoved, shift, 0.5f) && Near(r.backstopMoved, shift, 0.5f) && Near(r.barrierMoved, shift, 5f);
        SaveWorld();

        Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
        File.WriteAllText(ReportPath, JsonUtility.ToJson(r, true));
        return $"B6 prepare: ok={r.ok} shift={shift} moved clamp={r.clampMoved:F2} backstop={r.backstopMoved:F2} barrier={r.barrierMoved:F2} {r.treeBand} report={ReportPath}";
    }

    // Batchmode entry points (-executeMethod takes no arguments): result goes to the Editor log and TestResults.
    public static void PrepareCli() => Debug.Log(Prepare(100f));
    public static void RestoreCli()
    {
        string r = Restore();
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(ReportPath), "b6-restore.txt"), r);
        Debug.Log(r);
    }

    // qa-2's evidence-of-record chain (AF1) shifts the REAL MapConfig and rebuilds everything with the owners'
    // builds; these two only edit the asset (with a guard file holding the original value), nothing else:
    //   Unity.exe -batchmode -quit -projectPath <p> -executeMethod WorldBoundsShiftCheck.ShiftRealConfigCli   (+100 m)
    //   Unity.exe -batchmode -quit -projectPath <p> -executeMethod WorldBoundsShiftCheck.RevertRealConfigCli
    static string RealShiftPath => Path.Combine(Path.GetDirectoryName(ReportPath), "b6-real-shift.json");

    [System.Serializable]
    class RealShift { public float originalNorthLineZ, shiftedNorthLineZ; }

    public static void ShiftRealConfigCli()
    {
        var real = AssetDatabase.LoadAssetAtPath<MapConfig>(WorldBoundsBuilder.MapConfigPath);
        if (File.Exists(RealShiftPath)) throw new IOException($"{RealShiftPath} exists: revert the previous shift first");
        var s = new RealShift { originalNorthLineZ = real.northLineZ, shiftedNorthLineZ = real.northLineZ + 100f };
        Directory.CreateDirectory(Path.GetDirectoryName(RealShiftPath));
        File.WriteAllText(RealShiftPath, JsonUtility.ToJson(s, true));
        real.northLineZ = s.shiftedNorthLineZ;
        EditorUtility.SetDirty(real);
        AssetDatabase.SaveAssets();
        Debug.Log($"B6 real shift: MapConfig.northLineZ {s.originalNorthLineZ:F2} -> {real.northLineZ:F2} (guard {RealShiftPath})");
    }

    public static void RevertRealConfigCli()
    {
        if (!File.Exists(RealShiftPath)) throw new FileNotFoundException("No real-shift guard file; nothing to revert", RealShiftPath);
        var s = JsonUtility.FromJson<RealShift>(File.ReadAllText(RealShiftPath));
        var real = AssetDatabase.LoadAssetAtPath<MapConfig>(WorldBoundsBuilder.MapConfigPath);
        real.northLineZ = s.originalNorthLineZ;
        EditorUtility.SetDirty(real);
        AssetDatabase.SaveAssets();
        File.Delete(RealShiftPath);
        Debug.Log($"B6 real revert: MapConfig.northLineZ back to {real.northLineZ:F2}");
    }

    public static string Restore()
    {
        OpenWorld();
        var real = AssetDatabase.LoadAssetAtPath<MapConfig>(WorldBoundsBuilder.MapConfigPath);
        string barrier = BuildBarrier(real);
        string backstop = WorldBoundsBuilder.Build(real, save: false);
        string trees = WorldBoundsShiftTrees.RestoreBand();
        SaveWorld();
        AssetDatabase.DeleteAsset(CopyPath);
        var m = Measure();
        // Compare with the pre-shift state recorded by Prepare.
        var pre = File.Exists(ReportPath) ? JsonUtility.FromJson<Report>(File.ReadAllText(ReportPath)) : null;
        string backstopHash = Fingerprint(WorldBoundsBuilder.RootName), barrierHash = Fingerprint(BarrierRootName);
        bool sameAsBefore = pre != null && backstopHash == pre.beforeBackstopHash && barrierHash == pre.beforeBarrierHash
                            && Near(m.backstop, pre.beforeBackstopZ, 0.01f) && Near(m.barrier, pre.beforeBarrierZ, 0.01f);
        bool ok = Mathf.Abs(m.clamp - real.northLineZ) < 0.01f && sameAsBefore && trees.Contains("hashOk=True");
        return $"B6 restore: ok={ok} clampLineZ={m.clamp:F2} realLineZ={real.northLineZ:F2} " +
               $"backstopZ={m.backstop:F2} (before {pre?.beforeBackstopZ:F2}) barrierZ={m.barrier:F2} (before {pre?.beforeBarrierZ:F2}) " +
               $"backstopHash={backstopHash} (before {pre?.beforeBackstopHash}) barrierHash={barrierHash} (before {pre?.beforeBarrierHash}) " +
               $"copyDeleted={!File.Exists(CopyPath)} | {trees} | {backstop} | {barrier}";
    }

    // Level-designer's barrier build entry point (B2); it never saves the scene.
    static string BuildBarrier(MapConfig cfg) => BarrierBuilder.Build(cfg);

    static string BarrierRootName => BarrierBuilder.RootName;

    static (float clamp, float backstop, float barrier) Measure()
    {
        Physics.SyncTransforms();
        var root = GameObject.Find(WorldBoundsBuilder.RootName);
        float clamp = root ? root.GetComponent<WorldBoundsClamp>().Rule.LineZAt(0f) : float.NaN;
        float backstop = root ? CenterZ(root) : float.NaN;
        var barrier = GameObject.Find(BarrierRootName);
        return (clamp, backstop, barrier ? CenterZ(barrier) : float.NaN);
    }

    static float CenterZ(GameObject root)
    {
        Bounds? b = null;
        foreach (var c in root.GetComponentsInChildren<Collider>())
        {
            if (b == null) b = c.bounds;
            else { var x = b.Value; x.Encapsulate(c.bounds); b = x; }
        }
        foreach (var r in root.GetComponentsInChildren<Renderer>())
        {
            if (b == null) b = r.bounds;
            else { var x = b.Value; x.Encapsulate(r.bounds); b = x; }
        }
        return b?.center.z ?? root.transform.position.z;
    }

    /// <summary>FNV-1a over every collider and renderer under the root: name, world bounds centre and size (mm).</summary>
    static string Fingerprint(string rootName)
    {
        var root = GameObject.Find(rootName);
        if (!root) return "missing";
        ulong h = 1469598103934665603UL;
        int n = 0;
        void Mix(string s) { foreach (char ch in s) { h ^= ch; h *= 1099511628211UL; } }
        void Box(string name, Bounds b) { n++; Mix(name); Mix(b.center.ToString("F3")); Mix(b.size.ToString("F3")); }
        foreach (var c in root.GetComponentsInChildren<Collider>(true)) Box(c.name, c.bounds);
        foreach (var r in root.GetComponentsInChildren<Renderer>(true)) Box(r.name, r.bounds);
        var clamp = root.GetComponent<WorldBoundsClamp>();
        if (clamp) Mix(string.Join(";", System.Array.ConvertAll(clamp.Rule.Line, v => v.ToString("F3"))));
        return $"{n}:{h:X16}";
    }

    static bool Near(float a, float b, float tolerance) => Mathf.Abs(a - b) < tolerance;

    static void OpenWorld()
    {
        if (EditorSceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
    }

    static void SaveWorld()
    {
        var scene = EditorSceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }
}
