using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using WashedAshore.Gameplay;
using WashedAshore.Lookout;

namespace WashedAshore.Tests.PlayMode
{
    /// <summary>
    /// #8 S1, no view bump. Walks the tower up with the ordinary controller (W held, steering by yaw, never jumping) and reads the REAL camera
    /// (player.ViewCamera.transform) once per frame while the player is within one hairpin turn (window: horizontal distance under WindowRadius from the
    /// point half way between flight i's head and flight i+1's foot, and the feet within 1 m of that landing's height).
    /// VERDICT measure (qa-8 ruling after run 4, chosen after seeing that data): jitter = the residual of the real camera Y from a local quadratic fit over +-0.06 s; per turn the largest
    /// |residual| over the window frames at least 0.06 s from both ends; bar 0.010 m. Printed for information: jS = camY - (floor under the camera XZ + EyeHeight), which cannot pass on
    /// this tower (the 0.25 m capsule rides 0.21 m above a 52 degree ramp and 0.05 m above a landing), the sim feet's rise in the window, the largest rise in one tick, the floor collider hit.
    /// Frame time is pinned with Time.captureDeltaTime (1/144 and 1/60). Rub passes hold W with the yaw 20 degrees off the lane line on every flight.
    /// Tables go to Logs/tower-view-*.txt.
    /// </summary>
    public class TowerWalkViewTests : InputTestFixture
    {
        const float Bar = 0.010f, WindowRadius = 0.93f, TurnOut = 0.4f, ApproachBack = 0.3f, LookAhead = 0.6f, StallSeconds = 4f, StallMetres = 0.1f, LiftTickRise = 0.015f, FitHalf = 0.06f;

        class Wp { public string name; public Vector3 at, from; public bool lane, stop; }

        class Turn
        {
            public int index, frames, contactFrames, ticks, noFloor;
            public float sumDt, minS = float.MaxValue, maxS = float.MinValue, maxStepS, prevS = float.NaN;   // jS as measured (with the known-fail injection, if any)
            public float minB = float.MaxValue, maxB = float.MinValue;                                         // jS without any injection
            public float feetMin = float.MaxValue, feetMax = float.MinValue, lastTickFeet = float.NaN, maxTickRise, runRise, bestRunRise;
            public int runTicks, bestRunTicks;
            public readonly Dictionary<string, int> hits = new Dictionary<string, int>();   // collider the floor ray hit under the camera, per sample
            public readonly List<float> ts = new List<float>(), ys = new List<float>(), yb = new List<float>();   // per window frame: time, camY (with any injection), camY as measured
            public readonly List<int> kind = new List<int>();                                                    // 0 neither, 1 tick frame, 2 contact frame (a tick frame whose move ended in a side contact)
            public float resMax = float.NaN, resBase = float.NaN, resAtT; public int resKind;
            public float PeakToPeak => frames == 0 || maxS < minS ? float.NaN : maxS - minS;
            public float PeakToPeakBase => frames == 0 || maxB < minB ? float.NaN : maxB - minB;
        }

        readonly StringBuilder trace = new StringBuilder();
        Keyboard keyboard;
        float savedCapture;

        public override void Setup()
        {
            base.Setup();
            keyboard = InputSystem.AddDevice<Keyboard>();
            InputSystem.AddDevice<Mouse>();
            savedCapture = Time.captureDeltaTime;
        }

        public override void TearDown()
        {
            Time.captureDeltaTime = savedCapture;
            base.TearDown();
        }

        [UnityTest, Timeout(600000)] public IEnumerator S1_Centre_144fps() => Run("centre-144", 144f, 0f, false);
        [UnityTest, Timeout(600000)] public IEnumerator S1_Centre_60fps() => Run("centre-60", 60f, 0f, false);
        [UnityTest, Timeout(600000)] public IEnumerator S1_RubRight20_144fps() => Run("rubR-144", 144f, 20f, false);
        [UnityTest, Timeout(600000)] public IEnumerator S1_RubRight20_60fps() => Run("rubR-60", 60f, 20f, false);
        [UnityTest, Timeout(600000)] public IEnumerator S1_RubLeft20_144fps() => Run("rubL-144", 144f, -20f, false);
        [UnityTest, Timeout(600000)] public IEnumerator S1_RubLeft20_60fps() => Run("rubL-60", 60f, -20f, false);

