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
        // #45: every collider under the leaf, taken once. A leaf is posed only with them off, and they are on only at rest.
        Collider[] colliders;
        bool[] wasEnabled;
        float lastWritten = float.NaN;

        public HingeState State { get; private set; }
        /// <summary>Seconds since the state began.</summary>
        public float Elapsed { get; private set; }
        /// <summary>The angle last applied, degrees from closed.</summary>
        public float Angle { get; private set; }
        public Transform Leaf => leaf;
        public bool AtRest => LookoutRules.AtRest(State);
        /// <summary>True from the start of a swing until it comes to rest: the colliders are off for exactly this long.</summary>
        public bool Moving => !AtRest;
        /// <summary>Every collider under the leaf (cached).</summary>
        public System.Collections.Generic.IReadOnlyList<Collider> Colliders
        {
            get { EnsureColliders(); return colliders; }
        }

        public void Configure(Transform leafTransform, Vector3 axis)
        {
            leaf = leafTransform;
            localAxis = axis;
            captured = false;
            colliders = null;
            lastWritten = float.NaN;
        }

        void EnsureColliders()
        {
            if (colliders != null) return;
            colliders = leaf != null ? leaf.GetComponentsInChildren<Collider>(true) : System.Array.Empty<Collider>();
            wasEnabled = new bool[colliders.Length];
        }

        /// <summary>Switches every collider under the leaf on or off. The owner calls it from its state, never mid-move.</summary>
        public void SetCollidersEnabled(bool on)
        {
            EnsureColliders();
            for (int i = 0; i < colliders.Length; i++)
                if (colliders[i] != null) colliders[i].enabled = on;
        }

        /// <summary>
        /// The volume the leaf's renderers sweep over its whole arc, sampled every 5 degrees and at both ends (#45: the closed/open union misses
        /// the arc). The colliders are off while the leaf is posed, and its rotation and colliders are restored. False without renderers.
        /// </summary>
        public bool TrySweptBounds(float openAngle, out Bounds swept)
        {
            swept = default;
            if (leaf == null) return false;
            var renderers = leaf.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return false;
            if (!captured) Capture();
            EnsureColliders();
            var saved = leaf.localRotation;
            DisableRemembering();
            bool first = true;
            for (float a = 0f; ; a += 5f)
            {
                float angle = Mathf.Min(a, openAngle);
                leaf.localRotation = closedRotation * Quaternion.AngleAxis(angle, localAxis);
                foreach (var r in renderers)
                {
                    if (first) { swept = r.bounds; first = false; } else swept.Encapsulate(r.bounds);
                }
                if (angle >= openAngle) break;
            }
            leaf.localRotation = saved;
            RestoreRemembered();
            return true;
        }

        void DisableRemembering()
        {
            for (int i = 0; i < colliders.Length; i++)
            {
                wasEnabled[i] = colliders[i] != null && colliders[i].enabled;
                if (colliders[i] != null) colliders[i].enabled = false;
            }
        }

        void RestoreRemembered()
        {
            for (int i = 0; i < colliders.Length; i++)
                if (colliders[i] != null) colliders[i].enabled = wasEnabled[i];
        }

        /// <summary>Remembers the closed pose. Call once before the first Apply (Awake).</summary>
        public void Capture()
        {
            if (leaf == null) return;
            closedRotation = leaf.localRotation;
            captured = true;
            colliders = null;
            lastWritten = float.NaN;
            EnsureColliders();
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
            // A rest pose is never rewritten, and the leaf is never written with its colliders on (a collider moved with no Rigidbody, #45).
            if (Angle == lastWritten) return;
            EnsureColliders();
            DisableRemembering();
            leaf.localRotation = closedRotation * Quaternion.AngleAxis(Angle, localAxis);
            RestoreRemembered();
            lastWritten = Angle;
        }
    }
}
