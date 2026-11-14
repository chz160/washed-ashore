using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using WashedAshore.Wildlife;
using WashedAshore.World;

namespace WashedAshore.Tests.EditMode
{
    /// <summary>
    /// HabitatGround reads ground weights by TerrainLayer asset, so a tile's slot order does not matter. The test tiles sit 100 km out,
    /// far from any open Bells Bend scene, so the real tiles never fall inside the window.
    /// </summary>
    public class HabitatGroundSplatTests
    {
        const float Far = 100000f, TileSize = 100f;
        const int Res = 16;
        readonly List<Object> made = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            for (int i = made.Count - 1; i >= 0; i--) if (made[i]) Object.DestroyImmediate(made[i]); // GameObjects before their TerrainData
            made.Clear();
        }

        /// <summary>A tile at (x0, Far) whose layers are the given types in the given slot order; fill(layer slot) gives each slot's weight everywhere.</summary>
        Terrain Tile(float x0, GroundType[] order, System.Func<int, float> fill)
        {
            var td = new TerrainData { heightmapResolution = 33, size = new Vector3(TileSize, 10f, TileSize), alphamapResolution = Res };
            made.Add(td);
            var layers = new TerrainLayer[order.Length];
            for (int i = 0; i < order.Length; i++) { layers[i] = new TerrainLayer { name = GroundTypes.NameOf(order[i]) }; made.Add(layers[i]); }
            td.terrainLayers = layers;
            var a = new float[Res, Res, order.Length];
            for (int y = 0; y < Res; y++)
                for (int x = 0; x < Res; x++)
                    for (int l = 0; l < order.Length; l++) a[y, x, l] = fill(l);
            td.SetAlphamaps(0, 0, a);
            var go = Terrain.CreateTerrainGameObject(td);
            go.transform.position = new Vector3(x0, 0f, Far);
            made.Add(go);
            return go.GetComponent<Terrain>();
        }

        static HabitatGround Ground() => HabitatGround.Around(new Vector3(Far + 50f, 0f, Far + 50f), 0f);

        [Test]
        public void GrassIsFoundWhateverSlotItSitsIn()
        {
            Tile(Far, new[] { GroundType.ForestFloor, GroundType.Rock, GroundType.Grass }, l => l == 2 ? 0.7f : l == 0 ? 0.2f : 0.1f);
            var g = Ground();
            Assert.AreEqual(1, g.Tiles.Count);
            var p = new Vector3(Far + 50f, 0f, Far + 50f);
            Assert.AreEqual(0.7f, g.Grass(p), 0.01f);
            Assert.AreEqual(0.7f, g.Splat(p, GroundType.Grass), 0.01f);
            Assert.AreEqual(0.2f, g.Splat(p, GroundType.ForestFloor), 0.01f);
        }

        [Test]
        public void ATileWithoutAGrassLayerReadsZeroWithoutWarning()
        {
            Tile(Far, new[] { GroundType.ForestFloor, GroundType.Rock }, l => 0.5f);
            var g = Ground();
            var p = new Vector3(Far + 50f, 0f, Far + 50f);
            Assert.AreEqual(0f, g.Grass(p));
            Assert.AreEqual(0.5f, g.Splat(p, GroundType.Rock), 0.01f);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void ATileWhoseLayersAreAllUnknownWarnsOnce()
        {
            var td = new TerrainData { heightmapResolution = 33, size = new Vector3(TileSize, 10f, TileSize), alphamapResolution = Res };
            made.Add(td);
            var layer = new TerrainLayer { name = "RenamedGrass" };
            made.Add(layer);
            td.terrainLayers = new[] { layer };
            var go = Terrain.CreateTerrainGameObject(td);
            go.transform.position = new Vector3(Far, 0f, Far);
            made.Add(go);
            var g = Ground();
            var p = new Vector3(Far + 50f, 0f, Far + 50f);
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("none named in GroundTypes.LayerNames"));
            Assert.AreEqual(0f, g.Grass(p));
            Assert.AreEqual(0f, g.Grass(p));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void SharedEdgeGrassMatchesAcrossTilesWithDifferentOrders()
        {
            // Read each tile's own channel: HabitatGround returns the first tile that holds a shared-edge point, so it could not tell the two sides apart.
            var a = Tile(Far, new[] { GroundType.Grass, GroundType.Rock }, l => 0.5f);
            var b = Tile(Far + TileSize, new[] { GroundType.Rock, GroundType.Grass, GroundType.Kudzu }, l => l == 1 ? 0.5f : l == 0 ? 0.3f : 0.2f);
            var da = a.terrainData;
            var db = b.terrainData;
            var aa = da.GetAlphamaps(0, 0, Res, Res);
            var ab = db.GetAlphamaps(0, 0, Res, Res);
            float east = TerrainGroundMap.Build(da).Weight(aa, 5, Res - 1, GroundType.Grass);
            float west = TerrainGroundMap.Build(db).Weight(ab, 5, 0, GroundType.Grass);
            Assert.AreEqual(east, west, 1e-6f);
            Assert.AreEqual(0.5f, east, 0.01f);
        }
    }
}
