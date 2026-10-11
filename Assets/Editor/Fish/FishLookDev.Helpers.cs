using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using WashedAshore.Gameplay;
using WashedAshore.Level;
using WashedAshore.World;

namespace WashedAshore.Fish.Editor
{
    // FishLookDev helpers: shader and instancing checks, shore geometry from LevelMaps, offscreen shots.
    public static partial class FishLookDev
    {
        const int MinRegionPixels = 400;

        static string InstancingCheck((GameObject go, AnimationClip clip, Bounds b0) model, Look look, Vector3 edge, Vector3 outw, Vector3 along, float W)
        {
            var inst = Object.Instantiate(model.go);
            inst.hideFlags = HideFlags.HideAndDontSave;
            model.clip.SampleAnimation(inst, 0.25f * model.clip.length);
            var smr = inst.GetComponentInChildren<SkinnedMeshRenderer>();
            var mesh = new Mesh();
            smr.BakeMesh(mesh, true);
            var names = smr.sharedMaterials.Select(m => m ? m.name : "").ToArray();
            var meshToRoot = inst.transform.worldToLocalMatrix * smr.transform.localToWorldMatrix;
            Object.DestroyImmediate(inst);
            float u = look.TL / model.b0.size.z;
            var scale = new Vector3(look.x * u, look.y * u, u);
            var basePt = WithY(edge + outw * 3f, W + 0.5f);
            var matrices = Enumerable.Range(0, 4).Select(k =>
                Matrix4x4.TRS(basePt + along * ((k - 1.5f) * 1.2f), Quaternion.LookRotation(along, Vector3.up), scale) * meshToRoot).ToArray();
            var centres = Enumerable.Range(0, 4).Select(k => basePt + along * ((k - 1.5f) * 1.2f)).ToArray();
            var screenX = new float[4];
            int pixW = 0, pixH = 0;
            Material Mat(bool fin)
            {
                var m = new Material(fishShader) { hideFlags = HideFlags.DontSave, enableInstancing = true };
                m.SetColor("_BackColor", Hex(look.back)); m.SetColor("_BellyColor", Hex(look.belly)); m.SetColor("_FinColor", Hex(look.fins));
                m.SetFloat("_IsFin", fin ? 1f : 0f);
                m.SetVector("_FishCentre", new Vector4(basePt.x, basePt.y, basePt.z, look.TL));
                temp.Add(m);
                return m;
            }
            Material body = Mat(false), fins = Mat(true);
            var eye = basePt + outw * 5f + Vector3.up * 0.2f;
            var empty = ShotPixels("g4-instancing-check-empty.png", eye, basePt, 45f, null);
            var drawn = ShotPixels("g4-instancing-check.png", eye, basePt, 45f, cam =>
            {
                for (int k = 0; k < 4; k++) screenX[k] = cam.WorldToScreenPoint(centres[k]).x;
                pixW = cam.pixelWidth; pixH = cam.pixelHeight;
                for (int sm = 0; sm < mesh.subMeshCount; sm++)
                {
                    var rp = new RenderParams(names.Length > sm && names[sm].StartsWith("Fins") ? fins : body) { camera = cam, shadowCastingMode = ShadowCastingMode.Off };
                    Graphics.RenderMeshInstanced(rp, mesh, sm, matrices);
                }
            });
            // f-td: pass only if every instance drew in its own screen region (proves per-instance matrices, not "something
            // drew" or all four stacked on one matrix). Regions are the column bands between the midpoints of the projected centres.
            var order = Enumerable.Range(0, 4).OrderBy(k => screenX[k]).ToArray();
            var perRegion = new int[4];
            for (int y = 0; y < pixH; y++)
                for (int x = 0; x < pixW; x++)
                {
                    int i = y * pixW + x;
                    if (Mathf.Abs(empty[i].r - drawn[i].r) + Mathf.Abs(empty[i].g - drawn[i].g) + Mathf.Abs(empty[i].b - drawn[i].b) <= 6) continue;
                    int region = 0;
                    while (region < 3 && x > 0.5f * (screenX[order[region]] + screenX[order[region + 1]])) region++;
                    perRegion[region]++;
                }
            int changed = perRegion.Sum();
            int submeshes = mesh.subMeshCount;
            Object.DestroyImmediate(mesh);
            bool ok = SystemInfo.supportsInstancing && perRegion.All(n => n > MinRegionPixels);
            return $"instancing check (f-td 5.4 f): supportsInstancing {SystemInfo.supportsInstancing}, SRP Batcher {GraphicsSettings.useScriptableRenderPipelineBatching}, " +
                   $"enableInstancing true, 4 instances x {submeshes} submeshes via RenderMeshInstanced; {changed} pixels changed vs the empty frame, per instance region (left to right) {string.Join("/", perRegion)}, bar > {MinRegionPixels} each; per-instance tint not tested pre-gate (colours are per material until the Phase 1 instanced properties) " +
                   $"{(ok ? "PASS" : "FAIL")} (g4-instancing-check.png)";
        }

