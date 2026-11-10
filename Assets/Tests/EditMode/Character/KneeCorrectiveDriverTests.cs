using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using WashedAshore.Character.Appearance;
using WashedAshore.Character.Editor;

namespace WashedAshore.Tests.EditMode.Character
{
    /// <summary>
    /// KneeCorrectiveDriver (td-49 td-knee-corrective.md; qa rows K1-K4, C1 in part): the smoothstep, the flexion from three joints, a no-op without the shapes,
    /// equal weights on the shadow and the view body at the same pose, and a part swap that does not break the driver. The shapes are synthetic here (a copy of
    /// a Farmer mesh with two empty frames named Knee_L_90 / Knee_R_90); the real shapes come with ta-49's Survivor_Body.fbx.
    /// </summary>
    public class KneeCorrectiveDriverTests
    {
        /// <summary>The Survivor: its Body_ mesh carries the real Knee_L_90 / Knee_R_90 shapes.</summary>
        static CharacterAppearance Fixture()
        {
            var a = AssetDatabase.LoadAssetAtPath<CharacterAppearance>(CharacterAssetBuilder.SurvivorAppearancePath);
            Assert.IsNotNull(a, "the Survivor appearance is missing");
            return a;
        }

        /// <summary>Ensures the body's Body-slot renderer carries both knee shapes: the Survivor's real ones, else a mesh copy with empty frames.</summary>
        static void AddShapes(CharacterBody body)
        {
            var smr = body.Renderers(CharacterSlot.Body)[0];
            if (smr.sharedMesh.GetBlendShapeIndex("Knee_L_90") >= 0 && smr.sharedMesh.GetBlendShapeIndex("Knee_R_90") >= 0) return;
            var mesh = Object.Instantiate(smr.sharedMesh);
            var deltas = new Vector3[mesh.vertexCount];
            mesh.AddBlendShapeFrame("Knee_L_90", 100f, deltas, null, null);
            mesh.AddBlendShapeFrame("Knee_R_90", 100f, deltas, null, null);
            smr.sharedMesh = mesh;
        }

        /// <summary>Puts one leg on a straight vertical line (flex 0) or bends the shin by <paramref name="flex"/> degrees about the body's X axis.</summary>
        static void PoseKnee(CharacterBody body, string side, float flex)
        {
            var hip = body.Bone("UpperLeg." + side);
            var knee = body.Bone("LowerLeg." + side);
            var ankle = body.Bone("LowerLeg." + side + "_end");
            knee.position = hip.position + body.transform.TransformVector(new Vector3(0f, -0.4f, 0f));
            var shin = Quaternion.AngleAxis(flex, body.transform.right) * body.transform.TransformVector(new Vector3(0f, -0.4f, 0f));
            ankle.position = knee.position + shin;
        }

        [Test]
        public void WeightIsASmoothstepFromStartToTargetClampedToZeroToOne()
        {
            Assert.AreEqual(0f, KneeCorrectiveDriver.Weight(0f, 20f, 90f), 1e-6f);
            Assert.AreEqual(0f, KneeCorrectiveDriver.Weight(20f, 20f, 90f), 1e-6f);
            Assert.AreEqual(0.5f, KneeCorrectiveDriver.Weight(55f, 20f, 90f), 1e-6f);
            Assert.AreEqual(1f, KneeCorrectiveDriver.Weight(90f, 20f, 90f), 1e-6f);
            Assert.AreEqual(1f, KneeCorrectiveDriver.Weight(140f, 20f, 90f), 1e-6f);
            Assert.AreEqual(0f, KneeCorrectiveDriver.Weight(-5f, 20f, 90f), 1e-6f);
            float w30 = KneeCorrectiveDriver.Weight(30f, 20f, 90f);
            Assert.Less(w30, 0.1f, "the shape stays small at walking knee angles");
            Assert.Greater(w30, 0f);
        }

        [Test]
        public void FlexIsZeroForAStraightLegAndNinetyForARightAngle()
        {
            Assert.AreEqual(0f, KneeCorrectiveDriver.FlexDegrees(new Vector3(0, 1, 0), new Vector3(0, 0.5f, 0), new Vector3(0, 0, 0)), 1e-3f);
            Assert.AreEqual(90f, KneeCorrectiveDriver.FlexDegrees(new Vector3(0, 1, 0), new Vector3(0, 0.5f, 0), new Vector3(0, 0.5f, 0.5f)), 1e-3f);
        }

        [Test]
        public void NoComponentIsAddedAndNothingHappensWhenTheBodyHasNoKneeShapes()
        {
            var body = CharacterAssembler.Assemble(Fixture());
            try
            {
                // take the Survivor's shapes away: a copy of the body mesh without blend shapes, and no driver
                Object.DestroyImmediate(body.GetComponent<KneeCorrectiveDriver>());
                var smr = body.Renderers(CharacterSlot.Body)[0];
                var plain = Object.Instantiate(smr.sharedMesh);
                plain.ClearBlendShapes();
                smr.sharedMesh = plain;
                Assert.IsNull(KneeCorrectiveDriver.AddTo(body));
                Assert.IsNull(body.GetComponent<KneeCorrectiveDriver>());
            }
            finally { Object.DestroyImmediate(body.gameObject); }
        }

