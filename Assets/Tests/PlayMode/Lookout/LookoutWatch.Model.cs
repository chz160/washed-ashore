using UnityEngine;

namespace WashedAshore.Tests.PlayMode.Lookout
{
    /// <summary>
    /// The detector's own picture of the lanes, built by the fixture from the build manifest and the tuning values
    /// read once at fixture load (agreed with t-qa). Nothing here calls the game's lane rules or reads the live lanes,
    /// so a wrong lane, a swapped side or a retune inside a run can't move the detector's bounds with it.
    /// </summary>
    public partial class LookoutWatch
    {
        const float LevelPitchDegrees = 5f;   // a lane flatter than this is a level lane (tower-td-note §12a)

        /// <summary>One lane as the manifest gives it, world space.</summary>
        public class ModelLane
        {
            public Vector3 foot, head;
            public float width;
            /// <summary>The lane linked at each end, or -1 at a free end.</summary>
            public int footLink = -1, headLink = -1;
            /// <summary>A flight's level run beyond a free end, m (the manifest's stretch length); 0 on a level lane or at a linked end.</summary>
            public float footRun, headRun;
            /// <summary>Reach at a free end by the sign of the sideways offset (positive = Right), from the free end, never increasing.</summary>
            public float[] footNeg, footPos, headNeg, headPos;
            public float spacing;

            public float Length => Vector3.Distance(foot, head);
            public Vector3 AxisHorizontal => new Vector3(head.x - foot.x, 0f, head.z - foot.z).normalized;
            public Vector3 Right => Vector3.Cross(Vector3.up, AxisHorizontal);
            public float HorizontalLength => new Vector2(head.x - foot.x, head.z - foot.z).magnitude;
            public bool Level => Mathf.Asin(Mathf.Clamp01(Mathf.Abs(head.y - foot.y) / Mathf.Max(Length, 1e-6f))) * Mathf.Rad2Deg < LevelPitchDegrees;
            /// <summary>The lowest and highest lane parameter a player can be at: the lane itself, plus a flight's level run at a free end.</summary>
            public float Low => footLink >= 0 ? 0f : -footRun;
            public float High => headLink >= 0 ? Length : Length + headRun;
        }

        public ModelLane[] Model = new ModelLane[0];
        /// <summary>Tuning values as authored, read once at fixture load.</summary>
        public float AuthoredSlack, AuthoredTaper, AuthoredGlideDegrees, AuthoredMouthFactor, AuthoredAscendSpeed, AuthoredMouthHeight, AuthoredCatchBand;

        /// <summary>
        /// Where a point sits on a model lane: metres up the slope from the foot (level metres beyond either end),
        /// metres to the right of the line, metres above the line there.
        /// </summary>
        public static void Locate(Vector3 feet, ModelLane lane, out float u, out float lateral, out float above)
        {
            Vector3 d = feet - lane.foot, along = lane.AxisHorizontal;
            float h = d.x * along.x + d.z * along.z, run = lane.HorizontalLength, length = lane.Length;
            lateral = Vector3.Dot(d, lane.Right);
            if (h < 0f) { u = h; above = feet.y - lane.foot.y; }
            else if (h > run) { u = length + (h - run); above = feet.y - lane.head.y; }
            else { u = run > 0f ? h / run * length : 0f; above = feet.y - Mathf.Lerp(lane.foot.y, lane.head.y, run > 0f ? h / run : 0f); }
        }

        /// <summary>The player's lane parameter on the model lane they are on.</summary>
        float OwnU()
        {
            Locate(Player.transform.position, Model[Player.LaneIndex], out float u, out _, out _);
            return u;
        }

        static float Reach(float[] profile, float spacing, float fromFreeEnd)
        {
            if (profile == null || profile.Length == 0 || spacing <= 0f) return float.PositiveInfinity;
            float at = Mathf.Max(fromFreeEnd, 0f) / spacing;
            int last = profile.Length - 1;
            int below = Mathf.Min(Mathf.FloorToInt(at + 1e-4f), last), over = Mathf.Min(Mathf.CeilToInt(at - 1e-4f), last);
            return Mathf.Min(profile[below], profile[over]);
        }

        /// <summary>The three-term envelope on a final level stretch, for the side the offset is on, and which term set it.</summary>
        float Envelope(ModelLane lane, bool freeHead, float sign, float fromFreeEnd, float stretch, out string term)
        {
            term = "lip";
            if (stretch <= 0f) return 0f;
            float toGo = Mathf.Clamp(stretch - fromFreeEnd, 0f, stretch), along = stretch - toGo;
            float cap = lane.width * AuthoredMouthFactor * 0.5f, slope = Mathf.Tan(AuthoredGlideDegrees * Mathf.Deg2Rad) * toGo;
            float[] neg = freeHead ? lane.headNeg : lane.footNeg, pos = freeHead ? lane.headPos : lane.footPos;
            float reach = sign > 0f ? Reach(pos, lane.spacing, along) : (sign < 0f ? Reach(neg, lane.spacing, along) : Mathf.Min(Reach(neg, lane.spacing, along), Reach(pos, lane.spacing, along)));
            term = reach <= cap && reach <= slope ? "reach" : (slope <= cap ? "slope" : "cap");
            return Mathf.Max(0f, Mathf.Min(cap, Mathf.Min(slope, reach)));
        }

