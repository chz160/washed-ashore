using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;
using WashedAshore.Character.Stance;
using WashedAshore.Gameplay;
using WashedAshore.Tests.Support;

namespace WashedAshore.Tests.PlayMode.Character
{
    /// <summary>
    /// PRINT ONLY, [Explicit] (td-49 B22, B24, B32, B33, B33b; producer-49 order; director note1-s2-first): S2, the extent of the hairline gaps on the CURRENT body. One run over (A) the four probe poses and (B) EVERY frame of the table gate's own sequence
    /// (ConfiguredTables_Gate_ThreeIdlePhasesAndGaits: Stand and Crouch idle at 3 breathing phases; 8 cycle points of Walk1.15, Walk1.5, Jog5, Sprint8, CrouchWalk1.75; every configured pitch of at least +45; the gate's timing, with its advancing check replaced by the same 0.05 s / 4 frame wait).
    /// Nothing is asserted. The gate's settled rule (copied) is printed per gait frame; unsettled frames are still analysed and counted apart. Per frame, in the SAME frame as a fresh lit render and the 960x540 body-only render (solid magenta): every INTERIOR (not border-connected) 8-connected magenta component, NO colour threshold, with
    /// size, centroid, bounding box, a 7x7 lit crop, the [PINHOLE] tag (a member with 8 body neighbours), the border regions by a 3x3 sub-sample vote (td B33b: 9 rays per border pixel at offsets -1/3, 0, +1/3 px, the majority region; ties, no-hits and split votes counted; at most 120 border pixels per component, sampled evenly),
    /// the legacy B24 class (one region = HOLE CANDIDATE, 2 or more = GAP BETWEEN PARTS) and the B33 class of record: H1 one region = PINHOLE / HOLE CANDIDATE (defect); H2 two or more regions NOT joined at that spot = HAIRLINE GAP BETWEEN PARTS (non-defect, director spot-checks the crops);
    /// H3 two or more regions joined at that spot (TORSO-LEG.x at UpperLeg.x, TORSO-ARM.x at UpperArm.x, HEAD-TORSO at Neck; the R3 hit of the border pixel nearest the centroid within 0.05 m world of that bone's position, the distance printed) = HOLE CANDIDATE (defect).
    /// For frames with a component only: a 4x supersample (3840x2160) line per component (cost cap), and an attribution block per component sample pixel (B32: hit triangles, the two framing edges with their triangle indices, their 3-D separation in mm; at most 20 detailed per frame, the rest counted).
    /// K1 control: in Sit +75 and Squat +85 the known spots (391, 397) and (373, 490) must be members of an interior component, else that pose prints "VOID: known spot not flagged"; K2 prints each known pixel's class and border-region count. The 7 slit triangles of ta's open-edge loops (rest print loops 1-7) are projected (centroid) in every frame.
    /// Outputs in runs\RUN\s2: s2-frames.csv, s2-components.txt, s2-summary.txt. Test-only switch S2_PART=A (poses, Stand idle, Walk1.15, Walk1.5) or B (Jog5, Sprint8, Crouch idle, CrouchWalk1.75) splits the run in two calls; unset runs all.
    /// X1 P0 (td-49 B35, B35a, B35b, B35c; director gate/pod49-s2-b20-classes-ruling + note2): every H1 component is attributed and given a K-class in the order of record (K-h, K-f, K-a by separation, K-b, K-a without edge 2), see HairlineGapSizing.KClass.cs;
    /// with hull insets set (X1_P0_HullInsetSweep, or HULL_INSET on S2) a test-only backing hull is measured in every frame by same-frame twin renders, see HairlineGapSizing.Hull.cs. Outputs of the P0 call go to runs\RUN\s2-p0.
    /// </summary>
    public partial class HairlineGapSizing : StanceFixture
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        const int W = 960, H = 540, MaxClassified = 40, MaxDetailed = 20, MaxBorderSamples = 120;
        static readonly int[] SlitTris = { 2966, 3157, 3289, 3342, 12219, 13423, 16985 };
        static readonly (string a, string b, string bone)[] Joints = { ("TORSO", "LEG.L", "UpperLeg.L"), ("TORSO", "LEG.R", "UpperLeg.R"), ("TORSO", "ARM.L", "UpperArm.L"), ("TORSO", "ARM.R", "UpperArm.R"), ("HEAD", "TORSO", "Neck") };
        PlayerCharacterView view;
        readonly Dictionary<SkinnedMeshRenderer, string[]> triRegionCache = new Dictionary<SkinnedMeshRenderer, string[]>(), triBoneCache = new Dictionary<SkinnedMeshRenderer, string[]>();
        readonly Dictionary<string, Transform> boneByName = new Dictionary<string, Transform>();
        readonly StringBuilder comps = new StringBuilder(), csv = new StringBuilder("group,point,stance,pitch,settled,interior,classified,sizeSum,maxSize,H1,H2,H3,H3unresolved,noRegion,unclassified,pinholes,slitsOnScreen,slitsMagenta,slitsNearComponent,Ka_sep60,Ka_noEdge2,Kb,Kb_borderline60to100,Kf,Kh,H1_open4x_evidence" + System.Environment.NewLine);
        readonly SortedDictionary<string, Agg> agg = new SortedDictionary<string, Agg>();
        readonly List<string> knownSpotLines = new List<string>();
        readonly int[] slitOn = new int[7], slitMag = new int[7], slitNear = new int[7];
        int exceptions;

