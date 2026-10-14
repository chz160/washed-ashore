using UnityEngine;

namespace WashedAshore.Gameplay
{
    /// <summary>
    /// Pure lane rules (tower-design-spec.md §2.3 points 2-6, tower-td-note §6 condition 1), covered by EditMode
    /// tests. A lane has no physics: the feet are a function of (lane, distance along it, lateral offset).
    /// </summary>
    public static class ClimbRules
    {
        /// <summary>Which end of a lane a join or a hand-over happens at.</summary>
        public enum End { None, Foot, Head }

        /// <summary>A lane flatter than this is level: it moves at the area's walk speed (tower-design-spec.md §2.3 point 4).</summary>
        public const float LevelPitchDegrees = 5f;

        public static bool IsLevel(in StairLane lane) => Mathf.Abs(lane.Pitch) < LevelPitchDegrees;

        /// <summary>
        /// At a junction of two lanes: true when the input points along the next lane, away from the junction, by at
        /// least <c>laneJunctionAlong</c> (tighter than the cone for joining from free floor). Otherwise the player stays
        /// on the lane they are on (tower-td-note §12a rule 2).
        /// </summary>
        public static bool Continues(Vector3 wish, in StairLane next, End enteredAt, ClimbTuning t)
        {
            float along = Along(wish, next);
            return (enteredAt == End.Foot ? along : -along) >= t.laneJunctionAlong;
        }

        /// <summary>
        /// Signed target speed on a level lane (toward its head is positive): the area's walk speed, ramped by
        /// <see cref="LevelSpeed"/> near the end where it meets an inclined lane (<paramref name="junctionAt"/>;
        /// None when it meets none). <paramref name="inclineEnd"/> is the end of the inclined lane at that junction:
        /// heading for a flight's head means going down it, heading for its foot means going up.
        /// <paramref name="levelBetween"/> is the length of any other level lanes between this lane's junction end and the
        /// incline (a run-out of two segments), so the ramp is measured along the whole level chain.
        /// </summary>
        public static float LevelLaneTargetSpeed(float along, bool sprint, float u, float length, End junctionAt, End inclineEnd, ClimbTuning t, float levelBetween = 0f)
        {
            float mag = Mathf.Abs(along);
            if (mag < t.laneAlongDeadband) return 0f;
            bool towardHead = along > 0f;
            float full = t.towerWalkSpeed;
            if (junctionAt != End.None)
            {
                bool towardJunction = towardHead == (junctionAt == End.Head);
                bool descendsFlight = towardJunction == (inclineEnd == End.Head);
                float distance = (junctionAt == End.Foot ? u : length - u) + levelBetween;
                full = LevelSpeed(distance, StairSpeed(!descendsFlight, sprint, t), t);
            }
            return (towardHead ? full : -full) * Mathf.Clamp01(mag / t.laneFullSpeedAlong);
        }

        /// <summary>How much of a yaw-relative wish vector points up the flight, -1 to 1.</summary>
        public static float Along(Vector3 wish, in StairLane lane)
        {
            wish.y = 0f;
            return Vector3.Dot(Vector3.ClampMagnitude(wish, 1f), lane.AxisHorizontal);
        }

        /// <summary>
        /// Signed target speed along the lane at parameter <paramref name="u"/>, m/s (toward the head is positive).
        /// Nothing inside the deadband; full speed from <c>laneFullSpeedAlong</c>. On the flight the full speed is the
        /// stair speed for that direction (sprint multiplies it); on a level run it is <see cref="LevelSpeed"/>.
        /// </summary>
        public static float TargetSpeed(float along, bool sprint, float u, float length, ClimbTuning t)
        {
            float mag = Mathf.Abs(along);
            if (mag < t.laneAlongDeadband) return 0f;
            bool towardHead = along > 0f;
            float stair = StairSpeed(towardHead, sprint, t);
            float outOnTheLevel = u < 0f ? -u : (u > length ? u - length : 0f);
            float full = outOnTheLevel > 0f ? LevelSpeed(outOnTheLevel, stair, t) : stair;
            return (towardHead ? full : -full) * Mathf.Clamp01(mag / t.laneFullSpeedAlong);
        }

        /// <summary>Speed on the incline for a direction: ascent toward the head, descent toward the foot; sprint multiplies it.</summary>
        public static float StairSpeed(bool towardHead, bool sprint, ClimbTuning t)
        {
            float speed = towardHead ? t.stairAscendSpeed : t.stairDescendSpeed;
            return sprint ? speed * t.stairSprintMultiplier : speed;
        }

