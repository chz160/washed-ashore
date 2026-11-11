using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using WashedAshore.Character.Stance;
using WashedAshore.Gameplay;
using WashedAshore.Tests.Support;

namespace WashedAshore.Tests.PlayMode.Character
{
    /// <summary>
    /// PRINT ONLY, [Explicit] (td-49 B29 D4), on the PinholeD3 harness (same pose loop, attempt loop of up to 300 frames until a pinhole, same-frame block, exception containment and d3-style finally write kept).
    /// Why is the pinhole not drawn although the R3 ray through its centre hits a front triangle and no GPU triangle covers it (D3)? All in the SAME frame as a fresh lit render and the 960x540 body-only pass.
    /// U1 SUPERSAMPLE: a body-only pass at 3840x2160 (4x per axis); per pinhole the 4x window of +-16 supersample pixels as an M (magenta) / . (body) map, the magenta counts, and whether the magenta of the pinhole's own 4x4 block connects to the image border (the background).
    /// U2 REPROJECTION: per pinhole and for the control, the R3 hit point through Camera.WorldToViewportPoint x (960, 540) against the sample centre (x + 0.5, y + 0.5): the offset in pixels, the barycentrics of the sample in the projected winner triangle, and its distance to the nearest edge in pixels.
    /// U3 LOCAL TOPOLOGY at the sample centre: every triangle with a projected vertex within 3 px, in 2D; those whose footprint CONTAINS the sample are printed; else the 8 nearest edges (distance, endpoint indices and projected positions, partners by index and by posed position at 1e-6) flagged UNSHARED EDGE or SPLIT EDGE.
    /// U5 DOUBLE (td B29a, B29b): the projections, barycentrics and edge distances of U2/U3 are computed in double from a view-projection built from the printed camera position, rotation, FOV and aspect (not WorldToViewportPoint), with the float values and their difference printed beside them; the camera inputs are printed. U6: ALL hits of the pinhole ray (t to 7 decimals, triangle, front/back, double barycentrics, double-model ray, edge-graze test for pairs) and a READING line (outside or within 0.05 px of an edge of every hit = raster edge case; well inside a hit triangle = escalate).
    /// U4 CONTROL: the control pixel (nearest the centre, front-face R3 winner with |normal.ray| >= 0.3) must show at least one containing triangle and a reprojection offset of at most 0.05 px, else "U VOID". Output: runs\RUN\pinhole-d4\d4.txt. Nothing is asserted beyond reaching the pose.
    /// </summary>
    public class PinholeD4 : StanceFixture
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        static string F(float v) => v.ToString("R", Inv);
        static string P3(Vector3 v) => v.x.ToString("F6", Inv) + ", " + v.y.ToString("F6", Inv) + ", " + v.z.ToString("F6", Inv);
        static string P2(Vector2 v) => "(" + v.x.ToString("F4", Inv) + ", " + v.y.ToString("F4", Inv) + ")";

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

        /// <summary>The R3 winner of a ray (MirrorTie) and the hit count; winnerIndex is -1 when the ray meets no triangle.</summary>
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

        /// <summary>A body-only render of a copy of the first-person camera at w x h: only the Player layer, solid magenta. Returns the bottom-origin pixels.</summary>
        static Color32[] RenderBodyOnly(Camera source, int layer, int w, int h)
        {
            var go = new GameObject("PinholeD4BodyOnly");
            try
            {
                var c = go.AddComponent<Camera>();
                c.CopyFrom(source);
                c.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
                c.cullingMask = 1 << layer;
                c.clearFlags = CameraClearFlags.SolidColor;
                c.backgroundColor = new Color(1f, 0f, 1f, 1f);
                var rt = new RenderTexture(w, h, 24);
                c.targetTexture = rt;
                c.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply();
                RenderTexture.active = null;
                c.targetTexture = null;
                rt.Release();
                Object.Destroy(rt);
                var px = tex.GetPixels32();
                Object.Destroy(tex);
                return px;
            }
            finally { Object.Destroy(go); }
        }

        static bool[] ToMask(Color32[] px)
        {
            var m = new bool[px.Length];
            for (int i = 0; i < px.Length; i++) m[i] = px[i].r > 229 && px[i].g < 26 && px[i].b > 229;
            return m;
        }

        static bool Mag(bool[] m, int w, int h, int x, int yTop) => m[(h - 1 - yTop) * w + x];