        class Agg { public int frames, unsettled, framesWith, comps, h1, h2, h3, h3u, none, unclassified, pin, maxSize, sizeSum, ka, kaNoE2, kb, kbBorder, kf, kh, open4; }

        static bool Want(string what)
        {
            string part = System.Environment.GetEnvironmentVariable("S2_PART");
            if (string.IsNullOrEmpty(part)) return true;
            bool a = what == "poses" || what == "standIdle" || what == "Walk1.15" || what == "Walk1.5";
            return part == "A" ? a : !a;
        }

        /// <summary>One frame's analysis, exceptions contained and counted (no yield inside).</summary>
        void TryFrame(string group, string point, Stance stance, float pitch, bool settled, FirstPersonBody fp, (int x, int y)? spot = null)
        {
            try { Frame(group, point, stance, pitch, settled, fp, spot); }
            catch (System.Exception e) { exceptions++; comps.AppendLine($"EXCEPTION in {group} {point} +{pitch:F0}: {e.GetType().Name}: {e.Message}{System.Environment.NewLine}{e.StackTrace}"); csv.AppendLine($"{group},{point},{stance},{pitch:F0},{settled},EXCEPTION,,,,,,,,,,,,,,,,,,,"); }
        }

        /// <summary>Quick K1 check in the current frame: is the spot a member of an interior component?</summary>
        bool SpotFlagged(int x, int y)
        {
            var cam = player.ViewCamera;
            cam.aspect = W / (float)H;
            try { HullsVisible(false); var bg = ToMask(RenderPixels(cam, player.gameObject.layer, true)); return Interior(bg).Any(c => c.m.Contains((x, y))); }   // P0: K1 is judged with the hull off
            catch (System.Exception e) { exceptions++; comps.AppendLine($"EXCEPTION in SpotFlagged: {e.GetType().Name}: {e.Message}"); return false; }
            finally { HullsVisible(true); cam.ResetAspect(); }
        }

        /// <summary>The majority region of 9 sub-sample rays at one border pixel: the region name, "TIE" or "none"; split = the 9 rays did not all agree.</summary>
        (string region, bool split) Vote(Geo g, Camera cam, int x, int yTop)
        {
            var tally = new Dictionary<string, int>();
            for (int oy = -1; oy <= 1; oy++) for (int ox = -1; ox <= 1; ox++)
            {
                float fx = ox / 3f, fy = oy / 3f;
                var w = Winner(g, PixelRay(cam, x + fx, yTop + fy), x + 0.5 + fx, H - 1 - yTop + 0.5 - fy);
                tally[w.region] = tally.TryGetValue(w.region, out int n) ? n + 1 : 1;
            }
            int max = tally.Values.Max();
            var top = tally.Where(kv => kv.Value == max).Select(kv => kv.Key).ToList();
            return (top.Count > 1 ? "TIE" : top[0], tally.Count > 1);
        }

