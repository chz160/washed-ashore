using System;
using UnityEngine;

namespace WashedAshore.Fish
{
    /// <summary>How a species keeps company (brief group type).</summary>
    public enum FishGrouping : byte { Solitary, Loose, LooseSchool, School }

    /// <summary>Where a species holds in the water column (brief swimDepthMode).</summary>
    public enum FishDepthMode : byte { Mid, NearBed, NearSurface }

    /// <summary>Visibility tier (ruling/fish-visibility-tiers): V1 body near the surface, V2 shadow/flash only, V3 never a body.</summary>
    public enum FishTier : byte { V1, V2, V3 }

    /// <summary>
    /// One band (brief item 2), by bed depth only (ruling/fish-depth-bands). Structure is a tag with its own density
    /// multiplier, not a band.
    /// </summary>
    [Serializable]
    public class FishBand
    {
        public string name;
        [Tooltip("Bed depth below WaterLevelY, metres: [min, max).")]
        public Vector2 bedDepth;
        [Tooltip("Metres of band per metre of shoreline (shelf 7, ramp 13, open 30).")]
        public float nominalWidth = 7f;
        [Tooltip("Outer edge, metres from the bank (LevelMaps shore distance); 0 = none. Open: 50 (ruling/fish-depth-bands rev 4).")]
        public float maxShoreDistance;
        [Tooltip("Adult fish per 100 m of shoreline (F3 target, +/-25%). School units are counted separately.")]
        public float fishPer100m;
        [Tooltip("Surface events per minute per 100 m of shoreline (the band-rate surface signs).")]
        public float surfaceEventsPerMinPer100m;
        [Tooltip("Sign-only events per m² per minute in water of this band's depth beyond its outer edge (brief farWaterSignsPerM2PerMin; 0 = none).")]
        public float farSignsPerM2PerMin;

        /// <summary>Adults per square metre of this band outside structure: the plan and the F3 census both use this.</summary>
        public float DensityPerM2 => PerM2(fishPer100m);

        /// <summary>Converts a per-100-m-of-shoreline figure for this band to per square metre.</summary>
        public float PerM2(float per100m) => per100m / (100f * Mathf.Max(1e-3f, nominalWidth));
    }

    /// <summary>One species or school type (brief roster, fish-targets.json). Every number is a designer value.</summary>
    [Serializable]
    public class FishSpecies
    {
        public string name;
        public FishTier tier;
        [Tooltip("Census and surface signs only; the renderer never draws its body.")]
        public bool neverDrawBody;
        [Tooltip("FishBodies / renderer variant indices; each fish picks one, seeded.")]
        public int[] variants = new int[0];
        [Tooltip("Adult total length range, metres (real lengths: player scale, not halved). Scale = length / FishBodies noseToTail.")]
        public Vector2 lengthRange = new Vector2(0.2f, 0.3f);
        [Tooltip("Rare large adults: a share (FishTuning.lengthTailShare) of fish are drawn between lengthRange.y and this.")]
        public float lengthTailMax = 0.3f;

        [Header("Where")]
        [Tooltip("Adults per 100 m of shoreline in each band (index = FishTuning.bands). Also the species' share of the band's mix.")]
        public float[] per100mByBand = new float[0];
        [Tooltip("School type: counted in schools per 100 m (schoolsPer100mByBand), not in the adult density.")]
        public bool schoolUnit;
        public float[] schoolsPer100mByBand = new float[0];
        [Tooltip("No fish of this species over less water than this, metres (covers clearance + margin + waves + body; Validate).")]
        public float minBedDepth = 0.5f;
        public FishDepthMode depthMode;
        [Tooltip("Mid / NearSurface: body centre depth below the surface, metres.")]
        public Vector2 depthRange = new Vector2(0.3f, 1.5f);
        [Tooltip("NearBed: body bottom height above the bed, metres.")]
        public Vector2 bedOffset;
        [Tooltip("Clearance from the bed to the body's lowest point, metres.")]
        public float bedClearance = 0.15f;
        [Tooltip("Margin from the body's top to the moving surface outside surface events, metres.")]
        public float surfaceMargin = 0.1f;

