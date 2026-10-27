namespace WashedAshore.Tests.PlayMode.Lookout
{
    /// <summary>
    /// Pass targets for the lookout tests that are not tuning values. All were fixed before any result existed; none is a
    /// measured value. Tuning values are read from the tuning assets. Changing a number here after a result needs QA's
    /// agreement and is disclosed in the QA report.
    /// </summary>
    public static class LookoutTargets
    {
        // ---- names guaranteed by the build and placement scripts ----
        public const string WorldScene = "World";
        public const string TowerRoot = "LookoutTower";
        /// <summary>The build manifest beside the FBX (TowerBuilder.ManifestPath), relative to the project folder.</summary>
        public const string ManifestPath = "Assets/World/Lookout/MC39_Lookout.manifest.json";

        // ---- spec §3.2: the exact prompt strings (T-I1b compares against these, not against the asset) ----
        public const string PromptOpenHatch = "E – Open hatch";
        public const string PromptCloseHatch = "E – Close hatch";
        public const string PromptOpenWindow = "E – Open window";
        public const string PromptCloseWindow = "E – Close window";
        public const string PromptUseFirefinder = "E – Use firefinder";
        public const string PromptLeaveFirefinder = "E – Leave firefinder";
        public const string KeyKeyboard = "E", KeyXbox = "A", KeyPlayStation = "Cross", KeyOtherPad = "South";

        /// <summary>Frame time of the suite.</summary>
        public const float SuiteDeltaTime = 1f / 60f;
        /// <summary>Scripted turns are made at this yaw rate, deg/s, so their time doesn't depend on the frame rate. A script constant, not a pass target.</summary>
        public const float ScriptTurnDegPerSec = 180f;
        /// <summary>Camera roll is 0; this is the float tolerance on reading it back, degrees.</summary>
        public const float CameraRollTolerance = 0.01f;
        /// <summary>3D eye step at most this x sprint speed x dt (the teleport bound).</summary>
        public const float TeleportFactor = 1.5f;
        /// <summary>Launch = upward speed above take-off speed x this.</summary>
        public const float LaunchFactor = 1.1f;
        /// <summary>How many violation lines a run keeps for its failure message (all are counted).</summary>
        public const int ViolationsKept = 20;

        // ---- C3 ----
        /// <summary>Eye above every sill by at least this, m.</summary>
        public const float SillClearance = 0.5f;

        // ---- I1 ----
        /// <summary>Prompt shown at this distance and hidden at this, m (PlayMode).</summary>
        public const float PromptShownAt = 2.4f, PromptHiddenAt = 2.6f;

        // ---- I2 ----
        /// <summary>Swing duration bound, s; end-angle tolerance, degrees.</summary>
        public const float HatchMaxSeconds = 2.5f, HingeAngleTolerance = 0.5f;

        // ---- I3 ----
        /// <summary>In use within this, s; turn this far, degrees; bearing and pointer within this, degrees; FOV within this, degrees.</summary>
        public const float FirefinderEnterSeconds = 0.5f, FirefinderTurnDegrees = 90f, BearingTolerance = 0.5f, FovTolerance = 0.5f;
        /// <summary>Player position fixed within this, m; pivot pose and FOV restored within this.</summary>
        public const float FirefinderPlayerDrift = 0.01f, ViewRestoreTolerance = 1e-5f;

        // ---- I4 ----
        /// <summary>Sash swing time bounds, s. Hinged sashes, one per side.</summary>
        public const float SashMinSeconds = 0.6f, SashMaxSeconds = 2f;
        public const int HingedSashes = 4;
    }
}
