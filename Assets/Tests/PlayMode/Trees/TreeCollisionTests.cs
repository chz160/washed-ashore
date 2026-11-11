using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using WashedAshore.Gameplay;
using WashedAshore.Tests.Support;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace WashedAshore.Tests.PlayMode
{
    /// <summary>
    /// Tree collision, PlayMode (#21 T1 and T2). The Bells Bend LOD prefabs go onto a flat scratch terrain as terrain trees, exactly as the
    /// terrain builder uses them, and the real player (kinematic CharacterController, driven tick by tick with Simulate) walks and sprints
    /// into each one from four sides. The scratch terrain sits clear of the real tiles. The collider radius and height are read from the
    /// prefab, so the test follows a rebuild of the prefabs.
    /// </summary>
    public class TreeCollisionTests
    {
        const string Root = "Assets/World/BellsBend/Art/TreeLods/";
        static readonly string[] Prototypes =
        {
            "CommonTree_1", "CommonTree_2", "CommonTree_3", "CommonTree_4", "CommonTree_5", "Pine_1", "Pine_3",
            "TwistedTree_1", "DeadTree_1", "DeadTree_2", "DeadTree_3",
        };
        const float Size = 300f, GroundY = 100f, Dt = PlayerController.TickDt;
        const float Lane = 20f;           // metres between trees in the row
        const float StartDistance = 6f;   // how far in front of the trunk the player starts
        const float Slack = 0.1f;         // CharacterController skin width and contact tolerance

        PlayerController player;
        Terrain scratch;
        Vector3 origin;
        GameObject[] prefabs;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            SceneManager.LoadScene(TestSpawn.WorldScene, LoadSceneMode.Single);
            yield return null;
            yield return null;
            player = TestSpawn.Player();
            float minX = float.MaxValue, minZ = float.MaxValue;
            foreach (var t in Terrain.activeTerrains)
            {
                minX = Mathf.Min(minX, t.transform.position.x);
                minZ = Mathf.Min(minZ, t.transform.position.z);
            }
            origin = new Vector3(minX - Size - 500f, GroundY, minZ - Size - 500f);
            prefabs = new GameObject[Prototypes.Length];
#if UNITY_EDITOR
            for (int i = 0; i < Prototypes.Length; i++)
            {
                prefabs[i] = AssetDatabase.LoadAssetAtPath<GameObject>(Root + Prototypes[i] + ".prefab");
                Assert.IsNotNull(prefabs[i], "missing LOD prefab " + Prototypes[i]);
            }
#else
            Assert.Ignore("needs the Editor to load the LOD prefabs");
#endif
        }

        [TearDown]
        public void TearDown()
        {
            if (player != null) player.AutoTick = true;
            DestroyScratch();
        }

        void DestroyScratch()
        {
            if (scratch == null) return;
            var d = scratch.terrainData;
            Object.Destroy(scratch.gameObject);
            Object.Destroy(d);
            scratch = null;
        }

        static CapsuleCollider ColliderOf(GameObject prefab)
        {
            var c = prefab.GetComponent<CapsuleCollider>();
            Assert.IsNotNull(c, prefab.name + " has no CapsuleCollider on its root (T3: the collider must be on the LOD prefab)");
            Assert.IsFalse(c.isTrigger, prefab.name + " collider is a trigger");
            return c;
        }

        /// <summary>A scratch terrain whose tree instances are (prototype, world offset from the origin, width scale).</summary>
        IEnumerator BuildTerrain(List<(int proto, Vector3 offset, float width, float height)> trees)
        {
            DestroyScratch();
            var data = new TerrainData { heightmapResolution = 33, size = new Vector3(Size, 10f, Size) };
            var protos = new TreePrototype[prefabs.Length];
            for (int i = 0; i < protos.Length; i++) protos[i] = new TreePrototype { prefab = prefabs[i] };
            data.treePrototypes = protos;
            var list = new TreeInstance[trees.Count];
            for (int i = 0; i < list.Length; i++)
            {
                var t = trees[i];
                list[i] = new TreeInstance
                {
                    prototypeIndex = t.proto,
                    position = new Vector3(t.offset.x / Size, 0f, t.offset.z / Size),
                    widthScale = t.width, heightScale = t.height,
                    color = Color.white, lightmapColor = Color.white,
                };
            }
            data.SetTreeInstances(list, true);
            var go = Terrain.CreateTerrainGameObject(data);
            go.transform.position = origin;
            scratch = go.GetComponent<Terrain>();
            scratch.Flush();
            yield return null;
            Physics.SyncTransforms();
            yield return new WaitForFixedUpdate();
            yield return null;
        }

        static float Yaw(Vector3 dir) => Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;

        /// <summary>
        /// Starts the player <see cref="StartDistance"/> m from the trunk at <paramref name="tree"/> (terrain offset), facing it from direction
        /// <paramref name="from"/> (unit, from the trunk out to the start), and holds forward for <paramref name="ticks"/> ticks.
        /// Returns the closest distance reached along the approach axis, and the last one.
        /// </summary>
        IEnumerator Approach(Vector3 tree, Vector3 from, bool sprint, int ticks, System.Action<float, float, float> result)
        {
            player.AutoTick = false;
            Vector3 trunk = origin + tree;
            Vector3 start = trunk + from * StartDistance; start.y = GroundY + 1f;
            float yaw = Yaw(-from);
            yield return TestSpawn.At(start, yaw);
            var idle = new PlayerCommand { Yaw = yaw };
            for (int i = 0; i < 30; i++) player.Simulate(idle);
            Assert.IsTrue(player.IsGrounded, "grounded on the scratch terrain");
            var go = new PlayerCommand { Move = Vector2.up, Yaw = yaw, Buttons = sprint ? PlayerButtons.Sprint : PlayerButtons.None };
            float min = float.MaxValue, last = 0f;
            for (int i = 0; i < ticks; i++)
            {
                player.Simulate(go);
                Vector3 d = player.transform.position - trunk;
                last = new Vector2(d.x, d.z).magnitude;   // axis to axis: a head-on push may slide round the trunk, but never inside it
                min = Mathf.Min(min, last);
            }
            result(min, last, 0f);
        }

        static readonly Vector3[] Sides = { Vector3.forward, Vector3.back, Vector3.left, Vector3.right };

        IEnumerator BlockedFromAllSides(int protoIndex, float width, float height, string label)
        {
            var cc = ColliderOf(prefabs[protoIndex]);
            float treeR = cc.radius * width;
            var trees = new List<(int, Vector3, float, float)> { (protoIndex, new Vector3(Size / 2f, 0f, Size / 2f), width, height) };
            yield return BuildTerrain(trees);
            // The collider is aimed at, and measured from, its own centre in the prefab (TwistedTree_1's is off the root), scaled by the instance's width.
            Vector3 aim = new Vector3(Size / 2f + cc.center.x * width, 0f, Size / 2f + cc.center.z * width);
            foreach (bool sprint in new[] { false, true })
                foreach (var side in Sides)
                {
                    float min = 0f, last = 0f;
                    yield return Approach(aim, side, sprint, 180, (m, l, s) => { min = m; last = l; });
                    // Read after the approach: the capsule radius is only final once the player has been placed and has run (it was 0.25 before, 0.40 after).
                    float stop = treeR + player.Capsule.radius;   // axis-to-axis distance at contact
                    string what = $"{label} {(sprint ? "sprint" : "walk")} from {side}: closest axis distance {min:F3} m (contact at {stop:F3}), ended {last:F3} m";
                    Debug.Log("TreeCollisionTests " + what);
                    Assert.GreaterOrEqual(min, stop - Slack, "walked into the trunk: " + what);
                    Assert.LessOrEqual(min, stop + 0.3f, "never reached the trunk (something else stopped the player): " + what);
                }
        }


        [UnityTest]
        public IEnumerator T1_EveryTreePrototypeBlocksFromFourSidesAtWalkAndSprint()
        {
            for (int i = 0; i < Prototypes.Length; i++)
                yield return BlockedFromAllSides(i, 1f, 1f, Prototypes[i]);
        }

        // T1 / open item: field cedars are the Pine prototypes at another scale; the collider must scale with the instance.
        // T1 / open item: terrain trees keep the width scale and the height scale apart. BellsBendVegetation.Instance() sets
        // heightScale = s = (spec height range / unit height) and widthScale = s x [0.85, 1.15]. Field cedars (Pine, 5.5-8 m on a 7.32 m
        // prototype) give s 0.75-1.09; common trees (12-20 m on 7.26 m) reach 2.75. The ends of each range are walked here.
        [UnityTest]
        public IEnumerator T1_ColliderScalesWithTheTerrainInstance_FieldCedarAndLargeCommonTree()
        {
            int pine = System.Array.IndexOf(Prototypes, "Pine_1");
            Assert.GreaterOrEqual(pine, 0);
            float h = ColliderOf(prefabs[pine]).height;
            float unit = 7.32f;   // Pine_1 unit height, technical-artist's table
            float sMin = 5.5f / unit, sMax = 8f / unit;
            Debug.Log($"TreeCollisionTests field cedar instance scales: heightScale {sMin:F3}-{sMax:F3}, widthScale {sMin * 0.85f:F3}-{sMax * 1.15f:F3} (collider height {h:F2} m)");
            yield return BlockedFromAllSides(pine, sMin * 0.85f, sMin, $"Pine_1 field cedar smallest (w {sMin * 0.85f:F2}, h {sMin:F2})");
            yield return BlockedFromAllSides(pine, sMax * 1.15f, sMax, $"Pine_1 field cedar largest (w {sMax * 1.15f:F2}, h {sMax:F2})");
            yield return BlockedFromAllSides(pine, 0.75f, 0.75f, "Pine_1 x0.75");
            yield return BlockedFromAllSides(pine, 2f, 2f, "Pine_1 x2");
            float big = 20f / 7.26f;
            yield return BlockedFromAllSides(0, big * 1.15f, big, $"CommonTree_1 largest (w {big * 1.15f:F2}, h {big:F2})");
        }

        [UnityTest]
        public IEnumerator T2_AGapBetweenTwoTreesLetsThePlayerThroughAndANarrowOneDoesNot()
        {
            // CommonTree_1 (the common case) and TwistedTree_1 (radius 1.00 m on the root while its trunk leans, so the collider is the widest).
            foreach (string name in new[] { "CommonTree_1", "TwistedTree_1" })
                yield return Squeeze(System.Array.IndexOf(Prototypes, name));
        }

        IEnumerator Squeeze(int proto)
        {
            var cc = ColliderOf(prefabs[proto]);
            float treeR = cc.radius;
            Vector3 centre = new Vector3(cc.center.x, 0f, cc.center.z);   // collider centre from the tree origin; the gap is measured between collider centres
            Vector3 mid = new Vector3(Size / 2f, 0f, Size / 2f);
            // The capsule radius is only final once the player has been placed and has run (0.25 before, 0.40 after), so settle it first.
            yield return BuildTerrain(new List<(int, Vector3, float, float)>());
            player.AutoTick = false;
            yield return TestSpawn.At(origin + mid + Vector3.up, 0f);
            for (int i = 0; i < 30; i++) player.Simulate(new PlayerCommand());
            float playerR = player.Capsule.radius;
            foreach (var (gap, shouldPass) in new[] { (2f * playerR + 0.4f, true), (2f * playerR - 0.3f, false) })
            {
                // Two trunks side by side along x, an edge-to-edge gap of `gap`; the player walks along +z through the middle.
                float half = treeR + gap / 2f;
                var trees = new List<(int, Vector3, float, float)> { (proto, mid + Vector3.left * half - centre, 1f, 1f), (proto, mid + Vector3.right * half - centre, 1f, 1f) };
                yield return BuildTerrain(trees);
                foreach (bool sprint in new[] { false, true })
                {
                    player.AutoTick = false;
                    Vector3 start = origin + mid + Vector3.back * 6f; start.y = GroundY + 1f;
                    yield return TestSpawn.At(start, 0f);
                    var idle = new PlayerCommand { Yaw = 0f };
                    for (int i = 0; i < 30; i++) player.Simulate(idle);
                    var go = new PlayerCommand { Move = Vector2.up, Yaw = 0f, Buttons = sprint ? PlayerButtons.Sprint : PlayerButtons.None };
                    for (int i = 0; i < 180; i++) player.Simulate(go);
                    float z = player.transform.position.z - (origin.z + mid.z);
                    string what = $"{Prototypes[proto]} gap {gap:F2} m ({(sprint ? "sprint" : "walk")}): ended {z:F2} m past the trunk line";
                    Debug.Log("TreeCollisionTests T2 " + what);
                    if (shouldPass) Assert.Greater(z, treeR + 1f, "the player did not get through a visibly open gap: " + what);
                    else Assert.Less(z, 0f, "the player squeezed through a gap narrower than the capsule (no collision): " + what);
                }
            }
        }
    }
}
