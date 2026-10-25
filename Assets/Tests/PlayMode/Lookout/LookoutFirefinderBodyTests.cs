using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using WashedAshore.Lookout;

namespace WashedAshore.Tests.PlayMode.Lookout
{
    /// <summary>
    /// #40 director amend2 Q4, td part 12: from entering the firefinder to the end of the blend out, neither the view body nor the shadow body shows
    /// (no body in frame, no stray shadow of the player), and on leaving every renderer under the player has exactly its enabled flag and shadow mode from before.
    /// </summary>
    public class LookoutFirefinderBodyTests : LookoutFixture
    {
        [UnityTest]
        public IEnumerator Q4_TheBodyAndItsShadow_AreOffDuringTheView_AndRestoredExactly()
        {
            yield return ReachCab();
            var before = player.GetComponentsInChildren<Renderer>(true).Select(r => (r, r.enabled, r.shadowCastingMode)).ToList();
            Assert.IsNotEmpty(before, "the player has no body renderers to test");
            Assert.IsTrue(before.Any(b => b.enabled || b.shadowCastingMode != ShadowCastingMode.Off), "the body shows or casts a shadow before the view");
            watch.ExpectAuthoredCamera = false;
            yield return LookAt(firefinder.Alidade.position);
            yield return null;
            Assert.AreSame(firefinder, interactor.Target, $"aiming at the firefinder (prompt '{interactor.PromptLine}')");
            yield return Tap(Key.E);
            Assert.IsTrue(firefinder.InUse, "in use after the press");
            int bad = 0; string first = null;
            void Check(string phase)
            {
                foreach (var r in player.GetComponentsInChildren<Renderer>(true))
                    if (r.enabled || r.shadowCastingMode != ShadowCastingMode.Off) { bad++; first ??= $"{phase}: {r.name} enabled={r.enabled} shadows={r.shadowCastingMode}"; }
            }
            Check("entering");
            yield return Until(() => firefinder.CurrentPhase == Firefinder.Phase.InUse, LookoutTargets.FirefinderEnterSeconds, "reach the sight");
            Check("in use");
            yield return Wait(0.5f);
            Check("in use later");
            yield return Tap(Key.Escape);
            for (float t = 0f; firefinder.InUse && t < 5f; t += Time.deltaTime) { yield return null; if (firefinder.InUse) Check("leaving"); }
            Assert.IsFalse(firefinder.InUse, "the view ended");
            Assert.AreEqual(0, bad, "a body renderer showed or cast a shadow during the view: " + first);
            var changed = before.Where(b => b.r == null || b.r.enabled != b.enabled || b.r.shadowCastingMode != b.shadowCastingMode).Select(b => b.r != null ? b.r.name : "(destroyed)").ToList();
            Assert.IsEmpty(changed, "enabled flag and shadow mode restored exactly after the view: " + string.Join(", ", changed));
        }
    }
}
