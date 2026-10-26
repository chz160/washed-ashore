using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using WashedAshore.Gameplay;
using WashedAshore.Tests.Support;

namespace WashedAshore.Tests.PlayMode
{
    /// <summary>
    /// P7 acceptance tests for the POC walk-around. Thresholds follow
    /// _bmad-output/poc/acceptance-checklist.md.
    /// </summary>
    public class WorldWalkTests : InputTestFixture
    {
        const string WorldScene = "World";

        Keyboard keyboard;
        Mouse mouse;

        public override void Setup()
        {
            base.Setup();
            keyboard = InputSystem.AddDevice<Keyboard>();
            mouse = InputSystem.AddDevice<Mouse>();
        }

        static IEnumerator LoadWorld()
        {
            SceneManager.LoadScene(WorldScene, LoadSceneMode.Single);
            yield return null;
        }

        static PlayerController FindPlayer()
        {
            var tagged = GameObject.FindGameObjectsWithTag("Player");
            Assert.AreEqual(1, tagged.Length, "Expected exactly one object tagged Player");
            Assert.IsNotNull(tagged[0].GetComponent<CharacterController>(), "Player has no CharacterController");
            var player = tagged[0].GetComponent<PlayerController>();
            Assert.IsNotNull(player, "Player has no PlayerController");
            return player;
        }

        static Vector2 Horizontal(Vector3 v) => new Vector2(v.x, v.z);

        [UnityTest]
        public IEnumerator A_LoadsWorld()
        {
            yield return LoadWorld();
            Assert.AreEqual(WorldScene, SceneManager.GetActiveScene().name);
            FindPlayer();
        }

        [UnityTest]
        public IEnumerator B_HoldingWForTwoSecondsMovesPlayerAtLeastThreeMetres()
        {
            yield return LoadWorld();
            var player = FindPlayer();
            float waited = 0f;
            while (!player.IsGrounded && waited < 3f)
            {
                waited += Time.deltaTime;
                yield return null;
            }

            Vector2 start = Horizontal(player.transform.position);
            Press(keyboard.wKey);
            yield return new WaitForSeconds(2f);
            Release(keyboard.wKey);

            float moved = Vector2.Distance(start, Horizontal(player.transform.position));
            Debug.Log($"WorldWalkTests: W held 2 s moved {moved:F2} m");
            Assert.GreaterOrEqual(moved, 3f);
        }

        [UnityTest]
        public IEnumerator C_PlayerIsGroundedAfterThreeSeconds()
        {
            yield return LoadWorld();
            var player = FindPlayer();
            yield return new WaitForSeconds(3f);

            Vector3 pos = player.transform.position;
            Assert.IsTrue(player.IsGrounded, $"Player not grounded at {pos}");
            Assert.IsTrue(TerrainQuery.TryGroundHeight(pos, out float ground), $"No terrain tile under {pos}");
            Assert.GreaterOrEqual(pos.y, ground - 0.1f, "Player fell through the terrain");
        }

        /// <summary>
        /// C2a (option b, spec-playmode-spawn-19 5.1): the game's own start. The scene's spawn reference is TowerBaseSpawn and the player lands there on the ground.
        /// Nothing in this case moves the player: no TestSpawn, no Respawn(), no transform write, no controller toggle (director amend2 A2.1).
        /// </summary>
        [UnityTest]
        public IEnumerator C2_GameStartsAtTowerBaseSpawnOnTheGround()
        {
            var fallbackWarnings = new System.Collections.Generic.List<string>();
            void Watch(string msg, string stack, LogType type)
            {
                if (type == LogType.Warning && msg.StartsWith("PlayerController:")) fallbackWarnings.Add(msg);
            }
            Application.logMessageReceived += Watch;
            PlayerController player;
            GameObject spawn;
            try
            {
                // Watch through PlayerController.Start (it respawns there) and the landing.
                yield return LoadWorld();
                player = FindPlayer();
                spawn = GameObject.Find("TowerBaseSpawn");
                Assert.IsNotNull(spawn, "No TowerBaseSpawn in World");
                for (int i = 0; i < 10; i++) yield return null;
                yield return WaitGroundedUpTo(player, 3f);
            }
            finally { Application.logMessageReceived -= Watch; }

            Assert.AreSame(spawn.transform, player.SpawnPoint, "PlayerController.spawnPoint is not the scene's TowerBaseSpawn (the scene reference, not the name fallback)");
            AssertLanded(player, spawn.transform, "TowerBaseSpawn", fallbackWarnings);
        }

