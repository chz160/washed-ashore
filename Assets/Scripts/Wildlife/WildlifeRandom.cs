using UnityEngine;

namespace WashedAshore.Wildlife
{
    /// <summary>
    /// Seeds each agent's wander RNG from a run seed plus its placed position, so a given seed
    /// replays the same decisions. Tests call <see cref="OverrideSeed"/> before loading the scene.
    /// </summary>
    public static class WildlifeRandom
    {
        static int? overrideSeed;

        public static void OverrideSeed(int? seed) => overrideSeed = seed;

        public static int RunSeed(WildlifeTuning tuning) => overrideSeed ?? (tuning ? tuning.defaultSeed : 0);

        public static System.Random For(WildlifeTuning tuning, Vector3 placedAt)
        {
            unchecked
            {
                int h = RunSeed(tuning);
                h = h * 486187739 + Mathf.RoundToInt(placedAt.x * 10f);
                h = h * 486187739 + Mathf.RoundToInt(placedAt.z * 10f);
                return new System.Random(h);
            }
        }
    }
}
