using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using WashedAshore.Character.Stance;

namespace WashedAshore.Tests.PlayMode.Character
{
    /// <summary>
    /// X1 P0 hull half of the S2 print (HairlineGapSizing, partial class; td-49 B35 B/C/D, B35a, B35b; director look conditions), TEST-ONLY: no runtime script, prefab, FBX or material asset is touched; everything is created at run time and destroyed at the end.
    /// GENERATOR: per inset one skinned "backing hull" for the first-person view body: a radial-profile tube around the bind-pose bone axis (6 rings x 12 sides, both ends capped) for UpperLeg, LowerLeg, Foot, UpperArm and LowerArm, left and right, NEVER a hand bone.
    /// Each cell's radius = the smallest axis distance of the rest vertices whose top weight (boneIndex0) is that bone, in that ring's slab and angular sector, minus the inset; empty cells are interpolated from the neighbouring sectors (then rings).
    /// Weights: 100% to the segment bone; the end rings 50/50 with the neighbouring bone (never a hand bone). ONE SkinnedMeshRenderer per inset (body bones and bind poses), ONE material with vertex colour (B35a: forearmSkin sRGB (151, 119, 108) on the LowerArm tubes,
    /// clothInterior (65, 54, 47) on the leg and UpperArm tubes; ta-49 pod49\tools_x1\x1_colours.py, out\x1_colours.txt), shadows Off, on the Player layer, drawn only in this pass. HULL_INSET: mm, positive = inward of the innermost profile, negative = outward between the layers.
    /// LEAK OF RECORD (td B35b, qa F-2): in the SAME frame, with no yield, a hull-OFF render and a hull-ON render of BOTH the body-only and the lit view; LEAK = every pixel that differs in either render outside the K-a components' 1x pixels of the hull-OFF render; it must be 0.
    /// SSAO EVIDENCE (review of eng_50; td ruling pending, the record LEAK is NOT changed): PC_Renderer has SSAO on (Source DepthNormals, AfterOpaque off, so AO is applied inside the lit shaders of both renders), and the hull material's DepthOnly/DepthNormalsOnly passes feed it,
    /// so a hull that fills a gap can shift the AO of nearby body pixels (a halo) outside the K-a pixels. Per inset the test therefore also prints (a) the record leak pixels whose largest per-channel change is <= 3 LSB, and (b) LEAK-noAO: the same rule against a third
    /// hull-ON twin of the same frame rendered with the hull's DepthOnly and DepthNormalsOnly passes disabled (the hull is kept out of the SSAO input; the colour pass is unchanged). Evidence only: the selection and the verdict use the record LEAK.
    /// LEAK-noAO control: in each frame the no-AO twin's body-only mask must equal the record hull-ON body-only mask (the twin still draws the hull); in a frame where they differ LEAK-noAO is n/a, and an inset with any n/a frame has no LEAK-noAO total.
    /// Controls (fail-closed, evidence for td/qa): the fresh hull-OFF twin must equal the frame's baseline renders (drift 0); a hull-only render must cover the CPU-baked hull vertices (the hull is really skinned and drawn); both known spots measured (K1) and the K-a target #2499/#16175 found.
    /// SELECTION (td B35 D, literal): the smallest OUTWARD inset (the first of the sweep sorted from most inward to most outward) with LEAK 0, K-a 100% closed, both known spots and the K-a target #2499/#16175 closed, and valid controls; if every inset is valid and none passes, X1 FAILS (X2).
    /// No record rules on SSAO (td-review-baton2b.md ends at B35c), so the verdict never depends on LEAK-noAO: after an X1 FAILS line the summary prints a separate SSAO NOTE (evidence, td to rule) naming any inset that passes on LEAK-noAO.
    /// REST READ (eng_51, after the VOID baton 21): Survivor_Body is imported with Read/Write OFF, and in PlayMode Mesh.vertices / Mesh.triangles answer an error and an empty array (Mesh.canAccess is false), and
    /// Mesh.AcquireReadOnlyMeshData throws for the same reason (it builds MeshDataArray(mesh, checkReadWrite: true), which tests mesh.canAccess). boneWeights and bindposes are not gated (the S2 regions and GetBonesPerVertex of PinholeD3 T4 work in PlayMode),
    /// and SkinnedMeshRenderer.BakeMesh writes into a NEW, readable Mesh, which is how S2's attribution has always read the body. So the rest mesh is read with BakeMesh, test-only and without touching the asset or its import settings:
    /// a temporary SkinnedMeshRenderer on the same shared mesh, at the world origin, whose bones are placed at the inverse bind poses, so every skin matrix is the identity (to float precision) and the bake IS the rest mesh;
    /// the residual of each skin matrix is removed per vertex in double (the bake solved against the blend of the vertex's 4 skin matrices). Its triangles are the bake's, the order S2 attributes in (FNV-1a 7e3eaf8a, baton 17b D3).
    /// Fail-closed checks: vertex count, weights and bind poses present, index order hash, the same triangles as the live renderer's bake, no singular or non-finite vertex, and the rest re-skinned with the LIVE bones reproducing the live bake (knee shapes at 0) within 1 mm.
    /// FAIL-CLOSED VERDICT (eng_51): NO VERDICT (VOID), never X1 FAILS, when the rest read, the K-f map (any K-f row UNMAPPED or AMBIGUOUS), a hull build (a skipped segment, not 10 tubes of 144 triangles) or a control fails at ANY inset;
    /// the hull-only skin control fails in a frame with no hull vertex on screen while a hull bone is in view, and at an inset with no hull vertex on screen in every frame. The run stops before the first analysed frame (fail fast) when the rest read, the K-f map or a hull build fails.
    /// </summary>
    public partial class HairlineGapSizing
    {
        static readonly float[] DefaultInsets = { 3f, 0f, -3f, -6f };
        static readonly Color32 ForearmSkin = new Color32(151, 119, 108, 255), ClothInterior = new Color32(65, 54, 47, 255);
        const int Rings = 6, Sides = 12;
        const int HullSegments = 10, TrisPerTube = (Rings - 1) * Sides * 2 + 2 * Sides, VertsPerTube = Rings * Sides + 2;   // 10 tubes of 144 triangles and 74 vertices: 1,440 triangles, 740 vertices
        const float SkinControlMin = 0.9f;
        const int BoneInViewMarginPx = 10;
        const uint IndexOrderFnv = 0x7e3eaf8au;   // baton 17b D3 / SurvivorRestPrint T1: FNV-1a of the Survivor_Body triangle indices
        const double LiveCheckMm = 1.0;
        const string HullShaderName = "Universal Render Pipeline/Particles/Unlit";   // multiplies the vertex colour by default (URP ParticlesUnlitInput SampleAlbedo -> MixParticleColor); the production material is ta-49's (P1)

        float[] hullInsets = new float[0];   // mm
        bool hullToggleByEnabled;   // HULL_TOGGLE=enabled: toggle renderer.enabled; default: renderer.forceRenderingOff, so the renderer stays enabled and keeps its engine skinning between the twin renders
        class HullInfo { public SkinnedMeshRenderer owner, r; public float mm; public Mesh mesh; public int tris, verts, builtFrame, clamped, segments; public bool handBone; public string note; public Transform[] segBones = new Transform[0]; public readonly List<string> fail = new List<string>(); }
        class HullAcc
        {
            public int frames, skipped, kaComps, kaClosed, kaPixels, kaPixelsClosed, leakUnion, leakBody, leakLit, leakWasBody, leakWasInterior, leakWasBackground, leakHalo, leakFrames, maxLeak, leakFaint, leakNoAo, leakNoAoFrames, maxLeakNoAo, leakNoAoNa, driftFrames, kaIncompleteFrames, skinFail, skinNa, skinNoneInView;
            public int spotTotal, spotClosed; public readonly Dictionary<string, (int total, int closed)> target = new Dictionary<string, (int, int)>();
            public string note = ""; public bool handBone; public readonly List<string> leakList = new List<string>(); public readonly List<string> buildFail = new List<string>();
        }

