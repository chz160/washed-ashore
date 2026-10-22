using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using WashedAshore.Character.Appearance;
using WashedAshore.Character.Editor;

namespace WashedAshore.Tests.EditMode.Character
{
    /// <summary>Spec H2: 2+ men's and 2+ women's outfits assemble from the separate meshes with 0 errors; a runtime slot swap leaves no T-pose or seam.</summary>
    public class CharacterAssemblyTests
    {
        const float SeamTolerance = 0.01f; // m

        static IEnumerable<string> AppearancePaths() =>
            CharacterAssetBuilder.Outfits.SelectMany(kv => kv.Value.Select(o => CharacterAssetBuilder.AppearancePath(kv.Key, o.folder)));

        static CharacterAppearance Load(string path) => AssetDatabase.LoadAssetAtPath<CharacterAppearance>(path);

        static AnimationClip Idle(CharacterAppearance a) =>
            AssetDatabase.LoadAssetAtPath<CharacterClipSet>(CharacterAssetBuilder.ClipSetPath(a.bodyType)).Get("Idle");

        static (float min, float max) WorldY(SkinnedMeshRenderer smr)
        {
            var mesh = new Mesh();
            smr.BakeMesh(mesh, false);
            var ys = mesh.vertices.Select(v => (smr.transform.position + smr.transform.rotation * v).y).ToList();
            Object.DestroyImmediate(mesh);
            return (ys.Min(), ys.Max());
        }

        static void AssertPosedAndBound(CharacterBody body)
        {
            foreach (CharacterSlot slot in System.Enum.GetValues(typeof(CharacterSlot)))
            {
                Assert.AreEqual(1, body.Renderers(slot).Count, $"{slot} renderers");
                foreach (var smr in body.Renderers(slot))
                {
                    Assert.IsNotNull(smr.sharedMesh, $"{slot} mesh");
                    Assert.IsNotNull(smr.rootBone, $"{slot} rootBone");
                    foreach (var b in smr.bones)
                    {
                        Assert.IsNotNull(b, $"{slot}: null bone");
                        Assert.IsTrue(b.IsChildOf(body.transform), $"{slot}: bone {b.name} is outside the assembled skeleton");
                    }
                }
            }
            // not a T-pose: the wrist hangs well below the shoulder
            Assert.Less(body.Bone("Wrist.L").position.y, body.Bone("Shoulder.L").position.y - 0.25f, "arms are up (T-pose)");
            Assert.Less(body.Bone("Wrist.R").position.y, body.Bone("Shoulder.R").position.y - 0.25f, "arms are up (T-pose)");
            // seams: neighbouring slots overlap in height at the joints
            var head = WorldY(body.Renderers(CharacterSlot.Head)[0]);
            var torso = WorldY(body.Renderers(CharacterSlot.Body)[0]);
            var legs = WorldY(body.Renderers(CharacterSlot.Legs)[0]);
            var feet = WorldY(body.Renderers(CharacterSlot.Feet)[0]);
            Assert.LessOrEqual(head.min, torso.max + SeamTolerance, "gap between head and body");
            Assert.LessOrEqual(torso.min, legs.max + SeamTolerance, "gap between body and legs");
            Assert.LessOrEqual(legs.min, feet.max + SeamTolerance, "gap between legs and feet");
            Assert.Less(feet.min, 0.05f, "feet are not on the floor");
        }

