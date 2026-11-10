using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Text;
using UnityEngine;
using WashedAshore.Character.Stance;
using WashedAshore.Gameplay;
using WashedAshore.Tests.Support;

namespace WashedAshore.Tests.PlayMode.Character
{
    /// <summary>
    /// Geometry half of the S2 print (HairlineGapSizing, partial class): bake with regions, renders, interior components, the double-precision camera model and screen grid of the posed triangles (td B29a/B29b method),
    /// the R3 winner (MirrorTie) through that grid, the 4x supersample of a frame, and the per-pixel attribution block (td B32: hit triangles, the two framing edges with their triangle indices, their 3-D separation in mm).
    /// P0 (td-49 B35/B35a/B35b/B35c): each baked triangle also carries its dominant bone (K-h), the K-f map is computed at bake time, SuperLine reports whether the gap is open at 4x (evidence only), Attribute returns the framing edges' triangles, bones and separation,
    /// and the crop writer takes an explicit lit source (the hull-on twin) and writes a 1x 40x40 window. Print only; no state outside the instance.
    /// </summary>
    public partial class HairlineGapSizing
    {
        struct PTri { public Vector3 a, b, c; public string region, bone; public int index; public bool big; }
        class Comp { public List<(int x, int yTop)> m = new List<(int, int)>(); public double cx, cy; public int x0 = W, x1 = -1, y0 = H, y1 = -1; }

        static bool Mag(bool[] m, int x, int yTop) => m[(H - 1 - yTop) * W + x];

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

        static Ray PixelRay(Camera cam, float x, float yTop) => cam.ViewportPointToRay(new Vector3((x + 0.5f) / W, (H - 1 - yTop + 0.5f) / H, 0f));

        /// <summary>Bakes the enabled view renderers to world triangles with their BarOneRule region (majority of the corners' dominant-bone regions, cached per renderer).</summary>
        List<PTri> BakeRegions(FirstPersonBody fp)
        {
            var tris = new List<PTri>(40000);
            var mesh = new Mesh();
            boneByName.Clear();   // td B34c F1: no Transform from an earlier world survives a bake
            foreach (var r in fp.ViewRenderers)
            {
                if (!r.enabled || !(r is SkinnedMeshRenderer smr) || smr.sharedMesh == null || IsHull(smr)) continue;   // P0: the test hull is never part of the bake
                smr.BakeMesh(mesh, false);
                var v = mesh.vertices; var t = mesh.triangles;
                foreach (var b in smr.bones) if (b != null) boneByName[b.name] = b;
                if (!triRegionCache.TryGetValue(smr, out var tr))
                {
                    var weights = smr.sharedMesh.boneWeights; var bones = smr.bones;
                    string Reg(int vi) => weights.Length > vi && weights[vi].boneIndex0 >= 0 && weights[vi].boneIndex0 < bones.Length ? BarOneRule.Region(bones[weights[vi].boneIndex0]) : "UNKNOWN";
                    tr = new string[t.Length / 3];
                    for (int i = 0; i < t.Length; i += 3) tr[i / 3] = BarOneRule.TriRegion(Reg(t[i]), Reg(t[i + 1]), Reg(t[i + 2]));
                    triRegionCache[smr] = tr;
                }
                if (!triBoneCache.TryGetValue(smr, out var tb))
                {   // td B35b K-h: the dominant bone of each triangle = the majority of its corners' boneIndex0 bones (corner 0 on a 3-way tie), as BarOneRule.TriRegion does for regions
                    var weights = smr.sharedMesh.boneWeights; var bones = smr.bones;
                    string Bn(int vi) => weights.Length > vi && weights[vi].boneIndex0 >= 0 && weights[vi].boneIndex0 < bones.Length && bones[weights[vi].boneIndex0] != null ? bones[weights[vi].boneIndex0].name : "?";
                    tb = new string[t.Length / 3];
                    for (int i = 0; i < t.Length; i += 3) { string b0 = Bn(t[i]), b1 = Bn(t[i + 1]), b2 = Bn(t[i + 2]); tb[i / 3] = (b0 == b1 || b0 == b2) ? b0 : (b1 == b2 ? b1 : b0); }
                    triBoneCache[smr] = tb;
                }
                bool big = smr.sharedMesh.GetIndexCount(0) / 3 == 23791;   // Survivor_Body: the slit triangle numbers are in its index space
                if (big) EnsureKfMap(smr);   // td B35c: the K-f map is computed in the test, from the rest mesh (eng_51: read by RestOf without Read/Write; normally already computed by the preflight)
                Vector3 pos = smr.transform.position; Quaternion rot = smr.transform.rotation;
                for (int i = 0; i < t.Length; i += 3)
                    tris.Add(new PTri { a = pos + rot * v[t[i]], b = pos + rot * v[t[i + 1]], c = pos + rot * v[t[i + 2]], region = tr[i / 3], bone = tb[i / 3], index = i / 3, big = big });
            }
            Object.Destroy(mesh);
            foreach (var dead in triRegionCache.Keys.Where(k => k == null).ToList()) triRegionCache.Remove(dead);   // qa: no destroyed renderer from an earlier world stays a key
            foreach (var dead in triBoneCache.Keys.Where(k => k == null).ToList()) triBoneCache.Remove(dead);
            return tris;
        }

