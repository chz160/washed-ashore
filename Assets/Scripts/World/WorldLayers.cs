using UnityEngine;

namespace WashedAshore.World
{
    /// <summary>
    /// The project's physics layers, looked up by name so a TagManager reorder fails loudly (water spec W4,
    /// bells-bend-td-note 3 risk 3; physics-layers #43, td ruling adr/physics-layers-43). Water is the built-in
    /// layer 4; WorldBounds holds every invisible player-only boundary collider (the backstop and the barrier's
    /// fence, gate-post and leaf extensions). Robin, bird and ground/sky casts never hit either.
    /// Every mask is a union of named layers; there are no layer-number literals and no all-layers masks.
    /// The Layer Collision Matrix (ProjectSettings/DynamicsManager.asset) is tabled in docs/physics-guidelines.md.
    /// </summary>
    public static class WorldLayers
    {
        public const string DefaultName = "Default";
        public const string WaterName = "Water";
        public const string WorldBoundsName = "WorldBounds";
        public const string PlayerName = "Player";
        public const string WildlifeName = "Wildlife";
        public const string InteractableName = "Interactable";
        public const string PropName = "Prop";
        /// <summary>Reserved (#13 multiplayer hitboxes): no object is on it yet.</summary>
        public const string HitboxName = "Hitbox";
        /// <summary>Reserved (death ragdolls): no object is on it yet.</summary>
        public const string RagdollName = "Ragdoll";
        public const int BuiltInWater = 4;

        public static int Water => Require(WaterName);
        public static int WorldBounds => Require(WorldBoundsName);
        public static int Player => Require(PlayerName);
        public static int Wildlife => Require(WildlifeName);
        public static int Interactable => Require(InteractableName);
        public static int Prop => Require(PropName);
        public static int Hitbox => Require(HitboxName);
        public static int Ragdoll => Require(RagdollName);

        /// <summary>Water and WorldBounds: what ground, sky and sightline casts must leave out.</summary>
        public static int NonGroundMask => (1 << Water) | (1 << WorldBounds);

        /// <summary>The static world a body can stand on or be blocked by: Default (terrain, trees, the tower, placed colliders), Prop and Interactable.</summary>
        public static int WorldSolidMask => (1 << Require(DefaultName)) | (1 << Prop) | (1 << Interactable);

        /// <summary>Sightline casts (wildlife, birds, interact, editor and perf probes): the solid world; never the player, Water or WorldBounds.</summary>
        public static int SightMask => WorldSolidMask;

        /// <summary>The stance code's headroom, ground and lie-box queries: the solid world, never WorldBounds (as before).</summary>
        public static int StanceMask => WorldSolidMask;

        /// <summary>The interact ray: the solid world, so props, vegetation and interactables answer it.</summary>
        public static int InteractMask => WorldSolidMask;

        /// <summary>What the player's controller collides with: exactly the Player row of the matrix (the climb fit test uses it).</summary>
        public static int PlayerCollisionMask => WorldSolidMask | (1 << WorldBounds);

        /// <summary>Robin landing and flight obstacles: the solid world, the terrain's layer and the player (C2), never Water or WorldBounds.</summary>
        public static int RobinObstacleMask(Terrain terrain)
        {
            int mask = WorldSolidMask | (1 << Player);
            if (terrain) mask |= 1 << terrain.gameObject.layer;
            return mask & ~NonGroundMask;
        }

        /// <summary>The wildlife NavMesh bake filter: every layer but Water (animals never path on water). A bake filter, not a query; no re-bake in #43.</summary>
        public static int NavBakeMask => ~(1 << Water);

        /// <summary>Puts <paramref name="root"/> and every descendant on <paramref name="layer"/>.</summary>
        public static void SetLayerRecursive(Transform root, int layer)
        {
            if (root == null) return;
            root.gameObject.layer = layer;
            foreach (Transform child in root) SetLayerRecursive(child, layer);
        }

        public static int Require(string name)
        {
            int layer = LayerMask.NameToLayer(name);
            if (layer < 0) throw new System.InvalidOperationException($"Layer '{name}' is missing from ProjectSettings/TagManager");
            return layer;
        }
    }
}
