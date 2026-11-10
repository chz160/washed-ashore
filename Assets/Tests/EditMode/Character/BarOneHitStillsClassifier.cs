using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using WashedAshore.Character.Editor;
using WashedAshore.Tests.Support;

namespace WashedAshore.Tests.EditMode.Character
{
    /// <summary>
    /// READ-ONLY EditMode classifier for the Bar 1 hit stills (td-49 "STILLS FAULT (baton 10): td rules A"; no asset, import or scene change). BarOneHitStills (PlayMode) wrote, per run folder (PC_RUN), stills-bake-inputs.txt:
    /// per row the hit triangle's baked world corners and index, the renderer's transform, the localToWorldMatrix of every bone in order and the live blend shape weights. This test loads the shipped Survivor_Body mesh
    /// (readable in EditMode), checks name / vertex count / triangle index count / bindposes = recorded bone count (else the row is UNCLASSIFIED: MESHMISMATCH), takes the corners from triangles[3*triIndex..],
    /// and computes (i) the inversion dot (posed normal against the rest normal carried by each corner's blended skin matrix, inverse-transpose, three carried normals normalised and added) and (iii) the rest triangle
    /// centre and its vertex indices. SELF-CHECK (A3): the three rest corners are re-skinned with the recorded shape weights, bone matrices, weights and bindposes and compared with the recorded baked corners; a residual above
    /// 1e-4 m makes the row UNCLASSIFIED (RESIDUAL) and the residual is printed. Class: FOLD if the dot is below 0, HOLE if it is not and (ii) the PlayMode entry distance is <= 0.01 m, else UNCLASSIFIED. Writes
    /// stills-index.classified.csv beside each PlayMode index (never overwriting it), fold-probe-rows.csv (id, clip, normalized time, clip seconds, rest centre in Unity mesh space) for ta-49 and unity-cap-centre.txt
    /// (the most-shared position of the mesh and its last vertex, six decimals: ta-49's F2 proof). Asserts only that the inputs exist.
    /// </summary>
    public class BarOneHitStillsClassifier
    {
        const float ResidualLimit = 1e-4f, HoleEntry = 0.01f;
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        class RowData
        {
            public int id; public string src, anim, rend, meshName, shapes, xform; public bool hit;
            public string[] boneNames; public List<(float t, string rend, int tri, bool back)> rayHits = new List<(float, string, int, bool)>(); public int vertexCount, indexCount, bindposes, boneCount, triIndex; public float entry;
            public Vector3[] corners = new Vector3[3]; public List<Matrix4x4> bones = new List<Matrix4x4>(); public List<bool> boneNull = new List<bool>();
        }

        static float P(string s) => float.Parse(s, Inv);
        static Vector3 V(string[] t, int o) => new Vector3(P(t[o]), P(t[o + 1]), P(t[o + 2]));

        static List<RowData> Parse(string path)
        {
            var rows = new List<RowData>();
            RowData cur = null;
            foreach (var line in File.ReadAllLines(path))
            {
                if (line.StartsWith("ROW ")) { cur = new RowData { id = int.Parse(line.Substring(4)) }; continue; }
                if (cur == null) continue;
                int sp = line.IndexOf(' ');
                string key = sp < 0 ? line : line.Substring(0, sp), rest = sp < 0 ? "" : line.Substring(sp + 1);
                switch (key)
                {
                    case "src": cur.src = rest; break;
                    case "anim": cur.anim = rest; break;      // the VIEW animator: view|clip|normalizedTime|length|looping
                    case "xform": cur.xform = rest; break;
                    case "rayhit": { var t = rest.Split('|'); cur.rayHits.Add((P(t[1]), t[2], int.Parse(t[3]), t[4] == "back")); break; }   // i|t|renderer|triIndex|front/back|paired (BarOneHitStills 541b4eb1, LookDownBfAttrib ff4ed2cb)
                    case "bonenames": cur.boneNames = rest.Split('|'); break;   // lookdown rows only (the stills blocks carry no bone names)
                    case "hit": cur.hit = rest == "1"; break;
                    case "rend": { var t = rest.Split('|'); cur.rend = t[0]; cur.meshName = t[1]; cur.vertexCount = int.Parse(t[2]); cur.indexCount = int.Parse(t[3]); cur.bindposes = int.Parse(t[4]); cur.boneCount = int.Parse(t[5]); break; }
                    case "tri": { var t = rest.Split('|'); cur.triIndex = int.Parse(t[0]); cur.entry = P(t[1]); break; }
                    case "corners": { var t = rest.Split(' '); for (int i = 0; i < 3; i++) cur.corners[i] = V(t, i * 3); break; }
                    case "shapes": { var t = rest.Split('|'); cur.shapes = t.Length > 1 ? t[1] : ""; break; }
                    case "m":
                        if (rest == "null") { cur.bones.Add(Matrix4x4.identity); cur.boneNull.Add(true); }
                        else { var t = rest.Split(' '); var m = new Matrix4x4(); for (int k = 0; k < 16; k++) m[k] = P(t[k]); cur.bones.Add(m); cur.boneNull.Add(false); }
                        break;
                    case "END": rows.Add(cur); cur = null; break;
                }
            }
            return rows;
        }

