using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using WashedAshore.Character.Appearance;
using WashedAshore.Character.Editor;
using WashedAshore.Wildlife.Editor;

namespace WashedAshore.Tests.EditMode.Character
{
    /// <summary>Spec H1 and H3: clean scripted import, Generic avatars, facing +Z, every stance clip present and resolvable.</summary>
    public class CharacterImportTests
    {
        static readonly BodyType[] Types = { BodyType.Men, BodyType.Women };

        static IEnumerable<string> AllModelPaths()
        {
            foreach (var t in Types)
            {
                yield return CharacterAssetBuilder.AnimationsPath(t);
                yield return CharacterAssetBuilder.AuthoredPath(t);
                foreach (var (folder, prefix) in CharacterAssetBuilder.Outfits[t])
                    foreach (CharacterSlot s in System.Enum.GetValues(typeof(CharacterSlot)))
                        yield return CharacterAssetBuilder.PartPath(t, folder, prefix, s);
            }
        }

        static IEnumerable<string> PartPaths() => AllModelPaths().Where(p => !CharacterImportSettings.CarriesClips(p));

        static Transform Find(Transform root, string name) =>
            root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name);

        [TestCaseSource(nameof(Types))]
        public void LicenseIsCopiedIn(BodyType type)
        {
            var path = $"{CharacterImportSettings.PackRoot}/{type}/License.txt";
            Assert.IsTrue(File.Exists(path), path);
            StringAssert.Contains("CC0", File.ReadAllText(path));
        }

        [TestCaseSource(nameof(AllModelPaths))]
        public void ModelImportsAsGenericWithValidAvatar(string path)
        {
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            Assert.IsNotNull(importer, $"{path} is not imported");
            Assert.AreEqual(ModelImporterAnimationType.Generic, importer.animationType, path);
            var avatar = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Avatar>().FirstOrDefault();
            Assert.IsNotNull(avatar, $"{path}: no avatar");
            Assert.IsTrue(avatar.isValid, $"{path}: avatar invalid");
            Assert.IsFalse(avatar.isHuman, path);
        }

