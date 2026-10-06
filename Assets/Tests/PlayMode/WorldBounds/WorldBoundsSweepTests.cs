using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using WashedAshore.Gameplay;

namespace WashedAshore.Tests.PlayMode
{
    /// <summary>
    /// Spec B5 (designer-2 sweep rules): at 50 points along the north line the player starts 45 m
    /// south (20+ m run-up before level-2's fence) and walks, sprints, sprint-jumps (apex timed at
    /// each face), walks in at 30 and 60 degrees off the line normal on both sides, and takes 50 m/s
    /// CharacterController moves. Speeds and jump come from the scene player's PlayerController.
    /// qa-2 add-ons: the dry lake bed beyond both banks (no water in this POC) and the wall ends.
    /// A crossing is any frame where the player's raw position (read after Update, before the
    /// clamp's LateUpdate) is north of the line; clamp snaps of the player are reported too.
    /// qa-2 validity: a run counts only if it got within 1.5 m of the first face it started facing (fence
    /// or backstop south face) or was snapped; a run stopped earlier is "blocked by ..." and is replaced
    /// like a skipped start. Every run goes to TestResults/bb-b5-runs.csv, skips to bb-b5-skips.csv.
    /// The line is read from the scene's WorldBoundsClamp, so B6 reruns this unchanged on a shifted
    /// build. Game time runs at a fixed 1/60 s per frame for determinism.
    /// </summary>
    public partial class WorldBoundsSweepTests : InputTestFixture
    {
        public enum Approach { Walk, Sprint, SprintJump, Off30Left, Off30Right, Off60Left, Off60Right, Fast50 }

        const string WorldScene = "World";
        const int Points = 50;
        const int LakeBedPointsPerSide = 5;
        const float StartSouth = 45f;    // perpendicular start distance south of the line, m
        const float PushSeconds = 2f;    // keep pushing after the time it takes to cover the path
        const float FastSpeed = 50f;     // spec B5: one 50 m/s Move
        const float FaceSlack = 1.5f;    // qa-2: valid run = within 1.5 m of the first face
        const float MinRunUp = 20f;      // designer-2: run-up before the first barrier face
        const float MaxStartSlope = 45f; // designer-2: walkable start
        const int ReplaceSearch = 25;    // qa-2: replacement start searched +-25 m in X
        const int MaxRunsPerPoint = 6;   // the point itself plus up to 5 replacement runs (bounds sweep time)
        const int MaxSkipsPerCase = 3;   // qa-2: unreplaceable skips per case (<= 20 of 400 overall, from the CSV)

        Keyboard keyboard;
        Camera disabledCamera;

        // Scene state shared by one test's runs.
        WorldBoundsRule rule;
        PlayerController player;
        CharacterController cc;
        float wall, water;
        int playerSnaps;

        class Tally
        {
            public readonly List<string> crossings = new List<string>(), setupErrors = new List<string>(), skipped = new List<string>(),
                skipRows = new List<string>(), runRows = new List<string>();
            public readonly List<float> sprintPeaks = new List<float>();
            public int runs, valid, jumps, snaps;
            public float worstNorth = float.MinValue;
            string SprintSummary()
            {
                if (sprintPeaks.Count == 0) return "";
                var s = new List<float>(sprintPeaks); s.Sort();
                return $" sprintPeak min={s[0]:F2} median={s[s.Count / 2]:F2} m/s";
            }
            public string Summary => $"runs={runs} valid={valid} skipped={skipped.Count} crossings={crossings.Count} clampSnaps={snaps} " +
                                     $"jumps={jumps} worstNorthOfLine={worstNorth:F2}" + SprintSummary();
        }

        class RunResult
        {
            public bool valid;
            public string why;
        }

        public override void Setup()
        {
            base.Setup();
            keyboard = InputSystem.AddDevice<Keyboard>();
            Time.captureDeltaTime = 1f / 60f;
        }

        public override void TearDown()
        {
            Time.captureDeltaTime = 0f;
            WorldBoundsClamp.Snapped -= OnSnap;
            if (disabledCamera) disabledCamera.enabled = true;
            base.TearDown();
        }

        void OnSnap(GameObject go, Vector3 from, Vector3 to) { if (player && go == player.gameObject) playerSnaps++; }

        static float OffNormal(Approach m) => m switch
        {
            Approach.Off30Left => -30f, Approach.Off30Right => 30f,
            Approach.Off60Left => -60f, Approach.Off60Right => 60f,
            _ => 0f
        };

