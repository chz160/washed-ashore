using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using WashedAshore.Fish;

/// <summary>
/// Builds Assets/World/Fish/FishTuning.asset from f-designer's data of record, _bmad-output/poc/fish-targets.json
/// (studio:design fish/targets), so no tuning number is typed in twice. Variant names resolve against FishBodies (the
/// order f-artist's bake writes). Rerun after any brief revision; the asset is overwritten.
/// </summary>
public static class FishTuningImport
{
    public const string JsonPath = "_bmad-output/poc/fish-targets.json";
    public const string AssetPath = "Assets/World/Fish/FishTuning.asset";
    public const string BodiesPath = "Assets/World/Fish/FishBodies.asset";

    [MenuItem("Washed Ashore/Fish/Import Fish Tuning")]
    public static void Run() => Debug.Log(Import());

    /// <summary>Imports and saves; returns a one-line summary. Throws on any name that doesn't resolve.</summary>
    public static string Import()
    {
        string root = Path.GetDirectoryName(Application.dataPath);
        byte[] bytes = File.ReadAllBytes(Path.Combine(root, JsonPath));
        var src = JsonUtility.FromJson<Targets>(System.Text.Encoding.UTF8.GetString(bytes));
        var bodies = AssetDatabase.LoadAssetAtPath<FishBodies>(BodiesPath);
        if (!bodies) throw new FileNotFoundException("FishBodies missing; run f-artist's fish bake first", BodiesPath);

        var t = AssetDatabase.LoadAssetAtPath<FishTuning>(AssetPath);
        bool created = !t;
        if (created) t = ScriptableObject.CreateInstance<FishTuning>();
        Fill(t, src, bodies);
        t.briefSha16 = Sha16(bytes);
        if (created)
        {
            Directory.CreateDirectory(Path.Combine(root, Path.GetDirectoryName(AssetPath)));
            AssetDatabase.CreateAsset(t, AssetPath);
        }
        EditorUtility.SetDirty(t);
        AssetDatabase.SaveAssets();
        return $"FishTuning brief {src.briefRevision} ({t.briefSha16}): {t.bands.Length} bands, {t.species.Length} species, {t.structures.Length} structure types; " +
               $"validate: {t.Validate(bodies, 0f) ?? "ok (waves checked at runtime)"}";
    }

    /// <summary>First 16 hex chars of SHA-256 over the bytes (the brief's identity, as f-qa records it).</summary>
    public static string Sha16(byte[] bytes)
    {
        using (var sha = System.Security.Cryptography.SHA256.Create())
            return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").Substring(0, 16).ToLowerInvariant();
    }

