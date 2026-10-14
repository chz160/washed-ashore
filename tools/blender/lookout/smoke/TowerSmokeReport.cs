using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using WashedAshore.Lookout.Editor;

/// <summary>
/// Smoke import of the lookout tower export, NOT of record: run in a throwaway project by
/// tools/blender/lookout/smoke_import.ps1 to learn what Unity's FBX importer does with the export before the first
/// candidate build exists. It reports only: the root's axes and scale, the rotation of every empty after import,
/// whether each COL_/CLIP_ proxy became the intended collider, where the movers' pivots landed against the manifest,
/// and whether a reimport and a plain prefab copy are stable. No figure from it goes into any result.
/// Batch: -executeMethod TowerSmoke.Run -smokeReport path/to/report.json
/// </summary>
public static class TowerSmoke
{
    const string Folder = TowerImportSettings.Folder;
    const string ModelPath = Folder + "/MC39_Lookout.fbx";
    const string ManifestPath = Folder + "/MC39_Lookout.manifest.json";
    const string CopyPath = Folder + "/SmokeCopy.prefab";

    [Serializable] class ManifestObject { public string name, role; public float[] pivot; }
    [Serializable] class Manifest { public int schema; public ManifestObject[] objects; }

    public static void Run()
    {
        var args = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(args, "-smokeReport");
        string path = i >= 0 && i + 1 < args.Length ? args[i + 1] : "tower-smoke-report.json";
        try { File.WriteAllText(path, Report()); EditorApplication.Exit(0); }
        catch (Exception e) { File.WriteAllText(path, "{\"failed\":" + Q(e.ToString()) + "}"); EditorApplication.Exit(1); }
    }

    static string F(float v) => v.ToString("0.#####", CultureInfo.InvariantCulture);
    static string V(Vector3 v) => $"[{F(v.x)},{F(v.y)},{F(v.z)}]";
    static string Q(string s) => "\"" + (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "").Replace("\n", "\\n") + "\"";
    static bool Posed(Transform t) => Quaternion.Angle(t.localRotation, Quaternion.identity) > 0.01f || (t.localScale - Vector3.one).sqrMagnitude > 1e-8f;
    static bool IsProxy(string n) => n.StartsWith("COL_", StringComparison.Ordinal) || n.StartsWith("CLIP_", StringComparison.Ordinal);

    public static string Report()
    {
        AssetDatabase.ImportAsset(ModelPath, ImportAssetOptions.ForceUpdate);
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (!model) throw new InvalidOperationException(ModelPath + " did not import as a model");
        var manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(ManifestPath));
        var import = TowerImportSettings.LoadReport(ModelPath);
        var nodes = model.GetComponentsInChildren<Transform>(true).Where(t => t != model.transform).ToList();
        var byName = nodes.GroupBy(t => t.name).ToDictionary(g => g.Key, g => g.First());
        var sb = new StringBuilder("{");
        sb.Append($"\"not_of_record\":true,\"unity\":{Q(Application.unityVersion)},\"model\":{Q(ModelPath)},\"manifest_schema\":{manifest.schema},");

        // 1. Root axes and scale.
        var bounds = new Bounds(); bool any = false;
        foreach (var r in model.GetComponentsInChildren<Renderer>(true)) { if (!any) { bounds = r.bounds; any = true; } else bounds.Encapsulate(r.bounds); }
        string Mark(string n) => byName.TryGetValue(n, out var t) ? V(t.position) : "null";
        sb.Append($"\"root\":{{\"name\":{Q(model.name)},\"euler\":{V(model.transform.localEulerAngles)},\"scale\":{V(model.transform.localScale)}," +
                  $"\"render_bounds_min\":{V(bounds.min)},\"render_bounds_max\":{V(bounds.max)},\"MARK_Front\":{Mark("MARK_Front")},\"MARK_Up\":{Mark("MARK_Up")}}},");

