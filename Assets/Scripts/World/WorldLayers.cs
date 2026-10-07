using UnityEngine;

namespace WashedAshore.World
{
    /// <summary>
    /// The project's physics layers, looked up by name so a TagManager reorder fails loudly (water spec W4,
    /// bells-bend-td-note 3 risk 3). Water is the built-in layer 4; WorldBounds holds every invisible
    /// player-only boundary collider (the backstop and the barrier's fence, gate-post and leaf extensions).
    /// Robin, bird and ground/sky casts never hit either.
    /// </summary>
    public static class WorldLayers
    {
        public const string WaterName = "Water";
        public const string WorldBoundsName = "WorldBounds";
        public const int BuiltInWater = 4;

        public static int Water => Require(WaterName);
        public static int WorldBounds => Require(WorldBoundsName);

        /// <summary>Water and WorldBounds: what ground, sky and sightline casts must leave out.</summary>
        public static int NonGroundMask => (1 << Water) | (1 << WorldBounds);

        /// <summary>Unity's default raycast layers minus Water and WorldBounds.</summary>
        public static int SightMask => Physics.DefaultRaycastLayers & ~NonGroundMask;

        public static int Require(string name)
        {
            int layer = LayerMask.NameToLayer(name);
            if (layer < 0) throw new System.InvalidOperationException($"Layer '{name}' is missing from ProjectSettings/TagManager");
            return layer;
        }
    }
}