        /// <summary>The rest mesh of Survivor_Body in mesh space, read without Read/Write (see the class summary); <see cref="fail"/> empty = usable.</summary>
        sealed class RestMesh
        {
            public Mesh src; public Vector3[] v = new Vector3[0]; public int[] tri = new int[0]; public BoneWeight[] bw = new BoneWeight[0]; public Matrix4x4[] bind = new Matrix4x4[0];
            public readonly List<string> fail = new List<string>(); public string note = "";
        }
        RestMesh rest;
        List<string> preflightVoid;   // null = the fail-fast preflight passed (or has not run)

        static uint RestFnv(int[] tri) { uint h = 2166136261u; unchecked { foreach (int x in tri) { uint u = (uint)x; for (int b = 0; b < 4; b++) { h ^= (u >> (8 * b)) & 0xFFu; h *= 16777619u; } } } return h; }
        static double[] Aff(Matrix4x4 m) => new double[] { m.m00, m.m01, m.m02, m.m03, m.m10, m.m11, m.m12, m.m13, m.m20, m.m21, m.m22, m.m23 };
        /// <summary>a * b of two affine 3x4 matrices (implied last row 0 0 0 1), in double.</summary>
        static double[] AffMul(double[] a, double[] b)
        {
            var r = new double[12];
            for (int i = 0; i < 3; i++) for (int j = 0; j < 4; j++) r[4 * i + j] = a[4 * i] * b[j] + a[4 * i + 1] * b[4 + j] + a[4 * i + 2] * b[8 + j] + (j == 3 ? a[4 * i + 3] : 0.0);
            return r;
        }
        static bool Affine(Matrix4x4 m) => System.Math.Abs(m.m30) < 1e-6f && System.Math.Abs(m.m31) < 1e-6f && System.Math.Abs(m.m32) < 1e-6f && System.Math.Abs(m.m33 - 1f) < 1e-6f;
        static bool Finite(Vector3 p) => !float.IsNaN(p.x) && !float.IsNaN(p.y) && !float.IsNaN(p.z) && !float.IsInfinity(p.x) && !float.IsInfinity(p.y) && !float.IsInfinity(p.z);
        /// <summary>The blend of one vertex's (up to) 4 weighted matrices; false when a weighted bone index is out of range.</summary>
        static bool Blend(double[][] m, BoneWeight w, double[] a)
        {
            System.Array.Clear(a, 0, 12);
            var bi = new[] { w.boneIndex0, w.boneIndex1, w.boneIndex2, w.boneIndex3 }; var wt = new[] { w.weight0, w.weight1, w.weight2, w.weight3 };
            for (int k = 0; k < 4; k++)
            {
                if (wt[k] == 0f) continue;
                if (bi[k] < 0 || bi[k] >= m.Length || m[bi[k]] == null) return false;
                for (int e = 0; e < 12; e++) a[e] += wt[k] * m[bi[k]][e];
            }
            return true;
        }

        /// <summary>
        /// Reads the rest mesh of <paramref name="smr"/>'s shared mesh once (cached per mesh): BakeMesh of a test-only SkinnedMeshRenderer whose bones sit at the inverse bind poses (skin matrices = identity), the residual solved per vertex in double,
        /// then the fail-closed checks listed in the class summary. Nothing is left in the scene: the temporary objects are destroyed in this call, the live renderer's blend-shape weights are restored in this call.
        /// </summary>
        RestMesh RestOf(SkinnedMeshRenderer smr)
        {
            var src = smr.sharedMesh;
            if (rest != null && rest.src == src) return rest;
            var r = new RestMesh { src = src };
            rest = r;
            var bones = smr.bones; int nb = bones.Length, nv = src.vertexCount, nIdx = 0;
            for (int s = 0; s < src.subMeshCount; s++) nIdx += (int)src.GetIndexCount(s);
            r.bind = src.bindposes; r.bw = src.boneWeights;
            if (nv == 0) r.fail.Add($"the shared mesh '{src.name}' reports 0 vertices");
            if (r.bw.Length != nv) r.fail.Add($"boneWeights returned {r.bw.Length} entries for {nv} vertices");
            if (nb == 0 || r.bind.Length != nb) r.fail.Add($"bindposes returned {r.bind.Length} matrices for {nb} bones");
            if (r.bind.Any(m => !Affine(m))) r.fail.Add("a bind pose is not affine");
            if (r.fail.Count > 0) { r.note = "REST READ FAILED before the bake: " + string.Join("; ", r.fail); comps.AppendLine("== " + r.note); Debug.LogError("X1 P0 " + r.note); return r; }
            GameObject root = null; var bake = new Mesh(); var live = new Mesh();
            var o = new StringBuilder();
            try
            {
                // 1. the rest bake: bones at the inverse bind poses under a root at the origin, so bone.localToWorld * bind = identity
                root = new GameObject("S2RestBake (test only, destroyed in this call)");
                root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity); root.transform.localScale = Vector3.one;
                var bt = new Transform[nb]; int nonTrs = 0;
                for (int k = 0; k < nb; k++)
                {
                    var t = new GameObject("b" + k).transform; t.SetParent(root.transform, false);
                    var inv = r.bind[k].inverse;
                    if (inv.ValidTRS()) { t.localPosition = inv.GetColumn(3); t.localRotation = inv.rotation; t.localScale = inv.lossyScale; } else nonTrs++;   // a non-TRS bind pose keeps the identity; the solve below still removes it exactly
                    bt[k] = t;
                }
                var e = new double[nb][]; double eDev = 0;
                for (int k = 0; k < nb; k++)
                {
                    e[k] = AffMul(Aff(bt[k].localToWorldMatrix), Aff(r.bind[k]));
                    for (int i = 0; i < 3; i++) for (int j = 0; j < 4; j++) eDev = System.Math.Max(eDev, System.Math.Abs(e[k][4 * i + j] - (i == j ? 1.0 : 0.0)));
                }
                var tmp = root.AddComponent<SkinnedMeshRenderer>();
                tmp.forceRenderingOff = true; tmp.shadowCastingMode = ShadowCastingMode.Off; tmp.receiveShadows = false; tmp.quality = smr.quality;
                tmp.bones = bt; tmp.sharedMesh = src;   // a new renderer: every blend-shape weight is 0, so the bake has no knee shape
                tmp.BakeMesh(bake, false);   // the root is the identity, so the bake's local space is the bones' space
                var bv = bake.vertices; r.tri = bake.triangles;
                if (bv.Length != nv) r.fail.Add($"the rest bake returned {bv.Length} vertices, the shared mesh has {nv}");
                if (r.tri.Length != nIdx || r.tri.Length == 0) r.fail.Add($"the rest bake returned {r.tri.Length / 3} triangles, the shared mesh has {nIdx / 3}");
                uint fnv = RestFnv(r.tri);
                if (fnv != IndexOrderFnv) r.fail.Add($"index order FNV-1a {fnv:x8}, expected {IndexOrderFnv:x8} (baton 17b D3)");
                // 2. the residual: rest = (sum of w * E)^-1 * bake, in double
                var rv = new Vector3[bv.Length]; var a = new double[12]; int singular = 0, badIndex = 0, nonFinite = 0; double maxMove = 0, maxWsum = 0;
                for (int vi = 0; vi < bv.Length && vi < r.bw.Length; vi++)
                {
                    var w = r.bw[vi];
                    maxWsum = System.Math.Max(maxWsum, System.Math.Abs(w.weight0 + w.weight1 + w.weight2 + w.weight3 - 1.0));
                    if (!Blend(e, w, a)) { badIndex++; rv[vi] = new Vector3(float.NaN, float.NaN, float.NaN); continue; }
                    double det = a[0] * (a[5] * a[10] - a[6] * a[9]) - a[1] * (a[4] * a[10] - a[6] * a[8]) + a[2] * (a[4] * a[9] - a[5] * a[8]);
                    if (System.Math.Abs(det) < 1e-6) { singular++; rv[vi] = new Vector3(float.NaN, float.NaN, float.NaN); continue; }
                    double px = bv[vi].x - a[3], py = bv[vi].y - a[7], pz = bv[vi].z - a[11];
                    double x = (px * (a[5] * a[10] - a[6] * a[9]) - a[1] * (py * a[10] - a[6] * pz) + a[2] * (py * a[9] - a[5] * pz)) / det;
                    double y = (a[0] * (py * a[10] - a[6] * pz) - px * (a[4] * a[10] - a[6] * a[8]) + a[2] * (a[4] * pz - py * a[8])) / det;
                    double z = (a[0] * (a[5] * pz - py * a[9]) - a[1] * (a[4] * pz - py * a[8]) + px * (a[4] * a[9] - a[5] * a[8])) / det;
                    rv[vi] = new Vector3((float)x, (float)y, (float)z);
                    if (!Finite(rv[vi])) { nonFinite++; continue; }
                    maxMove = System.Math.Max(maxMove, (rv[vi] - bv[vi]).magnitude);
                }
                if (badIndex > 0) r.fail.Add($"{badIndex} vertices weight a bone index outside the {nb} bones");
                if (singular > 0) r.fail.Add($"{singular} vertices have a singular blended skin matrix");
                if (nonFinite > 0) r.fail.Add($"{nonFinite} rest vertices are not finite");
                r.v = rv;
                o.Append($"REST READ (test-only BakeMesh, no Read/Write): mesh '{src.name}' isReadable {src.isReadable}, {rv.Length} vertices, {r.tri.Length / 3} triangles, index order FNV-1a {fnv:x8} (expected {IndexOrderFnv:x8}); {nb} bones at the inverse bind poses ({nonTrs} not TRS), skin matrices within {eDev.ToString("G3", Inv)} of the identity; residual solve: largest move {maxMove.ToString("G3", Inv)} mesh units, largest |sum of weights - 1| {maxWsum.ToString("G3", Inv)}, bad bone index {badIndex}, singular {singular}, non-finite {nonFinite}");
                // 3. the live cross-check: the rest re-skinned with the LIVE bones must reproduce the live renderer's bake (its knee shapes at 0 for this one bake, then restored, no frame between)
                int ns = src.blendShapeCount; var saved = new float[ns];
                for (int i = 0; i < ns; i++) { saved[i] = smr.GetBlendShapeWeight(i); smr.SetBlendShapeWeight(i, 0f); }
                try { smr.BakeMesh(live, false); }
                finally { for (int i = 0; i < ns; i++) smr.SetBlendShapeWeight(i, saved[i]); }
                var lv = live.vertices; var lt = live.triangles;
                if (!lt.SequenceEqual(r.tri)) r.fail.Add($"the rest bake's triangles differ from the live renderer's bake ({lt.Length / 3} triangles, FNV-1a {RestFnv(lt):x8})");
                var lm = new double[nb][]; int nullBones = 0;
                for (int k = 0; k < nb; k++) { if (bones[k] == null) { nullBones++; continue; } lm[k] = AffMul(Aff(bones[k].localToWorldMatrix), Aff(r.bind[k])); }
                if (nullBones > 0) r.fail.Add($"{nullBones} live bones are missing");
                Vector3 pos = smr.transform.position; Quaternion rot = smr.transform.rotation;
                double maxDev = 0; int worst = -1, checkedN = 0;
                for (int vi = 0; vi < lv.Length && vi < rv.Length && vi < r.bw.Length && nullBones == 0; vi++)
                {
                    if (!Finite(rv[vi]) || !Blend(lm, r.bw[vi], a)) continue;
                    Vector3 wl = pos + rot * lv[vi];   // the live bake moved to world as every bake in these tests
                    double qx = a[0] * rv[vi].x + a[1] * rv[vi].y + a[2] * rv[vi].z + a[3] - wl.x, qy = a[4] * rv[vi].x + a[5] * rv[vi].y + a[6] * rv[vi].z + a[7] - wl.y, qz = a[8] * rv[vi].x + a[9] * rv[vi].y + a[10] * rv[vi].z + a[11] - wl.z;
                    double d = System.Math.Sqrt(qx * qx + qy * qy + qz * qz) * 1000.0; checkedN++;
                    if (d > maxDev) { maxDev = d; worst = vi; }
                }
                if (lv.Length != rv.Length) r.fail.Add($"the live bake has {lv.Length} vertices, the rest {rv.Length}");
                if (checkedN != rv.Length) r.fail.Add($"live cross-check covered {checkedN} of {rv.Length} vertices");
                if (maxDev > LiveCheckMm) r.fail.Add($"the rest re-skinned with the live bones misses the live bake by up to {maxDev.ToString("F3", Inv)} mm (vertex {worst}; limit {LiveCheckMm.ToString("F1", Inv)} mm)");
                o.Append($"; live cross-check (rest x live bones x bind poses vs the live bake moved to world, knee shapes 0 for that bake, {ns} blend shapes restored): {checkedN} vertices, largest miss {maxDev.ToString("F4", Inv)} mm (vertex {worst}, limit {LiveCheckMm.ToString("F1", Inv)} mm)");
            }
            catch (System.Exception ex) { r.fail.Add($"rest read threw {ex.GetType().Name}: {ex.Message}"); }
            finally { if (root != null) Object.DestroyImmediate(root); Object.DestroyImmediate(bake); Object.DestroyImmediate(live); }
            if (r.v.Length == 0) r.fail.Add("the mesh read returned 0 vertices");
            r.note = o + (r.fail.Count > 0 ? "; REST READ FAILED: " + string.Join("; ", r.fail) : "; rest read OK");
            comps.AppendLine("== " + r.note);
            if (r.fail.Count > 0) Debug.LogError("X1 P0 " + r.note); else Debug.Log("X1 P0 " + r.note);
            return r;
        }

