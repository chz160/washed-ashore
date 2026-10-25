using System.Collections.Generic;
using UnityEngine;
using WashedAshore.Gameplay;
using WashedAshore.World;

namespace WashedAshore.Tests.PlayMode.Lookout
{
    /// <summary>
    /// Per-frame detector for the climb tests (spec C1-C2; tower-qa-plan.md C1, C2; tower-design-spec.md R6-R9), as
    /// agreed with t-qa before the first run. Runs after everything else each frame and records every violation
    /// instead of asserting on the first. While the script expects the player on the tower:
    /// Lane geometry, links, stretch lengths and reach lists are the detector's own model, built from the build
    /// manifest at fixture load (LookoutWatch.Model.cs); it never calls the game's lane rules.
    /// ESCAPE
    ///  lane-lateral   on a lane, every frame from the first: the signed sideways offset is outside the bound for the
    ///                 side it is on + LaneLateralMargin. The bound: on a flight, the authored slack x clamp01(distance
    ///                 to the nearer lane end / taper length); on a final level stretch, min(half the lane width x
    ///                 mouth factor, tan(glide degrees) x distance to the lip or junction, the manifest's reach there);
    ///                 on an inner segment of a chain, zero;
    ///  lane-inward    on a lane, the offset's size grew from one frame to the next by more than JoinGrowthAllowance;
    ///  lane-range     lane parameter beyond the lane or its level run at a free end (manifest stretch lengths);
    ///  lane-height    feet more than LaneHeightMargin off the nosing line; a join that began more than
    ///                 JoinFromAboveHeight above the line (the open hatch) may close at stair speed, never growing;
    ///  sequence       support changed other than lane to its own foot or head landing and back;
    ///  free-fall      off a lane, feet fell more than EscapeDrop unsupported (the open-hatch drop onto the top lane,
    ///                 up to HatchDropMax, is the one exception);
    ///  no-surface     off a lane, no tower walk surface within EscapeDrop under the feet (unless inside a lane prism);
    ///  in-solid       off a lane, the capsule overlaps a solid tower collider;
    ///  plan           feet outside the tower's tapered plan at that height (leg lines from the build manifest);
    ///  state          (counted separately) the controller's climb state ended other than at the first foot.
    /// LAUNCH   upward feet speed above take-off speed x LaunchFactor, or an eye step above the teleport bound.
    /// STEP     supported feet step above speed in force x dt + FeetStepSlack; and over any SpeedWindow on one lane
    ///          or one floor, distance above the speed in force for that direction x SpeedWindowFactor (+ slack).
    /// CAMERA   FOV or roll changed while <see cref="ExpectAuthoredCamera"/> is set.
    /// </summary>
    [DefaultExecutionOrder(20000)]
    public partial class LookoutWatch : MonoBehaviour
    {
        const int Ground = -1, Falling = -2, None = -3;
        static readonly string[] WalkSurfaces = { "Landing", "CabFloor", "Hatch" };
        static readonly Collider[] buffer = new Collider[16];

        public PlayerController Player;
        public ClimbArea Area;
        public Transform Tower;
        /// <summary>Half the tower's plan at the pad tops and at the cab floor, and the cab floor's height, tower-local (build manifest).</summary>
        public float BaseHalf, TopHalf, CabFloorY;
        /// <summary>The lane that comes up through the hatch (the free-fall exception).</summary>
        public int HatchLane = None;
        /// <summary>Set while the script expects the player on the tower (first join to the exit at the first foot).</summary>
        public bool ExpectOnTower;
        /// <summary>Clear while the firefinder legitimately holds the camera.</summary>
        public bool ExpectAuthoredCamera = true;

        public readonly List<string> Violations = new List<string>();
        /// <summary>Every pass from one lane to the lane linked to it (a junction crossed without a hand-over): frame, lanes, along-lane speed.</summary>
        public readonly List<string> LinkCrossingLog = new List<string>();
        /// <summary>Every frame that crossed the climb area's edge while supported: the area entry cap's one exempt frame each.</summary>
        public readonly List<string> AreaCrossingLog = new List<string>();
        public int LinkCrossings, AreaCrossings;
        public int Frames, Escapes, Launches, StepBreaks, CameraBreaks, StateLost, LaneFrames, RadiusOffTower;
        public float MaxFeetStepOver, MaxUpwardSpeed, MaxLaneLateral, MinRadius = float.MaxValue;
        /// <summary>Largest sideways offset per lane, level stretches included (t-qa: the "by construction" claim, measured).</summary>
        public float[] MaxLateralByLane = new float[0];
        /// <summary>Every join: lane, where, side, sideways offset, the bound there and the term that set it, height above the line.</summary>
        public readonly List<string> Joins = new List<string>();