        void Frame(string group, string point, Stance stance, float pitch, bool settled, FirstPersonBody fp, (int x, int y)? spot)
        {
            var cam = player.ViewCamera;
            cam.aspect = W / (float)H;
            try
            {
                // ---- same frame from here to the end of this method: a fresh lit render, the body-only render, the bake, the 4x render, and (P0) the hull twin renders; the hull is off for everything but its own twin renders ----
                HullsVisible(false);
                var lit = RenderPixels(cam, 0, false);
                var bodyRaw = RenderPixels(cam, player.gameObject.layer, true);
                var bg = ToMask(bodyRaw);
                var tris = BakeRegions(fp);
                litBig = null;   // one 3840x2160 lit render at most per frame, taken lazily by Crops, never reused from an earlier frame
                var cs = Interior(bg).OrderByDescending(c => c.m.Count).ToList();
                int knownAt = spot.HasValue ? cs.FindIndex(c => c.m.Contains((spot.Value.x, spot.Value.y))) : -1;
                if (knownAt > 0) { var k = cs[knownAt]; cs.RemoveAt(knownAt); cs.Insert(0, k); knownAt = 0; }   // the known spot's component is always classified
                string key = group + " +" + pitch.ToString("F0", Inv);
                if (!agg.TryGetValue(key, out var a)) agg[key] = a = new Agg();
                a.frames++; if (!settled) a.unsettled++;
                comps.AppendLine($"== {group} {point} {stance} +{pitch:F0} settled {settled} (frame {Time.frameCount}): interior components {cs.Count}, baked triangles {tris.Count}");
                if (spot.HasValue && knownAt < 0) { string v = $"{group}: VOID: known spot ({spot.Value.x}, {spot.Value.y}) not flagged (not a member of any interior component)"; comps.AppendLine("  K1 " + v); knownSpotLines.Add(v); }
                int onS = 0, magS = 0, nearS = 0;
                for (int s = 0; s < SlitTris.Length; s++)   // the 7 slit triangles of ta's open-edge loops: projected centroid, magenta at it, an interior component within 3 px
                {
                    var tri = tris.FirstOrDefault(t => t.index == SlitTris[s] && t.big);
                    if (!tri.big) continue;
                    var vp = cam.WorldToViewportPoint((tri.a + tri.b + tri.c) / 3f);
                    int px = Mathf.FloorToInt(vp.x * W), py = H - 1 - Mathf.FloorToInt(vp.y * H);
                    if (vp.z <= 0f || px < 0 || px >= W || py < 0 || py >= H) continue;
                    slitOn[s]++; onS++;
                    if (Mag(bg, px, py)) { slitMag[s]++; magS++; }
                    if (cs.Any(c => px >= c.x0 - 3 && px <= c.x1 + 3 && py >= c.y0 - 3 && py <= c.y1 + 3)) { slitNear[s]++; nearS++; }
                }
                int h1 = 0, h2 = 0, h3 = 0, h3u = 0, none = 0, unclass = 0, pin = 0, sizeSum = 0, maxSize = 0, id = 0, classified = 0;
                var kc = new KCount(); var kaComps = new List<Comp>(); Comp spotComp = null;
                var frameTargets = new Dictionary<string, (Comp c, string region)>();
                if (cs.Count > 0)
                {
                    var geo = new Geo(cam, tris);
                    var big = ToMask(RenderPixels(cam, player.gameObject.layer, true, SW, SH));
                    foreach (var c in cs)
                    {
                        id++; sizeSum += c.m.Count; maxSize = Mathf.Max(maxSize, c.m.Count);
                        bool pinhole = c.m.Any(p => BodyNeighbours(bg, p.x, p.yTop) == 8);
                        if (pinhole) pin++;
                        string head = $"  component {id}: size {c.m.Count}, centroid ({c.cx:F1}, {c.cy:F1}), bbox x {c.x0}..{c.x1} y(top) {c.y0}..{c.y1}{(pinhole ? " [PINHOLE]" : "")}{(id == 1 && knownAt == 0 ? " [KNOWN SPOT]" : "")}";
                        if (id > MaxClassified) { unclass++; comps.AppendLine(head + $"; NOT CLASSIFIED (over the {MaxClassified} per frame cap)"); continue; }
                        classified++;
                        var member = new HashSet<(int x, int yTop)>(c.m);
                        var border = new HashSet<(int x, int yTop)>();
                        foreach (var mp in c.m)
                            for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
                            {
                                int bx = mp.x + dx, by = mp.yTop + dy;
                                if ((dx == 0 && dy == 0) || bx < 0 || bx >= W || by < 0 || by >= H) continue;
                                if (!Mag(bg, bx, by) && !member.Contains((bx, by))) border.Add((bx, by));
                            }
                        var bl = border.OrderBy(p => p.yTop).ThenBy(p => p.x).ToList();
                        int step = Mathf.Max(1, Mathf.CeilToInt(bl.Count / (float)MaxBorderSamples));
                        var regions = new SortedDictionary<string, int>(); int ties = 0, noHit = 0, splits = 0, voted = 0;
                        for (int i = 0; i < bl.Count; i += step)
                        {
                            voted++;
                            var v = Vote(geo, cam, bl[i].x, bl[i].yTop);
                            if (v.split) splits++;
                            if (v.region == "TIE") { ties++; continue; }
                            if (v.region == "none") { noHit++; continue; }
                            regions[v.region] = regions.TryGetValue(v.region, out int n) ? n + 1 : 1;
                        }
                        string legacy = regions.Count == 0 ? "NO BORDER REGION" : regions.Count == 1 ? "HOLE CANDIDATE" : "GAP BETWEEN PARTS";
                        string cls; var jointLines = new List<string>();
                        if (regions.Count == 0) { cls = "NO BORDER REGION (unclassified)"; none++; }
                        else if (regions.Count == 1) { cls = "H1 PINHOLE / HOLE CANDIDATE (one region " + regions.Keys.First() + "): defect"; h1++; }
                        else
                        {
                            var nb = bl.OrderBy(p => (p.x - c.cx) * (p.x - c.cx) + (p.yTop - c.cy) * (p.yTop - c.cy)).First();
                            var hit = Winner(geo, PixelRay(cam, nb.x, nb.yTop), nb.x + 0.5, H - 1 - nb.yTop + 0.5);
                            bool joined = false, unresolved = false;
                            foreach (var j in Joints)
                            {
                                if (!regions.ContainsKey(j.a) || !regions.ContainsKey(j.b)) continue;
                                if (hit.tri < 0 || !boneByName.TryGetValue(j.bone, out var bone) || bone == null) { unresolved = true; jointLines.Add($"    joint {j.a}-{j.b} at {j.bone}: no hit or no bone (or a destroyed bone): the joint test COULD NOT RUN"); continue; }
                                float d = Vector3.Distance(hit.point, bone.position);
                                bool within = d <= 0.05f; joined |= within;
                                jointLines.Add($"    joint {j.a}-{j.b} at {j.bone}: the R3 hit of border pixel ({nb.x}, {nb.yTop}) nearest the centroid, region {hit.region}, is {d.ToString("F4", Inv)} m (world) from the bone at {bone.position.x.ToString("F3", Inv)}, {bone.position.y.ToString("F3", Inv)}, {bone.position.z.ToString("F3", Inv)}: {(within ? "within" : "beyond")} 0.05 m");
                            }
                            if (joined) { cls = "H3 HOLE CANDIDATE (2+ regions JOINED at that spot): defect"; h3++; } else if (unresolved) { cls = "H3 UNRESOLVED (joint test could not run): defect candidate for td + director"; h3u++; } else { cls = "H2 HAIRLINE GAP BETWEEN PARTS (2+ regions, not joined at that spot): non-defect, director spot-checks the crop"; h2++; }
                        }
                        comps.AppendLine(head + $"; border body pixels {bl.Count} (voted {voted}, every {step}), regions by 3x3 vote [{string.Join(", ", regions.Select(kv => kv.Key + " " + kv.Value))}], ties {ties}, no hit {noHit}, split votes {splits}; legacy B24 class {legacy}; B33 class {cls}");
                        foreach (var jl in jointLines) comps.AppendLine(jl);
                        if (id == 1 && knownAt == 0) knownSpotLines.Add($"{group}: K1 FLAGGED, component of size {c.m.Count}; K2 B33 class {cls}, border regions {regions.Count} [{string.Join(", ", regions.Select(kv => kv.Key + " " + kv.Value))}], legacy {legacy}");
                        comps.AppendLine("    " + SuperLine(big, c, out bool open4));
                        Crops(group, point, pitch, c, cls.StartsWith("H1"), cls.StartsWith("H2"), string.Join("+", regions.Keys), cam);
                        if (id == 1 && knownAt == 0) spotComp = c;
                        bool isH1 = cls.StartsWith("H1");
                        if (id <= MaxDetailed || isH1)   // td B35: attribution for EVERY H1; the 20-per-frame cap stays for the print lines of the others
                        {
                            var cp = c.m.OrderBy(p => (p.x - c.cx) * (p.x - c.cx) + (p.yTop - c.cy) * (p.yTop - c.cy)).First();
                            var pinMembers = c.m.Where(p => BodyNeighbours(bg, p.x, p.yTop) == 8).OrderBy(p => (p.x - c.cx) * (p.x - c.cx) + (p.yTop - c.cy) * (p.yTop - c.cy)).ToList();
                            var sp = pinMembers.Count > 0 ? pinMembers[0] : cp;
                            if (id <= MaxDetailed) comps.AppendLine($"    lit crop 7x7 centred on ({cp.x}, {cp.yTop}) (r,g,b; M = magenta in the body-only render; rows from the top):");
                            for (int ry = -3; ry <= 3 && id <= MaxDetailed; ry++)
                            {
                                var row = new List<string>();
                                for (int rx = -3; rx <= 3; rx++)
                                {
                                    int xx = cp.x + rx, yy = cp.yTop + ry;
                                    if (xx < 0 || xx >= W || yy < 0 || yy >= H) { row.Add("out"); continue; }
                                    var q = lit[(H - 1 - yy) * W + xx];
                                    row.Add((Mag(bg, xx, yy) ? "M " : "") + q.r + "," + q.g + "," + q.b);
                                }
                                comps.AppendLine("      " + string.Join(" | ", row));
                            }
                            var framing = Attribute(comps, geo, cam, sp.x, sp.yTop);
                            MatchTargets(group, point, pitch, c, framing, string.Join("+", regions.Keys), frameTargets);   // td B34e / B35c: targets by their framing-edge triangles, never by component number
                            if (isH1) ClassifyH1(group, point, pitch, c, framing, open4, string.Join("+", regions.Keys), cam, kc, kaComps);
                        }
                    }
                    if (cs.Count > MaxDetailed) comps.AppendLine($"  7x7 crops printed for the first {MaxDetailed} components; attribution for those and for every H1 (td B35); {cs.Count - MaxDetailed} more counted");
                    WriteTargetCrops(group, point, pitch, frameTargets, lit, cam);
                }
                if (hullInsets.Length > 0) HullPass(group, point, pitch, cam, fp, lit, bodyRaw, cs, kaComps, frameTargets, spotComp, unclass);   // P0: same frame, no yield, against the hull-off renders above
                if (cs.Count > 0) a.framesWith++;
                a.comps += cs.Count; a.h1 += h1; a.h2 += h2; a.h3 += h3; a.h3u += h3u; a.none += none; a.unclassified += unclass; a.pin += pin; a.sizeSum += sizeSum; a.maxSize = Mathf.Max(a.maxSize, maxSize);
                a.ka += kc.ka; a.kaNoE2 += kc.kaNoE2; a.kb += kc.kb; a.kbBorder += kc.kbBorder; a.kf += kc.kf; a.kh += kc.kh; a.open4 += kc.open4;
                csv.AppendLine($"{group},{point},{stance},{pitch:F0},{settled},{cs.Count},{classified},{sizeSum},{maxSize},{h1},{h2},{h3},{h3u},{none},{unclass},{pin},{onS},{magS},{nearS},{kc.ka},{kc.kaNoE2},{kc.kb},{kc.kbBorder},{kc.kf},{kc.kh},{kc.open4}");
            }
            finally { HullsVisible(true); cam.ResetAspect(); }
        }