        static Matrix4x4 AddScaled(Matrix4x4 a, Matrix4x4 b, float w) { for (int i = 0; i < 16; i++) a[i] += b[i] * w; return a; }

        class MeshCache
        {
            public Mesh mesh; public Vector3[] rest; public int[] tri; public string[] assetBoneNames; public BoneWeight1[] weights; public byte[] perVertex; public int[] first; public Matrix4x4[] bind;
            public Dictionary<string, (Vector3[] delta, float frameWeight)> shapes = new Dictionary<string, (Vector3[], float)>();
        }

        static MeshCache Load(string meshName)
        {
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(SurvivorAssetBuilder.BodyPath))
            {
                if (!(o is Mesh m) || m.name != meshName) continue;
                var c = new MeshCache { mesh = m, rest = m.vertices, tri = m.triangles, weights = m.GetAllBoneWeights().ToArray(), perVertex = m.GetBonesPerVertex().ToArray(), bind = m.bindposes };
                c.first = new int[c.rest.Length];
                int run = 0;
                for (int i = 0; i < c.rest.Length; i++) { c.first[i] = run; run += c.perVertex[i]; }
                try   // bone names of the imported model, for rows whose block carries none (the stills blocks)
                {
                    var go = AssetDatabase.LoadAssetAtPath<GameObject>(SurvivorAssetBuilder.BodyPath);
                    if (go != null)
                        foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                            if (smr.sharedMesh != null && smr.sharedMesh.name == meshName) { c.assetBoneNames = smr.bones.Select(b => b != null ? b.name : "?").ToArray(); break; }
                }
                catch (System.Exception) { c.assetBoneNames = null; }
                for (int s = 0; s < m.blendShapeCount; s++)
                {
                    int last = m.GetBlendShapeFrameCount(s) - 1;
                    var dv = new Vector3[m.vertexCount];
                    m.GetBlendShapeFrameVertices(s, last, dv, null, null);
                    c.shapes[m.GetBlendShapeName(s)] = (dv, m.GetBlendShapeFrameWeight(s, last));
                }
                return c;
            }
            return null;
        }

        /// <summary>Skin matrix of a vertex: the sum of weight * bone.localToWorld * bindpose over its bone weights, from the RECORDED bone matrices.</summary>
        static Matrix4x4 Skin(MeshCache c, RowData r, int vertex)
        {
            var M = new Matrix4x4();
            for (int k = 0; k < c.perVertex[vertex]; k++)
            {
                var w = c.weights[c.first[vertex] + k];
                M = AddScaled(M, r.bones[w.boneIndex] * c.bind[w.boneIndex], w.weight);
            }
            return M;
        }

