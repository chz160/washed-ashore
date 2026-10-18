using UnityEngine;
using UnityEngine.InputSystem;
using WashedAshore.Interaction;

namespace WashedAshore.Gameplay
{
    /// <summary>
    /// Grounded first-person walk on a CharacterController. Tuning defaults come from
    /// _bmad-output/poc/controller-tuning.md; every number is a serialized field.
    /// Wading and swimming live in PlayerController.Water.cs, stair lanes in Climb/PlayerController.Climb.cs,
    /// the camera seam in PlayerController.View.cs.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public partial class PlayerController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] Transform cameraPivot;
        [SerializeField] Transform spawnPoint;
        [Tooltip("Looked up by name when spawnPoint is missing: a scripted level rebuild recreates PlayerSpawn.")]
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
        InputAction moveAction;
        InputAction lookAction;
        InputAction stickLookAction;
        InputAction sprintAction;
        InputAction jumpAction;
        Interactor interactor;
        Vector3 horizontalVelocity;
        float verticalVelocity;
        float pitch;

        public bool IsGrounded => controller != null && controller.isGrounded;
        public float WalkSpeed => walkSpeed;
        /// <summary>Upper limit on the walk target speed set by a place that wants a slower walk (the lookout stairs); 0 = none.</summary>
        public float WalkSpeedCap { get; set; }
        /// <summary>Metres the controller is pulled back down onto the ground after a step that left it airborne; 0 = off.</summary>
        public float GroundSnapDistance { get; set; }
        public float SprintSpeed => sprintSpeed;
        public float JumpHeight => jumpHeight;
        public float Gravity => gravity;
        public Transform SpawnPoint => spawnPoint;

        void Awake()
        {
            controller = GetComponent<CharacterController>();

            moveAction = new InputAction("Move", InputActionType.Value);
            moveAction.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w")
                .With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a")
                .With("Right", "<Keyboard>/d");
            moveAction.AddBinding("<Gamepad>/leftStick");

            lookAction = new InputAction("Look", InputActionType.Value, "<Mouse>/delta");
            stickLookAction = new InputAction("StickLook", InputActionType.Value, "<Gamepad>/rightStick");
            sprintAction = new InputAction("Sprint", InputActionType.Button, "<Keyboard>/leftShift");
            sprintAction.AddBinding("<Gamepad>/leftStickPress");
            jumpAction = new InputAction("Jump", InputActionType.Button, "<Keyboard>/space");
            jumpAction.AddBinding("<Gamepad>/buttonSouth");
            SetupWater();
            SetupClimb();
            SetupView();
            // The shared gamepad South: the Interactor runs first and takes the press while a prompt shows.
            interactor = GetComponentInChildren<Interactor>();
        }

        void OnEnable()
        {
            moveAction.Enable();
            lookAction.Enable();
            stickLookAction.Enable();
            sprintAction.Enable();
            jumpAction.Enable();
        }

        void OnDisable()
        {
            moveAction.Disable();
            lookAction.Disable();
            stickLookAction.Disable();
            sprintAction.Disable();
            jumpAction.Disable();
        }

        void OnDestroy()
        {
            moveAction.Dispose();
            lookAction.Dispose();
            stickLookAction.Dispose();
            sprintAction.Dispose();
            jumpAction.Dispose();
            TeardownClimb();
        }

