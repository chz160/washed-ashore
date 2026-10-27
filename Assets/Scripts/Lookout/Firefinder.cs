using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using WashedAshore.Gameplay;
using WashedAshore.Interaction;
using WashedAshore.World;

namespace WashedAshore.Lookout
{
    /// <summary>
    /// The firefinder (spec I3, tower-design-spec.md §5, N14). Using it puts the eye at the sight with a mild zoom;
    /// Q/E held, the mouse or the right stick turn the alidade; Esc, gamepad East or a tap of the interact key
    /// leaves. The bearing is read off the engraved ring against the alidade's pointer: nothing on screen prints
    /// it. The camera goes through the PlayerController's view seam and is put back exactly on leaving.
    /// </summary>
    public class Firefinder : MonoBehaviour, IInteractable, IInteractionFocus, IMovingTransforms
    {
        public enum Phase { Idle, Entering, InUse, Leaving }

        const float NotPressed = -1f;

        [SerializeField] string id = "lookout.firefinder";
        [SerializeField] LookoutTuning tuning;
        [Tooltip("The rotating sight. At identity it points north (+Z).")]
        [SerializeField] Transform alidade;
        [Tooltip("The pedestal axis and cab floor are given in this transform's space (the tower root).")]
        [SerializeField] Transform frame;
        [Tooltip("Where the pedestal's vertical axis meets the cab floor, frame space.")]
        [SerializeField] Vector3 axisAtFloor;

        InputAction turnLeftAction, lookAction, stickAction, leaveAction;
        PlayerController player;
        Interactor heldBy;
        Phase phase;
        float bearing, elevation, phaseTime, useTime, interactPressedAt = NotPressed, leftPressedAt = NotPressed, fromFov;
        Vector3 fromPosition;
        Quaternion fromRotation;
        bool started;
        // Director amend2 Q4: from entry to the end of the blend out no body and no body shadow shows. Exact values go back afterwards.
        struct RendererState { public Renderer renderer; public bool enabled; public ShadowCastingMode shadows; }
        readonly List<RendererState> hiddenBody = new List<RendererState>();

        public string Id => id;
        public Phase CurrentPhase => phase;
        public bool InUse => phase != Phase.Idle;
        /// <summary>The alidade's bearing: 0 = north (+Z), clockwise, east = 90, in [0, 360).</summary>
        public float BearingDegrees => bearing;
        /// <summary>View pitch at the sight, degrees (negative looks down).</summary>
        public float Elevation => elevation;
        public Transform Alidade => alidade;

        /// <summary>The alidade turns in play. It carries no collider (its collision proxies sit under the pedestal), so nothing is switched off.</summary>
        public System.Collections.Generic.IEnumerable<Transform> MovedTransforms { get { if (alidade != null) yield return alidade; } }
        public System.Collections.Generic.IEnumerable<Collider> SwitchedOffWhileMoving => System.Array.Empty<Collider>();
        public LookoutTuning Tuning => tuning;

        public string Verb => tuning.useVerb;
        public string Noun => tuning.firefinderNoun;
        public string FocusVerb => phase != Phase.Idle && useTime < tuning.firefinderPromptSeconds ? tuning.leaveVerb : null;
        public string FocusNoun => tuning.firefinderNoun;

        /// <summary>Wires everything (build script and tests).</summary>
        public void Configure(string stableId, LookoutTuning lookoutTuning, Transform alidadeTransform, Transform frameTransform, Vector3 axisFloorPoint)
        {
            id = stableId;
            tuning = lookoutTuning;
            alidade = alidadeTransform;
            frame = frameTransform;
            axisAtFloor = axisFloorPoint;
        }

        void Awake()
        {
            turnLeftAction = new InputAction("FirefinderTurnLeft", InputActionType.Button, "<Keyboard>/q");
            lookAction = new InputAction("FirefinderLook", InputActionType.Value, "<Mouse>/delta");
            stickAction = new InputAction("FirefinderStick", InputActionType.Value, "<Gamepad>/rightStick");
            leaveAction = new InputAction("FirefinderLeave", InputActionType.Button, "<Keyboard>/escape");
            leaveAction.AddBinding("<Gamepad>/buttonEast");
        }

        void Start()
        {
            if (!started) ApplyBearing(tuning.firefinderStartBearing);
        }

        void OnDisable()
        {
            if (heldBy != null) heldBy.EndFocus();
        }

        void OnDestroy()
        {
            turnLeftAction.Dispose();
            lookAction.Dispose();
            stickAction.Dispose();
            leaveAction.Dispose();
        }

        /// <summary>The one way the bearing changes (input, tests, later replication): sets it and turns the alidade.</summary>
        public void ApplyBearing(float degrees)
        {
            started = true;
            bearing = Mathf.Repeat(degrees, 360f);
            if (alidade != null) alidade.rotation = Quaternion.Euler(0f, bearing, 0f);
        }

        public bool CanInteract(Interactor by)
        {
            if (phase != Phase.Idle) return false;
            if (player == null) player = by.GetComponentInParent<PlayerController>();
            return player != null && player.IsGrounded && !player.ViewOverridden;
        }

        public void Interact(Interactor by)
        {
            if (!CanInteract(by)) return;
            heldBy = by;
            phase = Phase.Entering;
            phaseTime = useTime = elevation = 0f;
            interactPressedAt = leftPressedAt = NotPressed;
            fromPosition = player.CameraPivot.position;
            fromRotation = player.CameraPivot.rotation;
            fromFov = player.AuthoredFov;
            player.SuspendControl();
            HideBody();
            player.Teleported += OnPlayerTeleported;
            turnLeftAction.Enable();
            lookAction.Enable();
            stickAction.Enable();
            leaveAction.Enable();
            by.BeginFocus(this);
            Pose();
        }

