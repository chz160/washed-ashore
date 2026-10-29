using UnityEngine;
using WashedAshore.Interaction;
using WashedAshore.World;

namespace WashedAshore.Gameplay
{
    /// <summary>
    /// Grounded first-person walk on a CharacterController. Tuning defaults come from
    /// _bmad-output/poc/controller-tuning.md; every number is a serialized field.
    /// Wading and swimming live in PlayerController.Water.cs,
    /// the camera seam in PlayerController.View.cs.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public partial class PlayerController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] Transform cameraPivot;
        [SerializeField] Transform spawnPoint;
        [Tooltip("Only named in the warning when spawnPoint is missing: the scene carries the reference and the level builders re-link it (#46: no lookup by name).")]
        [SerializeField] string spawnPointName = "PlayerSpawn";

        [Header("Movement")]
        [SerializeField] float walkSpeed = 5f;
        [SerializeField] float sprintSpeed = 8f;
        [SerializeField] float acceleration = 20f;
        [SerializeField] float deceleration = 25f;

        [Header("Jump")]
        [SerializeField] float jumpHeight = 1.2f;

        [Header("Gravity")]
        [SerializeField] float gravity = -20f;
        [SerializeField] float groundedStickVelocity = -2f;
        [SerializeField] float terminalVelocity = -50f;
        [SerializeField] float fallThroughTolerance = 2f;
        [SerializeField] float spawnHeightOffset = 0.5f;

        [Header("Look")]
        [SerializeField] float lookSensitivity = 0.1f;
        [SerializeField] float stickLookSpeed = 120f;
        [SerializeField] float minPitch = -85f;
        [SerializeField] float maxPitch = 85f;

        CharacterController controller;
        Interactor interactor;
        Vector3 horizontalVelocity;
        float verticalVelocity;
        float pitch;

        /// <summary>The sim's grounded flag: what the last tick's move left, false after a placement, a bounds snap or (from the snapshot) a restore. Live it equals the CharacterController's flag.</summary>
        public bool IsGrounded => grounded;
        /// <summary>Same value as <see cref="IsGrounded"/>, named for the animator driver (P6): it reads the sim, never the controller.</summary>
        public bool SimGrounded => grounded;
        public float WalkSpeed => walkSpeed;
        /// <summary>Upper limit on the walk target speed set by a place that wants a slower walk (the lookout stairs); 0 = none.</summary>
        public float WalkSpeedCap { get; set; }
        /// <summary>When set, this move vector (x right, y forward) replaces the Move action: the FPS probe walks the real controller with it. Null in play.</summary>
        public Vector2? ExternalMoveInput { get; set; }
        /// <summary>This player's tower-zone state (saved capsule values and zone readings), written by TowerWalkZones (#46 P4: one slot per player).</summary>
        [System.NonSerialized] public TowerZoneState TowerZone;
        /// <summary>True while the trap door's duck holds this player's eye height (#46 P4: per player, not on the door).</summary>
        public bool HatchDuck { get; set; }
        /// <summary>Metres the controller is pulled back down onto the ground after a step that left it airborne; 0 = off.</summary>
        public float GroundSnapDistance { get; set; }
        /// <summary>This player's CharacterController (capsule values for the tower zones and the hatch).</summary>
        public CharacterController Capsule => controller;
        public float SprintSpeed => sprintSpeed;
        public float JumpHeight => jumpHeight;
        public float Gravity => gravity;
        public Transform SpawnPoint => spawnPoint;

        void Awake()
        {
            controller = GetComponent<CharacterController>();
            PlayerRegistry.Register(this);   // from Awake to OnDestroy: a player switched off (parked by a test or a cutscene) still has its body in the world
            ApplyPlayerLayer();
            SetupInput();
            SetupWater();
            SetupView();
            SetupStance();
            ResetInterpolation();
            // The shared gamepad South: the Interactor runs first and takes the press while a prompt shows.
            interactor = GetComponentInChildren<Interactor>();
        }

        void OnEnable()
        {
            EnableInput();
            simWaterTime = WaterClock.Now;
        }

        void OnDisable() => DisableInput();

        void OnDestroy()
        {
            DisposeInput();
            PlayerRegistry.Unregister(this);
        }

        /// <summary>
        /// Puts the Player root and everything under it (controller, camera pivot, shadow and view bodies, prompt, debug camera) on the
        /// Player layer (#43, td ruling). Set by script, never by a scene value, so edit-time tools and the NavMesh bake see what they
        /// saw before. Re-run after any body is built; objects made later under the root copy their parent's layer.
        /// </summary>
        public void ApplyPlayerLayer() => WashedAshore.World.WorldLayers.SetLayerRecursive(transform, WashedAshore.World.WorldLayers.Player);

        void Start()
        {
            ApplyPlayerLayer(); // every Awake has run: the other scripts' children are on the Player layer too
            LockCursor();
            Respawn();
        }

        static void LockCursor()
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        /// <summary>
        /// Slims and shortens (or restores) the capsule for a place the authored one cannot fit, such as the lookout stairs; the
        /// feet stay where they are.
        /// </summary>
        public void SetWalkCapsule(float radius, float height)
        {
            float bottom = controller.center.y - controller.height * 0.5f;
            controller.radius = radius;
            controller.height = height;
            controller.center = new Vector3(controller.center.x, bottom + height * 0.5f, controller.center.z);
            OnWalkCapsuleSet(height);
        }