        string Summary()
        {
            var s = new StringBuilder("EXCEPTIONS " + exceptions + (exceptions == 0 ? "" : ": THIS RUN IS VOID, no reference reading") + System.Environment.NewLine + "# S2 timing is NOT gate-identical (a 0.05 s / 4 frame wait replaces the gate's advancing check): a sizing run only. Slit positions are triangle centroids, not slit vertices." + System.Environment.NewLine + "# " + MirrorTie.StateLine + "; S2 hairline-gap extent on the current body (no colour threshold; interior = enclosed by body; H1/H3 defects, H2 hairline gap between parts, td B33)" + System.Environment.NewLine);
            s.AppendLine("# " + KRuleText);
            var runVoid = RunVoidReasons();   // eng_51: the rest read and the K-f map are run-level controls for S2 and P0 alike
            if (runVoid.Count > 0) { s.AppendLine("RUN-LEVEL CONTROLS FAILED: THIS RUN IS VOID (fail-closed, eng_51)"); foreach (var r in runVoid) s.AppendLine("  VOID reason: " + r); }
            else s.AppendLine("# run-level controls OK: " + rest?.note);
            s.AppendLine("key,frames,unsettled,framesWithInterior,components,H1(one region),H2(hairline between parts),H3(joined),H3UNRESOLVED(joint test could not run),noRegion,unclassified(over cap),PINHOLE,maxSize_px,sizeSum_px,K-a(sep<=60mm) DEFECT,K-a(no edge 2 or no attribution) DEFECT,K-b(>60mm),K-b borderline(60-100mm; in K-b),K-f(fin/fringe),K-h(hand),H1 open at 4x (evidence only)");
            var byGroup = new SortedDictionary<string, Agg>(); var total = new Agg();
            foreach (var kv in agg)
            {
                Line(s, kv.Key, kv.Value);
                string g = kv.Key.Substring(0, kv.Key.LastIndexOf(" +"));
                if (!byGroup.TryGetValue(g, out var ga)) byGroup[g] = ga = new Agg();
                Add(ga, kv.Value); Add(total, kv.Value);
            }
            s.AppendLine("# totals by group");
            foreach (var kv in byGroup) Line(s, kv.Key, kv.Value);
            Line(s, "ALL FRAMES", total);
            s.AppendLine("# K1/K2 known spots (Sit (391, 397), Squat (373, 490))");
            foreach (var l in knownSpotLines) s.AppendLine(l);
            s.AppendLine("# slit triangles, triangle centroid (not slit vertices) (loops 1-7 of the rest print): frames with the projected centroid on screen, magenta at it, an interior component within 3 px");
            for (int i = 0; i < SlitTris.Length; i++) s.AppendLine($"slit loop {i + 1} triangle {SlitTris[i]} (triangle centroid, not slit vertices): on screen {slitOn[i]}, magenta at the centroid {slitMag[i]}, interior component within 3 px {slitNear[i]}");
            s.AppendLine("# K-f map (td B35c; FBX polygon -> Unity triangle, computed in the test at bake time)");
            s.Append(kfMapText.Length > 0 ? kfMapText : "K-f map NOT COMPUTED (no Survivor_Body bake): K-f is EMPTY, its rows fall to K-a" + System.Environment.NewLine);
            s.AppendLine($"exceptions: {exceptions}");
            return s.ToString();
        }

