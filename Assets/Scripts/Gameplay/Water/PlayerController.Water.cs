using UnityEngine;
using WashedAshore.World;

namespace WashedAshore.Gameplay
{
    /// <summary>
    /// Wading and surface swimming (spec W6, water-swim-spec.md). The water is the scene's WaterBody and
    /// the numbers are the SwimTuning asset; with either missing the player is always Dry and walks as
    /// before. Depth is the surface minus the terrain under the feet, decided every frame, so nothing here
    /// touches a water collider, and the CharacterController excludes the Water layer as a guard.
    /// </summary>
    public partial class PlayerController
    {
        [Header("Water")]
        [Tooltip("Wade and swim numbers (water-swim-spec.md §3).")]
        [SerializeField] SwimTuning swimTuning;

        WaterMode waterMode;
        float waterDepth, surfaceY = float.NegativeInfinity, groundY, strokeTime;
        bool warnedNoTuning;

        public WaterMode WaterMode => waterMode;
        /// <summary>Surface minus ground under the feet this frame; 0 with no water.</summary>
        public float WaterDepth => waterDepth;
        /// <summary>Water surface at the player this frame (negative infinity with no water).</summary>
        public float SurfaceY => surfaceY;
        public SwimTuning SwimTuning => swimTuning;
        /// <summary>Swim speed (stroke mean); walk speed when there is no SwimTuning.</summary>
        public float SwimSpeed => swimTuning != null ? swimTuning.swimSpeed : walkSpeed;
        /// <summary>Eye height above the feet, from the camera pivot (0.9 x capsule height without one).</summary>
        public float EyeHeight => cameraPivot != null
            ? cameraPivot.position.y - transform.position.y
            : (controller != null ? controller.height * 0.9f : 1.6f);

        void SetupWater()
        {
            // Never ground on water (TD checklist); WorldBounds stays solid for the player.
            controller.excludeLayers |= 1 << WorldLayers.Water;
            if (swimTuning == null) return;
            string problem = swimTuning.Validate(walkSpeed, EyeHeight);
            if (problem != null) Debug.LogWarning($"PlayerController: SwimTuning '{swimTuning.name}': {problem}", this);
        }

        /// <summary>Re-reads the water under the player. Runs every frame, so a teleport, a fall-through reset or a clamp snap is picked up next frame.</summary>
        void UpdateWater()
        {
            var water = WaterBody.Active;
            if (water != null && swimTuning == null && !warnedNoTuning)
            {
                warnedNoTuning = true;
                Debug.LogError("PlayerController: no SwimTuning asset assigned; water is ignored and the player walks on the lake bed.", this);
            }
            Vector3 p = transform.position;
            if (water == null || swimTuning == null || !TerrainQuery.TryGroundHeight(p, out groundY))
            {
                waterMode = WaterMode.Dry;
                waterDepth = 0f;
                surfaceY = float.NegativeInfinity;
                return;
            }
            surfaceY = water.SurfaceY(p.x, p.z);
            waterDepth = Mathf.Max(0f, surfaceY - groundY);
            var next = SwimRules.Classify(waterMode, waterDepth, swimTuning);
            if (next != WaterMode.Swim) strokeTime = 0f;
            waterMode = next;
        }

        /// <summary>Horizontal velocity target from move input (yaw only; pitch never steers a swimmer).</summary>
        Vector3 MoveTarget(Vector2 input, bool sprintHeld, float dt)
        {
            if (swimTuning == null || waterMode == WaterMode.Dry)
                return (transform.forward * input.y + transform.right * input.x) * (sprintHeld ? sprintSpeed : walkSpeed);
            float speed = SwimRules.TargetSpeed(waterMode, waterDepth, sprintHeld, walkSpeed, sprintSpeed, swimTuning);
            if (waterMode == WaterMode.Swim)
            {
                input = SwimRules.SwimInput(input, swimTuning);
                if (input.sqrMagnitude > 0f) strokeTime += dt;
                speed *= SwimRules.StrokeSurge(strokeTime, swimTuning);
            }
            return (transform.forward * input.y + transform.right * input.x) * speed;
        }

        float MoveRate(Vector3 target, bool hasInput)
        {
            if (swimTuning == null) return hasInput ? acceleration : deceleration;
            bool over = horizontalVelocity.sqrMagnitude > target.sqrMagnitude + 1e-4f;
            return SwimRules.Rate(waterMode, hasInput, over, acceleration, deceleration, swimTuning);
        }

        bool CanJump => swimTuning == null || SwimRules.CanJump(waterMode, waterDepth, swimTuning);

        /// <summary>
        /// Highest underwater terrain under the capsule: the feet point and four points a radius out. Samples on dry
        /// ground (at or above the surface, e.g. the top of a bluff face) don't count, so water never lifts a swimmer
        /// onto what it couldn't step onto from land (w-designer guard, spec §5); capsule collision decides there.
        /// </summary>
        float FootprintGround()
        {
            Vector3 p = transform.position;
            float r = controller.radius, high = groundY;
            foreach (var o in Footprint)
                if (TerrainQuery.TryGroundHeight(p + o * r, out float h) && h < surfaceY) high = Mathf.Max(high, h);
            return high;
        }

        static readonly Vector3[] Footprint = { Vector3.forward, Vector3.back, Vector3.left, Vector3.right };

        /// <summary>Swimming vertical speed: no gravity, follow the surface at most surfaceFollowRate.</summary>
        float SwimVerticalVelocity(float dt)
        {
            // Never below the highest ground under the capsule (spec §5 fix 1): on the 18 deg shelf the uphill
            // edge of the footprint is higher than the ground under the feet, and resting on it would ground a swimmer.
            float target = SwimRules.FloatFeetY(surfaceY, FootprintGround(), controller.skinWidth, swimTuning);
            return SwimRules.FollowSpeed(transform.position.y, target, dt, swimTuning);
        }
    }
}