    public static void Fill(FishTuning t, Targets src, FishBodies bodies)
    {
        t.briefRevision = src.briefRevision;
        t.bakedSeed = src.defaultSeed;
        t.maxTurnPerFrame = src.limits.maxTurnDegPerFrame;
        t.southOfNorthLineOnly = src.limits.southOfNorthLineOnly;
        t.cellSize = src.sim.cellSize;
        t.simRadiusIn = src.sim.bodySimRadiusIn;
        t.simRadiusOut = src.sim.bodySimRadiusOut;
        t.eventRadius = src.sim.surfaceEventRadius;
        t.bodyDrawDistance = src.sim.bodyDrawDistance;
        t.maxIndividuals = src.sim.maxSimulatedIndividuals;
        t.maxSchoolMembers = src.sim.maxInstancedSchoolMembers;
        t.maxSurfaceEvents = src.sim.maxLiveSurfaceEvents;
        t.maxJumpsPerMin = src.carpJumpCapPerMin;
        t.restSpeed = src.animation.restSpeedThreshold;
        t.idleAnimRate = src.animation.idleAnimRate;
        t.strideBodyLengths = src.animation.strideBodyLengths;
        t.maxAnimRate = src.animation.maxRate;
        t.bodyTierDepth = src.visibility.bodyPlacementTierDepth;
        t.jumpBodyDrawRadius = src.visibility.jumpBodyDrawRadius;
        t.accelCruise = src.behaviour.accelCruise;
        t.accelBurst = src.behaviour.accelBurst;
        t.lengthTailShare = src.behaviour.lengthTailShare;
        var ss = src.surfaceSigns;
        t.signSeconds = new Vector2[System.Enum.GetValues(typeof(FishSurfaceKind)).Length];
        t.signSeconds[(int)FishSurfaceKind.Dimple] = Vector2.one * ss.dimpleSec;
        t.signSeconds[(int)FishSurfaceKind.Swirl] = Vector2.one * ss.swirlSec;
        t.signSeconds[(int)FishSurfaceKind.Roll] = Vector2.one * ss.rollOrTailSec;
        t.signSeconds[(int)FishSurfaceKind.RollOrTail] = Vector2.one * ss.rollOrTailSec;
        t.signSeconds[(int)FishSurfaceKind.GarGulpOrBask] = Vector2.one * ss.gulpSec;
        t.signSeconds[(int)FishSurfaceKind.Busting] = Range(ss.nervousWaterSec);          // surface feeding: as nervous water
        t.signSeconds[(int)FishSurfaceKind.Wake] = Vector2.one * ss.vWakeSec;
        t.signSeconds[(int)FishSurfaceKind.NervousWaterOrFlip] = Range(ss.nervousWaterSec);
        t.signSeconds[(int)FishSurfaceKind.Jump] = Vector2.one * ss.splashRingSec;
        t.signSeconds[(int)FishSurfaceKind.FleeWake] = Vector2.one * ss.vWakeSec;
        t.signSeconds[(int)FishSurfaceKind.Rise] = Vector2.one * ss.riseSec;
        t.baskSeconds = Range(ss.baskSec);
        t.jumpHeightBodyLengths = Range(ss.jumpHeightBodyLengths);
        t.garGulpShare = ss.garGulpShare;
        // Calibrated multiplier on the band sign rates; absent from the brief until it is recorded (then 1).
        t.surfaceEventMultiplier = ss.bandRateMultiplier > 0f ? ss.bandRateMultiplier : 1f;

        var s = src.scatter;
        t.scatter = new FishScatter
        {
            triggerWading = s.triggerWading, triggerSwimming = s.triggerSwimming, triggerBankWalk = s.triggerBankWalk,
            bankWalkLipDistance = s.bankWalkLipDistance, bottomTriggerSwim3D = s.bottomFishTriggerSwim3D,
            reactionSeconds = new Vector2(s.reactionTimeMin, s.reactionTimeMax), burstSpeed = new Vector2(s.burstSpeedMin, s.burstSpeedMax),
            burstSeconds = new Vector2(s.burstSecMin, s.burstSecMax), fleeCruiseSpeed = s.fleeCruiseSpeed,
            fleeDistance = new Vector2(s.fleeDistanceMin, s.fleeDistanceMax), fleeSurfaceSignDepth = s.fleeSurfaceSignDepth,
            returnStartSeconds = s.returnStartSec, settleSeconds = s.settleSec, reactionTurnRate = s.reactionTurnRateDegPerSec,
        };

        int nb = src.bands.Length;
        t.bands = new FishBand[nb];
        for (int b = 0; b < nb; b++)
        {
            var sb = src.bands[b];
            t.bands[b] = new FishBand { name = sb.band, bedDepth = new Vector2(sb.bedDepthMin, sb.bedDepthMax), nominalWidth = sb.nominalWidth, maxShoreDistance = sb.outerShoreDistance,
                fishPer100m = sb.adultsPer100m, surfaceEventsPerMinPer100m = sb.surfaceEventsPer100mPerMin, farSignsPerM2PerMin = sb.farWaterSignsPerM2PerMin };
        }

        int n = src.species.Length + src.schools.Length;
        t.species = new FishSpecies[n];
        for (int i = 0; i < src.species.Length; i++) t.species[i] = Adult(src.species[i], src, bodies);
        for (int i = 0; i < src.schools.Length; i++) t.species[src.species.Length + i] = School(src.schools[i], src, bodies);
        foreach (var sp in t.species) sp.canJump = Array.IndexOf(src.jumpSpecies ?? new string[0], sp.name) >= 0;

        t.structures = new FishStructureType[src.structure.Length];
        for (int i = 0; i < src.structure.Length; i++)
        {
            var st = src.structure[i];
            var prefs = new float[n];
            for (int k = 0; k < n; k++) prefs[k] = 1f;
            foreach (var p in st.prefs) prefs[SpeciesIndex(t, p.id)] = p.weight;
            t.structures[i] = new FishStructureType
            {
                kind = st.anchorType, prefs = prefs, reachOut = st.reachOut, levelKinds = LevelKinds(st.anchorType),
                bandMultiplier = ByBand(src, st.shelfMultiplier, st.rampMultiplier, st.openMultiplier),
                extraSpecies = st.flatheadPer100mShore > 0f ? SpeciesIndex(t, "FlatheadCatfish") : -1, extraPer100mShore = st.flatheadPer100mShore,
                extraSchoolSpecies = st.extraShadSchoolsPerAnchor > 0f ? SpeciesIndex(t, "GizzardShad") : -1, extraSchoolsPerAnchor = st.extraShadSchoolsPerAnchor,
            };
        }
    }

