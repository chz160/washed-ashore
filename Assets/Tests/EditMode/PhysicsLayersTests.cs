using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using WashedAshore.World;

namespace WashedAshore.Tests.EditMode
{
    /// <summary>
    /// Physics layers and the minimal Layer Collision Matrix (#43, td ruling adr/physics-layers-43). The ruled layer
    /// indices, the whole 32 x 32 matrix against the written table (docs/physics-guidelines.md, "Layer Collision Matrix"),
    /// and the named masks in <see cref="WorldLayers"/>. Reads the project settings only; no scene needed.
    /// </summary>
    public class PhysicsLayersTests
    {
        // Existing indices never move (scene YAML stores numbers); 10-15 are new. 3, 6 and 7 stay empty.
        static readonly (string name, int index)[] Ruled =
        {
            ("Default", 0), ("TransparentFX", 1), ("Ignore Raycast", 2), ("Water", 4), ("UI", 5), ("Birds", 8), ("WorldBounds", 9),
            ("Player", 10), ("Wildlife", 11), ("Interactable", 12), ("Prop", 13), ("Hitbox", 14), ("Ragdoll", 15),
        };

        // Every pair that is ON; every other pair is OFF (rule 22). Pairs are unordered.
        static readonly (string a, string b)[] On =
        {
            ("Player", "Default"), ("Player", "WorldBounds"), ("Player", "Prop"), ("Player", "Interactable"),
            ("Default", "Default"), ("Default", "Prop"), ("Default", "Interactable"),
            ("Prop", "Prop"), ("Prop", "Interactable"), ("Interactable", "Interactable"),
        };

        static int Index(string name) => Ruled.First(r => r.name == name).index;

        static bool ExpectedOn(int a, int b) =>
            On.Any(p => (Index(p.a) == a && Index(p.b) == b) || (Index(p.a) == b && Index(p.b) == a));

        [Test]
        public void Layers_ExistAtTheRuledIndex()
        {
            foreach (var (name, index) in Ruled)
                Assert.AreEqual(index, LayerMask.NameToLayer(name), $"layer '{name}' must be index {index}");
            foreach (int free in new[] { 3, 6, 7 })
                Assert.AreEqual(string.Empty, LayerMask.LayerToName(free), $"layer {free} must stay empty");
            Assert.AreEqual(WorldLayers.BuiltInWater, WorldLayers.Water);
            Assert.AreEqual(10, WorldLayers.Player);
            Assert.AreEqual(11, WorldLayers.Wildlife);
            Assert.AreEqual(12, WorldLayers.Interactable);
            Assert.AreEqual(13, WorldLayers.Prop);
            Assert.AreEqual(14, WorldLayers.Hitbox);
            Assert.AreEqual(15, WorldLayers.Ragdoll);
        }

        [Test]
        public void Matrix_EqualsTheRuledTable()
        {
            var wrong = new List<string>();
            for (int a = 0; a < 32; a++)
                for (int b = a; b < 32; b++)
                {
                    bool on = !Physics.GetIgnoreLayerCollision(a, b);
                    if (on != ExpectedOn(a, b))
                        wrong.Add($"{Label(a)} x {Label(b)}: is {(on ? "ON" : "OFF")}, ruled {(on ? "OFF" : "ON")}");
                }
            Assert.IsEmpty(wrong, "Layer Collision Matrix differs from the ruled table:\n" + string.Join("\n", wrong));
        }

        static string Label(int layer)
        {
            string n = LayerMask.LayerToName(layer);
            return string.IsNullOrEmpty(n) ? $"#{layer}" : n;
        }

        [Test]
        public void PlayerCollisionMask_IsExactlyThePlayerRowOfTheMatrix()
        {
            int row = 0;
            for (int other = 0; other < 32; other++)
                if (!Physics.GetIgnoreLayerCollision(WorldLayers.Player, other)) row |= 1 << other;
            Assert.AreEqual(row, WorldLayers.PlayerCollisionMask, "PlayerCollisionMask must equal the Player row (climb fit test)");
        }

        [Test]
        public void QueryMasks_LeaveOutThePlayerAndRobinMaskKeepsIt()
        {
            int player = 1 << WorldLayers.Player;
            Assert.AreEqual(0, WorldLayers.SightMask & player, "SightMask (wildlife, birds, tools)");
            Assert.AreEqual(0, WorldLayers.StanceMask & player, "StanceMask");
            Assert.AreEqual(0, WorldLayers.InteractMask & player, "InteractMask");
            Assert.AreEqual(0, WorldLayers.PlayerCollisionMask & player, "PlayerCollisionMask (Player x Player is OFF)");
            Assert.AreNotEqual(0, WorldLayers.RobinObstacleMask(null) & player, "robins must still treat the player as an obstacle (C2)");
        }

        [Test]
        public void QueryMasks_HoldTheSolidWorldAndNoWaterOrBoundsExceptThePlayerRow()
        {
            int solid = (1 << Index("Default")) | (1 << WorldLayers.Prop) | (1 << WorldLayers.Interactable);
            Assert.AreEqual(solid, WorldLayers.WorldSolidMask);
            Assert.AreEqual(solid, WorldLayers.SightMask);
            Assert.AreEqual(solid, WorldLayers.StanceMask);
            Assert.AreEqual(solid, WorldLayers.InteractMask);
            Assert.AreEqual(0, WorldLayers.SightMask & WorldLayers.NonGroundMask);
            Assert.AreEqual(0, WorldLayers.StanceMask & WorldLayers.NonGroundMask, "stance stays blind to WorldBounds as before");
            Assert.AreEqual(solid | (1 << WorldLayers.WorldBounds), WorldLayers.PlayerCollisionMask);
            Assert.AreEqual(0, WorldLayers.RobinObstacleMask(null) & WorldLayers.NonGroundMask);
            // Wildlife, Birds, Hitbox and Ragdoll are in no query mask (td: they get their own ruling when they get colliders).
            int never = (1 << WorldLayers.Wildlife) | (1 << Index("Birds")) | (1 << WorldLayers.Hitbox) | (1 << WorldLayers.Ragdoll);
            foreach (int mask in new[] { WorldLayers.SightMask, WorldLayers.StanceMask, WorldLayers.InteractMask, WorldLayers.PlayerCollisionMask, WorldLayers.RobinObstacleMask(null) })
                Assert.AreEqual(0, mask & never);
        }

        [Test]
        public void NavBakeMask_IsEveryLayerButWater()
        {
            Assert.AreEqual(~(1 << WorldLayers.Water), WorldLayers.NavBakeMask);
        }

        [Test]
        public void Require_ThrowsForAMissingLayer()
        {
            Assert.Throws<System.InvalidOperationException>(() => WorldLayers.Require("NoSuchLayer43"));
        }

        [Test]
        public void SetLayerRecursive_MovesTheWholeTree()
        {
            var root = new GameObject("root");
            var child = new GameObject("child");
            var grand = new GameObject("grand");
            try
            {
                child.transform.SetParent(root.transform);
                grand.transform.SetParent(child.transform);
                WorldLayers.SetLayerRecursive(root.transform, WorldLayers.Wildlife);
                Assert.AreEqual(WorldLayers.Wildlife, root.layer);
                Assert.AreEqual(WorldLayers.Wildlife, child.layer);
                Assert.AreEqual(WorldLayers.Wildlife, grand.layer);
            }
            finally { Object.DestroyImmediate(root); }
        }
    }
}
