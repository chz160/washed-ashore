using System;
using UnityEngine;

namespace WashedAshore.Birds
{
    /// <summary>One ground bout (bird density brief 3.3): weight and duration of an action and its clip variants.</summary>
    [Serializable]
    public class RobinBout
    {
        public string name;
        [Range(0f, 1f)] public float weight;
        [Tooltip("Seconds; for Hop this is the number of hops (rounded).")]
        public Vector2 duration;
        public string[] states;
    }

    /// <summary>Ground robins (spec B3, B4; brief 3.3). Distances in m, speeds in m/s, times in s.</summary>
    [Serializable]
    public class RobinTuning
    {
        [Header("Ground bouts (B3); never the same clip twice in a row")]
        public RobinBout[] bouts =
        {
            new RobinBout { name = "PeckGround", weight = 0.35f, duration = new Vector2(1f, 3f), states = new[] { "A_PeckGround_01", "A_PeckGround_02", "A_PeckGround_03" } },
            new RobinBout { name = "Hop", weight = 0.25f, duration = new Vector2(1f, 3f), states = new[] { "A_Hop_01", "A_Hop_02", "A_Hop_03" } },
            new RobinBout { name = "Idle", weight = 0.20f, duration = new Vector2(1.5f, 4f), states = new[] { "A_Idle_01", "A_Idle_02", "A_Idle_03" } },
            new RobinBout { name = "Preen", weight = 0.10f, duration = new Vector2(2f, 5f), states = new[] { "A_Preen_01", "A_Preen_02", "A_Preen_03" } },
            new RobinBout { name = "ScratchGround", weight = 0.10f, duration = new Vector2(1f, 2f), states = new[] { "A_ScratchGround_01", "A_ScratchGround_02", "A_ScratchGround_03" } },
        };
        public string flutterState = "A_Flutter";
        public string flyState = "A_Fly_01";
        public float crossFadeSeconds = 0.15f;
        public Vector2 playbackSpeed = new Vector2(0.9f, 1.1f);
        [Tooltip("Robins this close are neighbours: never the clip a neighbour is playing (B3).")]
        public float neighbourRadius = 8f;
        public float hopLength = 0.3f;
        public float hopSeconds = 0.29f;
        [Tooltip("Hops stay within this distance of the robin's start point.")]
        public float hopLeash = 4f;
        public float turnSpeed = 240f;
        public float idleTurnDegrees = 70f;

        [Header("Habitat (brief 3.2 / A4)")]
        public float minGrass = 0.6f;
        public float maxSlope = 15f;
        [Tooltip("A4: no start, hop or landing within this distance of the route polyline.")]
        public float offTrail = 3f;
        public float landMinFromRoute = 12f;
        public Vector2 landTreeDistance = new Vector2(6f, 30f);
        public float landRockClear = 3f;

        [Header("Alert and flush (B4)")]
        public float alertDistance = 14f;
        [Tooltip("Flight-initiation distance, horizontal, player-triggered.")]
        public float flightInitiationDistance = 8f;
        [Tooltip("Patch-mates within this distance of a flushing robin flush too.")]
        public float socialRadius = 8f;
        public Vector2 socialDelay = new Vector2(0.2f, 0.8f);
        public int maxFlushes = 2;
        public Vector2 flutterSeconds = new Vector2(0.3f, 0.6f);
        public float flutterRise = 0.6f;
        public float flySpeed = 8f;
        public float climbAngleDegrees = 30f;
        [Tooltip("Cruise height above the ground; the path must also clear crowns.")]
        public Vector2 climbHeight = new Vector2(4f, 10f);
        public float headingJitterDegrees = 40f;
        public int headings = 8;
        public Vector2 landDistance = new Vector2(25f, 40f);
        public float landFlutterDistance = 1.5f;
        public float calmSeconds = 20f;
        [Tooltip("Path check: capsule cast radius from start to landing (brief 3.3).")]
        public float pathRadius = 0.3f;
        public float bodyCenterHeight = 0.1f;
        public float bodyRadius = 0.08f;
        [Tooltip("With no clear heading, fly away and despawn once this far from the player and out of view.")]
        public float despawnDistance = 60f;
        public float respawnSeconds = 30f;
        public float minRespawnDistance = 40f;
    }

    /// <summary>Overhead flocks (spec B5; brief 3.3).</summary>
    [Serializable]
    public class FlockTuning
    {
        public string flappingState = "Flapping";
        public string glidingState = "Gliding";
        public float crossFadeSeconds = 0.25f;
        [Tooltip("Minimum time on a wing clip before switching.")]
        public float minDwellSeconds = 1f;
        [Tooltip("Flapping when vertical speed is above this; Gliding otherwise.")]
        public float flapAboveClimbRate = 0.3f;
        [Tooltip("Crow scale (lead ruling, about 0.45 m long).")]
        public float scale = 1.35f;

