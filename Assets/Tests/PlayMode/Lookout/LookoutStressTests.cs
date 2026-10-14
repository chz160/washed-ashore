using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;
using WashedAshore.Gameplay;
using WashedAshore.Lookout;

namespace WashedAshore.Tests.PlayMode.Lookout
{
    /// <summary>
    /// Spec C2 (tower-design-spec.md §9 T-C2a, T-C2b; tower-qa-plan.md C2): on every flight, every landing and in the
    /// cab, strafe, sprint and jump with the view sweeping round; 0 escapes, 0 stuck states, 0 launches. Then the
    /// same landing and cab patterns with the invisible walls off, where the detector has to report escapes.
    /// Getting from each station to the next after it is the stuck check: W toward a valid exit must keep making progress.
    /// </summary>
    public class LookoutStressTests : LookoutFixture
    {
        const float JumpEvery = 0.25f;
        static readonly int[] Seeds = { 11, 22, 33 };
        static readonly string[] WallWords = { "Landing", "Flight", "Cab" };

        enum Pattern { StrafeLeft, StrafeRight, SprintForward, SprintBack, JumpSpam, Everything }

        /// <summary>One input pattern for <paramref name="seconds"/>, with the view sweeping a full turn (direction from the seed).</summary>
        IEnumerator Run(Pattern pattern, float seconds, System.Random rng)
        {
            LetAll();
            yield return Face(Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f) * Vector3.forward);
            float sign = rng.Next(2) == 0 ? 1f : -1f, before = player.Yaw;
            Set(mouse.delta, new Vector2(1f, 0f), queueEventOnly: true);
            yield return null;
            yield return null;
            float perCount = Mathf.Max(Mathf.Abs(Mathf.DeltaAngle(before, player.Yaw)), 1e-4f);
            if (pattern == Pattern.StrafeLeft || pattern == Pattern.Everything) Hold(Key.A);
            if (pattern == Pattern.StrafeRight) Hold(Key.D);
            if (pattern == Pattern.SprintForward || pattern == Pattern.SprintBack || pattern == Pattern.Everything) Hold(Key.LeftShift);
            if (pattern == Pattern.SprintForward || pattern == Pattern.Everything) Hold(Key.W);
            if (pattern == Pattern.SprintBack) Hold(Key.S);
            bool jumps = pattern == Pattern.JumpSpam || pattern == Pattern.Everything;
            float nextJump = 0f;
            for (float t = 0f; t < seconds; t += Time.deltaTime)
            {
                Set(mouse.delta, new Vector2(sign * 360f / seconds * Time.deltaTime / perCount, 0f), queueEventOnly: true);
                if (jumps && t >= nextJump)
                {
                    Press(keyboard.spaceKey);
                    yield return null;
                    Release(keyboard.spaceKey);
                    nextJump = t + JumpEvery;
                }
                yield return null;
            }
            LetAll();
            yield return null;
        }

        /// <summary>The floor the player stands on: the flight whose group hands over at this height nearest to them (-1 = the ground at the first foot).</summary>
        int LandingIndex()
        {
            Vector3 p = player.transform.position;
            int best = -1;
            float bestD = Vector3.Distance(p, GroupFoot(Group(0)));
            for (int f = 0; f < FlightCount; f++)
            {
                Vector3 top = GroupHead(Group(f));
                float d = Vector3.Distance(p, top);
                if (Mathf.Abs(p.y - top.y) < capsule.stepOffset && d < bestD) { best = f; bestD = d; }
            }
            return best;
        }

        /// <summary>The flight whose group a lane belongs to, and the lane's place in that group going up.</summary>
        int FlightOf(int lane, out int place)
        {
            for (int f = 0; f < FlightCount; f++)
            {
                place = Group(f).IndexOf(lane);
                if (place >= 0) return f;
            }
            place = -1;
            Assert.Fail($"lane {lane + 1} is in no flight's group");
            return -1;
        }

