using UnityEngine;

namespace WashedAshore.Level
{
    // Written by the water build (BellsBendWater.Build) on the Water root: what the per-tile water was built against.
    public class BellsBendWaterInfo : MonoBehaviour
    {
        [Tooltip("First 16 hex chars of tools' lakeMaskSha256 (Data/terrain/build/report.json) at build time.")]
        public string lakeMaskSha16;
        [Tooltip("MapConfig.WaterLevelY at build time.")]
        public float waterLevelY;
        public float tileSize;
        public Vector2 gridOrigin;
        public int tilesX, tilesZ;
    }
}
