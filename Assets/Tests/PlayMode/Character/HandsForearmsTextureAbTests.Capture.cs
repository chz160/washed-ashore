#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace WashedAshore.Tests.PlayMode.Character
{
    // Capture, region of interest, comparison and results for the F10 A/B. See HandsForearmsTextureAbTests.cs.
    public partial class HandsForearmsTextureAbTests
    {
        static string CapturePath([CallerFilePath] string p = "") => p;

        static readonly (int w, int h)[] Resolutions = { (1920, 1080), (2560, 1440) };   // 1080p decides; 1440p is a labelled variant

        /// <summary>Places the player in the pose and yaw, then renders every twin in one frame at each resolution and compares inside the hands/forearms mask.</summary>
        IEnumerator Shoot(PoseDef pose, string yawName, float yaw, Dictionary<string, Dictionary<Renderer, Material[]>> v, bool control)
        {
            Vector3 dir = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            var centre = new Vector3(floor.transform.position.x, floor.transform.position.y + 2.05f, floor.transform.position.z);
            Place(centre - dir * (pose.walk ? 20f : 0f), yaw);
            var anim = view.Body.Animator;
            anim.speed = 1f;
            if (pose.walk)
            {
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W));
                yield return new WaitForSeconds(1.0f);
            }
            else yield return new WaitForSeconds(0.5f);
            player.SetLook(yaw, pose.pitch);
            yield return null;
            yield return null;
            if (pose.walk)
            {
                if (walkT < 0f)
                {
                    // The frame where the right wrist is furthest forward of the eye along the horizontal look direction, over 120 frames of the walk cycle.
                    var wrist = view.Body.Bone("Wrist.R");
                    var ct = player.ViewCamera.transform;
                    Vector3 flat = Vector3.ProjectOnPlane(ct.forward, Vector3.up).normalized;
                    float best = float.MinValue; string clip = "";
                    for (int i = 0; i < 120; i++)
                    {
                        yield return null;
                        if (anim.IsInTransition(0)) continue;
                        float d = Vector3.Dot(wrist.position - ct.position, flat);
                        if (d > best)
                        {
                            best = d; var st = anim.GetCurrentAnimatorStateInfo(0);
                            walkHash = st.fullPathHash; walkT = st.normalizedTime - Mathf.Floor(st.normalizedTime);
                            var ci = anim.GetCurrentAnimatorClipInfo(0); clip = ci.Length > 0 ? ci[0].clip.name : "?";
                        }
                    }
                    Assert.GreaterOrEqual(walkT, 0f, "no walk-cycle frame found");
                    log.AppendLine($"walk freeze: clip '{clip}', state hash {walkHash}, normalized time {walkT:F3}, right wrist {best:F3} m ahead of the eye");
                    player.SetLook(yaw, pose.pitch);
                }
                InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                anim.Play(walkHash, 0, walkT);
                anim.speed = 0f;
                anim.Update(0f);
                yield return null;
                yield return null;
            }
            foreach (var (w, h) in Resolutions)
            {
                if (control && w != 1920) continue;
                var s = CaptureAll(pose, yawName, w, h, v, control);
                stills.Add(s);
            }
            anim.speed = 1f;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            yield return new WaitForSeconds(0.3f);
        }

        // ---------------------------------------------------------------- capture

        Still CaptureAll(PoseDef pose, string yawName, int w, int h, Dictionary<string, Dictionary<Renderer, Material[]>> v, bool control)
        {
            var cam = player.ViewCamera;
            var oldRt = cam.targetTexture;
            var rt = new RenderTexture(w, h, 24);
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            Color32[] Grab(string key)
            {
                Use(v[key]);
                cam.targetTexture = rt; cam.aspect = w / (float)h;
                cam.Render();
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
                RenderTexture.active = null;
                return tex.GetPixels32();
            }
            Color32[] a, b, a2, again, shipped, blurred = null;
            bool[] roi;
            try
            {
                // All twins in the same frame, no time step, no yield between renders.
                a = Grab("4k"); b = Grab("2k"); a2 = Grab("4kB"); again = Grab("4k"); shipped = Grab("shipped");
                if (control) blurred = Grab("blur");
                roi = BuildRoi(cam, w, h);
            }
            finally { cam.targetTexture = oldRt; cam.ResetAspect(); Use(new Dictionary<Renderer, Material[]>(originals)); UnityEngine.Object.Destroy(tex); rt.Release(); UnityEngine.Object.Destroy(rt); }

            var s = new Still { pose = pose.name, yaw = yawName, w = w, h = h, control = control, x0 = w, y0 = h, x1 = -1, y1 = -1 };
            int n = 0, ab = 0, aa = 0, id = 0, bl = 0, sh = 0; float maxD = 0f;
            var heat = new Color32[w * h];
            for (int i = 0; i < roi.Length; i++)
            {
                if (!roi[i]) { byte g = (byte)(a[i].g / 4); heat[i] = new Color32(g, g, g, 255); continue; }
                n++; int x = i % w, y = i / w;
                if (x < s.x0) s.x0 = x; if (x > s.x1) s.x1 = x; if (y < s.y0) s.y0 = y; if (y > s.y1) s.y1 = y;
                float dab = Delta(a[i], b[i]);
                if (dab >= MinDelta) ab++;
                if (dab > maxD) maxD = dab;
                if (Delta(a[i], a2[i]) >= MinDelta) aa++;
                if (Delta(a[i], again[i]) >= MinDelta) id++;
                if (Delta(a[i], shipped[i]) >= MinDelta) sh++;
                if (blurred != null && Delta(a[i], blurred[i]) >= MinDelta) bl++;
                heat[i] = new Color32((byte)Mathf.Min(255, dab * 16f), 0, 0, 255);
            }
            s.roi = n;
            if (n > 0)
            {
                s.abPct = 100f * ab / n; s.aaPct = 100f * aa / n; s.idPct = 100f * id / n; s.maxD = maxD;
                s.net = Mathf.Max(0f, s.abPct - s.aaPct);
                s.blurNet = Mathf.Max(0f, 100f * bl / n - s.aaPct);
                s.shipNet = Mathf.Max(0f, 100f * sh / n - s.aaPct);   // art-49 control: the shipped Survivor.mat vs the scratch 4096 twin, above the A-A floor
                if (s.shipNet > 0f) voids.Add($"control shipped material vs the scratch 4096 twin reads {s.shipNet:F3}% above the floor at {pose.name}/{yawName}/{w}: the copy does not render what ships");
            }
            if (control)
            {
                // Identical pair: the same material rendered twice must not differ above the A-A floor. Blur: must be seen by the Measured rule.
                if (n < MinRoiPixels) voids.Add($"control: hands/forearms area is only {n} px (need {MinRoiPixels}); the controls cannot prove the detector");
                if (s.idPct > s.aaPct) voids.Add($"control identical pair reads {s.idPct:F3}% above its A-A floor {s.aaPct:F3}%");
                if (s.blurNet < MeasuredPct) voids.Add($"control blurred 4096 reads only {s.blurNet:F3}% (< {MeasuredPct}%): the detector or the mask cannot see a strong blur");
                log.AppendLine($"CONTROLS {pose.name}/{yawName}: ROI {n} px, identical-pair {s.idPct:F3}%, A-A floor {s.aaPct:F3}%, blurred-4096 net {s.blurNet:F3}%");
            }
            else
            {
                string res = $"{w}";
                // Labelled stills and diffs go under AB_KEY_DIR (td-49 B1): beside the blind crops they would let a crop be matched to its labelled still.
                string lab = Path.Combine(keyDir, "labelled"); Directory.CreateDirectory(lab);
                string stem = Path.Combine(lab, $"{pose.name}_{yawName}_{res}");
                SavePng(a, w, h, stem + "_4k.png"); SavePng(b, w, h, stem + "_2k.png"); SavePng(a2, w, h, stem + "_4kB.png"); SavePng(heat, w, h, stem + "_diff.png");
                if (w == 1920) { s.pa = a; s.pb = b; }
            }
            return s;
        }

        static float Delta(Color32 p, Color32 q) => Mathf.Max(Mathf.Abs(p.r - q.r), Mathf.Max(Mathf.Abs(p.g - q.g), Mathf.Abs(p.b - q.b)));

        static void SavePng(Color32[] px, int w, int h, string path)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false);
            t.SetPixels32(px); t.Apply();
            File.WriteAllBytes(path, t.EncodeToPNG());
            UnityEngine.Object.Destroy(t);
        }

        // ---------------------------------------------------------------- region of interest

        static bool IsHandBone(string n) => n.Contains("Wrist") || n.Contains("LowerArm") || n.Contains("Thumb") || n.Contains("Index") || n.Contains("Middle") || n.Contains("Ring") || n.Contains("Pinky") || n.Contains("Hand");

        /// <summary>
        /// Mask of the hand and forearm pixels: the baked view body is drawn into a private camera on layer 31 (nothing else is in that camera), first all triangles
        /// black, then the triangles whose vertices are mostly dominated by wrist, forearm or finger bones white, so the body's own occlusion is respected.
        /// World geometry is not drawn (it is not an occluder in this mask).
        /// </summary>
        bool[] BuildRoi(Camera cam, int w, int h)
        {
            var black = new Material(Shader.Find("Unlit/Color")) { color = Color.black };
            var white = new Material(Shader.Find("Unlit/Color")) { color = Color.white, renderQueue = 2001 };
            var go = new GameObject("AbMaskCam") { hideFlags = HideFlags.HideAndDontSave };
            var c = go.AddComponent<Camera>();
            c.CopyFrom(cam);
            c.transform.SetPositionAndRotation(cam.transform.position, cam.transform.rotation);
            c.enabled = false; c.cullingMask = 1 << MaskLayer; c.clearFlags = CameraClearFlags.SolidColor; c.backgroundColor = Color.black;
            c.allowMSAA = false; c.allowHDR = false; c.aspect = w / (float)h;
            var rt = new RenderTexture(w, h, 24) { antiAliasing = 1 };
            c.targetTexture = rt;
            var tmp = new List<Mesh>();
            foreach (var r in rends)
            {
                if (!(r is SkinnedMeshRenderer smr) || smr.sharedMesh == null) continue;
                var baked = new Mesh(); smr.BakeMesh(baked, false);
                var bw = smr.sharedMesh.boneWeights; var bones = smr.bones; var tris = baked.triangles;
                bool Hand(int vi) { if (vi >= bw.Length) return false; int bi = bw[vi].boneIndex0; return bi >= 0 && bi < bones.Length && bones[bi] != null && IsHandBone(bones[bi].name); }
                var roiTris = new List<int>();
                for (int i = 0; i < tris.Length; i += 3)
                    if ((Hand(tris[i]) ? 1 : 0) + (Hand(tris[i + 1]) ? 1 : 0) + (Hand(tris[i + 2]) ? 1 : 0) >= 2) { roiTris.Add(tris[i]); roiTris.Add(tris[i + 1]); roiTris.Add(tris[i + 2]); }
                var roiMesh = new Mesh { indexFormat = baked.indexFormat, vertices = baked.vertices };
                roiMesh.SetTriangles(roiTris, 0);
                var m = Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one);
                Graphics.DrawMesh(baked, m, black, MaskLayer, c);
                Graphics.DrawMesh(roiMesh, m, white, MaskLayer, c);
                tmp.Add(baked); tmp.Add(roiMesh);
            }
            c.Render();
            var t = new Texture2D(w, h, TextureFormat.RGB24, false);
            RenderTexture.active = rt; t.ReadPixels(new Rect(0, 0, w, h), 0, 0); t.Apply(); RenderTexture.active = null;
            var px = t.GetPixels32(); var roi = new bool[px.Length];
            for (int i = 0; i < px.Length; i++) roi[i] = px[i].r > 127;
            UnityEngine.Object.Destroy(t); rt.Release(); UnityEngine.Object.Destroy(rt); UnityEngine.Object.Destroy(go);
            foreach (var m in tmp) UnityEngine.Object.Destroy(m);
            UnityEngine.Object.Destroy(black); UnityEngine.Object.Destroy(white);
            return roi;
        }

        // ---------------------------------------------------------------- results and the blind pairs

        void WriteResults(System.Random rng, List<int> order, Texture2D t4, Texture2D t2)
        {
            var real = stills.FindAll(s => !s.control);
            var decide = real.FindAll(s => s.w == 1920);
            int tiny = decide.FindAll(s => s.roi < MinRoiPixels).Count, measured = 0;
            if (tiny > MaxTinyRoi) voids.Add($"{tiny} of {decide.Count} stills have a hands/forearms area under {MinRoiPixels} px");
            var res = new StringBuilder(log.ToString());
            res.AppendLine("still, resolution, ROI px, A-B changed %, A-A floor %, net % (A-B minus floor), max delta, shipped-vs-4096 above floor %, counted");
            foreach (var s in real)
            {
                bool counted = s.roi >= MinRoiPixels;
                if (s.w == 1920 && counted && s.net >= MeasuredPct) measured++;
                res.AppendLine($"{s.pose}_{s.yaw}, {s.w}x{s.h}{(s.w == 1920 ? "" : " (variant)")}, {s.roi}, {s.abPct:F3}, {s.aaPct:F3}, {s.net:F3}, {s.maxD:F0}, {s.shipNet:F3}, {(counted ? "yes" : "NO: ROI under " + MinRoiPixels + " px")}");
            }
            res.AppendLine($"MEASURED (1920x1080, >= {MeasuredStills} of {decide.Count} stills with >= {MeasuredPct}% net): {measured} stills -> {(measured >= MeasuredStills ? "YES" : "NO")}");

            // Blind pairs: order shuffled and sides randomised by the seed; the key stays out of the runner's folder.
            for (int i = order.Count - 1; i > 0; i--) { int j = rng.Next(i + 1); int tmp = order[i]; order[i] = order[j]; order[j] = tmp; }
            var key = new StringBuilder();
            for (int n = 0; n < decide.Count && n < order.Count; n++)
            {
                var s = decide[order[n]]; bool fourLeft = rng.Next(2) == 0;
                SavePng(Compose(s, fourLeft, out int cw, out int ch), cw, ch, Path.Combine(outDir, "blind", $"pair_{n + 1:00}.png"));
                key.AppendLine($"pair_{n + 1:00} = {s.pose}_{s.yaw}: LEFT={(fourLeft ? "4096" : "2048")} RIGHT={(fourLeft ? "2048" : "4096")}");
            }
            string keyText = key.ToString();
            File.WriteAllText(Path.Combine(keyDir, "ab4k-key.txt"), keyText);
            File.WriteAllText(Path.Combine(keyDir, "ab4k-seed.txt"), seedText);   // the seed and the script rebuild the key, so the seed stays with the key (td-49 B2)
            using (var sha = SHA256.Create()) res.AppendLine($"blind key sha256 {BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(keyText))).Replace("-", "").ToLowerInvariant()} (key file withheld in AB_KEY_DIR until art-49's picks are recorded)");
            res.AppendLine(voids.Count == 0 ? "RUN VALID" : "RUN VOID: " + string.Join("; ", voids));
            File.WriteAllText(Path.Combine(outDir, "ab4k-results.txt"), res.ToString());
        }

        /// <summary>A side-by-side crop around the hands/forearms (bounding box + 48 px, at least 400x300), 100% scale, a 16 px grey gap, no labels.</summary>
        static Color32[] Compose(Still s, bool fourLeft, out int cw, out int ch)
        {
            int x0 = s.x1 < 0 ? s.w / 4 : Mathf.Max(0, s.x0 - 48), x1 = s.x1 < 0 ? s.w * 3 / 4 : Mathf.Min(s.w - 1, s.x1 + 48);
            int y0 = s.y1 < 0 ? s.h / 2 : Mathf.Max(0, s.y0 - 48), y1 = s.y1 < 0 ? s.h - 1 : Mathf.Min(s.h - 1, s.y1 + 48);
            int w = Mathf.Max(400, x1 - x0 + 1), h = Mathf.Max(300, y1 - y0 + 1);
            w = Mathf.Min(w, s.w); h = Mathf.Min(h, s.h);
            x0 = Mathf.Clamp(x0, 0, s.w - w); y0 = Mathf.Clamp(y0, 0, s.h - h);
            cw = w * 2 + 16; ch = h;
            var o = new Color32[cw * ch];
            for (int i = 0; i < o.Length; i++) o[i] = new Color32(128, 128, 128, 255);
            var left = fourLeft ? s.pa : s.pb; var right = fourLeft ? s.pb : s.pa;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int src = (y0 + y) * s.w + (x0 + x);
                    o[y * cw + x] = left[src]; o[y * cw + w + 16 + x] = right[src];
                }
            return o;
        }
    }
}
#endif
