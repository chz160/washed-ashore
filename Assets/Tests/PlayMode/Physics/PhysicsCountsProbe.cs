using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;
using Unity.Profiling;
using Unity.Profiling.LowLevel;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using WashedAshore.Gameplay;

namespace WashedAshore.Tests.PlayMode
{
    /// <summary>
    /// GitHub #43 L5: Physics Profiler COUNTS (not times) at fixed spots, in Editor PlayMode. Explicit: run by name only.
    /// Output: TestResults/physics-layers-43/$PL43_PHASE/physics-counts.csv, physics-counters.txt, control-counters.txt, control-raw.csv.
    /// Label for any report: "Editor PlayMode counts, not player timings".
    /// td addendum 5: ground height from TerrainQuery.TryGroundHeight (all tiles); per-spot grounded/position check at end of settle
    /// and end of sampling, a respawn voids the spot; the positive control crate must land and rest before its counters count.
    /// </summary>
    public class PhysicsCountsProbe
    {
        const int SampleFrames = 90;
        const float Yaw = 0f;
        const float HorizTol = 0.5f, VertTol = 0.3f;
        static long[] controlMax;
        static int fellCount;
        static string controlFail;
        static string lastYTest = "";
        static readonly List<string> voids = new List<string>();

        class Spot
        {
            public string Name; public Vector3 Pos;
            public bool AuthoredOnly;     // floor height is the authored y (cab), not terrain
            public bool AlsoAuthored;     // accept the authored y as well as the terrain (spawn objects may sit on a structure)
        }

        static void OnLog(string msg, string stack, LogType type)
        {
            if (msg != null && msg.Contains("fell below terrain")) fellCount++;
        }

