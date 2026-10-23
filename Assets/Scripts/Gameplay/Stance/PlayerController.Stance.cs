using System;
using UnityEngine;
using UnityEngine.InputSystem;
using WashedAshore.Interaction;
using WashedAshore.World;

namespace WashedAshore.Gameplay
{
    /// <summary>
    /// Stances (player-stances.md): stand, crouch, squat, sit, sleep. The stance is the committed target; the eye and
    /// capsule follow it. Going down the capsule follows the eye every frame (height = eye + 0.15). Going up the
    /// headroom is checked first, the capsule grows, then the eye eases. A stand request that has no room is held
    /// until another stance key cancels it. Lowered stances are blocked in water, on the tower and in climb areas.
    /// The camera pivot is written here only while a stance other than Stand is held or changing, and never while the
    /// view is overridden (the hatch duck and the firefinder own it then).
    /// </summary>
    public partial class PlayerController
    {
        [Header("Stances")]
        [SerializeField] StanceTuning stanceTuning = new StanceTuning();
        [SerializeField] LookDownTuning lookDownTuning = new LookDownTuning();
        Stance trFromStance;

        const float StandHeightSlack = 0.01f;

        InputAction[] stanceActions;
        Stance stance, stancePrevious = Stance.Stand;
        Stance? stancePending;
        StanceKey injectedKey;
        float authoredStandHeight, standingHeight;
        bool walkCapsuleRestricted, stanceTransitioning, stanceFalling, stanceLie, sleepLock;
        float trFrom, trTo, trElapsed, trDuration, trPitchFrom, trPitchTo;
        float stanceEye;
        float sleepDim;
        int stanceJumpConsumedFrame = -1, stanceResumeFrame = -1;
        readonly Collider[] stanceBuffer = new Collider[16];

        public Stance Stance => stance;
        public bool StanceTransitioning => stanceTransitioning;
        public StanceTuning StanceTuning => stanceTuning;
        public LookDownTuning LookDownTuning => lookDownTuning;
        /// <summary>The stance the current change started from (the view offset blends from it).</summary>
        public Stance StanceFrom => trFromStance;
        /// <summary>0 to 1 along the stance change, the same ease as the eye; 1 when settled.</summary>
        public float StanceBlend => stanceTransitioning ? Smooth(trElapsed / trDuration) : 1f;
        /// <summary>The eye height above the feet the stance code wants now (the authored eye while standing).</summary>
        public float StanceEye => StanceWritesPivot ? stanceEye : AuthoredEyeHeight;
        /// <summary>The capsule height a stand-up returns to: 1.8, or the tower's 1.6 while a zone has slimmed it.</summary>
        public float StandingHeight => standingHeight;
        /// <summary>0 to 1: how far the sleep dim is in. A Volume reads this; nothing else shows it.</summary>
        public float SleepDim => sleepDim;
        /// <summary>Horizontal speed limit set by the stance; negative = none (standing).</summary>
        public float StanceSpeedLimit => stance == Stance.Stand ? -1f : stance == Stance.Crouch ? stanceTuning.crouchSpeed : 0f;
        /// <summary>Look is off from the start of the lie-down until the wake ends.</summary>
        public bool StanceLookLocked => sleepLock;
        public event Action<Stance, Stance> StanceChanged;

        bool StanceWritesPivot => cameraPivot != null && (stance != Stance.Stand || stanceTransitioning);
        float StandingEye => cameraPivot != null ? authoredPivotPosition.y : 1.65f;

        void OnValidate() => stanceTuning?.Clamp();

        void SetupStance()
        {
            authoredStandHeight = standingHeight = controller.height;
            stanceEye = StandingEye;
            stanceActions = new InputAction[StanceBindings.All.Length];
            for (int i = 0; i < stanceActions.Length; i++) stanceActions[i] = StanceBindings.Create(StanceBindings.All[i]);
        }

        void EnableStance() { foreach (var a in stanceActions) a.Enable(); }
        void DisableStance() { foreach (var a in stanceActions) a.Disable(); }
        void DisposeStance() { foreach (var a in stanceActions) a.Dispose(); }

        /// <summary>Presses a stance key as if the player had (tests, the debug tools). Consumed on the next frame.</summary>
        public void PressStanceKey(StanceKey key) => injectedKey = key;