        [Header("Group")]
        public FishGrouping grouping;
        public Vector2Int groupSize = Vector2Int.one;
        [Tooltip("Spacing between groupmates in body lengths: x = minimum (F5 no-overlap), y = target slot spacing.")]
        public Vector2 spacingBodyLengths = new Vector2(0.6f, 1.5f);
        [Tooltip("Slot radius around the group anchor, metres (schools: brief cohesionRadius).")]
        public float groupRadius = 3f;
        [Tooltip("The group wanders within this of its home, metres (brief behaviour block, INFERRED).")]
        public float homeRadius = 15f;

        [Header("Motion (m/s, deg/s)")]
        public float cruiseSpeed = 0.2f;
        public float burstSpeed = 1.5f;
        public float turnRateCruise = 90f;
        public float turnRateBurst = 360f;
        [Tooltip("Share of bouts spent holding rather than moving (N6).")]
        [Range(0f, 1f)] public float idleShare = 0.5f;
        [Tooltip("Seconds per hold bout (brief behaviour block, INFERRED).")]
        public Vector2 holdSeconds = new Vector2(4f, 12f);
        [Tooltip("Seconds per wander leg (brief behaviour block, INFERRED).")]
        public Vector2 wanderSeconds = new Vector2(5f, 15f);
        [Tooltip("Bottom fish: scatter only on a swimmer within the 3D trigger (FishTuning.scatter).")]
        public bool bottom;

        [Header("Surface (F7)")]
        public FishSurfaceKind surfaceEvent;
        public float surfaceEventsPerFishPerMin;
        [Tooltip("May leave the water (brief jumpSpecies; ruling/fish-airborne-jumps). Others never jump.")]
        public bool canJump;
        [Tooltip("Its body may be drawn while airborne in its own jump, even if neverDrawBody (brief airborneBodyAllowed).")]
        public bool airborneBodyAllowed;
        public float jumpsPerFishPerMin;
        [Tooltip("Whole-school events per school per minute (busting, nervous water).")]
        public float schoolEventsPerMin;
    }

    /// <summary>Structure anchor type (ruling fish-robertson-island-anchor): more fish, its own mix, structure-only extras.</summary>
    [Serializable]
    public class FishStructureType
    {
        [Tooltip("Matches the anchors asset's kind name.")]
        public string kind;
        [Tooltip("Adult density multiplier inside the anchor's reach, per band (index = FishTuning.bands; shelf = ramp = the anchor's, open 1).")]
        public float[] bandMultiplier = new float[0];
        [Tooltip("The level's anchor kinds (BellsBendStructureAnchors StructureKind names) that are this type.")]
        public string[] levelKinds = new string[0];
        [Tooltip("Mix weight per species inside this structure (index = FishTuning.species; 1 = band mix).")]
        public float[] prefs = new float[0];
        [Tooltip("Structure-only species added on top (e.g. flathead): species index, -1 = none.")]
        public int extraSpecies = -1;
        [Tooltip("Adults of extraSpecies per 100 m of anchor shoreline, spread over reachOut.")]
        public float extraPer100mShore;
        [Tooltip("Extra schools per anchor (schoolSpecies), spread over the anchor's area.")]
        public int extraSchoolSpecies = -1;
        public float extraSchoolsPerAnchor;
        [Tooltip("Metres out from the waterline the anchor reaches.")]
        public float reachOut = 20f;
    }

