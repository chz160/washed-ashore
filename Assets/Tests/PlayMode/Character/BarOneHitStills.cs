using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using WashedAshore.Character.Appearance;
using WashedAshore.Character.Stance;
using WashedAshore.Gameplay;
using WashedAshore.Tests.Support;

namespace WashedAshore.Tests.PlayMode.Character
{
    /// <summary>
    /// PRINT ONLY (td-49 "BAR 1 HIT STILLS" with amendments A-D, director-49): one still per Bar 1 back-face ray, READ at run time from the CSVs written earlier in the SAME run folder (PC_RUN must be the same for the
    /// whole baton): range/range-backface-attrib-eyes.csv (RangeEndTests.EyeEnds_LowAndHigh with RANGEEND_ATTRIB=1; 16x9 ray grid) and sweep/backface-hits.csv (PitchSweepTests.BackFaceHits_Print; 9x5 grid).
    /// The pitches are walked in the SOURCE order (-85..+85 in 5 degree steps, SetLook then two frames, nothing else inside the loop) and the stances in the source order, so a still is taken on the same frame history as the
    /// row it shows. At each row the ray of that grid cell is re-cast against the baked view mesh with the same Bake + intersection + MirrorTie.Replaces as the sources: stills-index.csv gets reproduced=Y/N, the hit
    /// renderer and triangle index and back=Y/N, and the file name gets ".notrepro" when the row is not reproduced. Two files per row: the unmarked 960x540 still and the .marked one (red outline of the cell).
    /// An index is written into each of range\stills and sweep\stills. Asserts only that the CSVs have rows. No row is hard-coded.
    /// </summary>
    public class BarOneHitStills : StanceFixture
    {
        struct Row { public string label, kind, line, srcRenderer; public int srcTri; public float eye, pitch; public bool high; public int ix, iy, gx, gy; }
        struct Tri { public Vector3 a, b, c; public string rend; public int index; public SkinnedMeshRenderer smr; public int i0, i1, i2; }

        PlayerCharacterView view;
        FirstPersonBody fp;

        static string Slug(string s) => new string(s.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray()).Trim('_');

        static List<string[]> ReadCsv(string path)
        {
            var rows = new List<string[]>();
            foreach (var line in File.ReadAllLines(path))
            {
                if (line.Length == 0 || line.StartsWith("#")) continue;
                rows.Add(line.Split(','));
            }
            return rows;   // [0] is the column header
        }

        static (StanceKey key, Stance stance) Map(string kind)
        {
            switch (kind)
            {
                case "crouch": return (StanceKey.Crouch, Stance.Crouch);
                case "squat": return (StanceKey.Squat, Stance.Squat);
                case "sit": return (StanceKey.Sit, Stance.Sit);
                case "sleep": return (StanceKey.Sleep, Stance.Sleep);
            }
            return (StanceKey.None, Stance.Stand);
        }

        void Tune(string kind, float eye)
        {
            var t = player.StanceTuning;
            switch (kind)
            {
                case "crouch": t.crouchEye = eye; break;
                case "squat": t.squatEye = eye; break;
                case "sit": t.sitEye = eye; break;
                case "sleep": t.sleepEye = eye; break;
            }
        }

        IEnumerator Setup(bool ranged, bool high)
        {
            yield return LoadWorld();
            yield return OnFloor();
            view = player.GetComponent<PlayerCharacterView>();
            Assert.IsNotNull(view?.Body, "no body on the Player");
            fp = view.FirstPerson;
            if (ranged)
            {
                var app = view.Body.Appearance;
                view.Body.Animator.runtimeAnimatorController = app.RangeController(high, true);
                if (view.ViewCopy != null && view.ViewCopy.ViewAnimator != null) view.ViewCopy.ViewAnimator.runtimeAnimatorController = app.RangeController(high, false);
                Assert.IsTrue(view.Body.Animator.runtimeAnimatorController.name.Contains("Range"), "not a range controller");
            }
            if (ranged) yield return null;   // the sweep source (BackFaceHits_Print) has no frame between OnFloor and its first stance
        }