    /// <summary>
    /// Which of f-level's StructureKind names make up each of the brief's anchor types (the two vocabularies were
    /// named separately; f-designer confirmed the mapping).
    /// </summary>
    static string[] LevelKinds(string anchorType)
    {
        switch (anchorType)
        {
            case "BluffFace": return new[] { "Bluff" };
            case "HollowMouth": return new[] { "HollowMouth" };
            case "LandingOrSlipway": return new[] { "FerryLanding", "Slipway" };
            case "IslandFace": return new[] { "IslandFace" };
            case "IslandChuteMouth": return new[] { "ChuteMouth" };
            default: throw new InvalidDataException($"anchor type '{anchorType}' has no level StructureKind mapping");
        }
    }

    static Vector2 Range(float[] v) => v.Length >= 2 ? new Vector2(v[0], v[1]) : throw new InvalidDataException("expected a [min, max] pair");

    static FishSpecies Adult(SpeciesRow r, Targets src, FishBodies bodies)
    {
        var b = src.behaviour;
        bool openOnly = r.per100mShelf <= 0f && r.per100mRamp <= 0f;
        var s = AdultRow(r, src, bodies);
        s.lengthTailMax = Mathf.Max(r.lengthTailMax, r.lengthMax);
        s.holdSeconds = Range(r.bottom ? b.holdSecBottom : b.holdSec);
        s.wanderSeconds = Range(b.wanderSec);
        s.homeRadius = r.grouping == "School" ? b.homeWanderRadiusSchool : r.bottom ? b.homeWanderRadiusBottom : openOnly ? b.homeWanderRadiusOpen : b.homeWanderRadius;
        return s;
    }

    static FishSpecies AdultRow(SpeciesRow r, Targets src, FishBodies bodies) => new FishSpecies
    {
        name = r.id, tier = Enum<FishTier>(r.tier), neverDrawBody = r.neverDrawBody, variants = Variants(r.variants, bodies, r.id),
        lengthRange = new Vector2(r.lengthMin, r.lengthMax),
        per100mByBand = ByBand(src, r.per100mShelf, r.per100mRamp, r.per100mOpen), schoolsPer100mByBand = new float[src.bands.Length],
        minBedDepth = r.minBedDepth, depthMode = Enum<FishDepthMode>(r.swimDepthMode), depthRange = new Vector2(r.depthMin, r.depthMax),
        bedOffset = new Vector2(r.bedOffsetMin, r.bedOffsetMax), bedClearance = r.bedClearance, surfaceMargin = r.surfaceMargin,
        grouping = Enum<FishGrouping>(r.grouping), groupSize = new Vector2Int(r.groupMin, r.groupMax),
        spacingBodyLengths = new Vector2(r.spacingMinBodyLengths, r.spacingTargetBodyLengths),
        cruiseSpeed = r.cruiseSpeed, burstSpeed = r.burstSpeed, turnRateCruise = r.maxTurnDegPerSecCruise, turnRateBurst = r.maxTurnDegPerSecBurst,
        idleShare = r.idleShare, bottom = r.bottom, surfaceEvent = Enum<FishSurfaceKind>(r.surfaceEvent), airborneBodyAllowed = r.airborneBodyAllowed,
        surfaceEventsPerFishPerMin = r.surfaceEventPerFishPerMin, jumpsPerFishPerMin = r.jumpPerFishPerMin, schoolEventsPerMin = r.schoolEventPerMin,
    };

