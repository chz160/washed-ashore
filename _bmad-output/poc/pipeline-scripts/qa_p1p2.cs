var sb = new System.Text.StringBuilder();
sb.Append("P1.4 defaultRP=").Append(UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline == null ? "null" : UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline.GetType().Name).Append("\n");
var q = UnityEngine.QualitySettings.renderPipeline;
sb.Append("P1.5 qualityRP=").Append(q == null ? "null" : q.GetType().Name).Append("\n");
sb.Append("P2.1 scenes=").Append(string.Join("|", System.Array.ConvertAll(UnityEditor.EditorBuildSettings.scenes, s => s.path + ":" + s.enabled))).Append("\n");
sb.Append("P2.2 canvas=").Append(UnityEngine.Object.FindObjectsByType<UnityEngine.Canvas>(UnityEngine.FindObjectsInactive.Include, UnityEngine.FindObjectsSortMode.None).Length)
  .Append(" uidoc=").Append(UnityEngine.Object.FindObjectsByType<UnityEngine.UIElements.UIDocument>(UnityEngine.FindObjectsInactive.Include, UnityEngine.FindObjectsSortMode.None).Length)
  .Append(" eventsys=").Append(UnityEngine.Object.FindObjectsByType<UnityEngine.EventSystems.EventSystem>(UnityEngine.FindObjectsInactive.Include, UnityEngine.FindObjectsSortMode.None).Length).Append("\n");
var scn = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
sb.Append("P2.3 activeScene=").Append(scn.path).Append(" roots=");
foreach (var g in scn.GetRootGameObjects()) {
  sb.Append(g.name).Append("[tag=").Append(g.tag);
  if (g.GetComponent<UnityEngine.CharacterController>() != null) sb.Append(",CC");
  if (g.GetComponent<UnityEngine.Terrain>() != null) sb.Append(",Terrain");
  var l = g.GetComponent<UnityEngine.Light>(); if (l != null) sb.Append(",Light:").Append(l.type);
  sb.Append("] ");
}
sb.Append("\nP2.3 playersTagged=").Append(UnityEngine.GameObject.FindGameObjectsWithTag("Player").Length);
sb.Append("\nP2.4 splashLogos=").Append(UnityEditor.PlayerSettings.SplashScreen.logos == null ? 0 : UnityEditor.PlayerSettings.SplashScreen.logos.Length);
return sb.ToString();
