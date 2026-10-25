using UnityEngine;
using WashedAshore.Gameplay;
using WashedAshore.Interaction;
using WashedAshore.World;

namespace WashedAshore.Lookout
{
    /// <summary>
    /// The cab's trap door (spec I2, tower-design-spec.md §4, N13). Hinged and heavy; it only ever moves on a
    /// press. The leaf's collider is on only at rest (a floor when closed, flat against the wall when open, and the
    /// aim target in both); it is off for the whole swing, so nothing is ever pushed. It can't be used while the player's capsule is in the
    /// swing volume (standing on it, or in the opening), so it can't trap, crush or launch. While it is not fully
    /// open the top flight's lane is gated below it, and a prebuilt blocker fills the swing volume while it moves.
    /// It also lowers the eye under the floor on the top flight: a function of lane position only (§4 duck).
    /// </summary>
    public class TrapDoor : MonoBehaviour, IInteractable, IMovingTransforms
    {
        [SerializeField] string id = "lookout.hatch";
        [SerializeField] LookoutTuning tuning;
        [SerializeField] Hinge hinge = new Hinge();
        [Tooltip("The leaf's collider (floor when closed, aim target in both rest states): off while the leaf moves.")]
        [SerializeField] Collider leafCollider;
        [Tooltip("Prebuilt WorldBounds box filling the swing volume: on only while the leaf moves.")]
        [SerializeField] Collider swingBlocker;
        [SerializeField] ClimbArea climbArea;
        [Tooltip("The lane that comes up through this hatch.")]
        [SerializeField] int laneIndex;
        [Tooltip("The opening and floor are given in this transform's space (the tower root).")]
        [SerializeField] Transform frame;
        [Tooltip("Opening in the floor: min and max corners at floor-top height, frame space.")]
        [SerializeField] Vector3 openingMin, openingMax;
        [Tooltip("Clear length of the opening along the top flight's run, m.")]
        [SerializeField] float openingLength;
        [Tooltip("Floor thickness at the opening, m.")]
        [SerializeField] float floorThickness;
        [Tooltip("Hinge to free edge, m: how high the leaf sweeps.")]
        [SerializeField] float leafWidth;

        PlayerController player;
        CharacterController playerCapsule;
        float passEyeHeight;
        bool ducking, gateKnown, collisionMoving;

        public string Id => id;
        public HingeState State => hinge.State;
        /// <summary>Hinge angle, degrees from closed.</summary>
        public float Angle => hinge.Angle;
        public LookoutTuning Tuning => tuning;
        public int LaneIndex => laneIndex;
        /// <summary>The eye height used under the floor, from the built opening (§4).</summary>
        public float PassEyeHeight => passEyeHeight;
        /// <summary>Clear length of the opening along the top flight's run, m.</summary>
        public float OpeningLength => openingLength;
        public float FloorThickness => floorThickness;
        public Collider LeafCollider => leafCollider;

        public System.Collections.Generic.IEnumerable<Transform> MovedTransforms { get { yield return hinge.Leaf; } }

        /// <summary>Every collider under the leaf (the hatch proxy is adopted under it) plus the serialized leaf collider.</summary>
        public System.Collections.Generic.IEnumerable<Collider> SwitchedOffWhileMoving
        {
            get
            {
                foreach (var c in hinge.Colliders) yield return c;
                if (leafCollider != null) yield return leafCollider;
            }
        }
        /// <summary>The opening at floor-top height, world space (the tower stands upright).</summary>
        public Bounds OpeningBounds
        {
            get
            {
                var b = new Bounds(frame.TransformPoint(openingMin), Vector3.zero);
                b.Encapsulate(frame.TransformPoint(openingMax));
                return b;
            }
        }

        public string Verb => hinge.State == HingeState.Open ? tuning.closeVerb : tuning.openVerb;
        public string Noun => tuning.hatchNoun;

        /// <summary>Wires everything (build script and tests).</summary>
        public void Configure(string stableId, LookoutTuning lookoutTuning, Transform leaf, Vector3 hingeAxis, Collider leafSolid,
            Collider blocker, ClimbArea area, int lane, Transform frameTransform, Vector3 openMin, Vector3 openMax,
            float lengthAlongRun, float thickness, float leafSweep)
        {
            id = stableId;
            tuning = lookoutTuning;
            hinge.Configure(leaf, hingeAxis);
            leafCollider = leafSolid;
            swingBlocker = blocker;
            climbArea = area;
            laneIndex = lane;
            frame = frameTransform;
            openingMin = openMin;
            openingMax = openMax;
            openingLength = lengthAlongRun;
            floorThickness = thickness;
            leafWidth = leafSweep;
        }

        void Awake()
        {
            hinge.Capture();
            Settle();
        }

        void OnDisable()
        {
            if (ducking && player != null) player.ClearViewOverride();
            ducking = false;
        }

        public bool CanInteract(Interactor by) => hinge.AtRest && FindPlayer() && !PlayerInTheWay();

        public void Interact(Interactor by)
        {
            if (!CanInteract(by)) return;
            SetState(hinge.State == HingeState.Closed ? HingeState.Opening : HingeState.Closing, 0f);
        }

        /// <summary>The one way state changes (input, tests, later replication): state plus seconds in it.</summary>
        public void SetState(HingeState state, float elapsed)
        {
            hinge.Apply(state, elapsed);
            Settle();
        }

