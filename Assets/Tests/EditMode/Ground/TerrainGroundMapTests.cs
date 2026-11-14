using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using WashedAshore.World;

namespace WashedAshore.Tests.EditMode
{
    /// <summary>TerrainGroundMap: ground types found by TerrainLayer asset name, whatever the tile's channel order.</summary>
    public class TerrainGroundMapTests
    {
        readonly List<Object> made = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in made) if (o) Object.DestroyImmediate(o);
            made.Clear();
        }

        TerrainLayer Layer(string name)
        {
            var l = new TerrainLayer { name = name };
            made.Add(l);
            return l;
        }

        TerrainLayer[] Layers(params GroundType[] order)
        {
            var r = new TerrainLayer[order.Length];
            for (int i = 0; i < r.Length; i++) r[i] = Layer(GroundTypes.NameOf(order[i]));
            return r;
        }

        static float[,,] Alphamap(params float[] texel)
        {
            var a = new float[1, 1, texel.Length];
            for (int i = 0; i < texel.Length; i++) a[0, 0, i] = texel[i];
            return a;
        }

        [Test]
        public void TodaysOrderMapsChannelToGroundId()
        {
            var map = TerrainGroundMap.Build(Layers(GroundType.Grass, GroundType.ForestFloor, GroundType.Asphalt, GroundType.Gravel, GroundType.Bank, GroundType.Rock, GroundType.Kudzu));
            Assert.AreEqual(7, map.LayerCount);
            for (int i = 0; i < GroundTypes.Count; i++) Assert.AreEqual(i, map.Channel((GroundType)i));
        }

        [Test]
        public void ShuffledOrderGivesTheRightChannelPerType()
        {
            var map = TerrainGroundMap.Build(Layers(GroundType.Rock, GroundType.Kudzu, GroundType.Grass));
            Assert.AreEqual(2, map.Channel(GroundType.Grass));
            Assert.AreEqual(0, map.Channel(GroundType.Rock));
            Assert.AreEqual(1, map.Channel(GroundType.Kudzu));
            Assert.IsFalse(map.Has(GroundType.Asphalt));
        }

        [Test]
        public void MissingLayerReadsZero()
        {
            var map = TerrainGroundMap.Build(Layers(GroundType.Grass, GroundType.ForestFloor, GroundType.Asphalt, GroundType.Gravel, GroundType.Bank, GroundType.Rock));
            Assert.IsFalse(map.Has(GroundType.Kudzu));
            Assert.AreEqual(-1, map.Channel(GroundType.Kudzu));
            var a = Alphamap(0.1f, 0.1f, 0.1f, 0.1f, 0.1f, 0.5f);
            Assert.AreEqual(0f, map.Weight(a, 0, 0, GroundType.Kudzu));
            var canonical = new float[GroundTypes.Count];
            for (int i = 0; i < canonical.Length; i++) canonical[i] = 9f;
            map.Expand(a, 0, 0, canonical);
            Assert.AreEqual(0f, canonical[(int)GroundType.Kudzu]);
            Assert.AreEqual(0.5f, canonical[(int)GroundType.Rock]);
        }

        [Test]
        public void NullEntriesUnknownNamesAndDuplicatesAreHandled()
        {
            var layers = new[] { null, Layer("SomethingElse"), Layer(GroundTypes.NameOf(GroundType.Grass)), Layer(GroundTypes.NameOf(GroundType.Grass)) };
            var map = TerrainGroundMap.Build(layers);
            Assert.AreEqual(4, map.LayerCount);
            Assert.AreEqual(2, map.Channel(GroundType.Grass), "the first of a duplicate name wins");
            Assert.AreEqual(0, TerrainGroundMap.Build((TerrainLayer[])null).LayerCount);
        }

        [Test]
        public void WeightIsZeroWhenTheChannelIsBeyondTheArrayDepth()
        {
            var map = TerrainGroundMap.Build(Layers(GroundType.Rock, GroundType.Kudzu, GroundType.Grass));
            Assert.AreEqual(0f, map.Weight(Alphamap(0.5f, 0.5f), 0, 0, GroundType.Grass));
        }

        [Test]
        public void ExpandGivesCanonicalOrderFromAShuffledTile()
        {
            var map = TerrainGroundMap.Build(Layers(GroundType.Rock, GroundType.Kudzu, GroundType.Grass));
            var canonical = new float[GroundTypes.Count];
            map.Expand(Alphamap(0.25f, 0.5f, 0.25f), 0, 0, canonical);
            Assert.AreEqual(0.25f, canonical[(int)GroundType.Grass]);
            Assert.AreEqual(0.25f, canonical[(int)GroundType.Rock]);
            Assert.AreEqual(0.5f, canonical[(int)GroundType.Kudzu]);
            Assert.AreEqual(0f, canonical[(int)GroundType.Gravel]);
        }

        [Test]
        public void DominantPicksTheHeaviestType()
        {
            var map = TerrainGroundMap.Build(Layers(GroundType.Rock, GroundType.Kudzu, GroundType.Grass));
            var g = map.Dominant(Alphamap(0.2f, 0.7f, 0.1f), 0, 0, out float w);
            Assert.AreEqual(GroundType.Kudzu, g);
            Assert.AreEqual(0.7f, w);
            Assert.IsNull(TerrainGroundMap.Build(new TerrainLayer[0]).Dominant(new float[1, 1, 0], 0, 0, out _));
        }
    }
}
