using NUnit.Framework;
using WashedAshore.World;

namespace WashedAshore.Tests.EditMode
{
    /// <summary>TerrainLayerPlan: which layers a tile keeps, and that shared-border weights come out bit-identical on both sides.</summary>
    public class TerrainLayerPlanTests
    {
        const int Grass = 0, Forest = 1, Asphalt = 2, Rock = 5, Kudzu = 6;

        static float[] Vec(params (int id, float v)[] entries)
        {
            var w = new float[GroundTypes.Count];
            foreach (var (id, v) in entries) w[id] = v;
            return w;
        }

        /// <summary>The kept layers' output expanded back to canonical IDs (0 for dropped layers).</summary>
        static float[] Canonical(float[] restricted, uint mask)
        {
            var c = new float[GroundTypes.Count];
            var ids = TerrainLayerPlan.IdsOf(mask);
            for (int k = 0; k < ids.Length; k++) c[ids[k]] = restricted[k];
            return c;
        }

        [Test]
        public void PrefilterZeroesDustAndRenormalises()
        {
            var w = Vec((Grass, 0.6f), (Forest, 0.4f), (Rock, TerrainLayerPlan.EdgeEps * 0.5f));
            TerrainLayerPlan.Prefilter(w);
            Assert.AreEqual(0f, w[Rock]);
            float s = 0f;
            foreach (var v in w) s += v;
            Assert.AreEqual(1f, s, 1e-6f);
            Assert.AreEqual(0.6f, w[Grass], 1e-3f);
        }

        [Test]
        public void SelectKeepsAtThresholdAndDropsJustBelow()
        {
            var max = Vec((Grass, 1f), (Forest, TerrainLayerPlan.DropBelow), (Rock, TerrainLayerPlan.DropBelow - 0.0001f));
            var kept = TerrainLayerPlan.Select(max, 0u);
            Assert.AreEqual(new[] { Grass, Forest }, TerrainLayerPlan.IdsOf(kept));
        }

        [Test]
        public void SelectKeepsATinyLayerThatTouchesAnEdge()
        {
            var max = Vec((Grass, 1f), (Kudzu, 0.01f));
            Assert.AreEqual(new[] { Grass }, TerrainLayerPlan.IdsOf(TerrainLayerPlan.Select(max, TerrainLayerPlan.EdgeMask(Vec((Grass, 1f))))));
            var edge = TerrainLayerPlan.EdgeMask(Vec((Grass, 0.99f), (Kudzu, 0.01f)));
            Assert.AreEqual(new[] { Grass, Kudzu }, TerrainLayerPlan.IdsOf(TerrainLayerPlan.Select(max, edge)));
        }

        [Test]
        public void RestrictIsInIdOrderSumsToOneAndNeverWritesDroppedLayers()
        {
            var w = Vec((Grass, 0.5f), (Forest, 0.01f), (Asphalt, 0.2f), (Kudzu, 0.29f));
            uint mask = (1u << Grass) | (1u << Asphalt) | (1u << Kudzu);
            var dst = new float[TerrainLayerPlan.Count(mask)];
            TerrainLayerPlan.Restrict(w, mask, dst);
            Assert.AreEqual(3, dst.Length);
            Assert.AreEqual(0.5f / 0.99f, dst[0], 1e-6f);   // Grass
            Assert.AreEqual(0.2f / 0.99f, dst[1], 1e-6f);   // Asphalt: ID order, not weight order
            Assert.AreEqual(0.29f / 0.99f, dst[2], 1e-6f);  // Kudzu
            Assert.AreEqual(1f, dst[0] + dst[1] + dst[2], 1e-6f);
        }

        [Test]
        public void ResultIsDeterministic()
        {
            var w = Vec((Grass, 0.3f), (Forest, 0.3f), (Rock, 0.4f));
            var a = new float[3];
            var b = new float[3];
            TerrainLayerPlan.Restrict(w, 0b100011u, a);
            TerrainLayerPlan.Restrict(w, 0b100011u, b);
            CollectionAssert.AreEqual(a, b);
        }

        [Test]
        public void TileWithMoreThanFourLayersKeepsThemAll()
        {
            var max = Vec((0, 0.3f), (1, 0.2f), (2, 0.2f), (3, 0.1f), (4, 0.1f), (5, 0.1f));
            uint mask = TerrainLayerPlan.Select(max, 0u);
            Assert.AreEqual(6, TerrainLayerPlan.Count(mask));
            Assert.Greater(TerrainLayerPlan.Count(mask), TerrainLayerPlan.PassLimit);
        }

        [Test]
        public void SharedEdgeWeightsAreBitIdenticalWhenTheTilesKeepDifferentInteriorSets()
        {
            // One shared raw edge texel, prefiltered once (the weight is a pure function of the world point).
            var edgeTexel = Vec((Grass, 0.55f), (Forest, 0.30f), (Kudzu, 0.15f));
            TerrainLayerPlan.Prefilter(edgeTexel);
            uint edgeMask = TerrainLayerPlan.EdgeMask(edgeTexel);

            // Tile A also keeps Rock for interior reasons; tile B keeps Asphalt. Both see the same edge.
            var maxA = Vec((Grass, 1f), (Forest, 0.5f), (Kudzu, 0.15f), (Rock, 0.8f));
            var maxB = Vec((Grass, 0.9f), (Forest, 0.3f), (Kudzu, 0.2f), (Asphalt, 0.6f));
            uint maskA = TerrainLayerPlan.Select(maxA, edgeMask), maskB = TerrainLayerPlan.Select(maxB, edgeMask);
            Assert.AreNotEqual(maskA, maskB);

            var a = new float[TerrainLayerPlan.Count(maskA)];
            var b = new float[TerrainLayerPlan.Count(maskB)];
            TerrainLayerPlan.Restrict(edgeTexel, maskA, a);
            TerrainLayerPlan.Restrict(edgeTexel, maskB, b);
            var ca = Canonical(a, maskA);
            var cb = Canonical(b, maskB);
            for (int i = 0; i < GroundTypes.Count; i++)
                Assert.AreEqual(System.BitConverter.SingleToInt32Bits(ca[i]), System.BitConverter.SingleToInt32Bits(cb[i]), $"ground {i} differs across the border");
        }
    }
}
