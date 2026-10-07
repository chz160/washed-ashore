using UnityEngine;

namespace WashedAshore.Gameplay
{
    public enum WaterMode { Dry, Wade, Swim }

    /// <summary>
    /// Pure wade/swim rules (spec W6, water-swim-spec.md §2-§3). No scene state, so EditMode tests and a
    /// server can run them. Depth is the water surface minus the ground under the player's feet.
    /// </summary>
    public static class SwimRules
    {
        /// <summary>
        /// Next water mode. Dry at or below wadeMinDepth; Swim from swimEnterDepth; a swimmer keeps
        /// swimming until the depth falls to swimExitDepth (hysteresis, so the shelf never flickers).
        /// </summary>
        public static WaterMode Classify(WaterMode previous, float depth, SwimTuning t)
        {
            if (depth <= t.wadeMinDepth) return WaterMode.Dry;
            if (depth >= t.swimEnterDepth) return WaterMode.Swim;
            return previous == WaterMode.Swim && depth > t.swimExitDepth ? WaterMode.Swim : WaterMode.Wade;
        }

        /// <summary>Target horizontal speed for <paramref name="mode"/> (swim: before the stroke surge).</summary>
        public static float TargetSpeed(WaterMode mode, float depth, bool sprintHeld, float walkSpeed, float sprintSpeed, SwimTuning t)
        {
            switch (mode)
            {
                case WaterMode.Swim:
                    return sprintHeld ? t.swimSprintSpeed : t.swimSpeed;
                case WaterMode.Wade:
                    bool sprint = sprintHeld && depth < t.wadeSprintMaxDepth;
                    return (sprint ? sprintSpeed : walkSpeed) * t.wadeSpeedByDepth.Evaluate(depth);
                default:
                    return sprintHeld ? sprintSpeed : walkSpeed;
            }
        }

        /// <summary>Swim input scaled for back and sideways strokes (forward is full speed).</summary>
        public static Vector2 SwimInput(Vector2 input, SwimTuning t) =>
            new Vector2(input.x * t.swimBackStrafeMultiplier, input.y >= 0f ? input.y : input.y * t.swimBackStrafeMultiplier);

        /// <summary>Speed factor of the stroke surge <paramref name="strokeTime"/> seconds into a stroke run; averages 1 over whole strokes.</summary>
        public static float StrokeSurge(float strokeTime, SwimTuning t) =>
            1f + t.strokeSurgeAmplitude * Mathf.Sin(2f * Mathf.PI * t.strokeSurgeHz * strokeTime);

        /// <summary>Horizontal acceleration toward a target. Land rates when Dry; water rates otherwise.</summary>
        public static float Rate(WaterMode mode, bool hasInput, bool overTarget, float landAcceleration, float landDeceleration, SwimTuning t)
        {
            switch (mode)
            {
                case WaterMode.Swim:
                    return overTarget ? t.swimOverspeedDeceleration : hasInput ? t.swimAcceleration : t.swimDeceleration;
                case WaterMode.Wade:
                    return hasInput && !overTarget ? t.waterAcceleration : t.waterDeceleration;
                default:
                    return hasInput ? landAcceleration : landDeceleration;
            }
        }

        public static bool CanJump(WaterMode mode, float depth, SwimTuning t) =>
            mode == WaterMode.Dry || (mode == WaterMode.Wade && depth < t.wadeJumpMaxDepth);

        /// <summary>Feet height a swimmer follows: floatDepth under the surface, never below the ground plus <paramref name="skin"/>.</summary>
        public static float FloatFeetY(float surfaceY, float groundY, float skin, SwimTuning t) =>
            Mathf.Max(surfaceY - t.floatDepth, groundY + skin);

        /// <summary>Vertical speed that reaches <paramref name="targetY"/> this frame, capped at surfaceFollowRate.</summary>
        public static float FollowSpeed(float feetY, float targetY, float dt, SwimTuning t) =>
            dt <= 0f ? 0f : Mathf.Clamp((targetY - feetY) / dt, -t.surfaceFollowRate, t.surfaceFollowRate);
    }
}
