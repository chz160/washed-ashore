using UnityEngine;

namespace WashedAshore.Lookout
{
    /// <summary>The explicit state of a hinged part (hatch leaf, sash): tower-td-note §3.7.</summary>
    public enum HingeState { Closed, Opening, Open, Closing }

    /// <summary>
    /// Pure rules for the lookout tower's interactables (spec I2-I4, tower-design-spec.md §4-§6): no scene access,
    /// so EditMode tests cover them. Animation is a function of (state, time since the state changed) and tuning.
    /// </summary>
    public static class LookoutRules
    {
        /// <summary>Compass bearing of a world direction: 0 = north (+Z), clockwise, east (+X) = 90, in [0, 360).</summary>
        public static float Bearing(Vector3 worldForward)
        {
            return Mathf.Repeat(Mathf.Atan2(worldForward.x, worldForward.z) * Mathf.Rad2Deg, 360f);
        }

        /// <summary>The unit horizontal direction of a bearing.</summary>
        public static Vector3 BearingDirection(float bearing)
        {
            float rad = bearing * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad));
        }

        /// <summary>Hinge angle, degrees, for a state and the seconds since it began. Closed is 0.</summary>
        public static float HingeAngle(HingeState state, float elapsed, float openAngle, float seconds, AnimationCurve curve)
        {
            switch (state)
            {
                case HingeState.Open: return openAngle;
                case HingeState.Opening: return openAngle * Ease(elapsed, seconds, curve);
                case HingeState.Closing: return openAngle * (1f - Ease(elapsed, seconds, curve));
                default: return 0f;
            }
        }

        /// <summary>The state after <paramref name="elapsed"/> seconds in <paramref name="state"/>: a swing ends by itself, nothing else changes without input (N13).</summary>
        public static HingeState Settle(HingeState state, float elapsed, float seconds)
        {
            if (elapsed < seconds) return state;
            if (state == HingeState.Opening) return HingeState.Open;
            if (state == HingeState.Closing) return HingeState.Closed;
            return state;
        }

        /// <summary>The state a press leads to: only a part at rest responds (spec §3.1: no queueing, no reversing mid-swing).</summary>
        public static bool TryToggle(HingeState state, out HingeState next)
        {
            next = state;
            if (state == HingeState.Closed) { next = HingeState.Opening; return true; }
            if (state == HingeState.Open) { next = HingeState.Closing; return true; }
            return false;
        }

        public static bool AtRest(HingeState state) => state == HingeState.Closed || state == HingeState.Open;

        static float Ease(float elapsed, float seconds, AnimationCurve curve)
        {
            float t = seconds > 0f ? Mathf.Clamp01(elapsed / seconds) : 1f;
            return curve != null && curve.length > 0 ? Mathf.Clamp01(curve.Evaluate(t)) : t;
        }

        /// <summary>
        /// Bearing turn rate for a rotate key held <paramref name="held"/> seconds (§5): nothing inside the tap
        /// window, then the slow rate for the slow time, then the fast rate. Degrees per second, unsigned.
        /// </summary>
        public static float KeyTurnRate(float held, LookoutTuning t)
        {
            if (held < t.firefinderTapSeconds) return 0f;
            return held < t.firefinderTapSeconds + t.firefinderKeySlowSeconds ? t.firefinderKeySlowDegPerSec : t.firefinderKeyFastDegPerSec;
        }

        /// <summary>
        /// A tap: released within the tap time of its press, on game time. Only a tap of the interact key leaves the
        /// firefinder; a hold that has started rotating never does.
        /// </summary>
        public static bool IsTap(float pressTime, float releaseTime, float tapSeconds) => releaseTime - pressTime < tapSeconds;

        /// <summary>
        /// True when an upright capsule (feet point, radius, height) reaches into an axis-aligned box, all in the
        /// same space. Used for the hatch's swing volume (§4 rule 1).
        /// </summary>
        public static bool CapsuleInBox(Vector3 feet, float radius, float height, Vector3 boxMin, Vector3 boxMax)
        {
            if (feet.y + height <= boxMin.y || feet.y >= boxMax.y) return false;
            float dx = Mathf.Max(boxMin.x - feet.x, 0f, feet.x - boxMax.x);
            float dz = Mathf.Max(boxMin.z - feet.z, 0f, feet.z - boxMax.z);
            return dx * dx + dz * dz < radius * radius;
        }

        /// <summary>
        /// Eye height that passes a hatch opening <paramref name="openingLength"/> m long along the run and keeps
        /// <paramref name="clearance"/> m from the floor's edge (§4): L tan(pitch) - t - clearance / cos(pitch),
        /// held between the duck floor and the standing eye height.
        /// </summary>
        public static float PassEyeHeight(float openingLength, float pitchDegrees, float floorThickness, float clearance,
            float minEyeHeight, float standingEyeHeight)
        {
            float pitch = pitchDegrees * Mathf.Deg2Rad;
            float pass = openingLength * Mathf.Tan(pitch) - floorThickness - clearance / Mathf.Cos(pitch);
            return Mathf.Clamp(pass, minEyeHeight, standingEyeHeight);
        }

        /// <summary>
        /// Eye height on the top flight as a function of position only (§4 duck conditions 1, 2 and 6): the same
        /// curve up, down and standing still. <paramref name="run"/> is the horizontal distance of the feet below
        /// the top of the flight, m. The eye lowers by smoothstep over <paramref name="duckRun"/>, ending
        /// <paramref name="clearance"/> before the eye reaches the opening's edge, holds while the eye is under
        /// the floor, and is back to standing height when the feet reach the cab floor (run 0).
        /// </summary>
        public static float HatchEyeHeight(float run, float openingLength, float pitchDegrees, float passEyeHeight,
            float standingEyeHeight, float duckRun, float clearance)
        {
            if (passEyeHeight >= standingEyeHeight) return standingEyeHeight;
            float lowered = openingLength + clearance;
            if (run >= lowered)
                return Mathf.Lerp(passEyeHeight, standingEyeHeight, Mathf.SmoothStep(0f, 1f, (run - lowered) / duckRun));
            // The eye is level with the top of the floor once the feet are passEyeHeight below it.
            float through = passEyeHeight / Mathf.Tan(pitchDegrees * Mathf.Deg2Rad);
            if (run >= through) return passEyeHeight;
            return Mathf.Lerp(standingEyeHeight, passEyeHeight, Mathf.SmoothStep(0f, 1f, run / through));
        }
    }
}