    /// <summary>Scatter and settle (F6; brief scatter block).</summary>
    [Serializable]
    public class FishScatter
    {
        public float triggerWading = 4f, triggerSwimming = 6f, triggerBankWalk = 2.5f;
        [Tooltip("A dry player only scatters shelf fish while within this of the waterline.")]
        public float bankWalkLipDistance = 2f;
        public float bottomTriggerSwim3D = 4f;
        public Vector2 reactionSeconds = new Vector2(0.2f, 0.4f);
        public Vector2 burstSpeed = new Vector2(1.5f, 2.5f);
        public Vector2 burstSeconds = new Vector2(1f, 2f);
        public float fleeCruiseSpeed = 0.6f;
        public Vector2 fleeDistance = new Vector2(8f, 15f);
        [Tooltip("A fleeing fish whose top is shallower than this leaves a flee wake (a surface event).")]
        public float fleeSurfaceSignDepth = 0.6f;
        public float returnStartSeconds = 20f;
        [Tooltip("Turn rate of the startle (C-start) during the reaction delay, deg/s; 0 = the species' turnRateBurst (brief scatter.reactionTurnRateDegPerSec, pending f-designer's ruling).")]
        public float reactionTurnRate;
        public float settleSeconds = 45f;
    }

    /// <summary>
    /// Fish tunables (adr/fish-1 §5.7): every designer-facing number lives here, none in code. Loaded from
    /// _bmad-output/poc/fish-targets.json by the Editor importer; lives in Assets/World/Fish.
    /// </summary>
    [CreateAssetMenu(fileName = "FishTuning", menuName = "Washed Ashore/Fish Tuning")]
    public class FishTuning : ScriptableObject
    {
        public const int AdrMaxLive = 256, AdrMaxDrawn = 64, AdrMaxEvents = 16;
        /// <summary>Metres between the body draw distance and the nearest point of a cell that goes live (N7).</summary>
        public const float MinSpawnGap = 10f;

        [Header("Source")]
        [Tooltip("fish-targets.json briefRevision this asset was imported from.")]
        public string briefRevision;
        [Tooltip("First 16 hex chars of SHA-256 over the fish-targets.json bytes imported.")]
        public string briefSha16;

        [Header("Run")]
        public int bakedSeed = 101;
        [Range(10f, 20f)] public float simHz = 15f;
        public float maxTurnPerFrame = 45f;

        [Header("Cells and radii (F9)")]
        public float cellSize = 32f;
        public float simRadiusIn = 50f;
        public float simRadiusOut = 60f;
        public float eventRadius = 150f;
        [Tooltip("Bodies beyond this aren't drawn (brief R_sub), metres.")]
        public float bodyDrawDistance = 25f;
        [Tooltip("A jumping body above the surface is drawn out to this, metres (brief jumpBodyDrawRadius); live cells start beyond it.")]
        public float jumpBodyDrawRadius = 40f;
        [Tooltip("Nothing holds within this of the player, metres (N2, N7).")]
        public float personalSpace = 2.5f;
        [Tooltip("Cells planned per frame at most; the rest queue nearest-first (web hitch guard).")]
        public int maxCellPlansPerFrame = 2;
        [Tooltip("A cell that couldn't go live (cap, or in view) waits this long before it's planned again, seconds.")]
        public float blockedRetrySeconds = 1f;

        [Header("Caps (brief numbers under adr/fish-1's 256/64/16)")]
        public int maxIndividuals = 48;
        public int maxSchoolMembers = 80;
        public int maxDrawn = 64;
        public int maxSurfaceEvents = 6;
        [Tooltip("Jumps per minute across all species at most (N4 carp cap).")]
        public float maxJumpsPerMin = 0.25f;

        [Header("Behaviour (brief behaviour block, INFERRED)")]
        [Tooltip("m/s^2 while cruising, holding and settling.")]
        public float accelCruise = 0.5f;
        [Tooltip("m/s^2 while bursting away (scatter, jump).")]
        public float accelBurst = 5f;
        [Range(0f, 1f)] public float lengthTailShare = 0.05f;

        [Header("Surface signs (brief surfaceSigns block, INFERRED)")]
        [Tooltip("Calibration multiplier on the bands' surface-event rates (f-designer's band-rate rule; recorded in the brief).")]
        public float surfaceEventMultiplier = 1f;
        /// <summary>Far-water sign-only events are on when the open band carries a far-water rate (brief F16).</summary>
        public bool FarWaterSigns => bands.Length > 0 && bands[bands.Length - 1].farSignsPerM2PerMin > 0f;
        [Tooltip("Visible life of each sign kind, seconds (min, max), indexed by FishSurfaceKind.")]
        public Vector2[] signSeconds = new Vector2[0];
        [Tooltip("Basking time at the surface, seconds.")]
        public Vector2 baskSeconds = new Vector2(20f, 60f);
        [Tooltip("How far the body clears the water at the top of a jump, body lengths; the airtime is ballistic.")]
        public Vector2 jumpHeightBodyLengths = new Vector2(0.3f, 1f);
        [Tooltip("Share of GarGulpOrBask signs that are gulps (the rest are basks).")]
        [Range(0f, 1f)] public float garGulpShare = 0.7f;

