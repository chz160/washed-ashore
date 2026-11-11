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
    /// PRINT ONLY, [Explicit] (director-49 note2, td-49 "REST PIXEL DROP" P5): one Unity still of the body AS LANDED at Stand idle, first person at pitch +60, and one side still, under the scene's own sun, FOV and eye height
    /// (the live player view camera in World.unity; the light, FOV and eye height are written to the index). Idle timing as the gate's first idle phase (1.5 s after the stance is reached). Optional marks:
    /// the environment variable STANDIDLE_MARKS names a text file with one mark per line, "x y half [label]" in the pixels of the 960x540 first-person still (x from the left, y from the TOP, as an image viewer shows it);
    /// each mark is outlined in red in a second, marked copy. Writes into runs\RUN\standidle: standidle_fp+60.png, standidle_fp+60.marked.png (when marks exist), standidle_side.png and standidle-index.txt.
    /// The side camera is a temporary camera 2.5 m to the player's right, looking at the player's chest, with the player camera's field of view and clip planes and every layer drawn. The side stills are taken with
    /// FirstPersonBody.SetHeadVisible(true) (restored in a finally): the lit standidle_side.png shows the SHADOW body with its head and the view body hidden, and the body-only side pass is the shadow body alone. The index records,
    /// for each pass, every renderer's name, enabled flag and shadowCastingMode, and the count of magenta-threshold pixels in each lit still (must be 0). No asserts beyond the scene loading; nothing is changed in the project.
    /// </summary>
    public class StandIdleStill : StanceFixture
    {
        static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);
        static string V3(Vector3 v) => F(v.x) + ";" + F(v.y) + ";" + F(v.z);

        [UnityTest, Explicit("Print only: Unity Stand_Idle still, first person +60 and side"), Timeout(600000)]
        public IEnumerator StandIdle_FirstPerson60_AndSide()
        {
            PinTicks();
            yield return LoadWorld();
            yield return OnFloor();
            var view = player.GetComponent<PlayerCharacterView>();
            Assert.IsNotNull(view?.FirstPerson, "no first-person body on the Player");
            var fp = view.FirstPerson;
            yield return new WaitForSeconds(1.5f);
            player.SetLook(player.Yaw, 60f);
            yield return null;
            yield return null;
            string dir = RunOutput.Dir("standidle");
            var cam = player.ViewCamera;
            var index = new StringBuilder("# " + WashedAshore.Tests.Support.MirrorTie.StateLine + System.Environment.NewLine);

            Light sun = null;
            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None)) if (l.type == LightType.Directional) { sun = l; break; }
            var ct = cam.transform;
            var va = view.ViewCopy != null && view.ViewCopy.ViewAnimator != null ? view.ViewCopy.ViewAnimator : view.Body.Animator;
            var info = va.GetCurrentAnimatorStateInfo(0);
            string clip = va.GetCurrentAnimatorClipInfo(0).OrderByDescending(c => c.weight).Select(c => c.clip.name).FirstOrDefault() ?? "";
            index.AppendLine($"stance {player.Stance}; view clip {clip}; normalizedTime {F(info.normalizedTime)}; pitch 60");
            index.AppendLine($"camera position {V3(ct.position)}; euler {V3(ct.eulerAngles)}; FOV {F(cam.fieldOfView)}; eye height above the player origin {F(ct.position.y - player.transform.position.y)}");
            index.AppendLine($"light {(sun != null ? "euler " + V3(sun.transform.eulerAngles) + "; intensity " + F(sun.intensity) : "none")}");
            index.AppendLine("view renderers enabled: " + string.Join(", ", fp.ViewRenderers.Where(r => r != null && r.enabled).Select(r => r.name)));

            index.AppendLine("pass fp lit + fp body-only: drawn renderers = view body (" + fp.ViewRenderers.Count(r => r != null && r.enabled) + " enabled: " + string.Join(", ", fp.ViewRenderers.Where(r => r != null && r.enabled).Select(r => r.name)) + "); shadow body (" + fp.BodyRenderers.Count + " renderers) is ShadowsOnly in first person, not drawn");
            index.AppendLine("fp pass view-body renderers: " + RendererList(fp.ViewRenderers));
            index.AppendLine("fp pass shadow-body renderers (fp.BodyRenderers): " + RendererList(fp.BodyRenderers));
            var fpTex = Render(cam, 960, 540);
            index.AppendLine($"magenta-threshold pixels in the lit first-person still (must be 0): {CountMagenta(fpTex)}");
            File.WriteAllBytes($"{dir}/standidle_fp+60.png", fpTex.EncodeToPNG());
            string marksPath = System.Environment.GetEnvironmentVariable("STANDIDLE_MARKS");
            if (!string.IsNullOrEmpty(marksPath) && File.Exists(marksPath))
            {
                var marked = new Texture2D(fpTex.width, fpTex.height, TextureFormat.RGB24, false);
                marked.SetPixels(fpTex.GetPixels());
                int n = 0;
                foreach (var line in File.ReadAllLines(marksPath))
                {
                    var t = line.Split(new[] { ' ', '\t' }, System.StringSplitOptions.RemoveEmptyEntries);
                    if (t.Length < 3 || line.StartsWith("#")) continue;
                    int x = int.Parse(t[0], CultureInfo.InvariantCulture), yTop = int.Parse(t[1], CultureInfo.InvariantCulture), half = int.Parse(t[2], CultureInfo.InvariantCulture);
                    Outline(marked, x, fpTex.height - 1 - yTop, half);
                    index.AppendLine($"mark {n++}: x {x}, y(top) {yTop}, half {half}{(t.Length > 3 ? ", " + string.Join(" ", t.Skip(3)) : "")}");
                }
                marked.Apply();
                File.WriteAllBytes($"{dir}/standidle_fp+60.marked.png", marked.EncodeToPNG());
                Object.Destroy(marked);
            }
            else index.AppendLine("marks: none (STANDIDLE_MARKS not set)");
            Object.Destroy(fpTex);

            // BODY-ONLY pass of the first-person view (td-49 P5): a copy of the view camera that draws only the Player layer (both bodies sit on it; the shadow body is ShadowsOnly in first person, so only the view body is drawn)
            // over a solid magenta background, then the magenta components are counted.
            BodyOnly(cam, player.gameObject.layer, dir, "standidle_fp+60", index);

            var go = new GameObject("StandIdleSideCamera");
            try
            {
                var sc = go.AddComponent<Camera>();
                sc.fieldOfView = cam.fieldOfView;
                sc.nearClipPlane = cam.nearClipPlane;
                sc.farClipPlane = cam.farClipPlane;
                sc.cullingMask = ~0;
                Vector3 target = player.transform.position + Vector3.up * 1.0f;
                sc.transform.position = player.transform.position + player.transform.right * 2.5f + Vector3.up * 1.0f;
                sc.transform.rotation = Quaternion.LookRotation((target - sc.transform.position).normalized, Vector3.up);
                fp.SetHeadVisible(true);   // the existing debug view: the shadow body is drawn normally with its head and the view body is hidden; restored below
                index.AppendLine("pass side lit + side body-only: drawn renderers = shadow body (fp.BodyRenderers, " + fp.BodyRenderers.Count(r => r != null && r.enabled && r.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly) + " enabled and not ShadowsOnly: " + string.Join(", ", fp.BodyRenderers.Where(r => r != null && r.enabled && r.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly).Select(r => r.name)) + "); view body hidden (" + fp.ViewRenderers.Count(r => r != null && r.enabled) + " view renderers enabled)");
                index.AppendLine("side pass view-body renderers: " + RendererList(fp.ViewRenderers));
                index.AppendLine("side pass shadow-body renderers (fp.BodyRenderers): " + RendererList(fp.BodyRenderers));
                index.AppendLine("NOTE: standidle_side.png is taken with SetHeadVisible(true): it shows the shadow body with its head, view body hidden (an earlier draft drew every layer).");
                var sideTex = Render(sc, 960, 540);
                index.AppendLine($"magenta-threshold pixels in the lit side still (must be 0): {CountMagenta(sideTex)}");
                File.WriteAllBytes($"{dir}/standidle_side.png", sideTex.EncodeToPNG());
                Object.Destroy(sideTex);
                BodyOnly(sc, player.gameObject.layer, dir, "standidle_side", index);   // the shadow body alone
                index.AppendLine($"side camera position {V3(sc.transform.position)}; euler {V3(sc.transform.eulerAngles)}; FOV {F(sc.fieldOfView)}; cullingMask everything");
            }
            finally
            {
                fp.SetHeadVisible(false);   // A4: always restore the first-person view, even if a pass throws
                Object.Destroy(go);
            }
            File.WriteAllText($"{dir}/standidle-index.txt", index.ToString());
            Debug.Log("StandIdleStill written to " + dir);
        }

        /// <summary>
        /// PRINT ONLY, [Explicit] (director-49 gate/pod49-bar1-gap-g-and-standidle-ruling P5, four more poses): the same first-person lit still and BODY-ONLY magenta pass with the component list as StandIdle_FirstPerson60_AndSide, for
        /// Stand idle at +85, Crouch idle at +75, Sit loop at +75 and Squat hold at +85. Each pose reloads the world, reaches the stance, waits 2 s (the gate waited 1.5 s) and sets the pitch. Writes into runs\RUN\standidle:
        /// pose_<name>.png (lit), pose_<name>.bodyonly.png and standidle-poses-index.txt (stance, view clip, normalized time, camera position and angles, FOV, eye height, light, drawn renderers, the magenta-threshold count of the
        /// lit still (must be 0), body pixels, interior components and candidate notches with size, centroid and bounding box). No asserts beyond the scene loading and the stance being reached.
        /// </summary>
        [UnityTest, Explicit("Print only: Unity first-person body-only stills for Stand +85, Crouch +75, Sit +75, Squat +85"), Timeout(900000)]
        public IEnumerator FourMorePoses_FirstPersonBodyOnly()
        {
            return Poses(null);
        }

        /// <summary>
        /// PRINT ONLY, [Explicit] (td-49 B20): the same four poses and body-only passes, and for EVERY interior magenta component one ray through EVERY member pixel (td-49 B20b; this replaced the centroid-plus-four-extremes samples),
        /// cast with the R3 path (the same baked view mesh as the gate, winner under MirrorTie.Replaces, nearest front hit, BarOneRule.Judge with the triangle identity). Prints, per ray: the pixel, every intersection sorted by distance
        /// (t, renderer, triangle, front/back, region), the winner, the front hit and the verdict. NO HIT = a gap between separate parts (floor seen between them: not a defect, no ray meets the body); a back-first ray with no front hit or a gap over G = a hole.
        /// Writes standidle-poses-probe.txt next to the pose index (td-49 B20a P6).
        /// </summary>
        [UnityTest, Explicit("Print only: ray probe through every interior magenta component of the four poses"), Timeout(900000)]
        public IEnumerator InteriorComponents_RayProbe()
        {
            return Poses(new StringBuilder("# " + WashedAshore.Tests.Support.MirrorTie.StateLine + "; ray probe of the interior components (R3 path, G legs 35 mm, arms/torso/head 10 mm)" + System.Environment.NewLine));
        }

        struct PTri { public Vector3 a, b, c; public string region, rend; public int index; }

        static List<PTri> BakeRegions(FirstPersonBody fp)
        {
            var tris = new List<PTri>(40000);
            var mesh = new Mesh();
            foreach (var r in fp.ViewRenderers)
            {
                if (!r.enabled || !(r is SkinnedMeshRenderer smr) || smr.sharedMesh == null) continue;
                smr.BakeMesh(mesh, false);
                var v = mesh.vertices;
                var t = mesh.triangles;
                var weights = smr.sharedMesh.boneWeights;
                var bones = smr.bones;
                string Reg(int vi) => weights.Length > vi && weights[vi].boneIndex0 >= 0 && weights[vi].boneIndex0 < bones.Length ? BarOneRule.Region(bones[weights[vi].boneIndex0]) : "UNKNOWN";
                Vector3 pos = smr.transform.position;
                Quaternion rot = smr.transform.rotation;
                for (int i = 0; i < t.Length; i += 3)
                    tris.Add(new PTri { a = pos + rot * v[t[i]], b = pos + rot * v[t[i + 1]], c = pos + rot * v[t[i + 2]], region = BarOneRule.TriRegion(Reg(t[i]), Reg(t[i + 1]), Reg(t[i + 2])), rend = smr.name, index = i / 3 });
            }
            Object.Destroy(mesh);
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

        /// <summary>One R3 ray: every intersection with the baked triangles, the MirrorTie winner, the nearest front hit and the Judge verdict. Returns the printed text and whether the ray meets any body triangle.</summary>
        static string CastR3(List<PTri> tris, Ray ray, out bool anyHit, out string verdictName)
        {
            verdictName = "none";
            Vector3 o = ray.origin, d = ray.direction;
            int count = 0;
            float best = float.MaxValue; bool backFace = false; PTri bestTri = default;
            var front = new BarOneRule.RayState();
            foreach (var tri in tris)
            {
                if (!RayTri(o, d, tri, out float t, out bool back)) continue;
                count++;
                front.Note(back, t, tri.region);
                if (MirrorTie.Replaces(best < float.MaxValue, best, backFace, bestTri.a, bestTri.b, bestTri.c, t, back, tri.a, tri.b, tri.c)) { best = t; backFace = back; bestTri = tri; }
            }
            anyHit = count > 0;
            if (count == 0) return "no body hit";
            var verdict = BarOneRule.Judge(backFace, best, bestTri.region, front.Found, front.T, front.Region);
            verdictName = verdict.ToString();
            return $"BODY HIT x{count}: winner {bestTri.rend}#{bestTri.index} {(backFace ? "back" : "front")} {bestTri.region} t {best:F4}; nearest front {(front.Found ? front.Region + " t " + front.T.ToString("F4") + " gap " + ((front.T - best) * 1000f).ToString("F2") + " mm" : "none")}; verdict {verdict}";
        }

        static Ray PixelRay(Camera cam, int x, int yTop) => cam.ViewportPointToRay(new Vector3((x + 0.5f) / 960f, (539 - yTop + 0.5f) / 540f, 0f));

        static (int x, int yTop) Nearest(List<(int x, int yTop)> pts, double tx, double ty)
        {
            var best = pts[0]; double bd = double.MaxValue;
            foreach (var pt in pts) { double dd = (pt.x - tx) * (pt.x - tx) + (pt.yTop - ty) * (pt.yTop - ty); if (dd < bd) { bd = dd; best = pt; } }
            return best;
        }

        /// <summary>
        /// td-49 B20a P2-P5 with B20b R2: every member pixel of each interior component (B20b replaced the centroid, bbox-corner and 3x3-grid samples), a ray per pixel through
        /// the R3 loop, the controls per pose (a ray through the body pixel nearest the image centre must hit the body; a ray through the image-corner magenta pixel must hit nothing; else the probe is VOID), and the BODY HIT IN INTERIOR COMPONENT list. The camera is not moved
        /// between the body-only render and these rays (the caller has no yield between them). The camera aspect is set to the still's 960:540 for the rays and reset afterwards.
        /// </summary>
        static void ProbeInteriors(StringBuilder probe, FirstPersonBody fp, Camera cam, string pose, BodyOnlyResult br)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            probe.AppendLine($"== {pose}: {br.interior.Count} interior components");
            var tris = BakeRegions(fp);
            probe.AppendLine($"baked view-body triangles {tris.Count} (enabled view renderers only; 0 would make every ray NO HIT)");
            var aaProbe = new RenderTexture(960, 540, 24);
            int rtAa = aaProbe.antiAliasing; aaProbe.Release(); Object.Destroy(aaProbe);
            probe.AppendLine($"camera aspect before probe {cam.aspect:F4}, pixelWidth x pixelHeight {cam.pixelWidth} x {cam.pixelHeight}, allowMSAA {cam.allowMSAA}, RT antiAliasing {rtAa} (the stills are rendered into a 960x540 RenderTexture of that kind), QualitySettings.antiAliasing {QualitySettings.antiAliasing}; aspect set to {960f / 540f:F4} for the rays and reset after");
            var hitsList = new List<string>();
            cam.aspect = 960f / 540f;
            try
            {
                // P5 controls
                var bodyPixels = new List<(int x, int yTop)>();
                for (int yt = 0; yt < br.h; yt += 1) for (int xx = 0; xx < br.w; xx += 1) if (!br.IsMagenta(xx, yt)) bodyPixels.Add((xx, yt));
                bool ctlBody = false, ctlCorner = false;
                if (bodyPixels.Count > 0)
                {
                    var cp = Nearest(bodyPixels, 480, 270);
                    string t1 = CastR3(tris, PixelRay(cam, cp.x, cp.yTop), out bool hit1, out _);
                    ctlBody = hit1;
                    probe.AppendLine($"control A (body pixel nearest the centre, ({cp.x}, {cp.yTop})): {t1} -> {(hit1 ? "bites" : "FAILS: must hit the body")}");
                }
                else probe.AppendLine("control A: the body-only render has no body pixel -> FAILS");
                bool cornerIsMagenta = br.IsMagenta(0, 0);
                string t2 = CastR3(tris, PixelRay(cam, 0, 0), out bool hit2, out _);
                ctlCorner = cornerIsMagenta && !hit2;
                probe.AppendLine($"control B (image corner (0, 0), magenta in the render: {cornerIsMagenta}): {t2} -> {(ctlCorner ? "bites" : "FAILS: must be magenta and hit nothing")}");
                if (!(ctlBody && ctlCorner)) { probe.AppendLine($"PROBE VOID: mapping ({pose}: a control failed, the pixel to ray mapping is wrong)"); return; }

                int totalRays = 0;
                foreach (var comp in br.interior)
                {
                    // td-49 B20b: EVERY member pixel of the component is probed (a magenta pixel means no FRONT face covers its centre, but a culled BACK face can: that is a hole)
                    probe.AppendLine($"component {comp.id}: size {comp.size}, centroid ({comp.cx:F1}, {comp.cyTop:F1}), bbox x {comp.x0}..{comp.x1}, y(top) {comp.y0Top}..{comp.y1Top}");
                    int noHit = 0, shown = 0, notShown = 0;
                    var byVerdict = new SortedDictionary<string, int>();
                    foreach (var sp in comp.members)
                    {
                        string txt = CastR3(tris, PixelRay(cam, sp.x, sp.yTop), out bool anyHit, out string vname);
                        totalRays++;
                        if (!anyHit) { noHit++; continue; }
                        byVerdict[vname] = byVerdict.TryGetValue(vname, out int nv) ? nv + 1 : 1;
                        string line = $"{pose} component {comp.id} (size {comp.size}) pixel ({sp.x}, {sp.yTop}): {txt}";
                        hitsList.Add(line);
                        if (shown < 20) { probe.AppendLine("    " + line); shown++; } else notShown++;
                    }
                    if (notShown > 0) probe.AppendLine($"    ... and {notShown} more hit pixels of this component not listed");
                    probe.AppendLine($"  component {comp.id} SUMMARY: rays {comp.members.Count}, NO HIT {noHit}, body hits {comp.members.Count - noHit}{(byVerdict.Count > 0 ? " by verdict [" + string.Join(", ", byVerdict.Select(kv => kv.Key + " " + kv.Value)) + "]" : "")} -> {(noHit == comp.members.Count ? "NON-DEFECT (every pixel printed no body hit: a gap between parts)" : "BODY HIT IN INTERIOR COMPONENT")}");
                }
                probe.AppendLine($"probe rays for {pose}: {totalRays}; elapsed {clock.ElapsedMilliseconds} ms (bake, controls and rays)");
                probe.AppendLine($"BODY HIT IN INTERIOR COMPONENT ({pose}): {hitsList.Count}");
                probe.AppendLine("  (detail lines of the hit pixels are printed under their component, at most 20 per component)");
            }
            finally { cam.ResetAspect(); }
        }

        IEnumerator Poses(StringBuilder probe)
        {
            PinTicks();
            string dir = RunOutput.Dir("standidle");
            var index = new StringBuilder("# " + WashedAshore.Tests.Support.MirrorTie.StateLine + System.Environment.NewLine);
            var poses = new (string name, StanceKey key, Stance stance, float pitch)[]
            {
                ("Stand_Idle_p85", StanceKey.None, Stance.Stand, 85f),
                ("Crouch_Idle_p75", StanceKey.Crouch, Stance.Crouch, 75f),
                ("Sit_Loop_p75", StanceKey.Sit, Stance.Sit, 75f),
                ("Squat_Hold_p85", StanceKey.Squat, Stance.Squat, 85f),
            };
            foreach (var pose in poses)
            {
                yield return LoadWorld();
                yield return OnFloor();
                var view = player.GetComponent<PlayerCharacterView>();
                Assert.IsNotNull(view?.FirstPerson, "no first-person body on the Player");
                var fp = view.FirstPerson;
                if (pose.key != StanceKey.None) yield return Go(pose.key, 6f);
                Assert.AreEqual(pose.stance, player.Stance, $"{pose.name}: could not reach {pose.stance}: {player.EntryBlockedBy(pose.stance)}");
                yield return new WaitForSeconds(2f);
                player.SetLook(player.Yaw, pose.pitch);
                yield return null;
                yield return null;
                var cam = player.ViewCamera;
                var ct = cam.transform;
                Light sun = null;
                foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None)) if (l.type == LightType.Directional) { sun = l; break; }
                var va = view.ViewCopy != null && view.ViewCopy.ViewAnimator != null ? view.ViewCopy.ViewAnimator : view.Body.Animator;
                var info = va.GetCurrentAnimatorStateInfo(0);
                string clip = va.GetCurrentAnimatorClipInfo(0).OrderByDescending(c => c.weight).Select(c => c.clip.name).FirstOrDefault() ?? "";
                index.AppendLine($"== {pose.name}: stance {player.Stance}; view clip {clip}; normalizedTime {F(info.normalizedTime)}; pitch {pose.pitch:F0}");
                index.AppendLine($"camera position {V3(ct.position)}; euler {V3(ct.eulerAngles)}; FOV {F(cam.fieldOfView)}; eye height above the player origin {F(ct.position.y - player.transform.position.y)}");
                index.AppendLine($"light {(sun != null ? "euler " + V3(sun.transform.eulerAngles) + "; intensity " + F(sun.intensity) : "none")}");
                index.AppendLine($"look-down table offset in force (back; down, metres): {F(fp.LastLookDownOffset.x)}; {F(fp.LastLookDownOffset.y)}");
                index.AppendLine("view-body renderers: " + RendererList(fp.ViewRenderers));
                index.AppendLine("shadow-body renderers (fp.BodyRenderers): " + RendererList(fp.BodyRenderers));
                var lit = Render(cam, 960, 540);
                index.AppendLine($"magenta-threshold pixels in the lit still (must be 0): {CountMagenta(lit)}");
                File.WriteAllBytes($"{dir}/pose_{pose.name}.png", lit.EncodeToPNG());
                Object.Destroy(lit);
                var br = BodyOnly(cam, player.gameObject.layer, dir, "pose_" + pose.name, index);
                if (probe != null) ProbeInteriors(probe, fp, cam, pose.name, br);   // same frame: no yield between the render above, the body-only pass and these rays
            }
            File.WriteAllText($"{dir}/standidle-poses-index.txt", index.ToString());
            if (probe != null) File.WriteAllText($"{dir}/standidle-poses-probe.txt", probe.ToString());
            Debug.Log("StandIdleStill poses written to " + dir);
        }

        static readonly Color Magenta = new Color(1f, 0f, 1f, 1f);

        /// <summary>qa-49: the lit stills must contain no exact magenta (255,0,255), otherwise a body-only count could be fooled by scene colours.</summary>
        static string CountMagenta(Texture2D tex)
        {
            int exact = 0, thresh = 0;
            foreach (var c in tex.GetPixels32())
            {
                if (c.r == 255 && c.g == 0 && c.b == 255) exact++;
                if (c.r > 229 && c.g < 26 && c.b > 229) thresh++;
            }
            return $"threshold (r>229, g<26, b>229) {thresh}, exact (255,0,255) {exact}";
        }

        /// <summary>One line per renderer of a list: name, enabled, shadowCastingMode (td-49 A2).</summary>
        static string RendererList(System.Collections.Generic.IEnumerable<Renderer> rs) =>
            string.Join("; ", rs.Where(r => r != null).Select(r => $"{r.name} enabled={r.enabled} shadowMode={r.shadowCastingMode}"));

        /// <summary>
        /// Renders a copy of <paramref name="source"/> with cullingMask = the given layer only and a solid magenta background, writes <paramref name="name"/>.bodyonly.png and records in the index: the body pixel count; the
        /// INTERIOR magenta components (8-connected, not connected to the image border: fully enclosed by body pixels = see-through) with size, centroid and bounding box (pixels, y from the top); and the candidate NOTCHES:
        /// background pixels that a 5x5 morphological closing of the body fills (thin slits and dents of the silhouette up to 4 px wide), 4-connected components of at least 4 px. A magenta pixel is r > 0.9, g < 0.1, b > 0.9.
        /// </summary>
        /// <summary>An interior magenta component of a body-only pass (enclosed by body pixels; the existing 8-connected non-border finder): id, size, centroid, bounding box and EVERY member pixel (x from the left, y from the TOP).</summary>
        class InteriorComp
        {
            public int id, size, x0, x1, y0Top, y1Top;
            public double cx, cyTop;
            public List<(int x, int yTop)> members = new List<(int, int)>();
        }

        /// <summary>The body-only pass: its interior components and the magenta mask (bg[y * w + x], y from the BOTTOM as in the texture) for the probe controls.</summary>
        class BodyOnlyResult
        {
            public List<InteriorComp> interior = new List<InteriorComp>();
            public bool[] bg;
            public int w, h;
            public bool IsMagenta(int x, int yTop) => bg[(h - 1 - yTop) * w + x];
        }

        static BodyOnlyResult BodyOnly(Camera source, int layer, string dir, string name, StringBuilder index)
        {
            var result = new BodyOnlyResult();
            var go = new GameObject("StandIdleBodyOnly");
            try
            {
                var c = go.AddComponent<Camera>();
                c.CopyFrom(source);
                c.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
                c.cullingMask = 1 << layer;
                c.clearFlags = CameraClearFlags.SolidColor;
                c.backgroundColor = Magenta;
                var tex = Render(c, 960, 540);
                File.WriteAllBytes($"{dir}/{name}.bodyonly.png", tex.EncodeToPNG());
                int w = tex.width, h = tex.height;
                var px = tex.GetPixels32();
                Object.Destroy(tex);
                var bg = new bool[w * h];   // y = 0 is the bottom row of the texture
                result.bg = bg; result.w = w; result.h = h;
                int body = 0;
                for (int i = 0; i < px.Length; i++)
                {
                    bg[i] = px[i].r > 229 && px[i].g < 26 && px[i].b > 229;
                    if (!bg[i]) body++;
                }
                index.AppendLine($"{name} body-only: body pixels {body} of {w * h}");
                var label = new int[w * h];
                var comps = new System.Collections.Generic.List<(int size, double cx, double cy, int x0, int y0, int x1, int y1, bool border)>();
                for (int start = 0; start < bg.Length; start++)
                {
                    if (!bg[start] || label[start] != 0) continue;
                    int id = comps.Count + 1, size = 0, x0 = w, y0 = h, x1 = -1, y1 = -1;
                    double sx = 0, sy = 0;
                    bool border = false;
                    var members = new List<int>();
                    var stack = new System.Collections.Generic.Stack<int>();
                    stack.Push(start); label[start] = id;
                    while (stack.Count > 0)
                    {
                        int i = stack.Pop(), x = i % w, y = i / w;
                        size++; sx += x; sy += y; members.Add(i);
                        if (x < x0) x0 = x; if (x > x1) x1 = x; if (y < y0) y0 = y; if (y > y1) y1 = y;
                        if (x == 0 || y == 0 || x == w - 1 || y == h - 1) border = true;
                        if (x > 0 && bg[i - 1] && label[i - 1] == 0) { label[i - 1] = id; stack.Push(i - 1); }
                        if (x < w - 1 && bg[i + 1] && label[i + 1] == 0) { label[i + 1] = id; stack.Push(i + 1); }
                        if (y > 0 && bg[i - w] && label[i - w] == 0) { label[i - w] = id; stack.Push(i - w); }
                        if (y < h - 1 && bg[i + w] && label[i + w] == 0) { label[i + w] = id; stack.Push(i + w); }
                        if (x > 0 && y > 0 && bg[i - w - 1] && label[i - w - 1] == 0) { label[i - w - 1] = id; stack.Push(i - w - 1); }
                        if (x < w - 1 && y > 0 && bg[i - w + 1] && label[i - w + 1] == 0) { label[i - w + 1] = id; stack.Push(i - w + 1); }
                        if (x > 0 && y < h - 1 && bg[i + w - 1] && label[i + w - 1] == 0) { label[i + w - 1] = id; stack.Push(i + w - 1); }
                        if (x < w - 1 && y < h - 1 && bg[i + w + 1] && label[i + w + 1] == 0) { label[i + w + 1] = id; stack.Push(i + w + 1); }
                    }
                    comps.Add((size, sx / size, sy / size, x0, y0, x1, y1, border));
                    if (!border)
                    {
                        var ic = new InteriorComp { id = result.interior.Count + 1, size = size, cx = sx / size, cyTop = h - 1 - sy / size, x0 = x0, x1 = x1, y0Top = h - 1 - y1, y1Top = h - 1 - y0 };
                        foreach (int m in members) ic.members.Add((m % w, h - 1 - m / w));
                        result.interior.Add(ic);
                    }
                }
                var interior = comps.Where(k => !k.border).ToList();
                index.AppendLine($"{name} interior magenta components (enclosed by body pixels): {interior.Count}");
                foreach (var k in interior) index.AppendLine($"  interior size {k.size}, centroid ({k.cx:F1}, {h - 1 - k.cy:F1}), bbox x {k.x0}..{k.x1}, y(top) {h - 1 - k.y1}..{h - 1 - k.y0}");
                // closing of the body mask (dilate 2, erode 2, square), notches = background pixels the closing fills that are NOT in an interior component
                var bodyMask = new bool[w * h];
                for (int i = 0; i < bodyMask.Length; i++) bodyMask[i] = !bg[i];
                var closed = Erode(Dilate(bodyMask, w, h, 2), w, h, 2);
                var notch = new bool[w * h];
                for (int i = 0; i < notch.Length; i++) notch[i] = closed[i] && bg[i] && !(label[i] > 0 && !comps[label[i] - 1].border);
                var seen = new bool[w * h];
                int n = 0;
                var lines = new System.Collections.Generic.List<string>();
                for (int start = 0; start < notch.Length; start++)
                {
                    if (!notch[start] || seen[start]) continue;
                    int size = 0, x0 = w, y0 = h, x1 = -1, y1 = -1;
                    double sx = 0, sy = 0;
                    var stack = new System.Collections.Generic.Stack<int>();
                    stack.Push(start); seen[start] = true;
                    while (stack.Count > 0)
                    {
                        int i = stack.Pop(), x = i % w, y = i / w;
                        size++; sx += x; sy += y;
                        if (x < x0) x0 = x; if (x > x1) x1 = x; if (y < y0) y0 = y; if (y > y1) y1 = y;
                        if (x > 0 && notch[i - 1] && !seen[i - 1]) { seen[i - 1] = true; stack.Push(i - 1); }
                        if (x < w - 1 && notch[i + 1] && !seen[i + 1]) { seen[i + 1] = true; stack.Push(i + 1); }
                        if (y > 0 && notch[i - w] && !seen[i - w]) { seen[i - w] = true; stack.Push(i - w); }
                        if (y < h - 1 && notch[i + w] && !seen[i + w]) { seen[i + w] = true; stack.Push(i + w); }
                    }
                    if (size >= 4) { n++; lines.Add($"  notch size {size}, centroid ({sx / size:F1}, {h - 1 - sy / size:F1}), bbox x {x0}..{x1}, y(top) {h - 1 - y1}..{h - 1 - y0}"); }
                }
                index.AppendLine($"{name} candidate notches (>= 4 px, closed by a 5x5 closing of the body): {n}");
                foreach (var l in lines) index.AppendLine(l);
                return result;
            }
            finally { Object.Destroy(go); }
        }

        static bool[] Dilate(bool[] a, int w, int h, int r)
        {
            var tmp = new bool[a.Length]; var o = new bool[a.Length];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    bool v = false;
                    for (int d = -r; d <= r && !v; d++) { int xx = x + d; if (xx >= 0 && xx < w && a[y * w + xx]) v = true; }
                    tmp[y * w + x] = v;
                }
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    bool v = false;
                    for (int d = -r; d <= r && !v; d++) { int yy = y + d; if (yy >= 0 && yy < h && tmp[yy * w + x]) v = true; }
                    o[y * w + x] = v;
                }
            return o;
        }

        static bool[] Erode(bool[] a, int w, int h, int r)
        {
            var inv = new bool[a.Length];
            for (int i = 0; i < a.Length; i++) inv[i] = !a[i];
            var d = Dilate(inv, w, h, r);   // erosion of a = complement of the dilation of its complement; the border counts as body-free outside, so edge pixels are not eroded away
            var o = new bool[a.Length];
            for (int i = 0; i < a.Length; i++) o[i] = !d[i];
            return o;
        }

        static Texture2D Render(Camera c, int w, int h)
        {
            var rt = new RenderTexture(w, h, 24);
            var old = c.targetTexture;
            try
            {
                c.targetTexture = rt;
                c.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply();
                return tex;
            }
            finally
            {
                c.targetTexture = old;
                RenderTexture.active = null;
                rt.Release();
                Object.Destroy(rt);
            }
        }

        static void Outline(Texture2D tex, int cx, int cy, int half)
        {
            for (int t = 0; t < 3; t++)
                for (int d = -half; d <= half; d++)
                {
                    Put(tex, cx + d, cy - half + t); Put(tex, cx + d, cy + half - t);
                    Put(tex, cx - half + t, cy + d); Put(tex, cx + half - t, cy + d);
                }
        }

        static void Put(Texture2D tex, int x, int y)
        {
            if (x >= 0 && x < tex.width && y >= 0 && y < tex.height) tex.SetPixel(x, y, Color.red);
        }
    }
}
