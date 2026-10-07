using System;
using UnityEngine;

namespace WashedAshore.World
{
    /// <summary>
    /// The one seam for water time (TD condition, water/tech-pick). Defaults to the time since the level
    /// loaded; a server or a test can swap in its own clock so every client and the server evaluate
    /// f(x, z, t) at the same t.
    /// </summary>
    public static class WaterClock
    {
        static readonly Func<double> Default = () => Time.timeSinceLevelLoadAsDouble;
        static Func<double> source = Default;

        /// <summary>Water time now, seconds.</summary>
        public static double Now => source();

        /// <summary>Replaces the clock; null restores the default.</summary>
        public static void Use(Func<double> clock) => source = clock ?? Default;
    }
}