        IEnumerator LoadWorld()
        {
            SceneManager.LoadScene(WorldScene, LoadSceneMode.Single);
            yield return null;
            yield return null;
            // Physics and the clamp still run; skipping rendering keeps the sweep quick.
            disabledCamera = Camera.main;
            if (disabledCamera) disabledCamera.enabled = false;
            var clamp = WorldBoundsTestKit.Clamp();
            rule = clamp.Rule;
            wall = WorldBoundsTestKit.BackstopThickness(clamp);
            player = WorldBoundsTestKit.Player();
            cc = player.GetComponent<CharacterController>();
            water = WorldBoundsTestKit.Config().WaterLevelY;
            playerSnaps = 0;
            WorldBoundsClamp.Snapped += OnSnap;
        }

        [UnityTest, Timeout(3600000)]
        public IEnumerator Sweep_FiftyPoints_ZeroCrossings([Values] Approach mode)
        {
            yield return LoadWorld();
            // Points with land at the line and straight south; starts slide or get replaced in Point().
            var xs = WorldBoundsTestKit.LandPointsAlongLine(rule, water, Points, StartSouth, 0f);
            var tally = new Tally();
            try
            {
                for (int i = 0; i < xs.Count; i++)
                    yield return Point(tally, $"{mode}", i, xs[i], OffNormal(mode), mode, slideStart: true);
            }
            finally { ReleaseAllKeys(); }

            Debug.Log($"WorldBoundsSweepTests[{mode}]: points={xs.Count} lineZ={rule.LineZAt(xs[0]):F1} x=[{xs[0]:F0},{xs[xs.Count - 1]:F0}] {tally.Summary}");
            WriteCsv($"{mode}", tally);
            Assert.AreEqual(Points, xs.Count);
            Assert.IsEmpty(tally.setupErrors, $"{mode}: setup errors: " + string.Join("; ", tally.setupErrors));
            Assert.IsEmpty(tally.crossings, $"{mode}: crossings: " + string.Join("; ", tally.crossings));
            // designer-2: a skipped straight-on (0 degree) approach is insufficient coverage.
            if (OffNormal(mode) == 0f) Assert.IsEmpty(tally.skipped, $"{mode}: straight-on points skipped: " + string.Join("; ", tally.skipped));
            Assert.LessOrEqual(tally.skipped.Count, MaxSkipsPerCase, $"{mode}: unreplaceable skips over the cap: " + string.Join("; ", tally.skipped));
            // Unity's NUnit 3.5 has no Assert.Warn: skips are reported as a warning plus the CSV.
            if (tally.skipped.Count > 0) Debug.LogWarning($"{mode}: {tally.skipped.Count} points SKIPPED (not passes): " + string.Join("; ", tally.skipped));
        }

        /// <summary>
        /// qa-2 escape risk: with no water yet, the lake bed beyond both banks is walkable ground. Walk and
        /// sprint north from it at 5 points per side, and at each wall end sprint north and 60 degrees outward.
        /// </summary>
        [UnityTest, Timeout(3600000)]
        public IEnumerator Sweep_LakeBedAndWallEnds_ZeroCrossings()
        {
            yield return LoadWorld();
            var (west, east) = WorldBoundsTestKit.LakeBedPointsAlongLine(rule, water, LakeBedPointsPerSide, StartSouth);
            var (minX, maxX) = WorldBoundsTestKit.TerrainXRange();
            float westEnd = minX + StartSouth * 2f, eastEnd = maxX - StartSouth * 2f;
            var tally = new Tally();
            try
            {
                for (int i = 0; i < west.Count; i++)
                {
                    yield return Point(tally, "LakeBedW walk", i, west[i], 0f, Approach.Walk, false);
                    yield return Point(tally, "LakeBedW sprint", i, west[i], 0f, Approach.Sprint, false);
                }
                for (int i = 0; i < east.Count; i++)
                {
                    yield return Point(tally, "LakeBedE walk", i, east[i], 0f, Approach.Walk, false);
                    yield return Point(tally, "LakeBedE sprint", i, east[i], 0f, Approach.Sprint, false);
                }
                yield return Point(tally, "WestEnd north", 0, westEnd, 0f, Approach.Sprint, false);
                yield return Point(tally, "WestEnd outward60", 0, westEnd, -60f, Approach.Sprint, false);
                yield return Point(tally, "EastEnd north", 0, eastEnd, 0f, Approach.Sprint, false);
                yield return Point(tally, "EastEnd outward60", 0, eastEnd, 60f, Approach.Sprint, false);
            }
            finally { ReleaseAllKeys(); }

            Debug.Log($"WorldBoundsSweepTests[LakeBedAndEnds]: west=[{string.Join(",", west.ConvertAll(v => v.ToString("F0")))}] " +
                      $"east=[{string.Join(",", east.ConvertAll(v => v.ToString("F0")))}] terrainX=[{minX:F0},{maxX:F0}] {tally.Summary}");
            WriteCsv("LakeBedAndEnds", tally);
            Assert.AreEqual(LakeBedPointsPerSide, west.Count, "west lake-bed points");
            Assert.AreEqual(LakeBedPointsPerSide, east.Count, "east lake-bed points");
            Assert.IsEmpty(tally.setupErrors, "setup errors: " + string.Join("; ", tally.setupErrors));
            Assert.IsEmpty(tally.crossings, "crossings: " + string.Join("; ", tally.crossings));
            Assert.LessOrEqual(tally.skipped.Count, MaxSkipsPerCase, "unreplaceable skips over the cap: " + string.Join("; ", tally.skipped));
            if (tally.skipped.Count > 0) Debug.LogWarning($"LakeBedAndEnds: {tally.skipped.Count} points SKIPPED (not passes): " + string.Join("; ", tally.skipped));
        }

