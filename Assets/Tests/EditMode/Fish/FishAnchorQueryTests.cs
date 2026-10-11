using NUnit.Framework;
using UnityEngine;
using WashedAshore.Fish;
using WashedAshore.Level;

namespace WashedAshore.Tests.EditMode.Fish
{
    /// <summary>Structure lookup over f-level's anchors: kind mapping, water side, reach, area, and fast rejection.</summary>
    public class FishAnchorQueryTests
    {
        static BellsBendStructureAnchors Anchors()
        {
            var a = ScriptableObject.CreateInstance<BellsBendStructureAnchors>();
            a.anchors = new[]
            {
                // A 100 m bank along z at x = 0, water toward +x.
                new BellsBendStructureAnchors.Anchor { kind = StructureKind.HollowMouth, name = "mouth", reach = 20f, length = 100f,
                    bounds = Rect.MinMaxRect(-20f, -20f, 20f, 120f),
                    points = new[] { new Vector2(0f, 0f), new Vector2(0f, 100f) }, normals = new[] { Vector2.right, Vector2.right } },
                new BellsBendStructureAnchors.Anchor { kind = StructureKind.Slipway, name = "slip", reach = 20f, length = 50f,
                    bounds = Rect.MinMaxRect(-20f, 480f, 20f, 570f),
                    points = new[] { new Vector2(0f, 500f), new Vector2(0f, 550f) }, normals = new[] { Vector2.right, Vector2.right } },
            };
            a.bounds = Rect.MinMaxRect(-20f, -20f, 20f, 570f);
            return a;
        }

        [Test]
        public void MapsKinds_ReturnsAnchorArea_AndRespectsReachAndWaterSide()
        {
            var t = FishPlanTests.Tuning(); // one structure type, levelKinds { "HollowMouth" }
            var q = new FishAnchorQuery(Anchors(), t);
            Assert.AreEqual(1, q.Unmapped, "the slipway has no structure type in this tuning");
            Assert.AreEqual(0, q.StructureAt(10f, 50f, out float area));
            Assert.AreEqual(100f * 20f, area);
            Assert.AreEqual(-1, q.StructureAt(25f, 50f, out _), "beyond reach");
            Assert.AreEqual(-1, q.StructureAt(-5f, 50f, out _), "land side");
            Assert.AreEqual(-1, q.StructureAt(10f, 525f, out _), "unmapped kind");
            Assert.AreEqual(-1, q.StructureAt(500f, 5000f, out _), "outside every anchor");
        }
    }
}
