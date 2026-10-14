using UnityEngine;
using WashedAshore.Interaction;

namespace WashedAshore.Lookout
{
    /// <summary>
    /// A hinged cab sash (spec I4, tower-design-spec.md §6): top-hinged, the bottom swings outward. Opening only
    /// rotates the sash's own transform, so its panes, missing panes and shards are the same objects open and
    /// closed. It never moves by itself, and the cab's boundary walls don't change with it.
    /// </summary>
    public class WindowSash : MonoBehaviour, IInteractable
    {
        [SerializeField] string id = "lookout.sash";
        [SerializeField] LookoutTuning tuning;
        [SerializeField] Hinge hinge = new Hinge();

        public string Id => id;
        public HingeState State => hinge.State;
        /// <summary>Hinge angle, degrees from closed.</summary>
        public float Angle => hinge.Angle;
        public LookoutTuning Tuning => tuning;

        public string Verb => hinge.State == HingeState.Open ? tuning.closeVerb : tuning.openVerb;
        public string Noun => tuning.sashNoun;

        /// <summary>Wires everything (build script and tests).</summary>
        public void Configure(string stableId, LookoutTuning lookoutTuning, Transform leaf, Vector3 hingeAxis)
        {
            id = stableId;
            tuning = lookoutTuning;
            hinge.Configure(leaf, hingeAxis);
        }

        void Awake() => hinge.Capture();

        public bool CanInteract(Interactor by) => hinge.AtRest;

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
        }

        void Update()
        {
            if (!hinge.AtRest) hinge.Tick(Time.deltaTime, tuning.sashOpenAngle, tuning.sashSeconds, tuning.hatchCurve);
        }
    }
}