        [Header("Swim clip (F5)")]
        [Tooltip("Below this speed a fish is at rest, m/s.")]
        public float restSpeed = 0.05f;
        [Tooltip("Clip cycles per second at rest (never a frozen frame).")]
        public float idleAnimRate = 0.25f;
        [Tooltip("Body lengths travelled per Swim cycle (f-artist, INFERRED until the F1 measurement).")]
        public float strideBodyLengths = 0.7f;
        public float maxAnimRate = 12.5f;
        [Range(0f, 1f)] public float amplitudeIdle = 0.25f;

        [Header("Visibility")]
        public float visibilityFloor = FishMurk.DefaultFloor;
        [Tooltip("V1 bodies draw as a body only within this depth; deeper is shadow at most (ruling/fish-visibility-tiers).")]
        public float bodyTierDepth = 0.6f;
        public bool southOfNorthLineOnly = true;

        [Header("Content")]
        public FishBand[] bands = new FishBand[0];
        public FishSpecies[] species = new FishSpecies[0];
        public FishStructureType[] structures = new FishStructureType[0];
        public FishScatter scatter = new FishScatter();

        public int MaxLive => maxIndividuals + maxSchoolMembers;

        /// <summary>
        /// The one body draw radius rule (ruling/fish-jump-body-radius; the sim's no-pop test and the renderer's draw rule
        /// both call it): a jumping fish whose body top is at or above the surface draws to jumpBodyDrawRadius; every other
        /// body to bodyDrawDistance (R_sub). bodyTopDepth = surface - body top (&lt;= 0 means out of the water).
        /// </summary>
        public float BodyDrawRadius(FishMode mode, float bodyTopDepth) =>
            mode == FishMode.Jump && bodyTopDepth <= 0f ? jumpBodyDrawRadius : bodyDrawDistance;

        /// <summary>Clip cycles per second for a fish of this length at this speed (brief animRateRule: idle + speed / stride, clamped).</summary>
        public float AnimRate(float speed, float length) =>
            FishSteering.AnimRate(speed < restSpeed ? 0f : speed, idleAnimRate, CyclesPerMetre(length), maxAnimRate);

        public float CyclesPerMetre(float length) => 1f / Mathf.Max(1e-3f, strideBodyLengths * length);

        /// <summary>Visible life of a sign kind, seconds (min, max); a missing entry is (1, 1).</summary>
        public Vector2 SignSeconds(FishSurfaceKind kind) => (int)kind < signSeconds.Length ? signSeconds[(int)kind] : Vector2.one;

