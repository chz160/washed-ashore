using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using WashedAshore.Lookout;
using WashedAshore.Lookout.Editor;
using WashedAshore.Tests.Lookout.PlayMode;
using WashedAshore.World;

namespace WashedAshore.Tests.Lookout.EditMode
{
    /// <summary>
    /// #45 M1/M3: no collider moves during play without a kinematic Rigidbody or being switched off for the move. Every component that
    /// moves transforms lists them in <see cref="IMovingTransforms"/>; this scans the committed scene and prefabs, and a configured
    /// tower (today's assets hold no hinged mover: the hatch, sashes and firefinder are wired by LookoutGameplaySetup), and fails
    /// on a collider under a moved transform that is neither. Also the tower's cooking options and the BoundaryGate pose.
    /// </summary>
    public class MovingCollidersTests
    {
        const string WorldScene = "Assets/Scenes/World.unity";
        const string TowerPrefab = "Assets/World/Lookout/LookoutTower.prefab";

        static string Path(Component c)
        {
            var s = c.name;
            for (var t = c.transform.parent; t != null; t = t.parent) s = t.name + "/" + s;
            return s;
        }

        /// <summary>Colliders under a moved transform that are neither switched off while it moves nor on a kinematic body.</summary>
        static List<string> Violations(IEnumerable<IMovingTransforms> owners)
        {
            var bad = new List<string>();
            foreach (var owner in owners)
            {
                var off = new HashSet<Collider>(owner.SwitchedOffWhileMoving.Where(c => c != null));
                foreach (var moved in owner.MovedTransforms)
                {
                    if (moved == null) continue;
                    foreach (var c in moved.GetComponentsInChildren<Collider>(true))
                    {
                        if (off.Contains(c)) continue;
                        if (c.attachedRigidbody != null && c.attachedRigidbody.isKinematic) continue;
                        bad.Add($"{owner.GetType().Name} moves '{moved.name}', but collider {Path(c)} is neither switched off while it moves nor on a kinematic Rigidbody");
                    }
                }
            }
            return bad;
        }

        static IEnumerable<IMovingTransforms> OwnersIn(GameObject root) =>
            root.GetComponentsInChildren<MonoBehaviour>(true).OfType<IMovingTransforms>();

        [Test]
        public void CommittedScenesAndPrefabs_HaveNoUnguardedMovingCollider()
        {
            var bad = new List<string>();
            if (SceneManager.GetActiveScene().path != WorldScene) EditorSceneManager.OpenScene(WorldScene, OpenSceneMode.Single);
            foreach (var go in SceneManager.GetActiveScene().GetRootGameObjects()) bad.AddRange(Violations(OwnersIn(go)));
            int prefabs = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.StartsWith("Assets/ThirdParty/") || path.StartsWith("Assets/Tests/")) continue;
                prefabs++;
                var root = PrefabUtility.LoadPrefabContents(path);
                try { bad.AddRange(Violations(OwnersIn(root)).Select(v => path + ": " + v)); }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            Assert.Greater(prefabs, 0, "no prefabs scanned");
            Assert.IsEmpty(bad, "colliders moved without a kinematic body or a switch-off:\n" + string.Join("\n", bad));
        }

