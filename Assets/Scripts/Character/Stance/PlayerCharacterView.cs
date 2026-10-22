using UnityEngine;
using WashedAshore.Character.Appearance;
using WashedAshore.Gameplay;

namespace WashedAshore.Character.Stance
{
    /// <summary>
    /// The Player's visible body: assembles the default appearance under the Player, hands it to <see cref="FirstPersonBody"/>
    /// (head shadow-only, per-stance back offset) and drives its Animator from the PlayerController. The camera never follows a bone.
    /// Parameter contract (engineer-2 to artist, 2026-10-18): Stance, Transitioning, Speed, MoveX, MoveZ, Grounded, VerticalSpeed,
    /// AirTime, JumpStart and JumpLand (triggers), LocoRate. A parameter the controller lacks is skipped.
    /// </summary>
    [DefaultExecutionOrder(50)]
    public class PlayerCharacterView : MonoBehaviour
    {
        [SerializeField] CharacterAppearance appearance;
        [SerializeField] FirstPersonBody firstPersonBody;
        [Tooltip("Seconds in the air before a landing plays Jump_Land (player-stances.md section 4).")]
        [SerializeField] float landAfterAir = 0.25f;
        [SerializeField] float jumpStartVelocity = 1.5f;

        static readonly int pStance = Animator.StringToHash("Stance"), pTransitioning = Animator.StringToHash("Transitioning"),
            pSpeed = Animator.StringToHash("Speed"), pMoveX = Animator.StringToHash("MoveX"), pMoveZ = Animator.StringToHash("MoveZ"),
            pGrounded = Animator.StringToHash("Grounded"), pVertical = Animator.StringToHash("VerticalSpeed"),
            pAir = Animator.StringToHash("AirTime"), pJumpStart = Animator.StringToHash("JumpStart"),
            pJumpLand = Animator.StringToHash("JumpLand"), pRate = Animator.StringToHash("LocoRate");

        /// <summary>Command-line flag of the FPS probe body-off arm: no assembler, no view copy, no first-person body work.</summary>
        public const string BodyOffArg = "-bbBodyOff";

        PlayerController player;
        CharacterBody characterBody;
        CharacterViewCopy viewCopy;
        Animator animator;
        readonly System.Collections.Generic.HashSet<int> has = new System.Collections.Generic.HashSet<int>();
        readonly System.Collections.Generic.HashSet<int> viewHas = new System.Collections.Generic.HashSet<int>();
        Animator viewAnimator;   // the view body's own Animator (capped arms), written with exactly the same parameters as the shadow's; null when there is no view body
        Vector3 lastPosition;
        bool wasGrounded = true;
        float airTime;

        public CharacterBody Body => characterBody;
        /// <summary>The pose-following view body the camera sees (no Head); null when none was built.</summary>
        public CharacterViewCopy ViewCopy => viewCopy;
        /// <summary>The view Animator the driver writes in step with the shadow's; null when none.</summary>
        public Animator ViewAnimator => viewAnimator;
        public FirstPersonBody FirstPerson => firstPersonBody;
        public CharacterAppearance Appearance { get => appearance; set => appearance = value; }

        /// <summary>Test-only body-off switch for the bird frame-time A-B (td): the file TestResults/player-character/bodyoff.flag turns the bodies off. Absent from release builds.</summary>
        static bool TestBodyOff()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            return System.IO.File.Exists("TestResults/player-character/bodyoff.flag");
#else
            return false;
#endif
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /// <summary>Test-only: forces the LocoRate the driver writes (the forced-bad slide case). Never set outside tests.</summary>
        public static float? TestRateOverride;
#endif

        void LogBodyCounts()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.Log($"[BodyAB] body {(characterBody == null ? "off" : "on")}: SMR {GetComponentsInChildren<SkinnedMeshRenderer>(true).Length}, Animator {GetComponentsInChildren<Animator>(true).Length}");
#endif
        }

