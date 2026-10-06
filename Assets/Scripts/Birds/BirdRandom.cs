using UnityEngine;

namespace WashedAshore.Birds
{
    /// <summary>
    /// The birds' own RNG streams (separate from WildlifeRandom, so wildlife placement and decisions are unchanged).
    /// Each bird is seeded from the run seed plus a salt. Tests call <see cref="OverrideSeed"/> before loading.
    /// </summary>
    public static class BirdRandom
    {
        const int Stream = 0x5B1D;
        static int? overrideSeed;

        public static void OverrideSeed(int? seed) => overrideSeed = seed;

        public static bool Overridden => overrideSeed.HasValue;

        public static int RunSeed(int baked) => overrideSeed ?? baked;

        public static System.Random For(int runSeed, int salt)
        {
            unchecked
            {
                int h = runSeed * 486187739 + Stream;
                h = h * 486187739 + salt;
                return new System.Random(h);
            }
        }

        public static float Range(this System.Random rng, float min, float max) => min + (float)rng.NextDouble() * (max - min);

        public static float Range(this System.Random rng, Vector2 range) => rng.Range(range.x, range.y);
    }
}
