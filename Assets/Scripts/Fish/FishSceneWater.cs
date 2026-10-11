using UnityEngine;
using WashedAshore.Gameplay;
using WashedAshore.Level;
using WashedAshore.World;

namespace WashedAshore.Fish
{
    /// <summary>Structure lookup: the FishTuning.structures index at a water point and its anchor's area, or -1.</summary>
    public delegate int FishStructureQuery(float x, float z, out float anchorArea);

    /// <summary>
    /// The scene's water for fish (adr/fish-1 T2-T4): bed from TerrainQuery.TryGroundHeight (exact, unsaturated; no
    /// tile = land, never the nearest-edge fallback), surface from WaterBody (WaterLevelY + WaterMotion at the water
    /// clock). WaterMaps is never read: it is GPU-only and saturates at 4 m. LevelMaps shore distance is read only for a
    /// band's outer edge (ruling/fish-depth-bands rev 4). With <c>maxZ</c> set (MapConfig.northLineZ), water north of
    /// it counts as land: no fish are planned or simulated there (FishTuning.southOfNorthLineOnly).
    /// </summary>
    public sealed class FishSceneWater : IFishWater
    {
        readonly MapConfig map;
        readonly BellsBendLevelMaps levelMaps;
        readonly float maxZ;
        readonly FishStructureQuery structure;

        public FishSceneWater(MapConfig map, BellsBendLevelMaps levelMaps, float maxZ, FishStructureQuery structure)
        {
            this.map = map;
            this.levelMaps = levelMaps;
            this.maxZ = maxZ;
            this.structure = structure;
        }

        public float ShoreDistance(float x, float z) => levelMaps.ShoreDistance(new Vector3(x, 0f, z));

        public float WaterLevelY => map ? map.WaterLevelY : 0f;

        public bool TryBed(float x, float z, out float bedY)
        {
            if (z > maxZ) { bedY = 0f; return false; }
            return TerrainQuery.TryGroundHeight(new Vector3(x, 0f, z), out bedY);
        }

        public int StructureAt(float x, float z, out float anchorArea)
        {
            anchorArea = 0f;
            return structure != null ? structure(x, z, out anchorArea) : -1;
        }

        /// <summary>The visible surface at (x, z) now: WaterBody when one is active, else the flat level.</summary>
        public float SurfaceY(float x, float z)
        {
            var body = WaterBody.Active;
            return body ? body.SurfaceY(x, z) : WaterLevelY;
        }
    }
}
