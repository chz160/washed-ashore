var sb = new System.Text.StringBuilder();
var t = UnityEngine.Terrain.activeTerrain;
if (t == null) return "NO ACTIVE TERRAIN";
var d = t.terrainData;
sb.Append("terrains=").Append(UnityEngine.Terrain.activeTerrains.Length).Append(" pos=").Append(t.transform.position).Append("\n");
sb.Append("P3.1 size=").Append(d.size.x).Append("x").Append(d.size.z).Append(" maxH=").Append(d.size.y).Append("\n");
int r = d.heightmapResolution;
var h = d.GetHeights(0, 0, r, r); float mn = 1, mx = 0;
foreach (var v in h) { if (v < mn) mn = v; if (v > mx) mx = v; }
sb.Append("P3.2 heightRange_m=").Append((mx - mn) * d.size.y).Append(" (min=").Append(mn * d.size.y).Append(" max=").Append(mx * d.size.y).Append(")\n");
var layers = d.terrainLayers;
sb.Append("P3.3 layers=").Append(layers.Length).Append("\n");
int aw = d.alphamapWidth, ah = d.alphamapHeight;
var a = d.GetAlphamaps(0, 0, aw, ah);
for (int i = 0; i < layers.Length; i++) {
  double s = 0; for (int y = 0; y < ah; y++) for (int x = 0; x < aw; x++) s += a[y, x, i];
  var L = layers[i];
  sb.Append("P3.4/5 layer").Append(i).Append(" ").Append(L == null ? "null" : L.name)
    .Append(" diffuse=").Append(L == null || L.diffuseTexture == null ? "null" : UnityEditor.AssetDatabase.GetAssetPath(L.diffuseTexture))
    .Append(" coverage=").Append((s / (aw * ah)).ToString("F4")).Append("\n");
}
sb.Append("P3.6 terrainShader=").Append(t.materialTemplate == null ? "null(materialTemplate)" : t.materialTemplate.shader.name).Append("\n");
var treeRx = new System.Text.RegularExpressions.Regex("tree|pine|birch|oak|maple|willow|palm|dead", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
var rockRx = new System.Text.RegularExpressions.Regex("rock|stone|boulder|pebble|bush|shrub", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
var protos = d.treePrototypes;
var counts = new int[protos.Length];
int offTerrain = 0;
foreach (var ti in d.treeInstances) {
  if (ti.prototypeIndex >= 0 && ti.prototypeIndex < counts.Length) counts[ti.prototypeIndex]++;
  if (ti.position.x < 0 || ti.position.x > 1 || ti.position.z < 0 || ti.position.z > 1) offTerrain++;
}
var treeNames = new System.Collections.Generic.HashSet<string>(); var rockNames = new System.Collections.Generic.HashSet<string>();
int treeInst = 0;
for (int i = 0; i < protos.Length; i++) {
  var p = protos[i]; string n = p.prefab == null ? "null" : p.prefab.name;
  string kind = treeRx.IsMatch(n) ? "tree" : (rockRx.IsMatch(n) ? "rockbush" : "other");
  if (kind == "tree") { treeNames.Add(n); treeInst += counts[i]; }
  if (kind == "rockbush") rockNames.Add(n);
  string meshPath = "";
  if (p.prefab != null) { var mf = p.prefab.GetComponentInChildren<UnityEngine.MeshFilter>(true); if (mf != null && mf.sharedMesh != null) meshPath = UnityEditor.AssetDatabase.GetAssetPath(mf.sharedMesh); }
  sb.Append("P4 proto").Append(i).Append(" ").Append(n).Append(" kind=").Append(kind).Append(" inst=").Append(counts[i])
    .Append(" prefab=").Append(p.prefab == null ? "" : UnityEditor.AssetDatabase.GetAssetPath(p.prefab)).Append(" mesh=").Append(meshPath).Append("\n");
}
sb.Append("P4.1 distinctTrees=").Append(treeNames.Count).Append(" P4.2 distinctRockBush=").Append(rockNames.Count)
  .Append(" P4.4 treeInstances=").Append(treeInst).Append(" totalInstances=").Append(d.treeInstanceCount).Append(" P4.8 offTerrain=").Append(offTerrain).Append("\n");
sb.Append("P4.6 detailProtos=").Append(d.detailPrototypes.Length).Append("\n");
for (int i = 0; i < d.detailPrototypes.Length; i++) {
  var dl = d.GetDetailLayer(0, 0, d.detailWidth, d.detailHeight, i); long s = 0; foreach (var v in dl) s += v;
  var dp = d.detailPrototypes[i];
  sb.Append("P4.7 detail").Append(i).Append(" ").Append(dp.prototype != null ? dp.prototype.name : (dp.prototypeTexture != null ? dp.prototypeTexture.name : "null"))
    .Append(" mesh=").Append(dp.usePrototypeMesh).Append(" density=").Append(s).Append("\n");
}
return sb.ToString();