        static int BodyNeighbours(bool[] bg, int x, int yTop)
        {
            int n = 0;
            for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0) continue;
                int xx = x + dx, yy = yTop + dy;
                if (xx < 0 || xx >= 960 || yy < 0 || yy >= 540) continue;
                if (!Mag(bg, 960, 540, xx, yy)) n++;
            }
            return n;
        }

        static bool Bary(Vector2 p, Vector2 a, Vector2 b, Vector2 c, out Vector3 uvw)
        {
            uvw = default;
            float d = (b.y - c.y) * (a.x - c.x) + (c.x - b.x) * (a.y - c.y);
            if (Mathf.Abs(d) < 1e-12f) return false;
            float u = ((b.y - c.y) * (p.x - c.x) + (c.x - b.x) * (p.y - c.y)) / d;
            float v = ((c.y - a.y) * (p.x - c.x) + (a.x - c.x) * (p.y - c.y)) / d;
            uvw = new Vector3(u, v, 1f - u - v);
            return true;
        }

        static float SegDist(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a; float l2 = ab.sqrMagnitude;
            float t = l2 < 1e-18f ? 0f : Mathf.Clamp01(Vector2.Dot(p - a, ab) / l2);
            return (p - (a + t * ab)).magnitude;
        }

        // ---- double precision camera model (td B29a U5, B29b): built from the printed camera position, rotation, FOV and aspect, not from WorldToViewportPoint ----
        sealed class DCam
        {
            public double px, py, pz, tanHalf, aspect;
            public double[] R = new double[9];   // row-major local -> world
            public static DCam Make(Camera cam)
            {
                var q = cam.transform.rotation; double x = q.x, y = q.y, z = q.z, w = q.w;
                var c = new DCam { px = cam.transform.position.x, py = cam.transform.position.y, pz = cam.transform.position.z, tanHalf = System.Math.Tan(cam.fieldOfView * System.Math.PI / 180.0 / 2.0), aspect = 960.0 / 540.0 };
                c.R = new[] { 1 - 2 * (y * y + z * z), 2 * (x * y - w * z), 2 * (x * z + w * y), 2 * (x * y + w * z), 1 - 2 * (x * x + z * z), 2 * (y * z - w * x), 2 * (x * z - w * y), 2 * (y * z + w * x), 1 - 2 * (x * x + y * y) };
                return c;
            }
            /// <summary>Pixel coordinates (y from the bottom) of a world point; false when it is behind the camera.</summary>
            public bool Proj(double X, double Y, double Z, out double sx, out double sy, out double depth)
            {
                double dx = X - px, dy = Y - py, dz = Z - pz;
                double cx = R[0] * dx + R[3] * dy + R[6] * dz, cy = R[1] * dx + R[4] * dy + R[7] * dz, cz = R[2] * dx + R[5] * dy + R[8] * dz;
                depth = cz;
                if (cz <= 1e-9) { sx = 0; sy = 0; return false; }
                sx = (cx / (cz * tanHalf * aspect) * 0.5 + 0.5) * 960.0; sy = (cy / (cz * tanHalf) * 0.5 + 0.5) * 540.0;
                return true;
            }
            /// <summary>Unit world direction of the ray from the camera position through the pixel point (sx, sy from the bottom).</summary>
            public void Ray(double sx, double sy, out double dx, out double dy, out double dz)
            {
                double nx = (sx / 960.0 - 0.5) * 2.0 * tanHalf * aspect, ny = (sy / 540.0 - 0.5) * 2.0 * tanHalf, nz = 1.0;
                double wx = R[0] * nx + R[1] * ny + R[2] * nz, wy = R[3] * nx + R[4] * ny + R[5] * nz, wz = R[6] * nx + R[7] * ny + R[8] * nz;
                double l = System.Math.Sqrt(wx * wx + wy * wy + wz * wz);
                dx = wx / l; dy = wy / l; dz = wz / l;
            }
        }

        static bool BaryD(double px, double py, double ax, double ay, double bx, double by, double cx, double cy, out double u, out double v, out double w)
        {
            u = v = w = 0;
            double d = (by - cy) * (ax - cx) + (cx - bx) * (ay - cy);
            if (System.Math.Abs(d) < 1e-18) return false;
            u = ((by - cy) * (px - cx) + (cx - bx) * (py - cy)) / d;
            v = ((cy - ay) * (px - cx) + (ax - cx) * (py - cy)) / d;
            w = 1.0 - u - v;
            return true;
        }

        static double SegDistD(double px, double py, double ax, double ay, double bx, double by)
        {
            double abx = bx - ax, aby = by - ay, l2 = abx * abx + aby * aby;
            double t = l2 < 1e-24 ? 0.0 : System.Math.Max(0.0, System.Math.Min(1.0, ((px - ax) * abx + (py - ay) * aby) / l2));
            double qx = ax + t * abx - px, qy = ay + t * aby - py;
            return System.Math.Sqrt(qx * qx + qy * qy);
        }

        static string D6(double v) => v.ToString("F6", Inv);
        static string D7(double v) => v.ToString("F7", Inv);

        [UnityTest, Explicit("Print only: D4 pinhole diagnostic (supersample, reprojection in double, all hits, local topology) at Sit +75 and Squat +85"), Timeout(1800000)]
        public IEnumerator PinholeD4_SitAndSquat()
        {
            PinTicks();
            string dir = RunOutput.Dir("pinhole-d4");
            var o = new StringBuilder("# " + MirrorTie.StateLine + "; pinhole D4 (td B29, B29a, B29b: U1-U6)" + System.Environment.NewLine);
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
                yield return new WaitForSeconds(2f);
                player.SetLook(player.Yaw, pose.pitch);
                yield return null;
                yield return null;
                var va = view.ViewCopy != null && view.ViewCopy.ViewAnimator != null ? view.ViewCopy.ViewAnimator : view.Body.Animator;
                o.AppendLine($"== {pose.name}: stance {player.Stance}, pitch {pose.pitch:F0}; retry each frame up to 300 until a frame has >= 1 pinhole");
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
                File.WriteAllText($"{dir}/d4.txt", o.ToString());
                Debug.Log("PinholeD4 written to " + dir);
            }
        }

        /// <summary>One attempt in ONE frame: fresh lit render, body-only render, pinhole count and the known spot's R3 ray; with at least one pinhole the full U1-U6 analysis follows in the same frame. Returns the pinhole count.</summary>
        int SameFrame(StringBuilder o, Camera cam, SkinnedMeshRenderer smr, string poseName, int attempt, float normalizedTime)
        {
            cam.aspect = 960f / 540f;
            try
            {
                var rtLit = new RenderTexture(960, 540, 24);
                var oldT = cam.targetTexture; cam.targetTexture = rtLit; cam.Render(); cam.targetTexture = oldT; rtLit.Release(); Object.Destroy(rtLit);
                var bg = ToMask(RenderBodyOnly(cam, player.gameObject.layer, 960, 540));
                var pinholes = new List<(int x, int yTop)>();
                for (int yy = 1; yy < 539; yy++) for (int xx = 1; xx < 959; xx++) if (Mag(bg, 960, 540, xx, yy) && BodyNeighbours(bg, xx, yy) == 8) pinholes.Add((xx, yy));
                o.AppendLine($"attempt {attempt}: frame {Time.frameCount}, view normalizedTime {normalizedTime.ToString("F4", Inv)}, pinhole pixels {pinholes.Count}");
                var spot = poseName.StartsWith("Sit") ? (x: 391, yTop: 397) : (x: 373, yTop: 490);
                {
                    var sm = new Mesh();
                    smr.BakeMesh(sm, false);
                    var stris = Bake(smr, sm);
                    Object.Destroy(sm);
                    var sw = Cast(stris, PixelRay(cam, spot.x, spot.yTop));
                    o.AppendLine($"  known spot ({spot.x}, {spot.yTop}): magenta {Mag(bg, 960, 540, spot.x, spot.yTop)}, body neighbours {BodyNeighbours(bg, spot.x, spot.yTop)} of 8, R3 ray: {(sw.winnerIndex < 0 ? "no body hit" : $"winner triangle #{sw.winnerIndex} {(sw.back ? "back" : "front")} t {sw.t.ToString("F4", Inv)} m, {sw.hits} hits")}");
                }
                if (pinholes.Count == 0) return 0;
                o.AppendLine($"ANALYSING the first frame with a pinhole: attempt {attempt}, frame {Time.frameCount}");
                Analyse4(o, cam, smr, bg, pinholes, spot);
                return pinholes.Count;
            }
            finally { cam.ResetAspect(); }
        }

        void Analyse4(StringBuilder o, Camera cam, SkinnedMeshRenderer smr, bool[] bg, List<(int x, int yTop)> pinholes, (int x, int yTop) spot)
        {
            var baked = new Mesh();
            smr.BakeMesh(baked, false);
            var tris = Bake(smr, baked);
            var bv = baked.vertices; var bt = baked.triangles;
            Vector3 pos = smr.transform.position; Quaternion rot = smr.transform.rotation;
            int nv = bv.Length, nt = bt.Length / 3;
            // U5: camera inputs and the double model
            var dc = DCam.Make(cam);
            var ct = cam.transform;
            o.AppendLine($"U5 camera: position ({ct.position.x.ToString("R", Inv)}, {ct.position.y.ToString("R", Inv)}, {ct.position.z.ToString("R", Inv)}), |position| {ct.position.magnitude.ToString("F4", Inv)} m; rotation (x,y,z,w) ({ct.rotation.x.ToString("R", Inv)}, {ct.rotation.y.ToString("R", Inv)}, {ct.rotation.z.ToString("R", Inv)}, {ct.rotation.w.ToString("R", Inv)}), euler {P3(ct.eulerAngles)}; fieldOfView {cam.fieldOfView.ToString("R", Inv)}; aspect {cam.aspect.ToString("R", Inv)} (set to 960/540 for this frame); near {cam.nearClipPlane.ToString("R", Inv)}, far {cam.farClipPlane.ToString("R", Inv)}; usePhysicalProperties {cam.usePhysicalProperties}; pixelRect {cam.pixelRect}; float32 step at |position| about {(Mathf.Abs(ct.position.x) * 1.19e-7f * 1000f).ToString("F4", Inv)} mm (x)");
            var pm = cam.projectionMatrix;
            o.AppendLine($"U5 projectionMatrix m00 {pm.m00.ToString("R", Inv)} (double model {(1.0 / (dc.tanHalf * dc.aspect)).ToString("R", Inv)}), m11 {pm.m11.ToString("R", Inv)} (double model {(1.0 / dc.tanHalf).ToString("R", Inv)}), m02 {pm.m02.ToString("R", Inv)}, m12 {pm.m12.ToString("R", Inv)} (a non-zero m02/m12 would mean a lens shift or oblique projection the double model does not have)");
            var pw = new Vector3[nv]; var pv = new Vector2[nv]; var inFront = new bool[nv];
            var pwx = new double[nv]; var pwy = new double[nv]; var pwz = new double[nv];
            var pdx = new double[nv]; var pdy = new double[nv]; var inFrontD = new bool[nv];
            var smrQ = rot; double qx = smrQ.x, qy = smrQ.y, qz = smrQ.z, qw = smrQ.w;
            double[] rs = { 1 - 2 * (qy * qy + qz * qz), 2 * (qx * qy - qw * qz), 2 * (qx * qz + qw * qy), 2 * (qx * qy + qw * qz), 1 - 2 * (qx * qx + qz * qz), 2 * (qy * qz - qw * qx), 2 * (qx * qz - qw * qy), 2 * (qy * qz + qw * qx), 1 - 2 * (qx * qx + qy * qy) };
            for (int i = 0; i < nv; i++)
            {
                pw[i] = pos + rot * bv[i];
                var vp = cam.WorldToViewportPoint(pw[i]);
                pv[i] = new Vector2(vp.x * 960f, vp.y * 540f);   // float, pixels, y from the BOTTOM
                inFront[i] = vp.z > 0f;
                double lx = bv[i].x, ly = bv[i].y, lz = bv[i].z;
                pwx[i] = pos.x + rs[0] * lx + rs[1] * ly + rs[2] * lz; pwy[i] = pos.y + rs[3] * lx + rs[4] * ly + rs[5] * lz; pwz[i] = pos.z + rs[6] * lx + rs[7] * ly + rs[8] * lz;
                inFrontD[i] = dc.Proj(pwx[i], pwy[i], pwz[i], out pdx[i], out pdy[i], out _);
            }
            var vtris = new List<int>[nv];
            for (int t = 0; t < nt; t++) for (int k = 0; k < 3; k++) { int vi = bt[3 * t + k]; (vtris[vi] ??= new List<int>(6)).Add(t); }
            int behind = 0;
            for (int t = 0; t < nt; t++) if (!inFrontD[bt[3 * t]] || !inFrontD[bt[3 * t + 1]] || !inFrontD[bt[3 * t + 2]]) behind++;
            o.AppendLine($"triangles with at least one vertex behind the camera (their behind-camera vertices are skipped by the 3 px near-test): {behind} of {nt}");
            o.AppendLine($"setup: bake vertices {nv}, triangles {nt}; projections in pixels (960 x 540), y from the BOTTOM; 'float' = Camera.WorldToViewportPoint of the float world point, 'double' = the double model above of the double world point (smr position + rotation applied to the bake vertex in double); sample centre of pixel (x, yTop) = (x + 0.5, 539.5 - yTop)");

            // U4 control pixel: nearest the centre, front-face R3 winner, |geometric normal . ray| >= 0.3 (at most 400 candidates)
            int cx = -1, cy = -1, tried = 0;
            {
                var cand = new List<(double d, int x, int y)>();
                for (int yy = 0; yy < 540; yy++) for (int xx = 0; xx < 960; xx++) if (!Mag(bg, 960, 540, xx, yy)) cand.Add(((xx - 480.0) * (xx - 480.0) + (yy - 270.0) * (yy - 270.0), xx, yy));
                cand.Sort((p, q) => p.d.CompareTo(q.d));
                foreach (var cd in cand)
                {
                    if (tried++ >= 400) break;
                    var r = PixelRay(cam, cd.x, cd.y);
                    var w0 = Cast(tris, r);
                    if (w0.winnerIndex < 0 || w0.back) continue;
                    var tr = tris[w0.winnerIndex];
                    if (Mathf.Abs(Vector3.Dot(Vector3.Cross(tr.b - tr.a, tr.c - tr.a).normalized, r.direction)) < 0.3f) continue;
                    cx = cd.x; cy = cd.y; break;
                }
            }

            string Facing2D(int t)
            {
                int a = bt[3 * t], b = bt[3 * t + 1], c = bt[3 * t + 2];
                double area = (pdx[b] - pdx[a]) * (pdy[c] - pdy[a]) - (pdy[b] - pdy[a]) * (pdx[c] - pdx[a]);   // y up: negative = clockwise on screen = front in Unity
                return (area < 0 ? "FRONT" : "BACK") + " (double signed area " + area.ToString("F4", Inv) + " px^2)";
            }

            // U2/U3/U5/U6 probe of one pixel sample centre
            (int containing, double offset) Probe(string label, int px, int py, bool control, bool allHits)
            {
                double Sx = px + 0.5, Sy = 539.5 - py;
                var ray = PixelRay(cam, px, py);
                var w = Cast(tris, ray);
                o.AppendLine($"  {label} ({px}, {py}) sample centre ({D6(Sx)}, {D6(Sy)}): R3 ray: {(w.winnerIndex < 0 ? "no body hit" : $"winner triangle #{w.winnerIndex} {(w.back ? "back" : "front")} t {w.t.ToString("F7", Inv)} m, {w.hits} hits")}");
                dc.Ray(Sx, Sy, out double rdx, out double rdy, out double rdz);
                o.AppendLine($"    Unity ray: origin {P3(ray.origin)}, direction ({ray.direction.x.ToString("R", Inv)}, {ray.direction.y.ToString("R", Inv)}, {ray.direction.z.ToString("R", Inv)}); double-model ray direction ({rdx.ToString("R", Inv)}, {rdy.ToString("R", Inv)}, {rdz.ToString("R", Inv)}); angle between them {(System.Math.Acos(System.Math.Min(1.0, ray.direction.x * rdx + ray.direction.y * rdy + ray.direction.z * rdz)) * 1e6).ToString("F3", Inv)} microradians");
                double offset = -1.0;
                if (w.winnerIndex >= 0)
                {
                    Vector3 hit = ray.origin + ray.direction * w.t;
                    var vpf = cam.WorldToViewportPoint(hit);
                    double hfx = vpf.x * 960.0, hfy = vpf.y * 540.0;
                    dc.Proj(hit.x, hit.y, hit.z, out double hdx, out double hdy, out _);
                    offset = System.Math.Sqrt((hdx - Sx) * (hdx - Sx) + (hdy - Sy) * (hdy - Sy));
                    double offF = System.Math.Sqrt((hfx - Sx) * (hfx - Sx) + (hfy - Sy) * (hfy - Sy));
                    int i0 = bt[3 * w.winnerIndex], i1 = bt[3 * w.winnerIndex + 1], i2 = bt[3 * w.winnerIndex + 2];
                    bool okd = BaryD(Sx, Sy, pdx[i0], pdy[i0], pdx[i1], pdy[i1], pdx[i2], pdy[i2], out double bu, out double bvv, out double bw);
                    bool okf = Bary(new Vector2((float)Sx, (float)Sy), pv[i0], pv[i1], pv[i2], out var fuvw);
                    double edD = System.Math.Min(SegDistD(Sx, Sy, pdx[i0], pdy[i0], pdx[i1], pdy[i1]), System.Math.Min(SegDistD(Sx, Sy, pdx[i1], pdy[i1], pdx[i2], pdy[i2]), SegDistD(Sx, Sy, pdx[i2], pdy[i2], pdx[i0], pdy[i0])));
                    o.AppendLine($"    U2 reprojected R3 hit point: double model ({D6(hdx)}, {D6(hdy)}), offset from the sample centre {offset.ToString("F6", Inv)} px; float WorldToViewportPoint ({D6(hfx)}, {D6(hfy)}), offset {offF.ToString("F6", Inv)} px (the offset is the error of the Unity ray plus the hit point)");
                    o.AppendLine($"    winner projected vertices, double | float | diff px: v{i0} ({D6(pdx[i0])}, {D6(pdy[i0])}) | {P2(pv[i0])} | {(new Vector2((float)pdx[i0], (float)pdy[i0]) - pv[i0]).magnitude.ToString("F5", Inv)}; v{i1} ({D6(pdx[i1])}, {D6(pdy[i1])}) | {P2(pv[i1])} | {(new Vector2((float)pdx[i1], (float)pdy[i1]) - pv[i1]).magnitude.ToString("F5", Inv)}; v{i2} ({D6(pdx[i2])}, {D6(pdy[i2])}) | {P2(pv[i2])} | {(new Vector2((float)pdx[i2], (float)pdy[i2]) - pv[i2]).magnitude.ToString("F5", Inv)}");
                    o.AppendLine($"    barycentrics of the SAMPLE in the winner: double {(okd ? "(" + D6(bu) + ", " + D6(bvv) + ", " + D6(bw) + ")" : "degenerate")}, float {(okf ? P3(fuvw) : "degenerate")}; distance of the sample to the nearest edge: double {edD.ToString("F6", Inv)} px");
                }
                // U3: near triangles (a vertex within 3 px of the sample), double
                // qa D-1: the near set is every triangle whose projected 2D bounding box (all three vertices in front of the camera), expanded by 3 px, contains the sample, plus the winner and every hit of the Unity ray
                var near = new List<int>();
                for (int t = 0; t < nt; t++)
                {
                    int a = bt[3 * t], b = bt[3 * t + 1], c = bt[3 * t + 2];
                    if (!inFrontD[a] || !inFrontD[b] || !inFrontD[c]) continue;
                    double x0 = System.Math.Min(pdx[a], System.Math.Min(pdx[b], pdx[c])), x1 = System.Math.Max(pdx[a], System.Math.Max(pdx[b], pdx[c]));
                    double y0 = System.Math.Min(pdy[a], System.Math.Min(pdy[b], pdy[c])), y1 = System.Math.Max(pdy[a], System.Math.Max(pdy[b], pdy[c]));
                    if (Sx >= x0 - 3.0 && Sx <= x1 + 3.0 && Sy >= y0 - 3.0 && Sy <= y1 + 3.0) near.Add(t);
                }
                int nearByBox = near.Count;
                if (w.winnerIndex >= 0 && !near.Contains(w.winnerIndex)) near.Add(w.winnerIndex);
                for (int t = 0; t < nt; t++) if (!near.Contains(t) && RayTri(ray.origin, ray.direction, tris[t], out _, out _)) near.Add(t);
                var containing = new List<int>();
                foreach (int t in near)
                {
                    int a = bt[3 * t], b = bt[3 * t + 1], c = bt[3 * t + 2];
                    if (BaryD(Sx, Sy, pdx[a], pdy[a], pdx[b], pdy[b], pdx[c], pdy[c], out double u1, out double v1, out double w1) && u1 >= -1e-9 && v1 >= -1e-9 && w1 >= -1e-9) containing.Add(t);
                }
                o.AppendLine($"    U3 triangles whose projected 2D bounding box expanded by 3 px contains the sample: {nearByBox} (near set {near.Count} with the winner and the ray hits); whose 2D footprint CONTAINS the sample (double): {containing.Count}");
                foreach (int t in containing)
                {
                    int a = bt[3 * t], b = bt[3 * t + 1], c = bt[3 * t + 2];
                    BaryD(Sx, Sy, pdx[a], pdy[a], pdx[b], pdy[b], pdx[c], pdy[c], out double u1, out double v1, out double w1);
                    bool hit = RayTri(ray.origin, ray.direction, tris[t], out float th, out bool bk);
                    o.AppendLine($"      CONTAINS triangle #{t} (vertices {a}, {b}, {c}): 2D orientation {Facing2D(t)}; barycentrics ({D6(u1)}, {D6(v1)}, {D6(w1)}); the Unity sample ray against it: {(hit ? (bk ? "back" : "front") + " t " + th.ToString("F7", Inv) + " m" : "no hit")}");
                }
                if (!control && containing.Count == 0)
                {
                    var edges = new Dictionary<long, (int i, int j, double d, int owner)>();
                    foreach (int t in near)
                        for (int k = 0; k < 3; k++)
                        {
                            int i = bt[3 * t + k], j = bt[3 * t + (k + 1) % 3];
                            int lo = Mathf.Min(i, j), hi = Mathf.Max(i, j);
                            long key = (long)lo * 1000003L + hi;
                            if (!edges.ContainsKey(key)) edges[key] = (lo, hi, SegDistD(Sx, Sy, pdx[lo], pdy[lo], pdx[hi], pdy[hi]), t);
                        }
                    var eight = edges.Values.OrderBy(e => e.d).Take(8).ToList();
                    o.AppendLine($"    no triangle footprint contains the sample; the {eight.Count} nearest edges of the near triangles:");
                    foreach (var e in eight)
                    {
                        var byIndex = vtris[e.i].Intersect(vtris[e.j]).Where(t => t != e.owner).ToList();
                        var dupI = new List<int> { e.i }; var dupJ = new List<int> { e.j };
                        for (int v = 0; v < nv; v++)
                        {
                            if (v != e.i && (pw[v] - pw[e.i]).sqrMagnitude <= 1e-12f) dupI.Add(v);
                            if (v != e.j && (pw[v] - pw[e.j]).sqrMagnitude <= 1e-12f) dupJ.Add(v);
                        }
                        var byPos = new List<int>();
                        foreach (int a in dupI) foreach (int b in dupJ)
                        {
                            if (a == e.i && b == e.j) continue;
                            foreach (int t in vtris[a].Intersect(vtris[b])) if (t != e.owner && !byPos.Contains(t)) byPos.Add(t);
                        }
                        string flag = "shared";
                        string detail = "";
                        if (byIndex.Count == 0 && byPos.Count == 0)
                        {
                            float best = float.MaxValue; int bestT = -1;
                            for (int t = 0; t < nt; t++)
                            {
                                if (t == e.owner) continue;
                                for (int k = 0; k < 3; k++)
                                {
                                    int a = bt[3 * t + k], b = bt[3 * t + (k + 1) % 3];
                                    float d1 = Mathf.Max((pw[a] - pw[e.i]).magnitude, (pw[b] - pw[e.j]).magnitude);
                                    float d2 = Mathf.Max((pw[a] - pw[e.j]).magnitude, (pw[b] - pw[e.i]).magnitude);
                                    float d = Mathf.Min(d1, d2);
                                    if (d < best) { best = d; bestT = t; }
                                }
                            }
                            flag = best <= 1e-3f ? "SPLIT EDGE (threshold 1e-3 bake-space units = 1 mm if one unit is a metre)" : "UNSHARED EDGE (threshold 1e-3 bake-space units = 1 mm if one unit is a metre)";
                            detail = $"; nearest other-triangle edge by posed position: triangle #{bestT}, endpoints differ by d {best.ToString("E3", Inv)} bake-space units (= {(best * 1000f).ToString("E3", Inv)} mm if one unit is a metre)";
                        }
                        o.AppendLine($"      edge {e.i}-{e.j} of triangle #{e.owner}: distance {e.d.ToString("F6", Inv)} px; projected (double) ({D6(pdx[e.i])}, {D6(pdy[e.i])}) ({D6(pdx[e.j])}, {D6(pdy[e.j])}); posed {P3(pw[e.i])} | {P3(pw[e.j])}; partners by index [{string.Join(", ", byIndex)}]; partners by posed position 1e-6 [{string.Join(", ", byPos)}] -> {flag}{detail}");
                    }
                }
                // U6: ALL hits of the Unity pinhole ray
                if (allHits)
                {
                    var hits = new List<(int t, float tf, bool back)>();
                    for (int t = 0; t < nt; t++) if (RayTri(ray.origin, ray.direction, tris[t], out float tf, out bool bk)) hits.Add((t, tf, bk));
                    hits.Sort((p, q) => p.tf.CompareTo(q.tf));
                    o.AppendLine($"    U6 all {hits.Count} hits of the Unity ray (sorted by t), each with the double-model data for the sample:");
                    bool anyWellInside = false, allEdgeCase = hits.Count > 0; int frontHits = 0; bool wellFront = false;
                    foreach (var h in hits)
                    {
                        int a = bt[3 * h.t], b = bt[3 * h.t + 1], c = bt[3 * h.t + 2];
                        bool okb = BaryD(Sx, Sy, pdx[a], pdy[a], pdx[b], pdy[b], pdx[c], pdy[c], out double u1, out double v1, out double w1);
                        double edm = System.Math.Min(SegDistD(Sx, Sy, pdx[a], pdy[a], pdx[b], pdy[b]), System.Math.Min(SegDistD(Sx, Sy, pdx[b], pdy[b], pdx[c], pdy[c]), SegDistD(Sx, Sy, pdx[c], pdy[c], pdx[a], pdy[a])));
                        bool inside = okb && u1 >= -1e-9 && v1 >= -1e-9 && w1 >= -1e-9;
                        // double 3D ray-triangle for the double-model ray
                        double e1x = pwx[b] - pwx[a], e1y = pwy[b] - pwy[a], e1z = pwz[b] - pwz[a], e2x = pwx[c] - pwx[a], e2y = pwy[c] - pwy[a], e2z = pwz[c] - pwz[a];
                        double ppx = rdy * e2z - rdz * e2y, ppy = rdz * e2x - rdx * e2z, ppz = rdx * e2y - rdy * e2x;
                        double det = e1x * ppx + e1y * ppy + e1z * ppz;
                        string dray = "double-model ray: parallel";
                        if (System.Math.Abs(det) > 1e-18)
                        {
                            double sx2 = dc.px - pwx[a], sy2 = dc.py - pwy[a], sz2 = dc.pz - pwz[a];
                            double uu = (sx2 * ppx + sy2 * ppy + sz2 * ppz) / det;
                            double qxx = sy2 * e1z - sz2 * e1y, qyy = sz2 * e1x - sx2 * e1z, qzz = sx2 * e1y - sy2 * e1x;
                            double vv = (rdx * qxx + rdy * qyy + rdz * qzz) / det;
                            double tt = (e2x * qxx + e2y * qyy + e2z * qzz) / det;
                            dray = $"double-model ray: t {D7(tt)}, ray barycentrics u {D6(uu)} v {D6(vv)} ({(uu >= 0 && vv >= 0 && uu + vv <= 1 && tt >= 0 ? "hits" : "MISSES")})";
                        }
                        o.AppendLine($"      hit triangle #{h.t} {(h.back ? "BACK-FACING (culled when cull is on; not used for the cull-on reading)" : "FRONT-FACING")} t {h.tf.ToString("F7", Inv)} m (vertices {a}, {b}, {c}); {dray}; sample 2D barycentrics (double) {(okb ? "(" + D6(u1) + ", " + D6(v1) + ", " + D6(w1) + ")" : "degenerate")}, sample {(inside ? "INSIDE" : "OUTSIDE")} its footprint, distance to its nearest edge {edm.ToString("F6", Inv)} px; 2D orientation {Facing2D(h.t)}");
                        if (!h.back) { frontHits++; if (inside && edm > 0.05) wellFront = true; }
                        if (inside && edm > 0.05) { anyWellInside = true; allEdgeCase = false; }
                        else if (!inside || edm <= 0.05)
                        {
                            // the neighbour across the nearest edge of this hit triangle
                            int bi = 0; double bd = double.MaxValue; int[] vi3 = { a, b, c };
                            for (int k = 0; k < 3; k++) { double dk = SegDistD(Sx, Sy, pdx[vi3[k]], pdy[vi3[k]], pdx[vi3[(k + 1) % 3]], pdy[vi3[(k + 1) % 3]]); if (dk < bd) { bd = dk; bi = k; } }
                            int ei = vi3[bi], ej = vi3[(bi + 1) % 3];
                            var across = vtris[ei].Intersect(vtris[ej]).Where(t => t != h.t).ToList();
                            o.AppendLine($"        nearest edge {ei}-{ej} ({bd.ToString("F6", Inv)} px): neighbours across it by index [{string.Join("; ", across.Select(t => "#" + t + " " + Facing2D(t) + ", in the cull-on render: " + (Facing2D(t).StartsWith("FRONT") ? "yes if front-facing triangles are drawn" : "no (back-facing, culled)")))}]{(across.Count == 0 ? " none: an open edge" : "")}");
                        }
                    }
                    for (int x1 = 0; x1 < hits.Count; x1++) for (int x2 = x1 + 1; x2 < hits.Count; x2++)
                    {
                        int ta = hits[x1].t, tb = hits[x2].t, shared = 0;
                        for (int k1 = 0; k1 < 3; k1++) for (int k2 = 0; k2 < 3; k2++) if ((pw[bt[3 * ta + k1]] - pw[bt[3 * tb + k2]]).sqrMagnitude <= 1e-12f) shared++;
                        o.AppendLine($"      pair #{ta} / #{tb}: |t difference| {System.Math.Abs(hits[x1].tf - hits[x2].tf).ToString("E3", Inv)} m ({(System.Math.Abs(hits[x1].tf - hits[x2].tf) <= 1e-6 ? "EQUAL within 1e-6 m: edge graze candidate" : "not equal")}); vertices equal by posed position: {shared} ({(shared >= 2 ? "they SHARE an edge by position" : "no shared edge by position")})");
                    }
                    o.AppendLine(frontHits == 0 ? "    U6 READING (cull-on render, FRONT-facing hits only): the ray has no front-facing hit" : wellFront ? "    U6 READING (cull-on render, FRONT-facing hits only): the sample is WELL INSIDE a front-facing hit triangle (more than 0.05 px from its edges) -> the GPU dropped a covered sample: ESCALATE" : "    U6 READING (cull-on render, FRONT-facing hits only): the sample is outside, or within 0.05 px of an edge of, every front-facing hit triangle -> a raster edge case (CPU float noise)");
                    o.AppendLine(hits.Count == 0 ? "    U6 READING (cull-off render, ALL hits): the ray has no hit" : anyWellInside ? "    U6 READING (cull-off render, ALL hits): the sample is WELL INSIDE a hit triangle (more than 0.05 px from its edges) -> the GPU dropped covered samples: ESCALATE" : allEdgeCase ? "    U6 READING (cull-off render, ALL hits): the sample is outside, or within 0.05 px of an edge of, EVERY hit triangle -> a raster edge case (CPU float noise); the neighbours across the nearest edges are listed above" : "    U6 READING (cull-off render, ALL hits): mixed");
                }
                return (containing.Count, offset);
            }

            // U1: the 4x supersample (3840 x 2160) body-only pass of this same frame
            const int SW = 3840, SH = 2160, SS = 4, W0 = -15, W1 = 18;   // 34 x 34 window: block origin -15 .. +18 is symmetric about the block centre (origin + 1.5)
            var big = ToMask(RenderBodyOnly(cam, player.gameObject.layer, SW, SH));
            o.AppendLine($"U1 map orientation: the TOP row of each map is the image top (y from the top), x from the left; each map line gives its window's top-left supersample pixel (x, y from the top) in the {SW}x{SH} image; one sample pixel = {SS}x{SS} supersample pixels, the block of pixel (x, yTop) starts at ({SS}*x, {SS}*yTop).");
            var targets = new List<(int x, int yTop, string label)>();
            foreach (var ph in pinholes) targets.Add((ph.x, ph.yTop, "pinhole"));
            if (!pinholes.Contains(spot)) targets.Add((spot.x, spot.yTop, "known spot (not a pinhole in this frame)"));
            foreach (var tg in targets)
            {
                int bx0 = tg.x * SS, by0 = tg.yTop * SS;
                int blockMag = 0;
                for (int dy = 0; dy < SS; dy++) for (int dx = 0; dx < SS; dx++) if (Mag(big, SW, SH, bx0 + dx, by0 + dy)) blockMag++;
                int winMag = 0;
                var map = new StringBuilder();
                for (int dy = W0; dy <= W1; dy++)
                {
                    var row = new StringBuilder();
                    for (int dx = W0; dx <= W1; dx++)
                    {
                        int xx = bx0 + dx, yy = by0 + dy;
                        bool m = xx >= 0 && xx < SW && yy >= 0 && yy < SH && Mag(big, SW, SH, xx, yy);
                        if (m) winMag++;
                        row.Append(m ? 'M' : '.');
                    }
                    map.AppendLine("        " + row);
                }
                // does the magenta of the pinhole's own 4x4 block connect to the image border? (8-connected flood fill, stops at the border)
                int visited = 0; bool toBorder = false;
                if (blockMag > 0)
                {
                    var seen = new bool[SW * SH];
                    var queue = new int[SW * SH];
                    int qh = 0, qt = 0;
                    for (int dy = 0; dy < SS; dy++) for (int dx = 0; dx < SS; dx++)
                    {
                        int xx = bx0 + dx, yy = by0 + dy;
                        int id = (SH - 1 - yy) * SW + xx;
                        if (big[id] && !seen[id]) { seen[id] = true; queue[qt++] = id; }
                    }
                    while (qh < qt && !toBorder)
                    {
                        int id = queue[qh++]; visited++;
                        int xx = id % SW, yy = SH - 1 - id / SW;
                        if (xx == 0 || xx == SW - 1 || yy == 0 || yy == SH - 1) { toBorder = true; break; }
                        for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
                        {
                            if (dx == 0 && dy == 0) continue;
                            int nx = xx + dx, ny = yy + dy;
                            int nid = (SH - 1 - ny) * SW + nx;
                            if (big[nid] && !seen[nid]) { seen[nid] = true; queue[qt++] = nid; }
                        }
                    }
                }
                o.AppendLine($"U1 {tg.label} ({tg.x}, {tg.yTop}) at 4x: magenta in its own 4x4 block {blockMag} of 16; in the 34x34 supersample-px window (block origin {W0}..+{W1}, symmetric about the block centre) {winMag} of {(W1 - W0 + 1) * (W1 - W0 + 1)}; its magenta {(blockMag == 0 ? "does not exist at 4x (the pinhole closes when supersampled)" : toBorder ? "CONNECTS to the image border (the background) after " + visited + " supersample pixels (a LOWER BOUND: the fill stops at the first border touch)" : "does NOT connect to the border: an enclosed region of " + visited + " pixels")}; window top-left supersample pixel ({bx0 + W0}, {by0 + W0}), block centre ({bx0 + 1.5}, {by0 + 1.5}) (x, y from the top); map (M magenta, . body):");
                o.Append(map);
            }

            // U2/U3/U4/U5/U6
            o.AppendLine("U4 control:");
            bool voidU = true;
            if (cx >= 0)
            {
                var cr = Probe($"control (body pixel nearest the centre, front winner, |n.ray| >= 0.3; {tried} candidates tried)", cx, cy, true, false);
                voidU = cr.containing < 1 || cr.offset < 0.0 || cr.offset > 0.05;
                o.AppendLine($"  U4 control: containing triangles {cr.containing}, reprojection offset {cr.offset.ToString("F6", Inv)} px -> {(voidU ? "U VOID (needs >= 1 containing triangle and offset <= 0.05 px)" : "bites")}");
            }
            else o.AppendLine("  U4 control: no front-face control pixel found -> U VOID");
            o.AppendLine($"U2/U3/U6 at the samples ({(voidU ? "U VOID: values shown for reference" : "control bites")}):");
            foreach (var tg in targets) Probe(tg.label, tg.x, tg.yTop, false, true);
            Object.Destroy(baked);
        }
    }
}
