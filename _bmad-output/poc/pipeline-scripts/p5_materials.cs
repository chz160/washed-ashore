// Create shared URP/Lit materials for the MegaKit and remap every FBX's embedded material to them.
var root = "Assets/ThirdParty/Quaternius/NatureMegaKit";
var matDir = root + "/Materials";
if (!UnityEditor.AssetDatabase.IsValidFolder(matDir)) UnityEditor.AssetDatabase.CreateFolder(root, "Materials");
var lit = UnityEngine.Shader.Find("Universal Render Pipeline/Lit");
if (lit == null) return "ERROR: URP/Lit shader not found";
// name -> albedo, normal, alphaClip
var spec = new System.Collections.Generic.Dictionary<string, string[]> {
  {"Bark_NormalTree",   new[]{"Bark_NormalTree","Bark_NormalTree_Normal","0"}},
  {"Bark_DeadTree",     new[]{"Bark_DeadTree","Bark_DeadTree_Normal","0"}},
  {"Bark_TwistedTree",  new[]{"Bark_TwistedTree","Bark_TwistedTree_Normal","0"}},
  {"Leaves_NormalTree", new[]{"Leaves_NormalTree_C","","1"}},
  {"Leaves_TwistedTree",new[]{"Leaves_TwistedTree_C","","1"}},
  {"Leaves_Pine",       new[]{"Leaf_Pine_C","","1"}},
  {"Leaves",            new[]{"Leaves","","1"}},
  {"Flowers",           new[]{"Flowers","","1"}},
  {"Grass",             new[]{"Grass","","1"}},
  {"Mushrooms",         new[]{"Mushrooms","","0"}},
  {"PathRocks",         new[]{"PathRocks_Diffuse","","0"}},
  {"Rocks",             new[]{"Rocks_Diffuse","","0"}},
};
var log = new System.Text.StringBuilder();
// normal maps must be imported as NormalMap
foreach (var kv in spec) {
  if (kv.Value[1] == "") continue;
  var ti = (UnityEditor.TextureImporter)UnityEditor.AssetImporter.GetAtPath(root + "/Textures/" + kv.Value[1] + ".png");
  if (ti != null && ti.textureType != UnityEditor.TextureImporterType.NormalMap) { ti.textureType = UnityEditor.TextureImporterType.NormalMap; ti.SaveAndReimport(); }
}
var mats = new System.Collections.Generic.Dictionary<string, UnityEngine.Material>();
foreach (var kv in spec) {
  var path = matDir + "/M_" + kv.Key + ".mat";
  var m = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Material>(path);
  if (m == null) { m = new UnityEngine.Material(lit); UnityEditor.AssetDatabase.CreateAsset(m, path); }
  m.shader = lit;
  var alb = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Texture2D>(root + "/Textures/" + kv.Value[0] + ".png");
  if (alb == null) log.AppendLine("WARN missing albedo " + kv.Value[0]);
  m.SetTexture("_BaseMap", alb); m.SetColor("_BaseColor", UnityEngine.Color.white);
  m.SetFloat("_Smoothness", 0.1f); m.SetFloat("_Metallic", 0f);
  if (kv.Value[1] != "") {
    m.SetTexture("_BumpMap", UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Texture2D>(root + "/Textures/" + kv.Value[1] + ".png"));
    m.EnableKeyword("_NORMALMAP");
  }
  bool clip = kv.Value[2] == "1";
  m.SetFloat("_AlphaClip", clip ? 1f : 0f); m.SetFloat("_Cutoff", 0.5f);
  m.SetFloat("_Cull", clip ? 0f : 2f);           // two-sided foliage
  if (clip) { m.EnableKeyword("_ALPHATEST_ON"); m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest; m.SetOverrideTag("RenderType", "TransparentCutout"); }
  else { m.DisableKeyword("_ALPHATEST_ON"); m.renderQueue = -1; m.SetOverrideTag("RenderType", "Opaque"); }
  m.enableInstancing = true;                       // needed for terrain detail mesh instancing
  UnityEditor.EditorUtility.SetDirty(m);
  mats[kv.Key] = m;
}
UnityEditor.AssetDatabase.SaveAssets();
// remap embedded FBX materials
int fbx = 0;
foreach (var guid in UnityEditor.AssetDatabase.FindAssets("t:Model", new[]{ root + "/FBX" })) {
  var p = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
  var mi = (UnityEditor.ModelImporter)UnityEditor.AssetImporter.GetAtPath(p);
  mi.materialImportMode = UnityEditor.ModelImporterMaterialImportMode.ImportStandard;
  foreach (var o in UnityEditor.AssetDatabase.LoadAllAssetsAtPath(p)) {
    var em = o as UnityEngine.Material; if (em == null) continue;
    var key = em.name;
    if (!mats.ContainsKey(key)) {
      // Bush_Common has a stray "Material" slot; fall back to the file's leaf material
      key = key == "Material" ? "Leaves_TwistedTree" : null;
      log.AppendLine("REMAP-FALLBACK " + p + " : " + em.name + " -> " + key);
      if (key == null) continue;
    }
    mi.AddRemap(new UnityEditor.AssetImporter.SourceAssetIdentifier(typeof(UnityEngine.Material), em.name), mats[key]);
  }
  mi.SaveAndReimport(); fbx++;
}
log.AppendLine("materials=" + mats.Count + " fbxRemapped=" + fbx);
return log.ToString();
