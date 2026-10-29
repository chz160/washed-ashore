using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;
using WashedAshore.Gameplay;
using Is = UnityEngine.TestTools.Constraints.Is;
using Kind = WashedAshore.Tests.PlayMode.PlayerStreams.Kind;

namespace WashedAshore.Tests.PlayMode
{
    /// <summary>
    /// #46 P1 and the allocation check (spec 94f34aa6, 7 T0, T1 and C6). Known-fail cases are [Explicit] [Category("KnownFail46")]; each must FAIL
    /// and prints the value that failed.
    /// </summary>
    public class PlayerTickTests : PlayerTickFixture
    {
        struct Stamp { public int frame; public PlayerCommand cmd; public float vy; public Stance stance; }

        IEnumerator Settle(float seconds)
        {
            float t0 = Time.time;
            while (Time.time - t0 < seconds) yield return null;
        }

        // ---- T0: a command source shuts the keyboard out --------------------------------------------------------------

        [UnityTest, Timeout(600000)]
        public IEnumerator T0_Sim_IgnoresKeyboardWithCommandSource() => KeyboardCase(true);

        /// <summary>KF-T0: the same with no command source (the gather drives): the keyboard moves the player and the case must FAIL, printing the distance.</summary>
        [UnityTest, Timeout(600000), Explicit("Known-fail: must FAIL"), Category("KnownFail46")]
        public IEnumerator KnownFail_T0_NoCommandSource() => KeyboardCase(false);

        IEnumerator KeyboardCase(bool withSource)
        {
            SetFrameTime(1f / 60f);
            yield return Place(Kind.Ground);
            player.CommandSource = tickNo => new PlayerCommand { Tick = tickNo, Yaw = yaw0 };
            yield return Settle(0.6f);
            if (!withSource) player.CommandSource = null;
            Vector3 settled = player.transform.position;
            float worstUp = 0f;
            bool crouched = false;
            System.Action<PlayerCommand> rec = _ => { worstUp = Mathf.Max(worstUp, player.SimVelocity.y); crouched |= player.Stance != Stance.Stand; };
            player.CommandExecuted += rec;
            Press(keyboard.wKey);
            for (int i = 0; i < 60; i++) yield return null;
            Release(keyboard.wKey);
            Press(keyboard.spaceKey);
            yield return null;
            Release(keyboard.spaceKey);
            yield return null;
            yield return null;
            Press(keyboard.leftCtrlKey);
            yield return null;
            Release(keyboard.leftCtrlKey);
            for (int i = 0; i < 10; i++) yield return null;
            player.CommandExecuted -= rec;
            float moved = Vector3.Distance(player.transform.position, settled);
            Debug.Log($"T0 withSource {withSource}: moved {moved:F4} m, worst upward SimVelocity {worstUp:F3}, left Stand {crouched}");
            Assert.LessOrEqual(moved, 0.001f, $"the player moved {moved:F4} m with W held, Space and Ctrl pressed");
            Assert.LessOrEqual(worstUp, 0.01f, $"a jump started: upward SimVelocity {worstUp:F3} m/s");
            Assert.IsFalse(crouched, "the stance changed");
        }

        // ---- T1: the gather latches edges --------------------------------------------------------------------------------

        [UnityTest, Timeout(600000)]
        public IEnumerator T1a_Gather_LatchesJump_OnANoTickFrame_At144() => EdgeOnNoTickFrame(false);

        [UnityTest, Timeout(600000)]
        public IEnumerator T1b_Gather_LatchesStance_OnANoTickFrame_At144() => EdgeOnNoTickFrame(true);

        IEnumerator EdgeOnNoTickFrame(bool stanceKey)
        {
            SetFrameTime(1f / 144f);
            yield return Place(Kind.Ground);
            yield return Settle(0.6f);
            var stamps = new List<Stamp>();
            System.Action<PlayerCommand> rec = c => stamps.Add(new Stamp { frame = Time.frameCount, cmd = c, vy = player.SimVelocity.y, stance = player.Stance });
            player.CommandExecuted += rec;
            int guard = 0;
            int t;
            do { t = player.Tick; yield return null; } while (player.Tick != t && guard++ < 100);   // a frame that ran no tick
            Assert.AreEqual(t, player.Tick, "found a frame with no tick");
            var key = stanceKey ? keyboard.leftCtrlKey : keyboard.spaceKey;
            Press(key);
            yield return null;
            Release(key);
            for (int i = 0; i < 12; i++) yield return null;
            player.CommandExecuted -= rec;
            var hit = stamps.FirstOrDefault(s => stanceKey ? s.cmd.Stance == StanceKey.Crouch : (s.cmd.Buttons & PlayerButtons.Jump) != 0);
            Assert.IsNotNull(hit.cmd.Tick == 0 ? null : (object)hit, "no executed command carried the edge");
            if (stanceKey) Assert.AreEqual(Stance.Crouch, hit.stance, "the stance is Crouch after that tick");
            else Assert.Greater(hit.vy, 0f, $"the jump started on that tick (SimVelocity.y {hit.vy:F3})");
            Debug.Log($"T1{(stanceKey ? "b" : "a")}: edge reached command tick {hit.cmd.Tick} (frame {hit.frame}); upward velocity {hit.vy:F3}, stance {hit.stance}");
        }