        public void Respawn()
        {
            if (spawnPoint == null)
            {
                Debug.LogWarning($"PlayerController: no spawn point (looked for '{spawnPointName}'); staying at {transform.position}.", this);
                return;
            }
            PlaceAt(spawnPoint.position, spawnPoint.eulerAngles.y, true);
        }

        /// <summary>Makes <paramref name="point"/> the spawn point and respawns there. Later respawns (the fall-through guard) return to it.</summary>
        public void SetSpawnPoint(Transform point)
        {
            if (point == null) throw new System.ArgumentNullException(nameof(point));
            spawnPoint = point;
            Respawn();
        }

        /// <summary>
        /// Respawns at <paramref name="position"/> facing world yaw <paramref name="yaw"/> (degrees) under Respawn's rules, without changing the spawn point.
        /// <paramref name="snapToGround"/> true puts the feet at the terrain height plus the spawn offset; false keeps position.y.
        /// </summary>
        public void SpawnAt(Vector3 position, float yaw, bool snapToGround = true)
        {
            if (!IsFinite(position) || float.IsNaN(yaw) || float.IsInfinity(yaw))
                throw new System.ArgumentException($"SpawnAt needs a finite position and yaw (got {position}, {yaw}).");
            PlaceAt(position, yaw, snapToGround);
        }

        static bool IsFinite(Vector3 v) =>
            !(float.IsNaN(v.x) || float.IsInfinity(v.x) || float.IsNaN(v.y) || float.IsInfinity(v.y) || float.IsNaN(v.z) || float.IsInfinity(v.z));

        void PlaceAt(Vector3 position, float yaw, bool snapToGround)
        {
            if (snapToGround && TerrainQuery.TryGroundHeight(position, out float ground))
                position.y = ground + spawnHeightOffset;

            // CharacterController overrides transform writes while enabled.
            controller.enabled = false;
            transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            controller.enabled = true;
            horizontalVelocity = Vector3.zero;
            verticalVelocity = 0f;
            waterMode = WaterMode.Dry; // the next frame's depth decides (spec §2: no stale state)
            grounded = false;          // a re-enabled CharacterController reports false until it moves
            simWaterTime = WaterClock.Now;
            teleportedThisTick = true;
            ResetStance();
            ResetInterpolation();
            PresentView();
            Teleported?.Invoke();
        }

        /// <summary>
        /// The world-bounds clamp moves this player to <paramref name="target"/>. A bounds snap is a teleport: the firefinder and the camera
        /// seam listen for <see cref="Teleported"/>. Velocities are kept, as the clamp always did.
        /// </summary>
        internal void BoundsSnap(Vector3 target)
        {
            // CharacterController overrides transform writes while enabled.
            controller.enabled = false;
            transform.position = target;
            controller.enabled = true;
            grounded = false;
            teleportedThisTick = true;
            ResetInterpolation();
            PresentView();
            Teleported?.Invoke();
        }

        void Move(float dt, in PlayerCommand cmd)
        {
            UpdateWater();
            Vector2 input = controlSuspended ? Vector2.zero : Vector2.ClampMagnitude(cmd.Move, 1f);
            bool sprint = !controlSuspended && (cmd.Buttons & PlayerButtons.Sprint) != 0;
            bool jump = !controlSuspended && (cmd.Buttons & PlayerButtons.Jump) != 0;
            Vector3 target = MoveTarget(input, sprint, dt);
            if (WalkSpeedCap > 0f) target = Vector3.ClampMagnitude(target, WalkSpeedCap);
            if (StanceSpeedLimit >= 0f) target = Vector3.ClampMagnitude(target, StanceSpeedLimit);
            float rate = MoveRate(target, input.sqrMagnitude > 0f);
            horizontalVelocity = Vector3.MoveTowards(horizontalVelocity, target, rate * dt);

            if (waterMode == WaterMode.Swim)
                verticalVelocity = SwimVerticalVelocity(dt);
            else if (grounded && CanJump && stance == Stance.Stand && !stanceTookJump && jump)
                verticalVelocity = Mathf.Sqrt(2f * jumpHeight * -gravity);
            else if (grounded && verticalVelocity < 0f)
                verticalVelocity = groundedStickVelocity;
            else
                verticalVelocity = Mathf.Max(verticalVelocity + gravity * dt, terminalVelocity);

            bool wasGrounded = grounded;
            controller.Move((horizontalVelocity + Vector3.up * verticalVelocity) * dt);
            // Walking down a steep ramp the horizontal step leaves the feet in the air; snap back onto it (never after a jump).
            if (GroundSnapDistance > 0f && wasGrounded && !controller.isGrounded && verticalVelocity <= 0f && waterMode == WaterMode.Dry)
            {
                controller.Move(Vector3.down * GroundSnapDistance);
                if (controller.isGrounded) verticalVelocity = groundedStickVelocity;
            }
            grounded = controller.isGrounded;
        }

        void GuardFallThrough()
        {
            if (!TerrainQuery.TryGroundHeight(transform.position, out float ground)) return;
            if (transform.position.y < ground - fallThroughTolerance)
            {
                Debug.LogWarning($"PlayerController: fell below terrain at {transform.position}, respawning.");
                Respawn();
            }
        }
    }
}
