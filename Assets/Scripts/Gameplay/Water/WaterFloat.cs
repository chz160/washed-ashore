using UnityEngine;
using WashedAshore.World;

namespace WashedAshore.Gameplay
{
    /// <summary>
    /// Floats a kinematic body on the water surface (spec W9 test crate). Its height every physics step
    /// (TD: FixedUpdate) is WaterBody.SurfaceY at its XZ and the current water time, minus the draft, the same f(x, z, t) the shader draws, so a
    /// server can float debris and boats from the same function. XZ is left alone (no current push):
    /// whatever moves the body sideways, including WorldBoundsClamp, still works.
    /// It is also floated once when enabled, so a body spawned in a frame without a physics step is never
    /// drawn (or seen by a server) at its spawn height (w-td).
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class WaterFloat : MonoBehaviour
    {
        [Tooltip("How far the pivot sits below the surface, metres (negative = above).")]
        [SerializeField] float draft = 0.2f;
        [Tooltip("Pivot never goes below the ground plus this, metres, so a body in the shallows rests on the bed.")]
        [SerializeField] float groundClearance = 0.3f;

        Rigidbody body;

        public float Draft => draft;

        void Awake()
        {
            body = GetComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
        }

        void OnEnable() => Float();

        void FixedUpdate() => Float();

        void Float()
        {
            var water = WaterBody.Active;
            if (water == null || body == null) return;
            // XZ from the transform: whatever moved the body sideways (a test, the clamp) wrote it there, and a
            // kinematic body's physics position only catches up at the next simulation step.
            Vector3 p = transform.position;
            float y = water.SurfaceY(p.x, p.z, WaterClock.Now) - draft;
            if (TerrainQuery.TryGroundHeight(p, out float ground)) y = Mathf.Max(y, ground + groundClearance);
            p.y = y;
            body.position = p;
            transform.position = p;
        }
    }
}
