using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;
using UnityEngine.UI;
using WashedAshore.Interaction;
using WashedAshore.World;

namespace WashedAshore.Tests.PlayMode.Lookout
{
    /// <summary>
    /// Spec I1 (tower-design-spec.md §3, T-I1a, T-I1c, T-I1d; tower-qa-plan.md I1): the interactor against stub
    /// interactables with nothing of the tower involved, so the system is shown to be reusable. Prompt range,
    /// occlusion, E and gamepad South, the shared South press, and the prompt as the only UI.
    /// </summary>
    public class LookoutInteractionTests : LookoutFixture
    {
        const string StubVerb = "Open", StubNoun = "crate";
        const string KeyboardKey = "E";    // spec §3.2: the keyboard key text
        const float StubSize = 0.5f;

        class Stub : MonoBehaviour, IInteractable
        {
            public bool Usable = true;
            public int Used;
            public string Verb => StubVerb;
            public string Noun => StubNoun;
            public bool CanInteract(Interactor by) => Usable;
            public void Interact(Interactor by) => Used++;
        }

        Gamepad gamepad;
        Stub stub;

        public override void Setup()
        {
            base.Setup();
            gamepad = InputSystem.AddDevice<Gamepad>();
        }

        /// <summary>A stub box whose near face is <paramref name="distance"/> m from the eye, straight ahead.</summary>
        Stub StubAt(float distance, int layer = 0)
        {
            var pivot = player.CameraPivot;
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.Destroy(go.GetComponent<Renderer>());
            go.name = "InteractionStub";
            go.layer = layer;
            go.transform.localScale = Vector3.one * StubSize;
            go.transform.SetPositionAndRotation(pivot.position + pivot.forward * (distance + StubSize * 0.5f), pivot.rotation);
            Physics.SyncTransforms();
            return go.AddComponent<Stub>();
        }

        GameObject WallAt(float distance, int layer)
        {
            var pivot = player.CameraPivot;
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.Destroy(go.GetComponent<Renderer>());
            go.name = "InteractionWall";
            go.layer = layer;
            go.transform.localScale = new Vector3(2f, 2f, 0.05f);
            go.transform.SetPositionAndRotation(pivot.position + pivot.forward * distance, pivot.rotation);
            Physics.SyncTransforms();
            return go;
        }

        IEnumerator Ready()
        {
            yield return LoadTower();
            yield return WaitGrounded(5f);
            // Level gaze, so the stub in front of the eye is clear of the ground.
            yield return Face(player.transform.forward);
        }

        static int EnabledGraphics()
        {
            int n = 0;
            foreach (var g in Object.FindObjectsByType<Graphic>(FindObjectsInactive.Exclude))
                if (g.isActiveAndEnabled && g.canvas != null && g.canvas.isActiveAndEnabled) n++;
            return n;
        }

        string Expected(string key) => $"{key}{interactor.Settings.separator}{StubVerb} {StubNoun}";

        [UnityTest]
        public IEnumerator A_PromptShowsInsideTheRange_AndNotBeyondIt()
        {
            yield return Ready();
            Assert.AreEqual(0, EnabledGraphics(), "no UI at all with nothing aimed at (N11)");
            Assert.IsNull(interactor.PromptLine);

            stub = StubAt(LookoutTargets.PromptShownAt);
            yield return null;
            yield return null;
            Assert.AreSame(stub, interactor.Target, $"target at {LookoutTargets.PromptShownAt} m");
            Assert.AreEqual(Expected(KeyboardKey), interactor.PromptLine, "the one prompt line, keyboard");
            Assert.AreEqual(1, EnabledGraphics(), "exactly one enabled Graphic while aiming");
            var texts = Object.FindObjectsByType<Text>(FindObjectsInactive.Include);
            Assert.AreEqual(1, texts.Length, "exactly one UI text element exists in the scene");
            Assert.AreEqual(interactor.PromptLine, texts[0].text, "the Text shows the prompt line");

            Object.Destroy(stub.gameObject);
            yield return null;
            stub = StubAt(LookoutTargets.PromptHiddenAt);
            yield return null;
            yield return null;
            Assert.IsNull(interactor.Target, $"no target at {LookoutTargets.PromptHiddenAt} m");
            Assert.IsNull(interactor.PromptLine);
            Assert.AreEqual(0, EnabledGraphics(), "prompt hidden beyond the range");
        }

