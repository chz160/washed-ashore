using UnityEngine;

namespace WashedAshore.Lookout
{
    /// <summary>
    /// Trap door, sash and firefinder numbers (tower-design-spec.md §8; the lookout third of the three tuning
    /// assets in tower-td-note §6 condition 12). Tests read their targets from it. Ranges are the spec's.
    /// </summary>
    [CreateAssetMenu(fileName = "LookoutTuning", menuName = "Washed Ashore/Lookout Tuning")]
    public class LookoutTuning : ScriptableObject
    {
        /// <summary>Spec R8: eye to any tower surface, m. Fixed; never lowered to pass a check.</summary>
        public const float EyeClearanceR8 = 0.20f;

        [Header("Trap door")]
        [Tooltip("Seconds to open or close. Never under 1.")]
        [Range(1f, 2.5f)] public float hatchSeconds = 1.4f;
        [Tooltip("Open angle, degrees: the manifest's hatch.open_angle_deg (an EditMode check holds them equal within 0.001; director #40 amend2 Q1).")]
        [Range(85f, 105f)] public float hatchOpenAngle = 92f;
        [Tooltip("Swing progress (0-1) against time (0-1). Monotonic; shared with the sashes.")]
        public AnimationCurve hatchCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
        [Tooltip("Head below a shut hatch on the top flight, m. At least hatchSwingDepth + hatchHeadroomMargin.")]
        [Range(0.10f, 0.30f)] public float hatchStopHeadroom = 0.15f;
        [Tooltip("How far the swing volume reaches below the floor, m.")]
        [Range(0.02f, 0.10f)] public float hatchSwingDepth = 0.05f;
        [Tooltip("Margin Validate keeps between the stop headroom and the swing depth, m.")]
        [Range(0.05f, 0.10f)] public float hatchHeadroomMargin = 0.05f;
        [Tooltip("Lowest eye height passing the hatch, m. The height used is computed from the built opening.")]
        [Range(1f, 1.65f)] public float hatchDuckMinEyeHeight = 1f;
        [Tooltip("Run over which the eye lowers before the hatch, m.")]
        [Range(0.3f, 1f)] public float hatchDuckRun = 0.6f;
        [Tooltip("R8: the eye stays this far from every tower surface, m. Fixed by the spec.")]
        [Range(EyeClearanceR8, EyeClearanceR8)] public float eyeClearance = EyeClearanceR8;

        [Header("Sashes")]
        [Tooltip("Seconds to open or close.")]
        [Range(0.6f, 2f)] public float sashSeconds = 0.9f;
        [Tooltip("Open angle, bottom outward, degrees.")]
        [Range(20f, 60f)] public float sashOpenAngle = 35f;

        [Header("Firefinder")]
        [Tooltip("Vertical FOV at the sight, degrees.")]
        [Range(40f, 55f)] public float firefinderFov = 45f;
        [Tooltip("Eye above the cab's walking surface (cab.floor_y) at the sight, m (spec of record, director #40 amend2 Q2).")]
        [Range(1.21f, 1.26f)] public float firefinderEyeHeight = 1.22f;
        [Tooltip("Eye behind the pedestal axis, m.")]
        [Range(0.15f, 0.195f)] public float firefinderEyeBack = 0.185f;
        [Tooltip("Camera blend to and from the sight, s.")]
        [Range(0.2f, 0.8f)] public float firefinderBlendSeconds = 0.4f;
        [Tooltip("Bearing turn per mouse count, degrees.")]
        [Range(0.02f, 0.10f)] public float firefinderMouseDegPerCount = 0.06f;
        [Tooltip("Bearing turn at full right stick, degrees per second.")]
        [Range(30f, 120f)] public float firefinderStickDegPerSec = 60f;
        [Tooltip("Q/E turn rate after the slow start, degrees per second.")]
        [Range(20f, 90f)] public float firefinderKeyFastDegPerSec = 45f;
        [Tooltip("Q/E turn rate during the slow start, degrees per second.")]
        [Range(5f, 20f)] public float firefinderKeySlowDegPerSec = 12f;
        [Tooltip("Length of the Q/E slow start, s.")]
        [Range(0.2f, 1f)] public float firefinderKeySlowSeconds = 0.5f;
        [Tooltip("E released within this is a tap (leave); held past it, Q/E rotate. s.")]
        [Range(0.15f, 0.30f)] public float firefinderTapSeconds = 0.20f;
        [Tooltip("Lowest view pitch at the sight, degrees (negative looks down).")]
        [Range(-40f, 0f)] public float firefinderPitchMin = -25f;
        [Tooltip("Highest view pitch at the sight, degrees.")]
        [Range(0f, 15f)] public float firefinderPitchMax = 5f;
        [Tooltip("Bearing the alidade starts at, degrees (0 = north, clockwise).")]
        [Range(0f, 359f)] public float firefinderStartBearing = 135f;
        [Tooltip("The leave line shows for this long at the start of each use, s. 0 = never.")]
        [Range(0f, 10f)] public float firefinderPromptSeconds = 4f;

        [Header("Prompt words (spec §3.2)")]
        public string openVerb = "Open";
        public string closeVerb = "Close";
        public string useVerb = "Use";
        public string leaveVerb = "Leave";
        public string hatchNoun = "hatch";
        public string sashNoun = "window";
        public string firefinderNoun = "firefinder";

        /// <summary>Spec §8 rules for this asset; null when fine.</summary>
        public string Validate()
        {
            if (!Mathf.Approximately(eyeClearance, EyeClearanceR8)) return $"eyeClearance {eyeClearance} must stay {EyeClearanceR8} (R8)";
            if (hatchStopHeadroom < hatchSwingDepth + hatchHeadroomMargin)
                return $"hatchStopHeadroom {hatchStopHeadroom} must be >= hatchSwingDepth {hatchSwingDepth} + {hatchHeadroomMargin}";
            if (firefinderPitchMin >= firefinderPitchMax) return $"firefinderPitchMin {firefinderPitchMin} must be below firefinderPitchMax {firefinderPitchMax}";
            if (firefinderKeySlowDegPerSec > firefinderKeyFastDegPerSec) return "firefinderKeySlowDegPerSec must not exceed firefinderKeyFastDegPerSec";
            string curve = MonotonicProblem(hatchCurve);
            return curve != null ? "hatchCurve " + curve : null;
        }

        /// <summary>Null when the curve runs 0 to 1 over 0-1 without ever going back; otherwise what is wrong.</summary>
        public static string MonotonicProblem(AnimationCurve curve, int samples = 64)
        {
            if (curve == null || curve.length == 0) return "has no keys";
            if (Mathf.Abs(curve.Evaluate(0f)) > 1e-3f || Mathf.Abs(curve.Evaluate(1f) - 1f) > 1e-3f) return "must run from 0 to 1";
            float last = curve.Evaluate(0f);
            for (int i = 1; i <= samples; i++)
            {
                float at = i / (float)samples, v = curve.Evaluate(at);
                if (v < last - 1e-4f) return $"goes backward at t={at:F2}";
                if (v > 1f + 1e-3f) return $"overshoots at t={at:F2}";
                last = v;
            }
            return null;
        }

        void OnValidate()
        {
            string problem = Validate();
            if (problem != null) Debug.LogWarning($"LookoutTuning '{name}': {problem}", this);
        }
    }
}
