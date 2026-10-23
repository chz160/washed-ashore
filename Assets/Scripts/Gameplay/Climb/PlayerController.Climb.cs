using UnityEngine;

namespace WashedAshore.Gameplay
{
    /// <summary>
    /// Stair lanes and climb areas (spec C1-C2, tower-design-spec.md §2.3, tower-td-note §6). Decided every frame
    /// from position, like the water. On a lane the CharacterController is disabled and the feet are written from
    /// <see cref="ClimbRules"/>; landings are ordinary ground. Inside an area the capsule is the area's small
    /// radius, jump is off and walking uses the area's speed. With no area enabled none of this runs.
    /// </summary>
    public partial class PlayerController
    {
        const float MovedBySomeoneSqr = 1e-6f; // 1 mm: anything else moving the player while on a lane

        static readonly Collider[] fitBuffer = new Collider[8];

        ClimbArea climbArea;      // the area whose climb state is in force; null off the tower
        ClimbArea leftArea;
        int lane = -1, leftLane = -1;
        float laneU, laneLateral, laneLift, laneSpeed, authoredRadius, smallOutsideFor;
        ClimbTuning lastClimbTuning;
        bool warnedSmallCapsule;
        Collider fitBlocker;
        Vector3 laneWritten, leftAt;
        bool leftReleased;

        /// <summary>The climb area whose rules are in force (small capsule, jump lock, walk speed); null outside one.</summary>
        public ClimbArea ClimbArea => climbArea;
        public bool OnLane => lane >= 0;
        /// <summary>The lane the player is on, or -1.</summary>
        public int LaneIndex => lane;
        /// <summary>Lane parameter: metres up the slope from the foot; outside 0..length it is the level run-in or run-out.</summary>
        public float LaneDistance => laneU;
        /// <summary>Signed speed along the lane, m/s.</summary>
        public float LaneSpeed => laneSpeed;
        /// <summary>The capsule radius the Player was authored with.</summary>
        public float AuthoredRadius => authoredRadius;
        /// <summary>How many times a falling player was caught by a lane (the open hatch). Tests and the C2 log read it.</summary>
        public int LaneCatches { get; private set; }
        /// <summary>Grounded for gameplay rules: on the ground, or standing on a lane.</summary>
        public bool IsSupported => OnLane || IsGrounded;

        bool ClimbJumpLocked => climbArea != null;
        bool ClimbActive => ClimbArea.ActiveCount != 0 || climbArea != null || controller.radius != authoredRadius;

        void SetupClimb()
        {
            authoredRadius = controller.radius;
            WorldBoundsClamp.Snapped += OnSnapped;
        }

        void TeardownClimb() => WorldBoundsClamp.Snapped -= OnSnapped;

        void OnSnapped(GameObject body, Vector3 from, Vector3 to)
        {
            if (body != gameObject) return;
            ClearClimb();
            Teleported?.Invoke();
        }

        /// <summary>Drops all lane and area state this frame (Respawn, a snap, leaving the bounds). The radius grows back through the fit check.</summary>
        void ClearClimb()
        {
            lane = leftLane = -1;
            laneSpeed = laneLift = 0f;
            climbArea = leftArea = null;
            if (!controller.enabled) controller.enabled = true;
        }

        /// <summary>True when this frame's movement was a lane step, so the walk branch must not run.</summary>
        bool ClimbMove(Vector2 input, bool sprintHeld, float dt)
        {
            Vector3 feet = transform.position;
            // Someone else moved the player or re-enabled the controller (a test kit, a snap): let go.
            if (lane >= 0 && (controller.enabled || (feet - laneWritten).sqrMagnitude > MovedBySomeoneSqr)) ClearClimb();
            if (climbArea != null && (!climbArea.isActiveAndEnabled || !climbArea.Contains(feet) || waterMode != WaterMode.Dry)) ClearClimb();
            FitCapsule(feet);

            var area = climbArea != null ? climbArea : (waterMode == WaterMode.Dry ? ClimbArea.At(feet) : null);
            if (area == null) return false;

            Vector3 wish = Vector3.ClampMagnitude(transform.forward * input.y + transform.right * input.x, 1f);
            if (leftLane >= 0 && wish.sqrMagnitude < Mathf.Epsilon) leftReleased = true;
            if (lane < 0) TryJoin(area, feet, wish);
            if (lane < 0) return false;
            StepLane(wish, sprintHeld, dt);
            return true;
        }

