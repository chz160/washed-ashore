using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using WashedAshore.Gameplay;
using WashedAshore.Lookout;
using WashedAshore.Tests.Support;

namespace WashedAshore.Tests.PlayMode
{
    /// <summary>T1 (the #41 ramp-plus-rail crease on the real tower flights), the REPEAT rows and the dt sweep (spec-64 s5, td M5, M6).</summary>
    public partial class ControllerProbe64
    {
        // Lane lines in world metres, spec-64 s2 (manifest values + tower origin (548, 90.9, -230)).
        static readonly float[][] LaneTable =
        {
            new[] { 546.655f, 90.951f, -230.734f, 548.898f, 95.066f, -228.491f },
            new[] { 549.509f, 95.066f, -229.102f, 547.266f, 99.180f, -231.345f },
            new[] { 546.655f, 99.180f, -230.734f, 548.898f, 103.295f, -228.491f },
            new[] { 549.509f, 103.295f, -229.102f, 547.390f, 107.181f, -231.220f },
            new[] { 546.780f, 107.181f, -230.610f, 548.524f, 110.382f, -228.865f },
            new[] { 549.135f, 110.382f, -229.476f, 547.640f, 113.125f, -230.971f },
            new[] { 547.029f, 113.125f, -230.360f, 548.150f, 115.182f, -229.239f },
            new[] { 548.710f, 115.182f, -229.447f, 548.624f, 117.240f, -230.816f },
        };

        class FlightInfo { public Vector3 foot, head; public float faceRunPlus, faceRunMinus; public List<Collider> walls = new List<Collider>(); }
        readonly List<FlightInfo> flights = new List<FlightInfo>();

        IEnumerator TowerSetup()
        {
            Assert.IsNotNull(towerRoot, "LookoutTower is not in World.unity");
            var tf = towerRoot.GetComponentsInChildren<Transform>(true).GroupBy(t => t.name).ToDictionary(g => g.Key, g => g.First(), System.StringComparer.Ordinal);
            var hatch = towerRoot.GetComponentInChildren<TrapDoor>(true);
            Assert.IsNotNull(hatch, "the tower has no TrapDoor");
            hatch.SetState(HingeState.Open, 0f);
            Assert.AreEqual(HingeState.Open, hatch.State, "hatch open");

            yield return TestSpawn.AtTowerBase();   // primes the slim capsule (the full capsule is never placed in a flight corridor)
            for (int i = 0; i < 2; i++) yield return null;
            bool slim = Mathf.Abs(cc.radius - 0.25f) < 1e-3f && Mathf.Abs(cc.height - 1.6f) < 1e-3f && (Mathf.Abs(cc.stepOffset - 0.25f) < 1e-3f || Mathf.Abs(cc.stepOffset - 0.05f) < 1e-3f);
            if (!slim) validity.Add($"slim capsule not primed (r {cc.radius}, h {cc.height}, step {cc.stepOffset})");

            flights.Clear();
            for (int i = 1; i <= 8; i++)
            {
                var fi = new FlightInfo();
                if (!tf.TryGetValue($"LANE_F{i:00}_Foot", out var ft) || !tf.TryGetValue($"LANE_F{i:00}_Head", out var hd)) { validity.Add($"no lane markers for F{i}"); flights.Add(fi); continue; }
                fi.foot = ft.position; fi.head = hd.position; flights.Add(fi);
                var t = LaneTable[i - 1];
                if ((fi.foot - new Vector3(t[0], t[1], t[2])).magnitude > 0.01f || (fi.head - new Vector3(t[3], t[4], t[5])).magnitude > 0.01f)
                    validity.Add($"F{i} lane markers differ from the spec table by more than 0.01 m (foot {fi.foot:F3}, head {fi.head:F3})");
                Vector3 run = Flat(fi.head - fi.foot), runDir = run.normalized, right = new Vector3(runDir.z, 0, -runDir.x);
                float rise = fi.head.y - fi.foot.y, L = run.magnitude;
                Vector3 centre = fi.foot + runDir * (L * 0.5f); centre.y = fi.foot.y + rise * 0.5f + 1f;
                float plus = float.NaN, minus = float.NaN;
                foreach (string suffix in new[] { "_01", "_02" })
                {
                    if (!tf.TryGetValue($"CLIP_CVX_Flight{i:00}{suffix}", out var wall) || !wall.TryGetComponent<Collider>(out var col)) { validity.Add($"F{i} wall {suffix} not found"); continue; }
                    fi.walls.Add(col);
                    float lat = Vector3.Dot(col.ClosestPoint(centre) - centre, right);
                    if (lat > 0) plus = lat; else minus = -lat;
                }
                fi.faceRunPlus = plus; fi.faceRunMinus = minus;
                Emit($"F{i}: L {L:F3} rise {rise:F3} wall faces +{plus:F3} / -{minus:F3} (expect 0.38-0.43)");
                // qa-64: where the lane line (foot-head) sits between the walls, at three points along the flight
                var offs = new List<string>();
                foreach (float at in new[] { 0.35f, L * 0.5f, L - 0.30f })
                {
                    Vector3 pt = fi.foot + runDir * at; pt.y = fi.foot.y + rise * at / L + 1f;
                    float pl = float.NaN, mi = float.NaN;
                    foreach (var wc in fi.walls) { float lt = Vector3.Dot(wc.ClosestPoint(pt) - pt, right); if (lt > 0) pl = lt; else mi = -lt; }
                    offs.Add($"a {at:F2}: +{pl:F3}/-{mi:F3} midline offset {(pl - mi) * 0.5f:F3}");
                }
                Emit($"    lane line vs wall midline (lateral, + = right of the run): {string.Join("; ", offs)}");
                if (!(plus > 0.38f && plus < 0.43f) || !(minus > 0.38f && minus < 0.43f)) validity.Add($"F{i} wall faces {plus:F3}/{minus:F3} outside 0.38-0.43");
            }
            Emit($"hatch {hatch.State}; tower origin {towerRoot.position:F3}");
        }

