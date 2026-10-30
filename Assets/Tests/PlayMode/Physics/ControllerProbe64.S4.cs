using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace WashedAshore.Tests.PlayMode
{
    /// <summary>
    /// #8 S4, no steering bumps. Measure (qa-8 plan): a side contact (controller hit with |normal.y| below SideNormalY) inside a row's main section, W held with the yaw
    /// 30 degrees off the flight toward the right or left wall, flights 1-8 up and down (32 rows) at the 60 Hz tick. Consecutive contact frames (gap up to 2) are one event.
    /// Window speed = flat distance over the last WindowSeconds / WindowSeconds. An event CHANGES speed when the lowest window speed that starts at or after its first frame is under
    /// s4Ratio x the window speed just before it. Pass = 0 such events in all 32 rows. Rows go to Logs/controller-probe-64-Probe64_S4-events.csv.
    /// </summary>
    public partial class ControllerProbe64
    {
        float s4Ratio = 0.8f;
        string s4EventsPath;

        string S4Contacts(ProbeRun s, ProbeResult res, List<Vector3> f, Dictionary<int, int> frameK, int kS, int kE)
        {
            int wf = Mathf.Max(1, Mathf.RoundToInt(WindowSeconds / dt));
            float VW(int k) => k >= wf && k < f.Count ? Flat(f[k] - f[k - wf]).magnitude / (wf * dt) : float.NaN;
            var frames = new SortedSet<int>(); var nameAt = new Dictionary<int, string>();
            foreach (var h in rec.hits)
            {
                if (!frameK.TryGetValue(h.frame, out int k) || Mathf.Abs(h.normal.y) >= SideNormalY || k < kS || k > kE) continue;
                frames.Add(k);
                if (!nameAt.ContainsKey(k)) nameAt[k] = h.name;
            }
            var events = new List<(int a, int b)>();
            foreach (int k in frames)
            {
                if (events.Count > 0 && k - events[events.Count - 1].b <= 2) events[events.Count - 1] = (events[events.Count - 1].a, k);
                else events.Add((k, k));
            }
            res.s4Events = events.Count; res.s4Changing = 0; res.s4Unmeasured = 0; res.s4MinRatio = float.NaN;
            string worst = "-"; int worstK = -1;
            foreach (var e in events)
            {
                float before = VW(e.a - 1);
                int last = Mathf.Min(e.b + wf, f.Count - 1);
                float after = float.MaxValue;
                for (int k = e.a + wf; k <= last; k++) { float v = VW(k); if (!float.IsNaN(v)) after = Mathf.Min(after, v); }
                if (float.IsNaN(before) || before < 0.05f || after == float.MaxValue) { res.s4Unmeasured++; continue; }
                float ratio = after / before;
                if (float.IsNaN(res.s4MinRatio) || ratio < res.s4MinRatio) { res.s4MinRatio = ratio; worst = nameAt[e.a]; worstK = e.a; }
                if (ratio < s4Ratio) res.s4Changing++;
            }
            string local = worstK >= 0 && towerRoot ? towerRoot.InverseTransformPoint(f[worstK]).ToString("F2") : "-";
            if (s4EventsPath != null)
                File.AppendAllText(s4EventsPath, $"{res.id},{s.flight},{s.dir},{s.side},{s.yawOff:F0},{frames.Count},{events.Count},{res.s4Changing},{res.s4Unmeasured},{F(res.s4MinRatio, 3)},{worst},{local.Replace(",", ";")}\n");
            return $"    S4: side-contact frames {frames.Count}, events {events.Count}, speed-changing (ratio < {s4Ratio:F2}) {res.s4Changing}, unmeasured {res.s4Unmeasured}, lowest ratio {F(res.s4MinRatio, 3)} at {worst} tower-local {local}";
        }

        IEnumerator S4Rows(string method, IEnumerable<(int flight, bool up, bool right)> rows, bool variants = false)
        {
            yield return Begin(method, 1f / 60f);
            s4EventsPath = LogDir + "controller-probe-64-" + method + "-events.csv";
            File.WriteAllText(s4EventsPath, "row_id,flight,dir,side,yaw_off,contact_frames,events,changing,unmeasured,min_ratio,worst_collider,worst_tower_local\n");
            try
            {
                yield return TowerSetup();
                WriteHeader(method);
                Emit($"S4 ratio {s4Ratio:F2}; rows: {rows.Count()}");
                var refs = new Dictionary<string, ProbeResult>();
                foreach (var r in rows) yield return Go(TowerRun(r.flight, r.up, 30f, r.right), refs);
                if (variants)
                    foreach (var v in new[] { (0.60f, 0f), (0.90f, 0.07f) })
                    {
                        // VARIANT start, not the spec row: F8 up on the left wall, whose spec start (along 0.60, lateral 0.07) is "start off line". Listed beside the 32 rows, never in place of row 30.
                        var s = TowerRun(8, true, 30f, false, v.Item1, v.Item2);
                        s.extraFlag = "VARIANT"; s.note = $"VARIANT start, not the spec row: along {v.Item1:F2} lateral {v.Item2:F2}";
                        Emit($"    {s.note}");
                        yield return Go(s, refs);
                    }
                Summary(method, false);
            }
            finally { End(); s4EventsPath = null; }
        }

        void S4Verdict(string method)
        {
            // A stuck row (never reached the end of its section) counts as one speed-changing result: a stopped player has no speed to compare.
            var variantRows = results.Where(r => r.run.extraFlag == "VARIANT").ToList();
            var all = results.Where(r => r.run.extraFlag != "VARIANT").ToList();   // the spec rows; the variants are listed below and never replace an INVALID spec row
            int events = all.Sum(r => r.s4Events), stuck = all.Count(r => r.stuck), unmeasured = all.Sum(r => r.s4Unmeasured);
            int changing = all.Sum(r => r.s4Changing) + stuck;
            string Id(ProbeResult r) => $"row {r.id} F{r.run.flight} {r.run.dir} {r.run.side}";
            var bad = all.Where(r => r.s4Changing > 0 || r.stuck).Select(r => $"{Id(r)} ({r.s4Changing}{(r.stuck ? " + STUCK" : "")})");
            var unmeas = all.Where(r => r.s4Unmeasured > 0).Select(r => $"{Id(r)} ({r.s4Unmeasured})");
            var stopped = all.Where(r => r.stops > 0).Select(r => $"{Id(r)} ({r.stops})");
            string line = $"S4 {method}: rows {all.Count} (invalid: {all.Count(r => r.invalid != null)}), contact events {events}, speed-changing {changing} (incl. {stuck} stuck rows), unmeasured events {unmeasured}; rows with a speed-changing result: {string.Join("; ", bad)}; rows with unmeasured events: {string.Join("; ", unmeas)}; rows with stops (v < 0.5 cap): {string.Join("; ", stopped)}";
            Emit(line);
            foreach (var v in variantRows) Emit($"S4 VARIANT start, not the spec row ({v.run.note}): {(v.invalid != null ? "INVALID " + v.invalid : $"events {v.s4Events}, speed-changing {v.s4Changing}, stuck {v.stuck}, lowest ratio {F(v.s4MinRatio, 3)}")}");
            Assert.IsEmpty(validity, $"{method}: PROBE VALID: no: {string.Join("; ", validity)} (see {logPath})");
            Assert.AreEqual(0, changing, "speed-changing contact(s): " + line);
        }

        static IEnumerable<(int, bool, bool)> S4AllRows() =>
            Enumerable.Range(1, 8).SelectMany(fl => new[] { true, false }.SelectMany(up => new[] { true, false }.Select(right => (fl, up, right))));

        [UnityTest, Explicit("#8 S4: 32 rows, may fail on main (#86 residuals)"), Category("ControllerProbe64"), Timeout(3600000)]
        public IEnumerator Probe64_S4()
        {
            s4Ratio = 0.8f;   // one fixture instance serves every test: the known-fail's bar must not leak
            yield return S4Rows("Probe64_S4", S4AllRows(), false);
            S4Verdict("Probe64_S4");
        }

        /// <summary>Known fail: the ratio bar is 1.00 (any slowdown after a contact counts) on flight 1 up, right wall. Must fail with "speed-changing contact(s)" on a row that is moving.</summary>
        [UnityTest, Explicit("known-fail: ratio bar 1.00"), Category("KnownFail"), Timeout(3600000)]
        public IEnumerator KF_Probe64_S4_BarAtOne()
        {
            s4Ratio = 1.0f;
            yield return S4Rows("Probe64_S4_KF", new[] { (1, true, true), (4, true, true) });
            S4Verdict("Probe64_S4_KF");
        }
    }
}
