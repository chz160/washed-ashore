using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using WashedAshore.Gameplay;
using WashedAshore.World;

namespace WashedAshore.Tests.PlayMode
{
    /// <summary>
    /// Spec W6 (water-swim-spec.md §7): the real PlayerController walks from a low bank into the river
    /// (speed vs depth), swims 50 m of open water at the surface, swims back and climbs out (one Wade/Swim
    /// flip each way); sprint-jumps in; and climbs out at 10 seeded low-bank stations plus the low-bank
    /// stations either side of each bluff and the one nearest the west neck corner, from 20 m out. Bluff
    /// faces are not exits, but must not trap a swimmer. Thresholds come from the scene's SwimTuning
    /// asset and PlayerController at test time. Game time is pinned at 1/60 s. Exit runs go to
    /// TestResults/water-w6-exits.csv.
    /// </summary>
    public partial class WaterSwimTests : InputTestFixture
    {
        const string WorldScene = "World";
        const int Seed = 20261006;           // logged; change only with qa
        const int RandomStations = 10;
        const float ExitStart = 20f;         // §7.5: swim straight at shore from 20 m out
        const float ExitDeadline = 15f;      // §7.5: dry ground >= W + lip within 15 s
        const float StallSpeed = 0.2f;       // §7.5: horizontal speed below this with input held ...
        const float StallSeconds = 1f;       // ... for longer than this is a stall
        const float SwimDistance = 50f;      // §7.2
        const float OpenDepth = 3f;          // §7.2: open water
        const float SouthOfLine = 60f;       // keep these runs clear of the north line
        static readonly Vector3 WestNeckCorner = new Vector3(-1419f, 0f, 1970f); // the corner itself is north of the line

        Keyboard keyboard;
        Camera disabledCamera;
        PlayerController player;
        SwimTuning tuning;
        WaterBody water;
        MapConfig cfg;
        WorldBoundsRule rule;
        int fallThroughs;

        public override void Setup()
        {
            base.Setup();
            keyboard = InputSystem.AddDevice<Keyboard>();
            Time.captureDeltaTime = 1f / 60f;
            Application.logMessageReceived += OnLog;
        }

        public override void TearDown()
        {
            Application.logMessageReceived -= OnLog;
            Time.captureDeltaTime = 0f;
            if (disabledCamera) disabledCamera.enabled = true;
            base.TearDown();
        }

        void OnLog(string message, string stack, LogType type)
        {
            if (message.StartsWith("PlayerController: fell below terrain")) fallThroughs++;
        }

        IEnumerator LoadWorld()
        {
            SceneManager.LoadScene(WorldScene, LoadSceneMode.Single);
            yield return null;
            yield return null;
            disabledCamera = Camera.main;
            if (disabledCamera) disabledCamera.enabled = false;
            player = WorldBoundsTestKit.Player();
            tuning = player.SwimTuning;
            Assert.IsNotNull(tuning, "PlayerController has no SwimTuning asset");
            water = WaterTestKit.Water();
            cfg = water.Config;
            rule = WorldBoundsTestKit.Clamp().Rule;
            fallThroughs = 0;
            Assert.IsNull(tuning.Validate(player.WalkSpeed, player.EyeHeight), "SwimTuning with the live controller");
        }

        void ReleaseAll()
        {
            if (keyboard.wKey.isPressed) Release(keyboard.wKey);
            if (keyboard.leftShiftKey.isPressed) Release(keyboard.leftShiftKey);
            if (keyboard.spaceKey.isPressed) Release(keyboard.spaceKey);
        }

        /// <summary>A seeded low-bank station with open water offshore: wet for 80 m, at least OpenDepth from 15 m out.</summary>
        bool OpenWaterStation(float w, out Vector3 shore, out Vector3 dir)
        {
            var low = WaterTestKit.LowBank(WaterTestKit.Stations(), rule, SouthOfLine);
            foreach (var s in WaterTestKit.Pick(low, low.Count, Seed))
            {
                if (!WaterTestKit.Offshore(s.Position, w, 80f, out dir)) continue;
                bool deep = true;
                for (float r = 15f; r <= 80f && deep; r += 1f)
                    deep = WaterTestKit.Ground(s.Position + dir * r, out float h) && w - h >= OpenDepth;
                if (!deep || rule.NorthOfLine(s.Position + dir * 80f) > -SouthOfLine) continue;
                shore = s.Position;
                Debug.Log($"WaterSwimTests: open-water station {s} bearing {WaterTestKit.Yaw(dir):F0} (seed {Seed})");
                return true;
            }
            shore = dir = default;
            return false;
        }

