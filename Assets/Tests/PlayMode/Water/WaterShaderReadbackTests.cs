using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using WashedAshore.World;

namespace WashedAshore.Tests.PlayMode
{
    /// <summary>
    /// Spec W9, w-td condition 1 (water/tech-pick): the GPU's f matches the C# WaterMotion.Offset near the four
    /// world corners and near the origin, where the spatial phase k·(d·xz) reaches ~1e4 rad and float sin() is
    /// otherwise unverified. A temporary copy of w-artist's water material with _WATER_DEBUG_HEIGHT on (unlit,
    /// rgb = saturate(h / 0.1 + 0.5), so h = (R - 0.5) x 0.1 m) is drawn on a temporary quad on a free layer and
    /// read back from an RGBAFloat target through a top-down orthographic camera. The quad and camera sit at the
    /// world positions themselves (no UV offsets), and the asserted bar is 3 mm (w-td), with W9's 5 cm reported. The shared material and
    /// w-level's quads are not touched. Runs once at the level-load clock and once at a 3-day clock.
    /// </summary>
    public class WaterShaderReadbackTests
    {
        const string WorldScene = "World";
        const string MaterialPath = "Assets/World/BellsBend/Water/BellsBendWater.mat";
        const string DebugKeyword = "_WATER_DEBUG_HEIGHT";
        const float SpecBar = 0.05f;     // W9 spec number, reported
        const float Tolerance = 0.003f;  // w-td: asserted bar (f totals <= 3 cm, so 5 cm could never fail)
        const float Inset = 20f;         // metres in from the terrain corners
        const int Size = 64;             // render target texels per side: 4 m / 64 = 6.25 cm per texel (w-td: <= 0.1 m)
        const float View = 4f;           // metres seen per side
        const int Grid = 5;              // texels sampled per side

        readonly List<Object> temp = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            WaterClock.Use(null);
            foreach (var o in temp) if (o) Object.Destroy(o);
            temp.Clear();
        }

        static (float min, float max) TerrainZRange()
        {
            float minZ = float.MaxValue, maxZ = float.MinValue;
            foreach (var t in Terrain.activeTerrains)
            {
                minZ = Mathf.Min(minZ, t.transform.position.z);
                maxZ = Mathf.Max(maxZ, t.transform.position.z + t.terrainData.size.z);
            }
            return (minZ, maxZ);
        }

        static int FreeLayer()
        {
            for (int i = 31; i >= 8; i--)
                if (string.IsNullOrEmpty(LayerMask.LayerToName(i))) return i;
            Assert.Fail("No unnamed layer for the readback camera");
            return -1;
        }

        [UnityTest, Timeout(300000)]
        public IEnumerator GpuHeight_MatchesCSharp_AtCornersAndOrigin([Values(0.0, 3 * 86400.0 + 0.123)] double clockOffset)
        {
            SceneManager.LoadScene(WorldScene, LoadSceneMode.Single);
            yield return null;
            yield return null;
#if UNITY_EDITOR
            var shared = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
#else
            Material shared = null;
#endif
            Assert.IsNotNull(shared, $"No water material at {MaterialPath}");
            var water = WaterTestKit.Water();
            var waves = water.Motion ? water.Motion.waves : new WaterWave[0];
            Assert.Greater(WaterMotion.MaxAmplitude(waves), 0f, "no waves: the readback would prove nothing");
            if (clockOffset > 0) WaterClock.Use(() => clockOffset + Time.timeSinceLevelLoadAsDouble);
            yield return null; // WaterBody pushes this frame's phases

            var mat = new Material(shared) { name = "W9ReadbackMaterial" };
            mat.EnableKeyword(DebugKeyword);
            if (mat.HasProperty("_DebugHeight")) mat.SetFloat("_DebugHeight", 1f); // keep the toggle in step with the keyword
            temp.Add(mat);
            int layer = FreeLayer();
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "W9ReadbackQuad";
            Object.Destroy(quad.GetComponent<Collider>());
            quad.layer = layer;
            quad.transform.rotation = Quaternion.Euler(90f, 0f, 0f); // front face up
            quad.transform.localScale = Vector3.one * (View * 2f);
            quad.GetComponent<MeshRenderer>().sharedMaterial = mat;
            temp.Add(quad);

            var rt = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear) { antiAliasing = 1 };
            temp.Add(rt);
            var camGo = new GameObject("W9ReadbackCamera");
            temp.Add(camGo);
            var cam = camGo.AddComponent<Camera>();
            cam.enabled = false;
            cam.orthographic = true;
            cam.orthographicSize = View * 0.5f;
            cam.cullingMask = 1 << layer;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.clear;
            cam.allowHDR = false;
            cam.allowMSAA = false;
            cam.targetTexture = rt;
            cam.transform.rotation = Quaternion.Euler(90f, 0f, 0f); // looking down; image up = +Z, right = +X
            var readback = new Texture2D(Size, Size, TextureFormat.RGBAFloat, false, true);
            temp.Add(readback);