        void TryJoin(ClimbArea area, Vector3 feet, Vector3 wish)
        {
            var t = area.Tuning;
            bool grounded = controller.isGrounded;
            // Falling into a lane is only for a player already in the area (the open hatch), never from the ground beside a flight.
            bool falling = !grounded && verticalVelocity < 0f && climbArea == area;
            int best = -1;
            float bestU = 0f, bestLateral = float.MaxValue;
            for (int i = 0; i < area.LaneCount; i++)
            {
                ref readonly StairLane candidate = ref area.Lane(i);
                float u = 0f, lateral = 0f;
                bool joins;
                if (area.Blocked(i)) continue;
                if (grounded)
                {
                    // An end linked to another lane has no mouth: it can't be joined from the floor.
                    float footRun = area.TryLink(i, ClimbRules.End.Foot, out _, out _) ? -1f : ClimbRules.FootRun(candidate, t);
                    float headRun = area.TryLink(i, ClimbRules.End.Head, out _, out _) ? -1f : ClimbRules.HeadRun(candidate, t);
                    var end = ClimbRules.JoinsAtEnd(feet, wish, candidate, t, footRun, headRun, controller.skinWidth, out u, out lateral);
                    // A lane gated below its head (the shut hatch) can't be entered from the top.
                    joins = end != ClimbRules.End.None && MayRejoin(area, i, feet, t)
                        && (end == ClimbRules.End.Foot || area.Gate(i) >= candidate.Length);
                }
                else joins = falling && ClimbRules.JoinsFromAbove(feet, candidate, t.laneCatchBand, out u, out lateral);
                // Inside two mouths at once: the lane whose centre line is nearer wins.
                if (joins && Mathf.Abs(lateral) < Mathf.Abs(bestLateral)) { best = i; bestU = u; bestLateral = lateral; }
            }
            if (best < 0) return;
            if (!grounded) LaneCatches++;

            climbArea = area;
            lane = best;
            laneU = bestU;
            laneLateral = bestLateral;
            laneLift = feet.y - LaneFeet(area.Lane(best), bestU, 0f).y;
            // Carry the walking speed onto the lane (no dead stop at the mouth), within what the lane allows at this point.
            ref readonly StairLane joined = ref area.Lane(best);
            // The walking speed is kept and the lane brakes it at stairAcceleration (no cut at the mouth). The one cap is the
            // area's own walk speed: a sprint in from outside the area becomes the area's walk the moment its rules start.
            float carried = Vector3.Dot(horizontalVelocity, joined.AxisHorizontal);
            laneSpeed = grounded ? Mathf.Clamp(carried, -t.towerWalkSpeed, t.towerWalkSpeed) : 0f;
            laneWritten = feet;
            leftLane = -1;
            horizontalVelocity = Vector3.zero;
            verticalVelocity = 0f;
            controller.enabled = false;
            FitCapsule(feet);
        }

        bool MayRejoin(ClimbArea area, int index, Vector3 feet, ClimbTuning t)
        {
            if (leftLane != index || leftArea != area) return true;
            return ClimbRules.RejoinAllowed(Vector3.Distance(feet, leftAt), t.laneRejoinDistance, leftReleased);
        }

