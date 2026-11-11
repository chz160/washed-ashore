using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using UnityEngine;

namespace WashedAshore.Tests.PlayMode.Character
{
    /// <summary>
    /// X1 P0 classifier half of the S2 print (HairlineGapSizing, partial class), TEST-ONLY. Every H1 component is attributed (td B35) and given ONE K-class in the order of record
    /// (td B35b = director gate/pod49-s2-b20-classes-ruling-note2-kc-order, settled by td B35c):
    ///   1. K-h: both framing edges' triangles are hand-dominant (Wrist or a finger bone) -> non-defect;
    ///   2. K-f: either framing edge has a triangle on the intended fin/fringe list (19 FBX polygons = 18 fin flaps + loop 19's two faces; loop 23 EXCLUDED, director 04:55Z) -> non-defect;
    ///   3. separation of the two framing edges &lt;= 60 mm -> K-a, DEFECT, whether open at 4x or not;
    ///   4. separation &gt; 60 mm -> K-b, non-defect (60-100 mm flagged borderline; every K-b is cropped for the director, borderline ones by name);
    ///   5. no framing edge 2, or no attribution -> K-a, DEFECT (fail-closed; "K-a = 0" includes these rows, qa F-4).
    /// "Open at 4x" is printed as evidence only. The K-f map (td B35c) is computed here at bake time from ta-49's file-space corners: a Unity triangle belongs to polygon P when all 3 of its rest vertices
    /// (the rest mesh read by RestOf without Read/Write, eng_51; ToFile = (-100x, 100y, 100z), SurvivorRestPrint.cs:26) lie within 1e-5 file metres of a corner of P; 0 matches = UNMAPPED and more than n-2 = AMBIGUOUS, both contributing nothing (fail-closed).
    /// Targets (td B34e, B35c) are found by their b20 framing-edge triangles, never by component number; each gets a lit 4x crop (plain + marked) and a lit 1x 40x40 window, or "target not found".
    /// </summary>
    public partial class HairlineGapSizing
    {
        const double KaSepMm = 60.0, BorderlineMm = 100.0, KfTolFile = 1e-5;
        const int MaxKbCrops = 60;
        const string KRuleText = "K-class order of record (td B35b, director note2; Noah accepted the classes ruling 2026-11-10): 1 K-h both framing edges hand-dominant = non-defect; 2 K-f an edge on an intended fin/fringe triangle (19 polygons, loop 23 excluded) = non-defect; 3 separation <= 60 mm = K-a DEFECT (open at 4x or not); 4 > 60 mm = K-b non-defect (60-100 mm borderline, cropped); 5 no edge 2 or no attribution = K-a DEFECT. Open at 4x = evidence only.";

        class KCount { public int ka, kaNoE2, kb, kbBorder, kf, kh, open4; }

        /// <summary>A hand bone of the Survivor rig: Wrist (the mitten fingers are weighted to it) or a finger bone; *_end and the .L/.R suffix are ignored. (eng_49 draft, reviewed.)</summary>
        static bool IsHandBone(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            string s = name.EndsWith("_end") ? name.Substring(0, name.Length - 4) : name;
            if (s.EndsWith(".L") || s.EndsWith(".R")) s = s.Substring(0, s.Length - 2);
            return s == "Wrist" || s == "Hand" || s.StartsWith("Thumb") || s.StartsWith("Index") || s.StartsWith("Middle") || s.StartsWith("Ring") || s.StartsWith("Pinky");
        }

