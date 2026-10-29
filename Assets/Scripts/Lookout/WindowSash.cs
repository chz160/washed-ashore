using UnityEngine;
using WashedAshore.Gameplay;
using WashedAshore.Interaction;
using WashedAshore.World;

namespace WashedAshore.Lookout
{
    /// <summary>
    /// A hinged cab sash (spec I4, tower-design-spec.md §6): top-hinged, the bottom swings outward. Opening only
    /// rotates the sash's own transform, so its panes, missing panes and shards are the same objects open and
    /// closed. It never moves by itself, and the cab's boundary walls don't change with it.
    /// </summary>
    public class WindowSash : MonoBehaviour, IInteractable, IMovingTransforms
    {
        [SerializeField] string id = "lookout.sash";
        [SerializeField] LookoutTuning tuning;
        [SerializeField] Hinge hinge = new Hinge();

        Bounds swing;
        bool swingKnown, swingTried, collisionMoving;

        public string Id => id;
        public HingeState State => hinge.State;
        /// <summary>Hinge angle, degrees from closed.</summary>
        public float Angle => hinge.Angle;
        public LookoutTuning Tuning => tuning;

        public System.Collections.Generic.IEnumerable<Transform> MovedTransforms { get { yield return hinge.Leaf; } }
        public System.Collections.Generic.IEnumerable<Collider> SwitchedOffWhileMoving => hinge.Colliders;

        public string Verb => hinge.State == HingeState.Open ? tuning.closeVerb : tuning.openVerb;
        public string Noun => tuning.sashNoun;

        /// <summary>Wires everything (build script and tests).</summary>
        public void Configure(string stableId, LookoutTuning lookoutTuning, Transform leaf, Vector3 hingeAxis)
        {
            id = stableId;
            tuning = lookoutTuning;
            hinge.Configure(leaf, hingeAxis);
        }

        void Awake()
        {
            hinge.Capture();
            Settle();
        }

        /// <summary>At rest, and the player's capsule is not in the volume the leaf sweeps (the leaf yields, never the player).</summary>
        public bool CanInteract(Interactor by) => hinge.AtRest && !PlayerInTheSwing();

        bool PlayerInTheSwing()
        {
            var players = PlayerRegistry.All;
            if (players.Count == 0) return false;
            if (!swingTried)
            {
                swingTried = true;
                swingKnown = hinge.TrySweptBounds(tuning.sashOpenAngle, out swing);
            }
            if (!swingKnown) return false;
            for (int i = 0; i < players.Count; i++)
            {
                var player = players[i];
                if (!PlayerRegistry.IsLive(player) || !player.TryGetComponent(out CharacterController playerCapsule)) continue;
                float r = playerCapsule.radius + playerCapsule.skinWidth;
                var capsule = new Bounds(player.transform.TransformPoint(playerCapsule.center), new Vector3(2f * r, playerCapsule.height, 2f * r));
                if (swing.Intersects(capsule)) return true;
            }
            return false;
        }

        /// <summary>The colliders are on only at rest; they are off for the whole swing, so the leaf never pushes anything.</summary>
        void Settle()
        {
            bool moving = hinge.Moving;
            hinge.SetCollidersEnabled(!moving);
            // autoSyncTransforms is off: once per settle, never per frame.
            if (collisionMoving && !moving) Physics.SyncTransforms();
            collisionMoving = moving;
        }

        public void Interact(Interactor by)
        {
            if (!hinge.AtRest) return;
            SetState(hinge.State == HingeState.Closed ? HingeState.Opening : HingeState.Closing, 0f);
        }

        /// <summary>The one way state changes (input, tests, later replication): state plus seconds in it.</summary>
        public void SetState(HingeState state, float elapsed)
        {
            hinge.Apply(state, elapsed);
            hinge.Pose(tuning.sashOpenAngle, tuning.sashSeconds, tuning.hatchCurve);
            Settle();
        }

        void Update()
        {
            if (hinge.AtRest) return;
            hinge.Tick(Time.deltaTime, tuning.sashOpenAngle, tuning.sashSeconds, tuning.hatchCurve);
            if (hinge.AtRest) Settle();
        }
    }
}
