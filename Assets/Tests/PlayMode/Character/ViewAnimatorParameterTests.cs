using System.Collections;
using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;
using WashedAshore.Character.Stance;
using WashedAshore.Gameplay;

namespace WashedAshore.Tests.PlayMode.Character
{
    /// <summary>
    /// td, amend7: PlayerCharacterView writes every Animator parameter, triggers included, to BOTH Animators in the same Update. In the same frame (a coroutine resumed
    /// after Update and before the Animators evaluate), every parameter name and value must be equal on the shadow Animator and the view Animator: at idle, jog, crouch walk
    /// and mid-jump. Proven red once by writing one parameter on the shadow only.
    /// </summary>
    public class ViewAnimatorParameterTests : StanceFixture
    {
        PlayerCharacterView view;

        IEnumerator Begin()
        {
            yield return LoadWorld();
            yield return OnFloor();
            view = player.GetComponent<PlayerCharacterView>();
            Assert.IsNotNull(view?.Body, "no body");
            Assert.IsNotNull(view.ViewAnimator, "the view body has no Animator (CharacterViewCopy.ViewAnimator)");
        }

        /// <summary>The parameters that differ between the two Animators right now, as text; null when every parameter name and value is equal.</summary>
        string Differences()
        {
            var shadow = view.Body.Animator;
            var viewAnim = view.ViewAnimator;
            var diffs = new List<string>();
            var viewParams = new Dictionary<int, AnimatorControllerParameter>();
            foreach (var p in viewAnim.parameters) viewParams[p.nameHash] = p;
            foreach (var p in shadow.parameters)
            {
                if (!viewParams.TryGetValue(p.nameHash, out var q)) { diffs.Add($"{p.name}: missing on the view Animator"); continue; }
                string a, b;
                switch (p.type)
                {
                    case AnimatorControllerParameterType.Float: a = shadow.GetFloat(p.nameHash).ToString("F5"); b = viewAnim.GetFloat(p.nameHash).ToString("F5"); break;
                    case AnimatorControllerParameterType.Int: a = shadow.GetInteger(p.nameHash).ToString(); b = viewAnim.GetInteger(p.nameHash).ToString(); break;
                    default: a = shadow.GetBool(p.nameHash).ToString(); b = viewAnim.GetBool(p.nameHash).ToString(); break;
                }
                if (a != b) diffs.Add($"{p.name}: shadow {a}, view {b}");
            }
            foreach (var kv in viewParams)
            {
                bool found = false;
                foreach (var p in shadow.parameters) if (p.nameHash == kv.Key) { found = true; break; }
                if (!found) diffs.Add($"{kv.Value.name}: missing on the shadow Animator");
            }
            return diffs.Count == 0 ? null : string.Join("; ", diffs);
        }

        [UnityTest, Timeout(900000)]
        public IEnumerator Parameters_AreEqualOnBothAnimators_InTheSameFrame()
        {
            yield return Begin();
            var log = new StringBuilder();
            var fails = new List<string>();

            // 1. idle
            yield return new WaitForSeconds(1.0f);
            Check("idle", log, fails);

            // 2. jog: the controller walks (a forward move input at the walk speed, then sprint off: 5 m/s is the Jog node)
            Place(new Vector3(floor.transform.position.x, floor.transform.position.y + 2.05f, floor.transform.position.z - 27f), 0f);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W));
            yield return new WaitForSeconds(1.2f);
            for (int i = 0; i < 5; i++) { yield return null; Check($"jog frame {i}", log, fails); }
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            yield return new WaitForSeconds(0.8f);

            // 3. crouch walk
            yield return Go(StanceKey.Crouch, 3f);
            Place(new Vector3(floor.transform.position.x, floor.transform.position.y + 2.05f, floor.transform.position.z - 20f), 0f);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W));
            yield return new WaitForSeconds(1.2f);
            for (int i = 0; i < 5; i++) { yield return null; Check($"crouch walk frame {i}", log, fails); }
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            yield return new WaitForSeconds(0.5f);
            yield return Go(StanceKey.Crouch, 3f);   // stand again

            // 4. mid-jump (the jump trigger and the air parameters)
            yield return new WaitForSeconds(0.6f);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Space));
            yield return null;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            for (int i = 0; i < 12; i++) { yield return new WaitForSeconds(0.04f); Check($"mid-jump {i}", log, fails); }

            Debug.Log("ViewAnimatorParameterTests:\n" + log);
            Assert.IsEmpty(fails, string.Join("\n", fails));
        }

        void Check(string moment, StringBuilder log, List<string> fails)
        {
            string d = Differences();
            log.AppendLine($"{moment}: {(d == null ? "all parameters equal" : "DIFFERENT: " + d)}");
            if (d != null) fails.Add($"{moment}: {d}");
        }

        /// <summary>Explicit, expected to go RED: one parameter written on the shadow only must be reported as a difference.</summary>
        [UnityTest, Explicit("Forced-bad case: a parameter written on the shadow only must fail the equality check"), Timeout(600000)]
        public IEnumerator Parameters_ShadowOnlyWrite_MustTurnRed()
        {
            yield return Begin();
            yield return new WaitForSeconds(0.5f);
            yield return null;   // after Update: the driver has written both
            view.Body.Animator.SetFloat("MoveX", 0.77f);   // the shadow only
            string d = Differences();
            string verdict = d == null ? "EXPECTED RED: got GREEN (harness blind)" : "EXPECTED RED: got RED: " + d;
            Debug.Log("ViewAnimatorParameterTests: " + verdict);
            Assert.IsNull(d, verdict + " (this assert is meant to fail)");
        }
    }
}