        [Test]
        public void WeightIsZeroStandingAndOneAtTheTargetFlexOnBothKnees()
        {
            var body = CharacterAssembler.Assemble(Fixture());
            try
            {
                AddShapes(body);
                var driver = KneeCorrectiveDriver.AddTo(body);
                Assert.IsNotNull(driver);
                Assert.AreEqual(2, driver.KneeCount);
                PoseKnee(body, "L", 0f);
                PoseKnee(body, "R", 0f);
                driver.Apply();
                Assert.AreEqual(0f, driver.LastWeight("L"), 1e-4f);
                Assert.AreEqual(0f, driver.LastWeight("R"), 1e-4f);
                var smr = body.Renderers(CharacterSlot.Body)[0];
                Assert.AreEqual(0f, smr.GetBlendShapeWeight(smr.sharedMesh.GetBlendShapeIndex("Knee_L_90")), 1e-3f);
                PoseKnee(body, "L", 90f);
                PoseKnee(body, "R", 90f);
                driver.Apply();
                Assert.AreEqual(1f, driver.LastWeight("L"), 1e-4f);
                Assert.AreEqual(100f, smr.GetBlendShapeWeight(smr.sharedMesh.GetBlendShapeIndex("Knee_L_90")), 1e-2f, "weight 1 = blend shape weight 100");
                Assert.AreEqual(100f, smr.GetBlendShapeWeight(smr.sharedMesh.GetBlendShapeIndex("Knee_R_90")), 1e-2f);
                PoseKnee(body, "L", 55f);
                driver.Apply();
                Assert.AreEqual(KneeCorrectiveDriver.Weight(55f, 20f, 90f), driver.LastWeight("L"), 1e-3f);
                Assert.AreEqual(1f, driver.LastWeight("R"), 1e-4f, "the other knee is unchanged");
            }
            finally { Object.DestroyImmediate(body.gameObject); }
        }

        [Test]
        public void ShadowAndViewBodyGiveTheSameWeightAtTheSamePose()
        {
            var shadow = CharacterAssembler.Assemble(Fixture());
            var view = CharacterAssembler.AssembleViewCopy(shadow);
            try
            {
                AddShapes(shadow);
                AddShapes(view.Body);
                var a = KneeCorrectiveDriver.AddTo(shadow);
                var b = KneeCorrectiveDriver.AddTo(view.Body);
                Assert.IsNotNull(a);
                Assert.IsNotNull(b);
                foreach (float flex in new[] { 0f, 25f, 45f, 70f, 90f, 120f })
                {
                    foreach (var body in new[] { shadow, view.Body })
                    {
                        PoseKnee(body, "L", flex);
                        PoseKnee(body, "R", flex * 0.5f);
                    }
                    a.Apply();
                    b.Apply();
                    Assert.AreEqual(a.LastWeight("L"), b.LastWeight("L"), 0.001f, "left knee at " + flex);
                    Assert.AreEqual(a.LastWeight("R"), b.LastWeight("R"), 0.001f, "right knee at " + flex);
                }
            }
            finally
            {
                if (view) Object.DestroyImmediate(view.gameObject);
                Object.DestroyImmediate(shadow.gameObject);
            }
        }

        [Test]
        public void TheTargetFlexComesFromTheAppearanceDataNotFromAComponentDefault()
        {
            var data = Object.Instantiate(Fixture());
            data.kneeShapeStartDegrees = 20f;
            data.kneeShapeTargetDegrees = 120f;
            CharacterBody body = null;
            try
            {
                body = CharacterAssembler.Assemble(data);
                AddShapes(body);
                var driver = KneeCorrectiveDriver.AddTo(body);
                Assert.AreEqual(120f, driver.TargetDegrees, 1e-4f);
                PoseKnee(body, "L", 120f);
                driver.Apply();
                Assert.AreEqual(1f, driver.LastWeight("L"), 1e-4f, "weight 1 at the authored flex");
                PoseKnee(body, "L", 90f);
                driver.Apply();
                Assert.Less(driver.LastWeight("L"), 1f, "less than 1 at 90 when the target is 120");
                Assert.AreEqual(KneeCorrectiveDriver.Weight(90f, 20f, 120f), driver.LastWeight("L"), 1e-3f);
            }
            finally
            {
                if (body) Object.DestroyImmediate(body.gameObject);
                Object.DestroyImmediate(data);
            }
        }

        [Test]
        public void WeightsAreWrittenAtAssemblyNotOnlyAtTheFirstLateUpdate()
        {
            var body = CharacterAssembler.Assemble(Fixture());
            try
            {
                var driver = body.GetComponent<KneeCorrectiveDriver>();
                Assert.IsNotNull(driver, "the Survivor body gets the driver at assembly");
                var smr = body.Renderers(CharacterSlot.Body)[0];
                foreach (var shape in new[] { "Knee_L_90", "Knee_R_90" })
                    Assert.AreEqual(0f, smr.GetBlendShapeWeight(smr.sharedMesh.GetBlendShapeIndex(shape)), 1e-3f, shape + ": the FBX default weight is 100, the rest pose needs 0 before any LateUpdate");
            }
            finally { Object.DestroyImmediate(body.gameObject); }
        }

        [Test]
        public void ASwapOfTheBodyPartRebindsWithoutAnError()
        {
            var body = CharacterAssembler.Assemble(Fixture());
            try
            {
                AddShapes(body);
                var driver = KneeCorrectiveDriver.AddTo(body);
                Assert.AreEqual(2, driver.KneeCount);
                Assert.IsTrue(body.SetSlot(CharacterSlot.Body, Fixture().body));   // a fresh renderer from the same part model: the shapes come with it
                Assert.AreEqual(2, driver.KneeCount, "the driver rebinds to the fresh renderer");
                var smr = body.Renderers(CharacterSlot.Body)[0];
                foreach (var shape in new[] { "Knee_L_90", "Knee_R_90" })
                    Assert.AreEqual(0f, smr.GetBlendShapeWeight(smr.sharedMesh.GetBlendShapeIndex(shape)), 1e-3f, shape + ": zero right after the rebind (the fresh mesh's default weight is 100)");
                driver.Apply();   // must not throw
            }
            finally { Object.DestroyImmediate(body.gameObject); }
        }
    }
}