        [TestCaseSource(nameof(PartPaths))]
        public void PartHasRequiredBonesAndMatchesTheSkeleton(string path)
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var type = path.Contains("/Women/") ? BodyType.Women : BodyType.Men;
            var reference = AssetDatabase.LoadAssetAtPath<GameObject>(CharacterAssetBuilder.PartPath(type, "Worker", "Worker", CharacterSlot.Body));
            var names = new HashSet<string>(go.GetComponentsInChildren<Transform>(true).Select(t => t.name));
            foreach (var bone in CharacterAssetBuilder.RequiredBones) Assert.IsTrue(names.Contains(bone), $"{path}: missing bone {bone}");
            var skeletonNames = reference.GetComponentsInChildren<Transform>(true).Select(t => t.name).Where(n => !n.StartsWith("Worker_"));
            foreach (var n in skeletonNames)
                Assert.IsTrue(names.Contains(n), $"{path}: bone '{n}' of the {type} skeleton is missing");
            var smr = go.GetComponentInChildren<SkinnedMeshRenderer>(true);
            Assert.IsNotNull(smr, $"{path}: no SkinnedMeshRenderer");
            foreach (var b in smr.bones) Assert.IsNotNull(b, $"{path}: null bone in SkinnedMeshRenderer");
        }

        /// <summary>Every rigged character FBX must show the same facing decision (flip or keep), never ambiguous.</summary>
        [Test]
        public void FacingDecisionIsTheSameOnEveryRiggedFile()
        {
            var json = ModelFacingPostprocessor.FacingReport();
            var report = JsonUtility.FromJson<Wrapper>(json);
            var mine = report.models.Where(m => m.path.StartsWith(CharacterImportSettings.PackRoot + "/") || m.path.StartsWith(CharacterImportSettings.ClipFolder + "/")).ToList();
            Assert.IsNotEmpty(mine, "no character model in the facing report");
            foreach (var m in mine)
                Assert.That(m.decision, Is.EqualTo("Flip").Or.EqualTo("Keep"), $"{m.path}: {m.decision} {m.reason}");
            Assert.AreEqual(1, mine.Select(m => m.decision).Distinct().Count(),
                "decisions differ: " + string.Join(", ", mine.GroupBy(m => m.decision).Select(g => $"{g.Key} x{g.Count()}")));
        }

        [System.Serializable] class Wrapper { public ModelFacingPostprocessor.ReportEntry[] models; }

        /// <summary>The +Z facing test (the postprocessor skips nothing here, but the rule is checked on the result).</summary>
        [TestCase("Assets/Art/Character/Appearances/Men_Worker.asset")]
        [TestCase("Assets/Art/Character/Appearances/Women_Worker.asset")]
        public void AssembledBodyFacesPlusZ(string appearancePath)
        {
            var appearance = AssetDatabase.LoadAssetAtPath<CharacterAppearance>(appearancePath);
            var clips = AssetDatabase.LoadAssetAtPath<CharacterClipSet>(CharacterAssetBuilder.ClipSetPath(appearance.bodyType));
            var body = CharacterAssembler.Assemble(appearance);
            try
            {
                clips.Get("Idle").SampleAnimation(body.Animator.gameObject, 0f);
                var left = body.Bone("Foot.L").position;
                var right = body.Bone("Foot.R").position;
                Assert.Less(left.x, right.x, "character's left foot must be at -X when facing +Z");
                // toes ahead of the ankles: centroid of the lowest feet vertices minus the mean ankle position, along +Z
                var feet = body.Renderers(CharacterSlot.Feet)[0];
                var mesh = new Mesh();
                feet.BakeMesh(mesh, false); // vertices are in the mesh node's frame; the FBX node scale does not apply to skinned vertices
                var verts = mesh.vertices.Select(v => feet.transform.position + feet.transform.rotation * v).ToList();
                float ankleZ = (left.z + right.z) * 0.5f;
                float ahead = verts.Max(v => v.z) - ankleZ, behind = ankleZ - verts.Min(v => v.z);
                Object.DestroyImmediate(mesh);
                Assert.Greater(ahead, behind + 0.03f, $"toes must point to +Z (foot reaches {ahead:F3} ahead of the ankle, {behind:F3} behind)");
                Assert.Greater(Vector3.Dot(body.Bone("Head").position - body.Bone("Hips").position, Vector3.up), 0.5f);
            }
            finally { Object.DestroyImmediate(body.gameObject); }
        }

        [TestCaseSource(nameof(Types))]
        public void EveryStanceClipExistsAndKeysOnlyBones(BodyType type)
        {
            var set = AssetDatabase.LoadAssetAtPath<CharacterClipSet>(CharacterAssetBuilder.ClipSetPath(type));
            Assert.IsNotNull(set);
            var skeleton = AssetDatabase.LoadAssetAtPath<GameObject>(CharacterAssetBuilder.SkeletonPath(type));
            var armature = skeleton.GetComponentInChildren<Animator>(true).transform;
            var paths = new HashSet<string>(armature.GetComponentsInChildren<Transform>(true)
                .Select(t => AnimationUtility.CalculateTransformPath(t, armature)));
            foreach (var name in CharacterClipSet.Names.Required)
            {
                var clip = set.Get(name);
                Assert.IsNotNull(clip, $"{type}: clip {name} missing");
                Assert.Greater(clip.length, 0.1f, $"{type}/{name} length");
                Assert.AreEqual(CharacterImportSettings.LoopClips.Contains(name), clip.isLooping, $"{type}/{name} loop flag");
                foreach (var b in AnimationUtility.GetCurveBindings(clip).Where(b => b.type == typeof(Transform)))
                {
                    Assert.IsTrue(paths.Contains(b.path), $"{type}/{name}: curve path '{b.path}' not in the skeleton");
                    // curves on the armature node itself (path "") must equal its rest pose, or playing the clip would move the node
                    if (b.path != "") continue;
                    float v = AnimationUtility.GetEditorCurve(clip, b).Evaluate(0f);
                    if (b.propertyName.StartsWith("m_LocalScale")) Assert.AreEqual(armature.localScale.x, v, 0.01f, $"{type}/{name}: {b.propertyName}");
                    if (b.propertyName.StartsWith("m_LocalPosition")) Assert.AreEqual(0f, v, 0.001f, $"{type}/{name}: {b.propertyName}");
                }
            }
        }
    }
}