        /// <summary>C2b (spec 5.2): TestSpawn.At("PlayerSpawn") puts the player on PlayerSpawn, facing its yaw, on the ground. A6 and B6 start there.</summary>
        [UnityTest]
        public IEnumerator C2b_TestSpawnAtPlayerSpawnLandsOnTheGround()
        {
            var fallbackWarnings = new System.Collections.Generic.List<string>();
            void Watch(string msg, string stack, LogType type)
            {
                if (type == LogType.Warning && msg.StartsWith("PlayerController:")) fallbackWarnings.Add(msg);
            }
            Application.logMessageReceived += Watch;
            PlayerController player;
            GameObject spawn;
            try
            {
                yield return LoadWorld();
                player = FindPlayer();
                spawn = GameObject.Find("PlayerSpawn");
                Assert.IsNotNull(spawn, "No PlayerSpawn in World");
                yield return TestSpawn.At(TestSpawn.PlayerSpawnPoint);
                yield return WaitGroundedUpTo(player, 3f);
            }
            finally { Application.logMessageReceived -= Watch; }

            AssertLanded(player, spawn.transform, "PlayerSpawn", fallbackWarnings);
            float yawMiss = Mathf.Abs(Mathf.DeltaAngle(player.transform.eulerAngles.y, spawn.transform.eulerAngles.y));
            Assert.LessOrEqual(yawMiss, 1f, $"Player yaw {player.transform.eulerAngles.y:F2} is {yawMiss:F2} deg from PlayerSpawn's yaw {spawn.transform.eulerAngles.y:F2}");
            var pop = Object.FindAnyObjectByType<WashedAshore.Wildlife.WildlifePopulation>();
            Assert.IsNotNull(pop, "No WildlifePopulation in World");
            Assert.AreSame(spawn.transform, pop.PlayerSpawn, "TestSpawn's PlayerSpawn is not the transform WildlifePopulation lays its route and animals out from");
        }

        /// <summary>
        /// C2c: PlayerController.SpawnAt, through TestSpawn.At(position, yaw). With ground snap the player lands on the ground at the pose;
        /// without it the given height is kept. Neither changes the spawn point. Bad input throws and moves nothing. AtTowerBase lands on TowerBaseSpawn.
        /// </summary>
        [UnityTest]
        public IEnumerator C2c_SpawnAtPlacesWithoutChangingTheSpawnPoint()
        {
            yield return LoadWorld();
            var player = FindPlayer();
            var spawn = GameObject.Find(TestSpawn.PlayerSpawnPoint);
            var tower = GameObject.Find(TestSpawn.TowerBasePoint);
            Assert.IsNotNull(spawn, "No PlayerSpawn in World");
            Assert.IsNotNull(tower, "No TowerBaseSpawn in World");
            Transform before = player.SpawnPoint;

            // Snapped pose: a few metres off PlayerSpawn, a yaw no scene point uses.
            Vector3 pose = spawn.transform.position + new Vector3(3f, 0f, 2f);
            const float yaw = 37f;
            yield return TestSpawn.At(pose, yaw);
            yield return WaitGroundedUpTo(player, 3f);
            Vector3 pos = player.transform.position;
            Assert.IsTrue(TerrainQuery.TryGroundHeight(pos, out float ground), $"No terrain tile under {pos}");
            Assert.IsTrue(player.IsGrounded, $"Player not grounded at {pos} after SpawnAt");
            Assert.That(pos.y, Is.InRange(ground - 0.1f, ground + 1f), "SpawnAt with snap did not put the player on the ground");
            float yawMiss = Mathf.Abs(Mathf.DeltaAngle(player.transform.eulerAngles.y, yaw));
            Assert.LessOrEqual(yawMiss, 1f, $"Player yaw {player.transform.eulerAngles.y:F2} is {yawMiss:F2} deg from the requested {yaw}");
            Assert.AreSame(before, player.SpawnPoint, "SpawnAt changed the spawn point");

            // Unsnapped pose: 3 m above the ground, height kept (two frames of fall allowed).
            Assert.IsTrue(TerrainQuery.TryGroundHeight(pose, out float poseGround), $"No terrain tile under {pose}");
            Vector3 high = new Vector3(pose.x, poseGround + 3f, pose.z);
            yield return TestSpawn.At(high, yaw, snapToGround: false);
            Assert.That(player.transform.position.y, Is.InRange(high.y - 0.25f, high.y + 0.01f), "SpawnAt without snap did not keep the given height");
            Assert.AreSame(before, player.SpawnPoint, "SpawnAt (no snap) changed the spawn point");

            // Bad input: throws before anything moves (no TestSpawn wrapper: nothing is placed).
            Vector3 still = player.transform.position;
            Assert.Throws<System.ArgumentException>(() => player.SpawnAt(new Vector3(float.NaN, 0f, 0f), 0f), "NaN position did not throw");
            Assert.Throws<System.ArgumentException>(() => player.SpawnAt(still, float.PositiveInfinity), "Infinite yaw did not throw");
            Assert.AreEqual(still, player.transform.position, "A rejected SpawnAt moved the player");

            // AtTowerBase: the named-point path to the game's own start.
            yield return TestSpawn.AtTowerBase();
            yield return WaitGroundedUpTo(player, 3f);
            AssertLanded(player, tower.transform, TestSpawn.TowerBasePoint, new System.Collections.Generic.List<string>());
            Assert.AreSame(tower.transform, player.SpawnPoint, "AtTowerBase did not set the spawn point to TowerBaseSpawn");
        }