    static FishSpecies School(SchoolRow r, Targets src, FishBodies bodies)
    {
        var perBand = new float[src.bands.Length];
        for (int b = 0; b < perBand.Length; b++)
            perBand[b] = r.id == "GizzardShad" ? src.bands[b].shadSchoolsPer100m : r.id == "SkipjackHerring" ? src.bands[b].skipjackSchoolsPer100m
                : throw new InvalidDataException($"school {r.id}: no per-band school count key in the bands");
        return new FishSpecies
        {
            name = r.id, tier = Enum<FishTier>(r.tier), neverDrawBody = r.neverDrawBody, variants = Variants(r.variants, bodies, r.id),
            lengthRange = new Vector2(r.lengthMin, r.lengthMax), lengthTailMax = r.lengthMax, per100mByBand = new float[src.bands.Length], schoolUnit = true,
            holdSeconds = Range(src.behaviour.holdSec), wanderSeconds = Range(src.behaviour.wanderSec), homeRadius = src.behaviour.homeWanderRadiusSchool,
            schoolsPer100mByBand = perBand, minBedDepth = r.minBedDepth, depthMode = FishDepthMode.Mid, depthRange = new Vector2(r.depthMin, r.depthMax),
            bedClearance = r.bedClearance, surfaceMargin = r.surfaceMargin, grouping = FishGrouping.School,
            groupSize = new Vector2Int(r.membersMin, r.membersMax), spacingBodyLengths = new Vector2(r.spacingMinBodyLengths, r.spacingBodyLengths),
            groupRadius = r.cohesionRadius, cruiseSpeed = r.cruiseSpeed, burstSpeed = r.burstSpeed,
            turnRateCruise = r.maxTurnDegPerSecCruise, turnRateBurst = r.maxTurnDegPerSecBurst, idleShare = 0f,
            surfaceEvent = Enum<FishSurfaceKind>(r.surfaceEvent), schoolEventsPerMin = r.schoolEventPerMin, airborneBodyAllowed = r.airborneBodyAllowed,
        };
    }

    static float[] ByBand(Targets src, float shelf, float ramp, float open)
    {
        var v = new float[src.bands.Length];
        for (int b = 0; b < v.Length; b++)
            v[b] = src.bands[b].band == "Shelf" ? shelf : src.bands[b].band == "Ramp" ? ramp : src.bands[b].band == "Open" ? open
                : throw new InvalidDataException($"band {src.bands[b].band}: species rows only carry Shelf/Ramp/Open");
        return v;
    }

    static int[] Variants(string[] names, FishBodies bodies, string id)
    {
        var v = new int[names.Length];
        for (int i = 0; i < names.Length; i++)
        {
            v[i] = Array.FindIndex(bodies.bodies, b => b.variant == names[i]);
            if (v[i] < 0) throw new InvalidDataException($"{id}: variant '{names[i]}' isn't in FishBodies (f-artist's bake)");
        }
        return v;
    }

    static int SpeciesIndex(FishTuning t, string id)
    {
        int i = Array.FindIndex(t.species, s => s.name == id);
        if (i < 0) throw new InvalidDataException($"species '{id}' isn't in the roster");
        return i;
    }

    static T Enum<T>(string name) where T : struct =>
        System.Enum.TryParse(name, out T v) ? v : throw new InvalidDataException($"'{name}' isn't a {typeof(T).Name}");