        struct Sample { public float time, u, allowed; public Vector3 feet; }

        float allowedSoFar;

        readonly Queue<Sample> window = new Queue<Sample>();
        CharacterController capsule;
        Vector3 lastFeet, lastEye;
        bool hasLast, wasOnLane, wasInArea, wasSupported;
        int support = None, lastLane = None, catchesAtJoin;
        float joinedAt, fallFromY, modeSince;
        bool falling;

        void Start() => capsule = Player.GetComponent<CharacterController>();

        /// <summary>Call after the script moves the player by hand (the start pose), so that step isn't judged.</summary>
        public void Forget()
        {
            hasLast = falling = false;
            support = checkedLane = None;
            window.Clear();
        }

        void LateUpdate()
        {
            if (Player == null) return;
            float dt = Time.deltaTime;
            Vector3 feet = Player.transform.position, eye = Player.CameraPivot.position;
            if (Area == null) { WalkableFrame(feet, eye, dt); return; }
            bool onLane = Player.OnLane, inArea = Player.ClimbArea != null, supported = Player.IsSupported;
            var t = Area.Tuning;
            Frames++;
            if (onLane) LaneFrames++;
            MinRadius = Mathf.Min(MinRadius, capsule.radius);
            if (!inArea && !Area.Contains(feet) && !Mathf.Approximately(capsule.radius, Player.AuthoredRadius)) RadiusOffTower++;

            if (hasLast && dt > 0f)
            {
                float up = (feet.y - lastFeet.y) / dt;
                MaxUpwardSpeed = Mathf.Max(MaxUpwardSpeed, up);
                // Inside the climb area nothing rises faster than a slow walk up a flight; outside it a jump is legal.
                float upLimit = inArea || wasInArea ? LookoutTargets.AreaMaxUpwardSpeed : Mathf.Sqrt(2f * Player.JumpHeight * -Player.Gravity) * LookoutTargets.LaunchFactor;
                if (up > upLimit) Flag(ref Launches, $"launch: upward {up:F2} m/s (limit {upLimit:F2}) at {feet:F2}");
                float eyeStep = Vector3.Distance(eye, lastEye), teleport = LookoutTargets.TeleportFactor * Player.SprintSpeed * dt;
                if (eyeStep > teleport) Flag(ref Launches, $"teleport: eye step {eyeStep:F3} m > {teleport:F3} at {feet:F2}");

                // Feet step: only while supported on the tower in both frames (a fall into the hatch is judged by the fall rules).
                if ((inArea || wasInArea) && supported && wasSupported)
                {
                    float speed = SpeedInForce(onLane, wasOnLane, feet.y >= lastFeet.y, t);
                    // The area entry cap, a named step: on the one frame that crosses the area's edge the speed in force is
                    // the faster one outside it (sprint); every frame after is judged at the area's own speed.
                    if (inArea != wasInArea)
                    {
                        speed = Mathf.Max(speed, Player.SprintSpeed);
                        AreaCrossings++;
                        if (AreaCrossingLog.Count < LookoutTargets.ViolationsKept)
                            AreaCrossingLog.Add($"f{Frames}: {(inArea ? "into" : "out of")} the area at {Vector3.Distance(feet, lastFeet) / dt:F2} m/s (frame before: {lastHorizontalSpeed:F2} m/s horizontal)");
                    }
                    float over = Vector3.Distance(feet, lastFeet) - (speed * dt + LookoutTargets.FeetStepSlack);
                    MaxFeetStepOver = Mathf.Max(MaxFeetStepOver, over);
                    if (over > 0f) Flag(ref StepBreaks, $"feet step over the bound by {over:F4} m at {feet:F2} (lane {Player.LaneIndex})");
                }
            }
            if (inArea) SustainedSpeed(feet, onLane, SpeedInForce(onLane, wasOnLane, feet.y >= lastFeet.y, t) * dt, t);
            else window.Clear();
            HandOverSpeed(feet, onLane, dt, t);
            SpeedChange(onLane, dt, t);
            wasOnLevel = OnLevel(onLane);

            if (ExpectAuthoredCamera)
            {
                var cam = Player.ViewCamera;
                float roll = Mathf.Abs(Mathf.DeltaAngle(0f, Player.CameraPivot.eulerAngles.z));
                if (cam != null && !Mathf.Approximately(cam.fieldOfView, Player.AuthoredFov)) Flag(ref CameraBreaks, $"FOV {cam.fieldOfView} at {feet:F2}");
                if (roll > LookoutTargets.CameraRollTolerance) Flag(ref CameraBreaks, $"camera roll {roll:F3} at {feet:F2}");
            }

            if (ExpectOnTower) CheckEscape(feet, onLane, inArea, supported, t);
            else { support = None; falling = false; }

            lastFeet = feet;
            lastEye = eye;
            hasLast = true;
            wasOnLane = onLane;
            wasInArea = inArea;
            wasSupported = supported;
        }

