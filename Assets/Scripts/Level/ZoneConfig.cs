using UnityEngine;

namespace WashedAshore.Level
{
    // Designer-owned classification and road parameters (design brief rev 2, sections a, b, d).
    // Ranges are the brief's allowed bands; values outside them go back to systems-designer.
    [CreateAssetMenu(fileName = "ZoneConfig", menuName = "Washed Ashore/Zone Config")]
    public class ZoneConfig : ScriptableObject
    {
        public const string AssetPath = "Assets/World/BellsBend/ZoneConfig.asset";

        [Header("Zones (a)")]
        public float zoneCell = 5f;
        [Range(6f, 10f)] public float forestSlopeDeg = 6f;   // designer-2 pick 6/60 from the rc4 sweep (brief)
        [Range(50f, 70f)] public float ridgeHRel = 60f;
        public float bankBuffer = 30f;
        public float bankHRel = 15f;
        public float roadBuffer = 20f;
        public float clearingSpawnRadius = 40f;
        public float clearingMarkerRadius = 20f;
        [Tooltip("Real metres; DEM box blur used for demSlope only.")]
        public float demSlopeBlurReal = 30f;
        public float cliffSlopeDeg = 40f;

        [Header("Roads (b)")]
        public RoadSpec[] roads =
        {
            new RoadSpec { osmNames = new[] { "Old Hickory Boulevard" }, id = 1, core = 5.5f, verge = 1.5f, falloff = 8f, gravel = false },
            new RoadSpec { osmNames = new[] { "Pecan Valley Road" }, id = 2, core = 5f, verge = 1f, falloff = 6f, gravel = false },
            new RoadSpec { osmNames = new[] { "Tidwell Hollow Road" }, id = 3, core = 3.5f, verge = 1f, falloff = 6f, gravel = true },
            new RoadSpec { osmNames = new[] { "Cleeces Rerry Road" }, id = 4, core = 4.5f, verge = 1f, falloff = 6f, gravel = false },
            new RoadSpec { osmNames = new[] { "Cleeces Ferry Road" }, id = 5, core = 3f, verge = 0.5f, falloff = 4f, gravel = true },
        };
        public float roadSmoothWindow = 30f;
        public float roadCutFillCap = 3f;
        public float roadBluffExclusion = 30f;
        [Tooltip("Levelling stops this far inland of the shore (brief rev 3 bank rule).")]
        public float roadShoreExclusion = 12f;

        [Header("Spawn (d)")]
        public double spawnAnchorLat = 36.1535, spawnAnchorLon = -86.9235;
        public double spawnFaceLat = 36.1660, spawnFaceLon = -86.9060;
        public float spawnSearchRadius = 150f;
        public float spawnFlatRadius = 25f;
        public float spawnMaxRange = 1.5f;
        public float spawnMaxSlope = 10f;
        public float spawnMinAboveWater = 3f;

        [System.Serializable]
        public class RoadSpec
        {
            public string[] osmNames;
            public byte id;
            public float core, verge, falloff;
            public bool gravel;
            public float PaintHalf => core * 0.5f + verge;
        }
    }
}
