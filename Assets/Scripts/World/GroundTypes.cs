namespace WashedAshore.World
{
    /// <summary>
    /// Canonical ground type IDs of the Bells Bend terrain. The IDs are stable (they are the order of
    /// AmbientCgLayers.Specs and the old splat slot numbers) but a tile's alphamap channel order is NOT:
    /// each tile holds only the layers it needs, so always go through <see cref="TerrainGroundMap"/>.
    /// </summary>
    public enum GroundType
    {
        Grass = 0,
        ForestFloor = 1,
        Asphalt = 2,
        Gravel = 3,
        Bank = 4,
        Rock = 5,
        Kudzu = 6,
    }

    public static class GroundTypes
    {
        public const int Count = 7;

        /// <summary>
        /// TerrainLayer asset names per ground type, indexed by <see cref="GroundType"/>. The one table the
        /// editor painter (AmbientCgLayers.Specs keys) and the runtime lookup share. When the ambientCG layers
        /// are replaced, keep these asset names or edit this table.
        /// </summary>
        public static readonly string[] LayerNames =
        {
            "OldFieldGrass", "ForestFloor", "CrackedAsphalt", "OvergrownGravel", "RiverBank", "BluffRock", "KudzuCover",
        };

        public static string NameOf(GroundType g) => LayerNames[(int)g];

        public static bool TryFromLayerName(string layerName, out GroundType g)
        {
            for (int i = 0; i < LayerNames.Length; i++)
                if (LayerNames[i] == layerName) { g = (GroundType)i; return true; }
            g = default;
            return false;
        }
    }
}
