using UnityEngine;

namespace WashedAshore.Fish
{
    /// <summary>
    /// The fish's own random numbers (adr/fish-1 §5.7): a stateless counter hash of (run seed, stream, cell, fish,
    /// counter), SplitMix64 style, so any fish in any cell can be recomputed in any order (census, re-entry, a server).
    /// Separate from WildlifeRandom and BirdRandom, so wildlife and bird placement are unchanged with fish on or off.
    /// No UnityEngine.Random and no shared System.Random. Tests call <see cref="OverrideSeed"/> before loading.
    /// </summary>
    public static class FishRandom
    {
        public const ulong StreamPlan = 0xF15A, StreamSteer = 0xF15B, StreamEvents = 0xF15C;
        static int? overrideSeed;

        public static void OverrideSeed(int? seed) => overrideSeed = seed;

        public static bool Overridden => overrideSeed.HasValue;

        public static int RunSeed(int baked) => overrideSeed ?? baked;

        /// <summary>64-bit hash of the inputs.</summary>
        public static ulong Hash(int runSeed, ulong stream, long cell, int fish, uint counter)
        {
            unchecked
            {
                ulong h = Mix((ulong)(uint)runSeed ^ (stream << 32));
                h = Mix(h ^ (ulong)cell);
                h = Mix(h ^ (ulong)(uint)fish);
                return Mix(h ^ counter);
            }
        }

        /// <summary>Uniform in [0, 1).</summary>
        public static float Value(int runSeed, ulong stream, long cell, int fish, uint counter) =>
            (Hash(runSeed, stream, cell, fish, counter) >> 40) * (1f / (1 << 24));

        public static float Range(int runSeed, ulong stream, long cell, int fish, uint counter, float min, float max) =>
            min + Value(runSeed, stream, cell, fish, counter) * (max - min);

        /// <summary>Cell id for integer cell coordinates (map cells are a few thousand wide at most).</summary>
        public static long CellId(int cx, int cz) => ((long)cz << 32) ^ (uint)cx;

        static ulong Mix(ulong z)
        {
            unchecked
            {
                z += 0x9E3779B97F4A7C15UL;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                return z ^ (z >> 31);
            }
        }
    }

    /// <summary>
    /// A fish's decision stream: (seed, stream, cell, fish) fixed, the counter advances per draw. A value type, so a
    /// fish's state holds its own stream with no allocation and no sharing.
    /// </summary>
    public struct FishDraws
    {
        readonly int seed;
        readonly ulong stream;
        readonly long cell;
        readonly int fish;
        uint counter;

        public FishDraws(int seed, ulong stream, long cell, int fish, uint start = 0)
        {
            this.seed = seed; this.stream = stream; this.cell = cell; this.fish = fish; counter = start;
        }

        public uint Counter => counter;
        public float Next() => FishRandom.Value(seed, stream, cell, fish, counter++);
        public float Range(float min, float max) => min + Next() * (max - min);
        public float Range(Vector2 r) => Range(r.x, r.y);
    }
}