        ProbeRun TowerRun(int flight, bool up, float th, bool right, float a0Over = -1f, float lat0Over = -1f)
        {
            var fi = flights[flight - 1];
            Vector3 run = Flat(fi.head - fi.foot), runDir = run.normalized; float L = run.magnitude, rise = fi.head.y - fi.foot.y;
            Vector3 d = up ? runDir : -runDir, rd = new Vector3(d.z, 0, -d.x), toWall = right ? rd : -rd;
            Vector3 runRight = new Vector3(runDir.z, 0, -runDir.x);
            float face = Vector3.Dot(toWall, runRight) > 0 ? fi.faceRunPlus : fi.faceRunMinus;
            // spec r4: F8 up starts at along 0.60 with lateral 0.07 (the F7 wall hull intrudes near the F8 foot); F8 down 0.45, all others 0.35 / 0.05
            bool f8up = up && flight == 8;
            float a0 = f8up ? 0.60f : !up && flight == 8 ? 0.45f : 0.35f, lat0 = f8up ? 0.07f : 0.05f;
            if (a0Over >= 0f) a0 = a0Over;   // #8 S4 variant rows only (VARIANT start, not the spec row)
            if (lat0Over >= 0f) lat0 = lat0Over;
            Vector3 refPt = up ? fi.foot : fi.head;
            Vector3 pos = refPt + d * a0 + (th > 0f ? toWall * lat0 : Vector3.zero);
            pos.y = (up ? fi.foot.y + a0 * rise / L : fi.head.y - a0 * rise / L) + 0.05f;
            float pathYaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
            var s = new ProbeRun
            {
                caseName = "T1", flight = flight.ToString(), dir = up ? "up" : "down", side = th > 0f ? (right ? "R" : "L") : "-", yawOff = th,
                cap = 1.15f, r = 0.25f, expectedWalkCap = 1.15f, start = pos, yaw = pathYaw + (right ? th : -th), d = d, refPt = refPt, toWall = toWall,
                aStart = a0, aEnd = L - 0.3f, runEnd = up && flight == 8 ? L - 0.1f : L + 0.1f, idealPath = Cos(th), useAt = false, tower = true,
                refKey = $"T1-F{flight}-{(up ? "up" : "down")}-{dt:F4}", isRef = th == 0f, logContacts = th == 30f || (up && (th == 10f || th == 20f || (th == 0f && flight >= 4)))   // td part10: also the theta 0 up rows of F4-F8 (what is straight ahead at the head)
            };
            s.wallCols = fi.walls;
            s.surfRef = up ? fi.foot.y : fi.head.y; s.surfFar = up ? fi.head.y : fi.foot.y; s.spanL = L;
            s.lateralThrough = true;   // td addendum part4: T1 gets the lateral test as well as ComputePenetration
            // both flight walls on every row (td C8): the target wall and the far wall
            // the lateral line only means something while the feet are alongside the wall: along 0..L from the reference point (part10 P1)
            s.walls.Add(new ProbeWall { dir = toWall, face = face, aMin = 0f, aMax = L });
            s.walls.Add(new ProbeWall { dir = -toWall, face = Vector3.Dot(toWall, runRight) > 0 ? fi.faceRunMinus : fi.faceRunPlus, aMin = 0f, aMax = L });
            return s;
        }