        void Awake()
        {
            player = GetComponent<PlayerController>();
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), BodyOffArg) >= 0 || TestBodyOff()) { LogBodyCounts(); return; }   // body-off arm: the bodies are never assembled
            Build();
        }

        void Build()
        {
            lastPosition = transform.position;
            if (appearance == null)
            {
                Debug.LogWarning("PlayerCharacterView: no CharacterAppearance assigned; the player has no body.", this);
                return;
            }
            characterBody = CharacterAssembler.Assemble(appearance, transform);
            if (characterBody == null) return;
            animator = characterBody.Animator;
            if (animator != null)
            {
                animator.applyRootMotion = false;
                foreach (var p in animator.parameters) has.Add(p.nameHash);
            }
            if (firstPersonBody == null) firstPersonBody = GetComponent<FirstPersonBody>() ?? gameObject.AddComponent<FirstPersonBody>();
            viewCopy = CharacterAssembler.AssembleViewCopy(characterBody, transform);
            firstPersonBody.Bind(player, characterBody.transform, viewCopy != null ? viewCopy.Body.transform : null);
            characterBody.SlotChanged += OnSlotChanged;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            // H9 retune launch argument (dev builds and the Editor only; a release build has no StanceRangeArgument type and ignores -bbStance).
            string bbStance = StanceRangeArgument.FromCommandLine(System.Environment.GetCommandLineArgs());
            if (bbStance != null) StanceRangeArgument.Apply(bbStance, player, characterBody, viewCopy); else StanceRangeArgument.ClearApplied();
            Debug.Log(StanceLogLine(player, StanceRangeArgument.Applied));
#else
            Debug.Log(StanceLogLine(player, null));   // a release build ignores -bbStance
#endif
            LogBodyCounts();
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /// <summary>
        /// Test-only: swaps the whole appearance (for example the women's rig) by destroying both bodies and assembling again, so the same PlayMode cases can run on
        /// either body type. The old bodies go at the end of the frame; wait one frame before measuring. Absent from release builds.
        /// </summary>
        public void RebuildWith(CharacterAppearance next)
        {
            if (characterBody != null) { characterBody.SlotChanged -= OnSlotChanged; Destroy(characterBody.gameObject); }
            if (viewCopy != null) Destroy(viewCopy.gameObject);
            characterBody = null; viewCopy = null; animator = null; viewAnimator = null;
            has.Clear(); viewHas.Clear();
            appearance = next;
            Build();
        }
#endif

        /// <summary>The one startup line, in every build (td): the stance eyes and crouch speed in force and the range argument applied ("none" in a release build, even when -bbStance is passed).</summary>
        public static string StanceLogLine(PlayerController p, string rangeArgs)
        {
            var t = p.StanceTuning;
            var c = System.Globalization.CultureInfo.InvariantCulture;
            return string.Format(c, "[Stance] eyes stand {0:F2} crouch {1:F2} squat {2:F2} sit {3:F2} sleep {4:F2}; crouchspeed {5:F2}; range args: {6}",
                p.AuthoredEyeHeight, t.crouchEye, t.squatEye, t.sitEye, t.sleepEye, t.crouchSpeed, string.IsNullOrEmpty(rangeArgs) ? "none" : rangeArgs);
        }

        void OnSlotChanged(CharacterSlot slot) => firstPersonBody.Rescan();

        void OnDestroy()
        {
            if (characterBody != null) characterBody.SlotChanged -= OnSlotChanged;
        }

        /// <summary>
        /// Order of record (td, amend7): this driver is execution order 50 and writes every parameter to BOTH Animators in Update, before Unity evaluates them
        /// (Animators evaluate between Update and LateUpdate). CharacterViewCopy (order 100) only copies poses in LateUpdate and mirrors nothing.
        /// </summary>
        void Update()
        {
            ResolveViewAnimator();
            if (animator == null || player == null || Time.deltaTime <= 0f) return;
            float dt = Time.deltaTime;
            Vector3 v = (transform.position - lastPosition) / dt;
            lastPosition = transform.position;
            Vector3 local = transform.InverseTransformDirection(v);
            float speed = new Vector2(v.x, v.z).magnitude;
            bool grounded = player.IsGrounded;

            if (grounded) { if (!wasGrounded && airTime >= landAfterAir) Trigger(pJumpLand); airTime = 0f; }
            else
            {
                if (wasGrounded && v.y > jumpStartVelocity && player.Stance == Gameplay.Stance.Stand) Trigger(pJumpStart);
                airTime += dt;
            }
            wasGrounded = grounded;

            Set(pStance, (int)player.Stance);
            Set(pTransitioning, player.StanceTransitioning);
            Set(pSpeed, speed);
            Set(pMoveX, local.x);
            Set(pMoveZ, local.z);
            Set(pGrounded, grounded);
            Set(pVertical, v.y);
            Set(pAir, airTime);
            float planar = new Vector2(local.x, local.z).magnitude;
            float cosFwd = planar > 0.05f ? Mathf.Clamp(local.z / planar, -1f, 1f) : 1f;
            float sideways = 1f - Mathf.Max(0f, cosFwd);   // 0 forward, 1 sideways or back (td)
            float rate = CharacterClipSet.LocomotionRate((int)player.Stance, speed, characterBody.SkeletonScale, sideways);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (TestRateOverride.HasValue) rate = TestRateOverride.Value;
#endif
            Set(pRate, rate);
        }

        void ResolveViewAnimator()
        {
            var next = viewCopy != null ? viewCopy.ViewAnimator : null;
            if (ReferenceEquals(next, viewAnimator)) return;
            viewAnimator = next;
            viewHas.Clear();
            if (viewAnimator != null) foreach (var p in viewAnimator.parameters) viewHas.Add(p.nameHash);
        }

        void Trigger(int id) { if (has.Contains(id)) animator.SetTrigger(id); if (viewAnimator != null && viewHas.Contains(id)) viewAnimator.SetTrigger(id); }
        void Set(int id, int v) { if (has.Contains(id)) animator.SetInteger(id, v); if (viewAnimator != null && viewHas.Contains(id)) viewAnimator.SetInteger(id, v); }
        void Set(int id, float v) { if (has.Contains(id)) animator.SetFloat(id, v); if (viewAnimator != null && viewHas.Contains(id)) viewAnimator.SetFloat(id, v); }
        void Set(int id, bool v) { if (has.Contains(id)) animator.SetBool(id, v); if (viewAnimator != null && viewHas.Contains(id)) viewAnimator.SetBool(id, v); }
    }
}
