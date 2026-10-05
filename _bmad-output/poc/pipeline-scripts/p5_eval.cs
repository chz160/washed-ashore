// P5: every vendor material (standalone .mat and every material referenced by vendor models/prefabs) must use a supported URP shader.
var bad = new System.Collections.Generic.List<string>();
var seen = new System.Collections.Generic.HashSet<UnityEngine.Material>();
int nullSlots = 0;
System.Action<UnityEngine.Material, string> check = (m, where) => {
  if (m == null) { nullSlots++; bad.Add("NULL material slot @ " + where); return; }
  if (!seen.Add(m)) return;
  var s = m.shader;
  bool urp = s != null && s.isSupported && s.name != "Hidden/InternalErrorShader"
    && (s.name.StartsWith("Universal Render Pipeline/") || s.name.StartsWith("Shader Graphs/"));
  if (!urp) bad.Add(m.name + " shader=" + (s == null ? "null" : s.name) + " supported=" + (s != null && s.isSupported) + " @ " + where);
};
var roots = new[]{ "Assets" };
var existing = System.Array.FindAll(roots, UnityEditor.AssetDatabase.IsValidFolder);
foreach (var g in UnityEditor.AssetDatabase.FindAssets("t:Material", existing)) {
  var p = UnityEditor.AssetDatabase.GUIDToAssetPath(g);
  foreach (var o in UnityEditor.AssetDatabase.LoadAllAssetsAtPath(p)) if (o is UnityEngine.Material) check((UnityEngine.Material)o, p);
}
int goCount = 0;
foreach (var g in UnityEditor.AssetDatabase.FindAssets("t:GameObject", existing)) {
  var p = UnityEditor.AssetDatabase.GUIDToAssetPath(g); goCount++;
  var go = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(p);
  foreach (var r in go.GetComponentsInChildren<UnityEngine.Renderer>(true))
    foreach (var m in r.sharedMaterials) check(m, p + ":" + r.name);
}
foreach (var r in UnityEngine.Object.FindObjectsByType<UnityEngine.Renderer>(UnityEngine.FindObjectsInactive.Include, UnityEngine.FindObjectsSortMode.None))
  foreach (var m in r.sharedMaterials) check(m, "scene:" + r.name);
foreach (var t in UnityEngine.Object.FindObjectsByType<UnityEngine.Terrain>(UnityEngine.FindObjectsSortMode.None)) {
  if (t.materialTemplate != null) check(t.materialTemplate, "terrain:" + t.name);
  foreach (var tp in t.terrainData.treePrototypes) if (tp.prefab != null) foreach (var r in tp.prefab.GetComponentsInChildren<UnityEngine.Renderer>(true)) foreach (var m in r.sharedMaterials) check(m, "treeProto:" + tp.prefab.name);
  foreach (var dp in t.terrainData.detailPrototypes) if (dp.prototype != null) foreach (var r in dp.prototype.GetComponentsInChildren<UnityEngine.Renderer>(true)) foreach (var m in r.sharedMaterials) check(m, "detailProto:" + dp.prototype.name);
}
var pipe =UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;
return "pipeline=" + (pipe == null ? "BUILT-IN(none)" : pipe.GetType().Name) + " materialsChecked=" + seen.Count + " modelsAndPrefabs=" + goCount
  + "\nNON_URP_COUNT=" + bad.Count + (bad.Count > 0 ? "\n" + string.Join("\n", bad) : "\nNON_URP: [] (empty)");