        /// <summary>
        /// §7.1-7.4: walk in from 6 m inland (speed vs depth), swim 50 m of open water (eye 0.35 +- 0.1 above S,
        /// never grounded, no flips, 27-30 s), turn, swim back and climb out. Modes: Dry, Wade, Swim, Wade, Dry.
        /// </summary>
        [UnityTest, Timeout(900000)]
        public IEnumerator WalkIn_Swim50m_AndBack()
        {
            yield return LoadWorld();
            float w = cfg.WaterLevelY, eyeAbove = player.EyeHeight - tuning.floatDepth;
            Assert.IsTrue(OpenWaterStation(w, out var shore, out var dir), "No low-bank station with 80 m of open water offshore");
            var start = shore - dir * 6f;
            Assert.IsTrue(WaterTestKit.Ground(start, out float g0) && g0 > w + 0.5f, $"walk-in start {start:F1} is not dry land");
            WorldBoundsTestKit.Teleport(player, start, WaterTestKit.Yaw(dir));
            yield return new WaitForSeconds(0.25f);

            var modes = new List<WaterMode> { player.WaterMode };
            var dry = new List<float>(); var at03 = new List<float>(); var at10 = new List<float>();
            var swimSpeeds = new List<float>();
            float openSwum = 0f, openTime = 0f, worstEye = 0f, t = 0f;
            int groundedDeep = 0, openFlips = 0;
            bool open = false, turned = false, outAgain = false;
            Vector3 last = player.transform.position;
            float deadline = 120f;
            Press(keyboard.wKey);
            try
            {
                while (t < deadline && !outAgain)
                {
                    yield return null;
                    t += Time.deltaTime;
                    var p = player.transform.position;
                    float step = WaterTestKit.Flat(p - last).magnitude, speed = step / Time.deltaTime;
                    last = p;
                    var mode = player.WaterMode;
                    if (modes[modes.Count - 1] != mode) { modes.Add(mode); if (open && !turned) openFlips++; }
                    if (!turned)
                    {
                        if (mode == WaterMode.Dry && t > 0.5f) dry.Add(speed);
                        if (mode == WaterMode.Wade && Mathf.Abs(player.WaterDepth - 0.3f) < 0.03f) at03.Add(speed);
                        if (mode == WaterMode.Wade && Mathf.Abs(player.WaterDepth - 1.0f) < 0.03f) at10.Add(speed);
                    }
                    if (mode == WaterMode.Swim && player.WaterDepth >= tuning.swimEnterDepth && player.IsGrounded)
                    {
                        if (groundedDeep++ == 0) Debug.Log($"WaterSwimTests[walk-in]: first grounded swim frame at t={t:F2} depth={player.WaterDepth:F2} turned={turned}");
                    }
                    if (!turned && mode == WaterMode.Swim && player.WaterDepth >= OpenDepth) open = true;
                    if (open && !turned)
                    {
                        openSwum += step;
                        openTime += Time.deltaTime;
                        swimSpeeds.Add(speed);
                        worstEye = Mathf.Max(worstEye, Mathf.Abs(p.y + player.EyeHeight - player.SurfaceY - eyeAbove));
                        Assert.Greater(p.y + player.EyeHeight, player.SurfaceY, "§7.4: camera went under the surface");
                        if (openSwum >= SwimDistance)
                        {
                            // Turn back toward the shore (yaw only; the input stays held).
                            turned = true;
                            player.transform.rotation = Quaternion.Euler(0f, WaterTestKit.Yaw(-dir), 0f);
                        }
                    }
                    if (turned && mode == WaterMode.Dry && player.IsGrounded && p.y >= w + cfg.bankLipAboveWater) outAgain = true;
                }
            }
            finally { ReleaseAll(); }

            float strokeMean = WaterTestKit.StrokeMean(swimSpeeds, 1f / tuning.strokeSurgeHz, 3f);
            Debug.Log($"WaterSwimTests[walk-in]: modes={string.Join(">", modes)} dry={WaterTestKit.Median(dry):F2} at0.3={WaterTestKit.Median(at03):F2} " +
                      $"at1.0={WaterTestKit.Median(at10):F2} swimStrokeMean={strokeMean:F2} open={openSwum:F1} m in {openTime:F1} s worstEye={worstEye:F3} " +
                      $"groundedDeep={groundedDeep} openFlips={openFlips} fallThroughs={fallThroughs}");
            CollectionAssert.AreEqual(new[] { WaterMode.Dry, WaterMode.Wade, WaterMode.Swim, WaterMode.Wade, WaterMode.Dry }, modes,
                "§7.3: one Wade/Swim flip each way, out and back");
            Assert.AreEqual(player.WalkSpeed, WaterTestKit.Median(dry), 0.1f, "§7.1 dry walk");
            Assert.AreEqual(player.WalkSpeed * tuning.wadeSpeedByDepth.Evaluate(0.3f), WaterTestKit.Median(at03), 0.2f, "§7.1 wade at 0.3 m");
            Assert.AreEqual(player.WalkSpeed * tuning.wadeSpeedByDepth.Evaluate(1.0f), WaterTestKit.Median(at10), 0.2f, "§7.1 wade at 1.0 m");
            Assert.AreEqual(tuning.swimSpeed, strokeMean, 0.1f, "§7.1 swim stroke mean");
            Assert.GreaterOrEqual(openSwum, SwimDistance, "§7.2 open-water distance");
            float expected = SwimDistance / tuning.swimSpeed;
            Assert.That(openTime, Is.InRange(expected * 0.97f, expected * 1.08f), $"§7.2 50 m time (27-30 s at 1.8 m/s)");
            Assert.LessOrEqual(worstEye, 0.1f, "§7.2 eye 0.35 +- 0.1 above S");
            Assert.AreEqual(0, openFlips, "§7.2 no state flips in open water");
            Assert.AreEqual(0, groundedDeep, "§7.8 grounded while S - G >= swimEnterDepth");
            Assert.IsTrue(outAgain, "did not climb back out");
            Assert.AreEqual(0, fallThroughs, "fall-through guard fired");
        }