        /// <summary>
        /// The walkable tower has no climb area, so only the lane-free checks run: no launch, no teleport, and the authored camera (FOV and roll)
        /// while <see cref="ExpectAuthoredCamera"/> is set. Same bounds and wording as the guided frame check.
        /// </summary>
        void WalkableFrame(Vector3 feet, Vector3 eye, float dt)
        {
            Frames++;
            MinRadius = Mathf.Min(MinRadius, capsule.radius);
            if (hasLast && dt > 0f)
            {
                float up = (feet.y - lastFeet.y) / dt;
                MaxUpwardSpeed = Mathf.Max(MaxUpwardSpeed, up);
                float upLimit = Mathf.Sqrt(2f * Player.JumpHeight * -Player.Gravity) * LookoutTargets.LaunchFactor;
                if (up > upLimit) Flag(ref Launches, $"launch: upward {up:F2} m/s (limit {upLimit:F2}) at {feet:F2}");
                float eyeStep = Vector3.Distance(eye, lastEye), teleport = LookoutTargets.TeleportFactor * Player.SprintSpeed * dt;
                if (eyeStep > teleport) Flag(ref Launches, $"teleport: eye step {eyeStep:F3} m > {teleport:F3} at {feet:F2}");
            }
            if (ExpectAuthoredCamera)
            {
                var cam = Player.ViewCamera;
                float roll = Mathf.Abs(Mathf.DeltaAngle(0f, Player.CameraPivot.eulerAngles.z));
                if (cam != null && !Mathf.Approximately(cam.fieldOfView, Player.AuthoredFov)) Flag(ref CameraBreaks, $"FOV {cam.fieldOfView} at {feet:F2}");
                if (roll > LookoutTargets.CameraRollTolerance) Flag(ref CameraBreaks, $"camera roll {roll:F3} at {feet:F2}");
            }
            lastFeet = feet;
            lastEye = eye;
            hasLast = true;
        }

        static float LaneSpeedCap(ClimbTuning t, bool ascending) =>
            (ascending ? t.stairAscendSpeed : t.stairDescendSpeed) * t.stairSprintMultiplier;

        bool wasOnLevel;
        float lastLaneSpeed;
        int lastSpeedLane = None;
        /// <summary>Largest frame-to-frame change of along-lane speed seen near each lane end (index lane x 2, + 1 for the head), m/s.</summary>
        public float[] MaxSpeedChangeByEnd = new float[0];

        /// <summary>On a level stretch of a lane: a level lane, or an inclined lane's level run beyond either end.</summary>
        bool OnLevel(bool onLane)
        {
            if (!onLane) return false;
            ModelLane lane = Model[Player.LaneIndex];
            float u = OwnU();
            return lane.Level || u < 0f || u > lane.Length;
        }

        /// <summary>
        /// The speed in force this frame (frozen wording): on a lane within the flight, the stair speed for the frame's
        /// direction x the sprint multiplier; on a level run, towerWalkSpeed; off a lane in the area, towerWalkSpeed;
        /// on the one frame that crosses between two of these, the larger.
        /// </summary>
        float SpeedInForce(bool onLane, bool wasOnLane, bool ascending, ClimbTuning t)
        {
            bool level = OnLevel(onLane);
            float now = onLane && !level ? LaneSpeedCap(t, ascending) : t.towerWalkSpeed;
            float before = wasOnLane && !wasOnLevel ? LaneSpeedCap(t, ascending) : t.towerWalkSpeed;
            return Mathf.Max(now, before);
        }

