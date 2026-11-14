using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using WashedAshore.World;

namespace WashedAshore.Tests.EditMode
{
    /// <summary>The ground type table: unique names in enum order, a round trip, and the guard that every name still resolves to a TerrainLayer asset.</summary>
    public class GroundTypesTests
    {
        const string LayerRoot = "Assets/World/BellsBend/Art/Layers";

        [Test]
        public void LayerNamesAreUniqueAndMatchTheEnumOrder()
        {
            Assert.AreEqual(GroundTypes.Count, GroundTypes.LayerNames.Length);
            Assert.AreEqual(GroundTypes.Count, System.Enum.GetValues(typeof(GroundType)).Length);
            Assert.AreEqual(GroundTypes.Count, GroundTypes.LayerNames.Distinct().Count());
            for (int i = 0; i < GroundTypes.Count; i++) Assert.AreEqual(GroundTypes.LayerNames[i], GroundTypes.NameOf((GroundType)i));
        }

        [Test]
        public void TryFromLayerNameRoundTrips()
        {
            for (int i = 0; i < GroundTypes.Count; i++)
            {
                Assert.IsTrue(GroundTypes.TryFromLayerName(GroundTypes.LayerNames[i], out var g));
                Assert.AreEqual((GroundType)i, g);
            }
        }

        [Test]
        public void UnknownOrNullNameIsRejected()
        {
            Assert.IsFalse(GroundTypes.TryFromLayerName("NoSuchLayer", out _));
            Assert.IsFalse(GroundTypes.TryFromLayerName(null, out _));
        }

        [Test]
        public void EveryLayerNameResolvesToATerrainLayerAsset()
        {
            // A renamed .terrainlayer would drop grass to 0 everywhere and empty the deer and robin habitat without an error.
            foreach (var name in GroundTypes.LayerNames)
            {
                var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>($"{LayerRoot}/{name}.terrainlayer");
                Assert.IsNotNull(layer, $"{LayerRoot}/{name}.terrainlayer is missing (GroundTypes.LayerNames and the asset names must agree)");
                Assert.AreEqual(name, layer.name);
            }
        }
    }
}
