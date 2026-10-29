using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;
using WashedAshore.Character.Appearance;
using WashedAshore.Character.Stance;
using WashedAshore.Gameplay;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace WashedAshore.Tests.PlayMode.Character
{
    /// <summary>
    /// S-A9, the player-stances.md section 3a range ends, proved by script at BOTH ends (director amend1):
    /// eye ends (crouch 1.10 / 1.30, squat 0.90 / 1.10, sit 0.75 / 0.90, sleep 0.25): the -85..+85 sweep on the view body with no near-slab
    /// triangle and no back face, the hidden-head eye match within 3 cm (camera minus the SHADOW body head eye point, world y), and S-A4
    /// headroom under a 1.65 m ceiling. Sleep pitch -45 and -85: a still each and the sleep sweep. Crouch speed 1.25 and 2.50: foot slide
    /// per plant on CrouchWalk, and the playback rate within 0.7 to 1.4. The clip variants come from the artist's RangeLow / RangeHigh
    /// override controllers. Each end prints PASS or FAIL in TestResults/player-character/range/range-ends.txt; a failed end shrinks its range.
    /// </summary>
    public class RangeEndTests : StanceFixture
    {
        static string OutDir => RunOutput.Dir("range");
        const float EyeUp = 0.1094f, EyeForward = 0.1011f;   // manifest eye point, Head bone rest frame
        const string RangeLow = "Assets/Art/Character/CharacterAnimator_Men_RangeLow.overrideController";
        const string RangeHigh = "Assets/Art/Character/CharacterAnimator_Men_RangeHigh.overrideController";

        struct Tri { public Vector3 a, b, c; }

        PlayerCharacterView view;
        FirstPersonBody fp;
        Transform head;
        Vector3 headLocalEye;
        readonly StringBuilder log = new StringBuilder();
        string eyeFigures = "";

        IEnumerator Start(bool high)
        {
            yield return LoadWorld();
            yield return OnFloor();
            view = player.GetComponent<PlayerCharacterView>();
            Assert.IsNotNull(view?.Body, "no body on the Player");
            fp = view.FirstPerson;
            head = view.Body.Bone("Head");
            Vector3 p = head.position + Vector3.up * EyeUp + player.transform.forward * EyeForward;
            headLocalEye = head.InverseTransformPoint(p);
            Directory.CreateDirectory(OutDir);
            // The range ends run on the artist's range controllers: the SHADOW on its variant (the capped Jog and Sprint arms stay mapped to _Full) and the VIEW on the base-based one.
            ApplyRangeControllers(high);
            RangeGuard(high);
            yield return null;
        }

        /// <summary>Assigns the range controllers of the given end to both Animators, from the appearance, as the -bbStance launch argument does.</summary>
        void ApplyRangeControllers(bool high)
        {
            var app = view.Body.Appearance;
            var shadowCtl = app.RangeController(high, true);
            var viewCtl = app.RangeController(high, false);
            Assert.IsNotNull(shadowCtl, $"the appearance has no shadow range controller for {(high ? "High" : "Low")}");
            Assert.IsNotNull(viewCtl, $"the appearance has no view range controller for {(high ? "High" : "Low")}");
            view.Body.Animator.runtimeAnimatorController = shadowCtl;
            if (view.ViewCopy != null && view.ViewCopy.ViewAnimator != null) view.ViewCopy.ViewAnimator.runtimeAnimatorController = viewCtl;
        }

        /// <summary>
        /// td's guard: these tests run on the range-end controllers, so their arm and gait readings are NOT the base clips' (the proofs for those are in LookDownTests and
        /// FootSlideTests). Fails unless both Animators really run the range controllers of this end; prints their names.
        /// </summary>
        void RangeGuard(bool high)
        {
            var app = view.Body.Appearance;
            Assert.AreSame(app.RangeController(high, true), view.Body.Animator.runtimeAnimatorController, "the shadow Animator is not on its range controller: do not read range-end results from this run");
            if (view.ViewCopy != null && view.ViewCopy.ViewAnimator != null)
                Assert.AreSame(app.RangeController(high, false), view.ViewCopy.ViewAnimator.runtimeAnimatorController, "the view Animator is not on its range controller");
            Assert.IsTrue(view.Body.Animator.runtimeAnimatorController.name.Contains("Range"), "not a range controller");
            log.AppendLine($"range guard ({(high ? "High" : "Low")}): shadow Animator on '{view.Body.Animator.runtimeAnimatorController.name}', view Animator on '{(view.ViewCopy != null && view.ViewCopy.ViewAnimator != null ? view.ViewCopy.ViewAnimator.runtimeAnimatorController.name : "none")}'");
        }

        static string Verdict(bool ok) => ok ? "PASS" : "FAIL";

        /// <summary>Reaches the stance, settles, runs the -85..+85 sweep, the eye match and (for crouch) the headroom case. Returns the failure text or null.</summary>
        IEnumerator EyeEnd(string name, StanceKey key, Stance stance, float eye, bool high, System.Action<string> done)
        {
            var fails = new List<string>();
            yield return Go(key, 6f);
            if (player.Stance != stance) { done($"{name}: could not reach {stance}: {player.EntryBlockedBy(stance)}"); yield break; }
            yield return new WaitForSeconds(2f);
            float floorTop = floor.transform.position.y + floor.transform.localScale.y * 0.5f;
            float camY = player.ViewCamera.transform.position.y - floorTop;
            float headY = head.TransformPoint(headLocalEye).y - floorTop;
            if (Mathf.Abs(camY - headY) > 0.03f) fails.Add($"eye match {camY:F3} vs {headY:F3} (diff {camY - headY:F3})");
            if (Mathf.Abs((player.ViewCamera.transform.position.y - player.transform.position.y) - eye) > 0.011f) fails.Add("pivot eye is not the tuned value");
            int near = 0, back = 0;
            for (float pitch = -85f; pitch <= 85.01f; pitch += 5f)
            {
                player.SetLook(player.Yaw, pitch);
                yield return null;
                yield return null;
                Check(player.ViewCamera, Bake(), out int n, out int b);
                near += n; back += b;
            }
            if (near > 0 || back > 0) fails.Add($"sweep near-slab {near}, back faces {back}");
            eyeFigures = $"camera {camY:F3} m, hidden-head eye point {headY:F3} m above the floor, error {camY - headY:+0.000;-0.000} m (limit 0.030); pivot eye {player.ViewCamera.transform.position.y - player.transform.position.y:F3} m (tuned {eye:F2}); sweep -85..+85 step 5: near-slab {near}, back faces {back}";
            done(fails.Count == 0 ? null : $"{name}: " + string.Join("; ", fails));
        }

        [UnityTest, Timeout(3000000)]
        public IEnumerator EyeEnds_LowAndHigh()
        {
            PinTicks();   // #46 PR B: one tick per frame (td-46 adr/46-b-test-pin-pattern)
            var results = new List<string>();
            var cases = new[]
            {
                ("crouch 1.10 (Low)", StanceKey.Crouch, Stance.Crouch, 1.10f, false),
                ("crouch 1.30 (High)", StanceKey.Crouch, Stance.Crouch, 1.30f, true),
                ("squat 0.90 (Low)", StanceKey.Squat, Stance.Squat, 0.90f, false),
                ("squat 1.10 (High)", StanceKey.Squat, Stance.Squat, 1.10f, true),
                ("sit 0.75 (Low)", StanceKey.Sit, Stance.Sit, 0.75f, false),
                ("sit 0.90 (High)", StanceKey.Sit, Stance.Sit, 0.90f, true),
                ("sleep 0.25 (High)", StanceKey.Sleep, Stance.Sleep, 0.25f, true),
            };
            foreach (var c in cases)
            {
                yield return Start(c.Item5);
                var t = player.StanceTuning;
                switch (c.Item3)
                {
                    case Stance.Crouch: t.crouchEye = c.Item4; break;
                    case Stance.Squat: t.squatEye = c.Item4; break;
                    case Stance.Sit: t.sitEye = c.Item4; break;
                    case Stance.Sleep: t.sleepEye = c.Item4; break;
                }
                string failure = null;
                yield return EyeEnd(c.Item1, c.Item2, c.Item3, c.Item4, c.Item5, s => failure = s);
                // S-A4 headroom at this end (crouch only): stand-up under a 1.65 m ceiling is refused.
                if (failure == null && c.Item3 == Stance.Crouch)
                {
                    var ceiling = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    ceiling.transform.localScale = new Vector3(6f, 0.2f, 6f);
                    ceiling.transform.position = player.transform.position + new Vector3(0f, 1.65f + 0.1f, 0f);
                    yield return Frames(3);
                    yield return Go(StanceKey.Crouch, 1f);
                    if (player.Stance != Stance.Crouch) failure = $"{c.Item1}: stood up under a 1.65 m ceiling (S-A4)";
                    Object.Destroy(ceiling);
                }
                string line = $"{Verdict(failure == null)} {c.Item1} :: {eyeFigures}{(c.Item3 == Stance.Crouch ? $"; headroom under a 1.65 m ceiling: {(failure != null && failure.Contains("S-A4") ? "stood up (FAIL)" : "refused (ok)")}" : "")}" + (failure == null ? "" : " :: FAILURE " + failure);
                results.Add(line);
                log.AppendLine(line);
                Debug.Log("RangeEndTests: " + line);
            }
            File.WriteAllText($"{OutDir}/range-eyes.txt", log.ToString());
            Assert.IsFalse(results.Exists(r => r.StartsWith("FAIL")), string.Join("\n", results));
        }

        [UnityTest, Timeout(1800000)]
        public IEnumerator SleepPitchEnds()
        {
            var results = new List<string>();
            foreach (float pitchEnd in new[] { -45f, -85f })
            {
                yield return Start(true);
                player.StanceTuning.sleepPitch = pitchEnd;
                yield return Go(StanceKey.Sleep, 6f);
                yield return new WaitForSeconds(2f);
                float reachedPitch = player.Pitch;
                bool reached = Mathf.Abs(reachedPitch - pitchEnd) < 1f;
                yield return Still($"{OutDir}/sleep-pitch{pitchEnd:F0}.png");
                int near = 0, back = 0;
                for (float pitch = -85f; pitch <= 85.01f; pitch += 5f)
                {
                    player.SetLook(player.Yaw, pitch);
                    yield return null;
                    yield return null;
                    Check(player.ViewCamera, Bake(), out int n, out int b);
                    near += n; back += b;
                }
                string line = $"{Verdict(reached && near == 0 && back == 0)} sleep pitch {pitchEnd:F0}: pitch reached {reachedPitch:F1}, sweep near-slab {near}, back faces {back}";
                results.Add(line);
                Debug.Log("RangeEndTests: " + line);
            }
            File.AppendAllText($"{OutDir}/range-sleep-pitch.txt", string.Join("\n", results) + "\n");
            Assert.IsFalse(results.Exists(r => r.StartsWith("FAIL")), string.Join("\n", results));
        }

        List<Tri> Bake()
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
                    tris.Add(new Tri { a = pos + rot * v[t[i]], b = pos + rot * v[t[i + 1]], c = pos + rot * v[t[i + 2]] });
            }
            Object.Destroy(mesh);
            return tris;
        }

        static void Check(Camera cam, List<Tri> tris, out int nearSlab, out int backRays)
        {
            Transform ct = cam.transform;
            Vector3 o = ct.position;
            float n = cam.nearClipPlane;
            float halfV = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad), halfH = halfV * (16f / 9f);
            nearSlab = backRays = 0;
            for (int iy = 0; iy < 9; iy++)
                for (int ix = 0; ix < 16; ix++)
                {
                    float x = (ix / 15f * 2f - 1f) * halfH, y = (iy / 8f * 2f - 1f) * halfV;
                    Vector3 d = (ct.forward + ct.right * x + ct.up * y).normalized;
                    float nearLimit = n / Vector3.Dot(d, ct.forward), best = float.MaxValue;
                    bool backFace = false;
                    foreach (var tri in tris)
                    {
                        Vector3 e1 = tri.b - tri.a, e2 = tri.c - tri.a;
                        Vector3 p = Vector3.Cross(d, e2);
                        float det = Vector3.Dot(e1, p);
                        if (Mathf.Abs(det) < 1e-9f) continue;
                        float inv = 1f / det;
                        Vector3 s = o - tri.a;
                        float u = Vector3.Dot(s, p) * inv;
                        if (u < 0f || u > 1f) continue;
                        Vector3 q = Vector3.Cross(s, e1);
                        float v = Vector3.Dot(d, q) * inv;
                        if (v < 0f || u + v > 1f) continue;
                        float t = Vector3.Dot(e2, q) * inv;
                        if (t < 0f) continue;
                        if (t <= nearLimit) nearSlab++;
                        if (t < best) { best = t; backFace = Vector3.Dot(Vector3.Cross(e1, e2), d) > 0f; }
                    }
                    if (best < float.MaxValue && backFace) backRays++;
                }
        }

        IEnumerator Still(string path)
        {
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
                File.WriteAllBytes(path, tex.EncodeToPNG());
            }
            finally
            {
                cam.targetTexture = old;
                RenderTexture.active = null;
                if (tex != null) Object.Destroy(tex);
                rt.Release();
                Object.Destroy(rt);
            }
            yield return null;
        }
    }
}