        static void Add(Agg t, Agg a) { t.frames += a.frames; t.unsettled += a.unsettled; t.framesWith += a.framesWith; t.comps += a.comps; t.h1 += a.h1; t.h2 += a.h2; t.h3 += a.h3; t.h3u += a.h3u; t.none += a.none; t.unclassified += a.unclassified; t.pin += a.pin; t.sizeSum += a.sizeSum; t.maxSize = Mathf.Max(t.maxSize, a.maxSize); t.ka += a.ka; t.kaNoE2 += a.kaNoE2; t.kb += a.kb; t.kbBorder += a.kbBorder; t.kf += a.kf; t.kh += a.kh; t.open4 += a.open4; }
        static void Line(StringBuilder s, string key, Agg a) => s.AppendLine($"{key},{a.frames},{a.unsettled},{a.framesWith},{a.comps},{a.h1},{a.h2},{a.h3},{a.h3u},{a.none},{a.unclassified},{a.pin},{a.maxSize},{a.sizeSum},{a.ka},{a.kaNoE2},{a.kb},{a.kbBorder},{a.kf},{a.kh},{a.open4}");

        [UnityTest, Explicit("Print only: S2 hairline-gap extent over the four probe poses and every frame of the table gate's own sequence"), Timeout(7200000)]
        public IEnumerator S2_PosesAndAllGateFrames()
        {
            hullInsets = ParseInsets(new float[0]);   // the hull only when HULL_INSET is set
            yield return Run("s2");
        }