        /// <summary>
        /// Speed on a level stretch <paramref name="distanceFromIncline"/> m from where it meets the incline: the area's
        /// walk speed, but never more than can be shed (or has been gained) at stairAcceleration over that distance,
        /// sqrt(stair^2 + 2 a d). So the player reaches the first tread already at stair speed and the change is a
        /// ramp, never a step. A function of position only.
        /// </summary>
        public static float LevelSpeed(float distanceFromIncline, float stairSpeed, ClimbTuning t)
        {
            float ramp = Mathf.Sqrt(stairSpeed * stairSpeed + 2f * t.stairAcceleration * Mathf.Max(0f, distanceFromIncline));
            return Mathf.Min(Mathf.Max(t.towerWalkSpeed, stairSpeed), ramp);
        }

        /// <summary>Where a point sits on a lane: the lane parameter, metres right of centre, metres above the nosing line there.</summary>
        public static void Locate(Vector3 feet, in StairLane lane, out float u, out float lateral, out float above)
        {
            Vector3 d = feet - lane.foot;
            lateral = Vector3.Dot(d, lane.Right);
            float run = Vector3.Dot(d, lane.AxisHorizontal), length = lane.Length, perMetre = lane.RunPerMetre;
            float totalRun = length * perMetre;
            if (perMetre < 1e-5f) u = Mathf.Clamp(d.y, 0f, length);  // vertical lane: height decides
            else if (run < 0f) u = run;
            else if (run > totalRun) u = length + (run - totalRun);
            else u = run / perMetre;
            above = feet.y - lane.Point(u, 0f).y;
        }

        /// <summary>
        /// The end a walking player joins at (§2.3 point 3), or None. The mouth of a free end is its whole final level
        /// stretch (a flight's level run, or the whole of a level lane), as wide at each point as the join funnel
        /// (<see cref="LateralBand"/>), so a join is always inside the band and never a snap. The wish must point into
        /// the lane inside the join cone (<c>laneJoinAlong</c>, 60 degrees by default), narrower than the on-lane
        /// deadband on purpose: crossing a landing to the next flight must not be taken for going back down the last.
        /// </summary>
        public static End JoinsAtEnd(Vector3 feet, Vector3 wish, in StairLane lane, ClimbTuning t, out float u, out float lateral)
        {
            return JoinsAtEnd(feet, wish, lane, t, FootRun(lane, t), HeadRun(lane, t), 0f, out u, out lateral);
        }

        /// <summary>
        /// As above with each end's level run given. A negative run means that end is linked to another lane and can't
        /// be joined from the floor at all. <paramref name="slop"/> is how far past the free end a join still counts
        /// (the controller's skin: it stops that far from where it is aimed); such a join starts at the free end.
        /// </summary>
        public static End JoinsAtEnd(Vector3 feet, Vector3 wish, in StairLane lane, ClimbTuning t, float footRun, float headRun, float slop, out float u, out float lateral)
        {
            Locate(feet, lane, out u, out lateral, out _);
            float along = Along(wish, lane), length = lane.Length;
            bool level = IsLevel(lane);
            // The stretch a foot join may start on: the level run below the foot, or the whole of a level lane.
            float footFrom = level ? 0f : -footRun, footTo = level ? length : 0f;
            if (footRun >= 0f && u >= footFrom - slop - BandEpsilon && u <= footTo + BandEpsilon && Mathf.Abs(feet.y - lane.foot.y) <= t.laneMouthHeight && along >= t.laneJoinAlong)
            {
                float at = Mathf.Clamp(u, footFrom, footTo);
                if (Mathf.Abs(lateral) <= LateralBand(lane, at, footRun, headRun, t, lateral) + BandEpsilon) { u = at; return End.Foot; }
            }
            float headFrom = level ? 0f : length, headTo = level ? length : length + headRun;
            if (headRun >= 0f && u >= headFrom - BandEpsilon && u <= headTo + slop + BandEpsilon && Mathf.Abs(feet.y - lane.head.y) <= t.laneMouthHeight && along <= -t.laneJoinAlong)
            {
                float at = Mathf.Clamp(u, headFrom, headTo);
                if (Mathf.Abs(lateral) <= LateralBand(lane, at, footRun, headRun, t, lateral) + BandEpsilon) { u = at; return End.Head; }
            }
            return End.None;
        }

        /// <summary>
        /// Null when the taper fits a set of lanes: it is no longer than half the shortest inclined lane, so the tapers at
        /// a flight's two ends never overlap. Otherwise what is wrong.
        /// </summary>
        public static string TaperProblem(StairLane[] lanes, ClimbTuning t)
        {
            for (int i = 0; i < lanes.Length; i++)
                if (!IsLevel(lanes[i]) && t.laneTaperLength * 2f > lanes[i].Length)
                    return $"laneTaperLength {t.laneTaperLength} is more than half of lane {i + 1}'s length {lanes[i].Length:F3}";
            return null;
        }