        static readonly (float th, bool right)[] TowerYaws = { (0f, true), (10f, true), (20f, true), (30f, true), (10f, false), (20f, false), (30f, false) };

        [UnityTest, Explicit("#64 controller probe: fails on main by design"), Category("ControllerProbe64"), Timeout(3600000)]
        public IEnumerator Probe64_Tower()
        {
            yield return Begin("Probe64_Tower", 1f / 60f);
            try
            {
                yield return TowerSetup();
                WriteHeader("Probe64_Tower");
                var refs = new Dictionary<string, ProbeResult>();
                var byKey = new Dictionary<string, ProbeResult>();
                for (int fl = 1; fl <= 8; fl++)
                    foreach (bool up in new[] { true, false })
                        foreach (var y in TowerYaws)
                        {
                            var s = TowerRun(fl, up, y.th, y.right);
                            yield return Go(s, refs);
                            byKey[$"{fl}{up}{y.th}{y.right}"] = results.Last();
                        }
                // M6: four repeat rows must match the originals within 1 mm with the same stops.
                foreach (var (fl, th) in new[] { (1, 10f), (1, 20f), (4, 10f), (4, 20f) })
                {
                    var s = TowerRun(fl, true, th, true); s.caseName = "REPEAT"; s.logContacts = false; s.extraFlag = "REPEAT"; s.refKey = ""; s.isRef = false;
                    yield return Go(s, refs);
                    var again = results.Last(); var first = byKey[$"{fl}True{th}True"];
                    bool same = again.invalid == null && first.invalid == null && (again.sectionEndFeet - first.sectionEndFeet).magnitude <= 0.001f && again.stops == first.stops && again.endStops == first.endStops && again.stuck == first.stuck;
                    Emit($"    REPEAT F{fl} up {th:F0}R vs row {first.id}: section-end feet differ {(again.sectionEndFeet - first.sectionEndFeet).magnitude * 1000f:F3} mm, stops {again.stops}/{first.stops}, end stops {again.endStops}/{first.endStops} -> {(same ? "same" : "NONDETERMINISTIC")}");
                    if (!same) { again.flags.Add("NONDETERMINISTIC"); validity.Add($"NONDETERMINISTIC repeat of row {first.id}"); }
                }
                Summary("Probe64_Tower", true);
            }
            finally { End(); }
            Verdict("Probe64_Tower");
        }

        /// <summary>Supplementary (td M5): T1 F1 and F8 up at theta 0, 10R, 20R, 10L, 20L at frame times 1/30 and 1/144.</summary>
        [UnityTest, Explicit("#64 controller probe: fails on main by design"), Category("ControllerProbe64"), Timeout(3600000)]
        public IEnumerator Probe64_DtSweep()
        {
            yield return Begin("Probe64_DtSweep", 1f / 30f);
            try
            {
                yield return TowerSetup();
                WriteHeader("Probe64_DtSweep");
                var refs = new Dictionary<string, ProbeResult>();
                foreach (float frameDt in new[] { 1f / 30f, 1f / 144f })
                {
                    dt = frameDt; Time.captureDeltaTime = dt;
                    yield return null;
                    foreach (int fl in new[] { 1, 8 })
                        foreach (var y in TowerYaws.Where(v => v.th == 0f || v.th == 10f || v.th == 20f))
                            { var sw = TowerRun(fl, true, y.th, y.right); yield return Go(sw, refs); Emit("    1/60 Tower row for comparison: " + TowerRef(sw)); }
                }
                Summary("Probe64_DtSweep", false);
            }
            finally { End(); }
            Verdict("Probe64_DtSweep");
        }
    }
}