            var (minX, maxX) = WorldBoundsTestKit.TerrainXRange();
            var (minZ, maxZ) = TerrainZRange();
            var points = new[]
            {
                new Vector2(minX + Inset, minZ + Inset), new Vector2(maxX - Inset, minZ + Inset),
                new Vector2(minX + Inset, maxZ - Inset), new Vector2(maxX - Inset, maxZ - Inset),
                new Vector2(Inset * 0.5f, Inset * 0.5f),
            };
            float w = water.Level, worst = 0f, worstFlipped = 0f;
            double t = water.WaterTime;
            var report = new List<string>();
            foreach (var c in points)
            {
                // The quad itself sits at the world corner, so the shader evaluates positionWS there (large-argument sin()).
                quad.transform.position = new Vector3(c.x, w, c.y);
                cam.transform.position = new Vector3(c.x, w + 10f, c.y);
                cam.nearClipPlane = 1f;
                cam.farClipPlane = 20f;
                cam.Render();
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                readback.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
                readback.Apply();
                RenderTexture.active = prev;

                float local = 0f, flipped = 0f;
                for (int gi = 0; gi < Grid; gi++)
                    for (int gj = 0; gj < Grid; gj++)
                    {
                        int px = 2 + gi * (Size - 5) / (Grid - 1), py = 2 + gj * (Size - 5) / (Grid - 1);
                        float x = c.x - View * 0.5f + (px + 0.5f) * View / Size, z = c.y - View * 0.5f + (py + 0.5f) * View / Size;
                        float gpu = (readback.GetPixel(px, py).r - 0.5f) * 0.1f;
                        float cpu = WaterMotion.Offset(waves, x, z, t);
                        local = Mathf.Max(local, Mathf.Abs(gpu - cpu));
                        // Orientation guard: the same texel read as if the image were upside down in Z.
                        float zFlip = c.y + View * 0.5f - (py + 0.5f) * View / Size;
                        flipped = Mathf.Max(flipped, Mathf.Abs(gpu - WaterMotion.Offset(waves, x, zFlip, t)));
                    }
                worst = Mathf.Max(worst, local);
                worstFlipped = Mathf.Max(worstFlipped, flipped);
                report.Add($"({c.x:F0},{c.y:F0}) max|gpu-cs|={local * 1000f:F2} mm (Z-flipped {flipped * 1000f:F2} mm)");
            }
            Debug.Log($"WaterShaderReadbackTests[clock+{clockOffset:F0}s]: t={t:F3} {string.Join("; ", report)}; worst={worst * 1000f:F2} mm " +
                      $"(asserted <= {Tolerance * 1000f:F0} mm; W9 spec bar {SpecBar * 1000f:F0} mm)");
            Assert.LessOrEqual(worst, Tolerance, "GPU f differs from C# f (w-td: fix with tile-local coordinates, not a looser bar)");
            // f is at most a few cm, so the 5 cm bar alone can't tell a matching surface from a misread one:
            // the readback must match the C# f clearly better than the same texels mirrored in Z.
            Assert.Less(worst, worstFlipped * 0.5f, "readback does not correlate with C# f (orientation or encoding wrong)");
        }
    }
}