        /// <summary>
        /// The sideways bound at a point on a model lane, for the side <paramref name="sign"/> is on: on a flight the
        /// authored slack tapering to zero before each end; on a final level stretch the envelope; zero elsewhere.
        /// </summary>
        public float Bound(ModelLane lane, float u, float sign, out string term)
        {
            float length = lane.Length;
            if (lane.Level)
            {
                bool footFree = lane.footLink < 0, headFree = lane.headLink < 0;
                term = "inner segment";
                if (footFree == headFree) return 0f;
                return Envelope(lane, headFree, sign, headFree ? length - u : u, length, out term);
            }
            term = "linked end";
            if (u < 0f) return lane.footLink < 0 ? Envelope(lane, false, sign, lane.footRun + u, lane.footRun, out term) : 0f;
            if (u > length) return lane.headLink < 0 ? Envelope(lane, true, sign, lane.headRun - (u - length), lane.headRun, out term) : 0f;
            term = "flight";
            return AuthoredTaper > 0f ? AuthoredSlack * Mathf.Clamp01(Mathf.Min(u, length - u) / AuthoredTaper) : AuthoredSlack;
        }

        // ---- on a lane ----------------------------------------------------------------------------------------------

        /// <summary>Lane-lateral violations seen on a flight proper and on a level stretch (the shifted-lane twin reads both).</summary>
        public int LateralOnFlight, LateralOnLevel;
        /// <summary>Largest sideways offset on each lane's flight proper (0 to length), m.</summary>
        public float[] MaxFlightLateralByLane = new float[0];

        int checkedLane = None;
        float checkedLateral, checkedHeight, joinHeight;

        void CheckLane(Vector3 feet)
        {
            int index = Player.LaneIndex;
            ModelLane lane = Model[index];
            Locate(feet, lane, out float u, out float signed, out float above);
            float lateral = Mathf.Abs(signed), height = Mathf.Abs(above - capsule.skinWidth);
            bool onFlight = !lane.Level && u >= 0f && u <= lane.Length;
            float bound = Bound(lane, u, signed, out string term);

            MaxLaneLateral = Mathf.Max(MaxLaneLateral, lateral);
            if (MaxLateralByLane.Length != Model.Length) { MaxLateralByLane = new float[Model.Length]; MaxFlightLateralByLane = new float[Model.Length]; }
            MaxLateralByLane[index] = Mathf.Max(MaxLateralByLane[index], lateral);
            if (onFlight) MaxFlightLateralByLane[index] = Mathf.Max(MaxFlightLateralByLane[index], lateral);

            if (lateral > bound + LookoutTargets.LaneLateralMargin)
            {
                if (onFlight) LateralOnFlight++; else LateralOnLevel++;
                Flag(ref Escapes, $"escape (lane-lateral): {signed:+0.000;-0.000} m off lane {index + 1}'s line on {(onFlight ? "the flight" : "a level stretch")} at u={u:F3}; bound {bound:F3} ({term})");
            }
            // Never outward: on one lane the offset's size does not grow from frame to frame.
            if (checkedLane == index && lateral > checkedLateral + LookoutTargets.JoinGrowthAllowance)
                Flag(ref Escapes, $"escape (lane-inward): offset on lane {index + 1} grew from {checkedLateral:F4} to {lateral:F4} m at u={u:F3}");

            // Height: on the line from the first frame, unless the join began from above (the open hatch); then it may
            // only close, within the time the stair speed needs.
            bool fromAbove = joinHeight > LookoutTargets.JoinFromAboveHeight && Time.time - joinedAt <= joinHeight / AuthoredAscendSpeed + Time.deltaTime;
            if (fromAbove)
            {
                if (height > AuthoredMouthHeight + AuthoredCatchBand || (checkedLane == index && height > checkedHeight + LookoutTargets.JoinGrowthAllowance))
                    Flag(ref Escapes, $"escape (lane-height): feet {height:F3} m off lane {index + 1}'s line while settling a join from above");
            }
            else if (height > LookoutTargets.LaneHeightMargin) Flag(ref Escapes, $"escape (lane-height): feet {height:F3} m off lane {index + 1}'s nosing line");

            if (u < lane.Low - LookoutTargets.LaneLateralMargin || u > lane.High + LookoutTargets.LaneLateralMargin)
                Flag(ref Escapes, $"escape (lane-range): lane parameter {u:F3} outside lane {index + 1} ({lane.Low:F2} to {lane.High:F2})");

            checkedLane = index;
            checkedLateral = lateral;
            checkedHeight = height;
        }

        /// <summary>A join from the floor or from a fall: logged with the bound in force where it happened.</summary>
        void BeginJoin(Vector3 feet)
        {
            int index = Player.LaneIndex;
            ModelLane lane = Model[index];
            Locate(feet, lane, out float u, out float signed, out float above);
            float bound = Bound(lane, u, signed, out string term);
            joinedAt = Time.time;
            joinHeight = Mathf.Abs(above - capsule.skinWidth);
            checkedLane = None;
            Joins.Add($"{{\"lane\":{index + 1},\"u\":{u:F4},\"side\":\"{(signed > 0f ? "right" : (signed < 0f ? "left" : "line"))}\",\"offset\":{Mathf.Abs(signed):F4},\"bound\":{bound:F4},\"term\":\"{term}\"," +
                      $"\"height\":{joinHeight:F4},\"caught\":{(Player.LaneCatches > catchesAtJoin ? "true" : "false")}}}");
            catchesAtJoin = Player.LaneCatches;
        }
    }
}
