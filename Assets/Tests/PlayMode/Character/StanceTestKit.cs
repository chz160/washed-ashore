using System.Collections;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using WashedAshore.Gameplay;
using WashedAshore.Tests.Support;

namespace WashedAshore.Tests.PlayMode.Character
{
    /// <summary>Shared setup for the stance PlayMode cases: the World scene, the Player lifted onto a thick floor high above the map.</summary>
    public abstract class StanceFixture : InputTestFixture
    {
        protected PlayerController player;
        protected CharacterController body;
        protected Keyboard keyboard;
        protected GameObject floor;
        protected Transform pivot;
        protected float standingEye;
        protected const float Eps = 0.011f;

        /// <summary>A file of record under Data/character/clips.</summary>
        protected static string ClipDataText(string file) => File.ReadAllText(Path.Combine(Path.GetDirectoryName(Application.dataPath), "Data", "character", "clips", file));

        static float Num(Match m, int i) => float.Parse(m.Groups[i].Value, CultureInfo.InvariantCulture);

        /// <summary>
        /// The Survivor's eye point in the Head bone's local frame: eye-point-Survivor.json "eye_in_head_bone_local" (x, up, forward; metres in the authoring frame, ta-49,
        /// survivor_eye.py) divided by 100 for the head bone's import scale, the same convention CharacterAssemblyTests.EyeLocal uses. One source for every hidden-head
        /// eye match (pod 49 R4-R6, td-49 final rulings); the Farmer's 0.1094 / 0.1011 constants are gone.
        /// </summary>
        protected static Vector3 SurvivorEyeLocal()
        {
            var m = Regex.Match(ClipDataText("eye-point-Survivor.json"), "\"Survivor_Head.fbx\"\\s*:\\s*\\{\\s*\"eye_in_head_bone_local\"\\s*:\\s*\\[\\s*([-0-9.eE]+),\\s*([-0-9.eE]+),\\s*([-0-9.eE]+)");
            Assert.IsTrue(m.Success, "eye-point-Survivor.json has no Survivor_Head.fbx eye_in_head_bone_local");
            return new Vector3(Num(m, 1), Num(m, 2), Num(m, 3)) / 100f;
        }

        /// <summary>
        /// The planted-foot drift (m, in the clip's unscaled frame) of a pack clip in measured-pack-Survivor.json, the measurement of the shipped Pack_Retargeted.fbx
        /// (ta-49, measure_clips.py). The clip names in that file carry the Blender ".001" re-import suffix.
        /// </summary>
        protected static float PackPlantDrift(string clip)
        {
            var m = Regex.Match(ClipDataText("measured-pack-Survivor.json"), "\"" + Regex.Escape(clip) + "(?:\\.\\d{3})?\"\\s*:\\s*\\{[^{}]*?\"plant_drift_m_max\"\\s*:\\s*([-0-9.eE]+)");
            Assert.IsTrue(m.Success, "measured-pack-Survivor.json has no plant_drift_m_max for " + clip);
            return Num(m, 1);
        }

        public override void Setup()
        {
            base.Setup();
            keyboard = InputSystem.AddDevice<Keyboard>();
            InputSystem.AddDevice<Mouse>();
        }

        protected IEnumerator LoadWorld()
        {
            SceneManager.LoadScene("World", LoadSceneMode.Single);
            yield return null;
            yield return null;
            var go = GameObject.FindGameObjectWithTag("Player");
            Assert.IsNotNull(go, "no Player");
            player = go.GetComponent<PlayerController>();
            body = go.GetComponent<CharacterController>();
            pivot = player.CameraPivot;
            standingEye = player.AuthoredEyeHeight;
            tickPin?.Track(player);
        }

        /// <summary>A 60 x 60 m, 4 m thick floor with its top 100 m above the player's feet, and the player standing on it.</summary>
        protected IEnumerator OnFloor()
        {
            Vector3 p = player.transform.position;
            floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "StanceTestFloor";
            floor.transform.localScale = new Vector3(60f, 4f, 60f);
            floor.transform.position = new Vector3(p.x, p.y + 100f - 2f, p.z);
            Place(new Vector3(p.x, p.y + 100.05f, p.z), 0f);
            yield return Settle();
#if UNITY_EDITOR
            // PC_RIG=women runs any case on the women's rig (Women_Worker): the Player's bodies are rebuilt once the floor is under it. Unset (the default) is the scene's Men_Farmer.
            if (System.Environment.GetEnvironmentVariable("PC_RIG") == "women")
            {
                var v = player.GetComponent<WashedAshore.Character.Stance.PlayerCharacterView>();
                var app = UnityEditor.AssetDatabase.LoadAssetAtPath<WashedAshore.Character.Appearance.CharacterAppearance>("Assets/Art/Character/Appearances/Women_Worker.asset");
                Assert.IsNotNull(app, "PC_RIG=women: no Women_Worker appearance");
                if (v.Body.Appearance.bodyType != WashedAshore.Character.Appearance.BodyType.Women) v.RebuildWith(app);
                yield return null;
                yield return null;
                Assert.AreEqual(WashedAshore.Character.Appearance.BodyType.Women, v.Body.Appearance.bodyType, "PC_RIG=women: not the women's body");
                yield return Settle();
            }
#endif
        }

        protected void Place(Vector3 at, float yaw)
        {
            WashedAshore.Tests.Support.TestSpawn.Reposition(player, at, yaw);
        }

        protected IEnumerator Settle()
        {
            for (int i = 0; i < 20; i++) yield return null;
            Assert.IsTrue(player.IsGrounded, "player is not on the floor");
        }

        protected float Eye => pivot.position.y - player.transform.position.y;
        protected float CapTop => body.height + (body.center.y - body.height * 0.5f);

        /// <summary>Presses a stance key and runs frames until the change has finished (or the limit passes).</summary>
        protected IEnumerator Go(StanceKey key, float limit = 3f)
        {
            Physics.SyncTransforms();
            player.PressStanceKey(key);
            yield return null;
            yield return Finish(limit);
        }

        protected IEnumerator Finish(float limit = 3f)
        {
            float t = 0f;
            while (player.StanceTransitioning && t < limit) { t += Time.deltaTime; yield return null; }
            yield return null;
        }

        protected IEnumerator Frames(int n) { Physics.SyncTransforms(); for (int i = 0; i < n; i++) yield return null; }

        TickRatePin tickPin;

        /// <summary>
        /// Pins Time.captureDeltaTime to the player's tick step for this test (one tick per frame; td-46 adr/46-b-test-pin-pattern). Call it first in
        /// a test; LoadWorld then tracks the player, and TearDown restores the capture time and asserts one tick per frame and no dropped ticks.
        /// </summary>
        protected void PinTicks()
        {
            if (tickPin != null) return;
            tickPin = new TickRatePin();
            tickPin.Begin();
        }

        [TearDown]
        public void RemoveFloor()
        {
            if (floor != null) Object.Destroy(floor);
            if (tickPin != null) { var p = tickPin; tickPin = null; p.End(); }
        }
    }
}