        // td-49 "11a CLASSIFIER dot = 0 FAULT" F-a/F-b: the mesh is in 1/101.56 m units, so rest normals are about 1e-8 and Unity's .normalized (zero below 1e-5) returned zero for every row. Every vector is
        // normalised here by explicit division in double; a zero-length vector is DEGENERATE and never a silent 0.
        static bool Unit(double x, double y, double z, out Vector3 u)
        {
            double m = System.Math.Sqrt(x * x + y * y + z * z);
            if (!(m > 1e-30)) { u = Vector3.zero; return false; }
            u = new Vector3((float)(x / m), (float)(y / m), (float)(z / m));
            return true;
        }

        static bool Unit(Vector3 v, out Vector3 u) => Unit(v.x, v.y, v.z, out u);

        /// <summary>Unit direction of M^-T n for the upper 3x3 of a skin matrix, from the cofactor matrix (C = det * M^-T), so no matrix inverse (which can fail on small determinants) is used.</summary>
        static bool Carried(Matrix4x4 m, Vector3 n, out Vector3 u)
        {
            double m00 = m.m00, m01 = m.m01, m02 = m.m02, m10 = m.m10, m11 = m.m11, m12 = m.m12, m20 = m.m20, m21 = m.m21, m22 = m.m22;
            double c00 = m11 * m22 - m12 * m21, c01 = -(m10 * m22 - m12 * m20), c02 = m10 * m21 - m11 * m20;
            double c10 = -(m01 * m22 - m02 * m21), c11 = m00 * m22 - m02 * m20, c12 = -(m00 * m21 - m01 * m20);
            double c20 = m01 * m12 - m02 * m11, c21 = -(m00 * m12 - m02 * m10), c22 = m00 * m11 - m01 * m10;
            double det = m00 * c00 + m01 * c01 + m02 * c02;
            if (!(System.Math.Abs(det) > 1e-30)) { u = Vector3.zero; return false; }
            double sgn = det < 0 ? -1.0 : 1.0;
            return Unit(sgn * (c00 * n.x + c01 * n.y + c02 * n.z), sgn * (c10 * n.x + c11 * n.y + c12 * n.z), sgn * (c20 * n.x + c21 * n.y + c22 * n.z), out u);
        }

        /// <summary>Posed normal against the rest normal carried by the three corners' skin matrices (three unit carried normals added, then normalised). +1 = same side, -1 = inverted winding.</summary>
        internal static float InversionDot(Vector3[] rest, Matrix4x4[] skin, Vector3[] posed, out bool degenerate)
        {
            degenerate = true;
            if (!Unit(Vector3.Cross(rest[1] - rest[0], rest[2] - rest[0]), out var restN)) return float.NaN;
            double sx = 0, sy = 0, sz = 0;
            for (int j = 0; j < 3; j++)
            {
                if (!Carried(skin[j], restN, out var cn)) return float.NaN;
                sx += cn.x; sy += cn.y; sz += cn.z;
            }
            if (!Unit(sx, sy, sz, out var sum)) return float.NaN;
            if (!Unit(Vector3.Cross(posed[1] - posed[0], posed[2] - posed[0]), out var posedN)) return float.NaN;
            degenerate = false;
            return Vector3.Dot(sum, posedN);
        }

        // td-49 F-c: the measure at THIS mesh's scale (rest edges 0.01 mesh units, a 100x skin matrix), not on posed Unity data.
        static readonly Vector3[] TinyRest = { new Vector3(0f, 0f, 0f), new Vector3(0.01f, 0f, 0f), new Vector3(0f, 0.01f, 0f) };

        static float TinyDot(Matrix4x4 skin, bool inverted, out bool degenerate)
        {
            var posed = new Vector3[3];
            for (int j = 0; j < 3; j++) posed[j] = skin.MultiplyPoint3x4(TinyRest[j]);
            if (inverted) (posed[1], posed[2]) = (posed[2], posed[1]);
            return InversionDot(TinyRest, new[] { skin, skin, skin }, posed, out degenerate);
        }

