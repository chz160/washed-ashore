using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace WashedAshore.Tests.PlayMode
{
    /// <summary>
    /// spec-64 s5d (td part22): all 116 T1 + REPEAT rows of Probe64_Tower with the ramp stepOffset forced from 0.05 to 0.25 every frame by a probe component (order -50).
    /// No game file changes. Supplementary: fails only when PROBE VALID is no (a NONDETERMINISTIC repeat is flagged and printed, not a failure).
    /// </summary>
    public partial class ControllerProbe64
    {
        [UnityTest, Explicit("#64 controller probe: fails on main by design"), Category("ControllerProbe64"), Timeout(3600000)]
        public IEnumerator Probe64_TowerStep()
        {
            yield return Begin("Probe64_TowerStep", 1f / 60f);
            ProbeStepForcer forcer = null;
            try
            {
                yield return TowerSetup();
                WriteHeader("Probe64_TowerStep");
                forcer = player.gameObject.AddComponent<ProbeStepForcer>(); forcer.body = cc;
                Emit($"step forcer on: ramp stepOffset 0.05 -> 0.25 every frame while slimmed (order -50); step now {cc.stepOffset:F3}");
                var refs = new Dictionary<string, ProbeResult>();
                var byKey = new Dictionary<string, ProbeResult>();
                for (int fl = 1; fl <= 8; fl++)
                    foreach (bool up in new[] { true, false })
                        foreach (var y in TowerYaws)
                        {
                            var s = TowerRun(fl, up, y.th, y.right);
                            s.variant = "step0.25";
                            yield return Go(s, refs);
                            var r = results.Last(); byKey[$"{fl}{up}{y.th}{y.right}"] = r;
                            Emit($"    step at row start {r.stepAtStart:F3}; frames changed so far {forcer.framesChanged}; Probe64_Tower row: {TowerRowSummary(r)}");
                        }
                foreach (var (fl, th) in new[] { (1, 10f), (1, 20f), (4, 10f), (4, 20f) })
                {
                    var s = TowerRun(fl, true, th, true); s.caseName = "REPEAT"; s.logContacts = false; s.extraFlag = "REPEAT"; s.refKey = ""; s.isRef = false; s.variant = "step0.25";
                    yield return Go(s, refs);
                    var again = results.Last(); var first = byKey[$"{fl}True{th}True"];
                    bool same = again.invalid == null && first.invalid == null && (again.sectionEndFeet - first.sectionEndFeet).magnitude <= 0.001f && again.stops == first.stops && again.endStops == first.endStops && again.stuck == first.stuck;
                    Emit($"    REPEAT F{fl} up {th:F0}R vs row {first.id}: section-end feet differ {(again.sectionEndFeet - first.sectionEndFeet).magnitude * 1000f:F3} mm, stops {again.stops}/{first.stops}, end stops {again.endStops}/{first.endStops} -> {(same ? "same" : "NONDETERMINISTIC")}; step at row start {again.stepAtStart:F3}");
                    if (!same) again.flags.Add("NONDETERMINISTIC");
                }
                Emit($"step forcer changed the step on {forcer.framesChanged} frames in total");
                Summary("Probe64_TowerStep", true);
            }
            finally
            {
                if (forcer) Object.Destroy(forcer);
                End();
            }
            Verdict("Probe64_TowerStep");
        }
    }
}