        [UnityTest, Explicit("print only: stills of the Bar 1 back-face rays, read from the CSVs of the same run"), Timeout(3000000)]
        public IEnumerator Stills_OfTheBarOneBackFaceRays()
        {
            PinTicks();
            MirrorTie.ResetCounters();
            string eyesCsv = $"{RunOutput.Dir("range")}/range-backface-attrib-eyes.csv", sweepCsv = $"{RunOutput.Dir("sweep")}/backface-hits.csv";
            var rows = new List<Row>();
            if (File.Exists(eyesCsv))
            {
                var csv = ReadCsv(eyesCsv);
                var h = csv[0].ToList();
                int cCase = h.IndexOf("case"), cPitch = h.IndexOf("pitch"), cX = h.IndexOf("gridX"), cY = h.IndexOf("gridY"), cRend = h.IndexOf("renderer"), cTri = h.IndexOf("triIndex");
                for (int i = 1; i < csv.Count; i++)
                {
                    string label = csv[i][cCase];
                    if (label.StartsWith("sleep pitch")) continue;   // the sleep-pitch-end rows are not eye-end cases
                    var parts = label.Split(' ');
                    rows.Add(new Row { label = label, kind = parts[0], eye = float.Parse(parts[1], CultureInfo.InvariantCulture), high = label.Contains("(High)"), pitch = float.Parse(csv[i][cPitch], CultureInfo.InvariantCulture), ix = int.Parse(csv[i][cX]), iy = int.Parse(csv[i][cY]), gx = 16, gy = 9, srcRenderer = csv[i][cRend], srcTri = int.Parse(csv[i][cTri]), line = string.Join(";", csv[i]) });
                }
            }
            var sweepRows = new List<Row>();
            if (File.Exists(sweepCsv))
            {
                var csv = ReadCsv(sweepCsv);
                var h = csv[0].ToList();
                int cStance = h.IndexOf("stance"), cPitch = h.IndexOf("pitch"), cX = h.IndexOf("gridX"), cY = h.IndexOf("gridY"), cRend = h.IndexOf("renderer"), cTri = h.IndexOf("triIndex");
                for (int i = 1; i < csv.Count; i++)
                    sweepRows.Add(new Row { label = csv[i][cStance], kind = csv[i][cStance].ToLowerInvariant(), pitch = float.Parse(csv[i][cPitch], CultureInfo.InvariantCulture), ix = int.Parse(csv[i][cX]), iy = int.Parse(csv[i][cY]), gx = 9, gy = 5, srcRenderer = csv[i][cRend], srcTri = int.Parse(csv[i][cTri]), line = string.Join(";", csv[i].Take(9)) });
            }
            Assert.IsTrue(rows.Count > 0 || sweepRows.Count > 0, $"no rows: neither {eyesCsv} nor {sweepCsv} has any (run the attribution tests with the same PC_RUN first)");
            string header = MirrorTie.StateLine + "; stills" + System.Environment.NewLine;
            var eyesIndex = new StringBuilder("# " + header + "source,case,pitch,gridX,gridY,file,reproduced,sameTri,back,hitRenderer,hitTriIndex,inversionDot,entryToOpenEdge_m,restCentreMesh,restVertexIdx,class,rowId,csvRow" + System.Environment.NewLine);
            var sweepIndex = new StringBuilder("# " + header + "source,case,pitch,gridX,gridY,file,reproduced,sameTri,back,hitRenderer,hitTriIndex,inversionDot,entryToOpenEdge_m,restCentreMesh,restVertexIdx,class,rowId,csvRow" + System.Environment.NewLine);
            string eyesDir = Directory.CreateDirectory($"{RunOutput.Dir("range")}/stills").FullName, sweepDir = Directory.CreateDirectory($"{RunOutput.Dir("sweep")}/stills").FullName;

            // eye-end cases, as RangeEndTests.EyeEnds_LowAndHigh runs them: a fresh world per case, range controllers of its end, the tuned eye, Go, 2 s, then the pitch walk
            foreach (var group in rows.GroupBy(r => r.label))
            {
                var first = group.First();
                var (key, stance) = Map(first.kind);
                yield return Setup(true, first.high);
                Tune(first.kind, first.eye);
                yield return Go(key, 6f);
                Assert.AreEqual(stance, player.Stance, $"could not reach {stance}");
                yield return new WaitForSeconds(2f);
                for (float pitch = -85f; pitch <= 85.01f; pitch += 5f)
                {
                    player.SetLook(player.Yaw, pitch);
                    yield return null;
                    yield return null;
                    foreach (var r in group.Where(x => Mathf.Approximately(x.pitch, pitch))) Capture(r, eyesDir, "range", eyesIndex);
                }
            }
            // pitch-sweep rows, as PitchSweepTests.BackFaceHits_Print runs them: ONE world, the five stances in order, each: Go, 2 s, the pitch walk (BackFaceHits_Print takes no stills: nothing but the two frames per pitch)
            if (sweepRows.Count > 0)
            {
                yield return Setup(false, false);
                var order = new[] { StanceKey.None, StanceKey.Crouch, StanceKey.Squat, StanceKey.Sit, StanceKey.Sleep };
                for (int s = 0; s < order.Length; s++)
                {
                    var stance = (Stance)s;
                    if (order[s] != StanceKey.None) yield return Go(order[s], 6f);
                    Assert.AreEqual(stance, player.Stance, $"could not reach {stance}");
                    yield return new WaitForSeconds(2f);
                    var mine = sweepRows.Where(r => r.kind == stance.ToString().ToLowerInvariant()).ToList();
                    for (float pitch = -85f; pitch <= 85.01f; pitch += 5f)
                    {
                        player.SetLook(player.Yaw, pitch);
                        yield return null;
                        yield return null;
                        foreach (var r in mine.Where(x => Mathf.Approximately(x.pitch, pitch))) Capture(r, sweepDir, "sweep", sweepIndex);
                    }
                }
            }
            foreach (var kv in bakeInputs) File.WriteAllText($"{kv.Key}/stills-bake-inputs.txt", kv.Value.ToString());
            File.WriteAllText($"{eyesDir}/stills-index.csv", eyesIndex.ToString());
            File.WriteAllText($"{sweepDir}/stills-index.csv", sweepIndex.ToString());
            Debug.Log($"BarOneHitStills: {rows.Count} eye-end rows from {eyesCsv}, {sweepRows.Count} sweep rows from {sweepCsv}; {MirrorTie.StateLine}");
        }