        /// <summary>
        /// Walks to the middle of flight <paramref name="target"/>, or onto the floor above it, from wherever a pattern
        /// left the player, through the Input System only (W and mouse yaw). Progress is checked against the stuck rule all the way.
        /// </summary>
        IEnumerator GoTo(int target, bool toLanding)
        {
            float progressAt = Time.time;
            Vector3 progressFrom = player.transform.position;
            for (int guard = 0; guard < 600; guard++)
            {
                if (hatch.State == HingeState.Closed && player.OnLane && player.LaneIndex == hatch.LaneIndex) yield return OpenHatchFromBelow();
                LetAll();
                if (player.OnLane)
                {
                    int on = player.LaneIndex;
                    int flight = FlightOf(on, out int place);
                    var group = Group(flight);
                    StairLane lane = area.Lane(on);
                    bool onTarget = on == target;
                    bool up = flight < target || (flight == target && (toLanding || place < group.IndexOf(target) || (onTarget && player.LaneDistance < lane.Length * 0.5f)));
                    if (onTarget && !toLanding && Mathf.Abs(player.LaneDistance - lane.Length * 0.5f) < climb.laneTaperLength) yield break;
                    // Held at a junction: the way on is along the linked lane, away from the junction.
                    int nextPlace = up ? place + 1 : place - 1;
                    if (AtJunction(on, up) && nextPlace >= 0 && nextPlace < group.Count)
                    {
                        Vector3 alongNext = area.Lane(group[nextPlace]).AxisHorizontal;
                        Hold(Key.W);
                        yield return Face(up ? alongNext : -alongNext);
                        for (float t = 0f; t < LookoutTargets.StuckSeconds && player.OnLane && player.LaneIndex == on; t += Time.deltaTime) yield return null;
                        continue;
                    }
                    yield return Face(up ? lane.AxisHorizontal : -lane.AxisHorizontal);
                    Hold(Key.W);
                    float want = lane.Length * 0.5f;
                    for (float t = 0f; t < LookoutTargets.StuckSeconds; t += Time.deltaTime)
                    {
                        if (!player.OnLane || player.LaneIndex != on || AtJunction(on, up)) break;
                        if (onTarget && !toLanding && (up ? player.LaneDistance >= want : player.LaneDistance <= want)) break;
                        yield return null;
                    }
                }
                else
                {
                    int landing = LandingIndex();
                    if (toLanding && landing == target) yield break;
                    bool up = landing < target;
                    int next = up ? landing + 1 : landing;
                    Assert.That(next, Is.InRange(0, FlightCount - 1), $"no flight to take from floor {landing} toward {target}");
                    var group = Group(next);
                    int entry = up ? group[0] : group[group.Count - 1];
                    yield return WalkTo(up ? GroupFoot(group) : GroupHead(group), LookoutTargets.StuckSeconds);
                    yield return Face(up ? area.Lane(entry).AxisHorizontal : -area.Lane(entry).AxisHorizontal);
                    Hold(Key.W);
                    for (float t = 0f; t < LookoutTargets.StuckSeconds && !player.OnLane; t += Time.deltaTime) yield return null;
                }
                // Stuck: under StuckProgress of movement in StuckSeconds with W held toward a valid exit.
                if (Vector3.Distance(player.transform.position, progressFrom) >= LookoutTargets.StuckProgress)
                {
                    progressFrom = player.transform.position;
                    progressAt = Time.time;
                }
                else if (Time.time - progressAt > LookoutTargets.StuckSeconds * 2f)
                    Assert.Fail($"stuck: under {LookoutTargets.StuckProgress} m of progress since t={progressAt:F1} heading for flight {target + 1} (landing={toLanding}) at {player.transform.position:F2}, " +
                                $"lane {player.LaneIndex}, u {player.LaneDistance:F2}, hatch {hatch.State}. {watch.Summary()}");
            }
            Assert.Fail($"never reached flight {target + 1} (landing={toLanding}). {watch.Summary()}");
        }

        IEnumerator AllPatterns(string station, System.Random rng, List<string> rows)
        {
            foreach (Pattern pattern in System.Enum.GetValues(typeof(Pattern)))
            {
                int escapes = watch.Escapes, launches = watch.Launches, steps = watch.StepBreaks;
                yield return Run(pattern, LookoutTargets.StressSecondsPerPattern, rng);
                rows.Add($"{{\"station\":\"{station}\",\"pattern\":\"{pattern}\",\"escapes\":{watch.Escapes - escapes},\"launches\":{watch.Launches - launches},\"stepBreaks\":{watch.StepBreaks - steps}}}");
            }
        }

