using UnityEngine;
using UnityEngine.InputSystem;

namespace WashedAshore.Gameplay
{
    /// <summary>
    /// Grounded first-person walk on a CharacterController. Tuning defaults come from
    /// _bmad-output/poc/controller-tuning.md; every number is a serialized field.
    /// Wading and swimming live in PlayerController.Water.cs.
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
        Vector3 horizontalVelocity;
        float verticalVelocity;
        float pitch;

        public bool IsGrounded => controller != null && controller.isGrounded;
        public float WalkSpeed => walkSpeed;
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
        }

        void Start()
        {
            if (ShouldLockOnStart(Application.platform))
                LockCursor();
            Respawn();
        }

        // Browsers only grant pointer lock after a user click and release it themselves on
        // Esc, so WebGL locks on click. Every other platform locks on Start as before.
        // Pure so EditMode tests can cover each platform without building for it.
        public static bool ShouldLockOnStart(RuntimePlatform p) => p != RuntimePlatform.WebGLPlayer;

        public static bool ShouldRelockOnClick(RuntimePlatform p) => p == RuntimePlatform.WebGLPlayer;

        public static bool ShouldApplyMouseLook(RuntimePlatform p, CursorLockMode lockState) =>
            p != RuntimePlatform.WebGLPlayer || lockState == CursorLockMode.Locked;

        static void LockCursor()
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
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

            // CharacterController overrides transform writes while enabled.
            controller.enabled = false;
            transform.SetPositionAndRotation(position, Quaternion.Euler(0f, spawnPoint.eulerAngles.y, 0f));
            controller.enabled = true;
            horizontalVelocity = Vector3.zero;
            verticalVelocity = 0f;
            waterMode = WaterMode.Dry; // the next frame's depth decides (spec §2: no stale state)
        }

        void Update()
        {
            float dt = Time.deltaTime;
            // WebGL: the browser drops pointer lock on Esc; the next click asks for it again.
            if (ShouldRelockOnClick(Application.platform)
                && Cursor.lockState != CursorLockMode.Locked
                && Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
                LockCursor();
            Look(dt);
            Move(dt);
            GuardFallThrough();
        }

        void Look(float dt)
        {
            Vector2 mouseDelta = ShouldApplyMouseLook(Application.platform, Cursor.lockState)
                ? lookAction.ReadValue<Vector2>()
                : Vector2.zero;
            Vector2 delta = mouseDelta * lookSensitivity
                + stickLookAction.ReadValue<Vector2>() * (stickLookSpeed * dt);
            transform.Rotate(0f, delta.x, 0f, Space.Self);
            pitch = Mathf.Clamp(pitch - delta.y, minPitch, maxPitch);
            if (cameraPivot != null)
                cameraPivot.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        }

        void Move(float dt)
        {
            UpdateWater();
            Vector2 input = Vector2.ClampMagnitude(moveAction.ReadValue<Vector2>(), 1f);
            Vector3 target = MoveTarget(input, sprintAction.IsPressed(), dt);
            float rate = MoveRate(target, input.sqrMagnitude > 0f);
            horizontalVelocity = Vector3.MoveTowards(horizontalVelocity, target, rate * dt);

            if (waterMode == WaterMode.Swim)
                verticalVelocity = SwimVerticalVelocity(dt);
            else if (controller.isGrounded && CanJump && jumpAction.WasPressedThisFrame())
                verticalVelocity = Mathf.Sqrt(2f * jumpHeight * -gravity);
            else if (controller.isGrounded && verticalVelocity < 0f)
                verticalVelocity = groundedStickVelocity;
            else
                verticalVelocity = Mathf.Max(verticalVelocity + gravity * dt, terminalVelocity);

            controller.Move((horizontalVelocity + Vector3.up * verticalVelocity) * dt);
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