        /// <summary>Frames the speed-change assertion did not judge because the player was at a hard limit, and the first of them.</summary>
        public int ExemptSpeedFrames;
        public readonly List<string> ExemptLog = new List<string>();
        /// <summary>Largest change of horizontal feet speed across a hand-over (join or leave), m/s, and how many hand-overs were seen.</summary>
        public float MaxHandOverSpeedChange;
        public int HandOvers;

        /// <summary>The hard limit the player is at on a lane, to within AtLimitWithin, or null: the gate, a lane or run end, a junction.</summary>
        string LimitAt(int lane, ClimbTuning t)
        {
            ModelLane on = Model[lane];
            float u = OwnU(), eps = LookoutTargets.AtLimitWithin;
            bool footLinked = on.footLink >= 0, headLinked = on.headLink >= 0;
            float low = on.Low, high = on.High;
            if (Mathf.Abs(u - Area.Gate(lane)) <= eps) return "at the gate";
            if (Mathf.Abs(u - low) <= eps) return footLinked ? "at the foot junction" : "at the foot end";
            if (Mathf.Abs(u - high) <= eps) return headLinked ? "at the head junction" : "at the head end";
            return null;
        }

        float lastHorizontalSpeed;

        /// <summary>
        /// Across a hand-over (walking into a mouth, or leaving at a free end) the horizontal speed of the feet may change
        /// by no more than the same bound: speed is carried, so neither is a dead stop.
        /// </summary>
        void HandOverSpeed(Vector3 feet, bool onLane, float dt, ClimbTuning t)
        {
            float horizontal = dt > 0f && hasLast ? Vector3.Distance(new Vector3(feet.x, 0f, feet.z), new Vector3(lastFeet.x, 0f, lastFeet.z)) / dt : 0f;
            if (hasLast && onLane != wasOnLane && Player.IsSupported && wasSupported)
            {
                HandOvers++;
                float change = Mathf.Abs(horizontal - lastHorizontalSpeed);
                MaxHandOverSpeedChange = Mathf.Max(MaxHandOverSpeedChange, change);
                float limit = t.stairAcceleration * dt * LookoutTargets.SpeedChangeFactor + LookoutTargets.SpeedChangeAllowance;
                if (change > limit) Flag(ref StepBreaks, $"speed step at a hand-over ({(onLane ? "onto" : "off")} lane {(onLane ? Player.LaneIndex : lastSpeedLane) + 1}): {lastHorizontalSpeed:F3} to {horizontal:F3} m/s (limit {limit:F3}) at {feet:F2}");
            }
            lastHorizontalSpeed = horizontal;
        }

        /// <summary>On a lane the along-lane speed is ramped, never stepped: its change between frames is bounded by stairAcceleration.</summary>
        void SpeedChange(bool onLane, float dt, ClimbTuning t)
        {
            int lane = onLane ? Player.LaneIndex : None;
            float speed = onLane ? Player.LaneSpeed : 0f;
            if (onLane && lane == lastSpeedLane && dt > 0f)
            {
                float change = Mathf.Abs(speed - lastLaneSpeed);
                float limit = t.stairAcceleration * dt * LookoutTargets.SpeedChangeFactor + LookoutTargets.SpeedChangeAllowance;
                if (MaxSpeedChangeByEnd.Length != Model.Length * 2) MaxSpeedChangeByEnd = new float[Model.Length * 2];
                int end = lane * 2 + (OwnU() > Model[lane].Length * 0.5f ? 1 : 0);
                MaxSpeedChangeByEnd[end] = Mathf.Max(MaxSpeedChangeByEnd[end], change);
                if (change > limit)
                {
                    // Exempt only at a hard limit of the lane (the gate, a lane or run end, a junction): never mid-lane.
                    string at = LimitAt(lane, t);
                    if (at == null) Flag(ref StepBreaks, $"speed step on lane {lane + 1}: {lastLaneSpeed:F3} to {speed:F3} m/s in one frame (limit {limit:F3})");
                    else { ExemptSpeedFrames++; if (ExemptLog.Count < LookoutTargets.ViolationsKept) ExemptLog.Add($"f{Frames}: lane {lane + 1} {at}: {lastLaneSpeed:F3} to {speed:F3} m/s"); }
                }
            }
            lastSpeedLane = lane;
            lastLaneSpeed = speed;
        }

