using UnityEngine;

// Technical-artist data: per-zone look targets for Bells Bend 25 years after the 1987 collapse.
// Numbers are designer-2's brief, section (a) rev 2 (_bmad-output/poc/bells-bend-design-brief.md).
// BellsBendGround paints from it and BellsBendCoverage grades against it.
// Zone ids match BellsBendLevelMaps.Zone() (level-2); the cliff overlay is IsCliff().
public static class BellsBendArtTargets
{
    public const int Seed = 1987;

    public const byte Outside = 0, Road = 1, RoadBuffer = 2, Bank = 3, Ridge = 4, Hollows = 5, Fields = 6, Clearing = 7, NorthVista = 8;
    public static readonly string[] ZoneNames = { "outside", "Z0 road", "Z1 road buffer", "Z2 bank", "Z4a ridge", "Z4b hollows", "Z5 former fields", "Z6 clearing", "ZN north vista" };

    // Tree species (BellsBendVegetation.TreeSpecs). Pine stands in for eastern redcedar.
    public const int Hardwood = 0, Pine = 1, Twisted = 2, Dead = 3, FieldCedar = 4;
    public static readonly string[] SpeciesNames = { "CommonTree", "Pine", "TwistedTree", "DeadTree", "Pine (cedar, 5.5-8 m)" };

    public class Zone
    {
        public float treesPerHa, floor, ceiling;
        public float[] mix;           // by species index
        public float clumpCover = 1f; // share of the zone inside clumps (Z5 only)
        public float shrubCell;       // chance a 2 m detail cell gets a shrub/briar/kudzu instance
        public float maxOpen = 1f, minOpen, maxOpenBare = 1f, minShrubShare; // pass criteria (brief table)
        public int ground;            // AmbientCgLayers index
    }

    public const float OpenRadius = 8f;   // open cell = no tree instance within 8 m of the 5 m cell centre
    public const float CellMetres = 5f;   // the brief's scoring grid
    public const float CliffMaxTreeSlope = 45f;
    public const float FieldClumpMetres = 50f; // about one cedar clump per 40-60 m
    public const float RoadBufferOuter = 12f;  // Z1 trees only beyond this distance from the centreline
    public const float BankMud = 6f;           // shore distance (m, inland) painted as river bank

    static float[] Mix(float hardwood, float pine, float twisted, float dead, float cedar) => new[] { hardwood, pine, twisted, dead, cedar };

    public static readonly Zone[] Zones =
    {
        null,
        new Zone { treesPerHa = 0, ground = AmbientCgLayers.Asphalt },
        new Zone { treesPerHa = 10, floor = 0, ceiling = 40, mix = Mix(0, 0, 0.5f, 0.5f, 0), shrubCell = 0.55f, maxOpenBare = 0.15f, ground = AmbientCgLayers.Kudzu },
        new Zone { treesPerHa = 100, floor = 60, ceiling = 160, mix = Mix(0.7f, 0, 0.3f, 0, 0), shrubCell = 0.3f, maxOpen = 0.30f, maxOpenBare = 0.10f, minShrubShare = 0.5f, ground = AmbientCgLayers.ForestFloor },
        new Zone { treesPerHa = 150, floor = 100, ceiling = 250, mix = Mix(0.7f, 0.15f, 0.07f, 0.08f, 0), shrubCell = 0.04f, maxOpen = 0.10f, maxOpenBare = 0.05f, ground = AmbientCgLayers.ForestFloor },
        new Zone { treesPerHa = 150, floor = 100, ceiling = 250, mix = Mix(0.7f, 0.15f, 0.07f, 0.08f, 0), shrubCell = 0.08f, maxOpen = 0.10f, maxOpenBare = 0.05f, ground = AmbientCgLayers.ForestFloor },
        new Zone { treesPerHa = 70, floor = 40, ceiling = 110, mix = Mix(0.1f, 0, 0, 0.1f, 0.8f), clumpCover = 0.5f, shrubCell = 0.3f, maxOpen = 0.50f, minOpen = 0.15f, maxOpenBare = 0.20f, ground = AmbientCgLayers.Grass },
        new Zone { treesPerHa = 0, shrubCell = 0.05f, ground = AmbientCgLayers.Grass },
        new Zone { treesPerHa = 150, floor = 60, ceiling = 250, mix = Mix(0.7f, 0.15f, 0.07f, 0.08f, 0), shrubCell = 0.04f, ground = AmbientCgLayers.ForestFloor },
    };

    public const float WholeLandMaxOpen = 0.35f, WholeLandMaxOpenBare = 0.12f;
    public const float KudzuCellShare = 0.60f, RoadSplatShare = 0.90f, RoadSplatMin = 0.6f, CliffSplatShare = 0.80f, CliffSplatMin = 0.5f;

    public static Zone Get(byte zone) => zone < Zones.Length ? Zones[zone] : null;
}