        [UnityTest, Explicit("#43 L5 physics counts probe"), Timeout(1800000)]
        public IEnumerator PhysicsCounts_AtThePerfSpots()
        {
            string phase = Environment.GetEnvironmentVariable("PL43_PHASE");
            if (string.IsNullOrEmpty(phase)) phase = "unset";
            string dir = "TestResults/physics-layers-43/" + phase;
            Directory.CreateDirectory(dir);
            voids.Clear(); fellCount = 0; controlFail = null; controlMax = null;
            Application.logMessageReceived += OnLog;

            SceneManager.LoadScene("World", LoadSceneMode.Single);
            yield return null;
            yield return null;
            var playerGo = GameObject.FindGameObjectWithTag("Player");
            Assert.IsNotNull(playerGo, "no Player");
            var cc = playerGo.GetComponent<CharacterController>();
            Assert.IsNotNull(cc, "no CharacterController");
            var pc = cc.GetComponent<PlayerController>();
            Assert.IsNotNull(pc, "no PlayerController");

            var handles = new List<ProfilerRecorderHandle>();
            ProfilerRecorderHandle.GetAvailable(handles);
            var recorders = new List<ProfilerRecorder>();
            var names = new List<string>();
            var info = new StringBuilder();
            foreach (var h in handles)
            {
                var d = ProfilerRecorderHandle.GetDescription(h);
                if (d.Category.Name != ProfilerCategory.Physics.Name) continue;
                if (d.UnitType == ProfilerMarkerDataUnit.TimeNanoseconds) continue;   // counts only: no timings
                names.Add(d.Name);
                recorders.Add(ProfilerRecorder.StartNew(ProfilerCategory.Physics, d.Name, 1));
                info.AppendLine($"{d.Category.Name} | {d.Name} | unit {d.UnitType}");
            }
            File.WriteAllText(dir + "/physics-counters.txt", info.ToString());
            Debug.Log("PhysicsCountsProbe: counters exposed (" + names.Count + ")\n" + info);
            Assert.Greater(names.Count, 0, "the Editor exposes no Physics count counters");

            float savedCapture = Time.captureDeltaTime;
            Time.captureDeltaTime = 1f / 60f;   // one frame = 1/60 s of game time, so 90 sampled frames are 1.5 s of physics
            for (int i = 0; i < 120; i++) yield return null;   // warm-up: scene-load broadphase adds must not land in the first spot

            var spots = new List<Spot>();
            Spot Terr(string n, Vector3 p)
            {
                if (!TerrainQuery.TryGroundHeight(p, out float g)) { Debug.Log("PhysicsCountsProbe: " + n + ": not measured (no ground reading at " + p + ")"); voids.Add(n + ": not measured (no ground reading)"); return null; }
                return new Spot { Name = n, Pos = new Vector3(p.x, g + 0.1f, p.z) };
            }
            void Add(Spot s) { if (s != null) spots.Add(s); }
            var spawn = GameObject.Find("PlayerSpawn");
            var centre = GameObject.Find("LM_OutdoorCenter");
            var tower = GameObject.Find("TowerBaseSpawn");
            Assert.IsNotNull(spawn, "no PlayerSpawn");
            spots.Add(new Spot { Name = "Spawn", Pos = spawn.transform.position, AlsoAuthored = true });
            Add(Terr("RiverBank", new Vector3(993.4f, 0f, 53.8f)));
            if (centre) Add(Terr("ValleyFloor_LM_OutdoorCenter", centre.transform.position));
            Vector3 ridge = Ridge(centre ? centre.transform.position : spawn.transform.position);
            Add(Terr("RidgeTop_highest_ground_within_1500m", ridge));
            if (tower) spots.Add(new Spot { Name = "LookoutBase_TowerBaseSpawn", Pos = tower.transform.position, AlsoAuthored = true });
            // Cab: the TowerWalkTests fixture pose (tower-local -0.5, 26.34, -0.5), the cab floor. Not found = named, not silently dropped.
            var lookout = GameObject.Find("LookoutTower");
            if (lookout) spots.Add(new Spot { Name = "Cab_TowerWalkTests_pose", Pos = lookout.transform.TransformPoint(new Vector3(-0.5f, 26.34f, -0.5f)), AuthoredOnly = true });
            else { Debug.Log("PhysicsCountsProbe: Cab: not measured"); File.AppendAllText(dir + "/physics-counters.txt", "Cab: not measured (no LookoutTower)\n"); voids.Add("Cab: not measured (no LookoutTower)"); }
            // Route stops: PlayerSpawn -> LM_OutdoorCenter -> TowerBaseSpawn, 8 stops.
            if (centre && tower)
                for (int i = 0; i <= 7; i++)
                {
                    float t = i / 7f;
                    Vector3 p = t < 0.5f ? Vector3.Lerp(spawn.transform.position, centre.transform.position, t * 2f) : Vector3.Lerp(centre.transform.position, tower.transform.position, (t - 0.5f) * 2f);
                    Add(Terr("Route_" + i, p));
                }

            var csv = new StringBuilder("spot,x,y,z,yaw,counter,mean,max,min,last,status\n");
            var log = new StringBuilder();
            foreach (var s in spots)
                yield return Measure(s, cc, pc, recorders, names, csv, log);
            File.WriteAllText(dir + "/spot-checks.txt", log.ToString());
            Debug.Log("PhysicsCountsProbe: spot checks\n" + log);

            // Positive control: a non-kinematic crate dropped next to the spawn must land, rest, and move the counters.
            Vector3 sp = spawn.transform.position;
            Vector3 cp = new Vector3(sp.x + 3f, 0f, sp.z);
            bool haveGround = TerrainQuery.TryGroundHeight(cp, out float cground);
            if (haveGround)
            {
                var crate = GameObject.CreatePrimitive(PrimitiveType.Cube);
                crate.name = "PL43_PositiveControlCrate";
                var rb = crate.AddComponent<Rigidbody>();
                rb.isKinematic = false;
                crate.transform.position = new Vector3(cp.x, cground + 2f, cp.z);
                yield return Control(crate, rb, cground, sp, cc, pc, recorders, names, csv, dir);
                UnityEngine.Object.Destroy(crate);
            }
            Time.captureDeltaTime = savedCapture;
            Application.logMessageReceived -= OnLog;

            File.WriteAllText(dir + "/physics-counts.csv", csv.ToString());
            var moved = new StringBuilder();
            for (int i = 0; i < names.Count; i++)
                moved.AppendLine($"{(controlMax != null && controlMax[i] > 0 ? "MOVED_IN_CONTROL" : "zero in control")} | {names[i]} | control max {(controlMax != null ? controlMax[i] : -1)}");
            File.AppendAllText(dir + "/control-counters.txt", moved.ToString());
            Debug.Log("PhysicsCountsProbe: control\n" + moved);
            foreach (var r in recorders) r.Dispose();
            File.WriteAllText(dir + "/voids.txt", voids.Count == 0 ? "none\n" : string.Join("\n", voids) + "\n");
            Debug.Log("PhysicsCountsProbe: wrote " + dir + "/physics-counts.csv; voided/not measured: " + (voids.Count == 0 ? "none" : string.Join(" ;; ", voids)));

            Assert.IsTrue(haveGround, "control: no ground reading at the crate spot " + cp);
            Assert.IsNull(controlFail, controlFail);
            Assert.AreEqual(0, voids.Count, "voided or unmeasured spots: " + string.Join(" ;; ", voids));
        }

