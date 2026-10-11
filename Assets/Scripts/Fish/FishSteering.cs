using UnityEngine;

namespace WashedAshore.Fish
{
    /// <summary>
    /// Pure fish steering rules (spec F4, F5). No scene state, so EditMode tests can run them against an analytic bed.
    /// Heights are world Y; the caller passes the bed (TerrainQuery ground) and the surface (WaterBody: WaterLevelY +
    /// WaterMotion.Offset), never a flat Y.
    /// </summary>
    public static class FishSteering
    {
        /// <summary>
        /// The Y band a fish may occupy: bed + clearance up to surface - margin. When the water is too shallow for both,
        /// the band collapses to the midpoint (the caller treats that column as unusable).
        /// </summary>
        public static Vector2 DepthBand(float bedY, float surfaceY, float clearance, float margin)
        {
            float lo = bedY + clearance, hi = surfaceY - margin;
            if (lo > hi) lo = hi = 0.5f * (lo + hi);
            return new Vector2(lo, hi);
        }

        /// <summary>True when the column has room for the fish: water deep enough for clearance + margin + minDepth.</summary>
        public static bool Usable(float bedY, float surfaceY, float clearance, float margin, float minBedDepth) =>
            surfaceY - bedY >= Mathf.Max(minBedDepth, clearance + margin);

        /// <summary>Target Y for a fish holding fraction <paramref name="columnFraction"/> of its band (0 = bed side, 1 = surface side).</summary>
        public static float HoldY(Vector2 band, float columnFraction) => Mathf.Lerp(band.x, band.y, Mathf.Clamp01(columnFraction));

        /// <summary>Yaw (degrees) toward <paramref name="desired"/>, limited to rateDegPerSec * dt and never more than maxStepDeg.</summary>
        public static float TurnToward(float yaw, float desired, float rateDegPerSec, float dt, float maxStepDeg)
        {
            float step = Mathf.Min(rateDegPerSec * Mathf.Max(0f, dt), maxStepDeg);
            return Mathf.MoveTowardsAngle(yaw, desired, step);
        }

        /// <summary>
        /// Rendered yaw for this frame (F5 is measured per rendered frame): the interpolated sim yaw, but never more than
        /// <paramref name="maxStepDeg"/> from last frame's rendered yaw. A hitch can run several ticks in one frame, so
        /// the tick cap alone doesn't bound the rendered turn.
        /// </summary>
        public static float RenderedYaw(float previousRendered, float interpolated, float maxStepDeg) =>
            Mathf.MoveTowardsAngle(previousRendered, interpolated, maxStepDeg);

        /// <summary>Interpolated yaw between two ticks along the short way round.</summary>
        public static float LerpYaw(float a, float b, float t) => a + Mathf.DeltaAngle(a, b) * Mathf.Clamp01(t);

        /// <summary>True when the anim-rate map isn't clamped at <paramref name="burstSpeed"/> (no clamp during a scatter; F5 correlation).</summary>
        public static bool RateCoversBurst(float burstSpeed, float idleRate, float cyclesPerMetre, float maxRate) =>
            idleRate + cyclesPerMetre * burstSpeed <= maxRate;

        /// <summary>
        /// Swim-clip playback rate, cycles/s (brief animRateRule): idleRate at rest plus cyclesPerMetre x speed, clamped
        /// to maxRate. Fins keep moving at rest; the tail beat rises linearly with speed.
        /// </summary>
        public static float AnimRate(float speed, float idleRate, float cyclesPerMetre, float maxRate) =>
            Mathf.Min(maxRate, idleRate + cyclesPerMetre * Mathf.Max(0f, speed));

        /// <summary>Speed eased toward the target at <paramref name="accel"/> m/s^2.</summary>
        public static float Ease(float speed, float target, float accel, float dt) => Mathf.MoveTowards(speed, target, accel * Mathf.Max(0f, dt));

        /// <summary>Compass yaw (0 = +Z, 90 = +X) of a horizontal direction.</summary>
        public static float Yaw(Vector3 d) => Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;

        /// <summary>Horizontal unit vector for a compass yaw.</summary>
        public static Vector3 Heading(float yawDeg) => new Vector3(Mathf.Sin(yawDeg * Mathf.Deg2Rad), 0f, Mathf.Cos(yawDeg * Mathf.Deg2Rad));

        /// <summary>
        /// Separation push from schoolmates closer than <paramref name="spacing"/> (horizontal and vertical), weighted
        /// by overlap. <paramref name="self"/> indexes into <paramref name="mates"/>.
        /// </summary>
        public static Vector3 Separation(Vector3[] mates, int count, int self, float spacing)
        {
            Vector3 push = Vector3.zero, p = mates[self];
            for (int i = 0; i < count; i++)
            {
                if (i == self) continue;
                Vector3 d = p - mates[i];
                float m = d.magnitude;
                if (m >= spacing) continue;
                push += (m > 1e-4f ? d / m : Heading(self * 137.5f)) * (1f - m / spacing);
            }
            return push;
        }

        /// <summary>Unit horizontal flee direction away from the player, biased toward deeper water by <paramref name="deeper"/> (unit, may be zero).</summary>
        public static Vector3 FleeDirection(Vector3 fish, Vector3 player, Vector3 deeper, float deeperWeight)
        {
            Vector3 away = fish - player;
            away.y = 0f;
            away = away.sqrMagnitude > 1e-6f ? away.normalized : Vector3.forward;
            Vector3 d = away + deeper * deeperWeight;
            d.y = 0f;
            return d.sqrMagnitude > 1e-6f ? d.normalized : away;
        }
    }
}
