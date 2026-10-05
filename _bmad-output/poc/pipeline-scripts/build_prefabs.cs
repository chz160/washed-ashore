// Build terrain-ready prefabs from MegaKit models: trees/rocks/bushes (tree prototypes) and grass/flowers (detail prototypes).
var root = "Assets/ThirdParty/Quaternius/NatureMegaKit";
var outRoot = root + "/Prefabs";
System.Func<string, string> ensure = (sub) => {
  if (!UnityEditor.AssetDatabase.IsValidFolder(outRoot)) UnityEditor.AssetDatabase.CreateFolder(root, "Prefabs");
  if (!UnityEditor.AssetDatabase.IsValidFolder(outRoot + "/" + sub)) UnityEditor.AssetDatabase.CreateFolder(outRoot, sub);
  return outRoot + "/" + sub;
};
// source, prefab name, category, collider radius (0 = none)
var items = new System.Collections.Generic.List<object[]>();
for (int i = 1; i <= 5; i++) {
  items.Add(new object[]{"CommonTree_" + i, "CommonTree_" + i, "Trees", 0.35f});
  items.Add(new object[]{"Pine_" + i, "Pine_" + i, "Trees", 0.35f});
  items.Add(new object[]{"TwistedTree_" + i, "TwistedTree_" + i, "Trees", 0.9f});
  items.Add(new object[]{"DeadTree_" + i, "DeadTree_" + i, "Trees", 0.5f});
}
for (int i = 1; i <= 3; i++) items.Add(new object[]{"Rock_Medium_" + i, "Rock_Medium_" + i, "Rocks", -1f});
items.Add(new object[]{"Bush_Common", "Bush_Common", "Bushes", 0f});
items.Add(new object[]{"Bush_Common_Flowers", "Bush_Common_Flowers", "Bushes", 0f});
items.Add(new object[]{"Fern_1", "Bush_Fern_1", "Bushes", 0f});
items.Add(new object[]{"Plant_1_Big", "Bush_Plant_1_Big", "Bushes", 0f});
foreach (var n in new[]{"Grass_Common_Short","Grass_Common_Tall","Grass_Wispy_Short","Grass_Wispy_Tall","Flower_3_Group","Flower_4_Group","Clover_1","Plant_7"})
  items.Add(new object[]{n, n, "Details", 0f});
var sb = new System.Text.StringBuilder();
foreach (var it in items) {
  var src = (string)it[0]; var name = (string)it[1]; var cat = (string)it[2]; var rad = (float)it[3];
  var model = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(root + "/FBX/" + src + ".fbx");
  if (model == null) { sb.AppendLine("MISSING " + src); continue; }
  var mf0 = model.GetComponent<UnityEngine.MeshFilter>(); var mr0 = model.GetComponent<UnityEngine.MeshRenderer>();
  var go = new UnityEngine.GameObject(name);
  go.AddComponent<UnityEngine.MeshFilter>().sharedMesh = mf0.sharedMesh;
  var mr = go.AddComponent<UnityEngine.MeshRenderer>(); mr.sharedMaterials = mr0.sharedMaterials;
  var b = mf0.sharedMesh.bounds;
  if (cat != "Details") {
    var lod = go.AddComponent<UnityEngine.LODGroup>();
    lod.SetLODs(new[]{ new UnityEngine.LOD(cat == "Trees" ? 0.01f : 0.03f, new UnityEngine.Renderer[]{ mr }) });
    lod.RecalculateBounds();
  }
  if (rad != 0f) {
    var cc = go.AddComponent<UnityEngine.CapsuleCollider>();
    if (rad < 0f) { rad = UnityEngine.Mathf.Min(b.extents.x, b.extents.z) * 0.9f; cc.center = new UnityEngine.Vector3(b.center.x, b.center.y, b.center.z); cc.height = UnityEngine.Mathf.Max(b.size.y, rad * 2f); }
    else { cc.center = new UnityEngine.Vector3(0f, b.size.y * 0.5f, 0f); cc.height = b.size.y; }
    cc.radius = rad;
  }
  var path = ensure(cat) + "/" + name + ".prefab";
  UnityEditor.PrefabUtility.SaveAsPrefabAsset(go, path);
  UnityEngine.Object.DestroyImmediate(go);
  sb.Append(path).Append(" size=").Append(b.size.ToString("F1")).Append(" tris=").Append(mf0.sharedMesh.triangles.Length / 3).AppendLine();
}
UnityEditor.AssetDatabase.SaveAssets();
return sb.ToString();