        // ---- hole-vs-fold measures (td-49 amendment G) ----
        struct OpenEdge { public Vector3 a, b; }

        static Vector3Int QPos(Vector3 p) => new Vector3Int(Mathf.RoundToInt(p.x * 1e4f), Mathf.RoundToInt(p.y * 1e4f), Mathf.RoundToInt(p.z * 1e4f));

        /// <summary>Edges used by exactly one triangle once vertices at the same position are welded at 0.1 mm: the open boundary of the baked view mesh.</summary>
        static List<OpenEdge> FindOpenEdges(List<Tri> tris)
        {
            var count = new Dictionary<(Vector3Int, Vector3Int), int>();
            var ends = new Dictionary<(Vector3Int, Vector3Int), OpenEdge>();
            void Add(Vector3 p, Vector3 q)
            {
                var kp = QPos(p); var kq = QPos(q);
                if (kp == kq) return;
                var key = (kp.x, kp.y, kp.z).CompareTo((kq.x, kq.y, kq.z)) <= 0 ? (kp, kq) : (kq, kp);
                count[key] = count.TryGetValue(key, out int n) ? n + 1 : 1;
                if (!ends.ContainsKey(key)) ends[key] = new OpenEdge { a = p, b = q };
            }
            foreach (var t in tris) { Add(t.a, t.b); Add(t.b, t.c); Add(t.c, t.a); }
            var open = new List<OpenEdge>();
            foreach (var kv in count) if (kv.Value == 1) open.Add(ends[kv.Key]);
            return open;
        }

        /// <summary>Smallest distance between the segments p1-q1 and p2-q2 (Ericson, Real-Time Collision Detection 5.1.9).</summary>
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

        // ---- bake inputs for the EditMode classifier (td-49 ruling A1) ----
        readonly Dictionary<string, StringBuilder> bakeInputs = new Dictionary<string, StringBuilder>();
        int rowCounter;
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        static string F(float v) => v.ToString("R", Inv);
        static string V3(Vector3 v) => F(v.x) + " " + F(v.y) + " " + F(v.z);

        /// <summary>
        /// One block per row for TheBarOneHitStillsClassifier (EditMode, read-only): the hit triangle's baked world corners and its index, the renderer and its transform, the localToWorldMatrix of every bone of the renderer
        /// in order, the renderer's live blend shape weights (the Knee shapes are in the bake), the mesh counts the classifier must find again, the stance clip and its normalized time (ta-49 fold_probe F4).
        /// </summary>
        static string AnimText(Animator animator, string label)
        {
            var info = animator.GetCurrentAnimatorStateInfo(0);
            string clip = ""; float cw = -1f, clen = 0f; bool looping = false;
            foreach (var ci in animator.GetCurrentAnimatorClipInfo(0)) if (ci.weight > cw) { cw = ci.weight; clip = ci.clip.name; clen = ci.clip.length; looping = ci.clip.isLooping; }
            return $"{label}|{clip}|{F(info.normalizedTime)}|{F(clen)}|{(looping ? 1 : 0)}";
        }