        // ---- K-f: ta-49's intended fin/fringe triangles, FILE space metres, 6 decimals (corner0, corner1, corner2) ----
        // Source: pod49\out\k_f_triangles.txt by ta-49 (scripted Blender, read-only, from Survivor_Body.fbx git blob 459fa2f6), 22 rows; on disk raw blob cceb9749 (written 2026-11-09 04:55:21Z).
        // The 20 rows below are the 19 K-f polygons: the 18 fin flaps (loops 0, 3, 4, 5, 6, 7, 9, 10, 12, 13, 14, 15, 17, 18, 20, 21, 22, 26) and loop 19's ONE quad flap as its two triangles (FBX 12600 + 12737).
        // The two loop-23 rows (boot-top rim, FBX 4663 + 4709) are NOT K-f (director 04:55Z); they are mapped below for the print only and never enter the K-f set.
        static readonly (int loop, int poly, double[] c)[] KfRows =
        {
            (0, 17771, new[] { 0.238824, -0.067882, 1.288741, 0.235716, -0.081788, 1.295713, 0.241522, -0.066758, 1.296226 }),
            (3, 5666, new[] { -0.150958, -0.044484, 1.121311, -0.149553, -0.039969, 1.121682, -0.151070, -0.042203, 1.125341 }),
            (4, 10900, new[] { -0.040365, 0.070154, 1.118196, -0.043389, 0.070367, 1.111925, -0.036823, 0.070797, 1.120428 }),
            (5, 9775, new[] { -0.066392, 0.066712, 1.111319, -0.066210, 0.065544, 1.117395, -0.068664, 0.068140, 1.119199 }),
            (6, 13926, new[] { 0.087719, 0.094676, 1.109651, 0.094932, 0.094119, 1.111153, 0.090574, 0.093810, 1.116611 }),
            (7, 5950, new[] { -0.151023, -0.056134, 1.102123, -0.150140, -0.054435, 1.103989, -0.147111, -0.056370, 1.100563 }),
            (9, 12773, new[] { 0.069273, -0.122284, 1.056489, 0.061171, -0.120905, 1.065583, 0.059418, -0.119130, 1.072765 }),
            (10, 14036, new[] { 0.090868, 0.036066, 1.056269, 0.083741, 0.035473, 1.063805, 0.098897, 0.034994, 1.054218 }),
            (12, 15978, new[] { 0.141680, 0.058406, 1.045412, 0.132115, 0.060862, 1.045946, 0.129875, 0.063121, 1.044569 }),
            (13, 6785, new[] { -0.136167, -0.018081, 1.038029, -0.135473, -0.028021, 1.044355, -0.130997, -0.005827, 1.043265 }),
            (14, 12511, new[] { 0.057221, 0.069449, 1.035976, 0.050667, 0.066612, 1.039192, 0.069241, 0.068793, 1.033921 }),
            (15, 9136, new[] { -0.077581, 0.066026, 1.030189, -0.078358, 0.064055, 1.028217, -0.077927, 0.068537, 1.022610 }),
            (17, 13915, new[] { 0.093261, 0.059630, 1.006874, 0.084253, 0.061872, 1.009842, 0.080734, 0.063722, 1.007177 }),
            (18, 11044, new[] { -0.025255, 0.074880, 0.967575, -0.026561, 0.068215, 0.997225, -0.024050, 0.072493, 0.978060 }),
            (20, 20742, new[] { 0.157645, -0.042993, 0.654062, 0.155951, -0.041546, 0.648378, 0.161238, -0.038255, 0.642137 }),
            (21, 19565, new[] { 0.127153, -0.117868, 0.572376, 0.125343, -0.114072, 0.573595, 0.130630, -0.117262, 0.574701 }),
            (22, 17227, new[] { 0.086681, -0.046066, 0.205444, 0.109547, -0.043673, 0.202588, 0.100551, -0.045246, 0.203805 }),
            (26, 14534, new[] { 0.056371, 0.038841, 0.003805, 0.064984, 0.033846, 0.002842, 0.068711, 0.025294, 0.001713 }),
            (19, 12600, new[] { 0.053475, 0.073187, 0.985273, 0.066412, 0.071153, 0.990665, 0.056883, 0.071985, 0.989846 }),
            (19, 12737, new[] { 0.066412, 0.071153, 0.990665, 0.048614, 0.075370, 0.977127, 0.053475, 0.073187, 0.985273 }),
        };
        static readonly (int loop, int poly, double[] c)[] KfExcludedRows =
        {
            (23, 4663, new[] { -0.105967, -0.046556, 0.180284, -0.106885, -0.048879, 0.178083, -0.108252, -0.043416, 0.177401 }),
            (23, 4709, new[] { -0.106885, -0.048879, 0.178083, -0.105967, -0.046556, 0.180284, -0.107010, -0.045494, 0.176600 }),
        };
        /// <summary>ta-49's Unity cross-check table, pod49\out\unity_rim_tris.txt (raw blob eaf86bf5), by ta's loop number. Evidence only: the in-test map is the one used.</summary>
        static readonly Dictionary<int, int[]> KfTaUnity = new Dictionary<int, int[]>
        {
            { 0, new[] { 19882 } }, { 3, new[] { 928 } }, { 4, new[] { 1963 } }, { 5, new[] { 1741 } }, { 6, new[] { 2397 } }, { 7, new[] { 9921 } }, { 9, new[] { 15396 } }, { 10, new[] { 16391 } },
            { 12, new[] { 2779 } }, { 13, new[] { 10712 } }, { 14, new[] { 15124 } }, { 15, new[] { 1632 } }, { 17, new[] { 15028 } }, { 18, new[] { 12471 } }, { 20, new[] { 3623 } }, { 21, new[] { 3433 } },
            { 22, new[] { 3017 } }, { 26, new[] { 16320 } }, { 19, new[] { 2210, 14143 } }, { 23, new[] { 719, 8889 } },
        };

