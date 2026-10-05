using System;
using UnityEngine;

namespace WashedAshore.Wildlife
{
    public enum WildlifeSpecies { Deer, Stag, Fox, Wolf }

    public enum ThreatResponse { Flee, KeepDistance }

    /// <summary>Per-species behaviour (brief 3.3). Speeds are ground speed in m/s, distances in m.</summary>
    [Serializable]
    public class SpeciesTuning
    {
        public WildlifeSpecies species;
        public ThreatResponse response = ThreatResponse.Flee;

        [Header("Locomotion")]
        public float walkSpeed = 1.3f;
        [Tooltip("Flee speed, or the wolf's retreat trot.")]
        public float runSpeed = 9f;
        public float acceleration = 12f;
        public float angularSpeed = 300f;

        [Header("Idle / graze and wander")]
        public Vector2 idleSeconds = new Vector2(4f, 12f);
        [Range(0f, 1f)] public float grazeChance = 0.6f;
        public Vector2 wanderLeg = new Vector2(5f, 20f);
        [Tooltip("Wander targets stay within this distance of the group anchor.")]
        public float leashRadius = 30f;
        [Tooltip("Members stay within this distance of the group centroid while calm (A3).")]
        public float cohesionRadius = 12f;

        [Header("Player response")]
        [Tooltip("Stop, face the player, stop grazing. 0 = never.")]
        public float alertDistance = 50f;
        [Tooltip("Flee trigger (deer, fox) or keep-distance minimum (wolf).")]
        public float triggerDistance = 35f;
        [Tooltip("Flee or retreat ends once the player is at least this far away.")]
        public float releaseDistance = 70f;
        [Tooltip("Flee also ends after this long. 0 = no limit.")]
        public float maxFleeSeconds = 10f;
        [Tooltip("Idle time after a flee before wandering again.")]
        public float calmDownSeconds = 6f;
        public float fleeJitterDegrees = 30f;
        public float fleeLegDistance = 25f;
    }

    /// <summary>One population line (brief 3.1): groupCount groups of groupSize.</summary>
    [Serializable]
    public class GroupSpec
    {
        public WildlifeSpecies species;
        public int groupCount = 1;
        public int groupSize = 1;
    }

    /// <summary>Habitat placement rules (brief 3.2).</summary>
    [Serializable]
    public class PlacementRules
    {
        public float navMeshSnap = 2f;
        public float minFromSpawn = 40f;
        public float minFromEdge = 30f;
        public Vector2 terrainCentre = new Vector2(256f, 256f);
        public float maxFromCentre = 170f;
        public float minBetweenAnchors = 50f;
        public float maxFromRoute = 60f;
        [Tooltip("Retune R1: anchors sit at least this far from the route (none on the trail itself).")]
        public float minFromRoute = 0f;
        public int attemptsPerAnchor = 200;
        [Tooltip("Retune R1-A: arc-assignment draws before a seed fails geometrically.")]
        public int maxAssignmentDraws = 20;

        [Header("Route coverage (retune R0)")]
        [Tooltip("The closed route is split into this many equal arcs from W0; each group anchor takes its own arc.")]
        public int routeArcs = 6;
        [Tooltip("0-based arcs the wolf pair may not take (the two touching W0).")]
        public int[] wolfForbiddenArcs = { 0, 5 };

        [Header("Arc adjacency and approach sightline (retune R1; off until released)")]
        [Tooltip("The two deer arcs may not be neighbours (cyclic).")]
        public bool deerArcsNotAdjacent;
        [Tooltip("The stag arc may not neighbour either deer arc (cyclic).")]
        public bool stagNotAdjacentToDeer;
        [Tooltip("Reject anchors with no clear eye-height ray from the route 40-80 m before them.")]
        public bool approachSightline;
        public Vector2 sightlineBack = new Vector2(40f, 80f);
        public float sightlineStep = 5f;
        public float sightlineEyeHeight = 1.65f;
        public float sightlineTargetHeight = 1f;

        [Header("Deer")]
        public float deerMinGrass = 0.6f;
        public float deerMaxSlope = 20f;
        public float deerMaxFromTree = 40f;
        public float deerHerdsMinApart = 100f;

        [Header("Stag")]
        public float stagMaxSlope = 25f;
        public float stagMinFromDeer = 60f;

        [Header("Fox")]
        public float foxMaxFromCover = 25f;
        public float foxMaxSlope = 25f;
        public string[] foxCoverPrefixes = { "Bush_Common" };

        [Header("Wolf")]
        public float wolfMinFromSpawn = 100f;
        public float wolfMinFromDeer = 60f;
        public float wolfMaxSlope = 30f;
    }

    /// <summary>A6 sighting-walk definition and pass bands (brief 3.4, 3.5).</summary>
    [Serializable]
    public class SightingTargets
    {
        public int[] seeds = { 101, 202, 303 };
        public float sampleInterval = 0.5f;
        public float walkSeconds = 180f;
        public float startDelayAfterGrounded = 1f;
        public float viewDistance = 80f;
        public float walkSpeed = 5f;
        public float scanAmplitudeDegrees = 25f;
        public float scanPeriodSeconds = 10f;
        [Tooltip("Route waypoints as (dx, dz) offsets from PlayerSpawn; closed loop, resumes at W1.")]
        public Vector2[] routeOffsets = Array.Empty<Vector2>();

        [Header("Pass bands")]
        public float sightingRateMin = 0.25f;
        public float sightingRateMax = 0.55f;
        public float maxGapSeconds = 45f;
        public float maxFirstSightingSeconds = 30f;
        public int maxVisibleMin = 3;
        public int maxVisibleMax = 8;
        public int busyCount = 5;
        public float busySampleShareMax = 0.10f;
        public int perSpeciesMinConsecutive = 2;
    }

    [CreateAssetMenu(menuName = "Washed Ashore/Wildlife Tuning", fileName = "WildlifeTuning")]
    public class WildlifeTuning : ScriptableObject
    {
        public string playerTag = "Player";
        [Tooltip("Seed for the authored scene and builds (brief 3.2: 101). Tests override it.")]
        public int defaultSeed = 101;
        [Tooltip("Density-brief revision these numbers implement (recorded in A6 evidence).")]
        public string briefRevision = "R0";

        [Header("Animator")]
        public float speedDampTime = 0.1f;
        [Tooltip("Blend-tree speed of the Walk clip.")]
        public float walkClipSpeed = 1.5f;
        [Tooltip("Blend-tree speed of the Gallop clip; faster agents play it faster so feet don't slide.")]
        public float gallopClipSpeed = 6f;

        [Header("Grounding and recovery")]
        [Range(0f, 1f)] public float groundAlign = 0.6f;
        public float repathAfterStuckSeconds = 1.5f;
        public float threatCheckInterval = 0.1f;

        public SpeciesTuning[] species = Array.Empty<SpeciesTuning>();
        public GroupSpec[] population = Array.Empty<GroupSpec>();
        public PlacementRules placement = new PlacementRules();
        public SightingTargets sighting = new SightingTargets();

        public SpeciesTuning Get(WildlifeSpecies s)
        {
            foreach (var t in species)
                if (t.species == s) return t;
            throw new ArgumentException($"WildlifeTuning has no entry for {s}", nameof(s));
        }
    }
}
