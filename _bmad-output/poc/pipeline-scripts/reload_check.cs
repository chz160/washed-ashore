UnityEditor.AssetDatabase.SaveAssets();
UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/World.unity", UnityEditor.SceneManagement.OpenSceneMode.Single);
var t = UnityEngine.Terrain.activeTerrain; var d = t.terrainData;
var h = d.GetHeights(0, 0, d.heightmapResolution, d.heightmapResolution); float mn = 1, mx = 0;
foreach (var v in h) { if (v < mn) mn = v; if (v > mx) mx = v; }
var a = d.GetAlphamaps(0, 0, d.alphamapWidth, d.alphamapHeight);
var cov = new float[d.alphamapLayers];
for (int y = 0; y < d.alphamapHeight; y++) for (int x = 0; x < d.alphamapWidth; x++) for (int l = 0; l < cov.Length; l++) cov[l] += a[y, x, l];
long det = 0;
for (int i = 0; i < d.detailPrototypes.Length; i++) foreach (var c in d.GetDetailLayer(0, 0, d.detailWidth, d.detailHeight, i)) det += c;
return "afterReload: asset=" + UnityEditor.AssetDatabase.GetAssetPath(d) + " size=" + d.size.x + "x" + d.size.z + " heightDelta=" + ((mx - mn) * d.size.y).ToString("F2") + "m min=" + (mn * d.size.y).ToString("F2") + " max=" + (mx * d.size.y).ToString("F2")
  + " layers=" + d.terrainLayers.Length + " coverage=" + string.Join(",", System.Array.ConvertAll(cov, c => (c / (d.alphamapWidth * d.alphamapHeight)).ToString("F3")))
  + " treeProtos=" + d.treePrototypes.Length + " treeInstances=" + d.treeInstanceCount + " detailLayers=" + d.detailPrototypes.Length + " detailSum=" + det
  + " shader=" + t.materialTemplate.shader.name;