        [UnityTest]
        public IEnumerator B_SolidsOcclude_WorldBoundsAndWaterDoNot_AndUnusableShowsNothing()
        {
            yield return Ready();
            stub = StubAt(LookoutTargets.PromptShownAt);
            var wall = WallAt(LookoutTargets.PromptShownAt * 0.5f, 0);
            yield return null;
            yield return null;
            Assert.IsNull(interactor.Target, "a solid wall in between occludes");
            Assert.IsNull(interactor.PromptLine);

            wall.layer = WorldLayers.WorldBounds;
            yield return null;
            yield return null;
            Assert.AreSame(stub, interactor.Target, "an invisible WorldBounds wall in between does not occlude");
            wall.layer = WorldLayers.Water;
            yield return null;
            yield return null;
            Assert.AreSame(stub, interactor.Target, "Water in between does not occlude");
            Object.Destroy(wall);

            stub.Usable = false;
            yield return null;
            yield return null;
            Assert.IsNull(interactor.Target, "CanInteract false: no target");
            Assert.IsNull(interactor.PromptLine, "CanInteract false: no prompt");
            Assert.AreSame(stub, interactor.AimedAt, "still what the ray hits");
            int refused = interactor.RefusedPresses;
            yield return Tap(Key.E);
            Assert.AreEqual(0, stub.Used, "E ignored while it can't be used");
            Assert.AreEqual(refused + 1, interactor.RefusedPresses, "the refused press is counted");
        }

        [UnityTest]
        public IEnumerator C_EAndGamepadSouthBothInteract_AndTheKeyTextFollowsTheDevice()
        {
            yield return Ready();
            stub = StubAt(LookoutTargets.PromptShownAt);
            yield return null;
            yield return Tap(Key.E);
            Assert.AreEqual(1, stub.Used, "E interacts");

            float y = player.transform.position.y;
            Press(gamepad.buttonSouth);
            yield return null;
            Release(gamepad.buttonSouth);
            float peak = y;
            for (float t = 0f; t < 0.5f; t += Time.deltaTime) { peak = Mathf.Max(peak, player.transform.position.y); yield return null; }
            Assert.AreEqual(2, stub.Used, "gamepad South interacts");
            Assert.Less(peak - y, capsule.stepOffset * 0.25f, "South with a prompt showing does not jump (§3.3)");
            Debug.Log($"LookoutInteractionTests: prompt after a gamepad press: '{interactor.PromptLine}'");
            StringAssert.EndsWith($"{StubVerb} {StubNoun}", interactor.PromptLine);
            StringAssert.DoesNotStartWith(KeyboardKey + interactor.Settings.separator, interactor.PromptLine, "the key text follows the gamepad");
        }

        [UnityTest]
        public IEnumerator D_GamepadSouthWithNoPrompt_JumpsAsBefore_AndSpaceAlwaysJumps()
        {
            yield return Ready();
            Assert.IsNull(interactor.PromptLine);
            float y = player.transform.position.y;
            Press(gamepad.buttonSouth);
            yield return null;
            Release(gamepad.buttonSouth);
            float peak = y;
            for (float t = 0f; t < 1.5f; t += Time.deltaTime) { peak = Mathf.Max(peak, player.transform.position.y); yield return null; }
            Assert.Greater(peak - y, player.JumpHeight * 0.5f, "South with no prompt jumps");

            yield return WaitGrounded(3f);
            stub = StubAt(LookoutTargets.PromptShownAt);
            yield return null;
            yield return null;
            Assert.IsNotNull(interactor.PromptLine, "prompt showing");
            y = player.transform.position.y;
            Press(keyboard.spaceKey);
            yield return null;
            Release(keyboard.spaceKey);
            peak = y;
            for (float t = 0f; t < 1.5f; t += Time.deltaTime) { peak = Mathf.Max(peak, player.transform.position.y); yield return null; }
            Assert.Greater(peak - y, player.JumpHeight * 0.5f, "Space jumps even with a prompt showing");
            Assert.AreEqual(0, stub.Used, "Space does not interact");
        }
    }
}