        /// <summary>
        /// Fail fast (eng_51), called right after each world loads and before any analysed frame: reads the rest mesh, computes the K-f map, builds the hulls (when insets are set) and checks them.
        /// On any failure it prints every reason, sets <see cref="preflightVoid"/> and returns false; the run then stops and writes NO VERDICT (VOID).
        /// </summary>
        bool Preflight(FirstPersonBody fp)
        {
            var why = new List<string>();
            var smr = BodySmr(fp);
            if (smr == null) why.Add("no enabled Survivor_Body view renderer (23791 triangles) in this world");
            else
            {
                var r = RestOf(smr);
                why.AddRange(r.fail.Select(f => "rest mesh read: " + f));
                EnsureKfMap(smr);
                if (kfMapped != KfRows.Length) why.Add($"K-f map: {kfMapped} of {KfRows.Length} K-f rows MAPPED (UNMAPPED {kfUnmapped}, AMBIGUOUS {kfAmbiguous}); mesh read {kfVerts} vertices");
            }
            PrepareHulls(fp);
            if (hullInsets.Length > 0)
            {
                if (hulls.Count != hullInsets.Length) why.Add($"hull: {hulls.Count} hulls built for {hullInsets.Length} insets");
                foreach (var h in hulls) foreach (var f in h.fail) why.Add($"hull inset {Mm(h.mm)} mm: {f}");
            }
            if (why.Count == 0) { comps.AppendLine("== PREFLIGHT OK: rest mesh read, K-f map complete" + (hullInsets.Length > 0 ? $", {hulls.Count} hulls of {HullSegments} tubes and {HullSegments * TrisPerTube} triangles" : "")); return true; }
            preflightVoid = why;
            string text = "PREFLIGHT FAILED (fail fast, before any analysed frame; P0 VERDICT: NO VERDICT (VOID)):" + System.Environment.NewLine + string.Join(System.Environment.NewLine, why.Select(x => "  VOID reason: " + x));
            comps.AppendLine("== " + text);
            Debug.LogError("X1 P0 " + text);
            return false;
        }
        readonly List<HullInfo> hulls = new List<HullInfo>();
        readonly Dictionary<float, HullAcc> hullAcc = new Dictionary<float, HullAcc>();
        readonly StringBuilder hullCsv = new StringBuilder("group,point,pitch,inset_mm,state,KaComps,KaClosed,KaPixels,KaPixelsClosed,leakUnion,leakBody,leakLit,leakWasBody,leakWasInteriorNotKa,leakWasBackground,leakWithin2pxOfKa,leakFaint3LSB,leakNoAO,twinOffDrift,skinControlHit,skinControlOnScreen,knownSpot,KaTarget,QaSlot1,QaSlot2,hullBonesInView" + System.Environment.NewLine);
        Material hullMat; string hullShader = "not built";

        static string Mm(float mm) => mm.ToString("+0.##;-0.##;0", CultureInfo.InvariantCulture);

