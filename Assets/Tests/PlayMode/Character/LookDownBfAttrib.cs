using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using WashedAshore.Tests.Support;

namespace WashedAshore.Tests.PlayMode.Character
{
    /// <summary>
    /// PRINT ONLY (td-49 "11b GATE RUN 1 RED", G1). Behind LOOKDOWN_BF_ATTRIB=1: for every gate row that has back-face rays, re-casts the same 32x18 ray grid on the same frame against the baked view mesh
    /// (same Bake, same Moller-Trumbore, same MirrorTie.Replaces as LookDownTests.Measure) and records each back-face winner: gait, cycle point, pitch, ray cell, frame position (16:9 normalised), renderer, triangle index,
    /// dominant bone and weights, hit height above the feet, distance. Also writes the bake-inputs block of BarOneHitStills (td-49 ruling A) and an index in its column order, in lookdown\stills, so
    /// BarOneHitStillsClassifier (sub-folder "lookdown") classes the rows FOLD / HOLE. Nothing here changes a measure or an assert of the gate.
    /// </summary>
    public sealed class LookDownBfAttrib
    {
        public static bool Enabled => System.Environment.GetEnvironmentVariable("LOOKDOWN_BF_ATTRIB") == "1";
        /// <summary>LOOKDOWN_BF_STILLS_GAP_MM (director-49, gate/pod49-bar1-render-faithful-measure): when set to a number, a still pair is also captured for every back-face ray whose gap to the next FRONT hit is at least that many millimetres. Unset or not a number = the Arm/Hand stills only, as before.</summary>
        public static float StillsGapMm
        {
            get
            {
                var v = System.Environment.GetEnvironmentVariable("LOOKDOWN_BF_STILLS_GAP_MM");
                return !string.IsNullOrEmpty(v) && float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out float mm) ? mm : float.NaN;
            }
        }

        public static string StateLine => "LOOKDOWN_BF_ATTRIB=" + (Enabled ? "1" : "unset") + "; LOOKDOWN_BF_STILLS_GAP_MM=" + (float.IsNaN(StillsGapMm) ? "unset" : StillsGapMm.ToString("R", CultureInfo.InvariantCulture));

        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        static string F(float v) => v.ToString("R", Inv);
        static string V3(Vector3 v) => F(v.x) + " " + F(v.y) + " " + F(v.z);

        struct T { public Vector3 a, b, c; public SkinnedMeshRenderer smr; public int index, i0, i1, i2; }   // i0..i2: the corner vertex indices from the BAKED mesh (the shared mesh is not read in PlayMode)
        struct OpenEdge { public Vector3 a, b; }

        readonly StringBuilder attrib = new StringBuilder("# " + MirrorTie.StateLine + "; " + StateLine + System.Environment.NewLine + "gait,cyclePoint,pitch,rayX,rayY,frameX,frameY,renderer,triIndex,dominantBone,weights,hitHeightAbovePlayerOrigin_m,distance_m,entryToOpenEdge_m,rowId" + System.Environment.NewLine);
        readonly StringBuilder bake = new StringBuilder("# " + MirrorTie.StateLine + "; " + StateLine + "; lookdown bake inputs" + System.Environment.NewLine);
        readonly StringBuilder index = new StringBuilder("# " + MirrorTie.StateLine + "; " + StateLine + System.Environment.NewLine + "source,case,pitch,gridX,gridY,file,reproduced,sameTri,back,hitRenderer,hitTriIndex,inversionDot,entryToOpenEdge_m,restCentreMesh,restVertexIdx,class,rowId,csvRow" + System.Environment.NewLine);
        int id;
        public int Rows => id;
        readonly List<(string name, byte[] png)> stills = new List<(string, byte[])>();
        readonly StringBuilder stillIndex = new StringBuilder("# " + MirrorTie.StateLine + "; " + StateLine + System.Environment.NewLine + "# Gameplay stills of forearm / hand back-face hits (director-49, gate/pod49-stand70-walk-bar1-conditional case b) and, with LOOKDOWN_BF_STILLS_GAP_MM set, of every back-face ray whose gap to the next front hit is at least that many mm (gate/pod49-bar1-render-faithful-measure). Taken with the gate's own view camera in World.unity on the same frame as the measure, so the scene's own light (World.unity:49967 Light, Directional, intensity 1.4, colour 1 0.96 0.88; its Transform World.unity:50031 rotation euler 50 -30 0, line 50039), the scene's camera FOV (World.unity:83758, 70 degrees; PlayerController.View.cs:38 authoredFov) and the stance eye height (PlayerController.View.cs:22 AuthoredEyeHeight) apply. The values below are the live ones." + System.Environment.NewLine + "file,gait,cyclePoint,pitch,rayX,rayY,triIndex,cameraPos,cameraEulerDeg,fovDeg,eyeHeightAbovePlayerOrigin_m,lightEulerDeg,lightIntensity,gapToNextFront_mm,dominantBone" + System.Environment.NewLine);

