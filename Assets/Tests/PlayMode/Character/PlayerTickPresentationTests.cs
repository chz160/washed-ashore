using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using WashedAshore.Character.Stance;
using WashedAshore.Gameplay;
using WashedAshore.Tests.Support;

namespace WashedAshore.Tests.PlayMode.Character
{
    /// <summary>
    /// #46 P2 and P6, the view side of the fixed tick (spec 94f34aa6, 7 T2b and T6): the camera and the body sit between the last two tick poses
    /// at 144 fps, and the animator drivers come from the simulated velocity. Known-fail cases are [Explicit] [Category("KnownFail46")]; each must
    /// FAIL and prints the value that failed.
    /// </summary>
    public class PlayerTickPresentationTests
    {
        float savedCapture;
        PlayerController player;
        float yaw0;

        [SetUp]
        public void SetUp() => savedCapture = Time.captureDeltaTime;

        [TearDown]
        public void TearDown()
        {
            Time.captureDeltaTime = savedCapture;
            PlayerController.TestInterpolationOff = false;
            PlayerCharacterView.TestTransformDeltaDrive = false;
        }

        IEnumerator StartGround(float frameTime, System.Func<int, PlayerCommand> stream)
        {
            Time.captureDeltaTime = frameTime;
            SceneManager.LoadScene(TestSpawn.WorldScene, LoadSceneMode.Single);
            yield return null;
            yield return null;
            player = TestSpawn.Player();
            yield return TestSpawn.At(TestSpawn.PlayerSpawnPoint);
            yaw0 = player.transform.eulerAngles.y;
            int start = player.Tick;
            player.CommandSource = tickNo => stream(tickNo - start);
        }

        PlayerCommand Walk(int s) => new PlayerCommand { Yaw = yaw0, Move = s >= 31 ? Vector2.up : Vector2.zero };

        // ---- T2b: interpolation -----------------------------------------------------------------------------------------

        [UnityTest, Timeout(600000)]
        public IEnumerator T2b_Interpolation_CameraAndBodyBetweenTicks() => Interpolation(false);

        /// <summary>KF-T2b: no interpolation; about 58% of the frames run no tick and the camera does not move on them, so check (2) must FAIL.</summary>
        [UnityTest, Timeout(600000), Explicit("Known-fail: must FAIL"), Category("KnownFail46")]
        public IEnumerator KnownFail_T2b_InterpolationOff() => Interpolation(true);

