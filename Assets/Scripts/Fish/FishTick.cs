namespace WashedAshore.Fish
{
    /// <summary>
    /// Fixed-step sim clock (adr/fish-1 §5.5): frame time accumulates, whole ticks run at <see cref="Step"/>, and the
    /// remainder is the render interpolation factor. Decisions only ever see Step, never the frame delta, so outcomes
    /// don't depend on frame rate. A long hitch runs at most <see cref="MaxTicksPerFrame"/> ticks and drops the rest.
    /// </summary>
    public struct FishTick
    {
        public readonly float Step;
        public readonly int MaxTicksPerFrame;
        double accumulator;

        /// <summary>Ticks run so far.</summary>
        public long Count { get; private set; }

        public FishTick(float hz, int maxTicksPerFrame = 4)
        {
            Step = 1f / hz;
            MaxTicksPerFrame = maxTicksPerFrame;
            accumulator = 0;
            Count = 0;
        }

        /// <summary>Adds frame time and returns how many ticks to run now.</summary>
        public int Advance(float frameDelta)
        {
            if (frameDelta > 0f) accumulator += frameDelta;
            int n = (int)(accumulator / Step);
            if (n > MaxTicksPerFrame)
            {
                n = MaxTicksPerFrame;
                accumulator = 0; // drop the backlog rather than spiral
            }
            else accumulator -= n * (double)Step;
            Count += n;
            return n;
        }

        /// <summary>Interpolation factor in [0, 1) between the previous and the latest tick.</summary>
        public float Alpha => (float)(accumulator / Step);
    }
}