        /// <summary>X1 P0 (td-49 B35/B35a/B35b/B35c), PRINT ONLY: the S2 run with the refined K-class classifier and the test-only backing hull at HULL_INSET (default +3, 0, -3, -6 mm; positive = inward of the innermost profile, negative = outward between the layers),
        /// the same-frame hull-on/hull-off twin LEAK in the body-only and the lit render, K-a closure, the targets and their twin crops. The verdict line is in s2-hull-summary.txt. Nothing is asserted.</summary>
        [UnityTest, Explicit("Print only: X1 P0, the S2 run with the backing-hull inset sweep and the twin-render leak test"), Category("X1P0"), Timeout(7200000)]
        public IEnumerator X1_P0_HullInsetSweep()
        {
            hullInsets = ParseInsets(DefaultInsets);
            yield return Run("s2-p0");
        }

        IEnumerator Run(string sub)
        {
            PinTicks();
            string dir = RunOutput.Dir(sub);
            outDir = dir;
            try
            {
                if (Want("poses")) yield return Poses();
                if (preflightVoid == null) yield return GateFrames();   // eng_51: a failed preflight stops the run before the 385-frame sweep
            }
            finally
            {
                File.WriteAllText($"{dir}/s2-frames.csv", csv.ToString());
                File.WriteAllText($"{dir}/s2-components.txt", "# " + MirrorTie.StateLine + System.Environment.NewLine + comps);
                File.WriteAllText($"{dir}/s2-summary.txt", Summary());
                File.WriteAllText($"{dir}/s2-crops-index.txt", CropIndex());
                if (hullInsets.Length > 0)
                {
                    File.WriteAllText($"{dir}/s2-hull-frames.csv", hullCsv.ToString());
                    File.WriteAllText($"{dir}/s2-hull-summary.txt", HullSummary());
                }
                DestroyHulls();
                Debug.Log("S2 written to " + dir);
            }
            if (preflightVoid != null) Assert.Fail("NO VERDICT (VOID), fail fast before any analysed frame (eng_51): " + string.Join("; ", preflightVoid) + ". See " + dir);
        }