        /// <summary>Float allowance when comparing an offset with its band, m.</summary>
        public const float BandEpsilon = 1e-4f;

        /// <summary>The widest a join mouth may be: the lane's own width times the tuned factor (half of it each side).</summary>
        public static float MouthWidth(in StairLane lane, ClimbTuning t)
        {
            return lane.width * t.laneMouthWidthFactor;
        }

        /// <summary>
        /// A reach profile at <paramref name="fromFreeEnd"/> metres along its stretch: the smaller of the two samples
        /// either side (never an interpolation, so never wider than the model computed). No profile = no limit.
        /// </summary>
        public static float Reach(float[] profile, float spacing, float fromFreeEnd)
        {
            if (profile == null || profile.Length == 0 || spacing <= 0f) return float.PositiveInfinity;
            float at = Mathf.Max(fromFreeEnd, 0f) / spacing;
            int last = profile.Length - 1;
            int below = Mathf.Min(Mathf.FloorToInt(at + BandEpsilon), last), above = Mathf.Min(Mathf.CeilToInt(at - BandEpsilon), last);
            return Mathf.Min(profile[below], profile[above]);
        }

        /// <summary>A copy of a reach profile that never increases from the free end (each sample is the smallest so far).</summary>
        public static float[] NonIncreasing(float[] profile)
        {
            if (profile == null) return null;
            var result = (float[])profile.Clone();
            for (int i = 1; i < result.Length; i++) result[i] = Mathf.Min(result[i], result[i - 1]);
            return result;
        }

        /// <summary>
        /// The join envelope on one side of a final level stretch (tower-td-note, lane-end ruling), a pure function of
        /// position: the smallest of half the mouth's width, the tangent of <c>laneGlideMaxDegrees</c> times the
        /// distance still to go to the lip or junction, and the model's reach there. It never increases from the free
        /// end and is zero at the lip. <paramref name="side"/> is positive to the right of someone facing up the lane,
        /// negative to the left, zero for the narrower of the two.
        /// </summary>
        public static float Envelope(in StairLane lane, ClimbTuning t, End freeEnd, float side, float fromFreeEnd, float stretchLength)
        {
            if (stretchLength <= 0f) return 0f;
            float toGo = Mathf.Clamp(stretchLength - fromFreeEnd, 0f, stretchLength);
            float open = Mathf.Min(MouthWidth(lane, t) * 0.5f, Mathf.Tan(t.laneGlideMaxDegrees * Mathf.Deg2Rad) * toGo);
            float[] left = freeEnd == End.Foot ? lane.footReachLeft : lane.headReachLeft, right = freeEnd == End.Foot ? lane.footReachRight : lane.headReachRight;
            float along = stretchLength - toGo;
            float reach = side > 0f ? Reach(right, lane.reachSpacing, along)
                : (side < 0f ? Reach(left, lane.reachSpacing, along) : Mathf.Min(Reach(left, lane.reachSpacing, along), Reach(right, lane.reachSpacing, along)));
            return Mathf.Max(0f, Mathf.Min(open, reach));
        }

        /// <summary>
        /// The allowed sideways offset at a point on a lane: a pure function of position (tower-td-note, lane-end ruling;
        /// t-designer's two envelopes). The player's offset is clamped into it and is never pushed outward, so standing
        /// still holds the offset and reversing never widens it.
        ///  - On a flight: <c>laneLateralSlack</c>, tapering to zero over the last <c>laneTaperLength</c> before each end,
        ///    so the player is on the line at every lane end and junction.
        ///  - On a level stretch with a free end (a flight's level run; a level lane whose one end is free): the join
        ///    envelope (<see cref="Envelope"/>), zero where the stretch meets the flight or the junction.
        ///  - On any other level lane (the inner segment of a chain): zero.
        /// A negative run means that end is linked to another lane. A mouth may be narrower on one side than the other:
        /// <paramref name="side"/> is the sign of the offset asked about (zero = the narrower side).
        /// </summary>
        public static float LateralBand(in StairLane lane, float u, float footRun, float headRun, ClimbTuning t, float side = 0f)
        {
            float length = lane.Length;
            if (IsLevel(lane))
            {
                bool footFree = footRun >= 0f, headFree = headRun >= 0f;
                if (length <= 0f || footFree == headFree) return 0f;
                return Envelope(lane, t, headFree ? End.Head : End.Foot, side, headFree ? length - u : u, length);
            }
            if (u < 0f) return footRun > 0f ? Envelope(lane, t, End.Foot, side, footRun + u, footRun) : 0f;
            if (u > length) return headRun > 0f ? Envelope(lane, t, End.Head, side, headRun - (u - length), headRun) : 0f;
            float toEnd = Mathf.Min(u, length - u);
            return t.laneTaperLength > 0f ? t.laneLateralSlack * Mathf.Clamp01(toEnd / t.laneTaperLength) : t.laneLateralSlack;
        }