        /// <summary>What one seed's pass leaves behind, for the determinism check.</summary>
        class Pass { public string Violations, End; }

        static readonly Dictionary<int, Pass> passes = new Dictionary<int, Pass>();

        /// <summary>T-C2a: every flight, every landing and the cab, six patterns each; and the sprint run-up into the first flight. The three seeds are the three runs.</summary>
        [UnityTest]
        public IEnumerator A_StrafeSprintJump_OnEveryFlightAndLanding_NoEscapeStuckOrLaunch([ValueSource(nameof(Seeds))] int seed)
        {
            var pass = new Pass();
            yield return StressPass(seed, pass);
            passes[seed] = pass;
        }

        /// <summary>T-C2a determinism: the first seed again in the same session gives the same violation log and the same end position.</summary>
        [UnityTest]
        public IEnumerator A2_FirstSeedAgain_IsIdentical()
        {
            int seed = Seeds[0];
            if (!passes.TryGetValue(seed, out Pass first))
            {
                first = new Pass();
                yield return StressPass(seed, first);
            }
            var second = new Pass();
            yield return StressPass(seed, second);
            Assert.AreEqual(first.Violations, second.Violations, "violation log differs between two passes of the same seed");
            Assert.AreEqual(first.End, second.End, "end position differs between two passes of the same seed");
        }

        IEnumerator StressPass(int seed, Pass pass)
        {
            var rng = new System.Random(seed);
            var rows = new List<string>();
            yield return LoadTower();
            yield return StartAtBase();

            // Sprint run-up from outside the area straight into the first flight (t-qa: real sprint speed at the entry).
            yield return Face(Flat(area.Lane(0).foot - player.transform.position));
            Hold(Key.LeftShift);
            Hold(Key.W);
            yield return Until(() => player.OnLane, 15f, "sprint into the first flight");
            watch.ExpectOnTower = true;
            yield return Wait(2f);
            LetAll();
            Assert.IsTrue(player.OnLane && player.LaneIndex == 0, "on the first flight after the sprint run-up");

            int flights = FlightCount;
            for (int i = 0; i < flights; i++)
            {
                yield return GoTo(i, toLanding: false);
                yield return AllPatterns($"flight{i + 1}", rng, rows);
                yield return GoTo(i, toLanding: true);
                yield return AllPatterns(i == flights - 1 ? "cab" : $"landing{i + 1}", rng, rows);
            }
            // Still able to leave: all the way down and off the first tread.
            yield return GoTo(0, toLanding: false);
            yield return Face(-area.Lane(0).AxisHorizontal);
            Hold(Key.W);
            yield return Until(() => !player.OnLane && player.ClimbArea == null, area.Lane(0).Length / climb.stairDescendSpeed + 10f, "walk off the first tread");
            watch.ExpectOnTower = false;
            LetAll();
            yield return null;
            pass.Violations = string.Join("\n", watch.Violations);
            pass.End = player.transform.position.ToString("F3");
            string lateral = string.Join(",", System.Array.ConvertAll(watch.MaxLateralByLane, v => v.ToString("F4")));
            Evidence($"c2-stress-seed{seed}.json", $"{{\"seed\":{seed},\"secondsPerPattern\":{LookoutTargets.StressSecondsPerPattern},\"joinsFromAbove\":{player.LaneCatches},\"maxLateralByFlight\":[{lateral}]," +
                                                   $"\"end\":\"{pass.End}\",\"watch\":\"{watch.Summary()}\",\"joins\":[{string.Join(",", watch.Joins)}],\"rows\":[{string.Join(",", rows)}]}}");
            Assert.AreEqual(0, watch.Escapes, "escapes\n" + pass.Violations);
            Assert.AreEqual(0, watch.Launches, "launches\n" + pass.Violations);
            Assert.AreEqual(LookoutTargets.JoinsFromAboveAllowed, player.LaneCatches, "joins from above during the stress pass");
            // The lane holds the player by construction: measured, per flight, under strafe and sprint input.
            Assert.AreEqual(area.LaneCount, watch.MaxFlightLateralByLane.Length, "every lane was stood on");
            for (int i = 0; i < FlightCount; i++)
                Assert.LessOrEqual(watch.MaxFlightLateralByLane[i], watch.AuthoredSlack + LookoutTargets.LaneLateralMargin, $"largest sideways offset on flight {i + 1}");
            AssertClean($"stress seed {seed}");
        }