        IEnumerator Poses()
        {
            var poses = new (string name, StanceKey key, Stance stance, float pitch, (int x, int y)? spot)[]
            {
                ("Stand_Idle_p85", StanceKey.None, Stance.Stand, 85f, null),
                ("Crouch_Idle_p75", StanceKey.Crouch, Stance.Crouch, 75f, null),
                ("Sit_Loop_p75", StanceKey.Sit, Stance.Sit, 75f, (391, 397)),
                ("Squat_Hold_p85", StanceKey.Squat, Stance.Squat, 85f, (373, 490)),
            };
            foreach (var pose in poses)
            {
                yield return LoadWorld();
                yield return OnFloor();
                view = player.GetComponent<PlayerCharacterView>();
                Assert.IsNotNull(view?.FirstPerson, "no first-person body on the Player");
                if (!Preflight(view.FirstPerson)) yield break;   // eng_51 fail fast: rest read, K-f map, hull build (built here so the hull is skinned by the engine before the first analysed frame)
                if (pose.key != StanceKey.None) yield return Go(pose.key, 6f);
                Assert.AreEqual(pose.stance, player.Stance, $"{pose.name}: could not reach {pose.stance}");
                yield return new WaitForSeconds(2f);
                player.SetLook(player.Yaw, pose.pitch);
                yield return null;
                yield return null;
                // K1: the known spots are looked for frame by frame (up to 60) until one is flagged; the analysis runs in that same frame
                for (int attempt = 0; attempt < 60; attempt++)
                {
                    bool ok = !pose.spot.HasValue || SpotFlagged(pose.spot.Value.x, pose.spot.Value.y);
                    if (ok || attempt == 59)
                    {
                        if (pose.spot.HasValue) comps.AppendLine($"== {pose.name}: known spot looked for in {attempt + 1} frame(s), {(ok ? "flagged" : "NOT flagged in 60 frames")}");
                        TryFrame("Pose " + pose.name, "-", pose.stance, pose.pitch, true, view.FirstPerson, pose.spot);
                        break;
                    }
                    yield return null;
                }
            }
        }

        const float FloorHalf = 30f, FloorMarginMin = 2f, SpeedTolerance = 0.05f;

        IEnumerator GateFrames()
        {
            bool anyStand = Want("standIdle") || Want("Walk1.15") || Want("Walk1.5") || Want("Jog5") || Want("Sprint8"), anyCrouch = Want("crouchIdle") || Want("CrouchWalk1.75");
            yield return LoadWorld();
            yield return OnFloor();
            view = player.GetComponent<PlayerCharacterView>();
            Assert.IsNotNull(view?.FirstPerson, "no first-person body on the Player");
            var fp = view.FirstPerson;
            if (!Preflight(fp)) yield break;   // eng_51 fail fast: rest read, K-f map, hull build (built here so the hull is skinned by the engine before the first analysed frame)
            var tuning = player.LookDownTuning;
            tuning.debugBackOverride = float.NaN;
            var floorCentre = new Vector3(floor.transform.position.x, floor.transform.position.y + 2.05f, floor.transform.position.z);
            foreach (var stance in new[] { Stance.Stand, Stance.Crouch })
            {
                if ((stance == Stance.Stand && !anyStand) || (stance == Stance.Crouch && !anyCrouch)) continue;
                if (stance == Stance.Crouch)
                {
                    InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                    player.ExternalMoveInput = null;
                    player.WalkSpeedCap = 0f;
                    Place(floorCentre, 0f);
                    yield return new WaitForSeconds(1.2f);
                    yield return Go(StanceKey.Crouch, 4f);
                }
                Assert.AreEqual(stance, player.Stance, $"could not reach {stance}");
                var pitches = tuning.Of(stance).Select(p => p.x).Where(x => x >= 45f).ToArray();
                if (Want(stance == Stance.Stand ? "standIdle" : "crouchIdle"))
                    for (int ph = 0; ph < 3; ph++)
                    {
                        yield return new WaitForSeconds(ph == 0 ? 1.5f : 2.7f);
                        foreach (float pitch in pitches)
                        {
                            player.SetLook(player.Yaw, pitch);
                            yield return null;
                            yield return null;
                            TryFrame(stance + " idle", "phase " + ph, stance, pitch, true, fp);
                        }
                    }
                var gaits = stance == Stance.Stand
                    ? new (string label, float target, float cap, Key[] keys)[] { ("Walk1.15", 1.15f, 1.15f, new Key[0]), ("Walk1.5", 1.5f, 1.5f, new Key[0]), ("Jog5", 5f, 0f, new[] { Key.W }), ("Sprint8", 8f, 0f, new[] { Key.W, Key.LeftShift }) }
                    : new (string label, float target, float cap, Key[] keys)[] { ("CrouchWalk1.75", 1.75f, 0f, new[] { Key.W }) };
                foreach (var g in gaits) if (Want(g.label)) yield return Gait(g.label, stance, g.target, g.cap, fp, pitches, g.keys);
            }
        }