        void StepLane(Vector3 wish, bool sprintHeld, float dt)
        {
            var t = climbArea.Tuning;
            StairLane on = climbArea.Lane(lane);
            float length = on.Length;
            bool footLinked = climbArea.TryLink(lane, ClimbRules.End.Foot, out int footLane, out var footEnd);
            bool headLinked = climbArea.TryLink(lane, ClimbRules.End.Head, out int headLane, out var headEnd);
            // A linked end has no level run and no hand-over: the next lane starts there.
            float footRun = footLinked ? 0f : ClimbRules.FootRun(on, t), headRun = headLinked ? 0f : ClimbRules.HeadRun(on, t);
            float target = LaneTarget(climbArea, lane, on, ClimbRules.Along(wish, on), sprintHeld, laneU, t);
            float carried = Mathf.Abs(Mathf.MoveTowards(laneSpeed, target, t.stairAcceleration * dt));
            float max = Mathf.Min(climbArea.Gate(lane), length + headRun);
            laneU = ClimbRules.Step(laneU, ref laneSpeed, target, t.stairAcceleration, -footRun, max, dt);
            // Sideways: clamp into the band for where the player now is (a negative run marks a linked end). Never pushed outward.
            ClimbRules.SettleOffsets(ref laneLateral, ref laneLift,
                ClimbRules.LateralBand(on, laneU, footLinked ? -1f : footRun, headLinked ? -1f : headRun, t, laneLateral), t, dt);

            // At a junction: carry on along the next lane if the input points along it; otherwise stay on this one.
            bool passed = headLinked && laneU >= length && PassOnto(headLane, headEnd, wish, carried, t)
                          || footLinked && laneU <= 0f && PassOnto(footLane, footEnd, wish, carried, t);
            if (passed) on = climbArea.Lane(lane);

            Vector3 feet = LaneFeet(on, laneU, laneLateral) + Vector3.up * laneLift;
            transform.position = laneWritten = feet;
            // The junction is one point on both lanes, so nothing moved; the new lane's own rules start next frame.
            if (passed) return;

            var end = ClimbRules.LeavesAt(laneU, target, length, footRun, headRun);
            if (end == ClimbRules.End.Foot && footLinked || end == ClimbRules.End.Head && headLinked) end = ClimbRules.End.None;
            if (end == ClimbRules.End.None) return;
            leftArea = climbArea;
            leftLane = lane;
            leftAt = feet;
            leftReleased = false;
            lane = -1;
            laneSpeed = laneLift = 0f;
            verticalVelocity = groundedStickVelocity;
            // Carry the lane's speed onto the landing, so the hand-over is not a dead stop either.
            horizontalVelocity = on.AxisHorizontal * target;
            controller.enabled = true;
            // Off the first tread: the area's rules end this frame, so jump works on the next press (director's jump condition).
            if (end == ClimbRules.End.Foot && on.footLeavesArea) { climbArea = null; FitCapsule(feet); }
        }

        /// <summary>Passes from the lane the player is on to the lane linked at this end, if it is open and the input points along it.</summary>
        bool PassOnto(int next, ClimbRules.End nextEnd, Vector3 wish, float carriedSpeed, ClimbTuning t)
        {
            if (climbArea.Blocked(next)) return false;
            ref readonly StairLane to = ref climbArea.Lane(next);
            // Entering a lane at its head while it is gated below the head (the shut hatch): not open.
            if (nextEnd == ClimbRules.End.Head && climbArea.Gate(next) < to.Length) return false;
            if (!ClimbRules.Continues(wish, to, nextEnd, t)) return false;
            lane = next;
            laneU = nextEnd == ClimbRules.End.Foot ? 0f : to.Length;
            // Speed carries over; if the next lane is slower there, it brakes at stairAcceleration like anywhere else.
            laneSpeed = nextEnd == ClimbRules.End.Foot ? carriedSpeed : -carriedSpeed;
            return true;
        }

        /// <summary>Signed target speed on a lane at a point: stair speed on an incline (walk speed on its level run), walk speed on a level lane, ramped where level meets incline.</summary>
        static float LaneTarget(ClimbArea area, int index, in StairLane on, float along, bool sprintHeld, float u, ClimbTuning t)
        {
            if (!ClimbRules.IsLevel(on)) return ClimbRules.TargetSpeed(along, sprintHeld, u, on.Length, t);
            // The incline this level lane leads to, through any other level lanes linked in between (a run-out of two segments).
            var junctionAt = ClimbRules.End.None;
            if (InclineBeyond(area, index, ClimbRules.End.Foot, out float between, out var inclineEnd)) junctionAt = ClimbRules.End.Foot;
            else if (InclineBeyond(area, index, ClimbRules.End.Head, out between, out inclineEnd)) junctionAt = ClimbRules.End.Head;
            return ClimbRules.LevelLaneTargetSpeed(along, sprintHeld, u, on.Length, junctionAt, inclineEnd, t, between);
        }