        /// <summary>One fresh render at w x h (the lit camera, or a copy with only the Player layer over solid magenta); bottom-origin pixels.</summary>
        static Color32[] RenderPixels(Camera source, int layer, bool bodyOnly, int w = W, int h = H)
        {
            GameObject go = null;
            var rt = new RenderTexture(w, h, 24);
            try
            {
                var c = source;
                if (bodyOnly)
                {
                    go = new GameObject("S2BodyOnly");
                    c = go.AddComponent<Camera>();
                    c.CopyFrom(source);
                    c.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
                    c.cullingMask = 1 << layer;
                    c.clearFlags = CameraClearFlags.SolidColor;
                    c.backgroundColor = new Color(1f, 0f, 1f, 1f);
                }
                var old = c.targetTexture;
                c.targetTexture = rt;
                c.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply();
                RenderTexture.active = null;
                c.targetTexture = old;
                var px = tex.GetPixels32();
                Object.Destroy(tex);
                return px;
            }
            finally { rt.Release(); Object.Destroy(rt); if (go != null) Object.Destroy(go); }
        }

        static bool[] ToMask(Color32[] px)
        {
            var m = new bool[px.Length];
            for (int i = 0; i < px.Length; i++) m[i] = px[i].r > 229 && px[i].g < 26 && px[i].b > 229;
            return m;
        }

        /// <summary>Interior components: 8-connected magenta regions that do not touch the image border.</summary>
        static List<Comp> Interior(bool[] bg)
        {
            var seen = new bool[W * H]; var list = new List<Comp>(); var stack = new Stack<int>();
            for (int s = 0; s < bg.Length; s++)
            {
                if (!bg[s] || seen[s]) continue;
                var c = new Comp(); bool border = false; seen[s] = true; stack.Push(s);
                while (stack.Count > 0)
                {
                    int id = stack.Pop(); int x = id % W, yTop = H - 1 - id / W;
                    c.m.Add((x, yTop)); c.cx += x; c.cy += yTop;
                    c.x0 = Mathf.Min(c.x0, x); c.x1 = Mathf.Max(c.x1, x); c.y0 = Mathf.Min(c.y0, yTop); c.y1 = Mathf.Max(c.y1, yTop);
                    if (x == 0 || x == W - 1 || yTop == 0 || yTop == H - 1) border = true;
                    for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        int nx = x + dx, ny = yTop + dy;
                        if (nx < 0 || nx >= W || ny < 0 || ny >= H) continue;
                        int nid = (H - 1 - ny) * W + nx;
                        if (bg[nid] && !seen[nid]) { seen[nid] = true; stack.Push(nid); }
                    }
                }
                if (!border) { c.cx /= c.m.Count; c.cy /= c.m.Count; list.Add(c); }
            }
            return list;
        }

