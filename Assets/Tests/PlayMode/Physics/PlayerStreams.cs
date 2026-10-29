using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using WashedAshore.Gameplay;
using WashedAshore.Tests.Support;
using WashedAshore.World;

namespace WashedAshore.Tests.PlayMode
{
    /// <summary>
    /// The command streams and the shared fixture of the #46 PR B tick tests (spec 94f34aa6, section 7). A stream is a pure function of the stream
    /// tick, so every run gets the same commands. Stream tick 1 is the first tick after the start pose is set.
    /// </summary>
    public static class PlayerStreams
    {
        public const int Length = 360;

        /// <summary>
        /// The tower stream stops dead against the F01 head wall at tick 221 at every frame rate (measured, |SimVelocity| 0.001; the player is at
        /// 1.5 m/s at tick 210). Past the stop every end state converges to the same point, so a measurement there cannot see a start difference.
        /// The tower stream is cut at tick 180: 41 ticks before the stop, |SimVelocity| 1.883 at that tick (td-46/qa-46 combined Tower spec).
        /// </summary>
        public const int TowerLength = 180;

        /// <summary>The tower-exit stream (td-46/qa-46: the zone snapshot must matter): down flight 1 and off the tower, past the footprint edge.</summary>
        public const int TowerExitLength = 300;

        public static int LengthOf(Kind kind) => kind == Kind.Tower ? TowerLength : kind == Kind.TowerExit ? TowerExitLength : Length;

        public enum Kind { Ground, Tower, Swim, TowerExit }

        /// <summary>The command of stream tick <paramref name="s"/> (1-based) for <paramref name="kind"/>, starting with body yaw <paramref name="yaw0"/>.</summary>
        public static PlayerCommand At(Kind kind, int s, float yaw0)
        {
            var c = new PlayerCommand { Yaw = yaw0 };
            switch (kind)
            {
                case Kind.Ground:
                    // G: 1-30 idle; 31-90 walk; 91-150 sprint; 151 jump + sprint; 152-210 walk; 211-260 walk and turn 0.6 deg a tick;
                    // 261 crouch; 262-330 walk; 331 crouch again (back to stand); 332-360 idle.
                    if (s >= 31 && s <= 331) c.Move = Vector2.up;
                    if (s >= 91 && s <= 151) c.Buttons |= PlayerButtons.Sprint;
                    if (s == 151) c.Buttons |= PlayerButtons.Jump;
                    if (s >= 211) c.Yaw = Wrap(yaw0 + 0.6f * (Mathf.Min(s, 260) - 210));
                    if (s == 261 || s == 331) c.Stance = StanceKey.Crouch;
                    break;
                case Kind.Tower:
                case Kind.TowerExit:
                    // T (TowerExit: the same commands, started at the F01 head facing down): 1-30 idle; then walk at the body yaw. Tower ends at tick 180 (TowerLength), TowerExit at 300 (TowerExitLength).
                    if (s >= 31) c.Move = Vector2.up;
                    break;
                default:
                    // W: 1-30 idle (settles to Swim); 31-360 sprint-swim.
                    if (s >= 31) { c.Move = Vector2.up; c.Buttons |= PlayerButtons.Sprint; }
                    break;
            }
            return c;
        }

        static float Wrap(float deg) => Mathf.Repeat(deg, 360f);
    }

    /// <summary>What a stream run left behind.</summary>
    public struct StreamOutcome
    {
        public Vector3 position;
        public float yaw;
        public Stance stance;
        public int ticks;
        public long dropped;
        public WaterMode waterMode;
        public int snaps;
        public int wrongWater;   // ticks whose water mode was not the stream's expected one
        public float speed;      // |SimVelocity| after the last tick
    }

    /// <summary>Base of the tick tests: World loaded per case, frame rate forced with captureDeltaTime and put back, every test knob reset.</summary>
    public abstract class PlayerTickFixture : InputTestFixture
    {
        protected PlayerController player;
        protected WorldBoundsClamp clamp;
        float savedCapture;
        protected float yaw0;
        protected Keyboard keyboard;

        public override void Setup()
        {
            base.Setup();
            keyboard = InputSystem.AddDevice<Keyboard>();
            InputSystem.AddDevice<Mouse>();
            savedCapture = Time.captureDeltaTime;
        }

        public override void TearDown()
        {
            Time.captureDeltaTime = savedCapture;
            PlayerController.TestVariableStep = false;
            PlayerController.TestInterpolationOff = false;
            PlayerController.TestFrameWaterTime = false;
            base.TearDown();
        }

        protected static void SetFrameTime(float seconds) => Time.captureDeltaTime = seconds;

        protected IEnumerator LoadWorld()
        {
            SceneManager.LoadScene(TestSpawn.WorldScene, LoadSceneMode.Single);
            yield return null;
            yield return null;
            player = TestSpawn.Player();
            clamp = WorldBoundsTestKit.Clamp();
        }

        Transform tower;

        Vector3 Lane(string name)
        {
            tower ??= GameObject.Find("LookoutTower").transform;
            return tower.GetComponentsInChildren<Transform>(true).First(t => t.name == name).position;
        }