        HashSet<int> kfSet = new HashSet<int>();   // EMPTY until the map is computed: fail-closed, those rows fall to K-a
        Mesh kfMesh; string kfMapText = "";
        int kfMapped = -1, kfUnmapped = -1, kfAmbiguous = -1, kfVerts = -1;   // -1 = not computed (eng_51: read by the fail-closed verdict and the preflight)

        /// <summary>td B35c: maps each K-f row to Unity triangles of the rest mesh (once per mesh), prints the map, and sets <see cref="kfSet"/> to the triangles of the MAPPED rows only.
        /// eng_51: the rest vertices and triangles come from <see cref="RestOf"/> (the shared mesh is not readable in PlayMode, so mesh.vertices and mesh.triangles were empty in baton 21).</summary>
        void EnsureKfMap(SkinnedMeshRenderer smr)
        {
            var rm = RestOf(smr);
            var mesh = rm.src;
            if (kfMesh == mesh) return;
            kfMesh = mesh;
            var v = rm.v; var t = rm.tri;
            var rows = KfRows.Select(r => (r.loop, r.poly, r.c, used: true)).Concat(KfExcludedRows.Select(r => (r.loop, r.poly, r.c, used: false))).ToArray();
            var near = new Dictionary<int, HashSet<int>>();   // vertex -> rows with a corner within 1e-5 file metres
            for (int vi = 0; vi < v.Length; vi++)
            {
                double fx = -100.0 * v[vi].x, fy = 100.0 * v[vi].y, fz = 100.0 * v[vi].z;   // ToFile, SurvivorRestPrint.cs:26
                for (int r = 0; r < rows.Length; r++)
                    for (int k = 0; k < 3; k++)
                    {
                        double dx = fx - rows[r].c[3 * k], dy = fy - rows[r].c[3 * k + 1], dz = fz - rows[r].c[3 * k + 2];
                        if (dx * dx + dy * dy + dz * dz <= KfTolFile * KfTolFile) { if (!near.TryGetValue(vi, out var set)) near[vi] = set = new HashSet<int>(); set.Add(r); }
                    }
            }
            var map = rows.Select(_ => new List<int>()).ToArray();
            for (int i = 0; i < t.Length; i += 3)
            {
                if (!near.TryGetValue(t[i], out var r0) || !near.TryGetValue(t[i + 1], out var r1) || !near.TryGetValue(t[i + 2], out var r2)) continue;
                foreach (int r in r0) if (r1.Contains(r) && r2.Contains(r)) map[r].Add(i / 3);
            }
            var o = new StringBuilder();
            o.AppendLine($"K-f source: ta-49 pod49\\out\\k_f_triangles.txt (22 rows; raw blob on disk cceb9749, the record cites 14c32986), Survivor_Body.fbx 459fa2f6; rule td B35c: all 3 rest vertices of a Unity triangle within {KfTolFile:0e+0} file metres (ToFile (-100x, 100y, 100z)) of corners of the polygon; 0 = UNMAPPED, > n-2 = AMBIGUOUS (fail-closed); mesh '{mesh.name}', {v.Length} vertices, {t.Length / 3} triangles (eng_51 rest read: {(rm.fail.Count == 0 ? "OK" : "FAILED: " + string.Join("; ", rm.fail))})");
            var set2 = new HashSet<int>(); int mapped = 0, unmapped = 0, ambiguous = 0;
            var byLoop = new SortedDictionary<int, SortedSet<int>>();
            for (int r = 0; r < rows.Length; r++)
            {
                string st = map[r].Count == 0 ? "UNMAPPED" : map[r].Count > 1 ? "AMBIGUOUS" : "MAPPED";   // every row is a triangle (n = 3), so n - 2 = 1
                if (rows[r].used) { if (st == "MAPPED") { mapped++; set2.UnionWith(map[r]); } else if (st == "UNMAPPED") unmapped++; else ambiguous++; }
                if (st == "MAPPED") { if (!byLoop.TryGetValue(rows[r].loop, out var bl)) byLoop[rows[r].loop] = bl = new SortedSet<int>(); bl.UnionWith(map[r]); }
                o.AppendLine($"  {(rows[r].used ? "K-f" : "EXCLUDED (director 04:55Z, loop 23 boot-top rim, not K-f)")} loop {rows[r].loop} FBX polygon {rows[r].poly}: Unity triangles [{string.Join(", ", map[r])}] {st}");
            }
            kfSet = set2;
            kfMapped = mapped; kfUnmapped = unmapped; kfAmbiguous = ambiguous; kfVerts = v.Length;
            o.AppendLine($"K-f set USED: {kfSet.Count} Unity triangles [{string.Join(", ", kfSet.OrderBy(x => x))}] from {mapped} MAPPED of {KfRows.Length} rows (19 polygons); UNMAPPED {unmapped}, AMBIGUOUS {ambiguous} (their K-f rows fall to K-a){(mapped == KfRows.Length && v.Length > 0 ? "" : "; K-f MAP INCOMPLETE: the P0 verdict is NO VERDICT (VOID) (eng_51 fail-closed)")}");
            var diffs = new List<string>();
            foreach (var kv in KfTaUnity) { byLoop.TryGetValue(kv.Key, out var got); var g = got != null ? got.ToArray() : new int[0]; if (!g.SequenceEqual(kv.Value.OrderBy(x => x))) diffs.Add($"loop {kv.Key}: ta [{string.Join(", ", kv.Value)}], test [{string.Join(", ", g)}]"); }
            o.AppendLine(diffs.Count == 0 ? "cross-check with ta-49's unity_rim_tris.txt (eaf86bf5), all 20 loops incl. the excluded loop 23: AGREES" : "cross-check with ta-49's unity_rim_tris.txt (eaf86bf5): DIFFERS (evidence only; the test map above is used): " + string.Join("; ", diffs));
            kfMapText = o.ToString();
            comps.Append("== " + kfMapText);
            Debug.Log("X1 P0 K-f map:" + System.Environment.NewLine + kfMapText);
        }