        float[] ParseInsets(float[] fallback)
        {
            hullToggleByEnabled = System.Environment.GetEnvironmentVariable("HULL_TOGGLE") == "enabled";
            string e = System.Environment.GetEnvironmentVariable("HULL_INSET");
            if (string.IsNullOrEmpty(e)) return fallback;
            return e.Split(',').Select(x => float.Parse(x.Trim(), Inv)).Distinct().ToArray();
        }

        bool IsHull(SkinnedMeshRenderer r) => hulls.Any(h => h.r == r);

        static SkinnedMeshRenderer BodySmr(FirstPersonBody fp) =>
            fp.ViewRenderers.OfType<SkinnedMeshRenderer>().FirstOrDefault(r => r.enabled && r.sharedMesh != null && r.sharedMesh.GetIndexCount(0) / 3 == 23791);

        static void Show(HullInfo h, bool on, bool byEnabled) { if (h.r == null) return; if (byEnabled) h.r.enabled = on; else { h.r.enabled = true; h.r.forceRenderingOff = !on; } }

        /// <summary>All hulls visible (between frames, so the engine skins them) or hidden (every S2 render except a hull's own twin render).</summary>
        void HullsVisible(bool on) { foreach (var h in hulls) Show(h, on, hullToggleByEnabled); }

        /// <summary>Destroys the hulls of the previous world and builds one per inset on this world's view body (called right after the world loads, so the engine skins them before the first analysed frame).</summary>
        void PrepareHulls(FirstPersonBody fp)
        {
            if (hullInsets.Length == 0) return;
            DestroyHullObjects();
            var smr = BodySmr(fp);
            if (smr == null) { comps.AppendLine("== HULL: no Survivor_Body view renderer in this world; its frames are SKIPPED"); return; }
            if (hullMat == null)
            {
                var sh = Shader.Find(HullShaderName);
                if (sh == null) { hullShader = "NONE: " + HullShaderName + " not found"; comps.AppendLine("== HULL: " + hullShader + "; every frame is SKIPPED"); return; }
                hullMat = new Material(sh) { name = "S2BackingHullTest (vertex colour)" };
                if (hullMat.HasProperty("_BaseColor")) hullMat.SetColor("_BaseColor", Color.white);
                if (hullMat.HasProperty("_Cull")) hullMat.SetFloat("_Cull", 0f);   // two-sided (test material)
                hullShader = sh.name;
            }
            foreach (float mm in hullInsets)
            {
                var h = BuildHull(smr, mm);
                h.builtFrame = Time.frameCount;
                hulls.Add(h);
                if (!hullAcc.TryGetValue(mm, out var a)) hullAcc[mm] = a = new HullAcc();
                a.note = h.note; a.handBone |= h.handBone;
                foreach (var f in h.fail) if (!a.buildFail.Contains(f)) a.buildFail.Add(f);   // eng_51: a failed build in ANY world voids the inset
            }
            HullsVisible(true);
        }

