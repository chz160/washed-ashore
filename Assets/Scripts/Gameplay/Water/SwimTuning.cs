using UnityEngine;

namespace WashedAshore.Gameplay
{
    /// <summary>
    /// Wade and surface-swim numbers (spec W6, _bmad-output/poc/water-swim-spec.md §3, approved in
    /// ruling/water-swim-numbers and r2). PlayerController references one asset; depth is the water
    /// surface minus the ground under the feet.
    /// </summary>
    [CreateAssetMenu(fileName = "SwimTuning", menuName = "Washed Ashore/Swim Tuning")]
    public class SwimTuning : ScriptableObject
    {
        [Header("Wading")]
        [Tooltip("Depth above which the player is wading, m. Ignores surface ripple at the lip.")]
        [Range(0f, 0.3f)] public float wadeMinDepth = 0.05f;
        [Tooltip("Walk speed multiplier by depth (m) while wading.")]
        public AnimationCurve wadeSpeedByDepth = Linear(
            new Vector2(0.05f, 0.9f), new Vector2(0.3f, 0.75f), new Vector2(0.6f, 0.6f), new Vector2(1.0f, 0.5f), new Vector2(1.35f, 0.4f));
        [Tooltip("Sprint (times the curve) works only shallower than this while wading, m.")]
        [Range(0f, 1.35f)] public float wadeSprintMaxDepth = 0.3f;
        [Tooltip("Jump works only shallower than this while wading, m. Never while swimming.")]
        [Range(0f, 1.35f)] public float wadeJumpMaxDepth = 0.6f;
        [Tooltip("Horizontal acceleration while wading, m/s^2 (land is 20).")]
        [Range(1f, 20f)] public float waterAcceleration = 8f;
        [Tooltip("Horizontal deceleration while wading, and when over the wade speed (entering from land), m/s^2.")]
        [Range(1f, 25f)] public float waterDeceleration = 10f;

        [Header("Swimming")]
        [Tooltip("Depth at which wading turns into swimming, m (chest depth).")]
        [Range(0.5f, 2f)] public float swimEnterDepth = 1.35f;
        [Tooltip("Depth at or below which a swimmer stands and wades again, m. Below swimEnterDepth (hysteresis).")]
        [Range(0.3f, 2f)] public float swimExitDepth = 1.15f;
        [Tooltip("Feet below the surface while swimming, m. With the eye at 1.65 this leaves the eye 0.35 above water.")]
        [Range(0.5f, 1.6f)] public float floatDepth = 1.30f;
        [Tooltip("Swim speed (stroke mean), m/s. Approved band 1.5-2.2.")]
        [Range(0.5f, 3f)] public float swimSpeed = 1.8f;
        [Tooltip("Swim speed with sprint held (stroke mean), m/s. At most 2.8 and under half of walk.")]
        [Range(0.5f, 3f)] public float swimSprintSpeed = 2.4f;
        [Tooltip("Multiplier for backward and sideways swim input.")]
        [Range(0.1f, 1f)] public float swimBackStrafeMultiplier = 0.6f;
        [Tooltip("Stroke speed pulse, fraction of the target (0 disables).")]
        [Range(0f, 0.5f)] public float strokeSurgeAmplitude = 0.2f;
        [Tooltip("Strokes per second.")]
        [Range(0.1f, 3f)] public float strokeSurgeHz = 0.9f;
        [Tooltip("Swim acceleration toward the target, m/s^2.")]
        [Range(0.5f, 20f)] public float swimAcceleration = 3f;
        [Tooltip("Swim deceleration with no input (glide), m/s^2.")]
        [Range(0.5f, 20f)] public float swimDeceleration = 2f;
        [Tooltip("Deceleration while faster than the swim target (sprint-jump into water), m/s^2.")]
        [Range(0.5f, 20f)] public float swimOverspeedDeceleration = 6f;
        [Tooltip("Largest vertical speed while following the surface, m/s.")]
        [Range(0.5f, 10f)] public float surfaceFollowRate = 4f;

        /// <summary>A piecewise-linear curve through the points (spec §3: linear keys), clamped past both ends.</summary>
        public static AnimationCurve Linear(params Vector2[] points)
        {
            var keys = new Keyframe[points.Length];
            for (int i = 0; i < points.Length; i++)
            {
                float into = i > 0 ? Slope(points[i - 1], points[i]) : 0f;
                float outOf = i < points.Length - 1 ? Slope(points[i], points[i + 1]) : 0f;
                keys[i] = new Keyframe(points[i].x, points[i].y, into, outOf);
            }
            return new AnimationCurve(keys) { preWrapMode = WrapMode.ClampForever, postWrapMode = WrapMode.ClampForever };
        }

        static float Slope(Vector2 a, Vector2 b) => (b.y - a.y) / (b.x - a.x);

        /// <summary>Spec §3 rules for this asset with a controller's walk and sprint speeds and eye height; null when fine.</summary>
        public string Validate(float walkSpeed, float eyeHeight)
        {
            if (swimExitDepth >= swimEnterDepth) return $"swimExitDepth {swimExitDepth} must be below swimEnterDepth {swimEnterDepth}";
            if (wadeMinDepth >= swimExitDepth) return $"wadeMinDepth {wadeMinDepth} must be below swimExitDepth {swimExitDepth}";
            if (floatDepth > swimEnterDepth) return $"floatDepth {floatDepth} must be <= swimEnterDepth {swimEnterDepth}";
            if (floatDepth + 0.2f > eyeHeight) return $"floatDepth {floatDepth} + 0.2 must be <= eye height {eyeHeight:F2} (camera above water)";
            if (swimSprintSpeed >= walkSpeed) return $"swimSprintSpeed {swimSprintSpeed} must be below walk {walkSpeed}";
            if (wadeSpeedByDepth == null || wadeSpeedByDepth.length == 0) return "wadeSpeedByDepth has no keys";
            float chest = wadeSpeedByDepth.Evaluate(swimEnterDepth) * walkSpeed;
            if (chest < swimSpeed) return $"chest-depth wade speed {chest:F2} must be >= swimSpeed {swimSpeed}";
            return null;
        }

        void OnValidate()
        {
            // Defaults of the live controller (World.unity): walk 5, eye 1.65. Tests re-check with the scene's values.
            string problem = Validate(5f, 1.65f);
            if (problem != null) Debug.LogWarning($"SwimTuning '{name}': {problem}", this);
        }
    }
}
