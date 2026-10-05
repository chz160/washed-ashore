var d = UnityEngine.Terrain.activeTerrain.terrainData;
float smax = 0; int steep22 = 0;
for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++) { float s = d.GetSteepness(x / 63f, y / 63f); smax = UnityEngine.Mathf.Max(smax, s); if (s > 22) steep22++; }
var a = d.GetAlphamaps(0, 0, d.alphamapWidth, d.alphamapHeight);
return "alpha " + d.alphamapWidth + "x" + d.alphamapHeight + " layers=" + d.alphamapLayers + " textures=" + d.alphamapTextureCount + " steepMax=" + smax + " steep22=" + steep22 + "/4096 a[100,100]=" + a[100,100,0] + "," + a[100,100,1] + "," + a[100,100,2];