        /// <summary>Starts a stream: loads World and puts the player at the kind's start pose. Sets <see cref="yaw0"/>.</summary>
        protected IEnumerator Place(PlayerStreams.Kind kind)
        {
            yield return LoadWorld();
            tower = null;
            if (kind == PlayerStreams.Kind.Ground)
            {
                yield return TestSpawn.At(TestSpawn.PlayerSpawnPoint);
                yaw0 = player.transform.eulerAngles.y;
            }
            else if (kind == PlayerStreams.Kind.TowerExit)
            {
                // Top of flight 1 (the F01 head lane point), body yaw down the flight: the stream walks down and off the tower.
                yield return TestSpawn.AtTowerBase();
                Vector3 foot = Lane("LANE_F01_Foot"), head = Lane("LANE_F01_Head");
                Vector3 dir = head - foot; dir.y = 0f; dir.Normalize();
                yaw0 = Quaternion.LookRotation(-dir, Vector3.up).eulerAngles.y;
                player.SpawnAt(head - dir * 0.3f, yaw0, false);
                yield return null;
            }
            else if (kind == PlayerStreams.Kind.Tower)
            {
                yield return TestSpawn.AtTowerBase();
                Vector3 foot = Lane("LANE_F01_Foot"), head = Lane("LANE_F01_Head");
                Vector3 dir = head - foot; dir.y = 0f; dir.Normalize();
                yaw0 = Quaternion.LookRotation(dir, Vector3.up).eulerAngles.y;
                player.SpawnAt(foot - dir * 0.3f, yaw0, false);
                yield return null;
            }
            else
            {
                // Mid-channel at the west end (WaterSwimAroundTests, MidChannel variant 2): out in the open water, south of the line, facing south.
                var rule = clamp.Rule;
                float w = WorldBoundsTestKit.Config().WaterLevelY;
                var (minX, _) = WorldBoundsTestKit.BarrierSpanX();
                float z0 = rule.LineZAt(minX) - 45f, edge = minX - 100f;
                for (float d = 0f; d < 600f; d += 0.5f)
                {
                    float x = minX - 100f - d;
                    if (TerrainQuery.TryGroundHeight(new Vector3(x, 0f, z0), out float h) && h < w) { edge = x; break; }
                }
                var (tMin, _) = WorldBoundsTestKit.TerrainXRange();
                float far = edge - (tMin + 50f);
                float xs = edge - Mathf.Max(20f, far * 0.5f);
                yaw0 = 180f;
                yield return TestSpawn.At(new Vector3(xs, 0f, z0), yaw0);
            }
        }

        /// <summary>Plays the stream through the player's own clock at the current frame time; stops at exactly <see cref="PlayerStreams.LengthOf"/> ticks.</summary>
        protected IEnumerator RunStream(PlayerStreams.Kind kind, List<PlayerCommand> executed, int captureAtTick, Action capture)
        {
            int start = player.Tick;
            int snaps0 = clamp.SnapCount;
            int wrong = 0, towerStopTick = -1;
            long dropped0 = player.DroppedTicks;
            player.CommandSource = tickNo =>
            {
                int s = tickNo - start;
                return s <= PlayerStreams.LengthOf(kind) ? PlayerStreams.At(kind, s, yaw0) : (PlayerCommand?)null;
            };
            Action<PlayerCommand> rec = cmd =>
            {
                int s = cmd.Tick - start;
                if (kind == PlayerStreams.Kind.Ground && player.WaterMode != WaterMode.Dry) wrong++;
                if (kind == PlayerStreams.Kind.Swim && s >= 31 && player.WaterMode != WaterMode.Swim) wrong++;
                executed?.Add(cmd);
                if (kind == PlayerStreams.Kind.Tower)
                {
                    // #46 spec (td/qa combined Tower spec 1): where the tower stream is still moving, and the tick it stops (|v| < 0.05).
                    float speed = player.SimVelocity.magnitude;
                    if (s % 30 == 0) UnityEngine.Debug.Log($"T stream tick {s}: position ({player.transform.position.x:F4}, {player.transform.position.y:F4}, {player.transform.position.z:F4}) |SimVelocity| {speed:F3}");
                    if (towerStopTick < 0 && s > 31 && speed < 0.05f) { towerStopTick = s; UnityEngine.Debug.Log($"T stream stops at tick {s}: |SimVelocity| {speed:F3}"); }
                }
                if (capture != null && s == captureAtTick) capture();
            };
            player.CommandExecuted += rec;
            int guard = 0;
            while (player.Tick < start + PlayerStreams.LengthOf(kind) && guard++ < 200000) yield return null;
            yield return null;   // a frame after the last tick: the source must stay silent
            player.CommandExecuted -= rec;
            player.CommandSource = null;
            LastOutcome = new StreamOutcome
            {
                position = player.transform.position,
                yaw = player.transform.eulerAngles.y,
                stance = player.Stance,
                ticks = player.Tick - start,
                dropped = player.DroppedTicks - dropped0,
                waterMode = player.WaterMode,
                snaps = clamp.SnapCount - snaps0,
                wrongWater = wrong,
                speed = player.SimVelocity.magnitude,
            };
        }

        protected StreamOutcome LastOutcome;
    }
}