        [UnityTest, Timeout(600000)]
        public IEnumerator T1c_TwoTickFrame_GivesTheEdgeToTheFirstTick([Values(false, true)] bool stanceKey) => TwoTickFrame(stanceKey, false);

        /// <summary>KF-T1: in the two-tick frame assert the SECOND command has the edge; must FAIL (a harness check: the test reads the right command).</summary>
        [UnityTest, Timeout(600000), Explicit("Known-fail: must FAIL"), Category("KnownFail46")]
        public IEnumerator KnownFail_T1_SecondTickHasTheEdge([Values(false, true)] bool stanceKey) => TwoTickFrame(stanceKey, true);

        IEnumerator TwoTickFrame(bool stanceKey, bool expectSecond)
        {
            SetFrameTime(1f / 30f);
            yield return Place(Kind.Ground);
            yield return Settle(0.6f);
            var stamps = new List<Stamp>();
            System.Action<PlayerCommand> rec = c => stamps.Add(new Stamp { frame = Time.frameCount, cmd = c });
            player.CommandExecuted += rec;
            var key = stanceKey ? keyboard.leftCtrlKey : keyboard.spaceKey;
            Press(key);
            yield return null;
            Release(key);
            for (int i = 0; i < 6; i++) yield return null;
            player.CommandExecuted -= rec;
            System.Func<Stamp, bool> carries = s => stanceKey ? s.cmd.Stance == StanceKey.Crouch : (s.cmd.Buttons & PlayerButtons.Jump) != 0;
            var hit = stamps.First(carries);
            var sameFrame = stamps.Where(s => s.frame == hit.frame).ToList();
            Debug.Log($"T1c {(stanceKey ? "stance" : "jump")}: the frame {hit.frame} ran {sameFrame.Count} ticks; edge flags {string.Join(",", sameFrame.Select(s => carries(s) ? 1 : 0))}");
            Assert.AreEqual(2, sameFrame.Count, "the frame ran exactly two ticks at 1/30");
            int index = expectSecond ? 1 : 0;
            Assert.IsTrue(carries(sameFrame[index]), $"command {index + 1} of the frame did not carry the edge (flags {string.Join(",", sameFrame.Select(s => carries(s) ? 1 : 0))})");
            Assert.IsFalse(carries(sameFrame[1 - index]), "the other command of the frame also carried the edge");
        }

        // ---- C6: no allocation in the tick -------------------------------------------------------------------------------

        [UnityTest, Timeout(600000)]
        public IEnumerator C6_Simulate_DoesNotAllocate() => AllocCase(false);

        /// <summary>KF-C6: the same measurement around one <c>new int[16]</c> in the measured block; must FAIL (the measurement is live).</summary>
        [UnityTest, Timeout(600000), Explicit("Known-fail: must FAIL"), Category("KnownFail46")]
        public IEnumerator KnownFail_C6_AllocationInTheBlock() => AllocCase(true);

        static object sink;

        IEnumerator AllocCase(bool allocate)
        {
            SetFrameTime(1f / 60f);
            yield return Place(Kind.Ground);
            player.AutoTick = false;
            int s = 0;
            for (int i = 0; i < 30; i++) player.Simulate(PlayerStreams.At(Kind.Ground, ++s, yaw0));   // warm-up
            var cmds = new PlayerCommand[PlayerStreams.Length];
            for (int i = 0; i < cmds.Length; i++) cmds[i] = PlayerStreams.At(Kind.Ground, 31 + (i % 100), yaw0);
            Assert.That(() =>
            {
                for (int i = 0; i < cmds.Length; i++)
                {
                    player.Simulate(cmds[i]);
                    if (allocate && i == 0) sink = new int[16];
                }
            }, Is.Not.AllocatingGCMemory(), "Simulate and the hooks allocated");
        }
    }
}