        static IEnumerator WaitGroundedUpTo(PlayerController player, float seconds)
        {
            float waited = 0f;
            while (!player.IsGrounded && waited < seconds)
            {
                waited += Time.deltaTime;
                yield return null;
            }
        }

        /// <summary>BB-QA-5 bar: within 1 m of the point, grounded, y on the ground, no PlayerController fallback warning.</summary>
        static void AssertLanded(PlayerController player, Transform point, string label, System.Collections.Generic.List<string> fallbackWarnings)
        {
            Vector3 pos = player.transform.position;
            float offset = Vector2.Distance(Horizontal(pos), Horizontal(point.position));
            Assert.IsTrue(TerrainQuery.TryGroundHeight(pos, out float ground), $"No terrain tile under {pos}");
            Debug.Log($"WorldWalkTests: player at {pos:F2}, {offset:F2} m from {label} {point.position:F2}, ground {ground:F2}, grounded={player.IsGrounded}");
            Assert.IsEmpty(fallbackWarnings, "PlayerController.spawnPoint is not wired in the scene: " + string.Join("; ", fallbackWarnings));
            Assert.LessOrEqual(offset, 1f, $"Player is not at {label}");
            Assert.IsTrue(player.IsGrounded, $"Player not grounded at {pos}");
            Assert.That(pos.y, Is.InRange(ground - 0.1f, ground + 1f), "Player is not on the ground");
        }

        [UnityTest]
        public IEnumerator D_FrameCountAdvances()
        {
            yield return LoadWorld();
            int startFrame = Time.frameCount;
            yield return new WaitForSecondsRealtime(1f);
            int frames = Time.frameCount - startFrame;
            Debug.Log($"WorldWalkTests: {frames} frames in 1 s realtime");
            Assert.GreaterOrEqual(frames, 10);
        }

        [UnityTest]
        public IEnumerator E_MouseDeltaRotatesPlayer()
        {
            yield return LoadWorld();
            var player = FindPlayer();
            yield return null;

            float startYaw = player.transform.eulerAngles.y;
            // Queue only: delta resets every update, so let the frame's own update apply it.
            Set(mouse.delta, new Vector2(100f, 0f), queueEventOnly: true);
            yield return null;
            yield return null;

            Assert.Greater(Mathf.Abs(Mathf.DeltaAngle(startYaw, player.transform.eulerAngles.y)), 1f);
        }
    }
}
