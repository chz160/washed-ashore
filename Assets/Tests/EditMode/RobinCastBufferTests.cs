using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using WashedAshore.Birds;
using WashedAshore.World;

namespace WashedAshore.Tests.EditMode
{
    /// <summary>
    /// #44 A2: the robin planner's flight-segment cast uses a fixed buffer. A cast that fills it is BLOCKED (director G2), so a
    /// dropped hit can never let a robin fly through something; below the buffer size the answer is the old SphereCastAll rule.
    /// Colliders are built far from the scene on the Default layer and read with a named mask.
    /// </summary>
    public class RobinCastBufferTests
    {
        static readonly Vector3 Origin = new Vector3(40000f, 40000f, 40000f);
        const float Radius = 0.3f;
        const float Length = 60f;
        static readonly Vector3 Dir = Vector3.forward;

        GameObject root;
        int mask;

        [SetUp]
        public void SetUp()
        {
            root = new GameObject("RobinCastBufferTests");
            mask = WorldLayers.WorldSolidMask;
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(root);

        /// <summary>Boxes in a line along the cast, each a hit, under <paramref name="parent"/>.</summary>
        void Boxes(int count, Transform parent)
        {
            for (int i = 0; i < count; i++)
            {
                var go = new GameObject("Box_" + i);
                go.transform.SetParent(parent, false);
                go.transform.position = Origin + Dir * (3f + i * 0.5f);
                go.AddComponent<BoxCollider>().size = new Vector3(0.4f, 0.4f, 0.2f);
            }
            Physics.SyncTransforms();
        }

        /// <summary>The rule the planner had before #44: every hit from SphereCastAll, ignoring the robin's own colliders.</summary>
        bool OldRule(Transform ignore)
        {
            foreach (var hit in Physics.SphereCastAll(Origin, Radius, Dir, Length, mask, QueryTriggerInteraction.Ignore))
                if (!ignore || !hit.collider.transform.IsChildOf(ignore)) return true;
            return false;
        }

        bool NewRule(Transform ignore) => RobinFlightPlanner.CastBlocked(Origin, Radius, Dir, Length, mask, ignore);

        [Test]
        public void BufferIsSizedAndDocumented()
        {
            Assert.AreEqual(64, RobinFlightPlanner.CastBufferSize);
        }

        [Test]
        public void ClearCast_IsNotBlocked_AndNoBufferFill()
        {
            int fills = RobinFlightPlanner.FullBufferHits;
            Assert.IsFalse(OldRule(null));
            Assert.IsFalse(NewRule(null));
            Assert.AreEqual(fills, RobinFlightPlanner.FullBufferHits);
        }

        [TestCase(1)]
        [TestCase(10)]
        [TestCase(RobinFlightPlanner.CastBufferSize - 1)]
        public void BelowTheBufferSize_NewRuleEqualsTheOldRule(int count)
        {
            Boxes(count, root.transform);
            int fills = RobinFlightPlanner.FullBufferHits;
            Assert.IsTrue(OldRule(null), "control: the old rule is blocked");
            Assert.AreEqual(OldRule(null), NewRule(null));
            Assert.AreEqual(fills, RobinFlightPlanner.FullBufferHits, "buffer must not be reported full");
        }

        [Test]
        public void IgnoredHitsBelowTheBufferSize_DoNotBlock_LikeTheOldRule()
        {
            var robin = new GameObject("Robin").transform;
            robin.SetParent(root.transform, false);
            Boxes(RobinFlightPlanner.CastBufferSize - 1, robin);
            Assert.IsFalse(OldRule(robin), "control: old rule ignores the robin's own colliders");
            Assert.IsFalse(NewRule(robin));
        }

        [TestCase(RobinFlightPlanner.CastBufferSize)]
        [TestCase(RobinFlightPlanner.CastBufferSize + 1)]
        [TestCase(RobinFlightPlanner.CastBufferSize + 36)]
        public void FullBuffer_IsBlocked_AndCounted(int count)
        {
            Boxes(count, root.transform);
            int fills = RobinFlightPlanner.FullBufferHits;
            Assert.IsTrue(NewRule(null), "a full buffer blocks the heading");
            Assert.AreEqual(fills + 1, RobinFlightPlanner.FullBufferHits, "the full buffer is detected and counted, not dropped");
        }

        /// <summary>The robin's own (ignored) colliders fill the buffer and a real blocker sits behind them: still blocked (G2), and the old rule agrees.</summary>
        [Test]
        public void IgnoredCollidersFillTheBuffer_WithABlockerBehind_IsBlocked()
        {
            var robin = new GameObject("Robin").transform;
            robin.SetParent(root.transform, false);
            Boxes(RobinFlightPlanner.CastBufferSize, robin);
            var blocker = new GameObject("Blocker");
            blocker.transform.SetParent(root.transform, false);
            blocker.transform.position = Origin + Dir * 40f;
            blocker.AddComponent<BoxCollider>().size = new Vector3(2f, 2f, 0.2f);
            Physics.SyncTransforms();
            int fills = RobinFlightPlanner.FullBufferHits;
            Assert.IsTrue(OldRule(robin), "control: the old rule is blocked by the blocker");
            Assert.IsTrue(NewRule(robin));
            Assert.AreEqual(fills + 1, RobinFlightPlanner.FullBufferHits);
        }

        [Test]
        public void Blocks_ReadsCountNotArrayLength()
        {
            var hits = new RaycastHit[4];
            int fills = RobinFlightPlanner.FullBufferHits;
            Assert.IsFalse(RobinFlightPlanner.Blocks(hits, 0, null));
            Assert.IsTrue(RobinFlightPlanner.Blocks(hits, 4, null));
            Assert.AreEqual(fills + 1, RobinFlightPlanner.FullBufferHits);
        }
    }
}
