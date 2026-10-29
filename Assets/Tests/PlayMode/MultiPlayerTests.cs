using System;
using System.Collections;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;
using WashedAshore.Gameplay;
using WashedAshore.Lookout;
using WashedAshore.Tests.Support;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace WashedAshore.Tests.PlayMode
{
    /// <summary>
    /// #46 P4, PR A forms (spec 94f34aa6, 7.A): two players in World at once. Frame-based at 1/60 capture; both players are driven by
    /// ExternalMoveInput through the normal Update path (no key or button is pressed, so neither clone's jump, sprint or stance can fire).
    /// Player B is a clone of the scene's player with its Camera and AudioListener off.
    /// Known-fail cases are [Explicit] [Category("KnownFail46")] and must FAIL when run: each prints the value that failed.
    /// </summary>
    public class MultiPlayerTests
    {
        const float Dt = 1f / 60f;
        float savedCapture;
        PlayerController a, b;
        Transform tower;
        TowerWalkZones zones;

        [SetUp]
        public void SetUp()
        {
            savedCapture = Time.captureDeltaTime;
            Time.captureDeltaTime = Dt;
        }

        [TearDown]
        public void TearDown()
        {
            Time.captureDeltaTime = savedCapture;
            TowerWalkZones.TestFirstPlayerOnly = false;
            WorldBoundsClamp.TestFirstPlayerOnly = false;
            if (b != null) UnityEngine.Object.Destroy(b.gameObject);
        }

        /// <summary>Loads World, puts A at PlayerSpawn, makes clone B, lets B's Start run. Both players idle.</summary>
        IEnumerator TwoPlayers()
        {
            SceneManager.LoadScene(TestSpawn.WorldScene, LoadSceneMode.Single);
            yield return null;
            yield return null;
            a = TestSpawn.Player();
            yield return TestSpawn.At(TestSpawn.PlayerSpawnPoint);
            var clone = UnityEngine.Object.Instantiate(a.gameObject);
            clone.name = "Player_B";
            foreach (var cam in clone.GetComponentsInChildren<Camera>(true)) cam.enabled = false;
            foreach (var l in clone.GetComponentsInChildren<AudioListener>(true)) l.enabled = false;
            b = clone.GetComponent<PlayerController>();
            yield return null;
            yield return null; // B's Start has respawned it at the spawn point
            a.ExternalMoveInput = Vector2.zero;
            b.ExternalMoveInput = Vector2.zero;
            Assert.AreEqual(2, PlayerRegistry.Count, "both players are registered");
            var t = GameObject.Find("LookoutTower");
            Assert.IsNotNull(t, "LookoutTower is not in World.unity");
            tower = t.transform;
            zones = tower.GetComponent<TowerWalkZones>();
            Assert.IsNotNull(zones, "the tower has no TowerWalkZones");
        }

        static IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }

        Vector3 P(string laneName) => tower.GetComponentsInChildren<Transform>(true).First(t => t.name == laneName).position;
        Vector3 Dir(int i) { Vector3 d = P($"LANE_F{i:00}_Head") - P($"LANE_F{i:00}_Foot"); d.y = 0f; return d.normalized; }
        static float YawOf(Vector3 dir) => Quaternion.LookRotation(dir, Vector3.up).eulerAngles.y;

        /// <summary>A point on the ground 3 m outside the tower footprint (tower-local x below the box), 0.5 m above the terrain.</summary>
        Vector3 OutsideFootprint()
        {
            var local = new Vector3(zones.footprintMin.x - 3f, 0f, 0.5f * (zones.footprintMin.z + zones.footprintMax.z));
            Vector3 w = tower.TransformPoint(local);
            Assert.IsTrue(TerrainQuery.TryGroundHeight(w, out float g), "no terrain 3 m outside the footprint");
            w.y = g + 0.5f;
            return w;
        }

        struct Authored { public float radius, height, slope, step; }

        static Authored Read(PlayerController p)
        {
            var c = p.Capsule;
            return new Authored { radius = c.radius, height = c.height, slope = c.slopeLimit, step = c.stepOffset };
        }

        // ---- T4a -------------------------------------------------------------------------------------------------

        [UnityTest, Timeout(300000)]
        public IEnumerator T4a_TwoPlayers_TowerZonesPerPlayer() => RunTowerZones();

        /// <summary>KF-T4a: the zone acts only on the first registered player (today's one-player behaviour); phase 2 must fail on B.</summary>
        [UnityTest, Timeout(300000), Explicit("Known-fail: must FAIL"), Category("KnownFail46")]
        public IEnumerator KnownFail_T4a_ZoneActsOnFirstPlayerOnly()
        {
            TowerWalkZones.TestFirstPlayerOnly = true;
            yield return RunTowerZones();
        }

        IEnumerator RunTowerZones()
        {
            yield return TwoPlayers();
            // Authored values are read at PlayerSpawn, outside the tower, before any zone has touched either capsule.
            var authA = Read(a);
            var authB = Read(b);
            Assert.IsFalse(a.TowerZone.slimmed || b.TowerZone.slimmed, "a capsule is slimmed at PlayerSpawn");
            Debug.Log($"T4a authored: radius {authA.radius} height {authA.height} slopeLimit {authA.slope} stepOffset {authA.step}");
            Vector3 ramp = Vector3.Lerp(P("LANE_F02_Foot"), P("LANE_F02_Head"), 0.5f) + Vector3.up * 0.05f;
            float yaw2 = YawOf(Dir(2));
            Vector3 outside = OutsideFootprint();

            // Phase 1: A mid-ramp on flight 2, B outside.
            TestSpawn.Reposition(a, ramp, yaw2);
            TestSpawn.Reposition(b, outside, 0f);
            yield return Frames(10);
            CheckInTower(a, "phase 1 A");
            CheckOutside(b, "phase 1 B", authB);

            // Phase 2: swap.
            TestSpawn.Reposition(b, ramp, yaw2);
            TestSpawn.Reposition(a, outside, 0f);
            yield return Frames(10);
            CheckInTower(b, "phase 2 B");
            CheckOutside(a, "phase 2 A", authA);

            // Phase 3: both climb flight 1, B 1.5 m behind A.
            yield return Phase3(a, b);
        }

        /// <summary>
        /// Walk flight 1 with ExternalMoveInput forward: the first player at the mouth, each later one 1.5 m behind the one before. Checks that
        /// every player reaches the head and that its speed on the ramp is the cap, after SettleFrames. Prints the first 40 ramp frames of each.
        /// </summary>
        IEnumerator Phase3(params PlayerController[] ps)
        {
            Vector3 dir1 = Dir(1);
            Vector3 mouth = P("LANE_F01_Foot") - dir1 * 0.3f;
            for (int i = 0; i < ps.Length; i++) TestSpawn.Reposition(ps[i], mouth - dir1 * (1.5f * i), YawOf(dir1));
            yield return Frames(2);
            foreach (var p in ps) { p.SetLook(YawOf(dir1), 0f); p.ExternalMoveInput = Vector2.up; }
            Vector3 head = P("LANE_F01_Head");
            Vector3 foot = P("LANE_F01_Foot");
            float flat = new Vector2(head.x - foot.x, head.z - foot.z).magnitude;
            int limit = Mathf.CeilToInt(2f * flat / 1.15f * 60f) + 120;
            var run = ps.Select(p => new Runner(p, head)).ToArray();
            float closest = float.MaxValue;
            for (int f = 0; f < limit && !run.All(r => r.Reached); f++)
            {
                yield return null;
                foreach (var r in run) r.Sample();
                if (ps.Length > 1) closest = Mathf.Min(closest, Vector3.Distance(ps[0].transform.position, ps[1].transform.position));
            }
            foreach (var r in run)
                Debug.Log($"T4a phase 3 {r.p.name}: all ramp frames mean {r.Mean:F3} max {r.Max:F3} at ramp frame {r.MaxFrame}; after {SettleFrames} ramp frames mean {r.SettledMean:F3} max {r.SettledMax:F3} over {r.SettledFrames} frames; first ramp frames (frame:speed m/s,feetY):{r.Trace}");
            Debug.Log($"T4a phase 3: players {ps.Length}, closest A-B distance {(ps.Length > 1 ? closest.ToString("F3") : "n/a")} m");
            foreach (var r in run)
            {
                Assert.IsTrue(r.Reached, $"{r.p.name} did not reach LANE_F01_Head within {limit} frames (feet {r.p.Capsule.bounds.min:F2}, head {head:F2})");
                Assert.Greater(r.SettledFrames, 10, $"{r.p.name} spent too few frames on the ramp after settling");
                Assert.LessOrEqual(r.SettledMax, 1.155f, $"{r.p.name} exceeded the ramp cap: max {r.SettledMax:F3} m/s after settling");
                Assert.GreaterOrEqual(r.SettledMean, 0.9f * 1.15f, $"{r.p.name} ramp mean {r.SettledMean:F3} m/s is below 0.9 x 1.15");
            }
        }

        /// <summary>Control for B1 (qa-46): the same phase 3 with one player and no clone. Info only.</summary>
        [UnityTest, Timeout(300000), Explicit("Info: control run, one player"), Category("Info46")]
        public IEnumerator Control_T4a_Phase3_OnePlayer()
        {
            SceneManager.LoadScene(TestSpawn.WorldScene, LoadSceneMode.Single);
            yield return null;
            yield return null;
            a = TestSpawn.Player();
            yield return TestSpawn.At(TestSpawn.PlayerSpawnPoint);
            a.ExternalMoveInput = Vector2.zero;
            var t = GameObject.Find("LookoutTower");
            Assert.IsNotNull(t, "LookoutTower is not in World.unity");
            tower = t.transform;
            zones = tower.GetComponent<TowerWalkZones>();
            yield return Phase3(a);
        }

        // The player arrives on the ramp faster than the cap and slows down to it. Measured in A-2b (one-player control, A and B):
        // 2.14 to 1.15 in 7 ramp frames, 2.76 to 1.15 in 12; settled max 1.153. The cap is checked after 30 ramp frames, with margin
        // (B1, td-46 adr/46-a-departures-4-b1); the all-frames figures are printed too, with the ramp frame of the maximum.
        const int SettleFrames = 30;

        sealed class Runner
        {
            public readonly PlayerController p;
            readonly Vector3 head;
            Vector3 last;
            float sum, settledSum;
            int n, settledN, rampFrames, frame;
            public float Max, SettledMax;
            public int MaxFrame;
            public string Trace = "";
            public bool Reached;
            public float Mean => n > 0 ? sum / n : 0f;
            public float SettledMean => settledN > 0 ? settledSum / settledN : 0f;
            public int SettledFrames => settledN;
            public Runner(PlayerController p, Vector3 head) { this.p = p; this.head = head; last = p.transform.position; }

            public void Sample()
            {
                Vector3 now = p.transform.position;
                float v = new Vector2(now.x - last.x, now.z - last.z).magnitude * 60f;
                last = now;
                frame++;
                if (p.TowerZone.OnRamp)
                {
                    if (rampFrames < 40) Trace += $" {frame}:{v:F2},{p.Capsule.bounds.min.y:F2}";
                    sum += v; n++;
                    if (v > Max) { Max = v; MaxFrame = rampFrames + 1; }
                    // The player slows to the cap over up to 12 ramp frames (measured in A-2b), so the cap is checked after SettleFrames
                    // ramp frames; see SettleFrames (build-list item B1).
                    if (++rampFrames > SettleFrames) { settledSum += v; settledN++; SettledMax = Mathf.Max(SettledMax, v); }
                }
                float d = new Vector2(now.x - head.x, now.z - head.z).magnitude;
                if (d <= 0.35f && Mathf.Abs(p.Capsule.bounds.min.y - head.y) <= 0.25f) Reached = true;
            }
        }

        void CheckInTower(PlayerController p, string label)
        {
            var c = p.Capsule;
            Debug.Log($"T4a {label}: radius {c.radius} height {c.height} slope {c.slopeLimit} step {c.stepOffset} cap {p.WalkSpeedCap} onRamp {p.TowerZone.OnRamp}");
            Assert.AreEqual(zones.towerRadius, c.radius, 1e-4f, $"{label}: radius {c.radius}");
            Assert.AreEqual(zones.towerHeight, c.height, 1e-4f, $"{label}: height {c.height}");
            Assert.AreEqual(zones.flightSlopeLimit, c.slopeLimit, 1e-4f, $"{label}: slopeLimit {c.slopeLimit}");
            Assert.AreEqual(zones.upperStepOffset, c.stepOffset, 1e-4f, $"{label}: stepOffset {c.stepOffset}");
            Assert.AreEqual(zones.stairWalkSpeed, p.WalkSpeedCap, 1e-4f, $"{label}: WalkSpeedCap {p.WalkSpeedCap}");
            Assert.IsTrue(p.TowerZone.OnRamp, $"{label}: not on the ramp");
        }

        void CheckOutside(PlayerController p, string label, Authored auth)
        {
            var c = p.Capsule;
            Debug.Log($"T4a {label}: radius {c.radius} height {c.height} slope {c.slopeLimit} step {c.stepOffset} cap {p.WalkSpeedCap} inFootprint {p.TowerZone.InFootprint}");
            Assert.AreEqual(auth.radius, c.radius, 1e-4f, $"{label}: radius {c.radius}, authored {auth.radius}");
            Assert.AreEqual(auth.height, c.height, 1e-4f, $"{label}: height {c.height}, authored {auth.height}");
            Assert.AreEqual(auth.slope, c.slopeLimit, 1e-4f, $"{label}: slopeLimit {c.slopeLimit}, authored {auth.slope}");
            Assert.AreEqual(auth.step, c.stepOffset, 1e-4f, $"{label}: stepOffset {c.stepOffset}, authored {auth.step}");
            Assert.AreEqual(0f, p.WalkSpeedCap, 1e-4f, $"{label}: WalkSpeedCap {p.WalkSpeedCap}");
            Assert.IsFalse(p.TowerZone.InFootprint, $"{label}: inside the footprint");
        }

        // ---- T4b -------------------------------------------------------------------------------------------------

        [UnityTest, Timeout(300000)]
        public IEnumerator T4b_TwoPlayers_BothClamped() => RunClamped(true);

        /// <summary>KF-T4b: the clamp's player pass acts only on the first registered player; B must be left north.</summary>
        [UnityTest, Timeout(300000), Explicit("Known-fail: must FAIL"), Category("KnownFail46")]
        public IEnumerator KnownFail_T4b_ClampActsOnFirstPlayerOnly()
        {
            WorldBoundsClamp.TestFirstPlayerOnly = true;
            yield return RunClamped(false);
        }

        IEnumerator RunClamped(bool expectBothLogs)
        {
            yield return TwoPlayers();
            var clamp = WorldBoundsTestKit.Clamp();
            var rule = clamp.Rule;
            var xs = WorldBoundsTestKit.LandPointsAlongLine(rule, WorldBoundsTestKit.Config().WaterLevelY, 2);
            Assert.AreEqual(2, xs.Count, "two land points along the line");
            yield return new WaitForSeconds(0.5f);

            int teleA = 0, teleB = 0;
            Action ca = () => teleA++, cb = () => teleB++;
            a.Teleported += ca;
            b.Teleported += cb;
            int snaps = clamp.SnapCount;
            WorldBoundsTestKit.Teleport(a, new Vector3(xs[0], 0f, rule.LineZAt(xs[0]) + 10f), 0f, 1f);
            WorldBoundsTestKit.Teleport(b, new Vector3(xs[1], 0f, rule.LineZAt(xs[1]) + 10f), 0f, 1f);
            LogAssert.Expect(LogType.Warning, new Regex($"^WorldBoundsClamp: {Regex.Escape(a.name)} was 10\\.00 m north"));
            if (expectBothLogs)
                LogAssert.Expect(LogType.Warning, new Regex($"^WorldBoundsClamp: {Regex.Escape(b.name)} was 10\\.00 m north"));
            yield return null;
            a.Teleported -= ca;
            b.Teleported -= cb;

            float northA = rule.NorthOfLine(a.transform.position), northB = rule.NorthOfLine(b.transform.position);
            Debug.Log($"T4b: A {northA:F2} m, B {northB:F2} m from the line after one frame; snaps {clamp.SnapCount - snaps}; Teleported A {teleA} B {teleB}");
            Assert.Less(northA, 0f, $"A still {northA:F2} m north of the line");
            Assert.Less(northB, 0f, $"B still {northB:F2} m north of the line");
            Assert.AreEqual(snaps + 2, clamp.SnapCount, "two snaps");
            Assert.AreEqual(1, teleA, "A's Teleported raised once");
            Assert.AreEqual(1, teleB, "B's Teleported raised once");
        }

        /// <summary>
        /// The ejection case (td-46 adr/46-a-departures-4-b1/verdict-correction). Each player starts 1 m south of the line, which is inside the
        /// 3 m thick backstop wall, and holds forward. The CharacterController is ejected through the wall's north face to line + radius +
        /// skin (0.45 m north) and the clamp puts it back by the end of the frame. So this does not test a walk up to the line (a walk from
        /// outside the wall is blocked): it tests that every player is clamped when something puts it north. After every frame's LateUpdate
        /// neither player is north of the line, and each is snapped at least once.
        /// </summary>
        [UnityTest, Timeout(300000)]
        public IEnumerator T4b_StartInsideBackstop_BothEjectedAndClamped() => RunEjection();

        [UnityTest, Timeout(300000), Explicit("Known-fail: must FAIL"), Category("KnownFail46")]
        public IEnumerator KnownFail_T4b_Ejection_ClampActsOnFirstPlayerOnly()
        {
            WorldBoundsClamp.TestFirstPlayerOnly = true;
            yield return RunEjection();
        }

        IEnumerator RunEjection()
        {
            yield return TwoPlayers();
            var clamp = WorldBoundsTestKit.Clamp();
            var rule = clamp.Rule;
            var xs = WorldBoundsTestKit.LandPointsAlongLine(rule, WorldBoundsTestKit.Config().WaterLevelY, 2);
            Assert.AreEqual(2, xs.Count, "two land points along the line");
            int teleA = 0, teleB = 0;
            Action ca = () => teleA++, cb = () => teleB++;
            a.Teleported += ca;
            b.Teleported += cb;
            // Each starts 1 m south of the line, facing north (yaw 0), and holds forward.
            WorldBoundsTestKit.Teleport(a, new Vector3(xs[0], 0f, rule.LineZAt(xs[0]) - 1f), 0f);
            WorldBoundsTestKit.Teleport(b, new Vector3(xs[1], 0f, rule.LineZAt(xs[1]) - 1f), 0f);
            yield return Frames(2);
            teleA = teleB = 0;
            a.ExternalMoveInput = Vector2.up;
            b.ExternalMoveInput = Vector2.up;
            // The recorder's LateUpdate runs after the clamp's (order 10000): it reads each player's state at the end of the frame.
            var rec = new GameObject("AfterClampRecorder").AddComponent<AfterClampRecorder>();
            rec.rule = rule; rec.a = a; rec.b = b;
            float worstA = float.MinValue, worstB = float.MinValue, preA = float.MinValue, preB = float.MinValue, maxStep = 0f;
            Vector3 lastA = a.transform.position;
            var trace = new System.Text.StringBuilder();
            var snaps = new System.Text.StringBuilder();
            Action<GameObject, Vector3, Vector3> onSnap = (go, from, to) => snaps.Append($"{System.Environment.NewLine}  {go.name}: snapped from {from:F2} (north {rule.NorthOfLine(from):F3}) to {to:F2} (north {rule.NorthOfLine(to):F3}) at frame {Time.frameCount}");
            WorldBoundsClamp.Snapped += onSnap;
            int traced = 0;
            for (int f = 0; f < 120; f++)
            {
                yield return null; // after this frame's Update, before its LateUpdate: the pre-clamp reading, printed for diagnosis
                float nA = rule.NorthOfLine(a.transform.position), nB = rule.NorthOfLine(b.transform.position);
                preA = Mathf.Max(preA, nA);
                preB = Mathf.Max(preB, nB);
                float step = Vector3.Distance(lastA, a.transform.position);
                maxStep = Mathf.Max(maxStep, step);
                lastA = a.transform.position;
                if ((nA > -0.3f || nB > -0.3f) && traced++ < 40)
                    trace.Append($"{System.Environment.NewLine}  frame {f}: before clamp A {nA:F3} B {nB:F3}; end of previous frame A {rec.lastA:F3} B {rec.lastB:F3}; A step {step:F3}; clamp snaps {clamp.SnapCount}");
            }
            WorldBoundsClamp.Snapped -= onSnap;
            worstA = rec.worstA; worstB = rec.worstB;
            UnityEngine.Object.Destroy(rec.gameObject);
            a.Teleported -= ca;
            b.Teleported -= cb;
            Debug.Log($"T4b ejection: end-of-frame worst NorthOfLine A {worstA:F3} B {worstB:F3}; before-clamp worst A {preA:F3} B {preB:F3}; largest A step per frame {maxStep:F3} m (dt {Time.deltaTime:F4}); Teleported A {teleA} B {teleB}; clamp snaps {clamp.SnapCount}{snaps}{System.Environment.NewLine}T4b ejection frames near the line:{trace}");
            Assert.LessOrEqual(worstA, 0f, $"A was {worstA:F3} m north of the line after a frame");
            Assert.LessOrEqual(worstB, 0f, $"B was {worstB:F3} m north of the line after a frame");
            Assert.GreaterOrEqual(teleA, 1, "A was never snapped");
            Assert.GreaterOrEqual(teleB, 1, "B was never snapped");
        }

        /// <summary>Reads both players' distance north of the line at the end of every frame, after the clamp's LateUpdate.</summary>
        [DefaultExecutionOrder(20000)]
        sealed class AfterClampRecorder : MonoBehaviour
        {
            public WorldBoundsRule rule;
            public PlayerController a, b;
            public float worstA = float.MinValue, worstB = float.MinValue, lastA, lastB;

            void LateUpdate()
            {
                lastA = rule.NorthOfLine(a.transform.position);
                lastB = rule.NorthOfLine(b.transform.position);
                worstA = Mathf.Max(worstA, lastA);
                worstB = Mathf.Max(worstB, lastB);
            }
        }

        // ---- C5-A: no allocation ---------------------------------------------------------------------------------

        static bool Allocates(Action act, int calls)
        {
            for (int i = 0; i < 20; i++) act(); // warm up
            try
            {
                Assert.That(() => { for (int i = 0; i < calls; i++) act(); }, Is.Not.AllocatingGCMemory());
                return false;
            }
            catch (AssertionException)
            {
                return true;
            }
        }

        [UnityTest, Timeout(300000)]
        public IEnumerator C5A_ZoneDuckAndClampPasses_DoNotAllocate_WithTwoPlayers()
        {
            yield return TwoPlayers();
            var clamp = WorldBoundsTestKit.Clamp();
            var hatch = tower.GetComponentInChildren<TrapDoor>(true);
            Assert.IsNotNull(hatch, "the tower has no TrapDoor");
            // Both players south of the line and idle, one inside the tower: the passes run their full paths without snapping anything.
            TestSpawn.Reposition(a, Vector3.Lerp(P("LANE_F02_Foot"), P("LANE_F02_Head"), 0.5f) + Vector3.up * 0.05f);
            TestSpawn.Reposition(b, OutsideFootprint());
            yield return Frames(10);
            bool zoneAlloc = Allocates(zones.ZonePass, 200);
            bool duckAlloc = Allocates(hatch.DuckPass, 200);
            bool clampAlloc = Allocates(clamp.Tick, 200);
            Debug.Log($"C5-A: allocates zone={zoneAlloc} duck={duckAlloc} clamp={clampAlloc} ({PlayerRegistry.Count} players)");
            Assert.IsFalse(zoneAlloc, "TowerWalkZones.ZonePass allocated");
            Assert.IsFalse(duckAlloc, "TrapDoor.DuckPass allocated");
            Assert.IsFalse(clampAlloc, "WorldBoundsClamp.Tick allocated");
        }

        static object sink;

        /// <summary>KF-C5A: the same check around a block that allocates one int[16] must report an allocation, i.e. this test must FAIL.</summary>
        [Test, Explicit("Known-fail: must FAIL"), Category("KnownFail46")]
        public void KnownFail_C5A_CheckSeesAnAllocation()
        {
            bool allocates = Allocates(() => sink = new int[16], 200);
            Assert.IsFalse(allocates, "the allocation check reports the block allocates (it is alive)");
        }
    }
}
