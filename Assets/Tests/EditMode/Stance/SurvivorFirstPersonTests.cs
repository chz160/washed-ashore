using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using WashedAshore.Character.Appearance;
using WashedAshore.Character.Editor;
using WashedAshore.Character.Stance;

namespace WashedAshore.Tests.EditMode.Stance
{
    /// <summary>Row H1 (qa-delta-p2): the Survivor's head is found by name, hidden from the camera and kept as a shadow caster; the body and the rest are what the camera sees.</summary>
    public class SurvivorFirstPersonTests
    {
        [Test]
        public void OnlyTheHeadRendererIsAHeadAndTheCameraSeesTheViewBodyWithoutIt()
        {
            var a = AssetDatabase.LoadAssetAtPath<CharacterAppearance>(CharacterAssetBuilder.SurvivorAppearancePath);
            Assert.IsNotNull(a, "Men_Survivor.asset is missing");
            var shadow = CharacterAssembler.Assemble(a);
            var view = CharacterAssembler.AssembleViewCopy(shadow);
            var host = new GameObject("FpbHost");
            var fpb = host.AddComponent<FirstPersonBody>();
            try
            {
                fpb.Bind(null, shadow.transform, view.Body.transform);
                Assert.AreEqual(1, fpb.HeadRenderers.Count, "exactly the head renderer is a head");
                Assert.AreSame(shadow.Renderers(CharacterSlot.Head)[0], fpb.HeadRenderers[0]);
                Assert.IsFalse(fpb.ViewRenderers.Contains(fpb.HeadRenderers[0]), "the head is not drawn to the camera");
                Assert.AreEqual(UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly, fpb.HeadRenderers[0].shadowCastingMode, "the head still casts the shadow");
                Assert.AreEqual(view.Body.Renderers(CharacterSlot.Body).Count, fpb.ViewRenderers.Count(r => r is SkinnedMeshRenderer), "the camera sees the view body's renderers");
                Assert.AreEqual(1, shadow.Renderers(CharacterSlot.Body).Count, "one body renderer, so the loop below cannot pass on an empty list");
                foreach (var r in shadow.Renderers(CharacterSlot.Body))
                    Assert.AreEqual(UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly, r.shadowCastingMode, "the shadow body is never drawn to the camera");
            }
            finally
            {
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(view.gameObject);
                Object.DestroyImmediate(shadow.gameObject);
            }
        }
    }
}