        void Update()
        {
            if (!hinge.AtRest)
            {
                hinge.Tick(Time.deltaTime, tuning.hatchOpenAngle, tuning.hatchSeconds, tuning.hatchCurve);
                if (hinge.AtRest) Settle();
            }
            else if (!gateKnown) RefreshGate(); // gate only: the leaf is not posed again at rest
        }

        /// <summary>Pose, collision and the lane gate for the current state. Collision changes only in these discrete steps.</summary>
        void Settle()
        {
            hinge.Pose(tuning.hatchOpenAngle, tuning.hatchSeconds, tuning.hatchCurve);
            // Start of a swing: blocker on, then every leaf collider off. End: leaf colliders on at the rest pose, then blocker off.
            // The blocker's volume contains both rest poses, so the leaf's collider never appears or vanishes around a player.
            bool moving = hinge.Moving;
            if (moving && swingBlocker != null) swingBlocker.enabled = true;
            hinge.SetCollidersEnabled(!moving);
            if (leafCollider != null) leafCollider.enabled = !moving;
            if (!moving && swingBlocker != null) swingBlocker.enabled = false;
            // autoSyncTransforms is off: once per settle, so a query in this frame sees the leaf at its rest pose (never per frame).
            if (collisionMoving && !moving) Physics.SyncTransforms();
            collisionMoving = moving;
            RefreshGate();
        }

        /// <summary>The lane gate for the current state (no pose, no collision change).</summary>
        void RefreshGate()
        {
            if (climbArea == null || laneIndex < 0 || laneIndex >= climbArea.LaneCount) return;
            // A lane linked at the top of the hatch's flight (the step off onto the cab floor) is open only with the hatch.
            if (climbArea.TryLink(laneIndex, ClimbRules.End.Head, out int exitLane, out _)) climbArea.SetBlocked(exitLane, hinge.State != HingeState.Open);
            if (hinge.State == HingeState.Open) { climbArea.SetGate(laneIndex, float.PositiveInfinity); gateKnown = true; return; }
            // Not fully open: the head stays hatchStopHeadroom below the floor's underside. Needs the capsule's height.
            gateKnown = FindPlayer();
            climbArea.SetGate(laneIndex, gateKnown ? GateDistance() : 0f);
        }

        float GateDistance() => ClimbRules.GateUnder(climbArea.Lane(laneIndex), UndersideY(), playerCapsule.height, tuning.hatchStopHeadroom);

        float UndersideY() => frame.TransformPoint(openingMin).y - floorThickness;

        /// <summary>
        /// §4 rule 1: any part of the capsule in the swing volume (the leaf's sweep over the opening, down to
        /// hatchSwingDepth below the floor's underside), or on the lane above where the shut hatch would stop it.
        /// </summary>
        bool PlayerInTheWay()
        {
            Vector3 feet = frame.InverseTransformPoint(player.transform.position);
            var min = new Vector3(openingMin.x, openingMin.y - floorThickness - tuning.hatchSwingDepth, openingMin.z);
            var max = new Vector3(openingMax.x, openingMin.y + leafWidth, openingMax.z);
            float radius = playerCapsule.radius + playerCapsule.skinWidth;
            if (LookoutRules.CapsuleInBox(feet, radius, playerCapsule.height, min, max)) return true;
            return player.OnLane && player.ClimbArea == climbArea && player.LaneIndex == laneIndex && player.LaneDistance > GateDistance();
        }

        bool FindPlayer()
        {
            if (player != null) return true;
            var tagged = GameObject.FindGameObjectWithTag("Player");
            if (tagged == null || !tagged.TryGetComponent(out player) || !tagged.TryGetComponent(out playerCapsule)) { player = null; return false; }
            ref readonly StairLane top = ref climbArea.Lane(laneIndex);
            passEyeHeight = LookoutRules.PassEyeHeight(openingLength, top.Pitch, floorThickness, tuning.eyeClearance,
                tuning.hatchDuckMinEyeHeight, player.AuthoredEyeHeight);
            return true;
        }

        /// <summary>Eye height above the feet at a lane parameter on the top lane (§4): position in, height out, nothing else.</summary>
        public float EyeHeightAt(float laneDistance)
        {
            ref readonly StairLane top = ref climbArea.Lane(laneIndex);
            float run = Mathf.Max(0f, top.Length - laneDistance) * top.RunPerMetre;
            return LookoutRules.HatchEyeHeight(run, openingLength, top.Pitch, passEyeHeight, player.AuthoredEyeHeight,
                tuning.hatchDuckRun, tuning.eyeClearance);
        }

        /// <summary>The duck: eye height from the position on the top lane, the same going up, down or standing still.</summary>
        void LateUpdate()
        {
            if (player == null || climbArea == null) return;
            bool onTopLane = player.OnLane && player.ClimbArea == climbArea && player.LaneIndex == laneIndex;
            float eye = player.AuthoredEyeHeight;
            if (onTopLane) eye = EyeHeightAt(player.LaneDistance);
            if (eye < player.AuthoredEyeHeight) { player.OverrideEyeHeight(eye); ducking = true; }
            else if (ducking) { player.ClearViewOverride(); ducking = false; }
        }
    }
}
