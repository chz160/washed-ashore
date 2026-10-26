using System.Collections;
using System.Globalization;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using WashedAshore.Gameplay;

namespace WashedAshore.Tests.Support
{
    /// <summary>
    /// The one way a test moves the player (studio:engineering adr/playmode-spawn-19). Outside Assets/Tests/PlayMode/Lookout/ no test writes the
    /// player's transform or switches PlayerController / CharacterController on or off except through this class.
    /// At(...) starts a test at a named point; Reposition keeps the controller's state for mid-test re-placement; Park / Parked.Move is the
    /// scripted-walk rail. TestSpawn never loads a scene: the kit loads World first (seed overrides and capture rates must be set before the load).
    /// </summary>
    public static class TestSpawn
    {
        public const string WorldScene = "World";
        public const string TowerBasePoint = "TowerBaseSpawn";
        public const string PlayerSpawnPoint = "PlayerSpawn";

        /// <summary>Largest horizontal miss between the target and where the player ended up before the placement counts as overridden.</summary>
        const float MissTolerance = 0.1f;
        const float GroundTolerance = 0.1f;

        public const string FrameTimeSpawnEnv = "WA_FRAMETIME_SPAWN";

        /// <summary>
        /// The start point the frame-time tests use: env WA_FRAMETIME_SPAWN when set ("PlayerSpawn" or "TowerBaseSpawn", anything else fails),
        /// else <paramref name="specStart"/>. The A-B-A runs in adr/playmode-spawn-19 part4 change only this value.
        /// </summary>
        public static string FrameTimeStart(string specStart)
        {
            string v = System.Environment.GetEnvironmentVariable(FrameTimeSpawnEnv);
            if (string.IsNullOrEmpty(v)) return specStart;
            if (v != PlayerSpawnPoint && v != TowerBasePoint)
                Assert.Fail($"{FrameTimeSpawnEnv} is '{v}'; it must be unset, '{PlayerSpawnPoint}' or '{TowerBasePoint}'.");
            return v;
        }

        /// <summary>The scene's player: exactly one object tagged Player and exactly one PlayerController, both active.</summary>
        public static PlayerController Player()
        {
            var tagged = GameObject.FindGameObjectsWithTag("Player");
            var controllers = Object.FindObjectsByType<PlayerController>(FindObjectsInactive.Exclude);
            if (tagged.Length != 1 || controllers.Length != 1)
                Assert.Fail($"TestSpawn.Player: expected one Player-tagged object and one PlayerController, found {tagged.Length} tagged and {controllers.Length} controllers.");
            return controllers[0];
        }

        /// <summary>Starts at the active Transform called <paramref name="pointName"/>, through PlayerController.SetSpawnPoint.</summary>
        public static IEnumerator At(string pointName)
        {
            var found = Object.FindObjectsByType<Transform>(FindObjectsInactive.Exclude).Where(t => t.name == pointName).ToArray();
            if (found.Length != 1) Assert.Fail($"TestSpawn.At(\"{pointName}\"): expected exactly one active Transform with that name, found {found.Length}.");
            Transform point = found[0];
            return Place(pointName, point.position, point.eulerAngles.y, true, p => p.SetSpawnPoint(point));
        }

        public static IEnumerator AtTowerBase() => At(TowerBasePoint);

        /// <summary>Starts at a world position and yaw (degrees), through PlayerController.SpawnAt. The spawn point is left alone.</summary>
        public static IEnumerator At(Vector3 position, float yaw, bool snapToGround = true) =>
            Place("pose", position, yaw, snapToGround, p => p.SpawnAt(position, yaw, snapToGround));

        static IEnumerator Place(string label, Vector3 target, float yaw, bool snapped, System.Action<PlayerController> place)
        {
            Assert.IsTrue(SceneManager.GetSceneByName(WorldScene).isLoaded, "TestSpawn.At: the World scene is not loaded (the kit loads it first).");
            var player = Player();
            var cc = player.GetComponent<CharacterController>();
            Assert.IsTrue(player.enabled && cc != null && cc.enabled, "TestSpawn.At: the player is parked (PlayerController or CharacterController is off). Release it first.");

            yield return null; // PlayerController.Start has run and respawned at the scene point
            place(player);
            yield return new WaitForFixedUpdate(); // one physics step: water and climb volumes see the new spot
            yield return null; // one Update: the controller's own Move runs, so IsGrounded means something

            Vector3 at = player.transform.position;
            float miss = Vector2.Distance(new Vector2(at.x, at.z), new Vector2(target.x, target.z));
            bool hasGround = TerrainQuery.TryGroundHeight(at, out float ground);
            Debug.Log($"[TestSpawn] {label} target {Fmt(target)} yaw {yaw:F2} snap {Bool(snapped)} -> player {Fmt(at)} ground {(hasGround ? ground.ToString("F2", CultureInfo.InvariantCulture) : "none")} grounded {Bool(player.IsGrounded)} frame {Time.frameCount}");
            if (miss > MissTolerance)
                Assert.Fail($"TestSpawn.At({label}): player is {miss:F2} m from the target (target {Fmt(target)}, player {Fmt(at)}); something moved it after the placement.");
            if (snapped && hasGround && at.y < ground - GroundTolerance)
                Assert.Fail($"TestSpawn.At({label}): feet at y {at.y:F2} are below the ground {ground:F2}.");
        }

        static string Fmt(Vector3 v) => string.Format(CultureInfo.InvariantCulture, "({0:F2},{1:F2},{2:F2})", v.x, v.y, v.z);
        static string Bool(bool b) => b ? "true" : "false";

        /// <summary>
        /// Mid-test re-placement: exactly what the kits did by hand (CharacterController off, set the transform, on). No state reset, no wait.
        /// For a test whose result depends on where it starts, use At instead.
        /// </summary>
        public static void Reposition(PlayerController player, Vector3 position, float? yaw = null)
        {
            var cc = player.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            if (yaw.HasValue) player.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw.Value, 0f));
            else player.transform.position = position;
            if (cc != null) cc.enabled = true;
        }

        /// <summary>Turns PlayerController and CharacterController off for a scripted walk. The only way a test switches them off.</summary>
        public static Parked Park(PlayerController player) => new Parked(player);

        public sealed class Parked
        {
            readonly PlayerController player;
            readonly CharacterController cc;

            internal Parked(PlayerController player)
            {
                this.player = player;
                cc = player.GetComponent<CharacterController>();
                player.enabled = false;
                if (cc != null) cc.enabled = false;
            }

            public PlayerController Player => player;

            /// <summary>Sets the transform for one frame of the scripted walk; fails if either controller has come back on.</summary>
            public void Move(Vector3 position, Quaternion rotation)
            {
                if (player.enabled || (cc != null && cc.enabled)) Assert.Fail("TestSpawn.Parked.Move: a controller is on while the player is parked.");
                player.transform.SetPositionAndRotation(position, rotation);
            }

            /// <summary>Turns the CharacterController and then PlayerController back on.</summary>
            public void Release()
            {
                if (cc != null) cc.enabled = true;
                player.enabled = true;
            }
        }
    }
}
