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
    /// swing volume (standing on it, or in the opening), so it can't trap, crush or launch. A prebuilt blocker fills
    /// the swing volume while it moves.
    /// It also lowers the eye under the floor on the top flight: a function of the feet's position on that flight only (§4 duck).
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
        [Tooltip("The top flight's foot and head (nosing line, frame space) and clear width: the duck's source, no lane needed.")]
        [SerializeField] Vector3 flightFoot, flightHead;
        [SerializeField] float flightWidth;
        [Tooltip("Prebuilt WorldBounds box under the closed leaf (top at the cab floor's underside): on only while the hatch is Closed.")]
        [SerializeField] Collider headroomStop;

        PlayerController player;
        CharacterController playerCapsule;
        float passEyeHeight;
        bool ducking, collisionMoving;

        public string Id => id;
        public HingeState State => hinge.State;
        /// <summary>Hinge angle, degrees from closed.</summary>
        public float Angle => hinge.Angle;
        public LookoutTuning Tuning => tuning;
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
            Collider blocker, Transform frameTransform, Vector3 openMin, Vector3 openMax,
            float lengthAlongRun, float thickness, float leafSweep,
            Vector3 topFlightFoot = default, Vector3 topFlightHead = default, float topFlightWidth = 0f, Collider headroom = null)
        {
            id = stableId;
            tuning = lookoutTuning;
            hinge.Configure(leaf, hingeAxis);
            leafCollider = leafSolid;
            swingBlocker = blocker;
            frame = frameTransform;
            openingMin = openMin;
            openingMax = openMax;
            openingLength = lengthAlongRun;
            floorThickness = thickness;
            leafWidth = leafSweep;
            flightFoot = topFlightFoot;
            flightHead = topFlightHead;
            flightWidth = topFlightWidth;
            headroomStop = headroom;
        }

        /// <summary>Headroom stop box (WorldBounds), or null.</summary>
        public Collider HeadroomStop => headroomStop;

        /// <summary>The top flight in frame space: the built data when given.</summary>
        bool TryFlight(out Vector3 foot, out Vector3 head, out float width)
        {
            if (flightWidth > 0f) { foot = flightFoot; head = flightHead; width = flightWidth; return true; }
            foot = head = Vector3.zero; width = 0f;
            return false;
        }

        static float PitchOf(Vector3 foot, Vector3 head)
        {
            Vector3 d = head - foot;
            return Mathf.Asin(Mathf.Clamp(d.normalized.y, -1f, 1f)) * Mathf.Rad2Deg;
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
        }

        /// <summary>Pose and collision for the current state. Collision changes only in these discrete steps.</summary>
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
            // The headroom stop fills the space under the closed leaf (keeps the head below the floor); it is on while Closed and while Closing (a player walking up during the close is
            // held at it, and at Closed it is never switched on around them: Interact refused a close that would touch their skin-grown capsule), off while Opening and Open.
            if (headroomStop != null) headroomStop.enabled = hinge.State == HingeState.Closed || hinge.State == HingeState.Closing;
            // autoSyncTransforms is off: once per settle, so a query in this frame sees the leaf at its rest pose (never per frame).
            if (collisionMoving && !moving) Physics.SyncTransforms();
            collisionMoving = moving;
        }

        /// <summary>
        /// §4 rule 1: any part of the capsule in the swing volume (the leaf's sweep over the opening, down to
        /// hatchSwingDepth below the floor's underside).
        /// Closing is also refused, silently, while the capsule grown by its skin on every side overlaps the headroom stop's volume: closing switches
        /// the stop on, and a stop switched on around a player would push them (director amend5, td part 54/55). Opening keeps the swing-box rule only:
        /// a player resting at the stop touches its volume through the skin and must still be able to open the hatch.
        /// </summary>
        bool PlayerInTheWay()
        {
            Vector3 feet = frame.InverseTransformPoint(player.transform.position);
            var min = new Vector3(openingMin.x, openingMin.y - floorThickness - tuning.hatchSwingDepth, openingMin.z);
            var max = new Vector3(openingMax.x, openingMin.y + leafWidth, openingMax.z);
            float skin = playerCapsule.skinWidth;
            float radius = playerCapsule.radius + skin;
            if (LookoutRules.CapsuleInBox(feet, radius, playerCapsule.height, min, max)) return true;
            if (hinge.State == HingeState.Open)
            {
                StopVolume(out Vector3 stopMin, out Vector3 stopMax);
                var grownFeet = new Vector3(feet.x, feet.y - skin, feet.z);
                if (LookoutRules.CapsuleInBox(grownFeet, radius, playerCapsule.height + 2f * skin, stopMin, stopMax)) return true;
            }
            return false;
        }

        /// <summary>The headroom stop's volume in frame space (from its box, so it is right while the stop is off); derived from the opening when there is no stop.</summary>
        void StopVolume(out Vector3 min, out Vector3 max)
        {
            if (headroomStop is BoxCollider box)
            {
                Transform t = box.transform;
                Vector3 centre = frame.InverseTransformPoint(t.TransformPoint(box.center));
                Vector3 half = Vector3.Scale(box.size, t.lossyScale) * 0.5f;
                half = new Vector3(Mathf.Abs(half.x), Mathf.Abs(half.y), Mathf.Abs(half.z));
                min = centre - half; max = centre + half;
                return;
            }
            float underside = openingMin.y - floorThickness;
            min = new Vector3(openingMin.x, underside - tuning.hatchStopHeadroom, openingMin.z);
            max = new Vector3(openingMax.x, underside, openingMax.z);
        }

        bool FindPlayer()
        {
            if (player != null) return true;
            var tagged = GameObject.FindGameObjectWithTag("Player");
            if (tagged == null || !tagged.TryGetComponent(out player) || !tagged.TryGetComponent(out playerCapsule)) { player = null; return false; }
            if (TryFlight(out Vector3 foot, out Vector3 head, out _))
                passEyeHeight = LookoutRules.PassEyeHeight(openingLength, PitchOf(foot, head), floorThickness, tuning.eyeClearance,
                    tuning.hatchDuckMinEyeHeight, player.AuthoredEyeHeight);
            else passEyeHeight = player.AuthoredEyeHeight;
            return true;
        }

        /// <summary>
        /// Eye height from the feet's position alone (§4 duck): on the top flight when the feet's projection on
        /// the foot-head segment is inside its width; the run is the horizontal distance to the head. Off the flight: standing.
        /// </summary>
        public float EyeHeightAtFeet(Vector3 worldFeet)
        {
            float standing = player != null ? player.AuthoredEyeHeight : 0f;
            if (!TryFlight(out Vector3 foot, out Vector3 head, out float width)) return standing;
            Vector3 p = frame.InverseTransformPoint(worldFeet) - foot;
            Vector3 axisH = new Vector3(head.x - foot.x, 0f, head.z - foot.z);
            float lengthH = axisH.magnitude;
            if (lengthH < 1e-4f) return standing;
            axisH /= lengthH;
            float along = Vector3.Dot(new Vector3(p.x, 0f, p.z), axisH);
            float lateral = Vector3.Dot(p, Vector3.Cross(Vector3.up, axisH));
            if (along < 0f || along > lengthH || Mathf.Abs(lateral) > width * 0.5f) return standing;
            return LookoutRules.HatchEyeHeight(lengthH - along, openingLength, PitchOf(foot, head), passEyeHeight, standing,
                tuning.hatchDuckRun, tuning.eyeClearance);
        }

        /// <summary>The duck: eye height from the position on the top flight, the same going up, down or standing still.</summary>
        void LateUpdate()
        {
            if (player == null && !FindPlayer()) return;   // the duck must not stay dormant until something aims at the hatch (td part 61, P3)
            float eye = EyeHeightAtFeet(player.transform.position);
            if (eye < player.AuthoredEyeHeight) { player.OverrideEyeHeight(eye); ducking = true; }
            else if (ducking) { player.ClearViewOverride(); ducking = false; }
        }
    }
}