        /// <summary>
        /// One sweep point: run it from its start; if the start is unusable or the run is blocked before the first
        /// face, replace it with the nearest valid start within +-ReplaceSearch m in X (qa-2), up to MaxRunsPerPoint
        /// runs. Every replacement and every unreplaceable point goes to the skip table.
        /// </summary>
        IEnumerator Point(Tally tally, string testCase, int index, float x, float yaw, Approach mode, bool slideStart)
        {
            string firstWhy = null;
            int runsHere = 0;
            for (int d = 0; d <= ReplaceSearch && runsHere < MaxRunsPerPoint; d++)
            {
                foreach (float cand in d == 0 ? new[] { x } : new[] { x - d, x + d })
                {
                    if (runsHere >= MaxRunsPerPoint) break;
                    Vector3 start; float pathLen; string why;
                    bool ok = slideStart ? TryStart(cand, yaw, out start, out pathLen, out why) : FixedStart(cand, yaw, out start, out pathLen, out why);
                    if (!ok) { firstWhy ??= why; continue; }
                    // A replacement (d > 0) must be a valid start: its approach to the first face is walkable (heightmap
                    // slope <= 45 deg, the CC limit), so the run budget is not spent on starts the bank already blocks.
                    if (d > 0 && !ApproachWalkable(start, Quaternion.Euler(0f, yaw, 0f) * Vector3.forward)) continue;
                    var res = new RunResult();
                    runsHere++;
                    yield return Run(tally, testCase, index, cand, yaw, mode, start, pathLen, res);
                    if (res.valid)
                    {
                        if (d > 0) tally.skipRows.Add($"{index},{x:F1},{testCase},\"{firstWhy}\",{cand:F1}");
                        yield break;
                    }
                    firstWhy ??= res.why;
                }
            }
            tally.skipRows.Add($"{index},{x:F1},{testCase},\"{firstWhy}\",none");
            tally.skipped.Add($"#{index} x={x:F0} {firstWhy}");
        }