        [Test]
        public void InversionDot_AtMeshScale_IsPlusOneForAnUntouchedWinding_AndMinusOneForAnInvertedOne()
        {
            foreach (var skin in new[] { Matrix4x4.Scale(Vector3.one * 100f), Matrix4x4.TRS(new Vector3(0.3f, 1f, -2f), Quaternion.Euler(30f, 70f, -20f), Vector3.one * 101.56f) })
            {
                Assert.AreEqual(1f, TinyDot(skin, false, out bool d1), 1e-4f); Assert.IsFalse(d1);
                Assert.AreEqual(-1f, TinyDot(skin, true, out bool d2), 1e-4f); Assert.IsFalse(d2);
            }
        }

        [Test]
        public void InversionDot_AMirroringSkinMatrixFlipsTheWinding_AndAZeroTriangleIsDegenerate()
        {
            // a skin matrix that mirrors (negative determinant) flips the posed winding while the carried normal (M^-T n, sign kept through det) stays on the rest side: -1 for the plain order, +1 for the swapped one
            var mirror = Matrix4x4.Scale(new Vector3(-100f, 100f, 100f));
            Assert.AreEqual(-1f, TinyDot(mirror, false, out bool d1), 1e-4f); Assert.IsFalse(d1);
            Assert.AreEqual(1f, TinyDot(mirror, true, out bool d4), 1e-4f); Assert.IsFalse(d4);
            var flat = new[] { Vector3.zero, Vector3.zero, Vector3.zero };
            Assert.IsTrue(float.IsNaN(InversionDot(flat, new[] { Matrix4x4.identity, Matrix4x4.identity, Matrix4x4.identity }, TinyRest, out bool d3)));
            Assert.IsTrue(d3, "a zero-length rest normal must be DEGENERATE, never a silent 0");
        }

        /// <summary>td-49 B3: the dominant bone of the hit triangle, the majority of the three corners' max-weight bones by NAME ("mixed:a/b/c" when all three differ); "unnamed" when the block has no bone names.</summary>
        static string DominantBone(RowData r, MeshCache c)
        {
            if (!r.hit) return "unnamed";
            if (Mismatch(r, c)) return "unclassified";   // td-49: never index a mesh that is not the recorded one
            return DominantOfTri(c, r.triIndex, r.boneNames ?? c.assetBoneNames);
        }

        static bool Mismatch(RowData r, MeshCache c) =>
            c == null || c.mesh.vertexCount != r.vertexCount || (int)c.mesh.GetIndexCount(0) != r.indexCount || c.bind.Length != r.bindposes;

        /// <summary>Dominant bone of a triangle of the cached mesh: majority of the three corners' max-weight bones by name; "mixed:a/b/c" when all differ; "unnamed" without names; "unclassified" when the index is out of range.</summary>
        static string DominantOfTri(MeshCache c, int triIndex, string[] boneNames)
        {
            if (boneNames == null) return "unnamed";
            if (triIndex < 0 || 3 * triIndex + 2 >= c.tri.Length) return "unclassified";
            var tri = new[] { c.tri[3 * triIndex], c.tri[3 * triIndex + 1], c.tri[3 * triIndex + 2] };
            var names = new string[3];
            for (int j = 0; j < 3; j++)
            {
                if (tri[j] < 0 || tri[j] >= c.perVertex.Length) return "unclassified";
                int bestBone = -1; float bestW = -1f;
                for (int k = 0; k < c.perVertex[tri[j]]; k++)
                {
                    var w = c.weights[c.first[tri[j]] + k];
                    if (w.weight > bestW) { bestW = w.weight; bestBone = w.boneIndex; }
                }
                names[j] = bestBone >= 0 && bestBone < boneNames.Length ? boneNames[bestBone] : "?";
            }
            if (names[0] == names[1] || names[0] == names[2]) return names[0];
            if (names[1] == names[2]) return names[1];
            return "mixed:" + names[0] + "/" + names[1] + "/" + names[2];
        }

