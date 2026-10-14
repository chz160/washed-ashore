namespace WashedAshore.Tests.PlayMode.Lookout
{
    /// <summary>
    /// Pass targets for the lookout tests that are not tuning values. Every number names the row it comes from:
    /// "spec" is _bmad-output/poc/tower-design-spec.md (r2), "plan" is _bmad-output/poc/tower-qa-plan.md, "td" is
    /// _bmad-output/poc/tower-td-note.md §6. All were fixed before any result existed; none is a measured value.
    /// Tuning values are read from the three tuning assets. Changing a number here after a result needs t-qa's
    /// agreement and is disclosed in the QA report.
    /// </summary>
    public static class LookoutTargets
    {
        // ---- names guaranteed by the build and placement scripts (t-tools TowerBuilder, t-level placement) ----
        public const string WorldScene = "World";
        public const string TowerRoot = "LookoutTower";
        public const string BaseSpawn = "TowerBaseSpawn";
        /// <summary>The build manifest beside the FBX (t-tools: TowerBuilder.ManifestPath), relative to the project folder.</summary>
        public const string ManifestPath = "Assets/World/Lookout/MC39_Lookout.manifest.json";
        /// <summary>td §6 condition 10: invisible walls are CLIP_ colliders on WorldBounds.</summary>
        public const string ClipPrefix = "CLIP_";

        // ---- spec §3.2: the exact prompt strings (T-I1b compares against these, not against the asset) ----
        public const string PromptOpenHatch = "E – Open hatch";
        public const string PromptCloseHatch = "E – Close hatch";
        public const string PromptOpenWindow = "E – Open window";
        public const string PromptCloseWindow = "E – Close window";
        public const string PromptUseFirefinder = "E – Use firefinder";
        public const string PromptLeaveFirefinder = "E – Leave firefinder";
        public const string KeyKeyboard = "E", KeyXbox = "A", KeyPlayStation = "Cross", KeyOtherPad = "South";

        // ---- C1 ----
        /// <summary>Spec C1, spec T-C1a, plan C1: base to cab, seconds.</summary>
        public const float ClimbMinSeconds = 60f, ClimbMaxSeconds = 120f;
        /// <summary>Spec T-C1a: within this fraction of the §2.4 formula with built lengths.</summary>
        public const float ClimbFormulaTolerance = 0.10f;
        /// <summary>Spec T-C1a: every landing turn made by the script is at least this, degrees.</summary>
        public const float LandingTurnMinDegrees = 60f;
        /// <summary>Spec T-C1c, R3: drift while released on a flight, m, over this hold, s.</summary>
        public const float FlightHoldDrift = 0.01f, FlightHoldSeconds = 10f;
        /// <summary>Spec R6, T-C1d; plan C1: feet step per frame at most speed in force x dt + this, m.</summary>
        public const float FeetStepSlack = 0.02f;
        /// <summary>Plan C1 (t-qa detector review item 4): over any window of this length, s, distance at most speed in force x this factor x window (+ FeetStepSlack).</summary>
        public const float SpeedWindow = 1f, SpeedWindowFactor = 1.05f;
        /// <summary>Plan C1 (t-qa, agreed with the level-run speed ruling): on a lane, along-lane speed changes between frames by at most stairAcceleration x dt x this + SpeedChangeAllowance, m/s.</summary>
        public const float SpeedChangeFactor = 1.05f, SpeedChangeAllowance = 0.01f;
        /// <summary>Plan C1 (t-qa): the speed-change assertion exempts only frames where the lane parameter is within this of a hard limit (the shut-hatch gate, a lane or run end, a junction), m.</summary>
        public const float AtLimitWithin = 0.001f;
        /// <summary>Plan C1, C2: 3D eye step at most this x sprint speed x dt (the teleport bound).</summary>
        public const float TeleportFactor = 1.5f;
        /// <summary>Spec T-C1e, R10: climb times at the proof frame rates agree within this fraction.</summary>
        public const float FrameRateTolerance = 0.02f;
        /// <summary>Spec T-C1b, T-C1f; td condition 2: authored radius back within this long, s, of being this far, m, clear of the first foot.</summary>
        public const float RadiusRestoreSeconds = 1f, RadiusRestoreClear = 1f;
        /// <summary>Plan (gating suites): frame time of the suite. td condition 9, spec T-C1e, T-I2e: the proof frame times.</summary>
        public const float SuiteDeltaTime = 1f / 60f;
        public static readonly float[] ProofDeltaTimes = { 1f / 30f, 1f / 120f, 1f / 240f };
        /// <summary>Plan (t-qa detector review item 6): scripted turns are made at this yaw rate, deg/s, so their time doesn't depend on the frame rate. A script constant, not a pass target.</summary>
        public const float ScriptTurnDegPerSec = 180f;
        /// <summary>Spec R7, T-C1d: camera roll is 0; this is the float tolerance on reading it back, degrees.</summary>
        public const float CameraRollTolerance = 0.01f;

        /// <summary>Spec T-C1i: at a heading-change junction, W on the old heading moves the player under this, m, with no game-made rotation, over this long, s.</summary>
        public const float JunctionHoldDrift = 0.01f, JunctionHoldSeconds = 2f;
        /// <summary>Spec T-C1i: stop this long, s, on the exit lane and on one level run per landing.</summary>
        public const float RunStopSeconds = 5f;

        /// <summary>
        /// t-td / t-qa on margins (lane-end ruling): every clearance figure is reported with its margin over the bar; a figure
        /// passes at the bar minus FloatTolerance; anything with less than MarginTarget over the bar is listed by name.
        /// </summary>
        public const float ClearanceBar = 0.30f, MarginTarget = 0.010f, FloatTolerance = 0.0005f;
        /// <summary>t-qa (landing 6 accepted at +7.4 mm): a margin under this is a visible warning though it passes; measured and manifest margins agree within MarginAgreement.</summary>
        public const float MarginWarn = 0.005f, MarginAgreement = 0.001f;

        /// <summary>
        /// ruling/lookout-tower-upper-landing-step-on-amend3 and the lane-end ruling: a junction that stops the player is at
        /// least this far past the lever's angle, degrees; a run-out stepping away from the next flight turns at most
        /// AwayMaxDegrees; the built lanes' turn agrees with the manifest's within TurnAgreementDegrees.
        /// </summary>
        public const float NoTwitchDegrees = 10f, AwayMaxDegrees = 30f, TurnAgreementDegrees = 0.1f;

        // ---- C2 ----
        /// <summary>Spec T-C2a: seconds per flight, landing and mouth per input pattern.</summary>
        public const float StressSecondsPerPattern = 20f;
        /// <summary>Spec T-C2a, plan C2 (final form agreed by t-designer and t-qa): stuck = under this much progress, m, in this long, s, with W held toward a valid exit.</summary>
        public const float StuckProgress = 0.1f, StuckSeconds = 5f;
        /// <summary>Plan C2: escape = more than this below the supporting surface, m; also the unsupported fall limit off a lane.</summary>
        public const float EscapeDrop = 0.5f;
        /// <summary>Spec §4 rule 5: the open-hatch drop onto the top lane is at most this, m (the one free-fall exception).</summary>
        public const float HatchDropMax = 1.3f;
        /// <summary>Plan C2 (t-qa detector review item 1): on a lane the centre is within the authored laneLateralSlack + this, m; and the feet within LaneHeightMargin of the nosing line.</summary>
        public const float LaneLateralMargin = 0.01f, LaneHeightMargin = 0.05f;
        /// <summary>Plan C2 (t-qa detector review item 3): feet inside the tower's tapered plan; this is the allowance for the leg section and the capsule at the first foot, m.</summary>
        public const float PlanMargin = 0.05f;
        /// <summary>Plan C2, spec T-C2a (frozen with t-qa): outside the climb area, launch = upward speed above take-off speed x this.</summary>
        public const float LaunchFactor = 1.1f;
        /// <summary>Plan C2, spec T-C2a: inside the climb area, launch = upward feet speed above this, m/s, at any frame.</summary>
        public const float AreaMaxUpwardSpeed = 2f;
        /// <summary>Plan C2 (t-qa freeze note): during the join grace the sideways offset may never grow; this is the float allowance, m.</summary>
        public const float JoinGrowthAllowance = 0.001f;
        /// <summary>Plan C2 (t-qa): a join counts as "from above" (the open hatch), and its height may close over time, only when it begins more than this above the line, m.</summary>
        public const float JoinFromAboveHeight = 0.05f;
        /// <summary>Plan C2 (t-qa): the shifted-lane twin moves every lane this far across the flights, m.</summary>
        public const float TwinLaneShift = 0.15f;
        /// <summary>Plan C2: joins from above (a fall caught by a lane) allowed in C1 and C2 runs. The deliberate step into the open hatch is tested apart.</summary>
        public const int JoinsFromAboveAllowed = 0;
        /// <summary>Spec T-C2b: a player on the ground never gets this far above it, m; seconds per probe.</summary>
        public const float GroundPlayerMaxRise = 1.3f, GroundProbeSeconds = 10f;
        /// <summary>How many violation lines a run keeps for its failure message (all are counted).</summary>
        public const int ViolationsKept = 20;
        /// <summary>How many junction crossings a run keeps in its log (a full climb and descent crosses about a dozen). A script constant.</summary>
        public const int LinkCrossingsKept = 200;

        // ---- C3 ----
        /// <summary>Spec T-C3a: no stall longer than this, s; eye above every sill by at least this, m.</summary>
        public const float CabStallSeconds = 0.5f, SillClearance = 0.5f;

        // ---- I1 ----
        /// <summary>Plan I1: prompt shown at this distance and hidden at this, m (PlayMode). Spec T-I1a: the EditMode pair.</summary>
        public const float PromptShownAt = 2.4f, PromptHiddenAt = 2.6f, RayInside = 2.49f, RayOutside = 2.51f;

        // ---- I2 ----
        /// <summary>Plan I2: stand on the closed leaf this long, s, with at most this drift, m. Spec T-I2a: no upward speed above this, m/s.</summary>
        public const float HatchStandSeconds = 10f, HatchStandDrift = 0.02f, HatchMaxUpwardSpeed = 2f;
        /// <summary>Spec T-I2b: swing duration bounds, s; peak angular speed, deg/s; end-angle tolerance, degrees.</summary>
        public const float HatchMinSeconds = 1f, HatchMaxSeconds = 2.5f, HatchPeakDegPerSec = 150f, HingeAngleTolerance = 0.5f;
        /// <summary>Plan I2: no state change without input over this long, s.</summary>
        public const float HatchIdleSeconds = 30f;
        /// <summary>Spec T-I2e: eye height repeats within this, m; stopped mid-duck this long, s.</summary>
        public const float DuckRepeatTolerance = 0.005f, DuckStopSeconds = 5f;

        // ---- I3 ----
        /// <summary>Spec T-I3a: in use within this, s; turn this far, degrees; bearing and pointer within this, degrees; FOV within this, degrees.</summary>
        public const float FirefinderEnterSeconds = 0.5f, FirefinderTurnDegrees = 90f, BearingTolerance = 0.5f, FovTolerance = 0.5f;
        /// <summary>Plan I3: player position fixed within this, m. td condition 7: pivot pose and FOV restored within this.</summary>
        public const float FirefinderPlayerDrift = 0.01f, ViewRestoreTolerance = 1e-5f;

        // ---- I4 ----
        /// <summary>Spec T-I4a: sash swing time bounds, s. Plan I4: hinged sashes, one per side.</summary>
        public const float SashMinSeconds = 0.6f, SashMaxSeconds = 2f;
        public const int HingedSashes = 4;
    }
}
