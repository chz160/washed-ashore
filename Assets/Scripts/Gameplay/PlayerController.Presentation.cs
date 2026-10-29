using UnityEngine;

namespace WashedAshore.Gameplay
{
    /// <summary>
    /// Rendering of the sim (#46 P2): the root transform holds the sim pose, so everything that reads the player's position reads the sim.
    /// The camera pivot and the body roots get an offset instead: the view shows the point <c>alpha</c> of the way between the last two
    /// tick poses, where alpha is the fraction of the next tick already elapsed. The view therefore runs up to one tick behind the sim,
    /// the usual cost of interpolation. <see cref="PresentView"/> is the one method that writes the camera pivot (with the override calls
    /// in PlayerController.View.cs, which write the world pose of the firefinder's view).
    /// </summary>
    public partial class PlayerController
    {
        Vector3 prevPos, currPos;
        float prevEye, currEye, overrideEye, renderAlpha = 1f;
        bool viewSetUp;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /// <summary>Test-only mutation (#46 KF-T2b): no interpolation, the view shows the latest tick pose. Reset in TearDown.</summary>
        public static bool TestInterpolationOff;
#endif

        /// <summary>
        /// World-space offset from the root (the sim pose) to the pose the view shows: the camera pivot and the body roots add it.
        /// Zero when the view is on the latest tick, at a teleport and while the view pose is overridden.
        /// </summary>
        public Vector3 RenderOffset
        {
            get
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                if (TestInterpolationOff) return Vector3.zero;
#endif
                return viewPoseOverridden ? Vector3.zero : Vector3.LerpUnclamped(prevPos, currPos, renderAlpha) - currPos;
            }
        }

        /// <summary>The eye height above the feet the sim has now: the override while one is held, else the stance's.</summary>
        float SimEye => eyeHeightOverridden ? overrideEye : StanceEye;

        float RenderEye
        {
            get
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                if (TestInterpolationOff) return currEye;
#endif
                // Never above the sim eye: on a stance drop the drawn eye must not sit over the current capsule top (td-46 adr/46-combined-c2-rulings/final).
                return Mathf.Min(Mathf.LerpUnclamped(prevEye, currEye, renderAlpha), currEye);
            }
        }

        /// <summary>
        /// After a tick: the pose the tick started from becomes the previous pose, the new sim pose the current one. The start pose, not the
        /// old current pose: they are the same unless something moved the root between ticks (a test or a tool writing the transform), and
        /// then there must be no blend across that jump.
        /// </summary>
        void PushInterpolation(Vector3 startPose)
        {
            prevPos = startPose;
            currPos = transform.position;
            prevEye = currEye;
            currEye = SimEye;
            renderAlpha = 1f;
        }

        /// <summary>A teleport (placement, bounds snap, restore): the view jumps with the body, no blend across the jump.</summary>
        void ResetInterpolation()
        {
            prevPos = currPos = transform.position;
            prevEye = currEye = SimEye;
            renderAlpha = 1f;
        }

        /// <summary>Writes the camera pivot: the interpolated eye height plus the render offset in the pivot's parent space, and the look pitch.</summary>
        void PresentView()
        {
            if (cameraPivot == null || viewPoseOverridden) return;
            Vector3 offsetLocal = Quaternion.Inverse(transform.rotation) * RenderOffset;
            cameraPivot.localPosition = new Vector3(authoredPivotPosition.x, RenderEye, authoredPivotPosition.z) + offsetLocal;
            cameraPivot.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        }
    }
}
