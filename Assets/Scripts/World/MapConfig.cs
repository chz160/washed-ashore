using UnityEngine;

namespace WashedAshore.World
{
    /// <summary>
    /// Single source of truth for the Bells Bend map scale, datum, water level and north line.
    /// tools/terrain/build_terrain.py reads this asset's YAML, so edit values here (Inspector)
    /// and rerun the pipeline. Unity Y = (DEM metres - datumMeters) * verticalScale.
    /// </summary>
    [CreateAssetMenu(fileName = "MapConfig", menuName = "Washed Ashore/Map Config")]
    public class MapConfig : ScriptableObject
    {
        [Header("Scale (greenlight bells-bend-land)")]
        [Tooltip("Game metres per real metre. 0.5 = 1:2.")]
        public float horizontalScale = 0.5f;
        [Tooltip("Vertical factor. Default 0.7; the pod may tune only within 0.6-0.85.")]
        [Range(0.6f, 0.85f)] public float verticalScale = 0.7f;
        [Tooltip("Real DEM elevation (m, NAVD88) that maps to Unity Y = 0.")]
        public float datumMeters = 115f;
        [Tooltip("Cheatham Lake normal pool, 385 ft MSL.")]
        public float poolElevationMeters = 117.3f;

        [Header("North boundary")]
        [Tooltip("Unity Z of the north line (36.2055 N by default). Player must stay at z <= northLineZ.")]
        public float northLineZ = 1986.7f;
        // Before opening the north for real, split content with Addressables:
        // _bmad-output/planning-artifacts/deferred-addressables-content-streaming.md
        [Tooltip("Old Hickory Blvd checkpoint gate state. Closed by default.")]
        public bool gateOpen;

        [Header("Terrain build (tools/terrain)")]
        [Tooltip("Terrain tile edge in game metres.")]
        public int tileSizeMeters = 1024;
        [Tooltip("Heightmap resolution per tile, 2^n+1.")]
        public int heightmapResolution = 1025;
        [Tooltip("Minimum terrain margin around the playable polygon, game metres (L3).")]
        public float marginMeters = 600f;
        [Tooltip("Minimum real-DEM country north of the line, game metres (B1).")]
        public float northVistaMinMeters = 400f;
        [Tooltip("Lake bed depth below WaterLevelY outside the polygon (L3 requires >= 8).")]
        public float lakeDepthBelowWater = 10f;
        [Tooltip("Outward distance from shore to full lake depth, game metres (L3 allows up to 150).")]
        public float shoreRampMeters = 20f;
        [Tooltip("Land inside the polygon is kept at least this far above WaterLevelY (outside the bank band).")]
        public float landMinAboveWater = 1.5f;
        [Tooltip("Pipeline manifest, relative to the project root.")]
        public string manifestPath = "Data/terrain/build/manifest.json";

        [Header("Shoreline bank rule (design brief rev 3 (c))")]
        [Tooltip("Height of the bank lip above WaterLevelY where land meets water.")]
        public float bankLipAboveWater = 0.3f;
        [Tooltip("Bank slope inland of the lip and down to wading depth.")]
        [Range(15f, 30f)] public float bankSlopeDeg = 18f;
        [Tooltip("Inland band where the bank cap applies, game metres.")]
        [Range(0f, 12f)] public float bankBandMeters = 12f;
        [Tooltip("Pre-bank height above WaterLevelY where the cap starts fading out (high banks keep their faces).")]
        public float bankHighStart = 4f;
        [Tooltip("Pre-bank height above WaterLevelY where the cap no longer applies.")]
        public float bankHighEnd = 6f;
        [Tooltip("Wading depth below WaterLevelY where the shelf meets the smoothstep to full lake depth.")]
        public float wadeDepth = 2f;
        [Tooltip("Pre-bank game slope where the steep-face exemption starts fading the cap out (rev 7).")]
        public float bankSteepFadeStartDeg = 35f;
        [Tooltip("Pre-bank game slope at and above which the cap no longer applies (rev 7).")]
        public float bankSteepExemptDeg = 40f;
        [Tooltip("1 = exempt steep cells only where ground within the band rises above bankHighEnd (faces of tall banks); 0 = exempt every steep cell.")]
        public int bankSteepExemptHighBanksOnly = 1;
        [Tooltip("No bank cap within this distance of the Buzzard/McCord bluff markers, game metres.")]
        public float bankBluffExclusionMeters = 60f;
        [Tooltip("Cap fades back in over this distance beyond the exclusion, game metres.")]
        public float bankBluffFadeMeters = 20f;

        [Header("Opposite-bank north cut (brief (f), ruling/bells-bend-north-cut)")]
        [Tooltip("Wavy lake edge and capped opposite banks north of the line, outside the neck.")]
        public bool oppositeBankEnabled = true;
        [Range(20f, 30f)] public float oppositeBankSlopeDeg = 25f;
        [Tooltip("Max northward offset of the lake edge from northLineZ, game metres.")]
        public float oppositeBankOffsetMax = 60f;
        [Range(80f, 200f)] public float oppositeBankWavelengthMin = 80f;
        [Range(80f, 200f)] public float oppositeBankWavelengthMax = 200f;
        [Tooltip("Offset tapers to 0 within this distance of each bank crossing.")]
        public float oppositeBankTaper = 100f;
        [Tooltip("Noise seed. Fixed so clean rebuilds reproduce the identical edge.")]
        public int oppositeBankSeed = 1987;

        /// <summary>The 117.3 m pool mapped to Unity Y.</summary>
        public float WaterLevelY => DemToUnityY(poolElevationMeters);

        /// <summary>Unity Y for a real DEM elevation in metres (L2 expected heights).</summary>
        public float DemToUnityY(float demMeters) => (demMeters - datumMeters) * verticalScale;
    }
}