        /// <summary>Back to Stand with the standing capsule and eye, no checks (respawn).</summary>
        void ResetStance()
        {
            stance = Stance.Stand;
            stancePrevious = Stance.Stand;
            trFromStance = Stance.Stand;
            stancePending = null;
            stanceTransitioning = false;
            stanceLie = false;
            sleepLock = false;
            sleepDim = 0f;
            SetStanceCapsule(standingHeight);
            stanceEye = StandingEye;
            if (cameraPivot != null && !viewPoseOverridden && !eyeHeightOverridden) cameraPivot.localPosition = authoredPivotPosition;
            if (interactor != null) interactor.enabled = true;
        }

        /// <summary>A place slimmed the walk capsule (the tower): any lowered stance ends at once, without a headroom test.</summary>
        void OnWalkCapsuleSet(float height)
        {
            standingHeight = height;
            walkCapsuleRestricted = height < authoredStandHeight - StandHeightSlack;
            if (stance == Stance.Stand && !stanceTransitioning) return;
            Stance old = stance;
            stancePending = null;
            Commit(Stance.Stand, old, true);
        }

        void StanceTick(float dt)
        {
            bool inputIgnored = controlSuspended || viewPoseOverridden || eyeHeightOverridden || stanceResumeFrame == Time.frameCount;
            bool lockedByTransition = stanceTransitioning && sleepLock;
            if (!inputIgnored && !lockedByTransition) StanceInput();
            if (stancePending.HasValue && !inputIgnored) TryGo(stancePending.Value, false);
            EnforceBlocks();
            AdvanceTransition(dt);
            AdvanceDim(dt);
        }

        void StanceInput()
        {
            StanceKey key = injectedKey;
            injectedKey = StanceKey.None;
            for (int i = 0; i < stanceActions.Length && key == StanceKey.None; i++)
                if (stanceActions[i].WasPressedThisFrame()) key = StanceBindings.All[i].Key;
            if (key != StanceKey.None) stancePending = null;

            Vector2 move = Vector2.ClampMagnitude(moveAction.ReadValue<Vector2>(), 1f);
            bool moving = move.sqrMagnitude > stanceTuning.moveInputSqr;
            bool jump = JumpPressed();
            bool sprint = moving && sprintAction.IsPressed();
            bool still = new Vector2(horizontalVelocity.x, horizontalVelocity.z).magnitude <= stanceTuning.stillSpeed;
            var d = StanceRules.Resolve(stance, stancePrevious, key, moving, jump, sprint, still);
            if (jump && stance != Stance.Stand) stanceJumpConsumedFrame = Time.frameCount;
            if (d.Change) TryGo(d.Target, true);
        }

        bool WaterBlocks => waterMode != WaterMode.Dry;
        bool ZoneBlocks => walkCapsuleRestricted || climbArea != null || WalkSpeedCap > 0f;

        /// <summary>A lowered stance held where it is not allowed ends: at once in water, when the capsule fits elsewhere.</summary>
        void EnforceBlocks()
        {
            if (stance == Stance.Stand) return;
            if (WaterBlocks) { stancePending = null; Commit(Stance.Stand, stance, true); }
            else if (ZoneBlocks) TryGo(Stance.Stand, false);
        }

        void TryGo(Stance target, bool fromKey)
        {
            if (target == stance) { stancePending = null; return; }
            bool lowering = CapOf(target) < CapOf(stance) - 1e-4f || EyeOf(target) < EyeOf(stance) - 1e-4f;
            if (lowering)
            {
                stancePending = null;
                if (target != Stance.Stand && !EntryAllowed(target)) return;
                Commit(target, stance, false);
                return;
            }
            // Going up: the capsule has to fit first. No room: hold the request.
            if (!HasHeadroom(CapOf(target))) { stancePending = target; return; }
            stancePending = null;
            Commit(target, stance, false);
        }

        bool EntryAllowed(Stance target) => EntryBlockedBy(target) == null;

        /// <summary>Why entering <paramref name="target"/> is refused right now, or null when it is allowed (tests and the debug tools read this).</summary>
        public string EntryBlockedBy(Stance target)
        {
            if (WaterBlocks) return "water";
            if (ZoneBlocks) return "tower or climb zone";
            if (!controller.isGrounded) return "not grounded";
            if (target == Stance.Sit && !SitSurfaceOk()) return "sit surface";
            if (target == Stance.Sleep && !SleepSurfaceOk()) return "sleep surface: " + sleepWhy;
            return null;
        }

        float EyeOf(Stance s) => stanceTuning.Eye(s, StandingEye);

