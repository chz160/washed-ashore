// Report hierarchy, transforms, bounds and materials of imported vendor models.
var sb = new System.Text.StringBuilder();
foreach (var guid in UnityEditor.AssetDatabase.FindAssets("t:Model", new[]{ "Assets/ThirdParty/Quaternius" })) {
  var p = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
  var go = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(p);
  var rs = go.GetComponentsInChildren<UnityEngine.Renderer>(true);
  var b = new UnityEngine.Bounds(); bool first = true;
  foreach (var r in rs) { if (first) { b = r.bounds; first = false; } else b.Encapsulate(r.bounds); }
  sb.Append(System.IO.Path.GetFileNameWithoutExtension(p)).Append(" children=").Append(go.transform.childCount)
    .Append(" rootRenderer=").Append(go.GetComponent<UnityEngine.Renderer>() != null)
    .Append(" size=").Append(b.size.ToString("F2"));
  foreach (var r in rs) {
    var t = r.transform;
    sb.Append(" | ").Append(r.GetType().Name).Append(":").Append(t.name)
      .Append(" lp=").Append(t.localPosition.ToString("F2")).Append(" lr=").Append(t.localEulerAngles.ToString("F0")).Append(" ls=").Append(t.localScale.ToString("F2"))
      .Append(" mats=[");
    foreach (var m in r.sharedMaterials) sb.Append(m == null ? "NULL" : m.name + "(" + m.shader.name + ")").Append(",");
    sb.Append("]");
  }
  var clips = new System.Collections.Generic.List<string>();
  foreach (var o in UnityEditor.AssetDatabase.LoadAllAssetsAtPath(p)) { var c = o as UnityEngine.AnimationClip; if (c != null && !c.name.StartsWith("__preview")) clips.Add(c.name); }
  if (clips.Count > 0) sb.Append(" clips=").Append(string.Join(",", clips));
  sb.AppendLine();
}
return sb.ToString();