        void Start()
        {
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
        /// feet stay where they are. The new radius is also the radius the climb code treats as authored, so it does not grow it back.
        /// </summary>
        public void SetWalkCapsule(float radius, float height)
        {
            float bottom = controller.center.y - controller.height * 0.5f;
            controller.radius = radius;
            controller.height = height;
            controller.center = new Vector3(controller.center.x, bottom + height * 0.5f, controller.center.z);
            authoredRadius = radius;
        }

        public void Respawn()
        {
            if (spawnPoint == null && !string.IsNullOrEmpty(spawnPointName))
            {
                var found = GameObject.Find(spawnPointName);
                if (found != null)
                {
                    spawnPoint = found.transform;
                    // Safety net only: the scene should carry the reference (level build re-links it).
                    Debug.LogWarning($"PlayerController: spawnPoint reference missing; using '{spawnPointName}' found by name.", this);
                }
            }
            if (spawnPoint == null)
            {
                Debug.LogWarning($"PlayerController: no spawn point (looked for '{spawnPointName}'); staying at {transform.position}.", this);
                return;
            }
            Vector3 position = spawnPoint.position;
            if (TerrainQuery.TryGroundHeight(position, out float ground))
                position.y = ground + spawnHeightOffset;

            ClearClimb();
            // CharacterController overrides transform writes while enabled.
            controller.enabled = false;
            transform.SetPositionAndRotation(position, Quaternion.Euler(0f, spawnPoint.eulerAngles.y, 0f));
            controller.enabled = true;
            horizontalVelocity = Vector3.zero;
            verticalVelocity = 0f;
            waterMode = WaterMode.Dry; // the next frame's depth decides (spec §2: no stale state)
            Teleported?.Invoke();
        }

        void Update()
        {
            float dt = Time.deltaTime;
            Look(dt);
            Move(dt);
            GuardFallThrough();
        }

        void Look(float dt)
        {
            if (controlSuspended) return;
            Vector2 mouseDelta = lookAction.ReadValue<Vector2>();
            Vector2 delta = mouseDelta * lookSensitivity
                + stickLookAction.ReadValue<Vector2>() * (stickLookSpeed * dt);
            transform.Rotate(0f, delta.x, 0f, Space.Self);
            pitch = Mathf.Clamp(pitch - delta.y, minPitch, maxPitch);
            if (cameraPivot != null && !viewPoseOverridden)
                cameraPivot.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        }

        void Move(float dt)
        {
            UpdateWater();
            Vector2 input = controlSuspended ? Vector2.zero : Vector2.ClampMagnitude(moveAction.ReadValue<Vector2>(), 1f);
            bool sprint = !controlSuspended && sprintAction.IsPressed();
            // Stair lanes: one check, and off any climb area the walk below runs as before.
            if (ClimbActive && ClimbMove(input, sprint, dt)) return;
            Vector3 target = climbArea != null ? ClimbWalkTarget(input) : MoveTarget(input, sprint, dt);
            if (WalkSpeedCap > 0f) target = Vector3.ClampMagnitude(target, WalkSpeedCap);
            float rate = MoveRate(target, input.sqrMagnitude > 0f);
            horizontalVelocity = Vector3.MoveTowards(horizontalVelocity, target, rate * dt);

            if (waterMode == WaterMode.Swim)
                verticalVelocity = SwimVerticalVelocity(dt);
            else if (controller.isGrounded && CanJump && JumpPressed())
                verticalVelocity = Mathf.Sqrt(2f * jumpHeight * -gravity);
            else if (controller.isGrounded && verticalVelocity < 0f)
                verticalVelocity = groundedStickVelocity;
            else
                verticalVelocity = Mathf.Max(verticalVelocity + gravity * dt, terminalVelocity);

            bool wasGrounded = controller.isGrounded;
            controller.Move((horizontalVelocity + Vector3.up * verticalVelocity) * dt);
            // Walking down a steep ramp the horizontal step leaves the feet in the air; snap back onto it (never after a jump).
            if (GroundSnapDistance > 0f && wasGrounded && !controller.isGrounded && verticalVelocity <= 0f && waterMode == WaterMode.Dry)
            {
                controller.Move(Vector3.down * GroundSnapDistance);
                if (controller.isGrounded) verticalVelocity = groundedStickVelocity;
            }
        }

        /// <summary>
        /// A jump press that counts: not while control is suspended or inside a climb area (silently ignored),
        /// and not a gamepad South press the Interactor took for a prompt. Space always jumps.
        /// </summary>
        bool JumpPressed()
        {
            if (!jumpAction.WasPressedThisFrame() || controlSuspended || ClimbJumpLocked) return false;
            bool fromGamepad = jumpAction.activeControl != null && jumpAction.activeControl.device is Gamepad;
            return !InteractionRules.JumpBlocked(fromGamepad, interactor != null && interactor.TookPressThisFrame);
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