        /// <summary>Known fail: 0.02 m added to the camY sample on window frame 10 of every turn. It counts only if a turn whose baseline residual is under 0.010 goes over it (the table prints both).</summary>
        [UnityTest, Timeout(600000), Explicit("known-fail: injects +0.02 m on one camera sample"), Category("KnownFail")]
        public IEnumerator KF_S1_OneFrameBump_144fps() => Run("kf-144", 144f, 0f, true);

        IEnumerator Run(string tag, float fps, float yawOffset, bool injectBump)
        {
            Time.captureDeltaTime = 1f / fps;
            SceneManager.LoadScene("World", LoadSceneMode.Single);
            yield return null;
            yield return null;
            var go = GameObject.FindGameObjectWithTag("Player");
            Assert.IsNotNull(go, "no Player");
            var player = go.GetComponent<PlayerController>();
            var body = go.GetComponent<CharacterController>();
            var tower = GameObject.Find("LookoutTower");
            Assert.IsNotNull(tower, "LookoutTower is not in World.unity");
            var tf = tower.GetComponentsInChildren<Transform>(true).ToDictionary(t => t.name, t => t, System.StringComparer.Ordinal);
            Vector3 P(string n) => tf[n].position;
            Vector3 Dir(int i) { Vector3 d = P($"LANE_F{i:00}_Head") - P($"LANE_F{i:00}_Foot"); d.y = 0f; return d.normalized; }
            Vector3 TurnPoint(int i) => (P($"LANE_F{i:00}_Head") + P($"LANE_F{i + 1:00}_Foot")) * 0.5f + Dir(i) * TurnOut;

            var hatchDoor = tower.GetComponentInChildren<TrapDoor>(true);
            Assert.IsNotNull(hatchDoor, "the tower has no TrapDoor");
            hatchDoor.SetState(HingeState.Open, 0f);

            var route = new List<Wp>();
            for (int i = 1; i <= 8; i++)
            {
                Vector3 foot = P($"LANE_F{i:00}_Foot"), head = P($"LANE_F{i:00}_Head");
                route.Add(new Wp { name = $"F{i:00} mouth", at = foot - Dir(i) * ApproachBack, stop = true });
                route.Add(new Wp { name = $"F{i:00} head", at = i == 8 ? head - Dir(i) * 0.1f : head, from = foot, lane = true });
                if (i < 8) route.Add(new Wp { name = $"turn {i}", at = TurnPoint(i) });
            }

            // The spawn is the temporary tower-base point (TestSpawn); the slim tower capsule is primed by it.
            yield return Support.TestSpawn.AtTowerBase();
            trace.Clear();
            var turns = Enumerable.Range(1, 7).Select(i => new Turn { index = i }).ToArray();
            var report = new StringBuilder($"S1 {tag}: fps {fps}, yaw offset {yawOffset} deg on flights, injectBump {injectBump}, bar {Bar:F3} m, window radius {WindowRadius} m\n");
            string failure = null;
            keyboard = Keyboard.current;
            Press(keyboard.wKey);
            bool held = true;
            float t0 = Time.time, lastBest = float.MaxValue, lastProgress = Time.time, sumDt = 0f;
            int idx = 0, frames = 0, lastTick = player.Tick;
            Vector3 lastPos = go.transform.position;
            while (idx < route.Count)
            {
                var w = route[idx];
                Vector3 pos = go.transform.position;
                float speed = (pos - lastPos).magnitude / Mathf.Max(Time.deltaTime, 1e-4f);
                lastPos = pos;
                frames++; sumDt += Time.deltaTime;
                Sample(player, body, tf, turns, injectBump, ref lastTick);
                pos.y = body.bounds.min.y;
                Vector3 flat = w.at - pos; flat.y = 0f;
                float reach = w.stop || w.lane ? 0.2f : 0.35f;
                if (flat.magnitude < reach && Mathf.Abs(w.at.y - pos.y) < 0.25f) { idx++; lastBest = float.MaxValue; lastProgress = Time.time; continue; }
                Vector3 aim = w.at;
                if (w.lane)
                {
                    Vector3 seg = w.at - w.from; seg.y = 0f;
                    Vector3 dir = seg.normalized;
                    float along = Mathf.Clamp(Vector3.Dot(new Vector3(pos.x - w.from.x, 0f, pos.z - w.from.z), dir), 0f, seg.magnitude);
                    aim = w.from + dir * Mathf.Min(along + LookAhead, seg.magnitude);
                    aim.y = w.at.y;
                    flat = aim - pos; flat.y = 0f;
                }
                if (flat.sqrMagnitude > 1e-4f)
                {
                    float yaw = Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg + (w.lane && !w.name.StartsWith("F08") ? yawOffset : 0f);
                    player.SetLook(yaw, 0f);
                }
                bool wantHeld = !(w.stop && flat.magnitude < 1.0f && speed > 1.2f);
                if (wantHeld && !held) Press(keyboard.wKey); else if (!wantHeld && held) Release(keyboard.wKey);
                held = wantHeld;
                float d = (w.at - pos).magnitude;
                if (d < lastBest - StallMetres) { lastBest = d; lastProgress = Time.time; }
                if (Time.time - lastProgress > StallSeconds)
                {
                    failure = $"stuck going to '{w.name}' at local {tower.transform.InverseTransformPoint(pos):F2} after {Time.time - t0:F1}s (grounded {player.IsGrounded})";
                    break;
                }
                yield return null;
            }
            Release(keyboard.wKey);

            float meanDt = sumDt / Mathf.Max(1, frames);
            bool dtOk = Mathf.Abs(meanDt - 1f / fps) < 0.1f / fps;
            report.AppendLine($"walk: {(failure == null ? "completed" : "FAILED: " + failure)} in {Time.time - t0:F1}s game time, {frames} frames, mean rendered dt {meanDt * 1000f:F3} ms (want {1000f / fps:F3}) {(dtOk ? "valid" : "INVALID")}");
            report.AppendLine("turn | frames | meanDt ms | ticks | contact frames | VERDICT: max |residual| of camY from a local quadratic fit (+-0.06 s) | baseline (no injection) | at t into window s / frame kind | bar | verdict || info: min jS | max jS | peak-to-peak jS | baseline p-p | max |step| jS | sim feet rise in window | max rise in one tick | longest lift run (ticks / rise) | no-floor frames | floor collider hits (player mask)");
            var bad = new List<string>();
            string[] kinds = { "neither", "tick frame", "contact frame" };
            foreach (var t in turns)
            {
                t.resMax = MaxResidual(t.ts, t.ys, out int at, out float atT); t.resAtT = atT; t.resKind = at >= 0 ? t.kind[at] : 0;
                t.resBase = MaxResidual(t.ts, t.yb, out _, out _);
                bool ok = t.frames >= 10 && t.noFloor == 0 && !float.IsNaN(t.resMax) && t.resMax <= Bar;
                string verdict = t.frames < 10 || float.IsNaN(t.resMax) ? "NO DATA" : t.noFloor > 0 ? "NO FLOOR" : ok ? "ok" : "FAIL";
                report.AppendLine($"{t.index} | {t.frames} | {(t.sumDt / Mathf.Max(1, t.frames)) * 1000f:F3} | {t.ticks} | {t.contactFrames} | {t.resMax:F4} | {t.resBase:F4} | {t.resAtT:F3} / {kinds[t.resKind]} | {Bar:F3} | {verdict} || {t.minS:F4} | {t.maxS:F4} | {t.PeakToPeak:F4} | {t.PeakToPeakBase:F4} | {t.maxStepS:F4} | {t.feetMax - t.feetMin:F3} | {t.maxTickRise:F4} | {t.bestRunTicks} / {t.bestRunRise:F3} | {t.noFloor} | {string.Join(" ", t.hits.OrderByDescending(h => h.Value).Select(h => h.Key + " x" + h.Value))}{(t.hits.Keys.Any(k => !k.StartsWith("WALK_Ramp") && !k.StartsWith("COL_CVX_Landing")) ? " [OTHER COLLIDER HIT]" : "")}");
                if (!ok) bad.Add(verdict == "NO DATA" ? $"turn {t.index}: no data ({t.frames} window frames)" : t.noFloor > 0 ? $"turn {t.index}: {t.noFloor} frames with no floor under the camera" : $"turn {t.index}: residual {t.resMax:F4} > {Bar:F3}");
            }
            if (failure == null && bad.Count > 0) failure = string.Join("; ", bad);
            if (failure == null && !dtOk) failure = $"INVALID run: mean rendered dt {meanDt * 1000f:F3} ms is off by more than 10%";
            report.AppendLine($"RESULT {tag} {(failure == null ? "PASS" : "FAIL: " + failure)}");
            Directory.CreateDirectory("Logs");
            File.WriteAllText($"Logs/tower-view-{tag}.txt", report.ToString());
            File.WriteAllText($"Logs/tower-view-{tag}-trace.csv", "turn,frame,time,tick(-1 none),simFeetY,camY,floorY at camera XZ,floorY at feet XZ,jS,collider hit (player mask),nearest collider (all layers)" + System.Environment.NewLine + trace);
            Debug.Log(report.ToString());
            Assert.IsNull(failure, failure);
        }

