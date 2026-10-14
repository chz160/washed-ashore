using System;
using UnityEngine;

namespace WashedAshore.Gameplay
{
    /// <summary>
    /// The one camera seam (tower-td-note §6 condition 7) and the control suspend pair (§3.6). Anything that moves
    /// the eye (a duck under a hatch, a fixed sight) goes through here, and clearing the override puts the camera
    /// pivot's authored local pose and the camera's authored FOV back exactly. Nothing else writes the pivot.
    /// </summary>
    public partial class PlayerController
    {
        Camera viewCamera;
        Vector3 authoredPivotPosition;
        float authoredFov;
        bool viewPoseOverridden, eyeHeightOverridden, controlSuspended;

        /// <summary>Raised when the player was moved by Respawn or a boundary snap: whoever holds the view or the controls lets go.</summary>
        public event Action Teleported;

        /// <summary>Eye height above the feet as authored on the Player (the camera pivot's local Y).</summary>
        public float AuthoredEyeHeight => authoredPivotPosition.y;
        /// <summary>The camera's authored vertical FOV, degrees.</summary>
        public float AuthoredFov => authoredFov;
        public Transform CameraPivot => cameraPivot;
        public Camera ViewCamera => viewCamera;
        public bool ViewOverridden => viewPoseOverridden || eyeHeightOverridden;
        public bool ControlSuspended => controlSuspended;
        /// <summary>Look pitch, degrees (positive looks down), and yaw.</summary>
        public float Pitch => pitch;
        public float Yaw => transform.eulerAngles.y;

        void SetupView()
        {
            if (cameraPivot == null) return;
            viewCamera = cameraPivot.GetComponent<Camera>();
            authoredPivotPosition = cameraPivot.localPosition;
            authoredFov = viewCamera != null ? viewCamera.fieldOfView : 0f;
        }

        /// <summary>Eye at <paramref name="eyeHeight"/> above the feet. Look and FOV stay with the player.</summary>
        public void OverrideEyeHeight(float eyeHeight)
        {
            if (cameraPivot == null || viewPoseOverridden) return;
            eyeHeightOverridden = true;
            cameraPivot.localPosition = new Vector3(authoredPivotPosition.x, eyeHeight, authoredPivotPosition.z);
        }

        /// <summary>Camera at a world pose with a FOV. Look input no longer turns the pivot until the override is cleared.</summary>
        public void OverrideView(Vector3 worldPosition, Quaternion worldRotation, float fov)
        {
            if (cameraPivot == null) return;
            viewPoseOverridden = true;
            cameraPivot.SetPositionAndRotation(worldPosition, worldRotation);
            if (viewCamera != null) viewCamera.fieldOfView = fov;
        }

        /// <summary>Puts the pivot's authored local pose (at the current look pitch) and the authored FOV back.</summary>
        public void ClearViewOverride()
        {
            if (cameraPivot == null) return;
            viewPoseOverridden = eyeHeightOverridden = false;
            cameraPivot.localPosition = authoredPivotPosition;
            cameraPivot.localRotation = Quaternion.Euler(pitch, 0f, 0f);
            if (viewCamera != null) viewCamera.fieldOfView = authoredFov;
        }

        /// <summary>Move, sprint, jump and look input off; horizontal velocity zeroed. Gravity and collision stay on.</summary>
        public void SuspendControl()
        {
            controlSuspended = true;
            horizontalVelocity = Vector3.zero;
        }

        public void ResumeControl() => controlSuspended = false;

        /// <summary>Sets where the player looks: body yaw and view pitch (degrees; positive pitch looks down).</summary>
        public void SetLook(float yaw, float lookPitch)
        {
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            pitch = Mathf.Clamp(lookPitch, minPitch, maxPitch);
            if (cameraPivot != null && !viewPoseOverridden) cameraPivot.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        }
    }
}
