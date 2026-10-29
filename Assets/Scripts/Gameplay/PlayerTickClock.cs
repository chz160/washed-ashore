namespace WashedAshore.Gameplay
{
    /// <summary>
    /// The fixed-step accumulator of one player (#46 P2). A plain class, kept apart from the step so #13's shared server driver can replace
    /// it without touching the sim. Each frame: <see cref="Add"/> the frame time, take ticks while <see cref="TryTake"/> says so, then
    /// <see cref="EndFrame"/>. Time that would need more than <see cref="MaxTicksPerFrame"/> ticks is dropped (no spiral) and counted.
    /// </summary>
    public sealed class PlayerTickClock
    {
        public const float TickDt = 1f / 60f;
        public const int MaxTicksPerFrame = 10;
        const double Slack = 1e-6;   // captureDeltaTime 1/60 gives exactly one tick per frame and 1/30 exactly two, despite float rounding

        double accumulator;

        /// <summary>Ticks that were dropped because a frame needed more than MaxTicksPerFrame.</summary>
        public long DroppedTicks { get; private set; }

        /// <summary>Fraction of the next tick already elapsed, in [0, 1).</summary>
        public float Alpha
        {
            get
            {
                double a = accumulator / TickDt;
                return a <= 0.0 ? 0f : a >= 1.0 ? 0.999999f : (float)a;
            }
        }

        public void Add(float frameDt) => accumulator += frameDt;

        /// <summary>True (and one tick of time is taken) while a whole tick is owed and fewer than the maximum ran this frame.</summary>
        public bool TryTake(int ticksThisFrame)
        {
            if (ticksThisFrame >= MaxTicksPerFrame || accumulator < TickDt - Slack) return false;
            accumulator -= TickDt;
            return true;
        }

        public void EndFrame(int ticksThisFrame)
        {
            if (ticksThisFrame >= MaxTicksPerFrame && accumulator >= TickDt - Slack)
            {
                DroppedTicks += (long)(accumulator / TickDt);
                accumulator = 0.0;
            }
            else if (accumulator < 0.0) accumulator = 0.0;
        }

        /// <summary>Forgets the time owed (a test source that ran out of commands).</summary>
        public void Drain() => accumulator = 0.0;
    }
}
