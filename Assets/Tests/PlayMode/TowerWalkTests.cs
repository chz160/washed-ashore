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
    /// Walks the lookout tower with the ordinary controller: spawn at the tower base (TowerBaseSpawn, which World.unity's
    /// Player uses after the WalkableTowerBuilder), hold W, steer by yaw toward each stair mouth in turn as a player
    /// would, never jump, reach the cab floor, then walk back down. Prints the times and, on a stall, where it stuck.
    /// Result is also written to Logs/tower-walk-test.txt.
    /// </summary>
    public class TowerWalkTests : InputTestFixture
    {
        const string LogFile = "Logs/tower-walk-test.txt";
        const float ReachXZ = 0.35f, ReachY = 0.25f, StallSeconds = 4f, StallMetres = 0.1f, LegLimitSeconds = 120f, TurnOut = 0.4f, ApproachBack = 0.3f, LookAhead = 0.6f;

        class Wp { public string name; public Vector3 at, from; public bool lane, stop; }

        Keyboard keyboard;

        public override void Setup()
        {
            base.Setup();
            keyboard = InputSystem.AddDevice<Keyboard>();
            InputSystem.AddDevice<Mouse>();
        }

        [UnityTest, Timeout(600000)]
        public IEnumerator WalkBaseToCabAndBack()
        {
            SceneManager.LoadScene("World", LoadSceneMode.Single);
            yield return null;
            yield return null;
            var go = GameObject.FindGameObjectWithTag("Player");
            Assert.IsNotNull(go, "no Player");
            var player = go.GetComponent<PlayerController>();
            var tower = GameObject.Find("LookoutTower");
            Assert.IsNotNull(tower, "LookoutTower is not in World.unity: run WalkableTowerBuilder");
            Assert.IsNull(Object.FindFirstObjectByType<ClimbArea>(), "a ClimbArea is in the scene: the guided climb must not take over");
            var tf = tower.GetComponentsInChildren<Transform>(true).ToDictionary(t => t.name, t => t, System.StringComparer.Ordinal);
            var body = go.GetComponent<CharacterController>();
            Vector3 P(string n) => tf[n].position;

            var up = new List<Wp>();
            var down = new List<Wp>();
            Vector3 Dir(int i) { Vector3 d = P($"LANE_F{i:00}_Head") - P($"LANE_F{i:00}_Foot"); d.y = 0f; return d.normalized; }
            // The hairpin at each landing goes round the end of the divider between the two flights, which is half way between
            // flight i's head and flight i+1's foot: aim a little way on past it in the direction flight i was travelling.
            Vector3 Turn(int i) => (P($"LANE_F{i:00}_Head") + P($"LANE_F{i + 1:00}_Foot")) * 0.5f + Dir(i) * TurnOut;
            Vector3 cab = tower.transform.TransformPoint(new Vector3(-0.5f, 26.34f, -0.5f));
            Vector3 entry = tower.transform.TransformPoint(new Vector3(0.62f, 26.34f, -0.70f));    // top of flight 8, in the hatch opening
            Vector3 edge = tower.transform.TransformPoint(new Vector3(-0.2f, 26.34f, -0.70f));    // on the cab floor beside the open hatch
            // A flight is 0.62 to 0.72 m between its walls and the capsule is 0.5: ease off and line up before each mouth, then
            // steer along the flight's centre line, as a player does.
            for (int i = 1; i <= 8; i++)
            {
                Vector3 foot = P($"LANE_F{i:00}_Foot"), head = P($"LANE_F{i:00}_Head");
                up.Add(new Wp { name = $"F{i:00} mouth", at = foot - Dir(i) * ApproachBack, stop = true });
                up.Add(new Wp { name = $"F{i:00} head", at = i == 8 ? head - Dir(i) * 0.1f : head, from = foot, lane = true });   // flight 8's head is against the cab wall
                if (i < 8) up.Add(new Wp { name = $"turn {i}", at = Turn(i) });
            }
            up.Add(new Wp { name = "cab entry", at = entry });
            up.Add(new Wp { name = "cab strip", at = edge });
            up.Add(new Wp { name = "cab floor", at = cab });
            down.Add(new Wp { name = "cab strip", at = edge });
            for (int i = 8; i >= 1; i--)
            {
                Vector3 foot = P($"LANE_F{i:00}_Foot"), head = P($"LANE_F{i:00}_Head");
                down.Add(new Wp { name = $"F{i:00} top mouth", at = i == 8 ? entry : head + Dir(i) * ApproachBack, stop = true });
                down.Add(new Wp { name = $"F{i:00} foot", at = foot, from = head, lane = true });
                if (i > 1) down.Add(new Wp { name = $"turn {i - 1}", at = Turn(i - 1) });
            }
            down.Add(new Wp { name = "base", at = new Vector3(go.transform.position.x, P("LANE_F01_Foot").y, go.transform.position.z) });

            Vector3 start = go.transform.position;
            var report = new StringBuilder($"start {start:F2}, cab floor world y {cab.y:F2}, walkSpeed {player.WalkSpeed}\n");
            // Spec T7.1 / Q5 (td adr/lookout-interactions-40 part 11): the hatch starts CLOSED, so it is opened through the real TrapDoor state path before the timer
            // starts; the 26.6 s +-10% bar then measures walking with the hatch open.
            var hatchDoor = tower.GetComponentInChildren<TrapDoor>(true);
            Assert.IsNotNull(hatchDoor, "the tower has no TrapDoor: run the build step");
            hatchDoor.SetState(HingeState.Open, 0f);
            Assert.AreEqual(HingeState.Open, hatchDoor.State, "the hatch is open before the walk");
            report.AppendLine($"hatch opened before the timer: state {hatchDoor.State}, angle {hatchDoor.Angle:F2} at scene time {Time.time:F2}s");
            keyboard = Keyboard.current;
            Press(keyboard.wKey);
            bool held = true;
            string failure = null;
            float upTime = 0f, downTime = 0f;
            int downFrames = 0, airFrames = 0; float curAir = 0f, maxAir = 0f, maxRise = 0f, stopDrift = -1f, prevY = 0f, groundSpeed = 0f; string riseAt = "";
            bool stopTested = false; var win = new List<(float t, Vector3 p)>();

            foreach (var leg in new[] { ("UP", up), ("DOWN", down) })
            {
                float t0 = Time.time;
                report.AppendLine($"{leg.Item1} timer starts at scene time {t0:F2}s, hatch {hatchDoor.State}");
                int index = 0;
                float lastBest = float.MaxValue, lastProgress = Time.time;
                Vector3 lastPos = go.transform.position;
                while (index < leg.Item2.Count)
                {
                    var w = leg.Item2[index];
                    Vector3 pos = go.transform.position;
                    float speed = (pos - lastPos).magnitude / Mathf.Max(Time.deltaTime, 1e-4f);
                    lastPos = pos;
                    pos.y = body.bounds.min.y;   // feet
                    Vector3 flat = w.at - pos; flat.y = 0f;
                    if (leg.Item1 == "UP" && Mathf.Abs(tower.transform.InverseTransformPoint(pos).y) < 0.5f)
                        { win.Add((Time.time, pos)); while (win.Count > 1 && Time.time - win[0].t > 0.25f) win.RemoveAt(0); float span = Time.time - win[0].t; if (span > 0.2f) groundSpeed = Mathf.Max(groundSpeed, new Vector2(pos.x - win[0].p.x, pos.z - win[0].p.z).magnitude / span); }   // on the ground in and around the tower, off the stairs
                    if (leg.Item1 == "DOWN")
                    {
                        downFrames++;
                        if (!player.IsGrounded) { airFrames++; curAir += Time.deltaTime; maxAir = Mathf.Max(maxAir, curAir); } else curAir = 0f;
                        if (downFrames > 1 && pos.y - prevY > maxRise) { maxRise = pos.y - prevY; riseAt = tower.transform.InverseTransformPoint(pos) + " going to " + w.name; }
                        prevY = pos.y;
                    }
                    if (leg.Item1 == "UP" && w.name == "F03 head" && !stopTested && (w.at - pos).magnitude < 1.5f)
                    {
                        // Let go of W on a flight: the player must stay where they are.
                        stopTested = true; Release(keyboard.wKey); held = false;
                        Vector3 p0 = go.transform.position; float te = Time.time;
                        while (Time.time - te < 1.0f) yield return null;
                        stopDrift = (go.transform.position - p0).magnitude;
                        Press(keyboard.wKey); held = true;
                        report.AppendLine($"  released W on flight 3 for 1 s: moved {stopDrift:F3} m");
                    }
                    float reach = w.stop ? 0.2f : w.lane ? 0.2f : ReachXZ;
                    if (flat.magnitude < reach && Mathf.Abs(w.at.y - pos.y) < ReachY)
                    {
                        report.AppendLine($"  {leg.Item1} reached {w.name} at t={Time.time - t0:F1}s local {tower.transform.InverseTransformPoint(pos):F2}");
                        index++; lastBest = float.MaxValue; lastProgress = Time.time;
                        continue;
                    }
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
                    if (flat.sqrMagnitude > 1e-4f) go.transform.rotation = Quaternion.LookRotation(flat.normalized, Vector3.up);
                    bool wantHeld = !(w.stop && flat.magnitude < 1.0f && speed > 1.2f);
                    if (wantHeld && !held) Press(keyboard.wKey); else if (!wantHeld && held) Release(keyboard.wKey);
                    held = wantHeld;
                    float d = (w.at - pos).magnitude;
                    if (d < lastBest - StallMetres) { lastBest = d; lastProgress = Time.time; }
                    if (Time.time - lastProgress > StallSeconds || Time.time - t0 > LegLimitSeconds * 3)
                    {
                        failure = $"{leg.Item1}: stuck going to '{w.name}' at local {tower.transform.InverseTransformPoint(pos):F2} (target local {tower.transform.InverseTransformPoint(w.at):F2}) after {Time.time - t0:F1}s; grounded {player.IsGrounded}; " +
                                  "touching: " + string.Join(", ", Physics.OverlapSphere(pos + Vector3.up * 0.9f, 0.7f, ~0, QueryTriggerInteraction.Ignore).Select(c => c.name).Distinct());
                        break;
                    }
                    yield return null;
                }
                float took = Time.time - t0;
                if (leg.Item1 == "UP") upTime = took; else downTime = took;
                report.AppendLine($"{leg.Item1}: {(failure == null ? "completed" : "FAILED")} in {took:F1}s");
                if (failure != null) break;
            }
            Release(keyboard.wKey);
            if (failure != null) report.AppendLine(failure);
            report.AppendLine($"descent: {airFrames} of {downFrames} frames airborne, longest airborne {maxAir:F2}s, largest per-frame rise {maxRise:F3} m at {riseAt}; ground speed beside the tower {groundSpeed:F1} m/s");
            if (failure == null)
            {
                if (upTime < 20f || upTime > 40f) failure = $"up time {upTime:F1}s outside 20-40 s";
                else if (groundSpeed < player.WalkSpeed * 0.8f) failure = $"ground speed beside the tower only reached {groundSpeed:F1} m/s of {player.WalkSpeed}";
                else if (stopDrift < 0f || stopDrift > 0.05f) failure = $"player drifted {stopDrift:F3} m in 1 s after releasing W on a flight";
                else if (maxAir > 0.15f || airFrames > downFrames * 0.05f) failure = $"descent not grounded: {airFrames}/{downFrames} frames airborne, longest {maxAir:F2}s";
                else if (maxRise > 0.06f) failure = $"descent bounced: largest per-frame rise {maxRise:F3} m";
                if (failure != null) report.AppendLine("ASSERT: " + failure);
            }
            report.AppendLine($"RESULT up {upTime:F1}s down {downTime:F1}s {(failure == null ? "PASS" : "FAIL")}");
            Directory.CreateDirectory("Logs");
            File.WriteAllText(LogFile, report.ToString());
            Debug.Log(report.ToString());
            Assert.IsNull(failure, failure);
        }
    }
}