        /// <summary>The gate's GateGait sequence frame for frame; the advancing check is replaced by the same 0.05 s / 4 frame wait.</summary>
        IEnumerator Gait(string label, Stance expected, float target, float cap, FirstPersonBody fp, float[] pitches, params Key[] keys)
        {
            var floorCentre = new Vector3(floor.transform.position.x, floor.transform.position.y + 2.05f, floor.transform.position.z);
            Place(floorCentre - Vector3.forward * 25f, 0f);
            yield return new WaitForSeconds(0.3f);
            if (cap > 0f) player.ExternalMoveInput = Vector2.up * (cap / player.WalkSpeed);
            else InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys));
            yield return new WaitForSeconds(0.9f);
            string token = label.StartsWith("Crouch") ? "CrouchWalk" : label.StartsWith("Walk") ? "Walk" : label.StartsWith("Jog") ? "Jog" : "Sprint";
            for (int k = 0; k < 8; k++)
            {
                Place(floorCentre - Vector3.forward * 25f, 0f);
                yield return new WaitForSeconds(0.1f + 0.11f * k);
                foreach (float pitch in pitches)
                {
                    player.SetLook(player.Yaw, pitch);
                    Place(floorCentre - Vector3.forward * 25f, 0f);
                    yield return null;
                    yield return null;
                    float tt = 0f; int fr = 0;
                    while (tt < 0.05f || fr < 4) { yield return null; tt += Time.deltaTime; fr++; }
                    bool settled = Settled(expected, target, token, out _) == null;
                    TryFrame(label, "cycle point " + k, expected, pitch, settled, fp);
                }
            }
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            player.ExternalMoveInput = null;
            yield return new WaitForSeconds(0.6f);
        }

        string GaitCheck(float target, out string extra)
        {
            float v = new Vector2(body.velocity.x, body.velocity.z).magnitude;
            float air = view != null && view.Body != null ? view.Body.Animator.GetFloat("AirTime") : 0f;
            bool grounded = player.IsGrounded && air <= 0.05f;
            Vector3 p = player.transform.position, c = floor.transform.position;
            float margin = Mathf.Min(FloorHalf - Mathf.Abs(p.x - c.x), FloorHalf - Mathf.Abs(p.z - c.z));
            extra = $",{grounded},{v:F2},{margin:F1}";
            var bad = new List<string>();
            if (!grounded) bad.Add($"not grounded (controller {player.IsGrounded}, AirTime {air:F2})");
            if (target > 0f ? Mathf.Abs(v - target) > SpeedTolerance * target : v > 0.05f) bad.Add($"speed {v:F2} m/s is not within 5% of {target:F2}");
            if (margin < FloorMarginMin) bad.Add($"{margin:F1} m from the floor edge (need {FloorMarginMin:F0})");
            return bad.Count == 0 ? null : string.Join("; ", bad);
        }

        /// <summary>The gate's settled rule (LookDownTests.Settled), copied: stance settled, pivot at the record eye, expected clip family dominant, speed within 5%, grounded, on the floor. Null = the sample counts.</summary>
        string Settled(Stance expected, float targetSpeed, string clipToken, out string diag)
        {
            var bad = new List<string>();
            float eye = player.ViewCamera.transform.position.y - player.transform.position.y;
            float record = expected == Stance.Crouch ? 1.20f : 1.65f;
            if (player.Stance != expected) bad.Add($"stance is {player.Stance}, expected {expected}");
            if (player.StanceTransitioning || player.StanceBlend < 0.999f) bad.Add($"stance not settled (transitioning {player.StanceTransitioning}, blend {player.StanceBlend:F2})");
            if (Mathf.Abs(eye - record) > 0.005f) bad.Add($"pivot eye {eye:F3} is not {record:F2} +-0.005");
            string dominant = ""; float w = -1f;
            foreach (var ci in view.Body.Animator.GetCurrentAnimatorClipInfo(0)) if (ci.weight > w) { w = ci.weight; dominant = ci.clip.name; }
            if (dominant.IndexOf(clipToken, System.StringComparison.OrdinalIgnoreCase) < 0) bad.Add($"dominant clip is '{dominant}' (weight {w:F2}), expected a {clipToken} clip");
            string gait = GaitCheck(targetSpeed, out string extra);
            if (gait != null) bad.Add(gait);
            diag = $"eye {eye:F3} blend {player.StanceBlend:F2} clip '{dominant}' w {w:F2}{extra}";
            return bad.Count == 0 ? null : string.Join("; ", bad);
        }
    }
}
