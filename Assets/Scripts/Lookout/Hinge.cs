using UnityEngine;

namespace WashedAshore.Lookout
{
    /// <summary>
    /// A hinged leaf (hatch, sash): explicit state plus seconds in that state, and one apply method that poses the
    /// transform (tower-td-note §6 condition 17). Closed is the transform's authored rotation; the angle turns
    /// about a local axis. Nothing here reads input.
    /// </summary>
    [System.Serializable]
    public class Hinge
    {
        [SerializeField] Transform leaf;
        [Tooltip("Hinge axis in the leaf's local space; a positive angle about it opens the leaf.")]
        [SerializeField] Vector3 localAxis = Vector3.right;

        Quaternion closedRotation;
        bool captured;

        public HingeState State { get; private set; }
        /// <summary>Seconds since the state began.</summary>
        public float Elapsed { get; private set; }
        /// <summary>The angle last applied, degrees from closed.</summary>
        public float Angle { get; private set; }
        public Transform Leaf => leaf;
        public bool AtRest => LookoutRules.AtRest(State);

        public void Configure(Transform leafTransform, Vector3 axis)
        {
            leaf = leafTransform;
            localAxis = axis;
            captured = false;
        }

        /// <summary>Remembers the closed pose. Call once before the first Apply (Awake).</summary>
        public void Capture()
        {
            if (leaf == null) return;
            closedRotation = leaf.localRotation;
            captured = true;
        }

        /// <summary>Starts a swing if the leaf is at rest. False when it is already moving.</summary>
        public bool TryToggle()
        {
            if (!LookoutRules.TryToggle(State, out var next)) return false;
            Apply(next, 0f);
            return true;
        }

        /// <summary>The one way state changes: sets the state and its elapsed time; <see cref="Pose"/> follows from them.</summary>
        public void Apply(HingeState state, float elapsed)
        {
            State = state;
            Elapsed = elapsed;
        }

        /// <summary>Advances the swing, settles it when its time is up, and poses the leaf. True on the frame it came to rest.</summary>
        public bool Tick(float dt, float openAngle, float seconds, AnimationCurve curve)
        {
            bool wasMoving = !AtRest;
            if (wasMoving)
            {
                Elapsed += dt;
                var settled = LookoutRules.Settle(State, Elapsed, seconds);
                if (settled != State) Apply(settled, 0f);
            }
            Pose(openAngle, seconds, curve);
            return wasMoving && AtRest;
        }

        /// <summary>Poses the leaf for the current state and elapsed time: a pure function of both.</summary>
        public void Pose(float openAngle, float seconds, AnimationCurve curve)
        {
            if (leaf == null) return;
            if (!captured) Capture();
            Angle = LookoutRules.HingeAngle(State, Elapsed, openAngle, seconds, curve);
            leaf.localRotation = closedRotation * Quaternion.AngleAxis(Angle, localAxis);
        }
    }
}