        /// <summary>One H1 component's K-class in the order of record; counts it, prints the line, collects K-a components (for the hull pass) and crops every K-b (borderline by name).</summary>
        void ClassifyH1(string group, string point, float pitch, Comp c, Framing fr, bool open4, string region, Camera cam, KCount kc, List<Comp> kaComps)
        {
            var kfOn = fr.e1Big.Concat(fr.e2Big).Where(kfSet.Contains).Distinct().OrderBy(x => x).ToList();
            string kcl, rule; bool borderline = false;
            if (fr.has1 && fr.has2 && fr.b1.Count > 0 && fr.b2.Count > 0 && fr.b1.All(IsHandBone) && fr.b2.All(IsHandBone)) { kcl = "K-h"; rule = "rule 1: both framing edges hand-dominant"; kc.kh++; }
            else if (kfOn.Count > 0) { kcl = "K-f"; rule = $"rule 2: framing edge on intended fin/fringe triangle(s) [{string.Join(", ", kfOn)}]"; kc.kf++; }
            else if (fr.has2 && fr.sepMm <= KaSepMm) { kcl = "K-a"; rule = "rule 3: separation <= 60 mm"; kc.ka++; }
            else if (fr.has2) { kcl = "K-b"; borderline = fr.sepMm <= BorderlineMm; rule = "rule 4: separation > 60 mm" + (borderline ? " (BORDERLINE 60-100 mm)" : ""); kc.kb++; if (borderline) kc.kbBorder++; }
            else { kcl = "K-a"; rule = fr.has1 ? "rule 5: no framing edge 2" : "rule 5: no attribution (no framing edge)"; kc.kaNoE2++; }
            if (open4) kc.open4++;
            if (kcl == "K-a") kaComps.Add(c);
            comps.AppendLine($"    K-class {kcl}{(kcl == "K-a" ? ": DEFECT" : ": non-defect")} ({rule}); open at 4x {open4} (evidence only); edge 1 bones [{string.Join(", ", fr.b1.Distinct())}], edge 2 bones [{string.Join(", ", fr.b2.Distinct())}]; separation {(fr.has2 ? fr.sepMm.ToString("F2", Inv) + " mm (world)" : "none")}");
            if (kcl != "K-b") return;
            if (kbCrops.Count >= MaxKbCrops) { kbCropsOver++; return; }
            kbCrops.Add(WriteCrop(borderline ? "K-b-borderline" : "K-b", group, point, pitch, c, region + $" sep {fr.sepMm.ToString("F1", Inv)} mm", cam));
        }

        readonly List<Crop> kbCrops = new List<Crop>(), hullCrops = new List<Crop>();
        int kbCropsOver;