        /// <summary>
        /// True when a falling player's feet have reached the nosing line inside the lane (§2.3 point 3, the open
        /// hatch): they join where they are, so nothing snaps.
        /// </summary>
        public static bool JoinsFromAbove(Vector3 feet, in StairLane lane, float catchBand, out float u, out float lateral)
        {
            Locate(feet, lane, out u, out lateral, out float above);
            return u > 0f && u < lane.Length && Mathf.Abs(lateral) <= lane.width * 0.5f && above <= 0f && above >= -catchBand;
        }

        /// <summary>
        /// One step along a lane: the speed moves toward the target, the parameter follows, and both stop at the
        /// limits (<paramref name="max"/> is the run-out's end, or less under a shut gate).
        /// </summary>
        public static float Step(float u, ref float speed, float targetSpeed, float acceleration, float min, float max, float dt)
        {
            speed = Mathf.MoveTowards(speed, targetSpeed, acceleration * dt);
            float next = u + speed * dt;
            if (next >= max) { next = max; if (speed > 0f) speed = 0f; }
            if (next <= min) { next = min; if (speed < 0f) speed = 0f; }
            return next;
        }

        /// <summary>Length of the level run beyond a lane's foot: the tuned run-out length, or less where the model has less floor.</summary>
        public static float FootRun(in StairLane lane, ClimbTuning t) => Mathf.Max(0f, Mathf.Min(t.laneRunOutLength, lane.footRunRoom));

        /// <summary>Length of the level run beyond a lane's head.</summary>
        public static float HeadRun(in StairLane lane, ClimbTuning t) => Mathf.Max(0f, Mathf.Min(t.laneRunOutLength, lane.headRunRoom));

        /// <summary>
        /// The end a player on a lane leaves through this frame, or None (§2.3 point 4: only through the two ends).
        /// They are handed back only at the far end of the level run beyond an end, while still heading out, so the
        /// whole capsule is on the landing by then (tower-td-note: no hand-over on the lip, none part-way along the
        /// run). Stopping on the run holds position on the lane; turning back walks back onto the flight.
        /// </summary>
        public static End LeavesAt(float u, float targetSpeed, float length, float footRun, float headRun)
        {
            if (u <= -footRun && targetSpeed < 0f) return End.Foot;
            if (u >= length + headRun && targetSpeed > 0f) return End.Head;
            return End.None;
        }

        /// <summary>
        /// The lane just left can be rejoined once the player has moved <paramref name="rejoinDistance"/> or
        /// released and re-pressed the input (§2.3 point 3).
        /// </summary>
        public static bool RejoinAllowed(float movedSinceLeaving, float rejoinDistance, bool releasedSinceLeaving)
        {
            return releasedSinceLeaving || movedSinceLeaving >= rejoinDistance;
        }

        /// <summary>
        /// The highest lane parameter under a ceiling at <paramref name="ceilingY"/>: the capsule's top stays
        /// <paramref name="headroom"/> below it (the shut hatch on the top flight).
        /// </summary>
        public static float GateUnder(in StairLane lane, float ceilingY, float capsuleHeight, float headroom)
        {
            float feetY = ceilingY - capsuleHeight - headroom, rise = lane.Axis.y;
            if (rise <= 1e-5f) return lane.Length;
            return Mathf.Clamp((feetY - lane.foot.y) / rise, 0f, lane.Length);
        }

        /// <summary>
        /// One step of the offsets a join started with. Sideways: clamped into <paramref name="band"/>, the allowed offset
        /// at where the player now is (<see cref="LateralBand"/>); it only ever moves toward the line and only because the
        /// player advanced into a narrower part. Height: back to the lane's line at stair speed. Nothing snaps (R6).
        /// </summary>
        public static void SettleOffsets(ref float lateral, ref float lift, float band, ClimbTuning t, float dt)
        {
            lateral = Mathf.Clamp(lateral, -band, band);
            lift = Mathf.MoveTowards(lift, 0f, t.stairAscendSpeed * dt);
        }

        /// <summary>On a level stretch of a lane: a level lane, or an inclined lane's level run beyond either end.</summary>
        public static bool OnLevel(in StairLane lane, float u) => IsLevel(lane) || u < 0f || u > lane.Length;

        /// <summary>Seconds up a set of lanes at the tuned ascent speed, plus landing crossings at the area walk speed (§2.4 formula).</summary>
        public static float ClimbSeconds(float laneLengthSum, float landingCrossingSum, ClimbTuning t)
        {
            return laneLengthSum / t.stairAscendSpeed + landingCrossingSum / t.towerWalkSpeed;
        }
    }
}