        HullInfo BuildHull(SkinnedMeshRenderer smr, float insetMm)
        {
            var rm = RestOf(smr);   // eng_51: the rest read without Read/Write (src.vertices is empty in PlayMode)
            if (rm.fail.Count > 0)
            {
                var bad = new HullInfo { owner = smr, mm = insetMm, note = $"hull inset {Mm(insetMm)} mm: NOT BUILT, the rest mesh read failed ({string.Join("; ", rm.fail)})" };
                bad.fail.Add("not built: the rest mesh read failed");
                comps.AppendLine("== " + bad.note);
                return bad;
            }
            var rv = rm.v; var bw = rm.bw; var bind = rm.bind; var bones = smr.bones;
            int nb = bones.Length;
            var segBones = new List<Transform>();
            var origin = new Vector3[nb];
            for (int i = 0; i < nb; i++) origin[i] = bind[i].inverse.MultiplyPoint3x4(Vector3.zero);   // rest origin of bone i in mesh space (the bind pose maps mesh space to bone space)
            int IndexOf(string n) { for (int i = 0; i < nb; i++) if (bones[i] != null && bones[i].name == n) return i; return -1; }
            int ParentIdx(int i) { for (var t = bones[i].parent; t != null; t = t.parent) { int k = System.Array.IndexOf(bones, t); if (k >= 0) return k; } return -1; }
            int FirstChildIdx(int i) { for (int k = 0; k < nb; k++) if (bones[k] != null && bones[k].parent == bones[i] && !IsHandBone(bones[k].name)) return k; return -1; }
            var cs = new Color[] { ((Color)ClothInterior).linear, ((Color)ForearmSkin).linear };   // linear colour space project: vertex colours are passed through, so they are written linear
            var verts = new List<Vector3>(); var cols = new List<Color>(); var weights = new List<BoneWeight>(); var tri = new List<int>();
            var used = new SortedSet<string>(); var notes = new List<string>(); var segNotes = new List<string>(); int clamped = 0;
            foreach (string side in new[] { ".L", ".R" })
                foreach (var (bone, tail, child) in new (string, string, string)[] { ("UpperLeg", "LowerLeg", "LowerLeg"), ("LowerLeg", "Foot", "Foot"), ("Foot", null, null), ("UpperArm", "LowerArm", "LowerArm"), ("LowerArm", "Wrist", null) })
                {
                    string name = bone + side;
                    int bi = IndexOf(name);
                    if (bi < 0) { notes.Add(name + " missing"); continue; }
                    var own = new List<int>();
                    for (int v = 0; v < rv.Length; v++) if (bw[v].boneIndex0 == bi) own.Add(v);
                    if (own.Count == 0) { notes.Add(name + " has no dominant vertices"); continue; }
                    int ti = tail != null ? IndexOf(tail + side) : FirstChildIdx(bi);   // LowerArm's tail is the Wrist POSITION only (never a weight)
                    Vector3 head = origin[bi];
                    Vector3 tl = ti >= 0 ? origin[ti] : rv[own.OrderByDescending(v => (rv[v] - head).sqrMagnitude).First()];
                    Vector3 axis = tl - head; float len = axis.magnitude;
                    if (len < 1e-9f) { notes.Add(name + " has no axis"); continue; }
                    var m = bones[bi].localToWorldMatrix * bind[bi];   // mesh space -> world, for the world size of a mesh unit
                    float worldPerUnit = (m.MultiplyVector(Vector3.right).magnitude + m.MultiplyVector(Vector3.up).magnitude + m.MultiplyVector(Vector3.forward).magnitude) / 3f;
                    float inset = insetMm * 0.001f / worldPerUnit;
                    Vector3 ad = axis / len, u = Vector3.Cross(ad, Mathf.Abs(ad.y) < 0.9f ? Vector3.up : Vector3.right).normalized, w = Vector3.Cross(ad, u);
                    var rad = new float[Rings, Sides];
                    for (int i = 0; i < Rings; i++) for (int j = 0; j < Sides; j++) rad[i, j] = float.MaxValue;
                    float segMin = float.MaxValue;
                    foreach (int v in own)
                    {
                        Vector3 d = rv[v] - head; float s = Vector3.Dot(d, ad) / len;
                        if (s < -0.1f || s > 1.1f) continue;
                        Vector3 radial = d - Vector3.Dot(d, ad) * ad; float rho = radial.magnitude, phi = Mathf.Atan2(Vector3.Dot(radial, w), Vector3.Dot(radial, u));
                        int j = Mathf.Clamp(Mathf.FloorToInt((phi + Mathf.PI) / (2f * Mathf.PI / Sides)), 0, Sides - 1);
                        segMin = Mathf.Min(segMin, rho);
                        for (int i = 0; i < Rings; i++) if (Mathf.Abs(s - i / (Rings - 1f)) <= 0.1f) rad[i, j] = Mathf.Min(rad[i, j], rho);
                    }
                    if (segMin == float.MaxValue) { notes.Add(name + " has no vertex in range"); continue; }
                    int empty = Fill(rad, segMin);
                    int parent = ParentIdx(bi), nchild = child != null ? IndexOf(child + side) : (tail == null ? FirstChildIdx(bi) : -1);
                    if (parent >= 0 && IsHandBone(bones[parent].name)) parent = -1;
                    if (nchild >= 0 && IsHandBone(bones[nchild].name)) nchild = -1;
                    Color col = bone == "LowerArm" ? cs[1] : cs[0];
                    BoneWeight Wt(int nbr) { if (nbr >= 0) { used.Add(bones[nbr].name); return new BoneWeight { boneIndex0 = bi, weight0 = 0.5f, boneIndex1 = nbr, weight1 = 0.5f }; } return new BoneWeight { boneIndex0 = bi, weight0 = 1f }; }
                    used.Add(name); segBones.Add(bones[bi]);
                    int baseIdx = verts.Count;
                    for (int i = 0; i < Rings; i++)
                        for (int j = 0; j < Sides; j++)
                        {
                            float r0 = rad[i, j], r = r0 - inset;
                            if (r < 0.1f * r0) { r = 0.1f * r0; clamped++; }
                            float phi = -Mathf.PI + (j + 0.5f) * 2f * Mathf.PI / Sides;
                            verts.Add(head + ad * (len * i / (Rings - 1f)) + r * (Mathf.Cos(phi) * u + Mathf.Sin(phi) * w));
                            cols.Add(col); weights.Add(Wt(i == 0 ? parent : i == Rings - 1 ? nchild : -1));
                        }
                    for (int i = 0; i < Rings - 1; i++)
                        for (int j = 0; j < Sides; j++)
                        {
                            int a = baseIdx + i * Sides + j, b = baseIdx + i * Sides + (j + 1) % Sides, c = baseIdx + (i + 1) * Sides + j, d2 = baseIdx + (i + 1) * Sides + (j + 1) % Sides;
                            tri.AddRange(new[] { a, c, b, b, c, d2 });
                        }
                    int cap0 = verts.Count; verts.Add(head); cols.Add(col); weights.Add(Wt(parent));
                    int cap1 = verts.Count; verts.Add(head + ad * len); cols.Add(col); weights.Add(Wt(nchild));
                    for (int j = 0; j < Sides; j++)
                    {
                        tri.AddRange(new[] { cap0, baseIdx + (j + 1) % Sides, baseIdx + j });
                        int last = baseIdx + (Rings - 1) * Sides;
                        tri.AddRange(new[] { cap1, last + j, last + (j + 1) % Sides });
                    }
                    segNotes.Add($"{name} ({own.Count} vertices, length {(len * worldPerUnit * 1000f).ToString("F0", Inv)} mm, {empty} empty cells interpolated, colour {(bone == "LowerArm" ? "forearmSkin" : "clothInterior")}, end rings {(parent >= 0 ? bones[parent].name : "-")}/{(nchild >= 0 ? bones[nchild].name : "-")}, inset {(inset * worldPerUnit * 1000f).ToString("F2", Inv)} mm = {inset.ToString("G4", Inv)} mesh units)");
                }
            var mesh = new Mesh { name = "S2BackingHull" + Mm(insetMm) };
            mesh.SetVertices(verts); mesh.SetColors(cols); mesh.SetTriangles(tri, 0); mesh.boneWeights = weights.ToArray(); mesh.bindposes = bind;
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            var go = new GameObject("S2BackingHull" + Mm(insetMm)) { layer = smr.gameObject.layer };   // scene root: never under the view root, so FirstPersonBody.Rescan cannot pick it up; a skinned mesh follows its bones, not its own transform
            var r2 = go.AddComponent<SkinnedMeshRenderer>();
            r2.sharedMesh = mesh; r2.bones = bones; r2.rootBone = smr.rootBone; r2.sharedMaterial = hullMat;
            r2.shadowCastingMode = ShadowCastingMode.Off; r2.receiveShadows = false; r2.updateWhenOffscreen = true; r2.skinnedMotionVectors = false;
            bool hand = used.Any(IsHandBone);
            var fail = new List<string>();   // eng_51: fail-closed build checks
            foreach (var n in notes) fail.Add("SKIPPED SEGMENT: " + n);
            if (segNotes.Count != HullSegments) fail.Add($"{segNotes.Count} tubes built, expected {HullSegments}");
            if (tri.Count / 3 != HullSegments * TrisPerTube) fail.Add($"{tri.Count / 3} triangles, expected {HullSegments * TrisPerTube} ({HullSegments} tubes x {TrisPerTube})");
            if (verts.Count != HullSegments * VertsPerTube) fail.Add($"{verts.Count} vertices, expected {HullSegments * VertsPerTube}");
            if (hand) fail.Add("HAND BONE in the hull");
            if (verts.Any(p => !Finite(p))) fail.Add("a hull vertex is not finite");
            string note = $"hull inset {Mm(insetMm)} mm: {tri.Count / 3} triangles, {verts.Count} vertices, {segNotes.Count} tubes (expected {HullSegments} x {TrisPerTube} triangles), 1 SkinnedMeshRenderer, 1 material ({hullShader}, vertex colour: clothInterior sRGB 65,54,47, forearmSkin sRGB 151,119,108), shadows Off, layer {go.layer}; bones [{string.Join(", ", used)}]{(hand ? " HAND BONE IN HULL (violation)" : "; no hand bone")}; cells clamped to 10% of their radius {clamped}{(notes.Count > 0 ? "; SKIPPED SEGMENTS: " + string.Join(", ", notes) : "")}{(fail.Count > 0 ? "; BUILD FAILED: " + string.Join("; ", fail) : "; build OK")}"
                + System.Environment.NewLine + "    segments: " + string.Join("; ", segNotes);
            comps.AppendLine("== " + note);
            var info = new HullInfo { owner = smr, r = r2, mm = insetMm, mesh = mesh, tris = tri.Count / 3, verts = verts.Count, clamped = clamped, handBone = hand, note = note, segments = segNotes.Count, segBones = segBones.ToArray() };
            info.fail.AddRange(fail);
            return info;
        }

        /// <summary>Fills empty cells (float.MaxValue): from the nearest filled sectors of the same ring (linear by angle), then from the nearest filled rings of the same sector, then the segment minimum. Returns the number filled.</summary>
        static int Fill(float[,] rad, float segMin)
        {
            int filled = 0;
            var ringHas = new bool[Rings];
            for (int i = 0; i < Rings; i++)
            {
                var have = Enumerable.Range(0, Sides).Where(j => rad[i, j] != float.MaxValue).ToList();
                ringHas[i] = have.Count > 0;
                if (have.Count == 0 || have.Count == Sides) continue;
                var copy = new float[Sides]; for (int j = 0; j < Sides; j++) copy[j] = rad[i, j];
                for (int j = 0; j < Sides; j++)
                {
                    if (copy[j] != float.MaxValue) continue;
                    int dp = 1; while (copy[(j - dp + Sides) % Sides] == float.MaxValue) dp++;
                    int dn = 1; while (copy[(j + dn) % Sides] == float.MaxValue) dn++;
                    rad[i, j] = (copy[(j - dp + Sides) % Sides] * dn + copy[(j + dn) % Sides] * dp) / (dp + dn); filled++;
                }
            }
            for (int i = 0; i < Rings; i++)
            {
                if (ringHas[i]) continue;
                int lo = i - 1; while (lo >= 0 && !ringHas[lo]) lo--;
                int hi = i + 1; while (hi < Rings && !ringHas[hi]) hi++;
                for (int j = 0; j < Sides; j++)
                {
                    rad[i, j] = lo >= 0 && hi < Rings ? (rad[lo, j] * (hi - i) + rad[hi, j] * (i - lo)) / (hi - lo) : lo >= 0 ? rad[lo, j] : hi < Rings ? rad[hi, j] : segMin;
                    filled++;
                }
            }
            return filled;
        }

        static int Diff(Color32[] a, Color32[] b) { int n = 0; for (int i = 0; i < a.Length; i++) if (a[i].r != b[i].r || a[i].g != b[i].g || a[i].b != b[i].b) n++; return n; }
        static bool Same(Color32 a, Color32 b) => a.r == b.r && a.g == b.g && a.b == b.b;
        static int MaxDelta(Color32 a, Color32 b) => Mathf.Max(Mathf.Abs(a.r - b.r), Mathf.Max(Mathf.Abs(a.g - b.g), Mathf.Abs(a.b - b.b)));