        // 2. Rotation of every empty (manifest role "empty") after import, and any other node that is rotated or scaled.
        var empties = manifest.objects.Where(o => o.role == "empty").ToList();
        var emptyRows = new List<string>(); int emptiesMissing = 0, emptiesRotated = 0; float worstEmptyOffset = 0f, worstEmptyAngle = 0f; string worstEmpty = "";
        foreach (var o in empties)
        {
            if (!byName.TryGetValue(o.name, out var t)) { emptiesMissing++; emptyRows.Add($"{{\"name\":{Q(o.name)},\"found\":false}}"); continue; }
            float offset = o.pivot != null && o.pivot.Length == 3 ? (t.position - new Vector3(o.pivot[0], o.pivot[1], o.pivot[2])).magnitude : -1f;
            float angle = Quaternion.Angle(t.localRotation, Quaternion.identity);
            worstEmptyOffset = Mathf.Max(worstEmptyOffset, offset);
            if (angle > worstEmptyAngle) { worstEmptyAngle = angle; worstEmpty = o.name; }
            if (Posed(t)) emptiesRotated++;
            // Every empty is listed, rotated or not.
            emptyRows.Add($"{{\"name\":{Q(o.name)},\"euler\":{V(t.localEulerAngles)},\"angle_from_identity_deg\":{F(angle)},\"scale\":{V(t.localScale)},\"position\":{V(t.position)},\"offset_from_manifest_m\":{F(offset)}}}");
        }
        var otherPosed = nodes.Where(t => !TowerImportSettings.IsBox(t.name) && Posed(t) && !empties.Any(o => o.name == t.name))
            .Select(t => $"{{\"name\":{Q(t.name)},\"euler\":{V(t.localEulerAngles)},\"scale\":{V(t.localScale)}}}").ToList();
        sb.Append($"\"empties\":{{\"in_manifest\":{empties.Count},\"missing\":{emptiesMissing},\"rotated_or_scaled\":{emptiesRotated},\"worst_offset_from_manifest_m\":{F(worstEmptyOffset)}," +
                  $"\"worst_angle_from_identity_deg\":{F(worstEmptyAngle)},\"worst_angle_node\":{Q(worstEmpty)},\"all\":[{string.Join(",", emptyRows)}]}},\"other_nodes_rotated_or_scaled\":[{string.Join(",", otherPosed)}],");

        // 3. Collision proxies: did each become the intended collider?
        var proxies = nodes.Where(t => IsProxy(t.name)).OrderBy(t => t.name, StringComparer.Ordinal).ToList();
        int box = 0, convex = 0, wrong = 0, rotatedBoxes = 0; var wrongRows = new List<string>();
        foreach (var t in proxies)
        {
            var cols = t.GetComponents<Collider>();
            bool wantBox = TowerImportSettings.IsBox(t.name), wantConvex = TowerImportSettings.IsConvex(t.name);
            bool ok = cols.Length == 1 && !cols[0].isTrigger && !t.GetComponent<MeshRenderer>()
                      && ((wantBox && cols[0] is BoxCollider) || (wantConvex && cols[0] is MeshCollider mc && mc.convex))
                      && t.gameObject.layer == (TowerImportSettings.IsClip(t.name) ? LayerMask.NameToLayer(TowerImportSettings.WorldBoundsLayer) : 0);
            if (ok && wantBox) { box++; if (Quaternion.Angle(t.localRotation, Quaternion.identity) > 0.01f) rotatedBoxes++; }
            else if (ok) convex++;
            else
            {
                wrong++;
                var mf = t.GetComponent<MeshFilter>();
                wrongRows.Add($"{{\"name\":{Q(t.name)},\"colliders\":{Q(string.Join("+", cols.Select(c => c.GetType().Name)))},\"layer\":{t.gameObject.layer},\"has_renderer\":{(t.GetComponent<MeshRenderer>() ? "true" : "false")}," +
                              $"\"mesh_vertices\":{(mf && mf.sharedMesh ? mf.sharedMesh.vertexCount : -1)},\"mesh_triangles\":{(mf && mf.sharedMesh ? mf.sharedMesh.triangles.Length / 3 : -1)}}}");
            }
        }
        sb.Append($"\"proxies\":{{\"nodes\":{proxies.Count},\"manifest_collision_objects\":{manifest.objects.Count(o => o.role == "collision")},\"box_ok\":{box},\"box_rotated_nodes\":{rotatedBoxes},\"convex_ok\":{convex},\"not_as_intended\":{wrong}," +
                  $"\"not_as_intended_list\":[{string.Join(",", wrongRows)}],\"import_report\":{(import == null ? "null" : JsonUtility.ToJson(import))}}},");

