using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using WashedAshore.Wildlife.Editor;
using Decision = WashedAshore.Wildlife.Editor.ModelFacingPostprocessor.Decision;

namespace WashedAshore.Tests.EditMode
{
    /// <summary>
    /// AC1a (spec Amendment 3): ModelFacingPostprocessor detects facing by the left/right bone rule, falls back to
    /// head minus hips, and otherwise leaves the model alone. The project has no real +Z or ambiguous rig, so the
    /// keep/ambiguous cases run on synthetic hierarchies built here (HideAndDontSave, destroyed in TearDown).
    /// The real-asset cases check that the 12 Quaternius animals were flipped by L/R and now read +Z.
    /// </summary>
    public class ModelFacingDetectionTests
    {
        const string AnimalsFbx = "Assets/ThirdParty/Quaternius/Animals/FBX";
        readonly List<Object> made = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in made) if (o) Object.DestroyImmediate(o);
            made.Clear();
        }

        /// <summary>A skinned rig. side +1 puts the left bones at +X (faces -Z), -1 at -X (faces +Z), 0 = no L/R
        /// bones; lrSpread is the half distance between the L and R bones.</summary>
        Transform Rig(int side, Vector3 headOffset, string leftName = "Leg.L", string rightName = "Leg.R",
                      float lrSpread = 0.2f, bool withRight = true)
        {
            var root = new GameObject("Root") { hideFlags = HideFlags.HideAndDontSave };
            made.Add(root);
            var arm = Child(root.transform, "Armature", Vector3.zero);
            var hips = Child(arm, "Hips", new Vector3(0f, 1f, 0f));
            var head = Child(hips, "Head", headOffset);
            if (side != 0)
            {
                Child(hips, leftName, new Vector3(lrSpread * side, -0.5f, 0f));
                if (withRight) Child(hips, rightName, new Vector3(-lrSpread * side, -0.5f, 0f));
            }
            // Fixes the skeleton's XZ extent (epsilon) independently of the L/R spread.
            Child(hips, "Tail", new Vector3(0f, 0f, 1f));
            Child(hips, "Snout", new Vector3(0f, 0f, -1f));
            var mesh = Child(root.transform, "Mesh", Vector3.zero);
            var smr = mesh.gameObject.AddComponent<SkinnedMeshRenderer>();
            smr.rootBone = hips;
            smr.bones = new[] { hips, head };
            return root.transform;
        }

        static Transform Child(Transform parent, string name, Vector3 localPosition)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = localPosition;
            return t;
        }

        static List<(Vector3, Quaternion)> Pose(Transform root) =>
            root.GetComponentsInChildren<Transform>(true).Select(t => (t.localPosition, t.localRotation)).ToList();

        static Vector3 HeadInRoot(Transform root) =>
            root.InverseTransformPoint(ModelFacingPostprocessor.FindHead(root).position);

        // ---- Synthetic fixtures -----------------------------------------------------------------------

        [Test]
        public void LeftBonesAtPlusX_FlipsByLeftRight()
        {
            var r = ModelFacingPostprocessor.Detect(Rig(+1, new Vector3(0f, 0.3f, -1f)));
            Assert.AreEqual(Decision.Flip, r.Value, ModelFacingPostprocessor.Describe(r));
            Assert.AreEqual("LR", r.decidedBy);
            Assert.AreEqual("L-R", r.lrForm);
            Assert.Greater(r.lrMeasure, r.epsilon);
        }

        [Test]
        public void Fixture1_LeftBonesAtMinusX_KeepsAndIsUnchanged()
        {
            var root = Rig(-1, new Vector3(0f, 0.3f, 1f));
            var before = Pose(root);
            var r = ModelFacingPostprocessor.Detect(root);
            Assert.AreEqual(Decision.Keep, r.Value, ModelFacingPostprocessor.Describe(r));
            Assert.AreEqual("LR", r.decidedBy);
            Assert.Less(r.lrMeasure, -r.epsilon);
            CollectionAssert.AreEqual(before, Pose(root), "Detect must not change the hierarchy");
        }

        [Test]
        public void Fixture2_NoLeftRightBonesAndSidewaysHead_IsAmbiguousAndUnchanged(
            [Values(1f, -1f)] float headX)
        {
            var root = Rig(0, new Vector3(headX, 0.3f, 0f));
            var before = Pose(root);
            var r = ModelFacingPostprocessor.Detect(root);
            Assert.AreEqual(Decision.Ambiguous, r.Value, ModelFacingPostprocessor.Describe(r));
            Assert.AreEqual("none", r.decidedBy);
            Assert.AreEqual("none", r.lrForm);
            Assert.IsNotEmpty(r.reason, "An ambiguous result must carry the reason the import warning logs");
            StringAssert.Contains("x " + headX.ToString("F2"), ModelFacingPostprocessor.Describe(r),
                "The warning text must include the measured head (x, z)");
            CollectionAssert.AreEqual(before, Pose(root));
        }

        [Test]
        public void UprightHeadWithoutLeftRight_IsAmbiguous()
        {
            var r = ModelFacingPostprocessor.Detect(Rig(0, new Vector3(0f, 0.7f, 0.01f)));
            Assert.AreEqual(Decision.Ambiguous, r.Value, ModelFacingPostprocessor.Describe(r));
        }

        [Test]
        public void NoLeftRightBones_FallsBackToHead([Values(-1f, 1f)] float headZ)
        {
            var r = ModelFacingPostprocessor.Detect(Rig(0, new Vector3(0f, 0.3f, headZ)));
            Assert.AreEqual(headZ < 0f ? Decision.Flip : Decision.Keep, r.Value, ModelFacingPostprocessor.Describe(r));
            Assert.AreEqual("head", r.decidedBy);
        }

        [Test]
        public void FaceBoneWithoutLeftRight_FallsBackToHead([Values(-1f, 1f)] float headZ)
        {
            // The Quaternius fish rigs name their head bone "Face" and have no L/R bones (Fish1).
            var root = Rig(0, new Vector3(0f, 0.3f, headZ));
            root.Find("Armature/Hips/Head").name = "Face";
            var r = ModelFacingPostprocessor.Detect(root);
            Assert.AreEqual(headZ < 0f ? Decision.Flip : Decision.Keep, r.Value, ModelFacingPostprocessor.Describe(r));
            Assert.AreEqual("head", r.decidedBy);
            Assert.AreEqual("Face", r.headBone);
        }

        [Test]
        public void LeftRightWinsOverADisagreeingHead()
        {
            // L bones at +X say -Z (flip); the head at +Z says keep. L/R decides, and the reason records the clash.
            var r = ModelFacingPostprocessor.Detect(Rig(+1, new Vector3(0f, 0.3f, 1f), "Leg_L", "Leg_R"));
            Assert.AreEqual(Decision.Flip, r.Value, ModelFacingPostprocessor.Describe(r));
            Assert.AreEqual("LR", r.decidedBy);
            Assert.IsNotEmpty(r.reason);
        }

        [Test]
        public void LeftRightWithinEpsilon_DefersToHead()
        {
            // Skeleton XZ extent 2 m -> epsilon 0.1 m; L-R measure 2 x 0.02 = 0.04 m sits inside it.
            var r = ModelFacingPostprocessor.Detect(Rig(+1, new Vector3(0f, 0.3f, 1f), lrSpread: 0.02f));
            Assert.Less(Mathf.Abs(r.lrMeasure), r.epsilon, ModelFacingPostprocessor.Describe(r));
            Assert.AreEqual(Decision.Keep, r.Value);
            Assert.AreEqual("head", r.decidedBy);
        }

        [Test]
        public void LeftOnlyRig_UsesLeftOnlyForm()
        {
            var r = ModelFacingPostprocessor.Detect(Rig(+1, new Vector3(0f, 0.3f, -1f), withRight: false));
            Assert.AreEqual("L-only", r.lrForm);
            Assert.AreEqual(Decision.Flip, r.Value, ModelFacingPostprocessor.Describe(r));
        }

        [TestCase("Leg.L", -1)] [TestCase("Leg_L", -1)] [TestCase("ear.l", -1)] [TestCase("FF.L_end", -1)]
        [TestCase("LeftArm", -1)] [TestCase("Left_Hand", -1)] [TestCase("Left.003", -1)] [TestCase("Left", -1)]
        [TestCase("mixamorig:LeftUpLeg", -1)] [TestCase("Leg.R", 1)] [TestCase("RightFoot", 1)] [TestCase("Ear1.R", 1)]
        [TestCase("Leftover", 0)] [TestCase("Rightful", 0)] [TestCase("Tail", 0)] [TestCase("Head", 0)]
        public void BoneSide_MatchesTheSpecPatterns(string name, int side) =>
            Assert.AreEqual(side, ModelFacingPostprocessor.BoneSide(name));

        [Test]
        public void Flip_IsIdempotent()
        {
            var root = Rig(+1, new Vector3(0f, 0.3f, -1f));
            var r = ModelFacingPostprocessor.Detect(root);
            Assert.AreEqual(Decision.Flip, r.Value);
            Assert.Less(HeadInRoot(root).z, 0f);

            ModelFacingPostprocessor.TurnNodes(root, r.turnedPaths);
            var head1 = HeadInRoot(root);
            Assert.Greater(head1.z, 0f, "The turn must bring the head to +Z");
            var r2 = ModelFacingPostprocessor.Detect(root);
            Assert.AreEqual(Decision.Keep, r2.Value, "A turned rig must read Keep, so a re-run never double-flips");

            var before = Pose(root);
            if (r2.Value == Decision.Flip) ModelFacingPostprocessor.TurnNodes(root, r2.turnedPaths);
            CollectionAssert.AreEqual(before, Pose(root));
            Assert.That(Vector3.Distance(head1, HeadInRoot(root)), Is.LessThan(1e-5f));
        }

        // ---- Skip rules -------------------------------------------------------------------------------

        [Test]
        public void NonFbx_IsSkipped()
        {
            Assert.AreEqual("not FBX", ModelFacingPostprocessor.SkipReason("Assets/Birds/Robin.glb", null));
            Assert.AreEqual("not FBX", ModelFacingPostprocessor.SkipReason("Assets/Birds/Robin.gltf", null));
            Assert.IsNull(ModelFacingPostprocessor.SkipReason("Assets/X/Model.FBX", null));
        }

        [Test]
        public void HumanoidRig_IsSkipped()
        {
            // The project has no Humanoid model. Never flip a real importer to Human to test this: that dirties its
            // avatar settings, and a later reimport writes them into the .meta.
            string path = AnimalsFbx + "/Fox.fbx";
            Assert.IsNull(ModelFacingPostprocessor.SkipReason(path, AssetImporter.GetAtPath(path)), "a Generic FBX rig is in scope");
            Assert.AreEqual("Humanoid rig", ModelFacingPostprocessor.SkipReason(path, ModelImporterAnimationType.Human));
            Assert.IsNull(ModelFacingPostprocessor.SkipReason(path, ModelImporterAnimationType.Generic));
        }

        // ---- The real Quaternius animals -------------------------------------------------------------

        static string[] AnimalPaths() =>
            AssetDatabase.FindAssets("t:Model", new[] { AnimalsFbx }).Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => p.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase)).OrderBy(p => p).ToArray();

        [Test]
        public void AllTwelveQuaterniusAnimals_WereFlippedByLeftRight_AndNowFacePlusZ()
        {
            var paths = AnimalPaths();
            Assert.AreEqual(12, paths.Length, string.Join(", ", paths));
            foreach (var path in paths)
            {
                var record = AssetDatabase.LoadAllAssetsAtPath(path).OfType<TextAsset>()
                    .FirstOrDefault(a => a.name == ModelFacingPostprocessor.RecordName);
                Assert.IsNotNull(record, $"{path}: no import record (not imported by ModelFacingPostprocessor v2?)");
                var imported = JsonUtility.FromJson<ModelFacingPostprocessor.Result>(record.text);
                Assert.AreEqual(Decision.Flip, imported.Value, $"{path}: {ModelFacingPostprocessor.Describe(imported)}");
                Assert.AreEqual("LR", imported.decidedBy, path);
                Assert.AreEqual("L-R", imported.lrForm, path);
                Assert.IsTrue(imported.applied, path);

                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                var now = ModelFacingPostprocessor.Detect(go.transform);
                Assert.AreEqual(Decision.Keep, now.Value, $"{path} after import: {ModelFacingPostprocessor.Describe(now)}");
                Assert.Greater(now.z, 0.9f, $"{path}: head is not at +Z after import");
            }
        }

        [Test]
        public void ReimportingTwice_GivesIdenticalHeadDirection()
        {
            string path = AnimalsFbx + "/Fox.fbx";
            Vector3 Measure()
            {
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
                return HeadInRoot(AssetDatabase.LoadAssetAtPath<GameObject>(path).transform);
            }
            var first = Measure();
            var second = Measure();
            Assert.Greater(first.z, 0f, "Fox head should be at +Z after import");
            Assert.That(Vector3.Distance(first, second), Is.LessThan(1e-5f), $"double-flip? {first} vs {second}");
        }
    }
}