        // ---------------- checks, helpers ----------------

        static string CheckShaders(params Shader[] shaders)
        {
            var sb = new StringBuilder();
            foreach (var shader in shaders)
            {
                int bad = 0, n = 0;
                var pass = ShaderUtil.GetShaderData(shader).GetSubshader(0).GetPass(0);
                foreach (var (platform, target) in new[] { (UnityEditor.Rendering.ShaderCompilerPlatform.D3D, BuildTarget.StandaloneWindows64), (UnityEditor.Rendering.ShaderCompilerPlatform.GLES3x, BuildTarget.WebGL) })
                    foreach (var kw in new[] { new string[0], new[] { "FOG_LINEAR" }, new[] { "INSTANCING_ON" } })
                        foreach (var stage in new[] { UnityEditor.Rendering.ShaderType.Vertex, UnityEditor.Rendering.ShaderType.Fragment })
                        {
                            n++;
                            var r = pass.CompileVariant(stage, kw, platform, target);
                            if (r.Success) continue;
                            bad++;
                            sb.Append($"\n  FAIL {shader.name} {platform} {stage} [{string.Join(",", kw)}]: " + string.Join(" | ", r.Messages.Select(m => $"line {m.line}: {m.message}")));
                        }
                sb.Insert(0, $"shader {shader.name}: hasError={ShaderUtil.ShaderHasError(shader)}, variants compiled {n - bad}/{n} (D3D + GLES3x/WebGL)\n");
                if (bad > 0 || ShaderUtil.ShaderHasError(shader)) throw new System.InvalidOperationException(sb.ToString());
            }
            return sb.ToString().TrimEnd();
        }

        static Vector3 WithY(Vector3 p, float y) => new Vector3(p.x, y, p.z);

        // Nearest 2 m cell to p whose shore distance lies in [lo, hi] (as BellsBendWaterLook).
        static Vector3 NearestShore(BellsBendLevelMaps maps, Vector3 p, float lo, float hi)
        {
            for (float r = 0f; r <= 400f; r += 2f)
            {
                int steps = Mathf.Max(1, Mathf.CeilToInt(Mathf.PI * r));
                for (int k = 0; k < steps; k++)
                {
                    float a = k * 2f * Mathf.PI / steps;
                    var q = p + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                    float sd = maps.ShoreDistance(q);
                    if (sd >= lo && sd <= hi) return q;
                }
            }
            throw new System.InvalidOperationException($"no shore cell near ({p.x},{p.z})");
        }

        // Unit XZ direction from land toward water (down the shore-distance gradient), as BellsBendWaterLook.
        static Vector3 Outward(BellsBendLevelMaps maps, Vector3 p)
        {
            var g = Vector3.zero;
            for (float r = 4f; r <= 12f; r += 4f)
                g += new Vector3(maps.ShoreDistance(p + new Vector3(r, 0f, 0f)) - maps.ShoreDistance(p - new Vector3(r, 0f, 0f)), 0f,
                                 maps.ShoreDistance(p + new Vector3(0f, 0f, r)) - maps.ShoreDistance(p - new Vector3(0f, 0f, r)));
            return g.sqrMagnitude > 1e-6f ? -g.normalized : Vector3.forward;
        }

        // Offscreen render, opaque RGB (no alpha channel in the PNG).
        static string Shot(string file, Vector3 pos, Vector3 target, float fov)
        {
            ShotPixels(file, pos, target, fov, null);
            return $"  {file}: eye ({pos.x:F1},{pos.y:F2},{pos.z:F1}) -> ({target.x:F1},{target.y:F2},{target.z:F1}) fov {fov:F0}, dist {Vector3.Distance(pos, target):F1} m";
        }

        static Color32[] ShotPixels(string file, Vector3 pos, Vector3 target, float fov, System.Action<Camera> beforeRender)
        {
            const int Wd = 1920, H = 1080;
            var go = new GameObject("_FishLookCamera") { hideFlags = HideFlags.HideAndDontSave };
            var cam = go.AddComponent<Camera>();
            cam.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(target - pos));
            cam.fieldOfView = fov; cam.nearClipPlane = 0.1f; cam.farClipPlane = 3000f;
            var rt = RenderTexture.GetTemporary(Wd, H, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;
            beforeRender?.Invoke(cam);
            cam.Render();
            var prev = RenderTexture.active; RenderTexture.active = rt;
            var tex = new Texture2D(Wd, H, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, Wd, H), 0, 0); tex.Apply();
            RenderTexture.active = prev; cam.targetTexture = null; RenderTexture.ReleaseTemporary(rt);
            File.WriteAllBytes(Path.Combine(OutDir, file), tex.EncodeToPNG());
            var px = tex.GetPixels32();
            Object.DestroyImmediate(tex); Object.DestroyImmediate(go);
            return px;
        }
    }
}
