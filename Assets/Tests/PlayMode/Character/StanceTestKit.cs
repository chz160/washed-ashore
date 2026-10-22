using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using WashedAshore.Gameplay;

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
            body.enabled = false;
            player.transform.SetPositionAndRotation(at, Quaternion.Euler(0f, yaw, 0f));
            body.enabled = true;
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

        [TearDown]
        public void RemoveFloor()
        {
            if (floor != null) Object.Destroy(floor);
        }
    }
}