    // ---- fish-targets.json shape (JsonUtility: fields named as the keys) ----
#pragma warning disable 0649
    [Serializable] public class Targets
    {
        public string briefRevision;
        public int defaultSeed;
        public Visibility visibility;
        public Limits limits;
        public BandRow[] bands;
        public SpeciesRow[] species;
        public SchoolRow[] schools;
        public StructureRow[] structure;
        public Scatter scatter;
        public Sim sim;
        public Animation animation;
        public Behaviour behaviour;
        public SurfaceSigns surfaceSigns;
        public float carpJumpCapPerMin;
        public string[] jumpSpecies;
    }
    [Serializable] public class Visibility { public float bodyPlacementTierDepth, jumpBodyDrawRadius; }
    [Serializable] public class Limits { public float maxTurnDegPerFrame; public bool southOfNorthLineOnly; }
    [Serializable] public class BandRow
    {
        public string band;
        public float bedDepthMin, bedDepthMax, nominalWidth, outerShoreDistance, adultsPer100m, shadSchoolsPer100m, skipjackSchoolsPer100m, surfaceEventsPer100mPerMin, farWaterSignsPerM2PerMin;
    }
    [Serializable] public class SpeciesRow
    {
        public string id, tier, grouping, swimDepthMode, surfaceEvent;
        public string[] variants;
        public float lengthMin, lengthMax, lengthTailMax, depthMin, depthMax, bedOffsetMin, bedOffsetMax, per100mShelf, per100mRamp, per100mOpen;
        public int groupMin, groupMax;
        public float burstSpeed, cruiseSpeed, maxTurnDegPerSecCruise, maxTurnDegPerSecBurst, idleShare, minBedDepth;
        public float surfaceEventPerFishPerMin, jumpPerFishPerMin, schoolEventPerMin;
        public float spacingMinBodyLengths, spacingTargetBodyLengths, bedClearance, surfaceMargin;
        public bool bottom, neverDrawBody, airborneBodyAllowed;
    }
    [Serializable] public class SchoolRow
    {
        public string id, tier, surfaceEvent;
        public string[] variants;
        public float lengthMin, lengthMax, depthMin, depthMax, spacingBodyLengths, spacingMinBodyLengths, cohesionRadius;
        public int membersMin, membersMax;
        public float schoolEventPerMin, cruiseSpeed, burstSpeed, maxTurnDegPerSecCruise, maxTurnDegPerSecBurst, minBedDepth, bedClearance, surfaceMargin;
        public bool neverDrawBody, airborneBodyAllowed;
    }
    [Serializable] public class Pref { public string id; public float weight; }
    [Serializable] public class StructureRow
    {
        public string anchorType;
        public float reachOut, shelfMultiplier, rampMultiplier, openMultiplier, flatheadPer100mShore, extraShadSchoolsPerAnchor;
        public Pref[] prefs;
    }
    [Serializable] public class Scatter
    {
        public float triggerWading, triggerSwimming, triggerBankWalk, bankWalkLipDistance, bottomFishTriggerSwim3D;
        public float burstSpeedMin, burstSpeedMax, burstSecMin, burstSecMax, fleeCruiseSpeed, fleeDistanceMin, fleeDistanceMax;
        public float fleeSurfaceSignDepth, returnStartSec, settleSec, reactionTimeMin, reactionTimeMax, reactionTurnRateDegPerSec;
    }
    [Serializable] public class Sim
    {
        public float cellSize, bodySimRadiusIn, bodySimRadiusOut, surfaceEventRadius, bodyDrawDistance;
        public int maxSimulatedIndividuals, maxInstancedSchoolMembers, maxLiveSurfaceEvents;
    }
    [Serializable] public class Animation { public float restSpeedThreshold, idleAnimRate, strideBodyLengths, maxRate; }
    [Serializable] public class Behaviour
    {
        public float accelCruise, accelBurst, homeWanderRadius, homeWanderRadiusBottom, homeWanderRadiusOpen, homeWanderRadiusSchool, lengthTailShare;
        public float[] holdSec, holdSecBottom, wanderSec;
    }
    [Serializable] public class SurfaceSigns
    {
        public float dimpleSec, riseSec, swirlSec, vWakeSec, rollOrTailSec, gulpSec, splashRingSec, garGulpShare, bandRateMultiplier;
        public float[] nervousWaterSec, baskSec, jumpHeightBodyLengths;
    }
#pragma warning restore 0649
}