        /// <summary>One approach from <paramref name="start"/> to the line at <paramref name="x"/>, <paramref name="yaw"/> degrees off its normal.</summary>
        IEnumerator Run(Tally tally, string testCase, int index, float x, float yaw, Approach mode, Vector3 start, float pathLen, RunResult res)
        {
            tally.runs++;
            float speed = mode == Approach.Sprint || mode == Approach.SprintJump ? player.SprintSpeed : player.WalkSpeed;
            // Rise time to the apex, and the ground covered meanwhile at full speed.
            float jumpLead = speed * Mathf.Sqrt(2f * player.JumpHeight / -player.Gravity);
            var dir = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;

            ReleaseAllKeys();
            yield return new WaitForSeconds(0.5f); // let PlayerController's velocity decay
            WorldBoundsTestKit.Teleport(player, start, yaw);
            yield return new WaitForSeconds(0.25f);
            float faceAhead = FaceDistance(start, dir), travel = 0f, maxZ = float.MinValue, peakSpeed = 0f;
            Vector3 last = player.transform.position;
            int snapsBefore = playerSnaps;
            float maxNorth = float.MinValue;
            void Sample()
            {
                var p = player.transform.position;
                travel = Mathf.Max(travel, Vector3.Dot(Flat(p - start), dir));
                maxZ = Mathf.Max(maxZ, p.z);
                maxNorth = Mathf.Max(maxNorth, rule.NorthOfLine(p));
                // qa-2: peak horizontal speed, so a Sprint case that only walks shows up.
                if (Time.deltaTime > 0f) peakSpeed = Mathf.Max(peakSpeed, Flat(p - last).magnitude / Time.deltaTime);
                last = p;
            }

            if (mode == Approach.Fast50)
            {
                float t = 0f, dur = (pathLen + 10f) / FastSpeed;
                while (t < dur)
                {
                    cc.Move(dir * (FastSpeed * Time.deltaTime));
                    Sample();
                    t += Time.deltaTime;
                    yield return null;
                    Sample();
                }
                // One single 50 m step (a whole second at 50 m/s) to check for tunnelling; not a speed sample.
                cc.Move(dir * FastSpeed);
                last = player.transform.position;
                Sample();
                yield return null;
                Sample();
            }
            else
            {
                float dur = pathLen / speed + PushSeconds, t = 0f;
                int jumpedAt = -1; // face index already jumped at (0 = first face, 1 = next)
                // Shift before W: pressing W then Shift in the same InputTestFixture update loses W (harness quirk, probed
                // 2026-10-06; Shift after W in later frames sprints normally).
                if (speed > player.WalkSpeed) Press(keyboard.leftShiftKey);
                Press(keyboard.wKey);
                while (t < dur)
                {
                    if (mode == Approach.SprintJump)
                    {
                        // Jump so the apex lands on the next face ahead: the barrier, or the backstop's south face.
                        float toFace = NextFace(dir) - cc.radius;
                        int face = toFace < (rule.LineZAt(player.transform.position.x) - wall - player.transform.position.z) / Mathf.Max(0.01f, dir.z) - 1f ? 0 : 1;
                        if (keyboard.spaceKey.isPressed) Release(keyboard.spaceKey);
                        else if (face > jumpedAt && toFace <= jumpLead && player.IsGrounded) { Press(keyboard.spaceKey); jumpedAt = face; tally.jumps++; }
                    }
                    yield return null;
                    Sample();
                    t += Time.deltaTime;
                }
                ReleaseAllKeys();
            }

            int snaps = playerSnaps - snapsBefore;
            tally.snaps += snaps;
            tally.worstNorth = Mathf.Max(tally.worstNorth, maxNorth);
            if (maxNorth > 0f)
                tally.crossings.Add($"{testCase}#{index} x={x:F0} maxNorth={maxNorth:F2} clampSnaps={snaps}");
            // Valid: snapped, reached the face it started facing, or stopped against the barrier or backstop itself (a
            // see-through fence bay can let the start raycasts pass yet stop the capsule).
            string stopper = Stopper(dir, out string barrierHit);
            // qa-2 validity source: snap; what it is stopped against (backstop, or barrier:<collider path>, which shows
            // dressing vs fence); else reach (got to the face it started facing without a barrier hit ahead).
            string source = snaps > 0 ? "snap"
                : barrierHit != null ? (barrierHit.StartsWith("WorldBounds/") ? "backstop" : "barrier:" + barrierHit)
                : travel >= faceAhead - cc.radius - FaceSlack ? "reach"
                : null;
            res.valid = source != null;
            if (res.valid) tally.valid++;
            else res.why = $"stopped at z={maxZ:F1} by {stopper}, {faceAhead - travel:F1} m short of the first face";
            if (mode == Approach.Sprint || mode == Approach.SprintJump) tally.sprintPeaks.Add(peakSpeed);
            tally.runRows.Add($"{testCase},{index},{x:F1},{res.valid},\"{source ?? "-"}\",{maxZ:F2},\"{stopper}\",{faceAhead:F1},{travel:F1},{snaps},{peakSpeed:F2}");
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        void ReleaseAllKeys()
        {
            if (keyboard.wKey.isPressed) Release(keyboard.wKey);
            if (keyboard.leftShiftKey.isPressed) Release(keyboard.leftShiftKey);
            if (keyboard.spaceKey.isPressed) Release(keyboard.spaceKey);
        }
    }
}