        /// <summary>Today's assets hold no hatch or sash, so the scan also runs on a tower wired through the real components' Configure calls (RealTowerWiring; nothing saved).</summary>
        [Test]
        public void ConfiguredTower_HasAHatchAndSashes_AndEveryMovedColliderIsGuarded()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(TowerPrefab);
            Assert.IsNotNull(prefab, "No tower prefab at " + TowerPrefab);
            var root = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            // In Edit mode the children of a prefab instance cannot be reparented (Adopt would fail: "Setting the parent of a transform which resides in a Prefab instance is not possible"), so the in-memory instance is unpacked first. Same nodes, same geometry; nothing is saved.
            PrefabUtility.UnpackPrefabInstance(root, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            try
            {
                // The real components, wired as LookoutGameplaySetup.Apply does (its manifest reader is schema 4, the manifest is 6): RealTowerWiring.
                var wired = RealTowerWiring.Wire(root);
                Assert.IsNull(wired.hatchError, "hatch wiring failed: " + wired.hatchError);
                Assert.IsNull(wired.sashError, "sash wiring failed: " + wired.sashError);
                Assert.IsNull(wired.firefinderError, "firefinder wiring failed: " + wired.firefinderError);
                Assert.IsNotNull(wired.hatch, "wiring gave no hatch");
                var owners = OwnersIn(root).ToList();
                Assert.GreaterOrEqual(owners.Count(o => o is TrapDoor), 1, "no hatch found");
                Assert.GreaterOrEqual(owners.Count(o => o is WindowSash), 1, "no sash found");
                Assert.GreaterOrEqual(owners.Count(o => o is Firefinder), 1, "no firefinder found");
                var alidade = owners.OfType<Firefinder>().First().Alidade;
                Assert.IsEmpty(alidade.GetComponentsInChildren<Collider>(true), "a collider sits under the firefinder's alidade");
                foreach (var sash in owners.OfType<WindowSash>())
                    Assert.IsNotEmpty(sash.SwitchedOffWhileMoving.ToList(), sash.Id + ": no collider is switched off while its leaf moves");
                Assert.IsEmpty(Violations(owners), "unguarded moving colliders");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        // ---- the scan is alive: a synthetic mover must be flagged ----
        class SyntheticMover : MonoBehaviour, IMovingTransforms
        {
            public Transform moved;
            public List<Collider> off = new List<Collider>();
            public IEnumerable<Transform> MovedTransforms { get { yield return moved; } }
            public IEnumerable<Collider> SwitchedOffWhileMoving => off;
        }

        static (GameObject root, SyntheticMover mover, BoxCollider box) Synthetic()
        {
            var root = new GameObject("SyntheticMover");
            var leaf = new GameObject("Leaf");
            leaf.transform.SetParent(root.transform, false);
            var box = leaf.AddComponent<BoxCollider>();
            var mover = root.AddComponent<SyntheticMover>();
            mover.moved = leaf.transform;
            return (root, mover, box);
        }

        [Test]
        public void Scan_FlagsAColliderThatMovesWithNoRigidbodyAndNoSwitchOff()
        {
            var (root, mover, box) = Synthetic();
            try
            {
                Assert.AreEqual(1, Violations(new[] { mover }).Count, "an unguarded moving collider must be flagged");
                mover.off.Add(box);
                Assert.IsEmpty(Violations(new[] { mover }), "a switched-off collider is guarded");
                mover.off.Clear();
                var body = box.gameObject.AddComponent<Rigidbody>();
                body.isKinematic = true;
                Assert.IsEmpty(Violations(new[] { mover }), "a kinematic body is guarded");
                body.isKinematic = false;
                Assert.AreEqual(1, Violations(new[] { mover }).Count, "a dynamic body is not a kinematic mover");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        [Test]
        public void TowerConvexColliders_HaveTheExplicitCookingOptions()
        {
            Assert.AreEqual((MeshColliderCookingOptions)30, WalkableTowerBuilder.TowerCooking, "the constant is the old default, written out");
            var root = PrefabUtility.LoadPrefabContents(TowerPrefab);
            try
            {
                var convex = root.GetComponentsInChildren<MeshCollider>(true).Where(m => m.convex).ToList();
                Assert.AreEqual(40, convex.Count, "the tower has 40 convex MeshColliders");
                foreach (var m in convex)
                {
                    Assert.IsNotNull(m.sharedMesh, m.name + " has no mesh");
                    Assert.AreEqual(WalkableTowerBuilder.TowerCooking, m.cookingOptions, m.name + " cookingOptions");
                }
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        [Test]
        public void AutoSyncTransforms_IsOff_SoASettleSyncsOnce()
        {
            Assert.IsFalse(Physics.autoSyncTransforms, "the hinge code relies on one explicit Physics.SyncTransforms per settle");
        }

        /// <summary>BoundaryGate never opens in play, so the scene must be saved in the pose Apply gives it.</summary>
        [Test]
        public void BoundaryGate_SavedLeafPose_EqualsApplysPose()
        {
            if (SceneManager.GetActiveScene().path != WorldScene) EditorSceneManager.OpenScene(WorldScene, OpenSceneMode.Single);
            var type = Type.GetType("WashedAshore.Level.BoundaryGate, WashedAshore.Level");
            Assert.IsNotNull(type, "BoundaryGate type not found");
            var gates = UnityEngine.Object.FindObjectsByType(type, FindObjectsInactive.Include);
            Assert.IsNotEmpty(gates, "no BoundaryGate in World");
            foreach (Component gate in gates)
            {
                T Field<T>(string n) => (T)type.GetField(n).GetValue(gate);
                var config = Field<MapConfig>("config");
                float open = Field<float>("openAngle");
                float a = config != null && config.gateOpen ? open : 0f;
                var left = Field<Transform>("leftLeaf"); var right = Field<Transform>("rightLeaf");
                Assert.IsNotNull(left); Assert.IsNotNull(right);
                Assert.Less(Quaternion.Angle(left.localRotation, Quaternion.Euler(0f, -a, 0f)), 0.01f, "left leaf is not in Apply's pose");
                Assert.Less(Quaternion.Angle(right.localRotation, Quaternion.Euler(0f, a, 0f)), 0.01f, "right leaf is not in Apply's pose");
            }
        }
    }
}