        /// <summary>Over any SpeedWindow spent on one lane or one floor: distance covered against the speed in force.</summary>
        void SustainedSpeed(Vector3 feet, bool onLane, float allowedThisFrame, ClimbTuning t)
        {
            float now = Time.time;
            int mode = onLane ? Player.LaneIndex : Ground;
            if (mode != lastLane || !Player.IsSupported) { window.Clear(); modeSince = now; lastLane = mode; allowedSoFar = 0f; }
            allowedSoFar += allowedThisFrame;
            float ownU = onLane ? OwnU() : 0f;
            window.Enqueue(new Sample { time = now, u = ownU, feet = feet, allowed = allowedSoFar });
            while (window.Count > 1 && now - window.Peek().time > LookoutTargets.SpeedWindow) window.Dequeue();
            var oldest = window.Peek();
            float span = now - oldest.time;
            if (span < LookoutTargets.SpeedWindow - Time.deltaTime || now - modeSince < LookoutTargets.SpeedWindow) return;
            // Allowed distance: the sum over the window's frames of the speed in force x dt (so the ramp at a lane end is judged fairly).
            float moved = onLane ? Mathf.Abs(ownU - oldest.u) : Vector3.Distance(feet, oldest.feet);
            float over = moved - ((allowedSoFar - oldest.allowed) * LookoutTargets.SpeedWindowFactor + LookoutTargets.FeetStepSlack);
            if (over > 0f) Flag(ref StepBreaks, $"sustained speed: {moved:F3} m in {span:F2} s, over by {over:F3} m (lane {Player.LaneIndex}) at {feet:F2}");
        }

        void CheckEscape(Vector3 feet, bool onLane, bool inArea, bool supported, ClimbTuning t)
        {
            Vector3 local = Tower.InverseTransformPoint(feet);
            float half = Mathf.Lerp(BaseHalf, TopHalf, Mathf.Clamp01(local.y / CabFloorY)) + LookoutTargets.PlanMargin;
            if (BaseHalf > 0f && (Mathf.Abs(local.x) > half || Mathf.Abs(local.z) > half))
                Flag(ref Escapes, $"escape (plan): feet {local:F2} outside the tower's {half:F2} m half-plan at that height");

            int now = onLane ? Player.LaneIndex : (supported ? LandingAt(feet) : Falling);
            if (onLane) CheckLane(feet);
            else
            {
                checkedLane = None;
                if (!inArea) { StateLost++; Flag(ref Escapes, $"escape (state): climb state lost at {feet:F2}"); }
                bool inPrism = InsideALanePrism(feet, t, out int prismLane);
                if (supported)
                {
                    if (!OverWalkSurface(feet) && !inPrism) Flag(ref Escapes, $"escape (no-surface): nothing to stand on within {LookoutTargets.EscapeDrop} m under {feet:F2}");
                    falling = false;
                }
                else
                {
                    if (!falling) { falling = true; fallFromY = lastFeet.y; }
                    float drop = fallFromY - feet.y;
                    float allowed = inPrism && prismLane == HatchLane ? LookoutTargets.HatchDropMax : LookoutTargets.EscapeDrop;
                    if (drop > allowed) Flag(ref Escapes, $"escape (free-fall): fell {drop:F2} m from y {fallFromY:F2} at {feet:F2}");
                }
                if (InsideASolid(feet)) Flag(ref Escapes, $"escape (in-solid): capsule inside a tower solid at {feet:F2}");
            }

            // Support may only go lane <-> its own foot landing (lane - 1; -1 is the ground) or head landing (lane), and back.
            if (now != Falling)
            {
                bool changed = support != None && (now != support || onLane != wasOnLaneSupport);
                bool passedOn = changed && onLane && wasOnLaneSupport && Linked(support, now);
                if (passedOn)
                {
                    LinkCrossings++;
                    if (LinkCrossingLog.Count < LookoutTargets.LinkCrossingsKept)
                        LinkCrossingLog.Add($"{{\"frame\":{Frames},\"from\":{support + 1},\"to\":{now + 1},\"laneSpeed\":{Player.LaneSpeed.ToString("F3")}}}");
                }
                if (changed && !passedOn && !Adjacent(support, wasOnLaneSupport, now, onLane))
                    Flag(ref Escapes, $"escape (sequence): support went from {Name(support, wasOnLaneSupport)} to {Name(now, onLane)} at {feet:F2}");
                // A join is from the floor or from a fall; passing from one lane to its linked lane is not one.
                if (onLane && !(wasOnLaneSupport && support == now) && !passedOn) BeginJoin(feet);
                support = now;
                wasOnLaneSupport = onLane;
            }
        }

