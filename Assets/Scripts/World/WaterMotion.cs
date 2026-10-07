using System;
using UnityEngine;

namespace WashedAshore.World
{
    /// <summary>One wind-ripple wave. Direction is a compass bearing in degrees (0 = +Z north, 90 = +X east).</summary>
    [Serializable]
    public struct WaterWave
    {
        [Tooltip("Crest height above the mean surface, metres.")]
        public float amplitude;
        [Tooltip("Crest to crest, metres. Must be > 0.")]
        public float wavelength;
        [Tooltip("Compass bearing the crests travel toward, degrees (0 = +Z).")]
        public float directionDeg;
        [Tooltip("Travel speed, metres per second.")]
        public float speed;
        [Tooltip("Phase offset, radians.")]
        public float phase;

        public float K => 2f * Mathf.PI / wavelength;
        public Vector2 Direction => new Vector2(Mathf.Sin(directionDeg * Mathf.Deg2Rad), Mathf.Cos(directionDeg * Mathf.Deg2Rad));
    }

    /// <summary>One visual-only scrolling normal layer (flow-advected micro ripples). No height term.</summary>
    [Serializable]
    public struct WaterScrollLayer
    {
        [Tooltip("Scroll velocity in world XZ, metres per second.")]
        public Vector2 velocity;
        [Tooltip("World size of one texture tile, metres. Must be > 0.")]
        public float tileSize;
    }

    /// <summary>
    /// Water surface motion (spec W9): a pure function of (x, z, t). No Time, Random or state in
    /// here; the caller passes t.
    ///   h(x, z, t) = sum_i A_i sin(k_i (d_i . xz) - phi_i(t)),  k_i = 2 pi / L_i,
    ///   phi_i(t)   = (k_i c_i t - phase_i) mod 2 pi, in double so long sessions keep their precision.
    /// WaterBody pushes A, k, d and phi_i(t) to the shader (WaterSurface.hlsl), which evaluates the same sum
    /// without ever seeing t, so a server computing phi the same way floats bodies on the surface the
    /// player sees. The current is visual flow only and has no height term.
    /// </summary>
    public static class WaterMotion
    {
        public const int MaxWaves = 4;
        public const int MaxScrollLayers = 2;
        const double TwoPi = 2.0 * Math.PI;

        /// <summary>Wave phase at time t, wrapped to [0, 2 pi).</summary>
        public static float Phase(WaterWave w, double t)
        {
            if (w.wavelength <= 0f) return 0f;
            double p = (TwoPi / w.wavelength * w.speed * t - w.phase) % TwoPi;
            return (float)(p < 0 ? p + TwoPi : p);
        }

        /// <summary>Height of the surface above MapConfig.WaterLevelY at (x, z) and time t, metres.</summary>
        public static float Offset(WaterWave[] waves, float x, float z, double t)
        {
            if (waves == null) return 0f;
            float h = 0f;
            int n = Mathf.Min(waves.Length, MaxWaves);
            for (int i = 0; i < n; i++)
            {
                var w = waves[i];
                if (w.wavelength <= 0f) continue;
                var d = w.Direction;
                h += w.amplitude * Mathf.Sin(w.K * (d.x * x + d.y * z) - Phase(w, t));
            }
            return h;
        }

        /// <summary>
        /// Scroll offset of a visual layer at time t, metres in world XZ, each axis wrapped in double to
        /// [0, tileSize) (w-td: no raw float time to the shader, no pop at any wrap).
        /// </summary>
        public static Vector2 ScrollOffset(WaterScrollLayer layer, double t) =>
            layer.tileSize <= 0f ? Vector2.zero : new Vector2(Wrap(layer.velocity.x * t, layer.tileSize), Wrap(layer.velocity.y * t, layer.tileSize));

        /// <summary>Flow-map cycle phase at time t, wrapped in double to [0, 1).</summary>
        public static float FlowPhase(float period, double t) => period <= 0f ? 0f : Wrap(t / period, 1.0);

        static float Wrap(double v, double period)
        {
            double r = v % period;
            if (r < 0) r += period;
            float f = (float)r;
            return f >= period ? 0f : f; // the double-to-float cast can round up to the period itself
        }

        /// <summary>Largest possible |Offset| for these waves (sum of amplitudes).</summary>
        public static float MaxAmplitude(WaterWave[] waves)
        {
            if (waves == null) return 0f;
            float a = 0f;
            for (int i = 0; i < Mathf.Min(waves.Length, MaxWaves); i++) a += Mathf.Abs(waves[i].amplitude);
            return a;
        }
    }
}
