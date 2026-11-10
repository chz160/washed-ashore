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
    /// PRINT ONLY, [Explicit], read-only (td-49 B21b E1-E5, for ta-49's D1 in Unity's OWN index space, which is what the GPU skins): the two see-through pinhole triangles of Survivor_Body, 20333 and 21587. The triangle index is the index into
    /// mesh.triangles divided by 3 (Unity reorders triangles and vertices on import, so it is not the FBX polygon index). E1: the sharedMesh counts are printed and asserted (35,940 vertices, 23,791 triangles). E2: per triangle the three vertex indices, rest positions
    /// (mesh space, 6 decimals and round trip), the centre, normals, UVs and the full bone weights with bone names. E3: for each of the 6 vertices every other vertex within 1e-5 of its position with its index, its weights and the max weight difference;
    /// "WEIGHT MISMATCH ON A POSITION DUPLICATE" when any bone differs by more than 1e-4. E4: for each edge of the two triangles the triangles sharing that edge BY POSITION (1e-5; 0 other triangles = an OPEN edge, a crack; 1 = closed; more = non-manifold), and any T-junction vertex
    /// lying inside the edge within 1e-5 of the segment. E5: written for ta-49 (map to Blender by position) and td-49 to TestResults/player-character/runs/RUN/rest-print/survivor-triangles-rest.txt (RUN is PC_RUN, else "rest-print") and logged. Nothing is changed.
    /// </summary>
    public class SurvivorRestPrint
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        // Unity mesh space = file space x -0.01, y 0.01, z 0.01 (qa-49 12jr: the imported mesh is 1/100 of the file metres), so 1e-5 FILE metres (the weld qa uses) = 1e-7 mesh units.
        const float PosTol = 1e-7f, WeightTol = 1e-4f;
        static Vector3 ToFile(Vector3 u) => new Vector3(-100f * u.x, 100f * u.y, 100f * u.z);
        static string R(Vector3 v) => v.x.ToString("R", Inv) + ", " + v.y.ToString("R", Inv) + ", " + v.z.ToString("R", Inv);
        static string F6(Vector3 v) => v.x.ToString("F6", Inv) + ", " + v.y.ToString("F6", Inv) + ", " + v.z.ToString("F6", Inv);

        Mesh mesh; string[] boneNames; Vector3[] v, n; Vector2[] uv; int[] tri; BoneWeight1[] all; byte[] per; int[] first;

        Dictionary<int, float> W(int vi)
        {
            var d = new Dictionary<int, float>();
            for (int k = 0; k < per[vi]; k++) { var w = all[first[vi] + k]; d[w.boneIndex] = (d.TryGetValue(w.boneIndex, out float cur) ? cur : 0f) + w.weight; }
            return d;
        }

        string WText(int vi) => string.Join(" ", W(vi).OrderByDescending(kv => kv.Value).Select(kv => $"{(kv.Key >= 0 && kv.Key < boneNames.Length ? boneNames[kv.Key] : "?")}#{kv.Key}:{kv.Value.ToString("F5", Inv)}"));

        float MaxDiff(int a, int b)
        {
            var wa = W(a); var wb = W(b); float m = 0f;
            foreach (int bone in wa.Keys.Union(wb.Keys)) m = Mathf.Max(m, Mathf.Abs((wa.TryGetValue(bone, out float x) ? x : 0f) - (wb.TryGetValue(bone, out float y) ? y : 0f)));
            return m;
        }

        static bool Same(Vector3 p, Vector3 q) => (p - q).sqrMagnitude <= PosTol * PosTol;
        static string UvText(Vector2 u) => $"({u.x.ToString("F6", Inv)}, {u.y.ToString("F6", Inv)})";

        void PrintEdges(StringBuilder text, int t, int[] vi3)
        {
            for (int e = 0; e < 3; e++)
            {
                int a = vi3[e], b = vi3[(e + 1) % 3];
                Vector3 pa = v[a], pb = v[b];
                var sharing = new List<int>();
                for (int tt = 0; tt < tri.Length / 3; tt++)
                {
                    if (tt == t) continue;
                    for (int k = 0; k < 3; k++)
                    {
                        Vector3 qa = v[tri[3 * tt + k]], qb = v[tri[3 * tt + (k + 1) % 3]];
                        if ((Same(qa, pa) && Same(qb, pb)) || (Same(qa, pb) && Same(qb, pa))) { sharing.Add(tt); break; }
                    }
                }
                string state = sharing.Count == 0 ? "OPEN EDGE (crack)" : sharing.Count == 1 ? "closed" : "NON-MANIFOLD";
                text.AppendLine($"   E4 edge vertices {a}-{b} (positions {F6(pa)} to {F6(pb)}): {sharing.Count} other triangle(s) share it by position {(sharing.Count > 0 ? "[" + string.Join(", ", sharing) + "]" : "")} -> {state}");
                Vector3 ab = pb - pa; float len2 = ab.sqrMagnitude;
                var tj = new List<string>();
                if (len2 > 1e-14f)
                    for (int o = 0; o < v.Length; o++)
                    {
                        if (Same(v[o], pa) || Same(v[o], pb)) continue;
                        float s = Vector3.Dot(v[o] - pa, ab) / len2;
                        if (s <= 0f || s >= 1f) continue;
                        if ((pa + ab * s - v[o]).magnitude <= PosTol) tj.Add($"{o} at s {s.ToString("F4", Inv)}");
                    }
                text.AppendLine($"      T-junction vertices lying inside this edge within {PosTol:E0} mesh units (1e-5 file metres): {(tj.Count == 0 ? "none" : string.Join(", ", tj))}");
            }
        }

        /// <summary>
        /// td-49 B22 E6: EVERY open-edge loop of Unity's Survivor_Body mesh. Vertices are welded by position (1e-5, with a union over neighbouring grid cells); an edge used by exactly one triangle after welding is open; open edges are grouped into connected loops by shared welded
        /// vertices. Per loop: id, welded vertex count, open edge count, centre (mean of the welded positions), bounding-box extent, the region of the loop's most common dominant bone (BarOneRule.Region), a "MISSING TRIANGLE (3-vertex loop)" flag when it has 3 vertices and 3 edges,
        /// and up to 12 triangle indices touching it. Then, for each of the two pinhole triangles, the nearest loop by distance from the triangle centre to the loop's nearest vertex.
        /// </summary>
        void PrintOpenLoops(StringBuilder text, int[] pinholeTris)
        {
            int nv = v.Length;
            var parent = new int[nv];
            for (int i = 0; i < nv; i++) parent[i] = i;
            int Find(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
            void Union(int a, int b) { a = Find(a); b = Find(b); if (a != b) parent[b] = a; }
            var cells = new Dictionary<Vector3Int, List<int>>();
            Vector3Int Cell(Vector3 p) => new Vector3Int(Mathf.FloorToInt(p.x / PosTol), Mathf.FloorToInt(p.y / PosTol), Mathf.FloorToInt(p.z / PosTol));
            for (int i = 0; i < nv; i++) { var c = Cell(v[i]); if (!cells.TryGetValue(c, out var l)) cells[c] = l = new List<int>(); l.Add(i); }
            for (int i = 0; i < nv; i++)
            {
                var c = Cell(v[i]);
                for (int dx = -1; dx <= 1; dx++) for (int dy = -1; dy <= 1; dy++) for (int dz = -1; dz <= 1; dz++)
                    if (cells.TryGetValue(new Vector3Int(c.x + dx, c.y + dy, c.z + dz), out var l)) foreach (int o in l) if (o > i && Same(v[o], v[i])) Union(i, o);
            }
            var edgeCount = new Dictionary<(int, int), int>();
            var edgeTris = new Dictionary<(int, int), List<int>>();
            for (int t = 0; t < tri.Length / 3; t++)
                for (int k = 0; k < 3; k++)
                {
                    int a = Find(tri[3 * t + k]), b = Find(tri[3 * t + (k + 1) % 3]);
                    if (a == b) continue;
                    var key = a < b ? (a, b) : (b, a);
                    edgeCount[key] = edgeCount.TryGetValue(key, out int cnt) ? cnt + 1 : 1;
                    if (!edgeTris.TryGetValue(key, out var tl)) edgeTris[key] = tl = new List<int>();
                    tl.Add(t);
                }
            var open = edgeCount.Where(kv => kv.Value == 1).Select(kv => kv.Key).ToList();
            // loops = connected components of the open edges over welded vertices
            var lp = new Dictionary<int, int>();
            int LFind(int x) { if (!lp.ContainsKey(x)) lp[x] = x; while (lp[x] != x) { lp[x] = lp[lp[x]]; x = lp[x]; } return x; }
            foreach (var (a, b) in open) { int ra = LFind(a), rb = LFind(b); if (ra != rb) lp[rb] = ra; }
            var groups = open.GroupBy(e => LFind(e.Item1)).ToList();
            text.AppendLine();
            text.AppendLine($"E6 open-edge loops of the Unity mesh (weld {PosTol:E0} mesh units = 1e-5 file metres): {open.Count} open edges in {groups.Count} loops (mesh vertices {nv}, welded {Enumerable.Range(0, nv).Select(Find).Distinct().Count()})");
            var loops = new List<(int id, int verts, int edges, Vector3 centre, Vector3 extent, string region, bool three, List<int> tris, List<Vector3> pts)>();
            int idn = 0;
            foreach (var g in groups.OrderBy(g => g.Select(e => e.Item1).Union(g.Select(e => e.Item2)).Distinct().Count()))
            {
                var vs = g.Select(e => e.Item1).Union(g.Select(e => e.Item2)).Distinct().ToList();
                var pts = vs.Select(w => v[w]).ToList();
                Vector3 mn = pts[0], mx = pts[0], sum = Vector3.zero;
                foreach (var p in pts) { mn = Vector3.Min(mn, p); mx = Vector3.Max(mx, p); sum += p; }
                var bones = new Dictionary<string, int>();
                foreach (int w in vs)
                {
                    var wd = W(w); if (wd.Count == 0) continue;
                    int top = wd.OrderByDescending(kv => kv.Value).First().Key;
                    string nm = top >= 0 && top < boneNames.Length ? boneNames[top] : "?";
                    bones[nm] = bones.TryGetValue(nm, out int c0) ? c0 + 1 : 1;
                }
                string dom = bones.Count == 0 ? "?" : bones.OrderByDescending(kv => kv.Value).First().Key;
                var tl = g.SelectMany(e => edgeTris[e]).Distinct().OrderBy(x => x).ToList();
                loops.Add((++idn, vs.Count, g.Count(), sum / pts.Count, mx - mn, BarOneRule.Region(dom) + " (dominant bone " + dom + ")", vs.Count == 3 && g.Count() == 3, tl, pts));
            }
            foreach (var l in loops)
                text.AppendLine($"   loop {l.id}: vertices {l.verts}, open edges {l.edges}, centre (Unity mesh space) {F6(l.centre)}, centre (file space, metres) {F6(ToFile(l.centre))}, extent (mesh) {F6(l.extent)}, extent (file) {F6(new Vector3(100f * l.extent.x, 100f * l.extent.y, 100f * l.extent.z))}, region {l.region}{(l.three ? "   MISSING TRIANGLE (3-vertex loop)" : "")}; triangles touching it: {string.Join(", ", l.tris.Take(12))}{(l.tris.Count > 12 ? ", ..." : "")}");
            foreach (int t in pinholeTris)
            {
                var centre = (v[tri[3 * t]] + v[tri[3 * t + 1]] + v[tri[3 * t + 2]]) / 3f;
                if (loops.Count == 0) { text.AppendLine($"   nearest loop to triangle {t}: none (the mesh has no open edge)"); continue; }
                var best = loops.OrderBy(l => l.pts.Min(p => (p - centre).magnitude)).First();
                text.AppendLine($"   nearest loop to triangle {t} (rest centre, Unity mesh space {F6(centre)}; file space {F6(ToFile(centre))}): loop {best.id}, {best.pts.Min(p => (p - centre).magnitude).ToString("F6", Inv)} mesh units = {(100f * best.pts.Min(p => (p - centre).magnitude)).ToString("F4", Inv)} file metres from its nearest vertex ({best.verts} vertices, region {best.region}{(best.three ? ", MISSING TRIANGLE" : "")})");
            }
        }

        [Test, Explicit("Print only: rest data, weights, position duplicates, edge sharing and T-junctions of Survivor_Body triangles 20333 and 21587")]
        public void PrintRestOfTheTwoPinholeTriangles()
        {
            var text = new StringBuilder("Survivor_Body rest data, Unity mesh space (the mesh's own units; the renderer's lossyScale is about 101.56 in the player)" + System.Environment.NewLine);
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(SurvivorAssetBuilder.BodyPath);
            Assert.IsNotNull(go, "the Survivor body asset did not load: " + SurvivorAssetBuilder.BodyPath);
            foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (smr.sharedMesh != null && smr.sharedMesh.name == "Survivor_Body") { mesh = smr.sharedMesh; boneNames = smr.bones.Select(b => b != null ? b.name : "?").ToArray(); break; }
            Assert.IsNotNull(mesh, "no mesh named Survivor_Body in " + SurvivorAssetBuilder.BodyPath);
            v = mesh.vertices; n = mesh.normals; uv = mesh.uv; tri = mesh.triangles;
            all = mesh.GetAllBoneWeights().ToArray(); per = mesh.GetBonesPerVertex().ToArray();
            text.AppendLine($"E1 counts: vertices {v.Length}, triangles {tri.Length / 3}, normals {n.Length}, uvs {uv.Length}, bindposes {mesh.bindposeCount}, bones {boneNames.Length}");
            { uint fh = 2166136261u; unchecked { foreach (int x in tri) { uint u = (uint)x; for (int b = 0; b < 4; b++) { fh ^= (u >> (8 * b)) & 0xFFu; fh *= 16777619u; } } } text.AppendLine($"T1 asset mesh '{mesh.name}' instanceID {mesh.GetEntityId()} isReadable {mesh.isReadable} triangles {tri.Length / 3} FNV-1a (32 bit, the 4 little-endian bytes of every triangle index) {fh:x8}"); }
            Assert.AreEqual(35940, v.Length, "E1: the vertex count of the Survivor_Body mesh");
            Assert.AreEqual(23791, tri.Length / 3, "E1: the triangle count of the Survivor_Body mesh");
            first = new int[v.Length]; int run = 0;
            for (int i = 0; i < v.Length; i++) { first[i] = run; run += per[i]; }

            foreach (int t in new[] { 20333, 21587 })
            {
                text.AppendLine();
                Assert.Less(3 * t + 2, tri.Length, "triangle index out of range: " + t);
                int[] vi3 = { tri[3 * t], tri[3 * t + 1], tri[3 * t + 2] };
                var centre = (v[vi3[0]] + v[vi3[1]] + v[vi3[2]]) / 3f;
                text.AppendLine($"== triangle {t} (Unity index space): vertex indices {vi3[0]}, {vi3[1]}, {vi3[2]}");
                text.AppendLine($"   E2 rest centre (6 decimals): {F6(centre)}; (R): {R(centre)}; file space (metres) {F6(ToFile(centre))}");
                // td-49 B23 sliver check: the triangle's area and its shortest edge, in mesh units and in file metres
                {
                    Vector3 e0 = v[vi3[1]] - v[vi3[0]], e1 = v[vi3[2]] - v[vi3[1]], e2 = v[vi3[0]] - v[vi3[2]];
                    float area = 0.5f * Vector3.Cross(e0, -e2).magnitude, shortest = Mathf.Min(e0.magnitude, Mathf.Min(e1.magnitude, e2.magnitude));
                    text.AppendLine($"   E2 sliver check: area {area.ToString("E4", Inv)} mesh units^2 = {(area * 1e4f).ToString("E4", Inv)} file metres^2; shortest edge {shortest.ToString("E4", Inv)} mesh units = {(shortest * 100f).ToString("F6", Inv)} file metres; edges (file metres) {(e0.magnitude * 100f).ToString("F6", Inv)}, {(e1.magnitude * 100f).ToString("F6", Inv)}, {(e2.magnitude * 100f).ToString("F6", Inv)}");
                }
                foreach (int vi in vi3)
                {
                    text.AppendLine($"   E2 vertex {vi}: rest position (6 decimals) {F6(v[vi])}; (R) {R(v[vi])}; normal {F6(n[vi])}; uv {UvText(uv[vi])}");
                    text.AppendLine($"      weights: {WText(vi)}");
                    var dup = new List<int>();
                    for (int o = 0; o < v.Length; o++) if (o != vi && Same(v[o], v[vi])) dup.Add(o);
                    text.AppendLine($"   E3 other vertices within {PosTol:E0} mesh units (1e-5 file metres) of vertex {vi}: {(dup.Count == 0 ? "none" : string.Join(", ", dup))}");
                    foreach (int o in dup)
                    {
                        float md = MaxDiff(vi, o);
                        text.AppendLine($"      vertex {o}: normal {F6(n[o])}; uv {UvText(uv[o])}; weights: {WText(o)}; max weight difference {md.ToString("F6", Inv)}{(md > WeightTol ? "   WEIGHT MISMATCH ON A POSITION DUPLICATE" : "")}");
                    }
                }
                PrintEdges(text, t, vi3);
            }
            PrintOpenLoops(text, new[] { 20333, 21587 });
            string run0 = System.Environment.GetEnvironmentVariable("PC_RUN");
            string dir = $"TestResults/player-character/runs/{(string.IsNullOrEmpty(run0) ? "rest-print" : run0)}/rest-print";
            Directory.CreateDirectory(dir);
            File.WriteAllText($"{dir}/survivor-triangles-rest.txt", text.ToString());
            Debug.Log(text.ToString());
        }

        /// <summary>
        /// PRINT ONLY, [Explicit], read-only (td-49 B32 item 4, for ta-49): the rest centre (Unity mesh space and file space, 6 decimals) and the dominant bones per vertex of a LIST of Unity triangles of Survivor_Body. The list is the environment variable REST_TRIS (comma separated),
        /// default 3415, 3553, 21202, 3642, 3664, 3665, 21348 (the layers around the two pinholes). Written to TestResults/player-character/runs/RUN/rest-print/survivor-triangles-centres.txt (RUN is PC_RUN, else "rest-print"). Nothing is changed.
        /// </summary>
        [Test, Explicit("Print only: rest centres and dominant bones of a list of Survivor_Body triangles (REST_TRIS)")]
        public void PrintRestCentresOfAList()
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(SurvivorAssetBuilder.BodyPath);
            Assert.IsNotNull(go, "the Survivor body asset did not load: " + SurvivorAssetBuilder.BodyPath);
            foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (smr.sharedMesh != null && smr.sharedMesh.name == "Survivor_Body") { mesh = smr.sharedMesh; boneNames = smr.bones.Select(b => b != null ? b.name : "?").ToArray(); break; }
            Assert.IsNotNull(mesh, "no mesh named Survivor_Body in " + SurvivorAssetBuilder.BodyPath);
            v = mesh.vertices; tri = mesh.triangles;
            all = mesh.GetAllBoneWeights().ToArray(); per = mesh.GetBonesPerVertex().ToArray();
            first = new int[v.Length]; int run = 0;
            for (int i = 0; i < v.Length; i++) { first[i] = run; run += per[i]; }
            string list = System.Environment.GetEnvironmentVariable("REST_TRIS");
            var ids = (string.IsNullOrEmpty(list) ? "3415,3553,21202,3642,3664,3665,21348" : list).Split(',').Select(x => int.Parse(x.Trim(), Inv)).ToArray();
            var text = new StringBuilder("Survivor_Body rest centres of a list of Unity triangles (mesh space = the mesh's own units; file space = x -100, y 100, z 100 times mesh space, metres)" + System.Environment.NewLine);
            text.AppendLine($"mesh vertices {v.Length}, triangles {tri.Length / 3}, list {string.Join(", ", ids)}");
            Assert.AreEqual(23791, tri.Length / 3, "the triangle count of the Survivor_Body mesh");
            foreach (int t in ids)
            {
                Assert.GreaterOrEqual(t, 0, "triangle index: " + t);
                Assert.Less(3 * t + 2, tri.Length, "triangle index out of range: " + t);
                int[] vi3 = { tri[3 * t], tri[3 * t + 1], tri[3 * t + 2] };
                var centre = (v[vi3[0]] + v[vi3[1]] + v[vi3[2]]) / 3f;
                text.AppendLine($"== triangle {t}: vertex indices {vi3[0]}, {vi3[1]}, {vi3[2]}; rest centre mesh space {F6(centre)}; file space {F6(ToFile(centre))}");
                foreach (int vi in vi3) text.AppendLine($"   vertex {vi}: rest position mesh {F6(v[vi])}; file {F6(ToFile(v[vi]))}; weights {WText(vi)}");
            }
            string run0 = System.Environment.GetEnvironmentVariable("PC_RUN");
            string dir = $"TestResults/player-character/runs/{(string.IsNullOrEmpty(run0) ? "rest-print" : run0)}/rest-print";
            Directory.CreateDirectory(dir);
            File.WriteAllText($"{dir}/survivor-triangles-centres.txt", text.ToString());
            Debug.Log(text.ToString());
        }
    }
}