        bool wasOnLaneSupport;

        /// <summary>Lane to the landing at the level of its own foot or head, and back. Lane to lane and landing to landing are not adjacent.</summary>
        bool Adjacent(int from, bool fromLane, int to, bool toLane)
        {
            if (fromLane == toLane) return false;
            int lane = fromLane ? from : to, landing = fromLane ? to : from;
            ModelLane l = Model[lane];
            return landing == LandingAt(l.foot + Vector3.up * capsule.skinWidth) || landing == LandingAt(l.head + Vector3.up * capsule.skinWidth);
        }

        /// <summary>Two lanes that share an end (tower-td-note §12a): the player passes from one to the other with no hand-over.</summary>
        bool Linked(int a, int b)
        {
            return Model[a].footLink == b || Model[a].headLink == b;
        }

        static string Name(int id, bool lane) => lane ? $"lane {id + 1}" : (id == Ground ? "the ground" : $"landing {id + 1}");

        /// <summary>The landing at the feet's height: the lane whose head is level with them (Ground at the first foot's level, Falling if neither).</summary>
        int LandingAt(Vector3 feet)
        {
            float band = capsule.stepOffset;
            for (int i = Model.Length - 1; i >= 0; i--)
                if (Mathf.Abs(feet.y - Model[i].head.y) < band) return i;
            return feet.y < Model[0].foot.y + band ? Ground : Falling;
        }

        bool OverWalkSurface(Vector3 feet)
        {
            float lift = capsule.stepOffset;
            if (!Physics.Raycast(feet + Vector3.up * lift, Vector3.down, out RaycastHit hit, lift + LookoutTargets.EscapeDrop,
                    WorldLayers.SightMask, QueryTriggerInteraction.Ignore)) return false;
            if (!hit.collider.transform.IsChildOf(Tower)) return false;
            foreach (var word in WalkSurfaces)
                if (hit.collider.name.Contains(word)) return true;
            return false;
        }

        bool InsideALanePrism(Vector3 feet, ClimbTuning t, out int index)
        {
            for (index = 0; index < Model.Length; index++)
            {
                ModelLane lane = Model[index];
                Locate(feet, lane, out float u, out float lateral, out float above);
                if (u > 0f && u < lane.Length && Mathf.Abs(lateral) <= lane.width * 0.5f && above >= -AuthoredCatchBand) return true;
            }
            index = None;
            return false;
        }

        /// <summary>The capsule, shrunk by its skin and lifted off the floor by its step height, against solid tower colliders.</summary>
        bool InsideASolid(Vector3 feet)
        {
            float r = capsule.radius - capsule.skinWidth;
            Vector3 bottom = feet + Vector3.up * (capsule.stepOffset + r), top = feet + Vector3.up * (capsule.height - r);
            if (bottom.y > top.y) bottom = top;
            int hits = Physics.OverlapCapsuleNonAlloc(bottom, top, r, buffer, WorldLayers.SightMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < hits; i++)
                if (buffer[i] != capsule && buffer[i].transform.IsChildOf(Tower)) return true;
            return false;
        }

        void Flag(ref int counter, string message)
        {
            counter++;
            if (Violations.Count < LookoutTargets.ViolationsKept) Violations.Add($"f{Frames}: {message}");
        }

        public string Summary() =>
            $"frames={Frames} laneFrames={LaneFrames} escapes={Escapes} stateLost={StateLost} launches={Launches} stepBreaks={StepBreaks} cameraBreaks={CameraBreaks} " +
            $"exemptSpeedFrames={ExemptSpeedFrames} linkCrossings={LinkCrossings} areaCrossings={AreaCrossings} handOvers={HandOvers} maxHandOverSpeedChange={MaxHandOverSpeedChange:F3} radiusOffTower={RadiusOffTower} maxFeetStepOver={MaxFeetStepOver:F4} maxLaneLateral={MaxLaneLateral:F4} maxUp={MaxUpwardSpeed:F2} minRadius={MinRadius:F2} catches={Player.LaneCatches}";
    }
}
