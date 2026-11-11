using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using WashedAshore.Character.Stance;
using WashedAshore.Gameplay;
using WashedAshore.Tests.Support;

namespace WashedAshore.Tests.PlayMode.Character
{
    /// <summary>
    /// PRINT ONLY, [Explicit] (td-49 B26 D3): why are the two see-through pinholes (Sit +75 and Squat +85, found by the pose probe) not drawn although the R3 ray through their centre hits a FRONT face? All in the SAME frame as a fresh lit and body-only render.
    /// T0 finds the pinhole pixels again (every magenta pixel of the body-only render with all 8 neighbours drawn body) and prints each one's R3 ray. T1 prints the index identity: FNV-1a (32 bit, over the 4 little-endian bytes of every triangle index) of the BakeMesh output's triangles
    /// and the view shared mesh's name, instance id, isReadable and counts (the shared mesh is unreadable in PlayMode, so its hash cannot be taken here; SurvivorRestPrint prints the hash of the asset mesh read in EditMode; if the two hashes differ the baton 16 triangle mapping is void).
    /// T2 renders the body-only pass again with a copy of the body material at _Cull 0 and says whether each pinhole pixel is then drawn (drawn = a back-facing GPU triangle covers it; still magenta = no GPU triangle covers it). T3 reads the SKINNED GPU vertex buffer
    /// (SkinnedMeshRenderer.GetVertexBuffer with vertexBufferTarget Raw; layout and space printed) for the ray's winner triangle and its 1-ring (triangles sharing a vertex index) and prints, per vertex, the GPU and the bake positions (the renderer's local skinned space moved to world by position + rotation, as every bake in these tests),
    /// the difference in millimetres and the normal-to-ray dots; CONTROL: the triangle under the body pixel nearest the image centre must agree within 0.1 mm, else T3 is VOID. T4 prints the skinning settings. T5: runs\RUN\pinhole-d3\d3.txt, nothing asserted.
    /// </summary>
    public class PinholeD3 : StanceFixture
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        static string F(float v) => v.ToString("R", Inv);
        static string P3(Vector3 v) => v.x.ToString("F6", Inv) + ", " + v.y.ToString("F6", Inv) + ", " + v.z.ToString("F6", Inv);

        public static uint Fnv(int[] triangles)
        {
            uint h = 2166136261u;
            unchecked
            {
                foreach (int x in triangles) { uint u = (uint)x; for (int b = 0; b < 4; b++) { h ^= (u >> (8 * b)) & 0xFFu; h *= 16777619u; } }
            }
            return h;
        }

        struct PTri { public Vector3 a, b, c; public int index; }

        static List<PTri> Bake(SkinnedMeshRenderer smr, Mesh baked)
        {
            var tris = new List<PTri>(40000);
            var v = baked.vertices; var t = baked.triangles;
            Vector3 pos = smr.transform.position; Quaternion rot = smr.transform.rotation;
            for (int i = 0; i < t.Length; i += 3)
                tris.Add(new PTri { a = pos + rot * v[t[i]], b = pos + rot * v[t[i + 1]], c = pos + rot * v[t[i + 2]], index = i / 3 });
            return tris;
        }

        static bool RayTri(Vector3 o, Vector3 d, PTri tri, out float t, out bool back)
        {
            t = 0f; back = false;
            Vector3 e1 = tri.b - tri.a, e2 = tri.c - tri.a, p = Vector3.Cross(d, e2);
            float det = Vector3.Dot(e1, p);
            if (Mathf.Abs(det) < 1e-9f) return false;
            float inv = 1f / det;
            Vector3 sv = o - tri.a;
            float u = Vector3.Dot(sv, p) * inv;
            if (u < 0f || u > 1f) return false;
            Vector3 q = Vector3.Cross(sv, e1);
            float v = Vector3.Dot(d, q) * inv;
            if (v < 0f || u + v > 1f) return false;
            t = Vector3.Dot(e2, q) * inv;
            if (t < 0f) return false;
            back = Vector3.Dot(Vector3.Cross(e1, e2), d) > 0f;
            return true;
        }

        static Ray PixelRay(Camera cam, int x, int yTop) => cam.ViewportPointToRay(new Vector3((x + 0.5f) / 960f, (539 - yTop + 0.5f) / 540f, 0f));