        /// <summary>
        /// td-49 "H1 RESULT" rayHits print: the camera (position, rotation, FOV) and EVERY intersection of the row's ray sorted by t (t, renderer, triIndex, front/back, whether MirrorTie pairs it with its exact mirror at the same t),
        /// the hit count with its parity, and the count before the winner with its parity (odd = the origin lies inside a shell: H3). Print only; the classifier ignores these lines.
        /// </summary>
        void AppendRayHits(StringBuilder sb, HitInfo h)
        {
            var cam = player.ViewCamera;
            var ct = cam.transform;
            sb.AppendLine($"camera {V3(ct.position)} | {F(ct.rotation.x)} {F(ct.rotation.y)} {F(ct.rotation.z)} {F(ct.rotation.w)} | fov {F(cam.fieldOfView)}");
            if (h.all == null) return;
            var sorted = h.all.OrderBy(x => x.t).ToList();
            sb.AppendLine($"rayhits {sorted.Count}|{(sorted.Count % 2 == 1 ? "odd" : "even")}");
            if (h.have)
            {
                float winnerT = (h.hit - h.origin).magnitude;
                int before = sorted.Count(x => x.t < winnerT - MirrorTie.TieTolerance);
                sb.AppendLine($"beforeWinner {before}|{(before % 2 == 1 ? "odd" : "even")}|winner {h.tri.rend} {h.tri.index} {(h.back ? "back" : "front")} t {F(winnerT)}");
            }
            for (int i = 0; i < sorted.Count; i++)
            {
                var x = sorted[i];
                bool paired = false;
                for (int j = 0; j < sorted.Count && !paired; j++)
                    paired = j != i && Mathf.Abs(sorted[j].t - x.t) <= MirrorTie.TieTolerance && sorted[j].back != x.back && MirrorTie.IsExactMirror(x.tri.a, x.tri.b, x.tri.c, sorted[j].tri.a, sorted[j].tri.b, sorted[j].tri.c);
                sb.AppendLine($"rayhit {i}|{F(x.t)}|{x.tri.rend}|{x.tri.index}|{(x.back ? "back" : "front")}|{(paired ? "mirror-paired" : "-")}");
            }
        }

        void Record(string dir, int id, Row r, string source, string file, HitInfo h, float entry)
        {
            if (!bakeInputs.TryGetValue(dir, out var sb)) bakeInputs[dir] = sb = new StringBuilder("# " + MirrorTie.StateLine + "; stills bake inputs" + System.Environment.NewLine);
            sb.AppendLine($"ROW {id}");
            sb.AppendLine($"src {source}|{r.label}|{r.pitch:F0}|{r.ix}|{r.iy}|{file}");
            // O1: the hit is on the VIEW body, which has its own Animator (and range controller on the eye-end runs): record the VIEW animator's clip, normalized time, length and loop flag; the shadow's is information only
            var viewAnimator = view.ViewCopy != null && view.ViewCopy.ViewAnimator != null ? view.ViewCopy.ViewAnimator : view.Body.Animator;
            sb.AppendLine("anim " + AnimText(viewAnimator, "view"));
            sb.AppendLine("animShadow " + AnimText(view.Body.Animator, "shadow"));
            AppendRayHits(sb, h);
            if (!h.back) { sb.AppendLine("hit 0"); sb.AppendLine("END"); return; }
            var smr = h.tri.smr;
            var mesh = smr.sharedMesh;
            sb.AppendLine("hit 1");
            sb.AppendLine($"rend {smr.name}|{mesh.name}|{mesh.vertexCount}|{mesh.GetIndexCount(0)}|{mesh.bindposeCount}|{smr.bones.Length}");
            sb.AppendLine($"xform {V3(smr.transform.position)} | {F(smr.transform.rotation.x)} {F(smr.transform.rotation.y)} {F(smr.transform.rotation.z)} {F(smr.transform.rotation.w)} | {V3(smr.transform.lossyScale)}");
            sb.AppendLine($"tri {h.tri.index}|{entry:R}");
            sb.AppendLine($"corners {V3(h.tri.a)} {V3(h.tri.b)} {V3(h.tri.c)}");
            var shapes = new StringBuilder();
            for (int i = 0; i < mesh.blendShapeCount; i++) shapes.Append(mesh.GetBlendShapeName(i)).Append('=').Append(F(smr.GetBlendShapeWeight(i))).Append(';');
            sb.AppendLine($"shapes {mesh.blendShapeCount}|{shapes}");
            sb.AppendLine($"bones {smr.bones.Length}");
            foreach (var b in smr.bones)
            {
                if (b == null) { sb.AppendLine("m null"); continue; }   // O3: never an identity stand-in
                var m = b.localToWorldMatrix;
                var line = new StringBuilder("m");
                for (int k = 0; k < 16; k++) line.Append(' ').Append(F(m[k]));
                sb.AppendLine(line.ToString());
            }
            sb.AppendLine("END");
        }