        static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float len2 = ab.sqrMagnitude;
            float t = len2 < 1e-12f ? 0f : Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2);
            return Vector2.Distance(p, a + ab * t);
        }

        static Vector2 H(Vector3 v) => new Vector2(v.x, v.z);

        IEnumerator Interpolation(bool off)
        {
            PlayerController.TestInterpolationOff = off;
            yield return StartGround(1f / 144f, Walk);
            Transform pivot = player.CameraPivot;
            var view = player.GetComponent<PlayerCharacterView>();
            Transform shadow = view != null && view.FirstPerson != null ? view.FirstPerson.ShadowRoot : null;
            Assert.IsNotNull(shadow, "no first-person shadow body on the Player");
            Vector3 authored = pivot.localPosition;            // the player has not moved yet: the offset is zero
            Vector3 shadowRest = shadow.position - player.transform.position;
            var positions = new List<Vector3>();
            player.CommandExecuted += _ => positions.Add(player.transform.position);
            while (positions.Count < 60) yield return null;    // stream tick 60 and later: walking at 5 m/s
            yield return null;
            float worstSeg = 0f, worstShadow = 0f, minStep = float.MaxValue, maxStep = 0f, worstBack = 0f;
            Vector2 dir = H(player.transform.forward).normalized;
            Vector2 last = H(pivot.position - player.transform.rotation * new Vector3(authored.x, 0f, authored.z));
            for (int f = 0; f < 144; f++)
            {
                yield return null;
                int k = positions.Count;
                Vector2 pk = H(positions[k - 1]), pk1 = H(positions[k - 2]);
                Vector2 c = H(pivot.position - player.transform.rotation * new Vector3(authored.x, 0f, authored.z));
                Vector2 sh = H(shadow.position - shadowRest);
                worstSeg = Mathf.Max(worstSeg, DistanceToSegment(c, pk1, pk));
                worstShadow = Mathf.Max(worstShadow, DistanceToSegment(sh, pk1, pk));
                float step = Vector2.Distance(c, last);
                minStep = Mathf.Min(minStep, step);
                maxStep = Mathf.Max(maxStep, step);
                worstBack = Mathf.Min(worstBack, Vector2.Dot(c - last, dir));
                last = c;
            }
            Debug.Log($"T2b interpolationOff {off}: camera off the tick segment by at most {worstSeg:F5} m, shadow body {worstShadow:F5} m; per-frame camera step {minStep:F4} to {maxStep:F4} m; most backward step {worstBack:F5} m");
            Assert.LessOrEqual(worstSeg, 0.001f, $"(1) the camera left the segment between the last two tick poses by {worstSeg:F5} m");
            Assert.LessOrEqual(worstShadow, 0.001f, $"(1) the shadow body left the segment by {worstShadow:F5} m");
            Assert.GreaterOrEqual(minStep, 0.5f * 5f / 144f, $"(2) a frame's camera step was only {minStep:F4} m (limit {0.5f * 5f / 144f:F4})");
            Assert.LessOrEqual(maxStep, 1.5f * 5f / 144f, $"(2) a frame's camera step was {maxStep:F4} m (limit {1.5f * 5f / 144f:F4})");
            Assert.GreaterOrEqual(worstBack, -0.0001f, $"(3) the camera moved backward by {-worstBack:F5} m in a frame");
        }

        // ---- T6: the animator drivers ------------------------------------------------------------------------------------

        [UnityTest, Timeout(600000)]
        public IEnumerator T6_Animator_DrivenBySimVelocity() => Animator6();

        /// <summary>KF-T6: the driver reads the transform change between frames again; Speed reads 0 on no-tick frames and spikes on tick frames, so the case must FAIL.</summary>
        [UnityTest, Timeout(600000), Explicit("Known-fail: must FAIL"), Category("KnownFail46")]
        public IEnumerator KnownFail_T6_TransformDeltaDrive()
        {
            PlayerCharacterView.TestTransformDeltaDrive = true;
            yield return Animator6();
        }

        IEnumerator Animator6()
        {
            yield return StartGround(1f / 144f, Walk);
            var view = player.GetComponent<PlayerCharacterView>();
            Assert.IsNotNull(view?.Body?.Animator, "the Player has no animated body");
            var anim = view.Body.Animator;
            foreach (var n in new[] { "Speed", "MoveX", "MoveZ" })
                Assert.IsTrue(System.Array.Exists(anim.parameters, p => p.name == n), $"the animator has no parameter {n}");
            int ticks = 0;
            player.CommandExecuted += _ => ticks++;
            while (ticks < 60) yield return null;
            float minSpeed = float.MaxValue, maxSpeed = 0f, worstDiff = 0f;
            for (int f = 0; f < 144; f++)
            {
                yield return null;
                Vector3 v = player.SimVelocity;
                Vector3 local = player.transform.InverseTransformDirection(v);
                float speed = anim.GetFloat("Speed"), mx = anim.GetFloat("MoveX"), mz = anim.GetFloat("MoveZ");
                minSpeed = Mathf.Min(minSpeed, speed);
                maxSpeed = Mathf.Max(maxSpeed, speed);
                worstDiff = Mathf.Max(worstDiff, Mathf.Abs(speed - new Vector2(v.x, v.z).magnitude), Mathf.Abs(mx - local.x), Mathf.Abs(mz - local.z));
            }
            Debug.Log($"T6 measured: Speed {minSpeed:F3} to {maxSpeed:F3} m/s over 144 frames at 1/144, worst difference from SimVelocity {worstDiff:F5}");
            Assert.LessOrEqual(worstDiff, 1e-3f, $"Speed, MoveX or MoveZ differ from SimVelocity by {worstDiff:F5}");
            Assert.GreaterOrEqual(minSpeed, 4.9f, $"Speed fell to {minSpeed:F3} m/s on a frame (max {maxSpeed:F3}): the animator sees a frame without a tick");
        }
    }
}
