using UnityEngine;

namespace WashedAshore.Gameplay
{
    /// <summary>
    /// Stair and climb-area numbers (tower-design-spec.md §8; split by assembly in tower-td-note §6 condition 12).
    /// A <see cref="ClimbArea"/> references one asset; tests read their targets from it. Ranges are the spec's.
    /// </summary>
    [CreateAssetMenu(fileName = "ClimbTuning", menuName = "Washed Ashore/Climb Tuning")]
    public class ClimbTuning : ScriptableObject
    {
        [Header("Retune levers")]
        [Tooltip("Speed up a flight, along the slope, m/s. The range is the 60-120 s climb band.")]
        [Range(0.30f, 0.54f)] public float stairAscendSpeed = 0.45f;
        [Tooltip("Speed down a flight, along the slope, m/s.")]
        [Range(0.30f, 0.80f)] public float stairDescendSpeed = 0.55f;
        [Tooltip("Sprint on a flight. 1 = sprint ignored.")]
        [Range(1f, 1.3f)] public float stairSprintMultiplier = 1f;
        [Tooltip("Walk speed inside the climb area off the flights (landings, cab), m/s. Sprint has no effect there.")]
        [Range(1f, 3f)] public float towerWalkSpeed = 1.6f;
        [Tooltip("Walking into a flight's end joins it when this much of the input points into it (0-1). 0.5 = within 60 degrees.")]
        [Range(0.3f, 0.8f)] public float laneJoinAlong = 0.5f;
        [Tooltip("At a junction of two lanes the player carries on onto the next lane when this much of the input points along it (0-1). 0.82 = within 35 degrees; the floor, 0.766, is the director's 40 degree ceiling. Provisional until the lane-end ruling is issued.")]
        [Range(JunctionAlongFloor, 0.97f)] public float laneJunctionAlong = 0.82f;
        [Tooltip("Width of the mouth at a flight's end, as a multiple of that flight's own width (its stringer spacing, from the model).")]
        [Range(1f, 1.5f)] public float laneMouthWidthFactor = 1f;

        [Header("Lanes")]
        [Tooltip("Acceleration along a flight, m/s^2.")]
        [Range(2f, 10f)] public float stairAcceleration = 4f;
        [Tooltip("Sideways play either side of a flight's centre line, m.")]
        [Range(0f, 0.05f)] public float laneLateralSlack = 0.05f;
        [Tooltip("Input along the flight below this does nothing (0-1).")]
        [Range(0.1f, 0.3f)] public float laneAlongDeadband = 0.2f;
        [Tooltip("Input along the flight from this up is full speed (0-1).")]
        [Range(0.3f, 0.8f)] public float laneFullSpeedAlong = 0.5f;
        [Tooltip("On a flight the sideways slack tapers to zero over this length before each end, m, so the player is on the line at every lane end and junction. Provisional until the lane-end ruling is issued.")]
        [Range(0.2f, 0.5f)] public float laneTaperLength = 0.30f;
        [Tooltip("On a final level stretch the sideways offset closes no faster than the tangent of this per metre walked toward the flight, degrees. 40 is the director's ceiling for being carried off the held heading.")]
        [Range(10f, 40f)] public float laneGlideMaxDegrees = 40f;
        [Tooltip("Length of the level run beyond each free flight end, m: the lane hands over at its far end, with the whole capsule on the deck. Not a retune lever. Provisional until the lane-end ruling is issued.")]
        [Range(0.30f, 0.45f)] public float laneRunOutLength = 0.34f;
        [Tooltip("Feet within this of a flight end's height can join it, m.")]
        [Range(0.1f, 0.3f)] public float laneMouthHeight = 0.20f;
        [Tooltip("A falling player joins a flight when the feet are within this below its nosing line, m.")]
        [Range(0.2f, 0.5f)] public float laneCatchBand = 0.30f;
        [Tooltip("The flight just left can't be rejoined until the player has moved this far or released and re-pressed, m.")]
        [Range(0.05f, 0.3f)] public float laneRejoinDistance = 0.10f;

        [Header("Capsule and jump")]
        [Tooltip("Capsule radius inside the climb area, m. Must fit the smallest landing and keep the eye 0.20 m off every surface.")]
        [Range(0.22f, 0.28f)] public float towerCapsuleRadius = 0.25f;
        [Tooltip("The authored capsule is tried this far above the feet when growing back, m, so level ground under it doesn't count as an obstacle.")]
        [Range(0.02f, 0.15f)] public float capsuleFitLift = 0.05f;
        [Tooltip("Outside every climb area the authored radius must be back within this long, s. Longer is logged as an error.")]
        [Range(0.5f, 5f)] public float capsuleRestoreSeconds = 1f;

        /// <summary>ruling/lookout-tower-upper-landing-step-on-amend3: carry-through at a junction is never wider than 40 degrees (cos 40).</summary>
        public const float JunctionAlongFloor = 0.766f;
        /// <summary>A level run is longer than the capsule (radius + skin) by at least this, m, so the hand-over point is clear of the lip.</summary>
        public const float RunOutMargin = 0.03f;

        /// <summary>Rules for this asset with a controller's authored radius, skin and walk speed; null when fine.</summary>
        public string Validate(float authoredRadius, float walkSpeed, float skinWidth = 0.05f)
        {
            if (laneJunctionAlong < JunctionAlongFloor) return $"laneJunctionAlong {laneJunctionAlong} must be >= {JunctionAlongFloor} (40 degrees)";
            if (laneRunOutLength < towerCapsuleRadius + skinWidth + RunOutMargin)
                return $"laneRunOutLength {laneRunOutLength} must be >= towerCapsuleRadius {towerCapsuleRadius} + skin {skinWidth} + {RunOutMargin}";
            if (laneFullSpeedAlong <= laneAlongDeadband) return $"laneFullSpeedAlong {laneFullSpeedAlong} must be above laneAlongDeadband {laneAlongDeadband}";
            if (towerCapsuleRadius > authoredRadius) return $"towerCapsuleRadius {towerCapsuleRadius} must not exceed the controller's radius {authoredRadius}";
            if (towerWalkSpeed > walkSpeed) return $"towerWalkSpeed {towerWalkSpeed} must not exceed walk speed {walkSpeed}";
            if (stairAscendSpeed * stairSprintMultiplier > towerWalkSpeed) return "sprinting up a flight must not be faster than walking a landing";
            if (laneJunctionAlong < laneJoinAlong) return $"laneJunctionAlong {laneJunctionAlong} must be >= laneJoinAlong {laneJoinAlong}";
            if (laneJoinAlong < laneAlongDeadband) return $"laneJoinAlong {laneJoinAlong} must be >= laneAlongDeadband {laneAlongDeadband}";
            if (laneLateralSlack * 2f >= towerCapsuleRadius) return $"laneLateralSlack {laneLateralSlack} is too wide for the capsule";
            return null;
        }

        void OnValidate()
        {
            // Defaults of the live controller (World.unity): radius 0.4, walk 5. Tests re-check with the scene's values.
            string problem = Validate(0.4f, 5f);
            if (problem != null) Debug.LogWarning($"ClimbTuning '{name}': {problem}", this);
        }
    }
}