        /// <summary>
        /// td-49 R4 (print only): the gap along the ray from the first hit (a back face) to the next FRONT-facing hit, that hit's triangle and its body region (dominant bone). Blank when the row has no rayhit lines
        /// (the 11a-era blocks) or when the first hit is not a back face; "none" in all three columns when the first hit is a back face and no front hit follows (R3(a)). A hit on another renderer or on a mismatched mesh reads "unclassified". Nothing else uses it: no class, no assert.
        /// </summary>
        static int gapPrinted, gapNone, gapBlankNoRayHits, gapBlankFirstNotBack;   // end-of-file summary of the front-gap column status, so a blank cannot hide a row (qa-49, td-49)

        static string GapSummary() => $"# frontGap status: gap printed {gapPrinted}, none {gapNone}, blank (no rayhit lines) {gapBlankNoRayHits}, blank (first hit not a back face) {gapBlankFirstNotBack}, total {gapPrinted + gapNone + gapBlankNoRayHits + gapBlankFirstNotBack}";

        static string FrontGapColumns(RowData r, MeshCache c)
        {
            // rayHits[0] stands in for the MirrorTie winner: that is valid while the rays have 0 mirror pairs (the "mirror-paired" column of every rayhit line is "-" in the runs of record).
            if (r.rayHits.Count == 0) { gapBlankNoRayHits++; return ",,,"; }
            if (!r.hit || !r.rayHits[0].back) { gapBlankFirstNotBack++; return ",,,"; }
            for (int i = 1; i < r.rayHits.Count; i++)
            {
                if (r.rayHits[i].back) continue;
                var h = r.rayHits[i];
                string region = h.rend != r.rend || Mismatch(r, c) ? "unclassified" : DominantOfTri(c, h.tri, r.boneNames ?? c.assetBoneNames);
                gapPrinted++;
                return "," + (h.t - r.rayHits[0].t).ToString("R", Inv) + "," + h.tri + "," + region;
            }
            gapNone++;
            return ",none,none,none";   // td-49 R4: a back first hit with NO front hit behind it is R3(a), a candidate real hole: never blank
        }

        /// <summary>The row's measures: returns the class and fills the dot, the residual and the rest columns.</summary>
        static string Classify(RowData r, MeshCache c, out float inversion, out float residual, out string centre, out string indices)
        {
            inversion = float.NaN; residual = float.NaN; centre = ""; indices = "";
            if (!r.hit) return "NOHIT";
            if (r.rend != "Survivor_Body") return "UNCLASSIFIED:NOTBODY(" + r.rend + ")";
            if (c == null) return "UNCLASSIFIED:MESHMISMATCH(no mesh named " + r.meshName + ")";
            if (c.mesh.vertexCount != r.vertexCount || (int)c.mesh.GetIndexCount(0) != r.indexCount || c.bind.Length != r.bindposes || r.bones.Count != r.bindposes)
                return $"UNCLASSIFIED:MESHMISMATCH(shipped {c.mesh.vertexCount}/{c.mesh.GetIndexCount(0)}/{c.bind.Length} vs recorded {r.vertexCount}/{r.indexCount}/{r.bindposes} bones {r.bones.Count})";
            int[] ci = { c.tri[3 * r.triIndex], c.tri[3 * r.triIndex + 1], c.tri[3 * r.triIndex + 2] };
            // the shape weights recorded at the bake
            var live = new List<(string name, float w)>();
            foreach (var kv in (r.shapes ?? "").Split(new[] { ';' }, System.StringSplitOptions.RemoveEmptyEntries)) { var t = kv.Split('='); live.Add((t[0], P(t[1]))); }
            foreach (int v in ci)   // O3: a corner skinned by a null bone cannot be classified
                for (int k = 0; k < c.perVertex[v]; k++)
                    if (r.boneNull[c.weights[c.first[v] + k].boneIndex]) return "UNCLASSIFIED:NULLBONE";
            residual = 0f;
            for (int j = 0; j < 3; j++)
            {
                Vector3 v = c.rest[ci[j]];
                foreach (var (name, w) in live) if (c.shapes.TryGetValue(name, out var sh)) v += sh.delta[ci[j]] * (w / sh.frameWeight);
                residual = Mathf.Max(residual, (Skin(c, r, ci[j]).MultiplyPoint3x4(v) - r.corners[j]).magnitude);
            }
            centre = $"{(c.rest[ci[0]] + c.rest[ci[1]] + c.rest[ci[2]]).x / 3f:F6};{(c.rest[ci[0]] + c.rest[ci[1]] + c.rest[ci[2]]).y / 3f:F6};{(c.rest[ci[0]] + c.rest[ci[1]] + c.rest[ci[2]]).z / 3f:F6}";
            indices = $"{ci[0]};{ci[1]};{ci[2]}";
            var restCorners = new[] { c.rest[ci[0]], c.rest[ci[1]], c.rest[ci[2]] };
            var skins = new[] { Skin(c, r, ci[0]), Skin(c, r, ci[1]), Skin(c, r, ci[2]) };
            inversion = InversionDot(restCorners, skins, r.corners, out bool degenerate);
            if (residual > ResidualLimit) return $"UNCLASSIFIED:RESIDUAL({residual:E2} m)";
            if (degenerate) return "UNCLASSIFIED:DEGENERATE";
            return inversion < 0f ? "FOLD" : (r.entry <= HoleEntry ? "HOLE" : "UNCLASSIFIED");
        }

