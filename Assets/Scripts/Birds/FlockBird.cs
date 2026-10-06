using System.Collections.Generic;
using UnityEngine;

namespace WashedAshore.Birds
{
    /// <summary>
    /// One overhead crow. Its <see cref="Flock"/> sets the pose each frame; this component owns the wings
    /// (B5; brief 3.3): Flapping when the vertical speed is above +0.3 m/s, Gliding otherwise, holding each clip
    /// at least 1.0 s and cross-fading over 0.25 s.
    /// </summary>
    public class FlockBird : MonoBehaviour
    {
        static readonly List<FlockBird> all = new List<FlockBird>();

        Animator animator;
        Renderer[] renderers;
        FlockTuning t;
        float dwell;
        bool started;

        public static IReadOnlyList<FlockBird> All => all;
        public Flock Flock { get; private set; }
        public Animator Animator => animator;
        public Vector3 Velocity { get; private set; }
        public bool Flapping { get; private set; }

        // ---- Test hooks (positive controls) ----
        public float ExtraModelYaw { get; set; }
        /// <summary>Flap while descending and glide while climbing (wing-rule control).</summary>
        public bool InvertWings { get; set; }

        public Bounds VisualBounds
        {
            get
            {
                Bounds b = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
                return b;
            }
        }

        void Awake()
        {
            animator = GetComponentInChildren<Animator>();
            renderers = GetComponentsInChildren<Renderer>();
        }

        void OnEnable() => all.Add(this);
        void OnDisable() => all.Remove(this);

        public void Init(Flock flock, FlockTuning tuning, System.Random rng)
        {
            Flock = flock;
            t = tuning;
            if (!animator) return;
            animator.applyRootMotion = false;
            animator.speed = rng.Range(0.9f, 1.1f); // wingbeats out of phase across the flock
            animator.Play(t.glidingState, 0, (float)rng.NextDouble());
            dwell = t.minDwellSeconds;
        }

        public void SetPose(Vector3 position, Vector3 velocity, float bankDegrees, float dt)
        {
            Velocity = velocity;
            Quaternion target = transform.rotation;
            Vector3 flat = new Vector3(velocity.x, 0f, velocity.z);
            if (flat.sqrMagnitude > 0.01f)
            {
                // Yaw and pitch follow the flight; bank into the turn. ExtraModelYaw is the facing control's hook.
                Vector3 fwd = Quaternion.Euler(0f, ExtraModelYaw, 0f) * velocity;
                target = Quaternion.LookRotation(fwd, Vector3.up) * Quaternion.Euler(0f, 0f, -bankDegrees);
            }
            var rot = started ? Quaternion.Slerp(transform.rotation, target, 1f - Mathf.Exp(-t.turnSmoothing * dt)) : target;
            transform.SetPositionAndRotation(position, rot);
            if (started) UpdateWings(velocity.y, dt);
            started = true;
        }

        void UpdateWings(float climbRate, float dt)
        {
            dwell += dt;
            bool flap = (InvertWings ? -climbRate : climbRate) > t.flapAboveClimbRate;
            if (flap == Flapping || dwell < t.minDwellSeconds || !animator) return;
            Flapping = flap;
            dwell = 0f;
            animator.CrossFadeInFixedTime(flap ? t.flappingState : t.glidingState, t.crossFadeSeconds, 0);
        }
    }
}