        // Grounded, in place and at ground height; null = good, else the reason.
        static string Check(Spot s, PlayerController pc, CharacterController cc)
        {
            Vector3 p = cc.transform.position;
            float dxz = new Vector2(p.x - s.Pos.x, p.z - s.Pos.z).magnitude;
            bool haveG = TerrainQuery.TryGroundHeight(p, out float g);
            bool yOk = s.AuthoredOnly ? Mathf.Abs(p.y - s.Pos.y) <= VertTol + 0.1f : (haveG && Mathf.Abs(p.y - g) <= VertTol);
            lastYTest = s.AuthoredOnly ? (yOk ? "y test passed: authored" : "y test failed: authored") : (yOk ? "y test passed: terrain" : "y test failed: terrain");
            if (!yOk && s.AlsoAuthored)
            {
                yOk = Mathf.Abs(p.y - s.Pos.y) <= VertTol + 0.1f;
                lastYTest = yOk ? "y test passed: authored (terrain test failed)" : "y test failed: terrain and authored";
            }
            var why = new List<string>();
            if (!pc.IsGrounded) why.Add("not grounded");
            if (dxz > HorizTol) why.Add($"horizontal {dxz:F2} m from spot");
            if (!yOk) why.Add($"y {p.y:F2} vs ground {(haveG ? g.ToString("F2") : "none")} authored {s.Pos.y:F2}");
            return why.Count == 0 ? null : string.Join(", ", why);
        }

        static string State(PlayerController pc, CharacterController cc)
        {
            Vector3 p = cc.transform.position;
            bool haveG = TerrainQuery.TryGroundHeight(p, out float g);
            return $"pos ({p.x:F2},{p.y:F2},{p.z:F2}) grounded {pc.IsGrounded} ground {(haveG ? g.ToString("F2") : "none")}";
        }

        static IEnumerator Measure(Spot s, CharacterController cc, PlayerController pc, List<ProfilerRecorder> recs, List<string> names, StringBuilder csv, StringBuilder log)
        {
            int fell0 = fellCount;
            cc.enabled = false;
            cc.transform.SetPositionAndRotation(s.Pos, Quaternion.Euler(0f, Yaw, 0f));   // yaw fixed at 0 for every spot, BEFORE and AFTER
            cc.enabled = true;
            pc.SetLook(Yaw, 0f);
            for (int i = 0; i < 60; i++) yield return null;   // settle onto the ground
            string c1 = Check(s, pc, cc);
            log.AppendLine($"{s.Name} target ({s.Pos.x:F2},{s.Pos.y:F2},{s.Pos.z:F2}) settle-end {State(pc, cc)} check {(c1 ?? "OK")} [{lastYTest}]");
            var sum = new double[recs.Count];
            var max = new long[recs.Count];
            var min = new long[recs.Count];
            var last = new long[recs.Count];
            for (int i = 0; i < min.Length; i++) { min[i] = long.MaxValue; max[i] = long.MinValue; }
            for (int f = 0; f < SampleFrames; f++)
            {
                yield return null;
                for (int i = 0; i < recs.Count; i++)
                {
                    long v = recs[i].Valid ? recs[i].LastValue : -1;
                    last[i] = v;
                    sum[i] += v; if (v > max[i]) max[i] = v; if (v < min[i]) min[i] = v;
                }
            }
            string c2 = Check(s, pc, cc);
            string status = "OK";
            var bad = new List<string>();
            if (c1 != null) bad.Add("settle: " + c1);
            if (c2 != null) bad.Add("sampling-end: " + c2);
            if (fellCount != fell0) bad.Add("respawn (fell below terrain) in window");
            if (bad.Count > 0) { status = "VOID " + string.Join(" | ", bad); voids.Add(s.Name + ": " + status); }
            log.AppendLine($"{s.Name} sample-end {State(pc, cc)} check {(c2 ?? "OK")} [{lastYTest}] status {status}");
            Vector3 at = s.Pos;
            string st = status.Replace(',', ';');
            for (int i = 0; i < recs.Count; i++)
                csv.AppendLine($"{s.Name},{at.x:F1},{at.y:F1},{at.z:F1},yaw{Yaw:F0},{names[i]},{sum[i] / SampleFrames:F2},{max[i]},{min[i]},{last[i]},{st}");
        }

