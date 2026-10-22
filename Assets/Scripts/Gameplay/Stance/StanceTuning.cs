using System;
using UnityEngine;

namespace WashedAshore.Gameplay
{
    /// <summary>
    /// Stance numbers of record: _bmad-output/design/player-stances.md §3 to §6. The standing eye and capsule are the
    /// Player's authored ones and are not here. Every value is tuneable in the Inspector.
    /// </summary>
    [Serializable]
    public class StanceTuning
    {
        [Header("Eye height above the feet (m)")]
        public float crouchEye = 1.20f;
        public float squatEye = 1.00f;
        public float sitEye = 0.80f;
        public float sleepEye = 0.20f;
        [Tooltip("Capsule top minus eye, m. Settled capsule = eye + this (§2).")]
        public float eyeToCapsuleTop = 0.15f;

        [Header("Speed (m/s)")]
        public float crouchSpeed = 1.75f;

        [Header("Enter / exit seconds")]
        public float crouchEnter = 0.25f;
        public float crouchExit = 0.25f;
        public float squatEnter = 0.40f;
        public float squatExit = 0.40f;
        public float sitEnter = 1.0f;
        public float sitExit = 0.8f;
        public float sleepEnter = 1.5f;
        public float sleepExit = 1.5f;
        [Tooltip("Lie-down: seconds to reach the sit-height eye before it carries on down to the lying eye.")]
        public float sleepFirstSegment = 0.6f;

        [Header("Rules")]
        [Tooltip("Horizontal speed at or under this counts as standing still (sit and sleep).")]
        public float stillSpeed = 0.10f;
        [Tooltip("Move input magnitude squared over this wakes or stands you.")]
        public float moveInputSqr = 0.01f;
        public float sitMaxSlope = 25f;
        public float sleepMaxSlope = 15f;
        public float sleepMaxGroundVariation = 0.5f;
        [Tooltip("Sit box: width, length ahead of the axis; height band above the ground.")]
        public float sitWidth = 0.6f;
        public float sitLength = 0.9f;
        public Vector2 sitBand = new Vector2(0.15f, 0.6f);
        public float sleepWidth = 0.7f;
        public float sleepLength = 1.9f;
        public Vector2 sleepBand = new Vector2(0.15f, 0.5f);
        [Tooltip("Anything within this of the lying eye blocks sleep (R8).")]
        public float sleepEyeClearance = 0.20f;

        [Header("Sleep view")]
        public float sleepPitch = -60f;
        public float dimSeconds = 2.0f;
        public float wakeUndimSeconds = 0.5f;
        [Tooltip("Post-exposure at full dim, EV (about 15% brightness).")]
        public float dimExposure = -2.7f;

        /// <summary>H9 retune ranges (player-stances.md section 3a). The eye ends are pending proof and may shrink; times are 0.5x to 1.5x of record, dim 10 to 30 percent.</summary>
        public void Clamp()
        {
            crouchEye = Mathf.Clamp(crouchEye, 1.10f, 1.30f);
            squatEye = Mathf.Clamp(squatEye, 0.90f, 1.10f);
            sitEye = Mathf.Clamp(sitEye, 0.75f, 0.90f);
            sleepEye = Mathf.Clamp(sleepEye, 0.20f, 0.25f);
            sleepPitch = Mathf.Clamp(sleepPitch, -85f, -45f);
            crouchSpeed = Mathf.Clamp(crouchSpeed, 1.25f, 2.50f);
            crouchEnter = Mathf.Clamp(crouchEnter, 0.125f, 0.375f); crouchExit = Mathf.Clamp(crouchExit, 0.125f, 0.375f);
            squatEnter = Mathf.Clamp(squatEnter, 0.2f, 0.6f); squatExit = Mathf.Clamp(squatExit, 0.2f, 0.6f);
            sitEnter = Mathf.Clamp(sitEnter, 0.5f, 1.5f); sitExit = Mathf.Clamp(sitExit, 0.4f, 1.2f);
            sleepEnter = Mathf.Clamp(sleepEnter, 0.75f, 2.25f); sleepExit = Mathf.Clamp(sleepExit, 0.75f, 2.25f);
            dimExposure = Mathf.Clamp(dimExposure, -3.5f, -1.7f);
            crouchEye = Mathf.Max(crouchEye, squatEye + 0.10f);
            squatEye = Mathf.Max(squatEye, sitEye + 0.10f);
        }

        public float Enter(Stance s)
        {
            switch (s)
            {
                case Stance.Crouch: return crouchEnter;
                case Stance.Squat: return squatEnter;
                case Stance.Sit: return sitEnter;
                case Stance.Sleep: return sleepEnter;
            }
            return 0f;
        }

        public float Exit(Stance s)
        {
            switch (s)
            {
                case Stance.Crouch: return crouchExit;
                case Stance.Squat: return squatExit;
                case Stance.Sit: return sitExit;
                case Stance.Sleep: return sleepExit;
            }
            return 0f;
        }

        public float Eye(Stance s, float standingEye)
        {
            switch (s)
            {
                case Stance.Crouch: return crouchEye;
                case Stance.Squat: return squatEye;
                case Stance.Sit: return sitEye;
                case Stance.Sleep: return sleepEye;
            }
            return standingEye;
        }
    }
}