        /// <summary>SSAO evidence (not the record): the hull's depth prepass passes on or off on the shared test material (URP LightMode tags of Particles/Unlit).</summary>
        void HullDepthPasses(bool on) { if (hullMat == null) return; hullMat.SetShaderPassEnabled("DepthOnly", on); hullMat.SetShaderPassEnabled("DepthNormalsOnly", on); }

        /// <summary>
        /// The P0 measurement of one frame (called from Frame, same frame, no yield). The hull is off on entry. Per inset: hull-ON body-only and lit renders against one fresh hull-OFF pair; LEAK, K-a closure, the known spot, the targets,
        /// the hull-only skin control, and twin crops of the targets. The hull is off again on return.
        /// </summary>
        void HullPass(string group, string point, float pitch, Camera cam, FirstPersonBody fp, Color32[] litBase, Color32[] bodyBase, List<Comp> cs, List<Comp> kaComps, Dictionary<string, (Comp c, string region)> frameTargets, Comp spotComp, int unclass)
        {
            var smr = BodySmr(fp);
            bool ready = hullMat != null && smr != null && hulls.Count == hullInsets.Length && hulls.All(h => h.owner == smr && h.r != null && Time.frameCount > h.builtFrame);
            if (!ready)
            {
                foreach (float mm in hullInsets) { if (!hullAcc.TryGetValue(mm, out var a0)) hullAcc[mm] = a0 = new HullAcc(); a0.skipped++; hullCsv.AppendLine($"{group},{point},{pitch:F0},{Mm(mm)},SKIPPED,,,,,,,,,,,,,,,,,,,,,"); }
                comps.AppendLine($"  HULL SKIPPED in this frame (hull not built, built this frame, or bound to another renderer): every inset counts the frame as skipped");
                return;
            }
            int layer = player.gameObject.layer;
            HullsVisible(false);
            var bodyOff = RenderPixels(cam, layer, true);   // the hull-OFF twin, same frame
            var litOff = RenderPixels(cam, 0, false);
            int drift = Diff(bodyOff, bodyBase) + Diff(litOff, litBase);   // control: two hull-off renders of one frame must agree
            var offMask = ToMask(bodyOff);
            var ka = new bool[W * H]; var interior = new bool[W * H];
            foreach (var c in cs) foreach (var p in c.m) interior[(H - 1 - p.yTop) * W + p.x] = true;
            foreach (var c in kaComps) foreach (var p in c.m) ka[(H - 1 - p.yTop) * W + p.x] = true;
            bool NearKa(int x, int yTop) { for (int dy = -2; dy <= 2; dy++) for (int dx = -2; dx <= 2; dx++) { int xx = x + dx, yy = yTop + dy; if (xx >= 0 && xx < W && yy >= 0 && yy < H && ka[(H - 1 - yy) * W + xx]) return true; } return false; }
            var bake = new Mesh();
            try
            {
                foreach (var h in hulls)
                {
                    var acc = hullAcc[h.mm];
                    Color32[] bodyOn, litOn;
                    Show(h, true, hullToggleByEnabled);
                    try { bodyOn = RenderPixels(cam, layer, true); litOn = RenderPixels(cam, 0, false); }   // the hull-ON twin, same frame, no yield
                    finally { HullsVisible(false); }
                    // SSAO evidence (not the record): a third twin, same frame, no yield, with the hull kept out of the depth/normals prepass that feeds SSAO
                    Color32[] bodyOnNoAo, litOnNoAo;
                    Show(h, true, hullToggleByEnabled); HullDepthPasses(false);
                    try { bodyOnNoAo = RenderPixels(cam, layer, true); litOnNoAo = RenderPixels(cam, 0, false); }
                    finally { HullDepthPasses(true); HullsVisible(false); }
                    var onMask = ToMask(bodyOn);
                    // LEAK-noAO control (evidence only): the no-AO twin must draw the same body-only coverage as the record hull-ON render, else LEAK-noAO is n/a in this frame
                    var noAoMask = ToMask(bodyOnNoAo); bool noAoValid = true;
                    for (int id = 0; id < W * H && noAoValid; id++) if (noAoMask[id] != onMask[id]) noAoValid = false;
                    // LEAK of record: any changed pixel in either render outside the hull-off K-a pixels
                    int lb = 0, ll = 0, lu = 0, wasBody = 0, wasInt = 0, wasBg = 0, halo = 0, faint = 0, noAo = 0; var first = new List<string>();
                    for (int id = 0; id < W * H; id++)
                    {
                        if (ka[id]) continue;
                        if (noAoValid && (!Same(bodyOnNoAo[id], bodyOff[id]) || !Same(litOnNoAo[id], litOff[id]))) noAo++;   // evidence: the record rule against the no-SSAO-input twin
                        bool db = !Same(bodyOn[id], bodyOff[id]), dl = !Same(litOn[id], litOff[id]);
                        if (!db && !dl) continue;
                        if (db) lb++; if (dl) ll++; lu++;
                        if (Mathf.Max(MaxDelta(bodyOn[id], bodyOff[id]), MaxDelta(litOn[id], litOff[id])) <= 3) faint++;   // evidence: largest per-channel change <= 3 LSB
                        int x = id % W, yTop = H - 1 - id / W;
                        string was = !offMask[id] ? "body" : interior[id] ? "interior magenta not K-a" : "background";
                        if (was == "body") wasBody++; else if (was == "background") wasBg++; else wasInt++;
                        if (NearKa(x, yTop)) halo++;
                        if (first.Count < 20) first.Add($"({x}, {yTop}) {(db && dl ? "body+lit" : db ? "body" : "lit")}, was {was}");
                    }
                    // K-a closure in the hull-ON body-only render
                    int closed = 0, pixels = 0, pixelsClosed = 0;
                    foreach (var c in kaComps)
                    {
                        bool all = true;
                        foreach (var p in c.m) { pixels++; if (onMask[(H - 1 - p.yTop) * W + p.x]) all = false; else pixelsClosed++; }
                        if (all) closed++;
                    }
                    bool Closed(Comp c) => c.m.All(p => !onMask[(H - 1 - p.yTop) * W + p.x]);
                    string spotText = "-";
                    if (spotComp != null) { bool sc = Closed(spotComp); acc.spotTotal++; if (sc) acc.spotClosed++; spotText = sc ? "CLOSED" : "OPEN"; }
                    var tText = new List<string>();
                    foreach (var t in Targets)
                    {
                        if (!frameTargets.TryGetValue(t.slot, out var tc)) { tText.Add("-"); continue; }
                        bool c2 = Closed(tc.c); acc.target.TryGetValue(t.slot, out var tv); acc.target[t.slot] = (tv.total + 1, tv.closed + (c2 ? 1 : 0)); tText.Add(c2 ? "CLOSED" : "OPEN");
                    }
                    // control: the hull alone (body hidden) must cover its own CPU-baked vertices
                    var vr = fp.ViewRenderers; var saved = vr.Select(r => r.forceRenderingOff).ToArray(); bool[] alone;
                    try { foreach (var r in vr) r.forceRenderingOff = true; Show(h, true, hullToggleByEnabled); alone = ToMask(RenderPixels(cam, layer, true)); }
                    finally { for (int i = 0; i < vr.Count; i++) vr[i].forceRenderingOff = saved[i]; HullsVisible(false); }
                    h.r.BakeMesh(bake, false);
                    var bv = bake.vertices; Vector3 hp = h.r.transform.position; Quaternion hr = h.r.transform.rotation; int onScreen = 0, hit = 0;
                    foreach (var v in bv)
                    {
                        var vp = cam.WorldToViewportPoint(hp + hr * v);
                        if (vp.z <= cam.nearClipPlane) continue;
                        int px = Mathf.FloorToInt(vp.x * W), py = H - 1 - Mathf.FloorToInt(vp.y * H);
                        if (px < 1 || px > W - 2 || py < 1 || py > H - 2) continue;
                        onScreen++;
                        bool any = false; for (int dy = -1; dy <= 1 && !any; dy++) for (int dx = -1; dx <= 1 && !any; dx++) any = !Mag(alone, px + dx, py + dy);
                        if (any) hit++;
                    }
                    // eng_51: a frame with no hull vertex on screen is a control FAILURE when a hull segment bone is in view (at least 10 px inside the image, in front of the near plane); n/a only when none is
                    int bonesInView = 0;
                    foreach (var sb in h.segBones)
                    {
                        if (sb == null) continue;
                        var bp = cam.WorldToViewportPoint(sb.position);
                        float bx = bp.x * W, by = bp.y * H;
                        if (bp.z > cam.nearClipPlane && bx >= BoneInViewMarginPx && bx <= W - BoneInViewMarginPx && by >= BoneInViewMarginPx && by <= H - BoneInViewMarginPx) bonesInView++;
                    }
                    bool skinOk;
                    if (onScreen == 0)
                    {
                        if (bonesInView > 0) { skinOk = false; acc.skinFail++; acc.skinNoneInView++; }
                        else { skinOk = true; acc.skinNa++; }
                    }
                    else { skinOk = hit >= SkinControlMin * onScreen; if (!skinOk) acc.skinFail++; }
                    // twin crops of the targets in this frame (lit 4x and the 1x window, hull on)
                    if (frameTargets.Count > 0)
                    {
                        Color32[] bigOn;
                        Show(h, true, hullToggleByEnabled);
                        try { bigOn = RenderPixels(cam, 0, false, SW, SH); } finally { HullsVisible(false); }
                        foreach (var kv in frameTargets)
                        {
                            var k = WriteCrop(kv.Key + "-hull" + Mm(h.mm) + "mm", group, point, pitch, kv.Value.c, kv.Value.region + " hull on", cam, bigOn);
                            WriteWindow1x(k, litOn, kv.Value.c);
                            hullCrops.Add(k);
                        }
                    }
                    acc.frames++; acc.kaComps += kaComps.Count; acc.kaClosed += closed; acc.kaPixels += pixels; acc.kaPixelsClosed += pixelsClosed;
                    acc.leakUnion += lu; acc.leakBody += lb; acc.leakLit += ll; acc.leakWasBody += wasBody; acc.leakWasInterior += wasInt; acc.leakWasBackground += wasBg; acc.leakHalo += halo;
                    acc.leakFaint += faint; if (!noAoValid) acc.leakNoAoNa++; else { acc.leakNoAo += noAo; if (noAo > 0) { acc.leakNoAoFrames++; acc.maxLeakNoAo = Mathf.Max(acc.maxLeakNoAo, noAo); } }
                    string noAoText = noAoValid ? noAo.ToString(CultureInfo.InvariantCulture) : "n/a";
                    if (drift > 0) acc.driftFrames++;
                    if (unclass > 0) acc.kaIncompleteFrames++;
                    if (lu > 0) { acc.leakFrames++; acc.maxLeak = Mathf.Max(acc.maxLeak, lu); if (acc.leakList.Count < 40) acc.leakList.Add($"{group} {point} +{pitch:F0}: {lu} px (body {lb}, lit {ll}; was body {wasBody}, interior not K-a {wasInt}, background {wasBg}; within 2 px of K-a {halo}; <= 3 LSB {faint}; LEAK-noAO {noAoText}); first: {string.Join("; ", first.Take(5))}"); }
                    hullCsv.AppendLine($"{group},{point},{pitch:F0},{Mm(h.mm)},{(drift > 0 ? "DRIFT" : !skinOk ? "SKINFAIL" : "OK")},{kaComps.Count},{closed},{pixels},{pixelsClosed},{lu},{lb},{ll},{wasBody},{wasInt},{wasBg},{halo},{faint},{noAoText},{drift},{hit},{onScreen},{spotText},{string.Join(",", tText)},{bonesInView}");
                    comps.AppendLine($"  HULL inset {Mm(h.mm)} mm: K-a components {kaComps.Count}, closed {closed} ({pixelsClosed} of {pixels} K-a pixels); LEAK {lu} px (body-only {lb}, lit {ll}; was body {wasBody}, interior magenta not K-a {wasInt}, background {wasBg}; within 2 px of a K-a pixel {halo}; largest channel change <= 3 LSB {faint}); LEAK-noAO {noAoText}{(noAoValid ? " px" : " (no-AO twin body-only mask differs from the hull-ON mask)")} (evidence: hull out of the SSAO input){(first.Count > 0 ? "; first " + first.Count + ": " + string.Join("; ", first) : "")}; twin-off drift {drift}; skin control {hit} of {onScreen} on-screen hull vertices drawn, hull bones in view {bonesInView}{(skinOk ? "" : onScreen == 0 ? " FAILED (no hull vertex on screen while a hull bone is in view)" : " FAILED")}; known spot {spotText}; targets {string.Join(" ", Targets.Select((t, i) => t.slot + " " + tText[i]))}{(unclass > 0 ? $"; K-a set INCOMPLETE ({unclass} unclassified components)" : "")}");
                }
            }
            finally { Object.Destroy(bake); HullsVisible(false); }
        }

