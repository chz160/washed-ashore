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
    /// Pod 49 (td-49 adr/49-phase2-reskin-and-sockets parts A6 and B): sockets are not bones, items attach to both bodies by flag,
    /// an explicitly empty slot is allowed, and the existing appearances are unchanged. Rows S1-S3, S5-S9, S12, B1a, R1 of qa-delta-p2.
    /// </summary>
    public class CharacterSocketTests
    {
        const int PlayerLikeLayer = 7;

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

        sealed class Rig : System.IDisposable
        {
            public CharacterBody Shadow;
            public CharacterViewCopy View;
            public CharacterEquipment Equipment;
            readonly GameObject host;

            public Rig(CharacterAppearance appearance = null)
            {
                Shadow = CharacterAssembler.Assemble(appearance != null ? appearance : Fixture());
                SetLayer(Shadow.transform, PlayerLikeLayer);
                View = CharacterAssembler.AssembleViewCopy(Shadow);
                SetLayer(View.transform, PlayerLikeLayer);
                host = new GameObject("EquipmentHost");
                Equipment = host.AddComponent<CharacterEquipment>();
                Equipment.Bind(Shadow, View);
            }

            public void Dispose()
            {
                if (View) Object.DestroyImmediate(View.gameObject);
                if (Shadow) Object.DestroyImmediate(Shadow.gameObject);
                if (host) Object.DestroyImmediate(host);
            }
        }

        /// <summary>
        /// The bone map's size before any socket exists: every Transform of the skeleton prefab except its root. For the men this is 80
        /// (the 79 bones under CharacterArmature plus the CharacterArmature node itself; measured 2026-11-03: the map held 80).
        /// </summary>
        static int PrefabBoneMapSize(CharacterAppearance a) => a.skeleton.GetComponentsInChildren<Transform>(true).Length - 1;

        static void SetLayer(Transform t, int layer)
        {
            t.gameObject.layer = layer;
            foreach (Transform c in t) SetLayer(c, layer);
        }

        /// <summary>A test item: a cube with a collider and a rigidbody, named so it carries the head token when asked.</summary>
        static GameObject Item(string name)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.AddComponent<Rigidbody>();
            return go;
        }

        static int RendererCount(Component root) => root.GetComponentsInChildren<Renderer>(true).Length;

        // ---- S1, S5, B1a: sockets exist on both bodies, are not bones ----

        [Test]
        public void EverySocketExistsOnBothBodiesUnderItsDefaultBoneAndTheBoneCountIsUnchanged()
        {
            using (var rig = new Rig())
            {
                foreach (var body in new[] { rig.Shadow, rig.View.Body })
                {
                    Assert.AreEqual(PrefabBoneMapSize(Fixture()), body.Bones.Count, "bone map grew");
                    Assert.IsNotNull(body.Sockets, "no sockets on " + body.name);
                    foreach (CharacterSocketId id in System.Enum.GetValues(typeof(CharacterSocketId)))
                    {
                        var s = body.Sockets.Get(id);
                        Assert.IsNotNull(s, id + " on " + body.name);
                        Assert.AreEqual("Socket_" + id, s.name);
                        Assert.AreEqual(CharacterSocketSet.Default(id).parentBone, s.parent.name, id + " parent");
                        Assert.IsFalse(body.Bones.ContainsKey(s.name), "a socket is in the bone map");
                    }
                }
            }
        }

        [Test]
        public void ARightHandSocketIsOnTheRightWristAndTheLeftOnTheLeft()
        {
            using (var rig = new Rig())
            {
                foreach (var body in new[] { rig.Shadow, rig.View.Body })
                {
                    Assert.AreSame(body.Bone("Wrist.R"), body.Sockets.Get(CharacterSocketId.HandR).parent);
                    Assert.AreSame(body.Bone("Wrist.L"), body.Sockets.Get(CharacterSocketId.HandL).parent);
                }
            }
        }

        [Test]
        public void SocketPosesMatchBetweenShadowAndViewInBodyRootSpaceForNonArmSockets()
        {
            using (var rig = new Rig())
            {
                rig.View.Apply();
                foreach (var id in new[] { CharacterSocketId.Back, CharacterSocketId.Head, CharacterSocketId.Hips })
                {
                    var a = rig.Shadow.transform.InverseTransformPoint(rig.Shadow.Sockets.Get(id).position);
                    var b = rig.View.Body.transform.InverseTransformPoint(rig.View.Body.Sockets.Get(id).position);
                    Assert.Less((a - b).magnitude, 0.001f, id + " differs between the bodies");
                }
            }
        }

        [Test]
        public void ASocketSetWithAMissingParentBoneFailsAssemblyWithAnError()
        {
            var set = ScriptableObject.CreateInstance<CharacterSocketSet>();
            set.entries.Add(new CharacterSocketSet.Entry { id = CharacterSocketId.Back, parentBone = "NoSuchBone" });
            var appearance = Object.Instantiate(Fixture());
            appearance.socketSet = set;
            try
            {
                LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("NoSuchBone"));
                Assert.IsNull(CharacterAssembler.Assemble(appearance), "assembled with a bad socket parent");
            }
            finally { Object.DestroyImmediate(appearance); Object.DestroyImmediate(set); }
        }

        // ---- S2, S3, S12: visibility flag ----

        [Test]
        public void ShadowOnlyItemHasNoViewCopyAndFirstPersonVisibleHasExactlyOne()
        {
            using (var rig = new Rig())
            {
                var prefab = Item("Crate");
                try
                {
                    int viewBefore = RendererCount(rig.View);
                    var hidden = rig.Equipment.Attach(CharacterSocketId.Back, prefab, SocketVisibility.ShadowOnly);
                    Assert.IsNotNull(hidden);
                    Assert.IsNotNull(rig.Equipment.Occupant(CharacterSocketId.Back));
                    Assert.IsNull(rig.Equipment.ViewOccupant(CharacterSocketId.Back), "shadow-only item on the view body");
                    Assert.AreEqual(viewBefore, RendererCount(rig.View), "view renderers changed");

                    var shown = rig.Equipment.Attach(CharacterSocketId.HandR, prefab, SocketVisibility.FirstPersonVisible);
                    Assert.IsNotNull(shown);
                    Assert.IsNotNull(rig.Equipment.ViewOccupant(CharacterSocketId.HandR));
                    Assert.AreEqual(viewBefore + 1, RendererCount(rig.View), "exactly one view copy");
                    Assert.AreSame(rig.View.Body.Sockets.Get(CharacterSocketId.HandR), rig.Equipment.ViewOccupant(CharacterSocketId.HandR).transform.parent);
                    Assert.AreSame(rig.Shadow.Sockets.Get(CharacterSocketId.HandR), rig.Equipment.Occupant(CharacterSocketId.HandR).transform.parent);
                }
                finally { Object.DestroyImmediate(prefab); }
            }
        }

        [Test]
        public void HeadRefusesAFirstPersonVisibleItem()
        {
            using (var rig = new Rig())
            {
                var prefab = Item("Hat");
                try
                {
                    LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("Head socket is shadow-only"));
                    Assert.IsNull(rig.Equipment.Attach(CharacterSocketId.Head, prefab, SocketVisibility.FirstPersonVisible));
                    Assert.IsNull(rig.Equipment.Occupant(CharacterSocketId.Head));
                    Assert.IsNotNull(rig.Equipment.Attach(CharacterSocketId.Head, prefab, SocketVisibility.ShadowOnly), "head shadow-only must work");
                }
                finally { Object.DestroyImmediate(prefab); }
            }
        }

        // ---- S7, S8: inert items, layer, occupancy, detach ----

        [Test]
        public void AttachedItemsAreInertAndOnTheBodysLayer()
        {
            using (var rig = new Rig())
            {
                var prefab = Item("Crate");
                try
                {
                    rig.Equipment.Attach(CharacterSocketId.HandL, prefab, SocketVisibility.FirstPersonVisible);
                    foreach (var item in new[] { rig.Equipment.Occupant(CharacterSocketId.HandL), rig.Equipment.ViewOccupant(CharacterSocketId.HandL) })
                    {
                        Assert.IsNotNull(item);
                        foreach (var c in item.GetComponentsInChildren<Collider>(true)) Assert.IsFalse(c.enabled, "collider enabled");
                        Assert.AreEqual(0, item.GetComponentsInChildren<Rigidbody>(true).Length, "rigidbody left");
                        foreach (var t in item.GetComponentsInChildren<Transform>(true)) Assert.AreEqual(PlayerLikeLayer, t.gameObject.layer, t.name);
                    }
                }
                finally { Object.DestroyImmediate(prefab); }
            }
        }

        [Test]
        public void AnOccupiedSocketRefusesASecondItem()
        {
            using (var rig = new Rig())
            {
                var prefab = Item("Crate");
                try
                {
                    Assert.IsNotNull(rig.Equipment.Attach(CharacterSocketId.Back, prefab, SocketVisibility.ShadowOnly));
                    LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("occupied"));
                    Assert.IsNull(rig.Equipment.Attach(CharacterSocketId.Back, prefab, SocketVisibility.ShadowOnly));
                    Assert.AreEqual(1, rig.Equipment.Handles.Count);
                }
                finally { Object.DestroyImmediate(prefab); }
            }
        }

        [Test]
        public void DetachRemovesBothCopiesAndLeavesNoRenderersBehind()
        {
            using (var rig = new Rig())
            {
                var prefab = Item("Crate");
                try
                {
                    int shadowBefore = RendererCount(rig.Shadow), viewBefore = RendererCount(rig.View);
                    var h = rig.Equipment.Attach(CharacterSocketId.Back, prefab, SocketVisibility.FirstPersonVisible);
                    Assert.AreEqual(shadowBefore + 1, RendererCount(rig.Shadow));
                    Assert.IsTrue(rig.Equipment.Detach(h));
                    Assert.AreEqual(shadowBefore, RendererCount(rig.Shadow), "shadow orphan");
                    Assert.AreEqual(viewBefore, RendererCount(rig.View), "view orphan");
                    Assert.IsNull(rig.Equipment.Occupant(CharacterSocketId.Back));
                    Assert.IsFalse(rig.Equipment.Detach(h), "second detach of one handle");
                    Assert.IsNotNull(rig.Equipment.Attach(CharacterSocketId.Back, prefab, SocketVisibility.ShadowOnly), "socket free again");
                }
                finally { Object.DestroyImmediate(prefab); }
            }
        }

        // ---- S5, S6: part swap and rebind with items attached ----

        [Test]
        public void APartSwapStillFitsWithItemsAttachedAndTheBoneCountStays()
        {
            using (var rig = new Rig())
            {
                var prefab = Item("Crate");
                try
                {
                    rig.Equipment.Attach(CharacterSocketId.Back, prefab, SocketVisibility.FirstPersonVisible);
                    Assert.IsTrue(rig.Shadow.SetSlot(CharacterSlot.Legs, (Fixture().legs != null ? Fixture().legs : Fixture().body)), "swap refused");
                    Assert.AreEqual(PrefabBoneMapSize(Fixture()), rig.Shadow.Bones.Count);
                    Assert.IsNotNull(rig.Equipment.Occupant(CharacterSocketId.Back), "item lost on swap");
                    Assert.AreSame(rig.Shadow.Sockets.Get(CharacterSocketId.Back), rig.Equipment.Occupant(CharacterSocketId.Back).transform.parent);
                }
                finally { Object.DestroyImmediate(prefab); }
            }
        }

        [Test]
        public void ItemsAreRemadeOnNewBodiesWhenEquipmentIsRebound()
        {
            using (var rig = new Rig())
            {
                var prefab = Item("Crate");
                CharacterBody shadow2 = null;
                CharacterViewCopy view2 = null;
                try
                {
                    rig.Equipment.Attach(CharacterSocketId.HandR, prefab, SocketVisibility.FirstPersonVisible);
                    shadow2 = CharacterAssembler.Assemble(Fixture());
                    view2 = CharacterAssembler.AssembleViewCopy(shadow2);
                    rig.Equipment.Bind(shadow2, view2);
                    Assert.AreSame(shadow2.Sockets.Get(CharacterSocketId.HandR), rig.Equipment.Occupant(CharacterSocketId.HandR).transform.parent);
                    Assert.AreSame(view2.Body.Sockets.Get(CharacterSocketId.HandR), rig.Equipment.ViewOccupant(CharacterSocketId.HandR).transform.parent);
                    Assert.AreEqual(1, rig.Equipment.Handles.Count);
                }
                finally
                {
                    if (view2) Object.DestroyImmediate(view2.gameObject);
                    if (shadow2) Object.DestroyImmediate(shadow2.gameObject);
                    Object.DestroyImmediate(prefab);
                }
            }
        }

        // ---- A6: explicit empty slots; R1: the existing appearances are unchanged ----

        [Test]
        public void AnExplicitlyEmptySlotAssemblesWithNoRenderersAndNoError()
        {
            var appearance = Object.Instantiate(Fixture());
            appearance.feet = null;
            appearance.legs = null;
            appearance.emptySlots = new System.Collections.Generic.List<CharacterSlot> { CharacterSlot.Legs, CharacterSlot.Feet };
            CharacterBody body = null;
            CharacterViewCopy view = null;
            try
            {
                body = CharacterAssembler.Assemble(appearance);
                Assert.IsNotNull(body);
                Assert.AreEqual(0, body.Renderers(CharacterSlot.Legs).Count);
                Assert.AreEqual(0, body.Renderers(CharacterSlot.Feet).Count);
                Assert.Greater(body.Renderers(CharacterSlot.Body).Count, 0);
                Assert.Greater(body.Renderers(CharacterSlot.Head).Count, 0);
                view = CharacterAssembler.AssembleViewCopy(body);
                Assert.IsNotNull(view, "view copy of a body with empty slots");
                Assert.AreEqual(0, view.Body.Renderers(CharacterSlot.Legs).Count);
                Assert.Greater(view.Body.Renderers(CharacterSlot.Body).Count, 0);
            }
            finally
            {
                if (view) Object.DestroyImmediate(view.gameObject);
                if (body) Object.DestroyImmediate(body.gameObject);
                Object.DestroyImmediate(appearance);
            }
        }

        [Test]
        public void AnUnsetSlotThatIsNotListedAsEmptyStillFails()
        {
            var appearance = Object.Instantiate(Fixture());
            appearance.emptySlots = new System.Collections.Generic.List<CharacterSlot>();   // nothing is marked empty: the Survivor's unset Legs part (first checked) must fail
            try
            {
                LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("no Legs part"));
                Assert.IsNull(CharacterAssembler.Assemble(appearance));
            }
            finally { Object.DestroyImmediate(appearance); }
        }

        [Test]
        public void HeadCanNeverBeListedAsEmpty()
        {
            var appearance = Object.Instantiate(Fixture());
            appearance.head = null;
            appearance.emptySlots = new System.Collections.Generic.List<CharacterSlot> { CharacterSlot.Head };
            try
            {
                Assert.IsFalse(appearance.IsEmpty(CharacterSlot.Head));
                LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("no Head part"));
                Assert.IsNull(CharacterAssembler.Assemble(appearance));
            }
            finally { Object.DestroyImmediate(appearance); }
        }

        [Test]
        public void EveryExistingAppearanceIsUnchangedNoEmptySlotsAllFourSlotsFilled()
        {
            var guids = AssetDatabase.FindAssets("t:CharacterAppearance", new[] { "Assets/Art/Character/Appearances" });
            Assert.GreaterOrEqual(guids.Length, 9, "appearance assets found");
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var a = AssetDatabase.LoadAssetAtPath<CharacterAppearance>(path);
                if (a.name == "Men_Survivor") continue; // the new appearance is the only one allowed to carry empty slots
                Assert.AreEqual(0, a.emptySlots.Count, path + ": empty slots");
                Assert.IsNull(a.socketSet, path + ": socket set");
                foreach (CharacterSlot slot in System.Enum.GetValues(typeof(CharacterSlot)))
                    Assert.IsNotNull(a.GetPart(slot), path + ": " + slot);
            }
        }

        [Test]
        public void EveryExistingAppearanceStillAssemblesWithAllFourSlotsAndFiveSockets()
        {
            var guids = AssetDatabase.FindAssets("t:CharacterAppearance", new[] { "Assets/Art/Character/Appearances" });
            foreach (var guid in guids)
            {
                var a = AssetDatabase.LoadAssetAtPath<CharacterAppearance>(AssetDatabase.GUIDToAssetPath(guid));
                if (a.name == "Men_Survivor") continue;
                var body = CharacterAssembler.Assemble(a);
                try
                {
                    Assert.IsNotNull(body, a.name);
                    foreach (CharacterSlot slot in System.Enum.GetValues(typeof(CharacterSlot)))
                        Assert.Greater(body.Renderers(slot).Count, 0, a.name + " " + slot);
                    Assert.AreEqual(5, body.GetComponentsInChildren<Transform>(true).Count(t => t.name.StartsWith("Socket_")), a.name);
                }
                finally { if (body) Object.DestroyImmediate(body.gameObject); }
            }
            LogAssert.NoUnexpectedReceived();
        }
    }
}
