using UnityEngine;

namespace WashedAshore.Gameplay
{
    /// <summary>Capture and restore of the sim state (#46 P3, spec section 5).</summary>
    public partial class PlayerController
    {
        public PlayerSimState CaptureState()
        {
            return new PlayerSimState
            {
                tick = tick,
                position = transform.position,
                yaw = transform.eulerAngles.y,
                pitch = pitch,
                horizontalVelocity = horizontalVelocity,
                verticalVelocity = verticalVelocity,
                grounded = grounded,
                walkSpeedCap = WalkSpeedCap,
                groundSnapDistance = GroundSnapDistance,
                capsuleRadius = controller.radius,
                capsuleHeight = controller.height,
                capsuleCenter = controller.center,
                stepOffset = controller.stepOffset,
                slopeLimit = controller.slopeLimit,
                viewPoseOverridden = viewPoseOverridden,
                eyeHeightOverridden = eyeHeightOverridden,
                controlSuspended = controlSuspended,
                resumeTickPending = resumeTickPending,
                overrideEye = overrideEye,
                waterMode = waterMode,
                strokeTime = strokeTime,
                simWaterTime = simWaterTime,
                stance = stance,
                stancePrevious = stancePrevious,
                trFromStance = trFromStance,
                hasStancePending = stancePending.HasValue,
                stancePending = stancePending ?? Stance.Stand,
                standingHeight = standingHeight,
                walkCapsuleRestricted = walkCapsuleRestricted,
                stanceTransitioning = stanceTransitioning,
                stanceFalling = stanceFalling,
                stanceLie = stanceLie,
                sleepLock = sleepLock,
                trFrom = trFrom,
                trTo = trTo,
                trElapsed = trElapsed,
                trDuration = trDuration,
                trPitchFrom = trPitchFrom,
                trPitchTo = trPitchTo,
                stanceEye = stanceEye,
                sleepDim = sleepDim,
                towerZone = TowerZone,
                hatchDuck = HatchDuck,
                simVelocity = simVelocity,
            };
        }

        /// <summary>
        /// Puts the player back in <paramref name="s"/>: the capsule shape first, then position and yaw through the same disable, set, enable as a
        /// placement (the controller overrides transform writes while enabled), then a transform sync (auto sync is off), then the fields. No
        /// Teleported event and no stance events: a restore is a rewind, not a gameplay move. The interactor follows the stance, as ResetStance does.
        /// </summary>
        public void RestoreState(in PlayerSimState s)
        {
            controller.radius = s.capsuleRadius;
            controller.height = s.capsuleHeight;
            controller.center = s.capsuleCenter;
            controller.stepOffset = s.stepOffset;
            controller.slopeLimit = s.slopeLimit;

            controller.enabled = false;
            transform.SetPositionAndRotation(s.position, Quaternion.Euler(0f, s.yaw, 0f));
            controller.enabled = true;
            Physics.SyncTransforms();

            tick = s.tick;
            pitch = s.pitch;
            horizontalVelocity = s.horizontalVelocity;
            verticalVelocity = s.verticalVelocity;
            grounded = s.grounded;
            WalkSpeedCap = s.walkSpeedCap;
            GroundSnapDistance = s.groundSnapDistance;
            viewPoseOverridden = s.viewPoseOverridden;
            eyeHeightOverridden = s.eyeHeightOverridden;
            controlSuspended = s.controlSuspended;
            resumeTickPending = s.resumeTickPending;
            overrideEye = s.overrideEye;
            waterMode = s.waterMode;
            strokeTime = s.strokeTime;
            simWaterTime = s.simWaterTime;
            stance = s.stance;
            stancePrevious = s.stancePrevious;
            trFromStance = s.trFromStance;
            stancePending = s.hasStancePending ? s.stancePending : (Stance?)null;
            standingHeight = s.standingHeight;
            walkCapsuleRestricted = s.walkCapsuleRestricted;
            stanceTransitioning = s.stanceTransitioning;
            stanceFalling = s.stanceFalling;
            stanceLie = s.stanceLie;
            sleepLock = s.sleepLock;
            trFrom = s.trFrom;
            trTo = s.trTo;
            trElapsed = s.trElapsed;
            trDuration = s.trDuration;
            trPitchFrom = s.trPitchFrom;
            trPitchTo = s.trPitchTo;
            stanceEye = s.stanceEye;
            sleepDim = s.sleepDim;
            TowerZone = s.towerZone;
            HatchDuck = s.hatchDuck;
            simVelocity = s.simVelocity;
            if (interactor != null) interactor.enabled = stance != Stance.Sit && stance != Stance.Sleep;
            teleportedThisTick = true;
            ResetInterpolation();
            PresentView();
        }
    }
}