        [Header("Altitude, m above the terrain under each bird (AGL)")]
        public Vector2 altitudeBand = new Vector2(25f, 50f); // R1
        public Vector2 orbitMeanAltitude = new Vector2(30f, 45f); // R1
        public Vector2 commuteAltitude = new Vector2(35f, 45f); // R1
        [Tooltip("Never less than this above the highest crown within crownSearchRadius.")]
        public float crownClearance = 10f;
        public float crownSearchRadius = 30f;
        public float waveAmplitude = 5f;
        public Vector2 wavePeriod = new Vector2(20f, 30f);
        [Tooltip("Height follows its target with this time constant (smooths terrain and crown changes).")]
        public float altitudeSmoothing = 1.5f;
        public float crownLookupInterval = 0.25f;

        [Header("Motion")]
        public Vector2 orbitRadius = new Vector2(35f, 55f); // R1
        public Vector2 orbitSpeed = new Vector2(9f, 12f);
        public float commuteSpeed = 12f;
        public Vector2 dwellSeconds = new Vector2(25f, 40f); // R1
        [Tooltip("Spacing target, centre to centre, horizontal (hard floor 1.5 m is the B5 gate).")]
        public float targetSpacing = 3f;
        [Tooltip("Every bird within this distance of the flock centroid.")]
        public float cohesionRadius = 15f;
        public Vector2 spreadPerBird = new Vector2(2.5f, 1.5f);
        public float verticalJitter = 0.5f;
        public float wobbleAmplitude = 0.3f;
        public float wobblePeriod = 4f;
        public float maxBankDegrees = 25f;
        public float turnSmoothing = 4f;
    }

    /// <summary>B6 bands and measurement (brief 3.4). Gate bands track the configured values (A1, A2).</summary>
    [Serializable]
    public class BirdSightingTargets
    {
        public float sampleInterval = 0.5f;
        public float walkSeconds = 180f;
        public float robinViewDistance = 30f;
        public float flockViewDistance = 200f;
        public int flockInViewMinBirds = 2;
        public int metConsecutiveSamples = 2;

        [Header("Gating bands")]
        public Vector2 flockInView = new Vector2(0.30f, 0.65f);
        public float maxEmptySkySeconds = 45f; // R1
        [Tooltip("R1: the first flock sighting (leading empty-sky gap) is gating.")]
        [UnityEngine.Serialization.FormerlySerializedAs("firstFlockSecondsReport")] public float firstFlockMaxSeconds = 30f;
        public Vector2 groundMetPerMinute = new Vector2(1.7f, 5.0f);
        public Vector2Int flushesPerWalk = new Vector2Int(3, 12);
        public float flushSeenMin = 0.40f;
        public Vector2Int maxInView = new Vector2Int(3, 20);
        public int busyCount = 15;
        public float busyShareMax = 0.05f;
        public Vector2 glideShare = new Vector2(0.40f, 0.75f);
        public float flapRuleMin = 0.95f;
        public float minSpacing = 1.5f;
        [Tooltip("A1: player-triggered flushes fire within FID - below / FID + above.")]
        public float fidBelow = 1f, fidAbove = 0.5f;
        [Tooltip("A2: every sample within [floor - below, ceiling + above] AGL, >= crown + crownMin, inside absolute.")]
        public float altitudeBelow = 5f, altitudeAbove = 10f, crownMin = 5f;
        public Vector2 altitudeAbsolute = new Vector2(20f, 90f);
    }

    [CreateAssetMenu(menuName = "Washed Ashore/Bird Tuning", fileName = "BirdTuning")]
    public class BirdTuning : ScriptableObject
    {
        public string playerTag = "Player";
        public string birdLayer = "Birds";
        [Tooltip("Bird-density-brief revision these numbers implement (recorded in B6 evidence).")]
        public string briefRevision = "final 2026-10-05 (A1-A4)";

        [Header("Spec outer bounds")]
        public Vector2Int robinTotal = new Vector2Int(4, 30);
        public Vector2Int flockCount = new Vector2Int(1, 4);
        public Vector2Int flockSize = new Vector2Int(3, 12);
        public int maxInViewCap = 20;

        public RobinTuning robin = new RobinTuning();
        public FlockTuning flock = new FlockTuning();
        public BirdSightingTargets sighting = new BirdSightingTargets();
    }
}