        [TestCaseSource(nameof(AppearancePaths))]
        public void OutfitAssemblesWithNoErrors(string path)
        {
            var appearance = Load(path);
            Assert.IsNotNull(appearance, path);
            var body = CharacterAssembler.Assemble(appearance);
            try
            {
                Assert.IsNotNull(body);
                Idle(appearance).SampleAnimation(body.Animator.gameObject, 0f);
                AssertPosedAndBound(body);
            }
            finally { if (body) Object.DestroyImmediate(body.gameObject); }
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void LegsSwapAtRuntimeKeepsThePoseAndTheSeams()
        {
            var worker = Load(CharacterAssetBuilder.AppearancePath(BodyType.Men, "Worker"));
            var farmer = Load(CharacterAssetBuilder.AppearancePath(BodyType.Men, "Farmer"));
            var body = CharacterAssembler.Assemble(worker);
            try
            {
                Idle(worker).SampleAnimation(body.Animator.gameObject, 0f);
                string before = body.Renderers(CharacterSlot.Legs)[0].sharedMesh.name;
                int changed = 0;
                body.SlotChanged += _ => changed++;
                Assert.IsTrue(body.SetSlot(CharacterSlot.Legs, farmer.legs));
                Assert.AreEqual(1, changed);
                Assert.AreNotEqual(before, body.Renderers(CharacterSlot.Legs)[0].sharedMesh.name, "legs mesh did not change");
                Idle(worker).SampleAnimation(body.Animator.gameObject, 0f);
                AssertPosedAndBound(body);
                Assert.AreEqual(4, body.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length, "old legs left behind");
            }
            finally { Object.DestroyImmediate(body.gameObject); }
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void MissingPartFailsWithAnErrorAndLeavesTheBodyIntact()
        {
            var body = CharacterAssembler.Assemble(Load(CharacterAssetBuilder.AppearancePath(BodyType.Men, "Worker")));
            try
            {
                LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("CharacterAssembler: Legs part is null"));
                Assert.IsFalse(body.SetSlot(CharacterSlot.Legs, null));
                Assert.AreEqual(1, body.Renderers(CharacterSlot.Legs).Count);
                Assert.IsNotNull(body.Renderers(CharacterSlot.Legs)[0].sharedMesh);
            }
            finally { Object.DestroyImmediate(body.gameObject); }
        }

        [Test]
        public void SwapsAndTintsNeverWriteIntoTheSharedAsset()
        {
            var farmer = Load(CharacterAssetBuilder.AppearancePath(BodyType.Men, "Farmer"));
            var worker = Load(CharacterAssetBuilder.AppearancePath(BodyType.Men, "Worker"));
            var legsBefore = farmer.legs;
            var body = CharacterAssembler.Assemble(farmer);
            try
            {
                Assert.IsTrue(body.SetSlot(CharacterSlot.Legs, worker.legs));
                body.SetTint(CharacterSlot.Body, "Skin", Color.red);
                Assert.AreSame(worker.legs, body.Appearance.legs, "the body's own copy takes the swap");
            }
            finally { Object.DestroyImmediate(body.gameObject); }
            Assert.AreSame(legsBefore, farmer.legs, "asset legs changed");
            Assert.AreEqual(0, farmer.tints.Count, "asset tints changed");
        }

        [Test]
        public void PartFromTheOtherBodyTypeIsRefusedWithAClearError()
        {
            var men = Load(CharacterAssetBuilder.AppearancePath(BodyType.Men, "Worker"));
            var women = Load(CharacterAssetBuilder.AppearancePath(BodyType.Women, "Worker"));
            var body = CharacterAssembler.Assemble(men);
            try
            {
                var before = body.Renderers(CharacterSlot.Head)[0];
                LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("belongs to another body type"));
                Assert.IsFalse(body.SetSlot(CharacterSlot.Head, women.head));
                Assert.AreSame(before, body.Renderers(CharacterSlot.Head)[0], "old head was replaced");
                Assert.AreSame(men.head, body.Appearance.head);
            }
            finally { Object.DestroyImmediate(body.gameObject); }
        }