        /// <summary>
        /// One rendered frame: if the player is within a turn's window, adds the camera's jS to that turn. jS = camY - (floorY under the camera XZ + EyeHeight); floorY is the
        /// nearest hit of a ray down from camY against the player collision mask (triggers and the player excluded). injectBump (known-fail) adds 0.02 m to jS on window frame 10 of every turn.
        /// </summary>
        void Sample(PlayerController player, CharacterController body, Dictionary<string, Transform> tf, Turn[] turns, bool injectBump, ref int lastTick)
        {
            Vector3 cam = player.ViewCamera.transform.position;
            Vector3 feet = player.transform.position;
            bool ticked = player.Tick != lastTick;
            lastTick = player.Tick;
            foreach (var t in turns)
            {
                Vector3 head = tf[$"LANE_F{t.index:00}_Head"].position, foot2 = tf[$"LANE_F{t.index + 1:00}_Foot"].position;
                Vector3 mid = (head + foot2) * 0.5f;
                Vector3 off = cam - mid; off.y = 0f;
                if (off.magnitude > WindowRadius || Mathf.Abs(feet.y - head.y) > 1f) continue;
                float floorCam = FloorY(cam.x, cam.z, cam.y, player, true, out string hitName), floorFeet = FloorY(feet.x, feet.z, feet.y + 0.6f, player, true, out _);
                FloorY(cam.x, cam.z, cam.y, player, false, out string anyName);
                t.hits[hitName] = t.hits.TryGetValue(hitName, out int n) ? n + 1 : 1;
                float jBase = float.IsNaN(floorCam) ? float.NaN : cam.y - (floorCam + player.EyeHeight);
                float inj = injectBump && t.frames == 10 ? 0.02f : 0f;
                float jS = jBase + inj;
                bool contact = ticked && (body.collisionFlags & CollisionFlags.Sides) != 0;
                t.ts.Add(Time.time); t.ys.Add(cam.y + inj); t.yb.Add(cam.y); t.kind.Add(contact ? 2 : ticked ? 1 : 0);
                trace.AppendLine($"{t.index},{t.frames},{Time.time:F4},{(ticked ? player.Tick : -1)},{feet.y:F4},{cam.y:F4},{floorCam:F4},{floorFeet:F4},{jS:F4},{hitName},{anyName}");
                t.frames++; t.sumDt += Time.deltaTime;
                if (float.IsNaN(jS)) t.noFloor++;
                else
                {
                    t.minS = Mathf.Min(t.minS, jS); t.maxS = Mathf.Max(t.maxS, jS);
                    t.minB = Mathf.Min(t.minB, jBase); t.maxB = Mathf.Max(t.maxB, jBase);
                    if (!float.IsNaN(t.prevS)) t.maxStepS = Mathf.Max(t.maxStepS, Mathf.Abs(jS - t.prevS));
                    t.prevS = jS;
                }
                t.feetMin = Mathf.Min(t.feetMin, feet.y); t.feetMax = Mathf.Max(t.feetMax, feet.y);
                if (!ticked) continue;
                t.ticks++;
                if (contact) t.contactFrames++;
                float rise = float.IsNaN(t.lastTickFeet) ? 0f : feet.y - t.lastTickFeet;
                t.lastTickFeet = feet.y;
                t.maxTickRise = Mathf.Max(t.maxTickRise, rise);
                if (rise > LiftTickRise) { t.runTicks++; t.runRise += rise; if (t.runRise > t.bestRunRise) { t.bestRunRise = t.runRise; t.bestRunTicks = t.runTicks; } }
                else { t.runTicks = 0; t.runRise = 0f; }
            }
        }