        string HullSummary()
        {
            string nl = System.Environment.NewLine;
            var s = new StringBuilder();
            s.AppendLine($"# X1 P0 backing hull (td-49 B35/B35a/B35b/B35c), per inset over every analysed frame. EXCEPTIONS {exceptions}. Insets in mm: positive = inward of the innermost profile, negative = outward between the layers.");
            s.AppendLine("# LEAK of record: same-frame twin renders, hull ON vs hull OFF, no yield between, in BOTH the body-only and the lit render; every differing pixel outside the hull-off K-a components' 1x pixels counts; must be 0. The 'was ...' split and 'within 2 px of K-a' are evidence only.");
            s.AppendLine($"# Toggle: {(hullToggleByEnabled ? "renderer.enabled (HULL_TOGGLE=enabled)" : "renderer.forceRenderingOff with the renderer kept enabled (default; HULL_TOGGLE=enabled for renderer.enabled)")}. Material: {hullShader}, vertex colour, two-sided, shadows Off. Colours clothInterior sRGB (65, 54, 47), forearmSkin sRGB (151, 119, 108) (ta-49 x1_colours.txt), written as linear {((Color)ClothInterior).linear} / {((Color)ForearmSkin).linear}.");
            s.AppendLine("# Controls (fail-closed): twin-off drift 0 in every frame; the hull-only render covers >= 90% of the on-screen CPU-baked hull vertices, and a frame with no hull vertex on screen while a hull bone is in view FAILS it (no hull vertex on screen in every frame also fails); no SKIPPED frame; no frame with an unclassified component; EXCEPTIONS 0; no hand bone in the hull.");
            s.AppendLine($"# Run-level controls (fail-closed, eng_51): the rest mesh read (vertices > 0, index order {IndexOrderFnv:x8}, live cross-check <= {LiveCheckMm.ToString("F1", Inv)} mm); every K-f row MAPPED (none UNMAPPED or AMBIGUOUS); every hull built with {HullSegments} tubes and {HullSegments * TrisPerTube} triangles, no skipped segment; both known spots measured and the K-a target found.");
            s.AppendLine("# Selection: the smallest OUTWARD inset (the sweep sorted from most inward to most outward; the first that passes) with LEAK 0, K-a 100% closed (components and pixels), both known spots and the K-a target #2499/#16175 closed, and valid controls. If ANY run-level or inset control fails: NO VERDICT (VOID), each reason printed. Only when every control holds and no inset passes: X1 FAILS (X2).");
            var global = RunVoidReasons();
            if (global.Count > 0) { s.AppendLine("RUN-LEVEL CONTROLS FAILED:"); foreach (var g in global) s.AppendLine("  " + g); }
            else s.AppendLine($"run-level controls: OK ({rest?.note})");
            s.AppendLine("# SSAO evidence (td ruling pending; not the record): PC_Renderer SSAO (DepthNormals source) sees the hull's depth prepass, so a closed gap can shift nearby body AO. '<= 3 LSB' splits the record leak by size; LEAK-noAO repeats the record rule on a third same-frame twin with the hull's DepthOnly/DepthNormalsOnly passes off. LEAK-noAO is n/a in a frame where the no-AO twin's body-only mask differs from the hull-ON body-only mask. The verdict uses the record LEAK only (td B35 D); any inset that passes on LEAK-noAO is printed as a separate SSAO NOTE for td to rule on.");
            var valid = new Dictionary<float, string>(); var pass = new Dictionary<float, bool>(); var passNoAo = new Dictionary<float, bool>();
            foreach (float mm in hullInsets)
            {
                if (!hullAcc.TryGetValue(mm, out var a)) { s.AppendLine($"inset {Mm(mm)} mm: no frame reached the hull pass"); valid[mm] = "no frame"; pass[mm] = passNoAo[mm] = false; continue; }
                var why = new List<string>();
                if (exceptions > 0) why.Add($"EXCEPTIONS {exceptions}");
                if (a.frames == 0) why.Add("0 frames measured");
                if (a.skipped > 0) why.Add($"{a.skipped} frames SKIPPED");
                if (a.driftFrames > 0) why.Add($"twin-off drift in {a.driftFrames} frames");
                if (a.skinFail > 0) why.Add($"skin control failed in {a.skinFail} frames ({a.skinNoneInView} of them with no hull vertex on screen while a hull bone was in view)");
                if (a.frames > 0 && a.skinNa + a.skinNoneInView >= a.frames) why.Add($"skin control: no hull vertex on screen in every frame ({a.skinNa + a.skinNoneInView} of {a.frames})");
                if (a.kaIncompleteFrames > 0) why.Add($"K-a set incomplete (unclassified components) in {a.kaIncompleteFrames} frames");
                if (a.handBone) why.Add("HAND BONE in the hull");
                foreach (var f in a.buildFail) why.Add("hull build: " + f);
                a.target.TryGetValue("K-a-target", out var kt);
                if (a.spotTotal != 2) why.Add($"known spots measured {a.spotTotal} of 2 (K1)");   // review of eng_50: a K1 miss is a control failure (VOID), never an X1 failure
                if (kt.total == 0) why.Add("K-a target #2499/#16175 not found");
                valid[mm] = why.Count == 0 ? "" : string.Join(", ", why);
                bool kaAll = a.kaComps == a.kaClosed && a.kaPixels == a.kaPixelsClosed, spots = a.spotTotal == 2 && a.spotClosed == 2, target = kt.total > 0 && kt.closed == kt.total;
                pass[mm] = why.Count == 0 && a.leakUnion == 0 && kaAll && spots && target;
                passNoAo[mm] = why.Count == 0 && a.leakNoAoNa == 0 && a.leakNoAo == 0 && kaAll && spots && target;
                s.AppendLine($"inset {Mm(mm)} mm: frames measured {a.frames}, skipped {a.skipped}; K-a components {a.kaComps}, closed {a.kaClosed}; K-a pixels {a.kaPixels}, closed {a.kaPixelsClosed}; LEAK {a.leakUnion} px in {a.leakFrames} frames (max {a.maxLeak}; body-only {a.leakBody}, lit {a.leakLit}; was body {a.leakWasBody}, interior magenta not K-a {a.leakWasInterior}, background {a.leakWasBackground}; within 2 px of K-a {a.leakHalo}; largest channel change <= 3 LSB {a.leakFaint})");
                string noAoSum = a.leakNoAoNa > 0 ? $"n/a (no-AO twin body-only mask differs from the hull-ON mask in {a.leakNoAoNa} frames; valid frames: {a.leakNoAo} px in {a.leakNoAoFrames} frames, max {a.maxLeakNoAo})" : $"{a.leakNoAo} px in {a.leakNoAoFrames} frames (max {a.maxLeakNoAo})";
                s.AppendLine($"  SSAO evidence (not the record): LEAK-noAO (hull DepthOnly/DepthNormalsOnly off) {noAoSum}; would pass with LEAK-noAO in place of LEAK: {passNoAo[mm]}");
                s.AppendLine($"  known spots (Sit (391, 397), Squat (373, 490)) closed {a.spotClosed} of {a.spotTotal} (need 2 of 2); targets: {string.Join("; ", Targets.Select(t => a.target.TryGetValue(t.slot, out var v) ? $"{t.slot} closed {v.closed} of {v.total}" : $"{t.slot} not measured"))}");
                s.AppendLine($"  controls: twin-off drift frames {a.driftFrames}; skin control failed {a.skinFail} (no hull vertex on screen while a hull bone in view {a.skinNoneInView}), no hull vertex on screen and no hull bone in view {a.skinNa}; K-a incomplete frames {a.kaIncompleteFrames}; VALID {(why.Count == 0 ? "yes" : "NO (" + valid[mm] + ")")}; LEAK0 {a.leakUnion == 0}; K-a all closed {kaAll}; known spots closed {spots}; K-a target closed {target}; PASS {pass[mm]}");
                s.AppendLine("  " + a.note.Replace(nl, nl + "  "));
                foreach (var l in a.leakList) s.AppendLine("  leak: " + l);
            }
            var order = hullInsets.OrderByDescending(x => x).ToList();
            var pick = order.Where(x => pass[x]).Select(x => (float?)x).FirstOrDefault();
            if (order.Count == 0 || global.Count > 0 || order.Any(x => valid[x].Length > 0))
            {   // eng_51: fail closed; a run-level failure or ANY invalid inset voids the whole sweep (the selection needs every more-inward inset measured), never X1 FAILS
                var reasons = new List<string>(global);
                if (order.Count == 0) reasons.Add("no inset in the sweep");
                foreach (var x in order.Where(x => valid[x].Length > 0)) reasons.Add($"inset {Mm(x)} mm: {valid[x]}");
                s.AppendLine($"P0 VERDICT: NO VERDICT (VOID): {reasons.Count} control failure(s), listed below{(pick.HasValue ? $" (inset {Mm(pick.Value)} mm passed on its own numbers; NOT selected while a control fails)" : "")}. Not an X1 failure; td decides the re-run.");
                foreach (var x in reasons) s.AppendLine("  VOID reason: " + x);
            }
            else if (pick.HasValue) s.AppendLine($"P0 VERDICT: SELECTED HULL_INSET = {Mm(pick.Value)} mm (the smallest outward inset with LEAK 0 and K-a 100% closed). X1 goes on to P1.");
            else
            {
                s.AppendLine("P0 VERDICT: X1 FAILS: no inset has LEAK 0 with K-a 100% closed (and the known spots and #2499/#16175 closed). The plan moves to X2 (td-49 B35 D).");
                if (order.Any(x => passNoAo[x]))   // evidence only, never part of the verdict: no record rules on SSAO
                    s.AppendLine($"SSAO NOTE (evidence, td to rule; not a verdict): passing on LEAK-noAO (hull out of the SSAO input, no-AO twin mask equal to the hull-ON mask in every frame): [{string.Join(", ", order.Where(x => passNoAo[x]).Select(x => Mm(x) + " mm"))}].");
            }
            return s.ToString();
        }