        static string AnimText(Animator animator, string label)
        {
            var info = animator.GetCurrentAnimatorStateInfo(0);
            string clip = ""; float cw = -1f, clen = 0f; bool looping = false;
            foreach (var ci in animator.GetCurrentAnimatorClipInfo(0)) if (ci.weight > cw) { cw = ci.weight; clip = ci.clip.name; clen = ci.clip.length; looping = ci.clip.isLooping; }
            return $"{label}|{clip}|{F(info.normalizedTime)}|{F(clen)}|{(looping ? 1 : 0)}";
        }

        static List<T> BakeView(IEnumerable<Renderer> renderers)
        {
            var tris = new List<T>(40000);
            var mesh = new Mesh();
            foreach (var r in renderers)
            {
                if (!r.enabled || !(r is SkinnedMeshRenderer smr) || smr.sharedMesh == null) continue;
                smr.BakeMesh(mesh, false);
                var v = mesh.vertices;
                var t = mesh.triangles;
                Vector3 pos = smr.transform.position;
                Quaternion rot = smr.transform.rotation;
                for (int i = 0; i < t.Length; i += 3)
                    tris.Add(new T { a = pos + rot * v[t[i]], b = pos + rot * v[t[i + 1]], c = pos + rot * v[t[i + 2]], smr = smr, index = i / 3, i0 = t[i], i1 = t[i + 1], i2 = t[i + 2] });
            }
            Object.Destroy(mesh);
            return tris;
        }