        /// <summary>
        /// qa-8's jitter measure: for each sample at least FitHalf from both window ends, a least-squares quadratic of camY against time over the samples within +-FitHalf of it; the residual of the
        /// centre sample is its distance from that fit. Returns the largest |residual| (NaN with no qualifying sample), its index and its time into the window.
        /// </summary>
        static float MaxResidual(List<float> ts, List<float> ys, out int at, out float atT)
        {
            at = -1; atT = 0f; double best = -1;
            if (ts.Count < 5) return float.NaN;
            float t0 = ts[0], t1 = ts[ts.Count - 1];
            for (int i = 0; i < ts.Count; i++)
            {
                if (ts[i] - t0 < FitHalf - 1e-4f || t1 - ts[i] < FitHalf - 1e-4f) continue;
                double s0 = 0, s1 = 0, s2 = 0, s3 = 0, s4 = 0, r0 = 0, r1 = 0, r2 = 0;
                for (int j = 0; j < ts.Count; j++)
                {
                    double x = ts[j] - ts[i];
                    if (System.Math.Abs(x) > FitHalf + 1e-4) continue;
                    double y = ys[j], x2 = x * x;
                    s0 += 1; s1 += x; s2 += x2; s3 += x2 * x; s4 += x2 * x2; r0 += y; r1 += x * y; r2 += x2 * y;
                }
                double det = s0 * (s2 * s4 - s3 * s3) - s1 * (s1 * s4 - s3 * s2) + s2 * (s1 * s3 - s2 * s2);
                if (System.Math.Abs(det) < 1e-18) continue;
                double detA = r0 * (s2 * s4 - s3 * s3) - s1 * (r1 * s4 - s3 * r2) + s2 * (r1 * s3 - s2 * r2);
                double resid = System.Math.Abs(ys[i] - detA / det);
                if (resid > best) { best = resid; at = i; atT = ts[i] - t0; }
            }
            return best < 0 ? float.NaN : (float)best;
        }

        /// <summary>The floor under (x, z): the nearest hit of a ray down from <paramref name="fromY"/> against the player's collision mask (or every collider), the player's own and triggers excluded; NaN with none.</summary>
        static float FloorY(float x, float z, float fromY, PlayerController player, bool playerMask, out string name)
        {
            float best = float.MaxValue, y = float.NaN; name = "none";
            foreach (var h in Physics.RaycastAll(new Vector3(x, fromY, z), Vector3.down, 3f, playerMask ? WashedAshore.World.WorldLayers.PlayerCollisionMask : ~0, QueryTriggerInteraction.Ignore))
                if (!h.collider.transform.IsChildOf(player.transform) && h.distance < best) { best = h.distance; y = h.point.y; name = h.collider.name; }
            return y;
        }
    }
}