        static int BodyNeighbours(bool[] bg, int x, int yTop)
        {
            int n = 0;
            for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0) continue;
                int xx = x + dx, yy = yTop + dy;
                if (xx < 0 || xx >= W || yy < 0 || yy >= H) continue;
                if (!Mag(bg, xx, yy)) n++;
            }
            return n;
        }

        /// <summary>Double-precision camera model (td B29a/B29b): built from position, rotation, FOV and aspect 960/540, not from WorldToViewportPoint.</summary>
        sealed class DCam
        {
            public double px, py, pz, tanHalf, aspect = W / (double)H;
            public double[] R = new double[9];   // row-major local -> world
            public static DCam Make(Camera cam)
            {
                var q = cam.transform.rotation; double x = q.x, y = q.y, z = q.z, w = q.w;
                var c = new DCam { px = cam.transform.position.x, py = cam.transform.position.y, pz = cam.transform.position.z, tanHalf = System.Math.Tan(cam.fieldOfView * System.Math.PI / 180.0 / 2.0) };
                c.R = new[] { 1 - 2 * (y * y + z * z), 2 * (x * y - w * z), 2 * (x * z + w * y), 2 * (x * y + w * z), 1 - 2 * (x * x + z * z), 2 * (y * z - w * x), 2 * (x * z - w * y), 2 * (y * z + w * x), 1 - 2 * (x * x + y * y) };
                return c;
            }
            /// <summary>Pixel coordinates (y from the bottom); false when the point is behind the camera.</summary>
            public bool Proj(double X, double Y, double Z, out double sx, out double sy)
            {
                double dx = X - px, dy = Y - py, dz = Z - pz;
                double cx = R[0] * dx + R[3] * dy + R[6] * dz, cy = R[1] * dx + R[4] * dy + R[7] * dz, cz = R[2] * dx + R[5] * dy + R[8] * dz;
                if (cz <= 1e-9) { sx = 0; sy = 0; return false; }
                sx = (cx / (cz * tanHalf * aspect) * 0.5 + 0.5) * W; sy = (cy / (cz * tanHalf) * 0.5 + 0.5) * H;
                return true;
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

        /// <summary>Distance from a point to a segment and the foot vector (point minus nearest point).</summary>
        static double SegDistD(double px, double py, double ax, double ay, double bx, double by, out double fx, out double fy)
        {
            double abx = bx - ax, aby = by - ay, l2 = abx * abx + aby * aby;
            double t = l2 < 1e-24 ? 0.0 : System.Math.Max(0.0, System.Math.Min(1.0, ((px - ax) * abx + (py - ay) * aby) / l2));
            fx = px - (ax + t * abx); fy = py - (ay + t * aby);
            return System.Math.Sqrt(fx * fx + fy * fy);
        }

        /// <summary>The posed triangles projected in double through <see cref="DCam"/>, with a 16 px screen grid so a ray only meets the triangles whose projected bounding box (expanded by 4 px) covers its cell.</summary>
        sealed class Geo
        {
            public readonly DCam cam; public readonly List<PTri> tris; public readonly double[] cx, cy; public readonly bool[] front;   // per corner, 3 per triangle
            readonly double[] x0, x1, y0, y1; readonly bool[] allFront;
            const int Cell = 16, Pad = 4; readonly int gw = W / Cell + 1, gh = H / Cell + 1;
            readonly List<int>[] cells; readonly List<int> always = new List<int>();
            public int behind;   // triangles with a corner behind the camera (always tested by the ray, never in the near set)
            public Geo(Camera c, List<PTri> t)
            {
                cam = DCam.Make(c); tris = t; int n = t.Count;
                cx = new double[3 * n]; cy = new double[3 * n]; front = new bool[3 * n]; x0 = new double[n]; x1 = new double[n]; y0 = new double[n]; y1 = new double[n]; allFront = new bool[n];
                cells = new List<int>[gw * gh];
                for (int i = 0; i < n; i++)
                {
                    var p = new[] { t[i].a, t[i].b, t[i].c }; bool all = true;
                    for (int k = 0; k < 3; k++) { front[3 * i + k] = cam.Proj(p[k].x, p[k].y, p[k].z, out cx[3 * i + k], out cy[3 * i + k]); all &= front[3 * i + k]; }
                    allFront[i] = all;
                    if (!all) { always.Add(i); behind++; continue; }
                    x0[i] = System.Math.Min(cx[3 * i], System.Math.Min(cx[3 * i + 1], cx[3 * i + 2])); x1[i] = System.Math.Max(cx[3 * i], System.Math.Max(cx[3 * i + 1], cx[3 * i + 2]));
                    y0[i] = System.Math.Min(cy[3 * i], System.Math.Min(cy[3 * i + 1], cy[3 * i + 2])); y1[i] = System.Math.Max(cy[3 * i], System.Math.Max(cy[3 * i + 1], cy[3 * i + 2]));
                    int gx0 = Mathf.Max(0, (int)((x0[i] - Pad) / Cell)), gx1 = Mathf.Min(gw - 1, (int)((x1[i] + Pad) / Cell)), gy0 = Mathf.Max(0, (int)((y0[i] - Pad) / Cell)), gy1 = Mathf.Min(gh - 1, (int)((y1[i] + Pad) / Cell));
                    if (x1[i] + Pad < 0 || y1[i] + Pad < 0 || x0[i] - Pad > W || y0[i] - Pad > H) continue;
                    for (int gy = gy0; gy <= gy1; gy++) for (int gx = gx0; gx <= gx1; gx++) (cells[gy * gw + gx] ??= new List<int>()).Add(i);
                }
            }
            /// <summary>Triangles that can contain a ray through the screen point (bottom-origin pixels).</summary>
            public IEnumerable<int> Candidates(double sx, double sy)
            {
                foreach (int i in always) yield return i;
                int gx = Mathf.Clamp((int)(sx / Cell), 0, gw - 1), gy = Mathf.Clamp((int)(sy / Cell), 0, gh - 1);
                var l = cells[gy * gw + gx];
                if (l != null) foreach (int i in l) yield return i;
            }
            /// <summary>Triangles with all corners in front whose projected bounding box expanded by <paramref name="margin"/> px contains the point.</summary>
            public List<int> Near(double sx, double sy, double margin)
            {
                var res = new List<int>();
                foreach (int i in Candidates(sx, sy)) if (allFront[i] && sx >= x0[i] - margin && sx <= x1[i] + margin && sy >= y0[i] - margin && sy <= y1[i] + margin) res.Add(i);
                return res;
            }
            public string Facing(int i) { double a = (cx[3 * i + 1] - cx[3 * i]) * (cy[3 * i + 2] - cy[3 * i]) - (cy[3 * i + 1] - cy[3 * i]) * (cx[3 * i + 2] - cx[3 * i]); return a < 0 ? "FRONT" : "BACK"; }   // y up: clockwise on screen = front
        }

        /// <summary>The R3 winner (MirrorTie) of a Unity ray through a screen point, using only the grid candidates: region, back face, t, the triangle list position, the hit point and the hit count.</summary>
        static (string region, bool back, float t, int tri, Vector3 point, int hits) Winner(Geo g, Ray ray, double sx, double sy)
        {
            float best = float.MaxValue; bool backFace = false; PTri bt = default; int bi = -1, hits = 0;
            foreach (int i in g.Candidates(sx, sy))
            {
                var tri = g.tris[i];
                if (!RayTri(ray.origin, ray.direction, tri, out float t, out bool back)) continue;
                hits++;
                if (MirrorTie.Replaces(bi >= 0, best, backFace, bt.a, bt.b, bt.c, t, back, tri.a, tri.b, tri.c)) { best = t; backFace = back; bt = tri; bi = i; }
            }
            return bi < 0 ? ("none", false, 0f, -1, Vector3.zero, 0) : (bt.region, backFace, best, bi, ray.origin + ray.direction * best, hits);
        }

        // ---- 4x supersample (3840 x 2160) of a frame, only when it has an interior component (producer-49's cost cap) ----
        const int SW = W * 4, SH = H * 4;
        bool[] flooded;   // reused 'seen' buffer, reset through the touched list

        /// <summary>At 4x: magenta sub-pixels inside the component's bounding box, and whether the magenta reachable from them (8-connected) touches the image border (the background). Visited count is a lower bound when it connects.</summary>
        string SuperLine(bool[] big, Comp c, out bool open)
        {
            open = false;   // td B35b: "open at 4x" is evidence only, never a class
            int count = 0; var seeds = new List<int>();
            for (int y = c.y0 * 4; y < (c.y1 + 1) * 4; y++) for (int x = c.x0 * 4; x < (c.x1 + 1) * 4; x++) { int id = (SH - 1 - y) * SW + x; if (big[id]) { count++; seeds.Add(id); } }
            if (count == 0) return "at 4x: the whole bounding box is body (the gap closes when supersampled)";
            flooded ??= new bool[SW * SH];
            var touched = new List<int>(); var stack = new Stack<int>(); bool toBorder = false;
            foreach (int s in seeds) if (!flooded[s]) { flooded[s] = true; touched.Add(s); stack.Push(s); }
            while (stack.Count > 0 && !toBorder)
            {
                int id = stack.Pop(); int x = id % SW, y = SH - 1 - id / SW;
                if (x == 0 || x == SW - 1 || y == 0 || y == SH - 1) { toBorder = true; break; }
                for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0) continue;
                    int nid = (SH - 1 - (y + dy)) * SW + (x + dx);
                    if (big[nid] && !flooded[nid]) { flooded[nid] = true; touched.Add(nid); stack.Push(nid); }
                }
            }
            int visited = touched.Count; open = toBorder;
            foreach (int id in touched) flooded[id] = false;
            return $"at 4x: {count} magenta sub-pixels in its bounding box (of {(c.x1 - c.x0 + 1) * (c.y1 - c.y0 + 1) * 16}); that magenta {(toBorder ? "CONNECTS to the image border (the background), visited at least " + visited : "does NOT connect to the border (enclosed, " + visited + " sub-pixels)")}";
        }

        /// <summary>Closest distance between two 3-D segments in double (Ericson), in the units of the points.</summary>
        static double SegSegDist(Vector3 p1, Vector3 q1, Vector3 p2, Vector3 q2)
        {
            double d1x = q1.x - p1.x, d1y = q1.y - p1.y, d1z = q1.z - p1.z, d2x = q2.x - p2.x, d2y = q2.y - p2.y, d2z = q2.z - p2.z, rx = p1.x - p2.x, ry = p1.y - p2.y, rz = p1.z - p2.z;
            double a = d1x * d1x + d1y * d1y + d1z * d1z, e = d2x * d2x + d2y * d2y + d2z * d2z, f = d2x * rx + d2y * ry + d2z * rz, s, t;
            double c = d1x * rx + d1y * ry + d1z * rz, b = d1x * d2x + d1y * d2y + d1z * d2z, den = a * e - b * b;
            s = den > 1e-18 ? System.Math.Max(0, System.Math.Min(1, (b * f - c * e) / den)) : 0;
            t = e > 1e-18 ? (b * s + f) / e : 0;
            if (t < 0) { t = 0; s = a > 1e-18 ? System.Math.Max(0, System.Math.Min(1, -c / a)) : 0; }
            else if (t > 1) { t = 1; s = a > 1e-18 ? System.Math.Max(0, System.Math.Min(1, (b - c) / a)) : 0; }
            double dx = rx + d1x * s - d2x * t, dy = ry + d1y * s - d2y * t, dz = rz + d1z * s - d2z * t;
            return System.Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        class EdgeRec { public Vector3 p, q; public double d, fx, fy; public List<string> tris = new List<string>(), bones = new List<string>(); public List<int> idx = new List<int>(), bigIdx = new List<int>(); }

        /// <summary>The attribution result (td B35b): the triangles at the two framing edges (Survivor_Body index space in e1Big/e2Big, for K-f), their dominant bones (K-h) and the 3-D separation in mm (NaN without edge 2).</summary>
        class Framing
        {
            public bool has1, has2; public double sepMm = double.NaN;
            public readonly List<int> e1 = new List<int>(), e2 = new List<int>(), e1Big = new List<int>(), e2Big = new List<int>();
            public readonly List<string> b1 = new List<string>(), b2 = new List<string>();
            public HashSet<int> All() => new HashSet<int>(e1.Concat(e2));
        }

        /// <summary>td B32 attribution for one flagged sample pixel: every hit of its Unity ray, the nearest distinct edge-position (E1) and the nearest one on the opposite side of the sample (E2) with the triangles (index, facing) at each, and their 3-D separation in mm (world metres x 1000).</summary>
        Framing Attribute(StringBuilder o, Geo g, Camera cam, int px, int yTop)
        {
            double sx = px + 0.5, sy = H - 1 - yTop + 0.5;
            var ray = PixelRay(cam, px, yTop);
            var hitTris = new List<int>();
            foreach (int i in g.Candidates(sx, sy)) if (RayTri(ray.origin, ray.direction, g.tris[i], out _, out _)) hitTris.Add(i);
            var hs = new List<string>();
            foreach (int i in hitTris) { RayTri(ray.origin, ray.direction, g.tris[i], out float th, out bool bk); hs.Add($"#{g.tris[i].index} {(bk ? "BACK" : "FRONT")} {g.tris[i].region} t {th.ToString("F7", Inv)} m"); }
            o.AppendLine($"      attribution at ({px}, {yTop}): ray hits {hitTris.Count}: [{string.Join("; ", hs)}]");
            var near = g.Near(sx, sy, 3.0); foreach (int i in hitTris) if (!near.Contains(i)) near.Add(i);
            var edges = new Dictionary<string, EdgeRec>();
            foreach (int i in near)
            {
                if (g.front[3 * i] == false || g.front[3 * i + 1] == false || g.front[3 * i + 2] == false) continue;
                var p = new[] { g.tris[i].a, g.tris[i].b, g.tris[i].c };
                for (int k = 0; k < 3; k++)
                {
                    int j = (k + 1) % 3; Vector3 u = p[k], v = p[j];
                    string ku = Mathf.RoundToInt(u.x * 1e5f) + "," + Mathf.RoundToInt(u.y * 1e5f) + "," + Mathf.RoundToInt(u.z * 1e5f), kv = Mathf.RoundToInt(v.x * 1e5f) + "," + Mathf.RoundToInt(v.y * 1e5f) + "," + Mathf.RoundToInt(v.z * 1e5f);
                    string key = string.CompareOrdinal(ku, kv) < 0 ? ku + "|" + kv : kv + "|" + ku;
                    if (!edges.TryGetValue(key, out var e))
                    {
                        double d = SegDistD(sx, sy, g.cx[3 * i + k], g.cy[3 * i + k], g.cx[3 * i + j], g.cy[3 * i + j], out double fx, out double fy);
                        edges[key] = e = new EdgeRec { p = u, q = v, d = d, fx = fx, fy = fy };
                    }
                    e.tris.Add($"#{g.tris[i].index} {g.Facing(i)} {g.tris[i].region}"); e.idx.Add(g.tris[i].index); e.bones.Add(g.tris[i].bone);
                    if (g.tris[i].big) e.bigIdx.Add(g.tris[i].index);
                }
            }
            var ordered = edges.Values.OrderBy(e => e.d).ToList();
            var fr = new Framing();
            if (ordered.Count == 0) { o.AppendLine("      framing edges: none in the near set"); return fr; }
            var e1 = ordered[0];
            fr.has1 = true; fr.e1.AddRange(e1.idx); fr.e1Big.AddRange(e1.bigIdx); fr.b1.AddRange(e1.bones);
            var e2 = ordered.Skip(1).FirstOrDefault(e => e.fx * e1.fx + e.fy * e1.fy < 0.0);
            o.AppendLine($"      framing edge 1: {e1.d.ToString("F4", Inv)} px from the sample; triangles [{string.Join(", ", e1.tris)}]");
            if (e2 == null) { o.AppendLine("      framing edge 2: none on the opposite side within the near set"); return fr; }
            fr.has2 = true; fr.e2.AddRange(e2.idx); fr.e2Big.AddRange(e2.bigIdx); fr.b2.AddRange(e2.bones); fr.sepMm = SegSegDist(e1.p, e1.q, e2.p, e2.q) * 1000.0;
            o.AppendLine($"      framing edge 2: {e2.d.ToString("F4", Inv)} px from the sample on the opposite side; triangles [{string.Join(", ", e2.tris)}]; 3-D separation of the two edges {fr.sepMm.ToString("F2", Inv)} mm (world)");
            return fr;   // td B34e: targets are found by these triangle indices; td B35b: the K-class by their bones, the K-f list and the separation
        }

        // ---- F5 (director gate/pod49-s2-h1-fix-required, producer-49): lit 4x crops of the largest holes, marked and unmarked, written in the frame they are found ----
        class Crop { public string slot, group, point, region, f1, f2, f3 = ""; public float pitch; public int size, frame; }
        readonly List<Crop> h1Top = new List<Crop>();
        readonly Dictionary<string, Crop> h2Top = new Dictionary<string, Crop>();
        string outDir; Color32[] litBig;

        static string Safe(string s) => s.Replace(' ', '_').Replace('.', '_');

        /// <summary>Keeps the 3 largest H1 components of ordinary frames (not the Pose group), the largest H2 of at least 100 px per group (ordinary frames) and the largest H2 overall; a crop is written when a component enters a list, the displaced one's files are deleted.</summary>
        void Crops(string group, string point, float pitch, Comp c, bool isH1, bool isH2, string region, Camera cam)
        {
            bool ordinary = !group.StartsWith("Pose");
            if (isH1 && ordinary && (h1Top.Count < 3 || c.m.Count > h1Top[h1Top.Count - 1].size))
            {
                h1Top.Add(WriteCrop("H1-ordinary", group, point, pitch, c, region, cam));
                h1Top.Sort((p, q) => q.size.CompareTo(p.size));
                if (h1Top.Count > 3) { DeleteCrop(h1Top[3]); h1Top.RemoveAt(3); }
            }
            if (!isH2) return;
            foreach (var slot in new[] { ordinary && c.m.Count >= 100 ? "H2-" + group : null, "H2-overall" })
            {
                if (slot == null) continue;
                if (h2Top.TryGetValue(slot, out var old) && c.m.Count <= old.size) continue;
                if (old != null) DeleteCrop(old);
                h2Top[slot] = WriteCrop(slot, group, point, pitch, c, region, cam);
            }
        }

        Crop WriteCrop(string slot, string group, string point, float pitch, Comp c, string region, Camera cam, Color32[] src = null)
        {
            if (src == null) src = litBig ??= RenderPixels(cam, 0, false, SW, SH);   // the lit camera at 3840x2160, still in the frame the component was found (P0: or the hull-on twin passed in)
            int bx0 = c.x0 * 4, bx1 = (c.x1 + 1) * 4, by0 = c.y0 * 4, by1 = (c.y1 + 1) * 4;   // component bounding box in 4x pixels, y from the top
            int half = Mathf.Max(80, Mathf.Max(bx1 - bx0, by1 - by0) / 2 + 48), cx = (bx0 + bx1) / 2, cy = (by0 + by1) / 2;
            int x0 = Mathf.Clamp(cx - half, 0, SW - 1), x1 = Mathf.Clamp(cx + half, x0 + 1, SW), y0 = Mathf.Clamp(cy - half, 0, SH - 1), y1 = Mathf.Clamp(cy + half, y0 + 1, SH);
            int w = x1 - x0, h = y1 - y0;
            var plain = new Color32[w * h];
            for (int r = 0; r < h; r++) System.Array.Copy(src, (SH - y1 + r) * SW + x0, plain, r * w, w);   // texture rows run from the bottom
            var marked = (Color32[])plain.Clone();
            void Dot(int xx, int yy) { if (xx >= x0 && xx < x1 && yy >= y0 && yy < y1) marked[(y1 - 1 - yy) * w + (xx - x0)] = new Color32(255, 0, 0, 255); }
            for (int t = 0; t < 2; t++)
            {
                for (int xx = bx0 - 4 - t; xx <= bx1 + 3 + t; xx++) { Dot(xx, by0 - 4 - t); Dot(xx, by1 + 3 + t); }
                for (int yy = by0 - 4 - t; yy <= by1 + 3 + t; yy++) { Dot(bx0 - 4 - t, yy); Dot(bx1 + 3 + t, yy); }
            }
            Directory.CreateDirectory($"{outDir}/crops");
            string name = $"{outDir}/crops/{slot}_{Safe(group)}_{Safe(point)}_p{pitch:F0}_size{c.m.Count}_at{(int)c.cx}x{(int)c.cy}_f{Time.frameCount}";
            foreach (var (path, px) in new[] { (name + ".png", plain), (name + ".marked.png", marked) })
            {
                var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
                tex.SetPixels32(px); tex.Apply();
                File.WriteAllBytes(path, tex.EncodeToPNG());
                Object.Destroy(tex);
            }
            return new Crop { slot = slot, group = group, point = point, pitch = pitch, size = c.m.Count, region = region, frame = Time.frameCount, f1 = name + ".png", f2 = name + ".marked.png" };
        }

        /// <summary>td B35c: a lit 1x 40x40 window of the 960x540 render (<paramref name="lit1x"/>, bottom-origin) centred on the component's centroid, enlarged 8x nearest-neighbour (320x320), next to the crop's other files.</summary>
        void WriteWindow1x(Crop k, Color32[] lit1x, Comp c)
        {
            const int Win = 40, Zoom = 8, Out = Win * Zoom;
            int wx0 = Mathf.Clamp((int)c.cx - Win / 2, 0, W - Win), wy0 = Mathf.Clamp((int)c.cy - Win / 2, 0, H - Win);   // window origin, y from the top
            var px = new Color32[Out * Out];
            for (int r = 0; r < Out; r++)
            {
                int yTop = wy0 + (Win - 1 - r / Zoom);   // output row r counts from the bottom
                for (int q = 0; q < Out; q++) px[r * Out + q] = lit1x[(H - 1 - yTop) * W + wx0 + q / Zoom];
            }
            k.f3 = k.f1.Substring(0, k.f1.Length - 4) + ".1x40.png";
            var tex = new Texture2D(Out, Out, TextureFormat.RGBA32, false);
            try { tex.SetPixels32(px); tex.Apply(); File.WriteAllBytes(k.f3, tex.EncodeToPNG()); }
            finally { Object.Destroy(tex); }
        }

        static void DeleteCrop(Crop k) { if (File.Exists(k.f1)) File.Delete(k.f1); if (File.Exists(k.f2)) File.Delete(k.f2); if (k.f3.Length > 0 && File.Exists(k.f3)) File.Delete(k.f3); }

        string CropIndex()
        {
            var s = new StringBuilder("slot,frame,gait,point,pitch,componentSize_px,region,files (lit 4x crop 3840x2160 window; the .marked copy has a red box 4 px outside the component's bounding box; .1x40 = the lit 1x 40x40 window enlarged 8x nearest-neighbour)" + System.Environment.NewLine);
            foreach (var t in Targets)
            {
                if (!targetCrop.TryGetValue(t.slot, out var k)) { s.AppendLine(t.notFound); foreach (var n in targetNear.Where(n => n.StartsWith(t.slot + ":"))) s.AppendLine("  near candidate (no crop, no substitute) " + n); continue; }
                s.AppendLine(Row(k));
            }
            foreach (var k in hullCrops) s.AppendLine(Row(k));
            foreach (var k in kbCrops) s.AppendLine(Row(k));
            if (kbCropsOver > 0) s.AppendLine($"K-b crops: {kbCropsOver} more K-b components not cropped (cap {MaxKbCrops}); see s2-components.txt");
            foreach (var k in h1Top.Concat(h2Top.Values.OrderBy(v => v.slot))) s.AppendLine(Row(k));
            return s.ToString();
        }

        static string Row(Crop k) => $"{k.slot},{k.frame},{k.group},{k.point},{k.pitch:F0},{k.size},{k.region},{k.f1};{k.f2}{(k.f3.Length > 0 ? ";" + k.f3 : "")}";
    }
}
