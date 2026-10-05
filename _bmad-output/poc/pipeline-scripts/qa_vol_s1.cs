var sb = new System.Text.StringBuilder();
foreach (var v in UnityEngine.Object.FindObjectsByType<UnityEngine.Rendering.Volume>(UnityEngine.FindObjectsInactive.Include, UnityEngine.FindObjectsSortMode.None)) {
  var p = v.sharedProfile; sb.Append("volume ").Append(v.name).Append(" global=").Append(v.isGlobal).Append(" profile=").Append(p == null ? "null" : UnityEditor.AssetDatabase.GetAssetPath(p)).Append(" comps=");
  if (p != null) foreach (var c in p.components) sb.Append(c.GetType().Name).Append(c.active ? "+" : "-").Append(",");
  sb.Append("\n");
}
foreach (var guid in UnityEditor.AssetDatabase.FindAssets("t:VolumeProfile", new[] { "Assets" })) {
  var path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
  var p = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.VolumeProfile>(path);
  sb.Append("profileAsset ").Append(path).Append(" comps=");
  foreach (var c in p.components) {
    sb.Append(c.GetType().Name).Append(c.active ? "+" : "-");
    var dof = c as UnityEngine.Rendering.Universal.DepthOfField; if (dof != null) sb.Append("(mode=").Append(dof.mode.value).Append(",ovr=").Append(dof.mode.overrideState).Append(")");
    var pan = c as UnityEngine.Rendering.Universal.PaniniProjection; if (pan != null) sb.Append("(dist=").Append(pan.distance.value).Append(")");
    sb.Append(",");
  }
  sb.Append("\n");
}
var agents = UnityEngine.Object.FindObjectsByType<UnityEngine.AI.NavMeshAgent>(UnityEngine.FindObjectsInactive.Include, UnityEngine.FindObjectsSortMode.None);
var types = new System.Collections.Generic.HashSet<string>();
foreach (var a in agents) {
  var src = UnityEditor.PrefabUtility.GetCorrespondingObjectFromOriginalSource(a.gameObject);
  string n = src != null ? UnityEditor.AssetDatabase.GetAssetPath(src) : a.gameObject.name;
  types.Add(n);
  var an = a.GetComponentInChildren<UnityEngine.Animator>();
  sb.Append("agent ").Append(a.name).Append(" src=").Append(n).Append(" animator=").Append(an == null ? "none" : (an.runtimeAnimatorController == null ? "noController" : an.runtimeAnimatorController.name)).Append("\n");
}
sb.Append("S1.1 agents=").Append(agents.Length).Append(" distinctSources=").Append(types.Count).Append("\n");
sb.Append("S1.2 navmeshVerts=").Append(UnityEngine.AI.NavMesh.CalculateTriangulation().vertices.Length).Append("\n");
sb.Append("windZones=").Append(UnityEngine.Object.FindObjectsByType<UnityEngine.WindZone>(UnityEngine.FindObjectsInactive.Include, UnityEngine.FindObjectsSortMode.None).Length).Append("\n");
sb.Append("productName=").Append(UnityEditor.PlayerSettings.productName).Append(" company=").Append(UnityEditor.PlayerSettings.companyName).Append(" splashShow=").Append(UnityEditor.PlayerSettings.SplashScreen.show);
return sb.ToString();