        // 4. Movers: where the hinge pivots landed against the manifest.
        var moverRows = new List<string>();
        foreach (var o in manifest.objects.Where(o => o.role == "moving"))
        {
            if (!byName.TryGetValue(o.name, out var t)) { moverRows.Add($"{{\"name\":{Q(o.name)},\"found\":false}}"); continue; }
            var want = o.pivot != null && o.pivot.Length == 3 ? new Vector3(o.pivot[0], o.pivot[1], o.pivot[2]) : Vector3.zero;
            moverRows.Add($"{{\"name\":{Q(o.name)},\"position\":{V(t.position)},\"manifest_pivot\":{V(want)},\"offset_m\":{F((t.position - want).magnitude)},\"euler\":{V(t.localEulerAngles)},\"scale\":{V(t.localScale)}}}");
        }
        sb.Append($"\"movers\":[{string.Join(",", moverRows)}],");

        // 5. Stability: a forced reimport gives the same hierarchy, and a plain prefab copy saves to the same bytes twice.
        string before = Signature(model);
        AssetDatabase.ImportAsset(ModelPath, ImportAssetOptions.ForceUpdate);
        model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        string after = Signature(model);
        byte[] first = SaveCopy(model), second = SaveCopy(model);
        int at = -1;
        for (int k = 0; k < Math.Min(first.Length, second.Length) && at < 0; k++) if (first[k] != second[k]) at = k;
        bool same = at < 0 && first.Length == second.Length;
        AssetDatabase.DeleteAsset(CopyPath);
        sb.Append($"\"stability\":{{\"reimport_same_hierarchy\":{(before == after ? "true" : "false")},\"plain_copy_prefab_identical_twice\":{(same ? "true" : "false")}," +
                  $"\"copy_bytes\":[{first.Length},{second.Length}],\"first_difference_at\":{at},\"note\":\"plain copy of the model only: no materials, LODGroup or gameplay components\"}},");

        // 6. Which export this was, and the ANCHOR_ empties (measurement anchors) by name, whatever their manifest role.
        string manifestSha;
        using (var sha = System.Security.Cryptography.SHA256.Create())
            manifestSha = BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(ManifestPath))).Replace("-", "").ToLowerInvariant();
        var anchors = nodes.Where(t => t.name.StartsWith("ANCHOR_", StringComparison.Ordinal)).ToList();
        bool frontAtPlusZ = byName.TryGetValue("MARK_Front", out var front) && Vector3.Dot(front.position.normalized, Vector3.forward) > 0.999f;
        sb.Append($"\"export\":{{\"manifest_sha256\":{Q(manifestSha)},\"schema\":{manifest.schema},\"manifest_empties\":{empties.Count},\"anchor_nodes\":{anchors.Count}," +
                  $"\"anchor_nodes_rotated_or_scaled\":{anchors.Count(Posed)},\"mark_front_at_plus_z\":{(frontAtPlusZ ? "true" : "false")}}},");

        // 7. The real TowerBuilder with inert stand-ins, when the smoke was started with -Builder (TowerSmokeBuilder.cs present).
        var builderType = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("TowerSmokeBuilder")).FirstOrDefault(t => t != null);
        string builder = builderType != null ? (string)builderType.GetMethod("Section").Invoke(null, null) : "null";
        sb.Append($"\"builder\":{builder},");

        sb.Append("\"limits\":\"Smoke import of a dev export in a throwaway project; not of record, no figure here is a result. It shows how the importer treats this " +
                  "export's empties and proxies, for the export named under 'export'. The 'stability' answers are a forced reimport and a plain prefab copy, NOT " +
                  "TowerBuilder.VerifyStableRebuild. " + (builderType != null
                      ? "The 'builder' section is the real TowerBuilder run against inert stand-ins: not the real rebuild check.\""
                      : "TowerBuilder did not run.\""));
        return sb.Append("}").ToString();
    }

    static string Signature(GameObject model)
    {
        var sb = new StringBuilder();
        foreach (var t in model.GetComponentsInChildren<Transform>(true))
            sb.Append(t.name).Append('|').Append(V(t.localPosition)).Append('|').Append(V(t.localEulerAngles)).Append('|')
              .Append(string.Join("+", t.GetComponents<Component>().Select(c => c ? c.GetType().Name : "missing"))).Append('\n');
        return sb.ToString();
    }

    static byte[] SaveCopy(GameObject model)
    {
        var copy = UnityEngine.Object.Instantiate(model);
        try
        {
            copy.name = "LookoutTowerSmoke";
            PrefabUtility.SaveAsPrefabAsset(copy, CopyPath, out bool ok);
            if (!ok) throw new InvalidOperationException("could not save " + CopyPath);
        }
        finally { UnityEngine.Object.DestroyImmediate(copy); }
        return File.ReadAllBytes(CopyPath);
    }
}
