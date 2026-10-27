using System.Collections.Generic;
using UnityEngine;
using WashedAshore.Gameplay;

namespace WashedAshore.Tests.PlayMode.Lookout
{
    /// <summary>
    /// Per-frame detector for the lookout tests. Runs after everything else each frame and records every violation
    /// instead of asserting on the first.
    /// LAUNCH   upward feet speed above take-off speed x LaunchFactor, or an eye step above the teleport bound.
    /// CAMERA   FOV or roll changed while <see cref="ExpectAuthoredCamera"/> is set.
    /// </summary>
    [DefaultExecutionOrder(20000)]
    public class LookoutWatch : MonoBehaviour
    {
        public PlayerController Player;
        /// <summary>Clear while the firefinder legitimately holds the camera.</summary>
        public bool ExpectAuthoredCamera = true;

        public readonly List<string> Violations = new List<string>();
        public int Frames, Launches, CameraBreaks;
        public float MaxUpwardSpeed, MinRadius = float.MaxValue;

        CharacterController capsule;
        Vector3 lastFeet, lastEye;
        bool hasLast;

        void Start() => capsule = Player.GetComponent<CharacterController>();

        /// <summary>Call after the script moves the player by hand (the start pose), so that step isn't judged.</summary>
        public void Forget() => hasLast = false;

        void LateUpdate()
        {
            if (Player == null) return;
            float dt = Time.deltaTime;
            Vector3 feet = Player.transform.position, eye = Player.CameraPivot.position;
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

        void Flag(ref int counter, string message)
        {
            counter++;
            if (Violations.Count < LookoutTargets.ViolationsKept) Violations.Add($"f{Frames}: {message}");
        }

        public string Summary() =>
            $"frames={Frames} launches={Launches} cameraBreaks={CameraBreaks} maxUp={MaxUpwardSpeed:F2} minRadius={MinRadius:F2}";
    }
}
