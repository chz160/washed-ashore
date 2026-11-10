using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using WashedAshore.Character.Appearance;
using WashedAshore.Character.Editor;

namespace WashedAshore.Tests.EditMode.Character
{
    /// <summary>
    /// Pod 49 item 5, baton 3 (qa rows H1, B1, F1 in part, M27): the Survivor appearance assembles from Head + Body only on the shared skeleton, its numbers come from data of
    /// record, the view body has no head, and the posed mesh stays inside the renderer bounds for every stance clip.
    /// </summary>
    public class SurvivorAppearanceTests
    {
        [System.Serializable] class SpeedEntry { public string name; public float speed; }
        [System.Serializable] class SpeedFile { public float scale; public SpeedEntry[] clips; }

        static CharacterAppearance Survivor()
        {
            var a = AssetDatabase.LoadAssetAtPath<CharacterAppearance>(CharacterAssetBuilder.SurvivorAppearancePath);
            Assert.IsNotNull(a, "Men_Survivor.asset is missing (run CharacterAssetBuilder.BatchBuild)");
            return a;
        }

        static SpeedFile Speeds() => JsonUtility.FromJson<SpeedFile>(File.ReadAllText(Path.Combine(CharacterAssetBuilder.ClipDataDir, "speeds-Survivor.json")));