        /// <summary>Call on the SAME frame as the Measure of the row, only when that Measure counted back faces. Records every back-face winner of the grid.</summary>
        public void Add(string gait, int point, float pitch, IEnumerable<Renderer> viewRenderers, Camera cam, float feetY, int gridX, int gridY, Animator viewAnimator, Animator shadowAnimator)
        {
            var tris = BakeView(viewRenderers);
            List<OpenEdge> open = null;
            Transform ct = cam.transform;
            Vector3 o = ct.position;
            float halfV = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad), halfH = halfV * (16f / 9f);
            for (int iy = 0; iy < gridY; iy++)
                for (int ix = 0; ix < gridX; ix++)
                {
                    float nx = ix / (float)(gridX - 1) * 2f - 1f, ny = iy / (float)(gridY - 1) * 2f - 1f;
                    Vector3 d = (ct.forward + ct.right * (nx * halfH) + ct.up * (ny * halfV)).normalized;
                    float bestT = float.MaxValue; bool bestBack = false; T best = default; bool have = false;
                    var all = new List<(float t, T tri, bool back)>();   // every intersection of this ray (td-49 "H1 RESULT" rayHits print)
                    foreach (var tri in tris)
                    {
                        Vector3 e1 = tri.b - tri.a, e2 = tri.c - tri.a, p = Vector3.Cross(d, e2);
                        float det = Vector3.Dot(e1, p);
                        if (Mathf.Abs(det) < 1e-9f) continue;
                        float inv = 1f / det;
                        Vector3 sv = o - tri.a;
                        float u = Vector3.Dot(sv, p) * inv;
                        if (u < 0f || u > 1f) continue;
                        Vector3 q = Vector3.Cross(sv, e1);
                        float v = Vector3.Dot(d, q) * inv;
                        if (v < 0f || u + v > 1f) continue;
                        float t = Vector3.Dot(e2, q) * inv;
                        if (t < 0f) continue;
                        bool isBack = Vector3.Dot(Vector3.Cross(e1, e2), d) > 0f;
                        all.Add((t, tri, isBack));
                        if (MirrorTie.Replaces(have, bestT, bestBack, best.a, best.b, best.c, t, isBack, tri.a, tri.b, tri.c)) { bestT = t; bestBack = isBack; best = tri; have = true; }
                    }
                    if (!have || !bestBack) continue;
                    open ??= FindOpenEdges(tris);
                    Vector3 hit = o + d * bestT;
                    float entry = float.MaxValue;
                    foreach (var e in open) entry = Mathf.Min(entry, SegmentSegmentDistance(o, hit, e.a, e.b));
                    string dominant = Record(gait, point, pitch, ix, iy, nx, ny, best, bestT, hit, entry, feetY, viewAnimator, shadowAnimator, cam, all);
                    float gap = float.NaN;   // gap from the back-face winner to the next FRONT hit along the ray, metres
                    foreach (var x in all.OrderBy(h => h.t)) if (!x.back && x.t > bestT) { gap = x.t - bestT; break; }
                    bool noFront = float.IsNaN(gap);   // R3(a), the candidate real hole: a back face with no front face behind it ALWAYS gets a still (td-49, qa-49), whatever the switch says
                    bool gapStill = noFront || (!float.IsNaN(StillsGapMm) && gap * 1000f >= StillsGapMm);
                    if (dominant.Contains("Arm") || dominant.Contains("Hand") || gapStill)
                        Still(cam, $"{gait}_cp{point}_p{pitch:+0;-0;0}_c{ix}x{iy}_tri{best.index}", gait, point, pitch, ix, iy, gridX, gridY, best.index, feetY, gap, dominant);
                }
        }

        string Record(string gait, int point, float pitch, int ix, int iy, float nx, float ny, T best, float dist, Vector3 hit, float entry, float feetY, Animator viewAnimator, Animator shadowAnimator, Camera cam, List<(float t, T tri, bool back)> all)
        {
            var smr = best.smr;
            var mesh = smr.sharedMesh;
            var bones = smr.bones;
            // dominant bone and top-2 weights of the hit triangle's first corner, named as Bake in LookDownTests names them
            var bw = mesh.boneWeights;   // B2: an empty array prints "unread", never an empty string
            int firstVertex = best.i0;   // B1: from the baked mesh, not from the shared mesh
            string N(int bi) => bi >= 0 && bi < bones.Length && bones[bi] != null ? bones[bi].name : "?";
            string dom = bw.Length > firstVertex ? N(bw[firstVertex].boneIndex0) : "unread";
            string wtext = bw.Length > firstVertex ? $"{N(bw[firstVertex].boneIndex0)}: {bw[firstVertex].weight0:F2}; {N(bw[firstVertex].boneIndex1)}: {bw[firstVertex].weight1:F2}" : "unread";
            int rowId = ++id;
            attrib.AppendLine($"{gait},{point},{pitch:F0},{ix},{iy},{nx:F3},{ny:F3},{smr.name},{best.index},{dom},\"{wtext}\",{hit.y - feetY:F3},{dist:F4},{entry:F4},{rowId}");
            string label = $"{gait} point {point}";
            index.AppendLine($"lookdown,{label},{pitch:F0},{ix},{iy},,Y,Y,Y,{smr.name},{best.index},,{entry:F4},,,,{rowId},{gait};{point};{dom}");
            bake.AppendLine($"ROW {rowId}");
            bake.AppendLine($"src lookdown|{label}|{pitch:F0}|{ix}|{iy}|");
            bake.AppendLine("anim " + AnimText(viewAnimator, "view"));
            bake.AppendLine("animShadow " + AnimText(shadowAnimator, "shadow"));
            AppendRayHits(cam, all, best, dist);
            bake.AppendLine("hit 1");
            bake.AppendLine($"rend {smr.name}|{mesh.name}|{mesh.vertexCount}|{mesh.GetIndexCount(0)}|{mesh.bindposeCount}|{bones.Length}");
            bake.AppendLine($"xform {V3(smr.transform.position)} | {F(smr.transform.rotation.x)} {F(smr.transform.rotation.y)} {F(smr.transform.rotation.z)} {F(smr.transform.rotation.w)} | {V3(smr.transform.lossyScale)}");
            bake.AppendLine($"tri {best.index}|{entry.ToString("R", Inv)}");
            bake.AppendLine($"corners {V3(best.a)} {V3(best.b)} {V3(best.c)}");
            var shapes = new StringBuilder();
            for (int i = 0; i < mesh.blendShapeCount; i++) shapes.Append(mesh.GetBlendShapeName(i)).Append('=').Append(F(smr.GetBlendShapeWeight(i))).Append(';');
            bake.AppendLine($"shapes {mesh.blendShapeCount}|{shapes}");
            bake.AppendLine($"bones {bones.Length}");
            var names = new StringBuilder("bonenames ");   // B3: the classifier names the dominant bone (majority of the three corners' max-weight bones) from these
            for (int i = 0; i < bones.Length; i++) names.Append(i == 0 ? "" : "|").Append(bones[i] != null ? bones[i].name : "?");
            bake.AppendLine(names.ToString());
            foreach (var b in bones)
            {
                if (b == null) { bake.AppendLine("m null"); continue; }
                var m = b.localToWorldMatrix;
                var line = new StringBuilder("m");
                for (int k = 0; k < 16; k++) line.Append(' ').Append(F(m[k]));
                bake.AppendLine(line.ToString());
            }
            bake.AppendLine("END");
            return dom;
        }

        /// <summary>One gameplay still of a hit (unmarked + the same with the ray cell outlined in red) with the camera, light and eye height recorded in the still index. Print only.</summary>
        void Still(Camera cam, string name, string gait, int point, float pitch, int ix, int iy, int gridX, int gridY, int triIndex, float feetY, float gap, string dominant)
        {
            var rt = new RenderTexture(960, 540, 24);
            var old = cam.targetTexture;
            Texture2D tex = null;
            try
            {
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;
                tex = new Texture2D(960, 540, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, 960, 540), 0, 0);
                tex.Apply();
                stills.Add((name + ".png", tex.EncodeToPNG()));
                int cx = Mathf.RoundToInt(ix / (float)(gridX - 1) * 959f), cy = Mathf.RoundToInt(iy / (float)(gridY - 1) * 539f);
                Outline(tex, cx, cy, 22, Color.red);
                tex.Apply();
                stills.Add((name + ".marked.png", tex.EncodeToPNG()));
                Light sun = null;
                foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None)) if (l.type == LightType.Directional) { sun = l; break; }
                var ct = cam.transform;
                stillIndex.AppendLine($"{name}.png,{gait},{point},{pitch:F0},{ix},{iy},{triIndex},{V3(ct.position).Replace(' ', ';')},{V3(ct.eulerAngles).Replace(' ', ';')},{F(cam.fieldOfView)},{F(ct.position.y - feetY)},{(sun != null ? V3(sun.transform.eulerAngles).Replace(' ', ';') : "none")},{(sun != null ? F(sun.intensity) : "")},{(float.IsNaN(gap) ? "none" : F(gap * 1000f))},{dominant}");
            }
            finally
            {
                cam.targetTexture = old;
                RenderTexture.active = null;
                if (tex != null) Object.Destroy(tex);
                rt.Release();
                Object.Destroy(rt);
            }
        }

        static void Outline(Texture2D tex, int cx, int cy, int half, Color c)
        {
            for (int t = 0; t < 3; t++)
                for (int d = -half; d <= half; d++)
                {
                    Put(tex, cx + d, cy - half + t, c); Put(tex, cx + d, cy + half - t, c);
                    Put(tex, cx - half + t, cy + d, c); Put(tex, cx + half - t, cy + d, c);
                }
        }

        static void Put(Texture2D tex, int x, int y, Color c)
        {
            if (x >= 0 && x < tex.width && y >= 0 && y < tex.height) tex.SetPixel(x, y, c);
        }

        /// <summary>
        /// td-49 "H1 RESULT" rayHits print: the camera (position, rotation, FOV) and EVERY intersection of the ray sorted by t (t, renderer, triIndex, front/back, whether MirrorTie pairs it with its exact mirror at the same t),
        /// the hit count with its parity, and the count before the winner with its parity (odd = the origin lies inside a shell: H3). Print only; the classifier ignores these lines.
        /// </summary>
        void AppendRayHits(Camera cam, List<(float t, T tri, bool back)> all, T winner, float winnerT)
        {
            var ct = cam.transform;
            bake.AppendLine($"camera {V3(ct.position)} | {F(ct.rotation.x)} {F(ct.rotation.y)} {F(ct.rotation.z)} {F(ct.rotation.w)} | fov {F(cam.fieldOfView)}");
            var sorted = all.OrderBy(x => x.t).ToList();
            bake.AppendLine($"rayhits {sorted.Count}|{(sorted.Count % 2 == 1 ? "odd" : "even")}");
            int before = sorted.Count(x => x.t < winnerT - MirrorTie.TieTolerance);
            bake.AppendLine($"beforeWinner {before}|{(before % 2 == 1 ? "odd" : "even")}|winner {winner.smr.name} {winner.index} back t {F(winnerT)}");
            for (int i = 0; i < sorted.Count; i++)
            {
                var x = sorted[i];
                bool paired = false;
                for (int j = 0; j < sorted.Count && !paired; j++)
                    paired = j != i && Mathf.Abs(sorted[j].t - x.t) <= MirrorTie.TieTolerance && sorted[j].back != x.back && MirrorTie.IsExactMirror(x.tri.a, x.tri.b, x.tri.c, sorted[j].tri.a, sorted[j].tri.b, sorted[j].tri.c);
                bake.AppendLine($"rayhit {i}|{F(x.t)}|{x.tri.smr.name}|{x.tri.index}|{(x.back ? "back" : "front")}|{(paired ? "mirror-paired" : "-")}");
            }
        }

        /// <summary>Writes lookdown-bf-attrib.csv and the classifier inputs stills\stills-bake-inputs.txt + stills-index.csv under outDir (the lookdown run folder).</summary>
        public void Write(string outDir)
        {
            File.WriteAllText($"{outDir}/lookdown-bf-attrib.csv", attrib.ToString());
            Directory.CreateDirectory($"{outDir}/stills");
            File.WriteAllText($"{outDir}/stills/stills-bake-inputs.txt", bake.ToString());
            File.WriteAllText($"{outDir}/stills/stills-index.csv", index.ToString());
            if (stills.Count > 0)
            {
                Directory.CreateDirectory($"{outDir}/forearm-stills");
                foreach (var (name, png) in stills) File.WriteAllBytes($"{outDir}/forearm-stills/{name}", png);
                File.WriteAllText($"{outDir}/forearm-stills/forearm-stills-index.csv", stillIndex.ToString());
            }
        }

        static Vector3Int QPos(Vector3 p) => new Vector3Int(Mathf.RoundToInt(p.x * 1e4f), Mathf.RoundToInt(p.y * 1e4f), Mathf.RoundToInt(p.z * 1e4f));

        // The two measures below are copies of BarOneHitStills (same 0.1 mm weld, same segment distance) so the entry distance means the same in both files.
        static List<OpenEdge> FindOpenEdges(List<T> tris)
        {
            var count = new Dictionary<(Vector3Int, Vector3Int), int>();
            var ends = new Dictionary<(Vector3Int, Vector3Int), OpenEdge>();
            void AddEdge(Vector3 p, Vector3 q)
            {
                var kp = QPos(p); var kq = QPos(q);
                if (kp == kq) return;
                var key = (kp.x, kp.y, kp.z).CompareTo((kq.x, kq.y, kq.z)) <= 0 ? (kp, kq) : (kq, kp);
                count[key] = count.TryGetValue(key, out int n) ? n + 1 : 1;
                if (!ends.ContainsKey(key)) ends[key] = new OpenEdge { a = p, b = q };
            }
            foreach (var t in tris) { AddEdge(t.a, t.b); AddEdge(t.b, t.c); AddEdge(t.c, t.a); }
            var open = new List<OpenEdge>();
            foreach (var kv in count) if (kv.Value == 1) open.Add(ends[kv.Key]);
            return open;
        }

        static float SegmentSegmentDistance(Vector3 p1, Vector3 q1, Vector3 p2, Vector3 q2)
        {
            Vector3 d1 = q1 - p1, d2 = q2 - p2, r = p1 - p2;
            float a = Vector3.Dot(d1, d1), e = Vector3.Dot(d2, d2), f = Vector3.Dot(d2, r), s, t;
            if (a <= 1e-12f && e <= 1e-12f) return r.magnitude;
            if (a <= 1e-12f) { s = 0f; t = Mathf.Clamp01(f / e); }
            else
            {
                float c = Vector3.Dot(d1, r);
                if (e <= 1e-12f) { t = 0f; s = Mathf.Clamp01(-c / a); }
                else
                {
                    float b = Vector3.Dot(d1, d2), denom = a * e - b * b;
                    s = denom > 1e-12f ? Mathf.Clamp01((b * f - c * e) / denom) : 0f;
                    t = (b * s + f) / e;
                    if (t < 0f) { t = 0f; s = Mathf.Clamp01(-c / a); }
                    else if (t > 1f) { t = 1f; s = Mathf.Clamp01((b - c) / a); }
                }
            }
            return (p1 + d1 * s - (p2 + d2 * t)).magnitude;
        }
    }
}
