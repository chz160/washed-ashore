var bad = new System.Collections.Generic.List<string>();
var seen = new System.Collections.Generic.HashSet<UnityEngine.Material>();
int nullSlots = 0, checkedMats = 0;
System.Action<UnityEngine.Material, string> chk = (m, src) => {
  if (m == null) { bad.Add(src + ":NULL"); return; }
  if (!seen.Add(m)) return;
  checkedMats++;
  var s = m.shader;
  if (s == null || s.name == "Hidden/InternalErrorShader" || !s.isSupported || !(s.name.StartsWith("Universal Render Pipeline/") || s.name.StartsWith("Shader Graphs/")))
    bad.Add(src + "|" + UnityEditor.AssetDatabase.GetAssetPath(m) + ":" + (s == null ? "null" : s.name) + (s != null && !s.isSupported ? "(unsupported)" : ""));
};
System.Action<UnityEngine.Renderer, string> chkR = (r, src) => {
  foreach (var m in r.sharedMaterials) { if (m == null) { nullSlots++; bad.Add(src + "/" + r.name + ":NULLSLOT"); } else chk(m, src + "/" + r.name); }
};
foreach (var r in UnityEngine.Object.FindObjectsByType<UnityEngine.Renderer>(UnityEngine.FindObjectsInactive.Include, UnityEngine.FindObjectsSortMode.None)) chkR(r, "scene");
var t = UnityEngine.Terrain.activeTerrain;
if (t != null) {
  if (t.materialTemplate != null) chk(t.materialTemplate, "terrainTemplate");
  var d = t.terrainData;
  foreach (var p in d.treePrototypes) if (p.prefab != null) foreach (var r in p.prefab.GetComponentsInChildren<UnityEngine.Renderer>(true)) chkR(r, "tree:" + p.prefab.name);
  foreach (var dp in d.detailPrototypes) if (dp.usePrototypeMesh && dp.prototype != null) foreach (var r in dp.prototype.GetComponentsInChildren<UnityEngine.Renderer>(true)) chkR(r, "detail:" + dp.prototype.name);
}
foreach (var guid in UnityEditor.AssetDatabase.FindAssets("t:Material", new[] { "Assets" })) {
  var path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
  foreach (var o in UnityEditor.AssetDatabase.LoadAllAssetsAtPath(path)) { var m = o as UnityEngine.Material; if (m != null) chk(m, "asset"); }
}
return "P5.1 checked=" + checkedMats + " offending=" + bad.Count + " P5.2 nullSlots=" + nullSlots + "\n" + string.Join("\n", bad.GetRange(0, System.Math.Min(bad.Count, 60)));