        /// <summary>
        /// Problems a designer must fix, or null. With <paramref name="bodies"/> and the shipped waves' amplitude, also
        /// checks every species' minimum depth fits its tallest body between the bed clearance and the surface margin
        /// at the lowest trough (F4 can't hold otherwise), and that every variant has a measured body.
        /// </summary>
        public string Validate(FishBodies bodies = null, float waveAmplitude = 0f)
        {
            if (simRadiusOut <= simRadiusIn) return "simRadiusOut must exceed simRadiusIn";
            float drawn = Mathf.Max(bodyDrawDistance, jumpBodyDrawRadius);
            if (simRadiusIn < drawn + MinSpawnGap) return $"simRadiusIn must be at least the largest draw radius + {MinSpawnGap} m (no pop-in)";
            if (eventRadius < simRadiusIn) return "eventRadius must cover simRadiusIn";
            if (MaxLive > AdrMaxLive || maxDrawn > AdrMaxDrawn || maxSurfaceEvents > AdrMaxEvents)
                return $"caps above adr/fish-1 ({AdrMaxLive}/{AdrMaxDrawn}/{AdrMaxEvents}) need a TD ruling";
            if (maxCellPlansPerFrame < 1) return "maxCellPlansPerFrame must be at least 1";
            foreach (var b in bands)
            {
                if (b == null || b.nominalWidth <= 0f || b.bedDepth.y <= b.bedDepth.x) return $"band {b?.name}: need nominalWidth > 0 and bedDepth max > min";
                // Shore distance is exact only south of the north line (f-level; guard test), so an edge needs the line.
                if (b.maxShoreDistance > 0f && !southOfNorthLineOnly) return $"band {b.name}: a maxShoreDistance needs southOfNorthLineOnly";
            }
            foreach (var s in species)
            {
                if (s == null) return "null species";
                if (s.per100mByBand.Length != bands.Length || s.schoolsPer100mByBand.Length != bands.Length)
                    return $"{s.name}: per-band arrays need {bands.Length} entries";
                if (s.variants.Length == 0) return $"{s.name}: no variants";
                if (s.groupSize.x < 1 || s.groupSize.y < s.groupSize.x) return $"{s.name}: groupSize must be 1 <= min <= max";
                if (s.cruiseSpeed <= 0f || s.burstSpeed < s.cruiseSpeed) return $"{s.name}: need 0 < cruiseSpeed <= burstSpeed";
                if (s.lengthRange.x <= 0f || s.lengthRange.y < s.lengthRange.x) return $"{s.name}: need 0 < lengthRange min <= max";
                // The shortest fish beats fastest: the rate map must not clamp below its burst (F5 correlation). A scatter
                // never exceeds the species' burstSpeed (the brief's burstRule).
                if (!FishSteering.RateCoversBurst(s.burstSpeed, idleAnimRate, CyclesPerMetre(s.lengthRange.x), maxAnimRate + 1e-4f))
                    return $"{s.name}: clip rate clamps below burstSpeed {s.burstSpeed} m/s for the shortest fish";
                if (s.lengthTailMax < s.lengthRange.y) return $"{s.name}: lengthTailMax below lengthRange max";
                if (!s.canJump && (s.jumpsPerFishPerMin > 0f || s.surfaceEvent == FishSurfaceKind.Jump)) return $"{s.name}: jumps but isn't a jump species";
                if (bodies == null) continue;
                foreach (int v in s.variants)
                {
                    if (!bodies.Has(v)) return $"{s.name}: variant {v} has no measured body in FishBodies";
                    var body = bodies.bodies[v];
                    float maxScale = s.lengthTailMax / Mathf.Max(1e-3f, body.noseToTail);
                    // Body bottom >= bed + clearance and top <= trough - margin, with the trough waveAmplitude below W.
                    float need = s.bedClearance + s.surfaceMargin + waveAmplitude + body.clipBounds.size.y * maxScale;
                    if (s.minBedDepth < need) return $"{s.name}: minBedDepth {s.minBedDepth:F2} m < clearance + margin + waves + tallest body = {need:F2} m";
                }
            }
            foreach (var st in structures)
                if (st == null || st.prefs.Length != species.Length || st.bandMultiplier.Length != bands.Length)
                    return $"structure {st?.kind}: prefs need {species.Length} entries and bandMultiplier {bands.Length}";
            return null;
        }

        public int StructureIndex(string kind)
        {
            for (int i = 0; i < structures.Length; i++) if (structures[i].kind == kind) return i;
            return -1;
        }

        /// <summary>The structure type that a level anchor kind (StructureKind name) belongs to, or -1.</summary>
        public int StructureForLevelKind(string levelKind)
        {
            for (int i = 0; i < structures.Length; i++)
                if (Array.IndexOf(structures[i].levelKinds, levelKind) >= 0) return i;
            return -1;
        }

        void OnValidate()
        {
            string problem = Validate();
            if (problem != null) Debug.LogWarning($"FishTuning '{name}': {problem}", this);
        }
    }
}