        // ---- targets, by their b20 framing-edge triangles (TestResults\player-character\runs\b20-s2\s2\s2-components.txt) ----
        class Target { public string slot, group, point, notFound; public float pitch; public int[] need; }
        static readonly Target[] Targets =
        {
            // td B34e: the K-a target, both #2499 and #16175 at the framing edges (b20 frame 2500: E1 [#2499 BACK LEG.L, #16175 FRONT LEG.L], E2 [#2545, #17297], 3.26 mm, 2 px); the director's twin stills spot
            new Target { slot = "K-a-target", group = "Walk1.5", point = "cycle point 5", pitch = 85f, need = new[] { 2499, 16175 }, notFound = "K-a target not found (Walk1.5, cycle point 5, +85, framing edges containing triangles 2499 and 16175): no substitute" },
            // qa 12mi/12mj, td B35c slot 1: b20 frame 1910 component 2, LEG.R, 2 px: E1 [#502 BACK LEG.R, #7958 FRONT LEG.R], E2 [#7460 BACK LEG.R, #7518 FRONT LEG.R], 4.69 mm, open at 4x
            new Target { slot = "QA-slot-1", group = "Walk1.15", point = "cycle point 7", pitch = 70f, need = new[] { 502, 7958, 7460, 7518 }, notFound = "QA slot 1 target not found (Walk1.15, cycle point 7, +70, LEG.R, b20 framing edges #502/#7958 + #7460/#7518, 2 px, 4.69 mm): no substitute" },
            // qa 12mi/12mj, td B35c slot 2: b20 frame 5064 component 3, LEG.L, 2 px: E1 [#20671 BACK LEG.L, #20672 FRONT LEG.L], E2 [#3343 FRONT LEG.L, #20793 BACK LEG.L], 6.47 mm, open at 4x
            new Target { slot = "QA-slot-2", group = "CrouchWalk1.75", point = "cycle point 2", pitch = 85f, need = new[] { 20671, 20672, 3343, 20793 }, notFound = "QA slot 2 target not found (CrouchWalk1.75, cycle point 2, +85, LEG.L, b20 framing edges #20671/#20672 + #3343/#20793, 2 px, 6.47 mm): no substitute" },
        };
        readonly Dictionary<string, Crop> targetCrop = new Dictionary<string, Crop>();
        readonly List<string> targetNear = new List<string>();

        /// <summary>A target matches when its frame key (group, point, pitch) matches and ALL its b20 triangles are at the framing edges; the largest such component in the frame wins. Partial matches are listed, never substituted.</summary>
        void MatchTargets(string group, string point, float pitch, Comp c, Framing fr, string region, Dictionary<string, (Comp c, string region)> frameTargets)
        {
            foreach (var t in Targets)
            {
                if (group != t.group || point != t.point || !Mathf.Approximately(pitch, t.pitch)) continue;
                var all = fr.All(); int have = t.need.Count(all.Contains);
                if (have == t.need.Length) { if (!frameTargets.TryGetValue(t.slot, out var cur) || c.m.Count > cur.c.m.Count) frameTargets[t.slot] = (c, region); }
                else if (have > 0) targetNear.Add($"{t.slot}: frame {Time.frameCount}, component size {c.m.Count} at ({c.cx:F1}, {c.cy:F1}), {have} of {t.need.Length} b20 triangles at its framing edges [{string.Join(", ", t.need.Where(all.Contains))}]");
            }
        }

        /// <summary>Writes each matched target's lit 4x crop (plain + marked) and lit 1x 40x40 window, in its frame, hull off.</summary>
        void WriteTargetCrops(string group, string point, float pitch, Dictionary<string, (Comp c, string region)> frameTargets, Color32[] lit1x, Camera cam)
        {
            foreach (var kv in frameTargets)
            {
                if (targetCrop.ContainsKey(kv.Key)) { comps.AppendLine($"  TARGET {kv.Key}: a second match in frame {Time.frameCount}; the first is kept"); continue; }
                var k = WriteCrop(kv.Key, group, point, pitch, kv.Value.c, kv.Value.region, cam);
                WriteWindow1x(k, lit1x, kv.Value.c);
                targetCrop[kv.Key] = k;
                comps.AppendLine($"  TARGET {kv.Key} found: component size {kv.Value.c.m.Count} at ({kv.Value.c.cx:F1}, {kv.Value.c.cy:F1}), region {kv.Value.region}; crops {k.f1}, {k.f2}, {k.f3}");
            }
        }
    }
}