        /// <summary>The R3 winner of a ray (MirrorTie) and every hit; winnerIndex is -1 when the ray meets no triangle.</summary>
        static (int winnerIndex, bool back, float t, int hits) Cast(List<PTri> tris, Ray ray)
        {
            float best = float.MaxValue; bool backFace = false; PTri bt = default; int n = 0; int w = -1;
            foreach (var tri in tris)
            {
                if (!RayTri(ray.origin, ray.direction, tri, out float t, out bool back)) continue;
                n++;
                if (MirrorTie.Replaces(best < float.MaxValue, best, backFace, bt.a, bt.b, bt.c, t, back, tri.a, tri.b, tri.c)) { best = t; backFace = back; bt = tri; w = tri.index; }
            }
            return (w, backFace, best, n);
        }

        /// <summary>A body-only render of the first-person camera: only the Player layer, solid magenta. Returns the pixels (bottom-origin) and the magenta mask.</summary>
        static bool[] BodyOnlyMask(Camera source, int layer, out Texture2D tex)
        {
            var go = new GameObject("PinholeD3BodyOnly");
            try
            {
                var c = go.AddComponent<Camera>();
                c.CopyFrom(source);
                c.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
                c.cullingMask = 1 << layer;
                c.clearFlags = CameraClearFlags.SolidColor;
                c.backgroundColor = new Color(1f, 0f, 1f, 1f);
                var rt = new RenderTexture(960, 540, 24);
                c.targetTexture = rt;
                c.Render();
                RenderTexture.active = rt;
                tex = new Texture2D(960, 540, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, 960, 540), 0, 0);
                tex.Apply();
                RenderTexture.active = null;
                c.targetTexture = null;
                rt.Release();
                Object.Destroy(rt);
                var px = tex.GetPixels32();
                var bg = new bool[px.Length];
                for (int i = 0; i < px.Length; i++) bg[i] = px[i].r > 229 && px[i].g < 26 && px[i].b > 229;
                return bg;
            }
            finally { Object.Destroy(go); }
        }

        static bool Magenta(bool[] bg, int x, int yTop) => bg[(539 - yTop) * 960 + x];

        static int BodyNeighbours(bool[] bg, int x, int yTop)
        {
            int n = 0;
            for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0) continue;
                int xx = x + dx, yy = yTop + dy;
                if (xx < 0 || xx >= 960 || yy < 0 || yy >= 540) continue;
                if (!Magenta(bg, xx, yy)) n++;
            }
            return n;
        }

        static string Dp(Vector3 a, Vector3 b) => $"{((a - b).magnitude * 1000f).ToString("F3", Inv)} mm";

        [UnityTest, Explicit("Print only: D3 pinhole diagnostic (index identity, cull-off render, GPU against bake vertices) at Sit +75 and Squat +85"), Timeout(1800000)]
        public IEnumerator PinholeD3_SitAndSquat()
        {
            PinTicks();
            string dir = RunOutput.Dir("pinhole-d3");
            var o = new StringBuilder("# " + MirrorTie.StateLine + "; pinhole D3 (td B26 + B28 R1-R3)" + System.Environment.NewLine);
            var poses = new (string name, StanceKey key, Stance stance, float pitch)[]
            {
                ("Sit_Loop_p75", StanceKey.Sit, Stance.Sit, 75f),
                ("Squat_Hold_p85", StanceKey.Squat, Stance.Squat, 85f),
            };
            try
            {
            foreach (var pose in poses)
            {
                yield return LoadWorld();
                yield return OnFloor();
                var view = player.GetComponent<PlayerCharacterView>();
                Assert.IsNotNull(view?.FirstPerson, "no first-person body on the Player");
                var fp = view.FirstPerson;
                yield return Go(pose.key, 6f);
                Assert.AreEqual(pose.stance, player.Stance, $"{pose.name}: could not reach {pose.stance}");
                var smr = fp.ViewRenderers.OfType<SkinnedMeshRenderer>().FirstOrDefault(r => r.enabled && r.sharedMesh != null);
                Assert.IsNotNull(smr, "no enabled view SkinnedMeshRenderer");
                smr.vertexBufferTarget |= GraphicsBuffer.Target.Raw;   // so GetVertexBuffer can expose the skinned vertices; set before the frames below skin the mesh
                yield return new WaitForSeconds(2f);
                player.SetLook(player.Yaw, pose.pitch);
                yield return null;
                yield return null;
                var va = view.ViewCopy != null && view.ViewCopy.ViewAnimator != null ? view.ViewCopy.ViewAnimator : view.Body.Animator;
                o.AppendLine($"== {pose.name}: stance {player.Stance}, pitch {pose.pitch:F0}; retry each frame up to 300 until a frame has >= 1 pinhole (td B28 R3)");
                float tMin = float.MaxValue, tMax = float.MinValue; bool found = false, failed = false;
                for (int attempt = 0; attempt < 300; attempt++)
                {
                    float nt = va.GetCurrentAnimatorStateInfo(0).normalizedTime;
                    tMin = Mathf.Min(tMin, nt); tMax = Mathf.Max(tMax, nt);
                    // ---- same frame from here to the end of SameFrame: no yield ----
                    int count = 0;
                    try { count = SameFrame(o, player.ViewCamera, smr, pose.name, attempt, nt); }
                    catch (System.Exception e) { o.AppendLine($"EXCEPTION in {pose.name} attempt {attempt}: {e.GetType().Name}: {e.Message}{System.Environment.NewLine}{e.StackTrace}"); failed = true; }
                    if (failed) break;
                    if (count > 0) { found = true; break; }
                    yield return null;
                }
                if (failed) continue;
                if (!found) o.AppendLine($"NOT REPRODUCED: 300 frames, 0 pinhole pixels in any; view animator normalizedTime covered {tMin.ToString("F4", Inv)} .. {tMax.ToString("F4", Inv)}");
            }
            }
            finally
            {
                File.WriteAllText($"{dir}/d3.txt", o.ToString());   // td B28e F2: the file is written even when a setup step throws
                Debug.Log("PinholeD3 written to " + dir);
            }
        }

        /// <summary>One attempt in ONE frame: fresh lit render, body-only render, pinhole count. Returns the count; when it is 0 only one line is printed, else the full T0-T4 analysis follows (still the same frame).</summary>
        int SameFrame(StringBuilder o, Camera cam, SkinnedMeshRenderer smr, string poseName, int attempt, float normalizedTime)
        {
            cam.aspect = 960f / 540f;
            try
            {
                var rtLit = new RenderTexture(960, 540, 24);
                var oldT = cam.targetTexture; cam.targetTexture = rtLit; cam.Render(); cam.targetTexture = oldT; rtLit.Release(); Object.Destroy(rtLit);
                var bg = BodyOnlyMask(cam, player.gameObject.layer, out var bodyTex);
                Object.Destroy(bodyTex);
                var pinholes = new List<(int x, int yTop)>();
                for (int yy = 1; yy < 539; yy++) for (int xx = 1; xx < 959; xx++) if (Magenta(bg, xx, yy) && BodyNeighbours(bg, xx, yy) == 8) pinholes.Add((xx, yy));
                o.AppendLine($"attempt {attempt}: frame {Time.frameCount}, view normalizedTime {normalizedTime.ToString("F4", Inv)}, pinhole pixels {pinholes.Count}");
                var spot = poseName.StartsWith("Sit") ? (x: 391, yTop: 397) : (x: 373, yTop: 490);
                {
                    var sm = new Mesh();
                    smr.BakeMesh(sm, false);
                    var stris = Bake(smr, sm);
                    Object.Destroy(sm);
                    var sw = Cast(stris, PixelRay(cam, spot.x, spot.yTop));
                    o.AppendLine($"  known spot ({spot.x}, {spot.yTop}): magenta {Magenta(bg, spot.x, spot.yTop)}, body neighbours {BodyNeighbours(bg, spot.x, spot.yTop)} of 8, R3 ray: {(sw.winnerIndex < 0 ? "no body hit" : $"winner triangle #{sw.winnerIndex} {(sw.back ? "back" : "front")} t {sw.t.ToString("F4", Inv)}, {sw.hits} hits")}");
                }
                if (pinholes.Count == 0) return 0;
                o.AppendLine($"ANALYSING the first frame with a pinhole: attempt {attempt}, frame {Time.frameCount}");
                Analyse(o, cam, smr, bg, pinholes, spot);
                return pinholes.Count;
            }
            finally { cam.ResetAspect(); }
        }

        void Analyse(StringBuilder o, Camera cam, SkinnedMeshRenderer smr, bool[] bg, List<(int x, int yTop)> pinholes, (int x, int yTop) spot)
        {
            var baked = new Mesh();
            smr.BakeMesh(baked, false);
            var tris = Bake(smr, baked);
            var shared = smr.sharedMesh;
            // T1
            o.AppendLine($"T1 index identity: bake mesh '{baked.name}' triangles {baked.triangles.Length / 3} vertices {baked.vertexCount} FNV-1a {Fnv(baked.triangles):x8}; view shared mesh '{shared.name}' entityId {shared.GetEntityId()} isReadable {shared.isReadable} vertices {shared.vertexCount} triangles {shared.GetIndexCount(0) / 3} (its hash is taken in EditMode by SurvivorRestPrint, same function; equal hashes = the same index order)");
            // T4
            string gpuSkin = "unknown (UnityEditor.PlayerSettings not reachable)";
            var ps = System.Type.GetType("UnityEditor.PlayerSettings, UnityEditor");
            if (ps != null) { var pr = ps.GetProperty("gpuSkinning"); if (pr != null) gpuSkin = pr.GetValue(null)?.ToString(); }
            string bpv = "unreadable in PlayMode";
            try { var per = shared.GetBonesPerVertex(); bpv = $"{per.Length} entries"; } catch (System.Exception) { }
            o.AppendLine($"T4 skinning: QualitySettings.skinWeights {QualitySettings.skinWeights}, smr.quality {smr.quality}, updateWhenOffscreen {smr.updateWhenOffscreen}, PlayerSettings.gpuSkinning {gpuSkin}, supportsComputeShaders {SystemInfo.supportsComputeShaders}, graphicsDeviceType {SystemInfo.graphicsDeviceType}, renderer lossyScale {P3(smr.transform.lossyScale)}, bones {smr.bones.Length}, bones per vertex of the shared mesh: {bpv}, vertexBufferTarget {smr.vertexBufferTarget}, rootBone {(smr.rootBone != null ? smr.rootBone.name : "none")}");
            // GPU buffer (R1: the skinned output buffer has its OWN layout: stride from the buffer, position at float 0, normal at float 3; the shared mesh offsets are printed for reference only)
            float[] gpu = null; int stride = 0, bufCount = 0, bufStrideBytes = 0; string gpuNote = "";
            GraphicsBuffer gb = null;
            try { gb = smr.GetVertexBuffer(); } catch (System.Exception e) { gpuNote = "GetVertexBuffer threw: " + e.Message; }
            if (gb != null)
            {
                stride = gb.stride / 4;
                gpu = new float[gb.count * gb.stride / 4];
                gb.GetData(gpu);
                bufCount = gb.count; bufStrideBytes = gb.stride;
                gb.Dispose();
                int st = shared.GetVertexAttributeStream(VertexAttribute.Position);
                gpuNote = $"skinned buffer: {bufCount} elements of {bufStrideBytes} bytes, stride {stride} floats (from the buffer), position read at float 0, normal at float 3; for reference only, the SHARED mesh has stream {st}, stride {shared.GetVertexBufferStride(st) / 4} floats, position offset {shared.GetVertexAttributeOffset(VertexAttribute.Position) / 4}, normal offset {shared.GetVertexAttributeOffset(VertexAttribute.Normal) / 4}, normal format {shared.GetVertexAttributeFormat(VertexAttribute.Normal)}";
            }
            else if (gpuNote.Length == 0) gpuNote = "GetVertexBuffer returned null (CPU skinning, or no skinned buffer yet)";
            o.AppendLine("T3 GPU buffer: " + gpuNote);
            Vector3 pos = smr.transform.position; Quaternion rot = smr.transform.rotation;
            var root = smr.rootBone;
            if (gpu != null)
            {
                var b0 = baked.vertices; var n0 = baked.normals;
                o.AppendLine($"A1 buffer sanity: gb.count {bufCount} (shared mesh vertices {shared.vertexCount}, bake vertices {baked.vertexCount}, expected 35940), gb.stride {bufStrideBytes} bytes; buffer vertex 0 first 10 floats: {string.Join(", ", Enumerable.Range(0, System.Math.Min(10, gpu.Length)).Select(i => gpu[i].ToString("R", Inv)))}; bake vertex 0 local position {P3(b0[0])}, local normal {P3(n0[0])}");
            }
            // R2: three candidate conversions of the buffer's local position to world: 0 = smr.transform pos+rot, 1 = rootBone.localToWorldMatrix, 2 = rootBone pos+rot
            string[] convName = { "smr.transform position + rotation", "rootBone.localToWorldMatrix", "rootBone position + rotation" };
            Vector3 GpuLocal(int vi) => new Vector3(gpu[vi * stride], gpu[vi * stride + 1], gpu[vi * stride + 2]);
            Vector3 GpuNLocal(int vi) => new Vector3(gpu[vi * stride + 3], gpu[vi * stride + 4], gpu[vi * stride + 5]);
            Vector3 ToWorld(int conv, Vector3 l)
            {
                if (conv == 0 || root == null) return pos + rot * l;
                if (conv == 1) return root.localToWorldMatrix.MultiplyPoint3x4(l);
                return root.position + root.rotation * l;
            }
            Vector3 NToWorld(int conv, Vector3 l)
            {
                if (conv == 0 || root == null) return rot * l;
                if (conv == 1) return root.localToWorldMatrix.MultiplyVector(l).normalized;
                return root.rotation * l;
            }
            var bv = baked.vertices; var bn = baked.normals; var bt = baked.triangles;
            Vector3 BakeW(int vi) => pos + rot * bv[vi];
            float MaxDiff(int tIndex, int conv)
            {
                float m = 0f;
                for (int k = 0; k < 3; k++) { int vi = bt[3 * tIndex + k]; m = Mathf.Max(m, (ToWorld(conv, GpuLocal(vi)) - BakeW(vi)).magnitude); }
                return m * 1000f;
            }
            string CompareTriangle(int tIndex, Ray ray, int conv)
            {
                var sb = new StringBuilder();
                for (int k = 0; k < 3; k++)
                {
                    int vi = bt[3 * tIndex + k];
                    Vector3 bw = BakeW(vi);
                    if (gpu == null) { sb.AppendLine($"      vertex {vi}: bake world {P3(bw)}; GPU buffer unavailable"); continue; }
                    Vector3 gw = ToWorld(conv, GpuLocal(vi));
                    sb.AppendLine($"      vertex {vi}: GPU world {P3(gw)}; bake world {P3(bw)}; |diff| {Dp(gw, bw)}; normal.ray GPU {Vector3.Dot(NToWorld(conv, GpuNLocal(vi)), ray.direction).ToString("F3", Inv)}, bake {Vector3.Dot(rot * bn[vi], ray.direction).ToString("F3", Inv)}");
                }
                return sb.ToString().TrimEnd();
            }
            // CONTROL: the triangle under the body pixel nearest the image centre, converted all three ways; the first within 0.1 mm is used
            int useConv = -1;
            {
                // A3: the body pixel nearest the centre whose R3 winner is a FRONT face with |geometric normal . ray| >= 0.3, searching outward (at most 400 candidates)
                var cand = new List<(double d, int x, int y)>();
                for (int yy = 0; yy < 540; yy++) for (int xx = 0; xx < 960; xx++) if (!Magenta(bg, xx, yy)) cand.Add(((xx - 480.0) * (xx - 480.0) + (yy - 270.0) * (yy - 270.0), xx, yy));
                cand.Sort((p, q) => p.d.CompareTo(q.d));
                int bx = -1, by = -1, tried = 0;
                Ray cr = default; (int winnerIndex, bool back, float t, int hits) cw = (-1, false, 0f, 0);
                foreach (var cd in cand)
                {
                    if (tried++ >= 400) break;
                    var r = PixelRay(cam, cd.x, cd.y);
                    var w0 = Cast(tris, r);
                    if (w0.winnerIndex < 0 || w0.back) continue;
                    var tr = tris[w0.winnerIndex];
                    if (Mathf.Abs(Vector3.Dot(Vector3.Cross(tr.b - tr.a, tr.c - tr.a).normalized, r.direction)) < 0.3f) continue;
                    bx = cd.x; by = cd.y; cr = r; cw = w0; break;
                }
                if (bx < 0) cw = (-1, false, 0f, 0);
                o.AppendLine($"CONTROL: body pixel nearest the centre with a front-face winner and |normal.ray| >= 0.3 ({bx}, {by}; {tried} candidates tried): R3 winner triangle #{cw.winnerIndex} ({(cw.back ? "back" : "front")}, {cw.hits} hits); rootBone {(root != null ? root.name : "none")}");
                if (gpu != null && cw.winnerIndex >= 0)
                {
                    for (int conv = 0; conv < 3; conv++)
                    {
                        float md = MaxDiff(cw.winnerIndex, conv);
                        o.AppendLine($"CONTROL conversion {conv} ({convName[conv]}): max |GPU - bake| over its 3 vertices {md.ToString("F4", Inv)} mm");
                        if (useConv < 0 && md <= 0.1f) useConv = conv;
                    }
                    o.AppendLine(CompareTriangle(cw.winnerIndex, cr, useConv < 0 ? 0 : useConv));
                    o.AppendLine(useConv < 0 ? "CONTROL: no conversion within 0.1 mm -> T3 VOID" : $"CONTROL bites with conversion {useConv} ({convName[useConv]}): used below");
                }
                else o.AppendLine("CONTROL: no GPU buffer or no winner -> T3 VOID");
            }
            bool voidT3 = useConv < 0;
            o.AppendLine($"T0 pinhole pixels (magenta with all 8 neighbours drawn body) in this frame: {pinholes.Count}");
            var targets = new List<(int x, int yTop, string label)>();
            foreach (var ph in pinholes) targets.Add((ph.x, ph.yTop, "pinhole"));
            if (!pinholes.Contains(spot)) targets.Add((spot.x, spot.yTop, "known spot (not a pinhole in this frame)"));
            foreach (var ph in targets)
            {
                var ray = PixelRay(cam, ph.x, ph.yTop);
                var w = Cast(tris, ray);
                o.AppendLine($"  {ph.label} ({ph.x}, {ph.yTop}): R3 ray: {(w.winnerIndex < 0 ? "no body hit" : $"winner triangle #{w.winnerIndex} {(w.back ? "back" : "front")} t {w.t.ToString("F4", Inv)}, {w.hits} hits")}");
                if (w.winnerIndex < 0) continue;
                var winnerVerts = new HashSet<int> { bt[3 * w.winnerIndex], bt[3 * w.winnerIndex + 1], bt[3 * w.winnerIndex + 2] };
                var ring = new List<int>();
                for (int tt = 0; tt < bt.Length / 3; tt++) if (tt != w.winnerIndex && (winnerVerts.Contains(bt[3 * tt]) || winnerVerts.Contains(bt[3 * tt + 1]) || winnerVerts.Contains(bt[3 * tt + 2]))) ring.Add(tt);
                o.AppendLine($"    winner triangle #{w.winnerIndex}: vertex indices {string.Join(", ", winnerVerts)}; 1-ring (share a vertex index) {ring.Count} triangles");
                o.AppendLine("    T3 " + (voidT3 ? "(VOID control; values shown for reference, conversion 0)" : "(control bites)") + ":");
                int cv = voidT3 ? 0 : useConv;
                o.AppendLine(CompareTriangle(w.winnerIndex, ray, cv));
                foreach (int tt in ring) { o.AppendLine($"    ring triangle #{tt}:"); o.AppendLine(CompareTriangle(tt, ray, cv)); }
            }
            // T2: cull-off render of the body-only pass
            var originals = smr.sharedMaterials;
            var copies = smr.materials;   // instances
            int withCull = copies.Count(m => m != null && m.HasProperty("_Cull"));
            foreach (var m in copies) if (m != null && m.HasProperty("_Cull")) m.SetInt("_Cull", 0);
            var bgOff = BodyOnlyMask(cam, player.gameObject.layer, out var offTex);
            Object.Destroy(offTex);
            smr.sharedMaterials = originals;
            foreach (var m in copies) if (m != null) Object.Destroy(m);
            int enclosedOff = 0;
            for (int yy = 1; yy < 539; yy++) for (int xx = 1; xx < 959; xx++) if (Magenta(bgOff, xx, yy) && BodyNeighbours(bgOff, xx, yy) == 8) enclosedOff++;
            o.AppendLine($"T2 cull-off body-only render (_Cull 0 on a copy of the view materials, restored after; materials with a _Cull property: {withCull} of {copies.Length}): enclosed magenta pixels now {enclosedOff} (was {pinholes.Count})");
            foreach (var ph in pinholes) o.AppendLine($"  pinhole ({ph.x}, {ph.yTop}): with cull OFF it is {(Magenta(bgOff, ph.x, ph.yTop) ? "STILL MAGENTA (no GPU triangle covers it)" : "DRAWN (a back-facing GPU triangle covers it)")}");
            Object.Destroy(baked);
        }
    }
}