        static IEnumerator Control(GameObject crate, Rigidbody rb, float ground, Vector3 spawnPos, CharacterController cc, PlayerController pc, List<ProfilerRecorder> recs, List<string> names, StringBuilder csv, string dir)
        {
            cc.enabled = false;
            cc.transform.SetPositionAndRotation(spawnPos, Quaternion.Euler(0f, Yaw, 0f));
            cc.enabled = true;
            pc.SetLook(Yaw, 0f);
            var sum = new double[recs.Count];
            var max = new long[recs.Count];
            var min = new long[recs.Count];
            for (int i = 0; i < min.Length; i++) { min[i] = long.MaxValue; max[i] = long.MinValue; }
            var raw = new StringBuilder("frame,crateY,crateSpeed," + string.Join(",", names) + "\n");
            int landing = -1;
            long[] landRow = null;
            for (int f = 0; f < SampleFrames; f++)
            {
                yield return null;
                var row = new long[recs.Count];
                for (int i = 0; i < recs.Count; i++)
                {
                    long v = recs[i].Valid ? recs[i].LastValue : -1;
                    row[i] = v;
                    sum[i] += v; if (v > max[i]) max[i] = v; if (v < min[i]) min[i] = v;
                }
                float cy = crate.transform.position.y;
                if (landing < 0 && cy - ground <= 0.55f) { landing = f; landRow = row; }
                raw.AppendLine($"{f},{cy:F3},{rb.linearVelocity.magnitude:F3},{string.Join(",", row)}");
            }
            File.WriteAllText(dir + "/control-raw.csv", raw.ToString());
            float endY = crate.transform.position.y; float endSpeed = rb.linearVelocity.magnitude;
            bool rests = Mathf.Abs(endY - ground) <= 0.6f && endSpeed < 0.1f;
            var ctl = new StringBuilder();
            ctl.AppendLine($"CONTROL crate ground {ground:F3}; landing frame {landing} (of {SampleFrames}); end y {endY:F3} (centre; resting centre is ground+0.5), end speed {endSpeed:F4} m/s; rests {rests}");
            for (int i = 0; i < recs.Count; i++)
                ctl.AppendLine($"RAW | {names[i]} | landing-frame LastValue {(landRow != null ? landRow[i].ToString() : "n/a")} | max {max[i]} | last {(recs[i].Valid ? recs[i].LastValue : -1)}");
            File.WriteAllText(dir + "/control-counters.txt", ctl.ToString());
            Debug.Log("PhysicsCountsProbe: " + ctl);
            for (int i = 0; i < recs.Count; i++)
                csv.AppendLine($"PositiveControl_dynamic_crate,{spawnPos.x + 3f:F1},{endY:F1},{spawnPos.z:F1},yaw{Yaw:F0},{names[i]},{sum[i] / SampleFrames:F2},{max[i]},{min[i]},{(landRow != null ? landRow[i] : -1)},{(rests ? "OK rests" : "VOID crate did not rest")}");
            controlMax = max;
            double total = 0;
            for (int i = 0; i < recs.Count; i++) total += max[i] > 0 ? max[i] : 0;
            if (landing < 0) controlFail = "control crate never reached the ground (cy-ground <= 0.55)";
            else if (!rests) controlFail = "control crate not at rest: y " + endY.ToString("F3") + " ground " + ground.ToString("F3") + " speed " + endSpeed.ToString("F4");
            else if (total <= 0) controlFail = "positive control read zero on every counter with a crate shown resting";
            if (landing >= 0 && rests)
            {
                int ni = names.IndexOf("Narrowphase Touches");
                if (ni < 0 || max[ni] <= 0)
                {
                    controlFail = (controlFail != null ? controlFail + " ;; " : "") + "counter 'Narrowphase Touches' " + (ni < 0 ? "is missing" : "max " + max[ni]) + " with the crate shown resting";
                    File.AppendAllText(dir + "/control-counters.txt", "NARROWPHASE_ZERO_AT_REST_TO_TD\n");
                }
            }
        }

        static float GroundY(float x, float z) => TerrainQuery.TryGroundHeight(new Vector3(x, 0f, z), out float h) ? h : float.NaN;

        static Vector3 Ridge(Vector3 from)
        {
            Vector3 best = from; float bestY = float.MinValue;
            for (float dx = -1500f; dx <= 1500f; dx += 10f)
                for (float dz = -1500f; dz <= 1500f; dz += 10f)
                {
                    if (dx * dx + dz * dz > 1500f * 1500f) continue;
                    float y = GroundY(from.x + dx, from.z + dz);
                    if (!float.IsNaN(y) && y > bestY) { bestY = y; best = new Vector3(from.x + dx, y, from.z + dz); }
                }
            return best;
        }
    }
}
