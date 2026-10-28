using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace WashedAshore.Lookout.Editor
{
    /// <summary>
    /// #64 option A, Step 2 (prototype, lives only in optionA.patch): swaps the tower's flight side walls
    /// (CLIP_CVX_Flight*) and landing decks (COL_CVX_Landing_*) from convex MeshColliders to primitives, and rounds the
    /// ends of the thin flight walls with a capsule at each end. It edits the saved LookoutTower.prefab in place
    /// (LoadPrefabContents), so World.unity is not touched and the builder, FBX import and textures are not rerun.
    /// A hull that is not an exact rectangular box (BoxFrame says no) stays a MeshCollider and is listed in the log.
    /// Run: Unity -batchmode -projectPath E:\GitHub\washed-ashore -executeMethod WashedAshore.Lookout.Editor.TowerPrimitiveColliders64.BuildPrefabOnly -quit
    /// </summary>
    public static class TowerPrimitiveColliders64
    {
        public const string LogPath = "Logs/tower-primitive-colliders-64.txt";
        const float Tolerance = 1e-4f, ScaleTolerance = 1e-3f;
        const float RailMaxThickness = 0.15f, RailMinLengthRatio = 4f;

        public static void BuildPrefabOnly()
        {
            try
            {
                string log = Apply();
                Write(log + "\ndone\n");
                Debug.Log(log);
                EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Write("FAILED: " + e);
                Debug.LogError(e);
                EditorApplication.Exit(1);
            }
        }

        static void Write(string text)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(LogPath)));
            File.WriteAllText(LogPath, text);
        }

        static bool Wanted(string name) =>
            name.StartsWith("CLIP_CVX_Flight", StringComparison.Ordinal) || name.StartsWith("COL_CVX_Landing_", StringComparison.Ordinal);

        public static string Apply()
        {
            var log = new StringBuilder($"TowerPrimitiveColliders64 {DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}\n");
            var root = PrefabUtility.LoadPrefabContents(WalkableTowerBuilder.PrefabPath);
            int boxes = 0, rails = 0, kept = 0;
            try
            {
                var nodes = root.GetComponentsInChildren<MeshCollider>(true)
                    .Where(m => Wanted(m.gameObject.name)).OrderBy(m => m.gameObject.name, StringComparer.Ordinal).ToList();
                log.AppendLine($"mesh colliders considered: {nodes.Count}");
                foreach (var mc in nodes)
                {
                    var t = mc.transform; string name = t.name;
                    var mesh = mc.sharedMesh;
                    if (!mesh) { kept++; log.AppendLine($"{name}: kept (no mesh)"); continue; }
                    if ((t.lossyScale - Vector3.one).magnitude > ScaleTolerance) { kept++; log.AppendLine($"{name}: kept (scale {t.lossyScale})"); continue; }
                    var corners = new List<Vector3>();
                    foreach (var v in mesh.vertices) if (!corners.Any(o => (o - v).sqrMagnitude < Tolerance * Tolerance)) corners.Add(v);
                    if (!WalkableTowerBuilder.BoxFrame(corners, out var centre, out var rot, out var size))
                    {
                        kept++; log.AppendLine($"{name}: kept as mesh, not a rectangular box ({corners.Count} corners, bounds size {mesh.bounds.size.ToString("F3")})");
                        continue;
                    }
                    var filter = t.GetComponent<MeshFilter>();
                    UnityEngine.Object.DestroyImmediate(mc);
                    if (filter) UnityEngine.Object.DestroyImmediate(filter);
                    t.localPosition += t.localRotation * centre;
                    t.localRotation *= rot;

                    int iT = 0, iL = 0;
                    for (int i = 1; i < 3; i++) { if (size[i] < size[iT]) iT = i; if (size[i] > size[iL]) iL = i; }
                    int iH = 3 - iT - iL;
                    bool rail = name.StartsWith("CLIP_CVX_Flight", StringComparison.Ordinal)
                        && size[iT] <= RailMaxThickness && size[iL] >= RailMinLengthRatio * size[iT] && iT != iL;
                    var bc = t.gameObject.AddComponent<BoxCollider>(); bc.center = Vector3.zero;
                    if (!rail)
                    {
                        bc.size = size; boxes++;
                        log.AppendLine($"{name}: box {size.ToString("F3")}");
                        continue;
                    }
                    float th = size[iT], half = size[iL] * 0.5f - th * 0.5f;
                    var shorter = size; shorter[iL] = size[iL] - th; bc.size = shorter;
                    foreach (float sign in new[] { -1f, 1f })
                    {
                        var cap = t.gameObject.AddComponent<CapsuleCollider>();
                        var c = Vector3.zero; c[iL] = sign * half; cap.center = c;
                        cap.direction = iH; cap.radius = th * 0.5f; cap.height = Mathf.Max(size[iH], th);
                    }
                    rails++;
                    log.AppendLine($"{name}: rail box {shorter.ToString("F3")} + 2 capsules r {th * 0.5f:F3} h {Mathf.Max(size[iH], th):F3} (full size {size.ToString("F3")}, long axis {"xyz"[iL]}, height axis {"xyz"[iH]})");
                }
                log.AppendLine($"primitives: {boxes} box, {rails} rounded rail, kept {kept} as mesh");
                PrefabUtility.SaveAsPrefabAsset(root, WalkableTowerBuilder.PrefabPath, out bool saved);
                if (!saved) throw new InvalidOperationException("could not save " + WalkableTowerBuilder.PrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            AssetDatabase.SaveAssets();
            log.AppendLine("prefab: " + WalkableTowerBuilder.PrefabPath);
            return log.ToString();
        }
    }
}