        /// <summary>§7.6: sprint-jump off a low bank into the river; the stroke mean of the first full stroke from 1.5 s after entering Swim.</summary>
        [UnityTest, Timeout(600000)]
        public IEnumerator SprintJumpIn_SettlesToSwimSpeed([Values(true, false)] bool sprintHeld)
        {
            yield return LoadWorld();
            float w = cfg.WaterLevelY;
            Assert.IsTrue(OpenWaterStation(w, out var shore, out var dir), "No open-water station");
            WorldBoundsTestKit.Teleport(player, shore - dir * 12f, WaterTestKit.Yaw(dir));
            yield return new WaitForSeconds(0.25f);
            Press(keyboard.leftShiftKey);
            Press(keyboard.wKey);
            float entryPeak = 0f, swimClock = -1f, t = 0f;
            bool jumped = false;
            var window = new List<float>();
            Vector3 last = player.transform.position;
            float stroke = 1f / tuning.strokeSurgeHz;
            try
            {
                while (t < 40f)
                {
                    // Jump at the first wet step (the lip): the realistic sprint-jump in.
                    if (!jumped && player.WaterMode != WaterMode.Dry) { Press(keyboard.spaceKey); jumped = true; }
                    else if (keyboard.spaceKey.isPressed) Release(keyboard.spaceKey);
                    yield return null;
                    t += Time.deltaTime;
                    var p = player.transform.position;
                    float speed = WaterTestKit.Flat(p - last).magnitude / Time.deltaTime;
                    last = p;
                    if (!jumped) entryPeak = Mathf.Max(entryPeak, speed);
                    if (swimClock < 0f && player.WaterMode == WaterMode.Swim)
                    {
                        swimClock = 0f;
                        if (!sprintHeld) Release(keyboard.leftShiftKey);
                    }
                    else if (swimClock >= 0f) swimClock += Time.deltaTime;
                    if (swimClock >= 1.5f) window.Add(speed);
                    if (swimClock >= 1.5f + stroke) break;
                }
            }
            finally { ReleaseAll(); }
            float mean = window.Count > 0 ? Sum(window) / window.Count : float.NaN;
            float peak = window.Count > 0 ? Mathf.Max(window.ToArray()) : float.NaN;
            Debug.Log($"WaterSwimTests[sprint-jump sprintHeld={sprintHeld}]: landPeak={entryPeak:F2} jumped={jumped} firstStrokeMean={mean:F2} peak={peak:F2}");
            Assert.IsTrue(jumped, "never reached the water");
            Assert.GreaterOrEqual(swimClock, 1.5f + stroke - 0.02f, "never swam a full stroke");
            Assert.LessOrEqual(mean, sprintHeld ? 2.5f : 1.9f, "§7.6 stroke mean");
        }

        static float Sum(List<float> xs) { float s = 0f; foreach (var x in xs) s += x; return s; }
    }
}