        [Test, Explicit("read-only classifier for the Bar 1 hit stills: needs PC_RUN of the stills run")]
        public void ClassifyTheStillsRows()
        {
            string run = System.Environment.GetEnvironmentVariable("PC_RUN");
            Assert.IsFalse(string.IsNullOrEmpty(run), "set PC_RUN to the run folder of the stills run");
            string root = $"TestResults/player-character/runs/{run}";
            var caches = new Dictionary<string, MeshCache>();
            var foldRows = new StringBuilder("# " + MirrorTie.StateLine + "; PC_RUN=" + run + System.Environment.NewLine + "source,id,viewClip,normalizedTime,clipSeconds,restCentreX,restCentreY,restCentreZ,restVertexIdx,class" + System.Environment.NewLine);
            int total = 0;
            foreach (string sub in new[] { "range", "sweep", "lookdown" })   // lookdown: rows written by the gate with LOOKDOWN_BF_ATTRIB=1 (td-49 G1)
            {
                string dir = $"{root}/{sub}/stills", bakePath = $"{dir}/stills-bake-inputs.txt", indexPath = $"{dir}/stills-index.csv";
                if (!File.Exists(bakePath) || !File.Exists(indexPath)) continue;
                var rows = Parse(bakePath).ToDictionary(r => r.id);
                var lines = File.ReadAllLines(indexPath);
                int headerAt = System.Array.FindIndex(lines, l => !l.StartsWith("#"));
                var h = lines[headerAt].Split(',').ToList();
                int cInv = h.IndexOf("inversionDot"), cCentre = h.IndexOf("restCentreMesh"), cIdx = h.IndexOf("restVertexIdx"), cClass = h.IndexOf("class"), cId = h.IndexOf("rowId");
                Assert.IsTrue(cInv >= 0 && cCentre >= 0 && cIdx >= 0 && cClass >= 0 && cId >= 0, "the PlayMode index lacks the expected columns");
                gapPrinted = gapNone = gapBlankNoRayHits = gapBlankFirstNotBack = 0;
                var outText = new StringBuilder();
                for (int i = 0; i < headerAt; i++) outText.AppendLine(lines[i]);
                outText.AppendLine("# renderer lossyScale seen in the rows: " + string.Join(" / ", rows.Values.Where(x => x.hit && x.xform != null).Select(x => x.xform.Split('|').Last().Trim()).Distinct()));
                outText.AppendLine("# classified by BarOneHitStillsClassifier; " + MirrorTie.StateLine + "; residual limit " + ResidualLimit.ToString("E0", Inv) + " m; hole entry " + HoleEntry.ToString(Inv) + " m");
                outText.AppendLine(lines[headerAt] + ",residual_m,dominantBone,frontGap_m,frontTri,frontRegion");
                for (int i = headerAt + 1; i < lines.Length; i++)
                {
                    if (lines[i].Length == 0) continue;
                    var cols = lines[i].Split(',');
                    int id = int.Parse(cols[cId]);
                    var r = rows[id];
                    MeshCache cache = null;
                    if (r.hit && !caches.TryGetValue(r.meshName, out cache)) caches[r.meshName] = cache = Load(r.meshName);
                    string cls = Classify(r, cache, out float inv, out float resid, out string centre, out string indices);
                    cols[cInv] = float.IsNaN(inv) ? "" : inv.ToString("F3", Inv);
                    cols[cCentre] = centre; cols[cIdx] = indices; cols[cClass] = cls;
                    outText.AppendLine(string.Join(",", cols) + "," + (float.IsNaN(resid) ? "" : resid.ToString("E2", Inv)) + "," + DominantBone(r, cache) + FrontGapColumns(r, cache));
                    if (r.hit && centre.Length > 0)
                    {
                        var a = r.anim.Split('|'); float nt = P(a[2]), clen = P(a[3]); bool looping = a[4] == "1";   // [0] label, [1] clip, [2] normalized time, [3] clip length, [4] looping
                        float clipSeconds = looping ? (nt - Mathf.Floor(nt)) * clen : Mathf.Min(nt, 1f) * clen;   // O2
                        var cc = centre.Split(';');
                        foldRows.AppendLine($"{sub},{id},{a[1]},{nt:F4},{clipSeconds:F4},{cc[0]},{cc[1]},{cc[2]},{indices},{cls}");
                    }
                    total++;
                }
                outText.AppendLine(GapSummary());
                Debug.Log($"{sub}: {GapSummary()}");
                File.WriteAllText($"{dir}/stills-index.classified.csv", outText.ToString());
            }
            Assert.Greater(total, 0, $"no stills rows found under {root} (run BarOneHitStills first, same PC_RUN)");
            File.WriteAllText($"{root}/fold-probe-rows.csv", foldRows.ToString());
            // ta-49 F2: the neck-cap centre in Unity mesh space, found as the MOST-SHARED position (Unity splits vertices), and the last vertex
            var body = Load("Survivor_Body");
            Assert.IsNotNull(body, "no mesh named Survivor_Body in " + SurvivorAssetBuilder.BodyPath);
            var valence = new Dictionary<Vector3Int, (int n, Vector3 p)>();
            Vector3Int best = default; int most = 0;
            foreach (int t in body.tri)
            {
                var p = body.rest[t];
                var key = new Vector3Int(Mathf.RoundToInt(p.x * 1e6f), Mathf.RoundToInt(p.y * 1e6f), Mathf.RoundToInt(p.z * 1e6f));
                int n = valence.TryGetValue(key, out var e) ? e.n + 1 : 1;
                valence[key] = (n, p);
                if (n > most) { most = n; best = key; }
            }
            var last = body.rest[body.rest.Length - 1];
            string cap = $"{MirrorTie.StateLine}{System.Environment.NewLine}Survivor_Body vertices {body.rest.Length}, triangles {body.tri.Length / 3}{System.Environment.NewLine}most-shared position (corners {most}): {valence[best].p.x:F6},{valence[best].p.y:F6},{valence[best].p.z:F6}{System.Environment.NewLine}last vertex (index {body.rest.Length - 1}): {last.x:F6},{last.y:F6},{last.z:F6}{System.Environment.NewLine}"
                + "most-shared position, round-trip (R): " + valence[best].p.x.ToString("R", Inv) + "," + valence[best].p.y.ToString("R", Inv) + "," + valence[best].p.z.ToString("R", Inv) + System.Environment.NewLine
                + "last vertex, round-trip (R): " + last.x.ToString("R", Inv) + "," + last.y.ToString("R", Inv) + "," + last.z.ToString("R", Inv) + System.Environment.NewLine;   // td-49: the 6-decimal limit is disclosed; R prints the full float
            File.WriteAllText($"{root}/unity-cap-centre.txt", cap);
            Debug.Log(cap + $"classified rows: {total}");
        }
    }
}