        /// <summary>eng_51: the run-level fail-closed reasons (the fail-fast preflight, the rest read, the K-f map), each printed; empty = all held.</summary>
        List<string> RunVoidReasons()
        {
            var g = new List<string>();
            if (preflightVoid != null) g.AddRange(preflightVoid.Select(x => "preflight (fail fast): " + x));
            if (rest == null) g.Add("the rest mesh was never read (no Survivor_Body view renderer reached the preflight)");
            else
            {
                g.AddRange(rest.fail.Select(x => "rest mesh read: " + x));
                if (rest.v.Length == 0) g.Add("the mesh read returned 0 vertices");
            }
            if (kfMapped < 0) g.Add("K-f map not computed");
            else
            {
                if (kfUnmapped > 0) g.Add($"K-f map: {kfUnmapped} K-f rows UNMAPPED");
                if (kfAmbiguous > 0) g.Add($"K-f map: {kfAmbiguous} K-f rows AMBIGUOUS");
                if (kfMapped != KfRows.Length) g.Add($"K-f map: {kfMapped} of {KfRows.Length} K-f rows MAPPED");
                if (kfVerts == 0) g.Add("K-f map: the mesh read returned 0 vertices");
            }
            return g.Distinct().ToList();
        }

        void DestroyHullObjects()
        {
            foreach (var h in hulls) { if (h.r != null) Object.Destroy(h.r.gameObject); if (h.mesh != null) Object.Destroy(h.mesh); }
            hulls.Clear();
        }

        void DestroyHulls()
        {
            DestroyHullObjects();
            if (hullMat != null) Object.Destroy(hullMat);
            hullMat = null;
        }
    }
}