        public void Tick(Interactor by, float dt)
        {
            useTime += dt;
            phaseTime += dt;
            if (phase == Phase.Entering && phaseTime >= tuning.firefinderBlendSeconds) phase = Phase.InUse;
            if (phase == Phase.InUse) ReadInput(by, dt);
            if (phase == Phase.Leaving && phaseTime >= tuning.firefinderBlendSeconds)
            {
                // The player keeps looking where the sight pointed.
                player.SetLook(bearing, 0f);
                by.EndFocus();
                return;
            }
            Pose();
        }

        void ReadInput(Interactor by, float dt)
        {
            // The press that entered never counts: only a press made while in use starts a tap or a hold.
            var interact = by.Action;
            if (interact.WasPressedThisFrame()) interactPressedAt = useTime;
            if (turnLeftAction.WasPressedThisFrame()) leftPressedAt = useTime;

            float turn = 0f;
            if (interactPressedAt >= 0f && interact.IsPressed()) turn += LookoutRules.KeyTurnRate(useTime - interactPressedAt, tuning);
            if (leftPressedAt >= 0f && turnLeftAction.IsPressed()) turn -= LookoutRules.KeyTurnRate(useTime - leftPressedAt, tuning);

            Vector2 mouse = lookAction.ReadValue<Vector2>() * tuning.firefinderMouseDegPerCount;
            Vector2 stick = stickAction.ReadValue<Vector2>() * (tuning.firefinderStickDegPerSec * dt);
            ApplyBearing(bearing + turn * dt + mouse.x + stick.x);
            elevation = Mathf.Clamp(elevation + mouse.y + stick.y, tuning.firefinderPitchMin, tuning.firefinderPitchMax);

            bool leave = leaveAction.WasPressedThisFrame();
            if (interactPressedAt >= 0f && interact.WasReleasedThisFrame())
            {
                leave |= LookoutRules.IsTap(interactPressedAt, useTime, tuning.firefinderTapSeconds);
                interactPressedAt = NotPressed;
            }
            if (leftPressedAt >= 0f && turnLeftAction.WasReleasedThisFrame()) leftPressedAt = NotPressed;
            if (leave) BeginLeave();
        }

        void BeginLeave()
        {
            SightPose(out fromPosition, out fromRotation);
            fromFov = tuning.firefinderFov;
            phase = Phase.Leaving;
            phaseTime = 0f;
        }

        /// <summary>The eye at the sight: behind the pedestal axis, at the tuned height above the cab floor, looking along the bearing.</summary>
        public void SightPose(out Vector3 position, out Quaternion rotation)
        {
            Vector3 along = LookoutRules.BearingDirection(bearing);
            position = frame.TransformPoint(axisAtFloor) + Vector3.up * tuning.firefinderEyeHeight - along * tuning.firefinderEyeBack;
            rotation = Quaternion.LookRotation(along, Vector3.up) * Quaternion.Euler(-elevation, 0f, 0f);
        }

        /// <summary>Camera pose for the phase and its time: a blend into the sight, the sight, or a blend back to the player's eye.</summary>
        void Pose()
        {
            float blend = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(phaseTime / tuning.firefinderBlendSeconds));
            SightPose(out Vector3 toPosition, out Quaternion toRotation);
            float toFov = tuning.firefinderFov;
            if (phase == Phase.InUse) blend = 1f;
            else if (phase == Phase.Leaving)
            {
                toPosition = player.transform.position + Vector3.up * player.AuthoredEyeHeight;
                toRotation = Quaternion.Euler(0f, bearing, 0f);
                toFov = player.AuthoredFov;
            }
            player.OverrideView(Vector3.Lerp(fromPosition, toPosition, blend), Quaternion.Slerp(fromRotation, toRotation, blend),
                Mathf.Lerp(fromFov, toFov, blend));
        }

        void OnPlayerTeleported()
        {
            if (heldBy != null) heldBy.EndFocus();
        }

        /// <summary>Focus over, however it ended: the camera, FOV and controls go back to the player.</summary>
        public void End(Interactor by)
        {
            if (phase == Phase.Idle) return;
            phase = Phase.Idle;
            heldBy = null;
            turnLeftAction.Disable();
            lookAction.Disable();
            stickAction.Disable();
            leaveAction.Disable();
            if (player == null) return;
            player.Teleported -= OnPlayerTeleported;
            player.ClearViewOverride();
            RestoreBody();
            player.ResumeControl();
        }

        /// <summary>Records every renderer under the player (view body and shadow body) with its enabled flag and shadow mode, then turns them all off.</summary>
        void HideBody()
        {
            RestoreBody();
            foreach (var r in player.GetComponentsInChildren<Renderer>(true))
            {
                hiddenBody.Add(new RendererState { renderer = r, enabled = r.enabled, shadows = r.shadowCastingMode });
                r.enabled = false;
                r.shadowCastingMode = ShadowCastingMode.Off;
            }
        }

        void RestoreBody()
        {
            foreach (var s in hiddenBody)
            {
                if (s.renderer == null) continue;
                s.renderer.enabled = s.enabled;
                s.renderer.shadowCastingMode = s.shadows;
            }
            hiddenBody.Clear();
        }
    }
}
