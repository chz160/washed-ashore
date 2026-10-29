using UnityEngine;
using UnityEngine.InputSystem;
using WashedAshore.Interaction;

namespace WashedAshore.Gameplay
{
    /// <summary>
    /// Every InputAction of the player and the gather step (#46 P1): the only player code that reads an action or a device. Update calls
    /// <see cref="GatherFrame"/> once per frame; the tick takes the latest values as a <see cref="PlayerCommand"/> and never reads input.
    /// Held values (move, sprint, yaw, pitch) are sampled once per frame and copied into every tick of that frame; edges (jump, stance)
    /// latch until one tick takes them. Gather records raw intent: the tick applies every game rule again from sim state.
    /// </summary>
    public partial class PlayerController
    {
        InputAction moveAction;
        InputAction lookAction;
        InputAction stickLookAction;
        InputAction sprintAction;
        InputAction jumpAction;
        InputAction[] stanceActions;
        StanceKey injectedKey;

        // Gathered this frame.
        Vector2 frameMove;
        bool frameSprint, jumpLatched;
        StanceKey stanceLatched;

        /// <summary>ResumeControl ran: this frame's stance key is dropped at gather, and the next tick ignores stance input.</summary>
        void NoteResume()
        {
            stanceResumeFrame = Time.frameCount;
            resumeTickPending = true;
        }

        /// <summary>Presses a stance key as if the player had (tests, the debug tools). Taken by the next gather.</summary>
        public void PressStanceKey(StanceKey key) => injectedKey = key;

        void SetupInput()
        {
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
            stanceActions = new InputAction[StanceBindings.All.Length];
            for (int i = 0; i < stanceActions.Length; i++) stanceActions[i] = StanceBindings.Create(StanceBindings.All[i]);
        }

        void EnableInput()
        {
            moveAction.Enable();
            lookAction.Enable();
            stickLookAction.Enable();
            sprintAction.Enable();
            jumpAction.Enable();
            foreach (var a in stanceActions) a.Enable();
        }

        void DisableInput()
        {
            moveAction.Disable();
            lookAction.Disable();
            stickLookAction.Disable();
            sprintAction.Disable();
            jumpAction.Disable();
            foreach (var a in stanceActions) a.Disable();
        }

        void DisposeInput()
        {
            moveAction.Dispose();
            lookAction.Dispose();
            stickLookAction.Dispose();
            sprintAction.Dispose();
            jumpAction.Dispose();
            foreach (var a in stanceActions) a.Dispose();
        }

        /// <summary>
        /// Once per frame, before the ticks: look (per frame, for latency: the body turns and the pivot pitches at once), the held values, and
        /// the jump and stance edges. A frame with no tick loses nothing: the edges stay latched.
        /// </summary>
        void GatherFrame(float frameDt)
        {
            FrameLook(frameDt);
            frameMove = Vector2.ClampMagnitude(ExternalMoveInput ?? moveAction.ReadValue<Vector2>(), 1f);
            frameSprint = sprintAction.IsPressed();
            if (JumpPressRoutes()) jumpLatched = true;
            StanceKey key = StanceKey.None;
            if (stanceResumeFrame != Time.frameCount)
            {
                key = injectedKey;
                injectedKey = StanceKey.None;
                for (int i = 0; i < stanceActions.Length && key == StanceKey.None; i++)
                    if (stanceActions[i].WasPressedThisFrame()) key = StanceBindings.All[i].Key;
            }
            if (key != StanceKey.None && stanceLatched == StanceKey.None) stanceLatched = key;   // several presses before a tick collapse to the first
        }

        void FrameLook(float frameDt)
        {
            if (controlSuspended || StanceLookLocked) return;
            Vector2 mouseDelta = lookAction.ReadValue<Vector2>();
            Vector2 delta = mouseDelta * lookSensitivity
                + stickLookAction.ReadValue<Vector2>() * (stickLookSpeed * frameDt);
            transform.Rotate(0f, delta.x, 0f, Space.Self);
            pitch = Mathf.Clamp(pitch - delta.y, minPitch, maxPitch);
        }

        /// <summary>
        /// A jump press that counts for routing: not a gamepad South press the Interactor took for a prompt. Space always jumps. The
        /// suspended-control half of the old rule is applied in the tick.
        /// </summary>
        bool JumpPressRoutes()
        {
            if (!jumpAction.WasPressedThisFrame()) return false;
            bool fromGamepad = jumpAction.activeControl != null && jumpAction.activeControl.device is Gamepad;
            return !InteractionRules.JumpBlocked(fromGamepad, interactor != null && interactor.TookPressThisFrame);
        }

        /// <summary>The command for the next tick from this frame's gather; the edges are spent.</summary>
        PlayerCommand TakeCommand()
        {
            var cmd = new PlayerCommand
            {
                Tick = tick + 1,
                Move = frameMove,
                Yaw = transform.eulerAngles.y,
                Pitch = pitch,
                Buttons = (frameSprint ? PlayerButtons.Sprint : PlayerButtons.None) | (jumpLatched ? PlayerButtons.Jump : PlayerButtons.None),
                Stance = stanceLatched,
            };
            jumpLatched = false;
            stanceLatched = StanceKey.None;
            return cmd;
        }
    }
}