        [TestCase(BodyType.Men, "Farmer")]
        [TestCase(BodyType.Women, "Worker")]
        public void PosedMeshStaysInsideTheRendererBoundsForEveryStanceClip(BodyType type, string outfit)
        {
            var appearance = Load(CharacterAssetBuilder.AppearancePath(type, outfit));
            Assert.IsTrue(appearance.hasPoseBounds, "no pose bounds; run the builder");
            var set = AssetDatabase.LoadAssetAtPath<CharacterClipSet>(CharacterAssetBuilder.ClipSetPath(type));
            var body = CharacterAssembler.Assemble(appearance);
            try
            {
                // every stance clip plus the full-swing Jog and Sprint clips the shadow plays (a wider arm swing must not push a hand out of the bounds)
                foreach (var name in CharacterClipSet.Names.Required.Concat(CharacterAnimatorBuilder.ShadowSwapped.Select(n => n + CharacterClipSet.Names.FullSuffix)))
                {
                    var clip = set.Get(name);
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
                                    Assert.IsTrue(bounds.Contains(smr.transform.position + smr.transform.rotation * v), $"{type}/{name} t{s} {slot}: vertex outside the renderer bounds");
                                Object.DestroyImmediate(mesh);
                            }
                    }
                }
            }
            finally { Object.DestroyImmediate(body.gameObject); }
        }

        [Test]
        public void ViewCopyFollowsTheShadowPoseHasNoHeadAnimatorOrColliderAndRebuildsOnSwap()
        {
            var farmer = Load(CharacterAssetBuilder.AppearancePath(BodyType.Men, "Farmer"));
            var worker = Load(CharacterAssetBuilder.AppearancePath(BodyType.Men, "Worker"));
            var set = AssetDatabase.LoadAssetAtPath<CharacterClipSet>(CharacterAssetBuilder.ClipSetPath(BodyType.Men));
            var shadow = CharacterAssembler.Assemble(farmer);
            var copy = CharacterAssembler.AssembleViewCopy(shadow);
            try
            {
                Assert.IsNotNull(copy);
                Assert.AreEqual(Vector3.zero, copy.transform.localPosition);
                Assert.AreEqual(0, copy.Body.Renderers(CharacterSlot.Head).Count, "view body has a head");
                foreach (var slot in new[] { CharacterSlot.Body, CharacterSlot.Legs, CharacterSlot.Feet })
                    foreach (var r in copy.Body.Renderers(slot))
                    {
                        Assert.AreEqual(UnityEngine.Rendering.ShadowCastingMode.Off, r.shadowCastingMode, slot.ToString());
                        Assert.IsTrue(r.receiveShadows, slot.ToString());
                    }
                Assert.IsNotNull(copy.ViewAnimator, "the view body has its own Animator");
                Assert.AreSame(farmer.controller, copy.ViewAnimator.runtimeAnimatorController, "the view Animator uses the base (capped) controller");
                Assert.AreSame(farmer.shadowController, shadow.Animator.runtimeAnimatorController, "the shadow uses the override (full swing)");
                Assert.AreNotSame(shadow.Animator.runtimeAnimatorController, copy.ViewAnimator.runtimeAnimatorController);
                Assert.AreEqual(AnimatorCullingMode.AlwaysAnimate, copy.ViewAnimator.cullingMode);
                Assert.AreEqual(0, copy.Resyncs);
                Assert.AreEqual(0, copy.GetComponentsInChildren<Collider>(true).Length, "view body has a Collider");
                Assert.AreEqual(0, shadow.GetComponentsInChildren<Collider>(true).Length, "shadow body has a Collider");
                foreach (var clipName in new[] { "Idle", "Run", "Sleep_Loop", "Sit_Loop" })
                {
                    set.Get(clipName).SampleAnimation(shadow.Animator.gameObject, set.Get(clipName).length * 0.37f);
                    copy.Apply();
                    foreach (var bone in new[] { "Hips", "Head", "Foot.L", "Foot.R", "UpperLeg.L", "Chest" })
                        Assert.Less(Vector3.Distance(shadow.Bone(bone).position, copy.Body.Bone(bone).position), 1e-4f, $"{clipName}/{bone}");
                }
                string before = copy.Body.Renderers(CharacterSlot.Legs)[0].sharedMesh.name;
                Assert.IsTrue(shadow.SetSlot(CharacterSlot.Legs, worker.legs));
                Assert.AreNotEqual(before, copy.Body.Renderers(CharacterSlot.Legs)[0].sharedMesh.name, "view legs were not rebuilt");
                Assert.AreEqual(3, copy.Body.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length);
                Assert.IsTrue(shadow.SetSlot(CharacterSlot.Head, worker.head));
                Assert.AreEqual(0, copy.Body.Renderers(CharacterSlot.Head).Count, "a head swap must not give the view body a head");
            }
            finally
            {
                if (copy) Object.DestroyImmediate(copy.gameObject);
                Object.DestroyImmediate(shadow.gameObject);
            }
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void ViewCopyCarriesTheShadowTintsBeforeAndAfterCreation()
        {
            var farmer = Load(CharacterAssetBuilder.AppearancePath(BodyType.Men, "Farmer"));
            var shadow = CharacterAssembler.Assemble(farmer);
            shadow.SetTint(CharacterSlot.Body, "Skin", Color.red);
            var view = CharacterAssembler.AssembleViewCopy(shadow);
            try
            {
                Color Skin(CharacterBody body, CharacterSlot slot)
                {
                    var smr = body.Renderers(slot)[0];
                    int i = System.Array.FindIndex(smr.sharedMaterials, m => m && m.name == "Skin");
                    Assert.GreaterOrEqual(i, 0, $"no Skin material on {slot}");
                    var block = new MaterialPropertyBlock();
                    smr.GetPropertyBlock(block, i);
                    return block.GetColor("_BaseColor");
                }
                Assert.AreEqual(Color.red, Skin(view.Body, CharacterSlot.Body), "tint made before the view copy existed");
                shadow.SetTint(CharacterSlot.Body, "Skin", Color.blue);
                Assert.AreEqual(Color.blue, Skin(shadow, CharacterSlot.Body));
                Assert.AreEqual(Color.blue, Skin(view.Body, CharacterSlot.Body), "tint made after the view copy existed");
            }
            finally
            {
                Object.DestroyImmediate(view.gameObject);
                Object.DestroyImmediate(shadow.gameObject);
            }
        }

        /// <summary>Boundary continuity (td): where an enter clip hands over to the hold, and where the hold hands over to the exit, at every range end,
        /// hips and head must not move more than 0.02 m.</summary>
        [TestCase(BodyType.Men, "Farmer")]
        [TestCase(BodyType.Women, "Worker")]
        public void EnterHoldExitBoundariesAtEveryRangeEndDoNotJump(BodyType type, string outfit)
        {
            var appearance = Load(CharacterAssetBuilder.AppearancePath(type, outfit));
            var set = AssetDatabase.LoadAssetAtPath<CharacterClipSet>(CharacterAssetBuilder.ClipSetPath(type));
            var body = CharacterAssembler.Assemble(appearance);
            try
            {
                Vector3[] Pose(AnimationClip clip, float t)
                {
                    clip.SampleAnimation(body.Animator.gameObject, t);
                    return new[] { body.Bone("Hips").position, body.Bone("Head").position };
                }
                void Check(string from, float fromFraction, string to, float toFraction, string label)
                {
                    var a = set.Get(from);
                    var b = set.Get(to);
                    Assert.IsNotNull(a, from);
                    Assert.IsNotNull(b, to);
                    var pa = Pose(a, a.length * fromFraction);
                    var pb = Pose(b, b.length * toFraction);
                    for (int i = 0; i < 2; i++)
                        Assert.LessOrEqual(Vector3.Distance(pa[i], pb[i]), 0.02f, $"{type} {label}: {from} -> {to} {(i == 0 ? "hips" : "head")} jumps by {Vector3.Distance(pa[i], pb[i]):F4} m");
                }
                var chains = new List<(string enter, string hold, string exit)>
                {
                    ("Squat_Enter", "Squat_Hold", "Squat_Exit"), ("Sit_Enter", "Sit_Loop", "Sit_Exit"), ("Sleep_Down", "Sleep_Loop", "Sleep_Wake"),
                    ("Squat_Enter_Low", "Squat_Hold_Low", "Squat_Exit_Low"), ("Squat_Enter_High", "Squat_Hold_High", "Squat_Exit_High"),
                    ("Sit_Enter_Low", "Sit_Loop_Low", "Sit_Exit_Low"), ("Sit_Enter_High", "Sit_Loop_High", "Sit_Exit_High"),
                    ("Sleep_Down_High", "Sleep_Loop_High", "Sleep_Wake_High"),
                };
                foreach (var (enter, hold, exit) in chains)
                {
                    Check(enter, 1f, hold, 0f, "transition to hold");
                    foreach (var phase in new[] { 0f, 0.25f, 0.5f, 0.75f })
                        Check(hold, phase, exit, 0f, $"hold (phase {phase}) to exit");
                }
            }
            finally { Object.DestroyImmediate(body.gameObject); }
        }

        static readonly string[] HandPoints = { "Wrist", "Index4", "Middle4", "Pinky4", "Thumb3" };

        [Test]
        public void CappedClipsOnTheViewBodyKeepEveryHandOutOfTheFrustum()
        {
            var farmer = Load(CharacterAssetBuilder.AppearancePath(BodyType.Men, "Farmer"));
            var set = AssetDatabase.LoadAssetAtPath<CharacterClipSet>(CharacterAssetBuilder.ClipSetPath(BodyType.Men));
            var shadow = CharacterAssembler.Assemble(farmer);
            var view = CharacterAssembler.AssembleViewCopy(shadow);
            try
            {
                Idle(farmer).SampleAnimation(shadow.Animator.gameObject, 0f);
                view.Apply();
                var local = EyeLocal("Farmer_Head.fbx");
                var camera = shadow.Bone("Head").TransformPoint(new Vector3(local[0], local[1], local[2]) / 100f); // the camera never follows the body
                float tv = Mathf.Tan(35f * Mathf.Deg2Rad), th = tv * 16f / 9f;
                foreach (var family in new[] { "Jog", "Sprint" })
                    foreach (var dir in new[] { "", "Back", "Left", "Right" })
                    {
                        var clip = set.Get(family + dir); // the capped clip: what the view Animator plays
                        for (int phase = 0; phase < 16; phase++)
                        {
                            clip.SampleAnimation(view.ViewAnimator.gameObject, clip.length * phase / 16f);
                            for (int pitch = -15; pitch <= 30; pitch += 5)
                            {
                                var fwd = Quaternion.Euler(pitch, 0f, 0f) * Vector3.forward;
                                var upv = Quaternion.Euler(pitch, 0f, 0f) * Vector3.up;
                                foreach (var side in new[] { "L", "R" })
                                    foreach (var point in HandPoints)
                                    {
                                        var bone = view.Body.Bone(point + "." + side);
                                        if (bone == null) continue;
                                        var d = bone.position - camera;
                                        float z = Vector3.Dot(fwd, d);
                                        bool inView = z > 0.1f && Mathf.Abs(Vector3.Dot(upv, d)) / z <= tv && Mathf.Abs(Vector3.Dot(Vector3.right, d)) / z <= th;
                                        Assert.IsFalse(inView, $"{family}{dir} phase {phase} pitch {pitch}: {point}.{side} is inside the frame");
                                    }
                            }
                        }
                    }
            }
            finally
            {
                Object.DestroyImmediate(view.gameObject);
                Object.DestroyImmediate(shadow.gameObject);
            }
        }

        static void Drive(Animator animator, float speedX, float speedZ, float dt)
        {
            animator.SetFloat("MoveX", speedX);
            animator.SetFloat("MoveZ", speedZ);
            animator.SetFloat("Speed", Mathf.Sqrt(speedX * speedX + speedZ * speedZ));
            animator.SetFloat("LocoRate", 1.1f);
            animator.SetBool("Grounded", true);
            animator.Update(dt);
        }

        [Test]
        public void TwoAnimatorsShareWeightsAndLegsButOnlyTheViewArmsAreCapped()
        {
            var farmer = Load(CharacterAssetBuilder.AppearancePath(BodyType.Men, "Farmer"));
            var shadow = CharacterAssembler.Assemble(farmer);
            var view = CharacterAssembler.AssembleViewCopy(shadow);
            try
            {
                // the same parameters to both Animators, as the driver does
                for (int i = 0; i < 20; i++)
                {
                    Drive(shadow.Animator, 0f, 8f, 0.05f);
                    Drive(view.ViewAnimator, 0f, 8f, 0.05f);
                }
                Assert.IsFalse(view.CheckSync(), "identical drivers must stay in sync");
                Assert.AreEqual(0, view.Resyncs);
                view.Apply();
                foreach (var bone in new[] { "Hips", "Head", "Chest", "Foot.L", "Foot.R", "UpperLeg.L", "LowerLeg.R", "Shoulder.L" })
                    Assert.Less(Vector3.Distance(shadow.Bone(bone).position, view.Body.Bone(bone).position), 1e-4f, bone);
                float armGap = 0f;
                foreach (var bone in new[] { "Wrist.L", "Wrist.R", "LowerArm.L", "LowerArm.R" })
                    armGap = Mathf.Max(armGap, Vector3.Distance(shadow.Bone(bone).position, view.Body.Bone(bone).position));
                Assert.Greater(armGap, 0.02f, "the capped view arms must differ from the shadow's full swing");
            }
            finally
            {
                Object.DestroyImmediate(view.gameObject);
                Object.DestroyImmediate(shadow.gameObject);
            }
        }

        [Test]
        public void SyncGuardResyncsADriftedViewAnimatorAndCountsIt()
        {
            var farmer = Load(CharacterAssetBuilder.AppearancePath(BodyType.Men, "Farmer"));
            var shadow = CharacterAssembler.Assemble(farmer);
            var view = CharacterAssembler.AssembleViewCopy(shadow);
            try
            {
                for (int i = 0; i < 10; i++)
                {
                    Drive(shadow.Animator, 0f, 5f, 0.05f);
                    Drive(view.ViewAnimator, 0f, 5f, 0.05f);
                }
                view.CheckSync();
                Assert.AreEqual(0, view.Resyncs, "the first alignment is not a fault");
                view.ViewAnimator.Update(0.21f); // the view Animator drifts ahead of the shadow
                Assert.IsTrue(view.CheckSync(), "a drift above the tolerance must resync");
                Assert.AreEqual(1, view.Resyncs);
                var a = shadow.Animator.GetCurrentAnimatorStateInfo(0);
                var v = view.ViewAnimator.GetCurrentAnimatorStateInfo(0);
                Assert.AreEqual(a.shortNameHash, v.shortNameHash);
                float gap = Mathf.Abs((a.normalizedTime % 1f) - (v.normalizedTime % 1f));
                Assert.Less(Mathf.Min(gap, 1f - gap), CharacterViewCopy.SyncTolerance);
                Assert.IsFalse(view.CheckSync());
                Assert.AreEqual(1, view.Resyncs);
            }
            finally
            {
                Object.DestroyImmediate(view.gameObject);
                Object.DestroyImmediate(shadow.gameObject);
            }
        }

        static float[] EyeLocal(string head)
        {
            var text = System.IO.File.ReadAllText("_bmad-output/poc/character-clips/eye-point-Men.json");
            int at = text.IndexOf("\"" + head + "\"", System.StringComparison.Ordinal);
            var m = System.Text.RegularExpressions.Regex.Match(text.Substring(at), "\"eye_in_head_bone_local\": \\[\\s*([-0-9.eE]+),\\s*([-0-9.eE]+),\\s*([-0-9.eE]+)");
            return new[] { float.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture), float.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture), float.Parse(m.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture) };
        }

        [Test]
        public void MenSkeletonIsScaledUniformlyWomenAreNot()
        {
            var men = AssetDatabase.LoadAssetAtPath<GameObject>(CharacterAssetBuilder.SkeletonPath(BodyType.Men)).transform.localScale;
            var women = AssetDatabase.LoadAssetAtPath<GameObject>(CharacterAssetBuilder.SkeletonPath(BodyType.Women)).transform.localScale;
            Assert.AreEqual(men.x, men.y, 1e-6f);
            Assert.AreEqual(men.x, men.z, 1e-6f);
            Assert.AreEqual(CharacterAssetBuilder.SkeletonScaleOf(BodyType.Men), men.x, 1e-5f);
            Assert.That(men.x, Is.InRange(1.0f, 1.06f), "td: above 1.06 goes to the director");
            Assert.AreEqual(Vector3.one, women);
            Assert.AreEqual(men.x, Load(CharacterAssetBuilder.AppearancePath(BodyType.Men, "Farmer")).skeletonScale, 1e-5f);
        }

        [Test]
        public void FarmerStandEyeIsOneSeventyAboveTheFloorOnTheScaledBody()
        {
            var farmer = Load(CharacterAssetBuilder.AppearancePath(BodyType.Men, "Farmer"));
            var shadow = CharacterAssembler.Assemble(farmer);
            var view = CharacterAssembler.AssembleViewCopy(shadow);
            try
            {
                Idle(farmer).SampleAnimation(shadow.Animator.gameObject, 0f);
                view.Apply();
                Assert.AreEqual(shadow.SkeletonScale, view.Body.SkeletonScale, 1e-6f, "both bodies carry the scale");
                var local = EyeLocal("Farmer_Head.fbx");
                foreach (var body in new[] { shadow, view.Body })
                {
                    var head = body.Bone("Head");
                    var eye = head.TransformPoint(new Vector3(local[0], local[1], local[2]) / 100f);
                    Assert.AreEqual(1.70f, eye.y, 0.012f, $"stand eye above the floor (feet at 0): {eye.y:F4}");
                }
            }
            finally
            {
                Object.DestroyImmediate(view.gameObject);
                Object.DestroyImmediate(shadow.gameObject);
            }
        }

        [Test]
        public void SkeletonAnimatorAlwaysAnimates()
        {
            foreach (BodyType type in new[] { BodyType.Men, BodyType.Women })
            {
                var skeleton = AssetDatabase.LoadAssetAtPath<GameObject>(CharacterAssetBuilder.SkeletonPath(type));
                Assert.AreEqual(AnimatorCullingMode.AlwaysAnimate, skeleton.GetComponentInChildren<Animator>(true).cullingMode, type.ToString());
            }
        }

        [Test]
        public void TintsApplyPerMaterialWithoutChangingSharedMaterials()
        {
            var a = Load(CharacterAssetBuilder.AppearancePath(BodyType.Men, "Worker"));
            var body = CharacterAssembler.Assemble(a);
            try
            {
                var smr = body.Renderers(CharacterSlot.Body)[0];
                var mats = smr.sharedMaterials;
                int skin = System.Array.FindIndex(mats, m => m && m.name == "Skin");
                Assert.GreaterOrEqual(skin, 0, "Skin material not found: " + string.Join(", ", mats.Select(m => m ? m.name : "null")));
                var shared = mats[skin].GetColor("_BaseColor");
                body.SetTint(CharacterSlot.Body, "Skin", Color.red);
                var block = new MaterialPropertyBlock();
                smr.GetPropertyBlock(block, skin);
                Assert.AreEqual(Color.red, block.GetColor("_BaseColor"));
                Assert.AreEqual(shared, smr.sharedMaterials[skin].GetColor("_BaseColor"), "shared material was modified");
            }
            finally { Object.DestroyImmediate(body.gameObject); }
        }
    }
}
