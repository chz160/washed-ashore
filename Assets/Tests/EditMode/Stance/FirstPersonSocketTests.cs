using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using WashedAshore.Character.Appearance;
using WashedAshore.Character.Editor;
using WashedAshore.Character.Stance;

namespace WashedAshore.Tests.EditMode.Stance
{
    /// <summary>Row S4 (qa-delta-p2): an attached item is never a head by renderer NAME; the visibility flag decides what the camera sees.</summary>
    public class FirstPersonSocketTests
    {
        /// <summary>Any appearance in the project (the Survivor once it exists): these tests need a body with the standard skeleton, not a particular character.</summary>
        static CharacterAppearance Fixture()
        {
            // deterministic: sorted paths; the Survivor if it exists, else a Men_ appearance (never a 62-bone women's rig), else the first
            var paths = new System.Collections.Generic.List<string>();
            foreach (var guid in AssetDatabase.FindAssets("t:CharacterAppearance")) paths.Add(AssetDatabase.GUIDToAssetPath(guid));
            paths.Sort(System.StringComparer.Ordinal);
            string pick = paths.Find(p => p.Contains("Survivor")) ?? paths.Find(p => System.IO.Path.GetFileName(p).StartsWith("Men_")) ?? paths[0];
            return AssetDatabase.LoadAssetAtPath<CharacterAppearance>(pick);
        }

        [Test]
        public void AnItemNamedHeadlampOnTheHipsIsNotInTheHeadListAndTheRealHeadStillIs()
        {
            var farmer = Fixture();
            var shadow = CharacterAssembler.Assemble(farmer);
            var view = CharacterAssembler.AssembleViewCopy(shadow);
            var host = new GameObject("FpbHost");
            var fpb = host.AddComponent<FirstPersonBody>();
            var equipment = host.AddComponent<CharacterEquipment>();
            var prefab = GameObject.CreatePrimitive(PrimitiveType.Cube);
            prefab.name = "Headlamp";
            try
            {
                fpb.Bind(null, shadow.transform, view.Body.transform);
                int headBefore = fpb.HeadRenderers.Count;
                Assert.Greater(headBefore, 0, "the farmer's head is found by name");
                equipment.Changed += fpb.Rescan;
                equipment.Bind(shadow, view);
                var h = equipment.Attach(CharacterSocketId.Hips, prefab, SocketVisibility.FirstPersonVisible);
                Assert.IsNotNull(h);
                Assert.AreEqual(headBefore, fpb.HeadRenderers.Count, "Headlamp was taken for a head");
                var shadowItem = equipment.Occupant(CharacterSocketId.Hips).GetComponent<Renderer>();
                var viewItem = equipment.ViewOccupant(CharacterSocketId.Hips).GetComponent<Renderer>();
                Assert.IsFalse(fpb.HeadRenderers.Contains(shadowItem));
                Assert.IsTrue(fpb.ViewRenderers.Contains(viewItem), "the visible copy is a first-person renderer");
                Assert.AreEqual(UnityEngine.Rendering.ShadowCastingMode.Off, viewItem.shadowCastingMode, "view copy casts a shadow");
                Assert.AreEqual(UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly, shadowItem.shadowCastingMode, "shadow copy is drawn to the camera");
                Assert.IsTrue(viewItem.enabled, "the visible copy is not drawn");
            }
            finally
            {
                Object.DestroyImmediate(prefab);
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(view.gameObject);
                Object.DestroyImmediate(shadow.gameObject);
            }
        }

        [Test]
        public void AShadowOnlyItemIsNotAFirstPersonRenderer()
        {
            var farmer = Fixture();
            var shadow = CharacterAssembler.Assemble(farmer);
            var view = CharacterAssembler.AssembleViewCopy(shadow);
            var host = new GameObject("FpbHost");
            var fpb = host.AddComponent<FirstPersonBody>();
            var equipment = host.AddComponent<CharacterEquipment>();
            var prefab = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                fpb.Bind(null, shadow.transform, view.Body.transform);
                equipment.Changed += fpb.Rescan;
                equipment.Bind(shadow, view);
                int viewBefore = fpb.ViewRenderers.Count;
                equipment.Attach(CharacterSocketId.Back, prefab, SocketVisibility.ShadowOnly);
                Assert.AreEqual(viewBefore, fpb.ViewRenderers.Count, "a shadow-only item reached the first-person list");
                var r = equipment.Occupant(CharacterSocketId.Back).GetComponent<Renderer>();
                Assert.AreEqual(UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly, r.shadowCastingMode);
                Assert.IsFalse(fpb.ViewRenderers.Contains(r));
            }
            finally
            {
                Object.DestroyImmediate(prefab);
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(view.gameObject);
                Object.DestroyImmediate(shadow.gameObject);
            }
        }

        [Test]
        public void WithNoViewBodyAHeadSocketItemIsShadowOnlyAndNotAFirstPersonRenderer()
        {
            var farmer = Fixture();
            var shadow = CharacterAssembler.Assemble(farmer);
            var host = new GameObject("FpbHost");
            var fpb = host.AddComponent<FirstPersonBody>();
            var equipment = host.AddComponent<CharacterEquipment>();
            var prefab = GameObject.CreatePrimitive(PrimitiveType.Cube);
            prefab.name = "Hat";
            try
            {
                fpb.Bind(null, shadow.transform, null);   // no view body: the shadow body is what the camera sees
                equipment.Changed += fpb.Rescan;
                equipment.Bind(shadow, null);
                Assert.IsNotNull(equipment.Attach(CharacterSocketId.Head, prefab, SocketVisibility.ShadowOnly));
                var r = equipment.Occupant(CharacterSocketId.Head).GetComponent<Renderer>();
                Assert.IsFalse(fpb.ViewRenderers.Contains(r), "a head-socket hat reached the first-person list");
                Assert.AreEqual(UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly, r.shadowCastingMode, "the hat is drawn at the camera");
                fpb.SetHeadVisible(true);
                Assert.AreEqual(UnityEngine.Rendering.ShadowCastingMode.On, r.shadowCastingMode, "debug view draws the hat");
            }
            finally
            {
                Object.DestroyImmediate(prefab);
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(shadow.gameObject);
            }
        }
    }
}