        [Test]
        public void AssemblesFromHeadAndBodyAloneWithNoErrorsOnTheSharedSkeleton()
        {
            var a = Survivor();
            var body = CharacterAssembler.Assemble(a);
            try
            {
                Assert.IsNotNull(body);
                Assert.AreEqual(1, body.Renderers(CharacterSlot.Head).Count);
                Assert.AreEqual(1, body.Renderers(CharacterSlot.Body).Count);
                Assert.AreEqual(0, body.Renderers(CharacterSlot.Legs).Count);
                Assert.AreEqual(0, body.Renderers(CharacterSlot.Feet).Count);
                var prefabNames = a.skeleton.GetComponentsInChildren<Transform>(true).Count(t => t != a.skeleton.transform);
                Assert.AreEqual(prefabNames, body.Bones.Count, "the bone map equals the skeleton prefab's (sockets are not bones)");
                Assert.AreEqual(5, body.GetComponentsInChildren<Transform>(true).Count(t => t.name.StartsWith("Socket_")));
                Assert.IsNotNull(body.GetComponent<KneeCorrectiveDriver>(), "the knee driver is added when the body carries the shapes");
                Assert.AreEqual(2, body.GetComponent<KneeCorrectiveDriver>().KneeCount);
            }
            finally { if (body) Object.DestroyImmediate(body.gameObject); }
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void TheViewBodyHasTheBodyOnlyNoHeadNoColliderAndItsOwnKneeDriver()
        {
            var shadow = CharacterAssembler.Assemble(Survivor());
            var view = CharacterAssembler.AssembleViewCopy(shadow);
            try
            {
                Assert.IsNotNull(view);
                Assert.AreEqual(0, view.Body.Renderers(CharacterSlot.Head).Count, "the view body has no head");
                Assert.AreEqual(1, view.Body.Renderers(CharacterSlot.Body).Count);
                Assert.AreEqual(0, view.GetComponentsInChildren<Collider>(true).Length);
                Assert.IsNotNull(view.Body.GetComponent<KneeCorrectiveDriver>(), "the view body drives its own shapes");
            }
            finally
            {
                if (view) Object.DestroyImmediate(view.gameObject);
                Object.DestroyImmediate(shadow.gameObject);
            }
        }

        [Test]
        public void TheNumbersAreDataOfRecordNotTypedConstants()
        {
            var a = Survivor();
            var file = Speeds();
            Assert.AreEqual(file.scale, a.skeletonScale, 1e-5f, "skeletonScale comes from speeds-Survivor.json");
            Assert.AreEqual(1.0156f, a.skeletonScale, 1e-4f, "td-49's ruled Survivor scale (Addendum 19): a regenerated speeds file cannot pass unseen");
            Assert.AreEqual(file.clips.First(c => c.name == "Walk").speed / file.scale, a.walkClipSpeed, 1e-4f);
            Assert.AreEqual(file.clips.First(c => c.name == "Run").speed / file.scale, a.runClipSpeed, 1e-4f);
            Assert.AreEqual(20f, a.kneeShapeStartDegrees);
            var mesh = a.body.GetComponentsInChildren<SkinnedMeshRenderer>(true)[0].sharedMesh;
            // a presence check: the shape is named Knee_{L|R}_90 and the target is parsed from that name; it proves the shapes exist on the imported mesh and agree, not that 90 degrees is right
            Assert.GreaterOrEqual(mesh.GetBlendShapeIndex(string.Format(KneeCorrectiveDriver.ShapeFormat + "", "L")), 0);
            Assert.AreEqual(float.Parse(string.Format(KneeCorrectiveDriver.ShapeFormat, "L").Split('_').Last(), System.Globalization.CultureInfo.InvariantCulture), a.kneeShapeTargetDegrees, 1e-4f,
                "the knee target is the flex in the shape name");
            CollectionAssert.AreEquivalent(new[] { CharacterSlot.Legs, CharacterSlot.Feet }, a.emptySlots);
            Assert.IsNull(a.legs);
            Assert.IsNull(a.feet);
            Assert.IsNotNull(a.controller);
            Assert.IsNotNull(a.shadowController);
            Assert.IsTrue(a.hasPoseBounds, "pose bounds measured by the builder on the Survivor's own clips");
            Assert.AreEqual(BodyType.Men, a.bodyType);
        }

        [Test]
        public void TheSpeedsFileValidationRefusesAMissingScaleOrWalkOrRunForTheSurvivor()
        {
            Assert.IsNull(CharacterAssetBuilder.ValidateSurvivorSpeeds(File.ReadAllText(Path.Combine(CharacterAssetBuilder.ClipDataDir, "speeds-Survivor.json"))), "the shipped file is valid");
            StringAssert.Contains("scale", CharacterAssetBuilder.ValidateSurvivorSpeeds("{\"scale\":0,\"clips\":[{\"name\":\"Walk\",\"speed\":1.2},{\"name\":\"Run\",\"speed\":2.6}]}"));
            StringAssert.Contains("scale", CharacterAssetBuilder.ValidateSurvivorSpeeds("{\"clips\":[{\"name\":\"Walk\",\"speed\":1.2},{\"name\":\"Run\",\"speed\":2.6}]}"), "a file with no scale key must be refused (the default is not 1)");
            StringAssert.Contains("Walk", CharacterAssetBuilder.ValidateSurvivorSpeeds("{\"scale\":1.0156,\"clips\":[{\"name\":\"Run\",\"speed\":2.6}]}"));
            StringAssert.Contains("Run", CharacterAssetBuilder.ValidateSurvivorSpeeds("{\"scale\":1.0156,\"clips\":[{\"name\":\"Walk\",\"speed\":1.2},{\"name\":\"Run\",\"speed\":0}]}"));
            Assert.IsNotNull(CharacterAssetBuilder.ValidateSurvivorSpeeds("{\"scale\":1.0156}"), "no clips list");
            Assert.IsNotNull(CharacterAssetBuilder.ValidateSurvivorSpeeds("not json"), "unreadable");
        }

        [Test]
        public void StandIdleIsInTheClipSetAndLoops()
        {
            var set = AssetDatabase.LoadAssetAtPath<CharacterClipSet>(CharacterAssetBuilder.ClipSetPath(BodyType.Men));
            var clip = set.Get(CharacterClipSet.Names.StandIdle);
            Assert.IsNotNull(clip, "Stand_Idle");
            Assert.IsTrue(clip.isLooping, "Stand_Idle loops");
            Assert.IsNotNull(set.Get(CharacterClipSet.Names.Idle), "the pack Idle stays imported");
        }

        [Test]
        public void ThePosedMeshStaysInsideTheRendererBoundsForEveryStanceClipAndTheFullSwingClips()
        {
            var a = Survivor();
            var set = AssetDatabase.LoadAssetAtPath<CharacterClipSet>(CharacterAssetBuilder.ClipSetPath(BodyType.Men));
            var body = CharacterAssembler.Assemble(a);
            try
            {
                var names = set.clips.Where(c => CharacterAssetBuilder.IsPoseBoundsClip(c.name)).Select(c => c.name).ToList();   // the builder's own list (td-49 C3)
                Assert.Greater(names.Count, 30, "the pose-bounds clip list");
                CollectionAssert.Contains(names, CharacterClipSet.Names.StandIdle);
                foreach (var name in names)
                {
                    var clip = set.Get(name);
                    Assert.IsNotNull(clip, name);
                    for (int s = 0; s < 5; s++)
                    {
                        clip.SampleAnimation(body.Animator.gameObject, clip.length * s / 4f);
                        foreach (CharacterSlot slot in System.Enum.GetValues(typeof(CharacterSlot)))
                            foreach (var smr in body.Renderers(slot))
                            {
                                var mesh = new Mesh();
                                smr.BakeMesh(mesh, false);
                                var bounds = smr.bounds;
                                bounds.Expand(0.02f);
                                foreach (var v in mesh.vertices)
                                    Assert.IsTrue(bounds.Contains(smr.transform.position + smr.transform.rotation * v), $"{name} t{s} {slot}: vertex outside the renderer bounds");
                                Object.DestroyImmediate(mesh);
                            }
                    }
                }
            }
            finally { Object.DestroyImmediate(body.gameObject); }
        }
    }
}