        float CapOf(Stance s) => s == Stance.Stand
            ? standingHeight
            : Mathf.Max(EyeOf(s) + stanceTuning.eyeToCapsuleTop, 2f * controller.radius);

        void Commit(Stance target, Stance from, bool immediateCapsule)
        {
            if (target == Stance.Squat && (from == Stance.Stand || from == Stance.Crouch)) stancePrevious = from;
            else if (target == Stance.Squat) stancePrevious = Stance.Stand;
            float fromEye = stanceTransitioning || from != Stance.Stand ? stanceEye : StandingEye;
            float toEye = EyeOf(target);
            bool lowering = toEye < fromEye - 1e-4f;
            trFromStance = from;
            stance = target;
            trFrom = fromEye;
            trTo = toEye;
            trElapsed = 0f;
            stanceFalling = lowering;
            stanceLie = target == Stance.Sleep;
            sleepLock = target == Stance.Sleep || from == Stance.Sleep;
            trDuration = Mathf.Max(0.01f, lowering ? stanceTuning.Enter(target) : stanceTuning.Exit(from));
            trPitchFrom = pitch;
            trPitchTo = pitch;
            if (target == Stance.Sleep) trPitchTo = stanceTuning.sleepPitch;
            else if (from == Stance.Sleep) trPitchTo = 0f;
            stanceTransitioning = true;
            // Going up the capsule grows now (the headroom was checked); going down it follows the eye each frame.
            if (!lowering || immediateCapsule) SetStanceCapsule(CapOf(target));
            if (interactor != null) interactor.enabled = target != Stance.Sit && target != Stance.Sleep;
            if (from != target) StanceChanged?.Invoke(from, target);
        }

        float SampleEye(float elapsed)
        {
            if (stanceLie && stanceFalling)
            {
                float mid = Mathf.Min(trFrom, stanceTuning.sitEye);
                float seg = Mathf.Min(stanceTuning.sleepFirstSegment, trDuration * 0.9f);
                if (elapsed < seg) return Mathf.Lerp(trFrom, mid, Smooth(elapsed / seg));
                return Mathf.Lerp(mid, trTo, Smooth((elapsed - seg) / (trDuration - seg)));
            }
            return Mathf.Lerp(trFrom, trTo, Smooth(elapsed / trDuration));
        }

