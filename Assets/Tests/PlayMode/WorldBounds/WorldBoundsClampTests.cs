using System.Collections;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using WashedAshore.Gameplay;

namespace WashedAshore.Tests.PlayMode
{
    /// <summary>Spec B4: the clamp snaps a player (and a Rigidbody) north of the line back south and logs it.</summary>
    public class WorldBoundsClampTests
    {
        const string WorldScene = "World";

        static IEnumerator LoadWorld()
        {
            SceneManager.LoadScene(WorldScene, LoadSceneMode.Single);
            yield return null;
            yield return null;
        }

        static float MidLandX(WorldBoundsRule rule)
        {
            var xs = WorldBoundsTestKit.LandPointsAlongLine(rule, WorldBoundsTestKit.Config().WaterLevelY, 1);
            return xs[0];
        }

        [Test]
        public void Rule_SnapsOnlyNorthOfLine()
        {
            var rule = new WorldBoundsRule(new[] { new Vector2(-100f, 50f), new Vector2(100f, 70f) }, 3f);
            Assert.AreEqual(60f, rule.LineZAt(0f), 1e-4f);
            Assert.AreEqual(50f, rule.LineZAt(-500f), 1e-4f, "line continues flat past the west end");
            Assert.AreEqual(70f, rule.LineZAt(500f), 1e-4f, "line continues flat past the east end");
            Assert.IsFalse(rule.TryConstrain(new Vector3(0f, 5f, 59.9f), out _));
            Assert.IsTrue(rule.TryConstrain(new Vector3(0f, 5f, 70f), out var c));
            Assert.AreEqual(new Vector3(0f, 5f, 57f), c);
        }

        [UnityTest]
        public IEnumerator Player_TeleportedTenMetresNorth_IsSnappedBackWithinOneFrame()
        {
            yield return LoadWorld();
            var clamp = WorldBoundsTestKit.Clamp();
            var player = WorldBoundsTestKit.Player();
            var rule = clamp.Rule;
            float x = MidLandX(rule);
            yield return new WaitForSeconds(0.5f);

            int snapsBefore = clamp.SnapCount;
            WorldBoundsTestKit.Teleport(player, new Vector3(x, 0f, rule.LineZAt(x) + 10f), 0f, 1f);
            Assert.Greater(rule.NorthOfLine(player.transform.position), 9.9f, "teleport did not land north of the line");
            int frame = Time.frameCount;
            LogAssert.Expect(LogType.Warning, new Regex($"^WorldBoundsClamp: {Regex.Escape(player.name)} was 10\\.00 m north"));
            yield return null;

            Assert.LessOrEqual(Time.frameCount - frame, 1);
            Vector3 p = player.transform.position;
            Debug.Log($"WorldBoundsClampTests: player at {p:F2}, {rule.NorthOfLine(p):F2} m from the line after {Time.frameCount - frame} frame(s)");
            Assert.Less(rule.NorthOfLine(p), 0f, "player still north of the line");
            Assert.AreEqual(snapsBefore + 1, clamp.SnapCount);
        }

        [UnityTest]
        public IEnumerator Rigidbody_DroppedTenMetresNorth_IsSnappedBackWithinOneFrame()
        {
            yield return LoadWorld();
            var clamp = WorldBoundsTestKit.Clamp();
            var rule = clamp.Rule;
            float x = MidLandX(rule);
            Assert.IsTrue(TerrainQuery.TryGroundHeight(new Vector3(x, 0f, rule.LineZAt(x) + 10f), out float ground));
            yield return null;

            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = "ClampTestBox";
            box.transform.position = new Vector3(x, ground + 2f, rule.LineZAt(x) + 10f);
            var rb = box.AddComponent<Rigidbody>();
            rb.linearVelocity = new Vector3(0f, 0f, 5f);
            int frame = Time.frameCount;
            LogAssert.Expect(LogType.Warning, new Regex("^WorldBoundsClamp: ClampTestBox was"));
            yield return null;

            Debug.Log($"WorldBoundsClampTests: Rigidbody at {rb.position:F2} after {Time.frameCount - frame} frame(s)");
            Assert.LessOrEqual(Time.frameCount - frame, 1);
            Assert.IsFalse(rule.IsNorth(rb.position), "Rigidbody still north of the line");
            Assert.LessOrEqual(rb.linearVelocity.z, 0.01f, "northward velocity kept");
            Object.Destroy(box);
        }

