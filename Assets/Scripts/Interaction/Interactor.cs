using UnityEngine;
using UnityEngine.InputSystem;
using WashedAshore.World;

namespace WashedAshore.Interaction
{
    /// <summary>
    /// Aim and press (spec I1, tower-design-spec.md §3, tower-td-note §6). Sits on the player's camera pivot: one
    /// ray per frame straight ahead, at most <see cref="InteractionSettings.RangeCap"/> m, nearest hit only, so
    /// solids occlude. If the hit collider or a parent is an <see cref="IInteractable"/> that can be used, the
    /// prompt line shows and E / gamepad South uses it. Runs before the PlayerController, which asks
    /// <see cref="TookPressThisFrame"/> before it lets gamepad South jump. Nothing is highlighted (N12).
    /// </summary>
    [DefaultExecutionOrder(Order)]
    public class Interactor : MonoBehaviour
    {
        /// <summary>Before the PlayerController (order 0): the shared South press is decided here first.</summary>
        public const int Order = -100;
        public const string ActionName = "Interact";
        public const string KeyboardBinding = "<Keyboard>/e";
        public const string GamepadBinding = "<Gamepad>/buttonSouth";
        const int KeyboardBindingIndex = 0, GamepadBindingIndex = 1;

        [SerializeField] InteractionSettings settings;
        [SerializeField] InteractionPrompt prompt;

        InputAction interactAction;
        IInteractable target, aimedAt;
        IInteractionFocus focus;
        Collider lastHit;
        IInteractable lastHitInteractable;
        InputDevice labelDevice;
        string keyLabel, lineVerb, lineNoun, lineKey, composed, line;
        int tookPressFrame = -1;

        public InteractionSettings Settings => settings;
        /// <summary>The Interact action (E, gamepad South). A focus reads press and release from here.</summary>
        public InputAction Action => interactAction;
        /// <summary>What the player is aiming at and can use this frame; null when nothing.</summary>
        public IInteractable Target => target;
        /// <summary>The interactable under the aim ray, usable or not; null when the ray hits none.</summary>
        public IInteractable AimedAt => aimedAt;
        /// <summary>Presses made while aiming at an interactable that could not be used (it was moving or blocked).</summary>
        public int RefusedPresses { get; private set; }
        /// <summary>The focus holding the player (the firefinder); null when free.</summary>
        public IInteractionFocus Focus => focus;
        /// <summary>The prompt line this frame; null when hidden.</summary>
        public string PromptLine => line;
        /// <summary>True on the frame a target or a focus took the interact press.</summary>
        public bool TookPressThisFrame => tookPressFrame == Time.frameCount;

        void Awake()
        {
            interactAction = new InputAction(ActionName, InputActionType.Button, KeyboardBinding);
            interactAction.AddBinding(GamepadBinding);
        }

        void OnEnable() => interactAction.Enable();

        void OnDisable()
        {
            EndFocus();
            interactAction.Disable();
            target = null;
            SetLine(null);
        }

        void OnDestroy() => interactAction.Dispose();

        /// <summary>Wires the references (build script and tests).</summary>
        public void Configure(InteractionSettings interactionSettings, InteractionPrompt promptView)
        {
            settings = interactionSettings;
            prompt = promptView;
        }

        /// <summary>Hands the player to <paramref name="next"/> until <see cref="EndFocus"/>. Ends any focus already held.</summary>
        public void BeginFocus(IInteractionFocus next)
        {
            if (ReferenceEquals(focus, next)) return;
            EndFocus();
            focus = next;
            target = null;
        }

        /// <summary>Ends the focus, if any. Safe to call from the focus itself, and when the player is respawned or snapped.</summary>
        public void EndFocus()
        {
            if (focus == null) return;
            var ending = focus;
            focus = null;
            ending.End(this);
        }

        void Update()
        {
            if (settings == null) { target = null; SetLine(null); return; }

            bool pressed = interactAction.WasPressedThisFrame();
            if (pressed) UseDevice(interactAction.activeControl != null ? interactAction.activeControl.device : null);

            if (focus != null)
            {
                target = aimedAt = null;
                if (pressed) tookPressFrame = Time.frameCount;
                focus.Tick(this, Time.deltaTime);
            }
            else
            {
                target = Aim(transform.position, transform.forward);
                if (pressed && target == null && aimedAt != null) RefusedPresses++;
                if (pressed && target != null)
                {
                    tookPressFrame = Time.frameCount;
                    target.Interact(this);
                    if (focus == null && !target.CanInteract(this)) target = null;
                }
            }

            if (focus != null) SetLine(Compose(focus.FocusVerb, focus.FocusNoun));
            else if (target != null) SetLine(Compose(target.Verb, target.Noun));
            else SetLine(null);
        }

        /// <summary>
        /// The usable interactable on a ray; null when there is none. One Physics.Raycast: WorldLayers.SightMask,
        /// triggers ignored, nearest hit only.
        /// </summary>
        public IInteractable Aim(Vector3 origin, Vector3 direction)
        {
            float range = InteractionRules.Range(settings.interactRange, InteractionSettings.RangeCap);
            aimedAt = null;
            if (!Physics.Raycast(origin, direction, out RaycastHit hit, range, WorldLayers.SightMask, QueryTriggerInteraction.Ignore))
                return null;
            if (!ReferenceEquals(hit.collider, lastHit))
            {
                lastHit = hit.collider;
                lastHitInteractable = hit.collider.GetComponentInParent<IInteractable>();
            }
            aimedAt = lastHitInteractable;
            return aimedAt != null && aimedAt.CanInteract(this) ? aimedAt : null;
        }

        /// <summary>Key text follows the device that sent the last press; the keyboard until there is one.</summary>
        void UseDevice(InputDevice device)
        {
            if (keyLabel != null && ReferenceEquals(device, labelDevice)) return;
            labelDevice = device;
            int binding = device is Gamepad ? GamepadBindingIndex : KeyboardBindingIndex;
            keyLabel = InteractionRules.KeyLabel(interactAction.GetBindingDisplayString(binding),
                settings.unnamedSouthDisplay, settings.genericPadKey);
        }

        /// <summary>Builds the line only when the key, verb or noun changed, so a steady prompt allocates nothing.</summary>
        string Compose(string verb, string noun)
        {
            if (string.IsNullOrEmpty(verb)) return null;
            if (keyLabel == null) UseDevice(null);
            if (ReferenceEquals(verb, lineVerb) && ReferenceEquals(noun, lineNoun) && ReferenceEquals(keyLabel, lineKey)) return composed;
            lineVerb = verb;
            lineNoun = noun;
            lineKey = keyLabel;
            composed = InteractionRules.PromptLine(keyLabel, settings.separator, InteractionRules.VerbNoun(verb, noun));
            return composed;
        }

        void SetLine(string next)
        {
            line = next;
            if (prompt != null) prompt.Show(next, settings);
        }
    }
}