        static float Smooth(float t) => Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));

        void AdvanceTransition(float dt)
        {
            if (!stanceTransitioning) return;
            trElapsed += dt;
            bool done = trElapsed >= trDuration;
            stanceEye = done ? trTo : SampleEye(trElapsed);
            if (stanceFalling) SetStanceCapsule(Mathf.Max(stanceEye + stanceTuning.eyeToCapsuleTop, 2f * controller.radius));
            if (trPitchFrom != trPitchTo)
            {
                pitch = Mathf.Lerp(trPitchFrom, trPitchTo, Smooth(trElapsed / trDuration));
                if (cameraPivot != null && !viewPoseOverridden) cameraPivot.localRotation = Quaternion.Euler(pitch, 0f, 0f);
            }
            if (done)
            {
                stanceTransitioning = false;
                stanceFalling = false;
                if (stance == Stance.Stand) { SetStanceCapsule(standingHeight); stanceEye = StandingEye; }
                else SetStanceCapsule(CapOf(stance));
                stanceLie = stance == Stance.Sleep;
                sleepLock = stance == Stance.Sleep;
            }
            if (cameraPivot != null && !viewPoseOverridden && !eyeHeightOverridden)
                cameraPivot.localPosition = new Vector3(authoredPivotPosition.x, stanceEye, authoredPivotPosition.z);
        }

        void AdvanceDim(float dt)
        {
            if (stance == Stance.Sleep && !stanceTransitioning)
                sleepDim = Mathf.Min(1f, sleepDim + dt / Mathf.Max(0.01f, stanceTuning.dimSeconds));
            else
                sleepDim = Mathf.Max(0f, sleepDim - dt / Mathf.Max(0.01f, stanceTuning.wakeUndimSeconds));
        }

        /// <summary>Capsule height with the feet fixed (same as SetWalkCapsule, radius untouched).</summary>
        void SetStanceCapsule(float height)
        {
            if (Mathf.Abs(controller.height - height) < 1e-4f) return;
            float bottom = controller.center.y - controller.height * 0.5f;
            controller.height = height;
            controller.center = new Vector3(controller.center.x, bottom + height * 0.5f, controller.center.z);
        }

        int StanceMask => WorldLayers.StanceMask;

        bool IsOwn(Collider c) => c.transform.IsChildOf(transform);

        string lastBlocker;

        bool AnyBlocking(int count)
        {
            for (int i = 0; i < count; i++)
                if (!IsOwn(stanceBuffer[i])) { lastBlocker = stanceBuffer[i].name; return true; }
            return false;
        }

        /// <summary>True when a capsule of this height, feet where they are, overlaps nothing.</summary>
        bool HasHeadroom(float height)
        {
            float r = controller.radius;
            Vector3 feet = transform.position + controller.center - Vector3.up * (controller.height * 0.5f);
            float skin = controller.skinWidth;
            Vector3 p1 = feet + Vector3.up * (r + skin);
            Vector3 p2 = feet + Vector3.up * Mathf.Max(height - r, r + skin);
            int n = Physics.OverlapCapsuleNonAlloc(p1, p2, r - skin * 0.4f, stanceBuffer, StanceMask, QueryTriggerInteraction.Ignore);
            return !AnyBlocking(n);
        }

        readonly RaycastHit[] downHits = new RaycastHit[8];

        /// <summary>Nearest ground hit straight down, ignoring the player's own colliders (the capsule top is under the ray when sitting).</summary>
        bool DownCast(Vector3 origin, float distance, out RaycastHit best)
        {
            int n = Physics.RaycastNonAlloc(origin, Vector3.down, downHits, distance, StanceMask, QueryTriggerInteraction.Ignore);
            best = default;
            float nearest = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                if (IsOwn(downHits[i].collider) || downHits[i].distance >= nearest) continue;
                nearest = downHits[i].distance;
                best = downHits[i];
            }
            return nearest < float.MaxValue;
        }

        float GroundNormalAngle()
        {
            Vector3 o = transform.position + Vector3.up * 0.3f;
            return DownCast(o, 1.2f, out RaycastHit hit)
                ? Vector3.Angle(hit.normal, Vector3.up) : 90f;
        }

        /// <summary>A box lying ahead of the axis: length from the axis, width, height band above the player's feet.</summary>
        bool BoxClear(float width, float length, Vector2 band)
        {
            Vector3 centre = transform.position + transform.forward * (length * 0.5f) + Vector3.up * ((band.x + band.y) * 0.5f);
            var half = new Vector3(width * 0.5f, (band.y - band.x) * 0.5f, length * 0.5f);
            int n = Physics.OverlapBoxNonAlloc(centre, half, stanceBuffer, transform.rotation, StanceMask, QueryTriggerInteraction.Ignore);
            return !AnyBlocking(n);
        }

        bool SitSurfaceOk()
        {
            var t = stanceTuning;
            return GroundNormalAngle() <= t.sitMaxSlope && BoxClear(t.sitWidth, t.sitLength, t.sitBand);
        }

        string sleepWhy;

        bool SleepSurfaceOk()
        {
            var t = stanceTuning;
            float slope = GroundNormalAngle();
            if (slope > t.sleepMaxSlope) { sleepWhy = $"slope {slope:F1}"; return false; }
            float lo = float.MaxValue, hi = float.MinValue;
            var water = WaterBody.Active;
            const int samples = 5;
            for (int i = 0; i < samples; i++)
            {
                Vector3 p = transform.position + transform.forward * (t.sleepLength * i / (samples - 1)) + Vector3.up * 1f;
                if (!DownCast(p, 3f, out RaycastHit hit)) { sleepWhy = $"no ground at sample {i}"; return false; }
                lo = Mathf.Min(lo, hit.point.y);
                hi = Mathf.Max(hi, hit.point.y);
                if (water != null && water.SurfaceY(hit.point.x, hit.point.z) > hit.point.y) { sleepWhy = "water over the ground"; return false; }
            }
            if (hi - lo > t.sleepMaxGroundVariation) { sleepWhy = $"ground varies {hi - lo:F2}"; return false; }
            if (!BoxClear(t.sleepWidth, t.sleepLength, t.sleepBand)) { sleepWhy = "lie box blocked by " + lastBlocker; return false; }
            Vector3 eye = transform.position + Vector3.up * t.sleepEye;
            int n = Physics.OverlapSphereNonAlloc(eye, t.sleepEyeClearance, stanceBuffer, StanceMask, QueryTriggerInteraction.Ignore);
            if (AnyBlocking(n)) { sleepWhy = "eye sphere blocked by " + lastBlocker; return false; }
            return true;
        }
    }
}