        /// <summary>
        /// producer-2 / team-lead: more than 1024 static colliders north of the line (vista, fence corners) must not hide a
        /// Rigidbody from the clamp: snapped in 1 frame, and no clamp warning other than the snap itself.
        /// </summary>
        [UnityTest]
        public IEnumerator Rigidbody_AmongOver1024StaticCollidersNorth_IsSnappedWithinOneFrame()
        {
            yield return LoadWorld();
            var clamp = WorldBoundsTestKit.Clamp();
            var rule = clamp.Rule;
            float x = MidLandX(rule);
            Assert.IsTrue(TerrainQuery.TryGroundHeight(new Vector3(x, 0f, rule.LineZAt(x) + 10f), out float ground));

            const int StaticCount = 1500;
            var clutter = new GameObject("ClampTestClutter");
            for (int i = 0; i < StaticCount; i++)
            {
                var c = new GameObject($"Static_{i}");
                c.transform.SetParent(clutter.transform, false);
                c.transform.position = new Vector3(x + (i % 50) * 3f - 75f, ground + 5f + (i / 50) * 2f, rule.LineZAt(x) + 30f);
                c.AddComponent<BoxCollider>();
            }
            var fallback = new System.Collections.Generic.List<string>();
            void Watch(string msg, string stack, LogType type)
            {
                if (type == LogType.Warning && msg.StartsWith("WorldBoundsClamp:") && !msg.Contains("ClampTestBox")) fallback.Add(msg);
            }
            Application.logMessageReceived += Watch;
            try
            {
                yield return null;
                var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
                box.name = "ClampTestBox";
                box.transform.position = new Vector3(x, ground + 2f, rule.LineZAt(x) + 10f);
                var rb = box.AddComponent<Rigidbody>();
                int frame = Time.frameCount;
                LogAssert.Expect(LogType.Warning, new Regex("^WorldBoundsClamp: ClampTestBox was"));
                yield return null;

                Debug.Log($"WorldBoundsClampTests: {StaticCount} static colliders north; Rigidbody at {rb.position:F2} after {Time.frameCount - frame} frame(s), other clamp warnings={fallback.Count}");
                Assert.LessOrEqual(Time.frameCount - frame, 1);
                Assert.IsFalse(rule.IsNorth(rb.position), "Rigidbody still north of the line");
                Assert.IsEmpty(fallback, "unexpected clamp warnings");
                Object.Destroy(box);
            }
            finally
            {
                Application.logMessageReceived -= Watch;
                Object.Destroy(clutter);
            }
        }

        /// <summary>
        /// producer-2: the clamp's per-frame cost must not scale with static scenery. With the full vista forest loaded
        /// (its tree colliders live in the TerrainCollider) its LateUpdate must average under 0.5 ms.
        /// </summary>
        [UnityTest]
        public IEnumerator Clamp_PerFrameCost_UnderHalfAMillisecondWithTheVistaLoaded()
        {
            yield return LoadWorld();
            var clamp = WorldBoundsTestKit.Clamp();
            yield return new WaitForSeconds(1f);
            var lateUpdate = typeof(WorldBoundsClamp).GetMethod("LateUpdate", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            const int Calls = 200;
            for (int i = 0; i < 10; i++) lateUpdate.Invoke(clamp, null); // warm up
            var sw = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < Calls; i++) lateUpdate.Invoke(clamp, null);
            sw.Stop();
            double ms = sw.Elapsed.TotalMilliseconds / Calls;
            int trees = 0;
            foreach (var t in Terrain.activeTerrains) trees += t.terrainData.treeInstanceCount;
            Debug.Log($"WorldBoundsClampTests: clamp LateUpdate {ms:F4} ms/frame over {Calls} calls; terrain tree instances={trees}, Rigidbodies={Object.FindObjectsByType<Rigidbody>().Length}");
            Assert.Less(ms, 0.5, "clamp per-frame cost");
        }
    }
}