        /// <summary>The detector's twin: with the invisible landing, flight-side and cab walls off, the same landing patterns must report escapes.</summary>
        [UnityTest]
        public IEnumerator B_Twin_WithTheWallsOff_TheDetectorReportsEscapes()
        {
            var rng = new System.Random(Seeds[0]);
            yield return LoadTower();
            yield return StartAtBase();
            yield return WalkTo(area.Lane(0).Point(-ClimbRules.FootRun(area.Lane(0), climb) * 0.5f, 0f), 15f);
            var log = new ClimbLog();
            yield return ClimbUpTo(0, log);
            int off = 0;
            foreach (var c in tower.GetComponentsInChildren<Collider>())
            {
                if (!c.name.StartsWith(LookoutTargets.ClipPrefix)) continue;
                foreach (var word in WallWords)
                    if (c.name.Contains(word)) { c.enabled = false; off++; break; }
            }
            Assert.Greater(off, 0, $"no {LookoutTargets.ClipPrefix} walls found to switch off");
            int before = watch.Escapes;
            foreach (Pattern pattern in new[] { Pattern.SprintForward, Pattern.StrafeLeft, Pattern.StrafeRight, Pattern.SprintBack })
            {
                if (watch.Escapes > before) break;
                yield return Run(pattern, LookoutTargets.StressSecondsPerPattern, rng);
            }
            Debug.Log($"LookoutStressTests: twin with {off} walls off: {watch.Escapes - before} escape frames. {watch.Summary()}");
            Evidence("c2-twin.json", $"{{\"wallsOff\":{off},\"escapeFrames\":{watch.Escapes - before},\"watch\":\"{watch.Summary()}\"}}");
            Assert.Greater(watch.Escapes, before, "with the walls off the detector must report an escape from the first landing");
        }

        /// <summary>Climbs from the first foot onto the landing above lane <paramref name="lane"/>.
        IEnumerator ClimbUpTo(int lane, ClimbLog log)
        {
            yield return Face(area.Lane(0).AxisHorizontal);
            Hold(Key.W);
            yield return Until(() => player.OnLane, 5f, "join the first lane");
            watch.ExpectOnTower = true;
            LetAll();
            yield return GoTo(lane, toLanding: true);
        }

        /// <summary>T-C2b (R9): from the ground, walking and jumping at the first flight's sides never gets the player onto a lane or up the tower.</summary>
        [UnityTest]
        public IEnumerator C_FromTheGround_TheFlightCannotBeEnteredFromItsSide()
        {
            yield return LoadTower();
            yield return StartAtBase();
            StairLane first = area.Lane(0);
            float ground = player.transform.position.y;
            foreach (float u in new[] { first.Length * 0.15f, first.Length * 0.3f })
                foreach (float side in new[] { -1f, 1f })
                {
                    Vector3 beside = first.Point(u, side * (first.width + capsule.radius * 4f));
                    beside.y = ground;
                    yield return WalkTo(first.Point(-LookoutTargets.RadiusRestoreClear, side * (first.width + capsule.radius * 4f)), 15f);
                    yield return WalkTo(beside, 15f);
                    yield return Face(-side * first.Right);
                    Hold(Key.W);
                    float nextJump = 0f;
                    for (float t = 0f; t < LookoutTargets.GroundProbeSeconds; t += Time.deltaTime)
                    {
                        if (t >= nextJump) { Press(keyboard.spaceKey); yield return null; Release(keyboard.spaceKey); nextJump = t + JumpEvery; }
                        Assert.IsFalse(player.OnLane, $"joined a lane from its side at u={u:F1}, side {side}");
                        Assert.IsNull(player.ClimbArea, "climb state from the ground beside a flight");
                        Assert.LessOrEqual(player.transform.position.y, ground + LookoutTargets.GroundPlayerMaxRise, $"rose above ground + {LookoutTargets.GroundPlayerMaxRise} m at u={u:F1}, side {side}");
                        yield return null;
                    }
                    LetAll();
                    yield return null;
                }
            Assert.AreEqual(player.AuthoredRadius, capsule.radius, 0f, "authored radius on the ground");
        }
    }
}
