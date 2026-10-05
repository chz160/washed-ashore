using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using WashedAshore.Gameplay;

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
            Terrain terrain = Terrain.activeTerrain;
            Assert.IsNotNull(terrain, "No active Terrain");
            float ground = terrain.SampleHeight(pos) + terrain.transform.position.y;
            Assert.GreaterOrEqual(pos.y, ground - 0.1f, "Player fell through the terrain");
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
