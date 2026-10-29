using System;
using UnityEngine;
using WashedAshore.World;

namespace WashedAshore.Gameplay
{
    /// <summary>
    /// The fixed-tick simulation (#46 P2). Update gathers input, then runs whole ticks of <see cref="TickDt"/> from the player's own
    /// <see cref="PlayerTickClock"/>; each tick is <see cref="Simulate"/>, which reads only the command, this player and the world: no
    /// frame time, no input, never another player. A test or #13's server drives the same step with <see cref="CommandSource"/> or by
    /// calling <see cref="Simulate"/> with <see cref="AutoTick"/> off.
    /// </summary>
    public partial class PlayerController
    {
        /// <summary>The sim step, seconds: 60 Hz (td-46 adr/46-spec-review-r1 9.3).</summary>
        public const float TickDt = PlayerTickClock.TickDt;

        readonly PlayerTickClock clock = new PlayerTickClock();
        int tick;
        float stepDt = TickDt;
        bool grounded, resumeTickPending, stanceTookJump, teleportedThisTick;
        double simWaterTime;
        Vector3 simVelocity;

        /// <summary>The number of ticks this player has run. A command drives tick <c>Tick + 1</c>.</summary>
        public int Tick => tick;
        /// <summary>Ticks dropped because a frame needed more than the maximum; every test asserts it stays 0.</summary>
        public long DroppedTicks => clock.DroppedTicks;
        /// <summary>False stops the Update clock: the owner of the player (a test, #13's driver) calls <see cref="Simulate"/> itself.</summary>
        public bool AutoTick { get; set; } = true;
        /// <summary>
        /// When set, the clock takes its commands from here (argument: the tick to drive) and gather does not run. A null result stops ticking
        /// for this frame and drains the time owed, so a test can stop at an exact tick count.
        /// </summary>
        public Func<int, PlayerCommand?> CommandSource { get; set; }
        /// <summary>Raised after every tick, with the command that drove it (recorders).</summary>
        public event Action<PlayerCommand> CommandExecuted;
        /// <summary>Moved velocity of the last tick (position change after collision over <see cref="TickDt"/>); zero after a teleport. Remote copies animate from it.</summary>
        public Vector3 SimVelocity => simVelocity;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /// <summary>Test-only mutation (#46 KF-T2a): one step per frame with the frame time, as before the fixed tick. Reset in TearDown.</summary>
        public static bool TestVariableStep;
        /// <summary>Test-only mutation (#46 KF-T2a-W, KF-T3c): the tick reads the frame-cached water surface. Reset in TearDown.</summary>
        public static bool TestFrameWaterTime;
#endif

        void Update()
        {
            float frameDt = Time.deltaTime;
            if (CommandSource == null) GatherFrame(frameDt);
            if (AutoTick) RunClock(frameDt);
            PresentView();
        }

        void RunClock(float frameDt)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (TestVariableStep)
            {
                stepDt = frameDt;
                var one = CommandSource != null ? CommandSource(tick + 1) : TakeCommand();
                if (one.HasValue) Simulate(one.Value);
                stepDt = TickDt;
                renderAlpha = 1f;
                return;
            }
#endif
            clock.Add(frameDt);
            int n = 0;
            while (clock.TryTake(n))
            {
                PlayerCommand cmd;
                if (CommandSource != null)
                {
                    PlayerCommand? next = CommandSource(tick + 1);
                    if (!next.HasValue) { clock.Drain(); break; }
                    cmd = next.Value;
                }
                else cmd = TakeCommand();
                Simulate(cmd);
                n++;
            }
            clock.EndFrame(n);
            renderAlpha = clock.Alpha;
        }

        /// <summary>
        /// Runs exactly one tick now with <paramref name="cmd"/>. Order: water time, look, the hooks before the move (tower zones), stance,
        /// move, the fall-through guard, the hooks after the move (hatch duck, world bounds), then the sim velocity.
        /// </summary>
        public void Simulate(in PlayerCommand cmd)
        {
            tick++;
            simWaterTime += stepDt;
            teleportedThisTick = false;
            stanceTookJump = false;
            Vector3 before = transform.position;

            if (!controlSuspended && !StanceLookLocked) ApplyLook(cmd);
            PlayerTickHooks.RunBefore(this);
            StanceTick(stepDt, cmd);
            Move(stepDt, cmd);
            GuardFallThrough();
            PlayerTickHooks.RunAfter(this);

            resumeTickPending = false;
            simVelocity = teleportedThisTick ? Vector3.zero : (transform.position - before) / stepDt;
            if (teleportedThisTick) ResetInterpolation();
            else PushInterpolation(before);
            var executed = cmd;
            executed.Tick = tick;   // the tick this command drove (spec 2.3); the sim itself never reads Tick
            CommandExecuted?.Invoke(executed);
        }

        /// <summary>
        /// The look the command carries, applied absolutely on every tick, live and in a replay alike (one code path, so no float-rounding
        /// difference between them): the yaw is written from the command, never kept from the frame look's rotation.
        /// </summary>
        void ApplyLook(in PlayerCommand cmd)
        {
            transform.rotation = Quaternion.Euler(0f, cmd.Yaw, 0f);
            pitch = Mathf.Clamp(cmd.Pitch, minPitch, maxPitch);
        }

        /// <summary>Water surface at (x, z) at the sim's water time (never the frame-cached one, so ticks agree at every frame rate).</summary>
        float SimSurfaceY(WaterBody water, float x, float z)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (TestFrameWaterTime) return water.SurfaceY(x, z);
#endif
            return water.SurfaceY(x, z, simWaterTime);
        }
    }
}
