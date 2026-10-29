using UnityEngine;

namespace WashedAshore.Gameplay
{
    /// <summary>
    /// Everything the player's tick reads or writes (#46 P3): <see cref="PlayerController.CaptureState"/> and
    /// <see cref="PlayerController.RestoreState"/> move it. Restoring a state and running the same commands again gives the same result,
    /// which is the basis for reconciliation. Not in it: latched gather edges (input, not state), the interpolation poses, log-only fields,
    /// and the world (hinges, water, terrain).
    /// </summary>
    public struct PlayerSimState
    {
        // Clock
        public int tick;
        // Pose
        public Vector3 position;
        public float yaw, pitch;
        // Motion
        public Vector3 horizontalVelocity;
        public float verticalVelocity;
        public bool grounded;
        // Zone outputs
        public float walkSpeedCap, groundSnapDistance;
        // Capsule
        public float capsuleRadius, capsuleHeight, stepOffset, slopeLimit;
        public Vector3 capsuleCenter;
        // View and control
        public bool viewPoseOverridden, eyeHeightOverridden, controlSuspended, resumeTickPending;
        public float overrideEye;
        // Water
        public WaterMode waterMode;
        public float strokeTime;
        public double simWaterTime;
        // Stance
        public Stance stance, stancePrevious, trFromStance;
        public bool hasStancePending;
        public Stance stancePending;
        public float standingHeight, trFrom, trTo, trElapsed, trDuration, trPitchFrom, trPitchTo, stanceEye, sleepDim;
        public bool walkCapsuleRestricted, stanceTransitioning, stanceFalling, stanceLie, sleepLock;
        // Tower and hatch
        public TowerZoneState towerZone;
        public bool hatchDuck;
        // Presentation (not read by the sim; kept so remote copies can animate)
        public Vector3 simVelocity;
    }
}