        /// <summary>
        /// Follows the links from one end of a level lane through other level lanes until an inclined lane is met.
        /// True when there is one; then the level length passed on the way, and the end of the incline it is met at.
        /// </summary>
        static bool InclineBeyond(ClimbArea area, int index, ClimbRules.End side, out float levelBetween, out ClimbRules.End inclineEnd)
        {
            levelBetween = 0f;
            inclineEnd = ClimbRules.End.None;
            for (int hops = 0; hops < area.LaneCount; hops++)
            {
                if (!area.TryLink(index, side, out int next, out var nextEnd)) return false;
                ref readonly StairLane lane = ref area.Lane(next);
                if (!ClimbRules.IsLevel(lane)) { inclineEnd = nextEnd; return true; }
                levelBetween += lane.Length;
                index = next;
                side = nextEnd == ClimbRules.End.Foot ? ClimbRules.End.Head : ClimbRules.End.Foot;
            }
            return false;
        }

        /// <summary>Feet on a lane: the nosing line plus the controller's skin, which is where the controller rests on a floor.</summary>
        Vector3 LaneFeet(in StairLane on, float u, float lateral) => on.Point(u, lateral) + Vector3.up * controller.skinWidth;

        /// <summary>Walk target inside a climb area off the lanes: the area's speed, sprint ignored.</summary>
        Vector3 ClimbWalkTarget(Vector2 input) =>
            (transform.forward * input.y + transform.right * input.x) * climbArea.Tuning.towerWalkSpeed;

        /// <summary>
        /// The capsule radius for where the player is: the area's small radius inside, the authored one outside.
        /// It shrinks at once; it grows back only when the authored capsule fits here, checked every frame.
        /// No other controller value is ever written.
        /// </summary>
        void FitCapsule(Vector3 feet)
        {
            if (climbArea != null) lastClimbTuning = climbArea.Tuning;
            float want = climbArea != null ? climbArea.Tuning.towerCapsuleRadius : authoredRadius;
            if (Mathf.Approximately(controller.radius, want)) { smallOutsideFor = 0f; warnedSmallCapsule = false; return; }
            if (want < controller.radius || AuthoredCapsuleFits(feet)) { controller.radius = want; smallOutsideFor = 0f; warnedSmallCapsule = false; return; }
            // Still small off the climb: fine beside the tower, a bug anywhere else (banks, bluffs and the swim footprint assume the authored radius).
            if (ClimbArea.At(feet) != null) { smallOutsideFor = 0f; return; }
            smallOutsideFor += Time.deltaTime;
            if (warnedSmallCapsule || lastClimbTuning == null || smallOutsideFor < lastClimbTuning.capsuleRestoreSeconds) return;
            warnedSmallCapsule = true;
            string blocker = fitBlocker != null ? $"{fitBlocker.name} (layer {LayerMask.LayerToName(fitBlocker.gameObject.layer)})" : "a full overlap buffer";
            Debug.LogError($"PlayerController: capsule radius is still {controller.radius} (authored {authoredRadius}) {smallOutsideFor:F1} s after leaving a climb area, " +
                           $"at {feet:F2}; the authored capsule is blocked by {blocker}.", this);
        }

        /// <summary>
        /// True when the authored capsule, skin included, touches nothing but the player: the whole capsule, lifted
        /// by the tuned amount so level ground under it doesn't count. A full result buffer counts as blocked;
        /// staying small a little longer is the safe failure.
        /// </summary>
        bool AuthoredCapsuleFits(Vector3 feet)
        {
            if (lastClimbTuning == null) return true;
            float r = authoredRadius + controller.skinWidth;
            Vector3 bottom = feet + Vector3.up * (r + lastClimbTuning.capsuleFitLift);
            Vector3 top = feet + controller.center + Vector3.up * (controller.height * 0.5f - authoredRadius);
            if (bottom.y > top.y) bottom = top;
            int hits = Physics.OverlapCapsuleNonAlloc(bottom, top, r, fitBuffer, WashedAshore.World.WorldLayers.PlayerCollisionMask, QueryTriggerInteraction.Ignore);
            fitBlocker = null;
            if (hits >= fitBuffer.Length) return false;
            for (int i = 0; i < hits; i++)
                if (fitBuffer[i] != controller) { fitBlocker = fitBuffer[i]; return false; }
            return true;
        }
    }
}