        List<Tri> BakeView()
        {
            var tris = new List<Tri>(40000);
            var mesh = new Mesh();
            foreach (var r in fp.ViewRenderers)
            {
                if (!r.enabled || !(r is SkinnedMeshRenderer smr)) continue;
                smr.BakeMesh(mesh, false);
                var v = mesh.vertices;
                var t = mesh.triangles;
                Vector3 pos = smr.transform.position;
                Quaternion rot = smr.transform.rotation;
                for (int i = 0; i < t.Length; i += 3)
                    tris.Add(new Tri { a = pos + rot * v[t[i]], b = pos + rot * v[t[i + 1]], c = pos + rot * v[t[i + 2]], rend = smr.name, index = i / 3, smr = smr, i0 = t[i], i1 = t[i + 1], i2 = t[i + 2] });
            }
            Object.Destroy(mesh);
            return tris;
        }

        struct HitInfo { public bool have, back; public Tri tri; public Vector3 origin, hit; public List<Tri> tris; public List<(float t, Tri tri, bool back)> all; }   // all: every intersection of the ray (td-49 "H1 RESULT" rayHits print)

        /// <summary>Re-casts the row's ray (same grid, same Bake, same Moller-Trumbore, same MirrorTie.Replaces as the sources) against the baked view mesh.</summary>
        HitInfo Cast(Row r)
        {
            var cam = player.ViewCamera;
            Transform ct = cam.transform;
            float halfV = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad), halfH = halfV * (16f / 9f);
            float x = (r.ix / (float)(r.gx - 1) * 2f - 1f) * halfH, y = (r.iy / (float)(r.gy - 1) * 2f - 1f) * halfV;
            Vector3 o = ct.position, d = (ct.forward + ct.right * x + ct.up * y).normalized;
            float bestT = float.MaxValue; bool bestBack = false; Tri best = default; bool have = false;
            var tris = BakeView();
            var all = new List<(float t, Tri tri, bool back)>();
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
            return new HitInfo { have = have, back = have && bestBack, tri = best, origin = o, hit = o + d * bestT, tris = tris, all = all };
        }

        void Capture(Row r, string dir, string source, StringBuilder index)
        {
            var h = Cast(r);
            bool sameTri = h.back && h.tri.rend == r.srcRenderer && h.tri.index == r.srcTri;
            string suffix = sameTri ? "" : (h.back ? ".othertri" : ".notrepro");
            string measures = ",,,,";   // inversionDot, entryToOpenEdge_m, restCentreMesh, restVertexIdx, class: only the entry is measured here; the rest is the EditMode classifier's (td-49 ruling A)
            float entry = float.NaN;
            if (h.back)
            {
                entry = float.MaxValue;
                foreach (var e in FindOpenEdges(h.tris)) entry = Mathf.Min(entry, SegmentSegmentDistance(h.origin, h.hit, e.a, e.b));
                measures = $",{entry:F4},,,";
            }
            int id = ++rowCounter;
            string name = $"{Slug(r.label)}_p{r.pitch:+0;-0;0}_c{r.ix}x{r.iy}" + suffix;
            var cam = player.ViewCamera;
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
                File.WriteAllBytes($"{dir}/{name}.png", tex.EncodeToPNG());   // the unmarked twin
                int cx = Mathf.RoundToInt(r.ix / (float)(r.gx - 1) * 959f), cy = Mathf.RoundToInt(r.iy / (float)(r.gy - 1) * 539f);   // ray grid cell centre in image pixels (iy 0 = bottom row)
                Outline(tex, cx, cy, 22, new Color(1f, 0f, 0f));
                tex.Apply();
                File.WriteAllBytes($"{dir}/{name}.marked.png", tex.EncodeToPNG());
                Record(dir, id, r, source, name + ".png", h, entry);
                index.AppendLine($"{source},{r.label},{r.pitch:F0},{r.ix},{r.iy},{name}.png,{(sameTri ? "Y" : "N")},{(sameTri ? "Y" : "N")},{(h.back ? "Y" : "N")},{(h.have ? h.tri.rend + "," + h.tri.index : ",")},{measures},{id},{r.line}");
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
    }
}
