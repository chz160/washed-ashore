using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using WashedAshore.Gameplay;
using WashedAshore.Lookout;
using WashedAshore.Tests.Support;

namespace WashedAshore.Tests.PlayMode
{
    /// <summary>
    /// GitHub #64 wall-slide probe (spec-64, td-64 changes M1-M7, S1-S2). Walks the real PlayerController with ExternalMoveInput at fixed yaw
    /// offsets into walls, a corner, a doorway, lips, a crease twin and the lookout flights, and prints speed kept, stops and contacts.
    /// Public API only; the slide mode (option A) is read by reflection so the same file compiles on main. Fails on main by design.
    /// This file: the run engine, the metrics and the report. Cases are in ControllerProbe64.Fixture.cs and ControllerProbe64.Tower.cs.
    /// </summary>
    public partial class ControllerProbe64
    {
        const string LogDir = "Logs/";
        const float SettleSeconds = 0.25f, WindowSeconds = 0.10f, StopSeconds = 0.20f, SideNormalY = 0.3f, PopLimit = 0.05f, ThroughSlack = 0.02f, OffLineLimit = 0.03f;

        float dt = 1f / 60f, savedCapture;
        PlayerController player;
        CharacterController cc;
        ProbeContactRecorder rec;
        Transform towerRoot;
        int idCounter, snapCount, teleCount;
        readonly List<ProbeResult> results = new List<ProbeResult>();
        readonly List<string> validity = new List<string>();
        string logPath, csvPath, contactsPath;
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        static string F(float v, int n) => float.IsNaN(v) ? "-" : v.ToString("F" + n, Inv);
        static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
        float Along(ProbeRun s, Vector3 p) => Vector3.Dot(Flat(p - s.refPt), s.d);
        float Lateral(ProbeRun s, Vector3 p) => Vector3.Dot(Flat(p - s.refPt), s.toWall);

        const string Columns = "id | case | flight | dir | side | yaw_off | dt | cap | ref | start_miss | start_miss2 | start_lat | contact_along | kept_h | kept_path | ideal_path | min_vW | stops | longest_s | first_stop_along | end_stops | stuck | rise_max | air_frames | seat_gap | flags | touched | verdict | mode";

        // ---- test setup ------------------------------------------------------------------------------------------------

        IEnumerator Begin(string method, float frameDt)
        {
            dt = frameDt;
            savedCapture = Time.captureDeltaTime;
            Time.captureDeltaTime = dt;
            results.Clear(); validity.Clear(); idCounter = 0;
            logPath = LogDir + "controller-probe-64-" + method + ".txt";
            csvPath = LogDir + "controller-probe-64-" + method + ".csv";
            contactsPath = LogDir + "controller-probe-64-" + method + "-contacts.csv";
            Directory.CreateDirectory(LogDir);
            File.WriteAllText(logPath, ""); File.WriteAllText(csvPath, Columns.Replace(" | ", ",") + (method == "Probe64_F5tDiag" || method == "Probe64_TowerStep" ? ",variant" : "") + "\n");
            File.WriteAllText(contactsPath, "row_id,frame,t,collider,nx,ny,nz,px,py,pz,py_minus_feet_y\n");
            SceneManager.LoadScene(TestSpawn.WorldScene, LoadSceneMode.Single);
            yield return null;
            yield return null;
            player = TestSpawn.Player();
            cc = player.GetComponent<CharacterController>();
            rec = player.gameObject.AddComponent<ProbeContactRecorder>();
            var tower = GameObject.Find("LookoutTower");
            towerRoot = tower ? tower.transform : null;
            zones = towerRoot ? towerRoot.GetComponent<TowerWalkZones>() : null;
            WorldBoundsClamp.Snapped += OnSnapped;
            player.Teleported += OnTeleported;
            var capGo = new GameObject("PROBE64_PenetrationCapsule") { hideFlags = HideFlags.DontSave };
            capGo.transform.position = new Vector3(0f, -5000f, 0f);
            penCapsule = capGo.AddComponent<CapsuleCollider>(); penCapsule.isTrigger = true;
            ApplySlideMode();
        }

        CapsuleCollider penCapsule;

        /// <summary>td-64: absent property = "n/a (main)"; present = set from env WA_SLIDE_MODE when given and read back, a mismatch is PROBE INVALID (never a silent Off).</summary>
        void ApplySlideMode()
        {
            string want = System.Environment.GetEnvironmentVariable("PROBE64_SLIDE");
            var pi = typeof(PlayerController).GetProperty("Slide");
            if (pi == null) { if (!string.IsNullOrEmpty(want)) validity.Add($"PROBE64_SLIDE={want} but this tree has no PlayerController.Slide"); return; }
            if (string.IsNullOrEmpty(want)) return;
            try
            {
                pi.SetValue(player, System.Enum.Parse(pi.PropertyType, want));
                string back = pi.GetValue(player)?.ToString();
                if (back != want) validity.Add($"slide mode set to {want} but reads back {back}");
            }
            catch (System.Exception e) { validity.Add($"slide mode {want} could not be set: {e.GetType().Name}"); }
        }

        void End()
        {
            WorldBoundsClamp.Snapped -= OnSnapped;
            if (player) { player.Teleported -= OnTeleported; player.ExternalMoveInput = null; }
            if (rec) Destroy(rec);
            if (penCapsule) Destroy(penCapsule.gameObject);
            Time.captureDeltaTime = savedCapture;
        }

        static void Destroy(UnityEngine.Object o) => UnityEngine.Object.Destroy(o);
        void OnSnapped(GameObject go, Vector3 a, Vector3 b) => snapCount++;
        void OnTeleported() => teleCount++;

        // ---- header and mode ------------------------------------------------------------------------------------------

        string Mode()
        {
            var pi = typeof(PlayerController).GetProperty("Slide");
            string slide = pi == null ? "n/a (main)" : pi.GetValue(player)?.ToString() ?? "?";
            string kind = "-";
            if (towerRoot)
            {
                var c = towerRoot.GetComponentsInChildren<Collider>(true).FirstOrDefault(x => x.name == "CLIP_CVX_Flight01_01");
                kind = c == null ? "none" : c is MeshCollider ? "mesh" : c is BoxCollider ? "box" : c.GetType().Name;
            }
            return slide + "/" + kind;
        }

        void Emit(string line)
        {
            Debug.Log("[Probe64] " + line);
            File.AppendAllText(logPath, line + "\n");
        }

        void WriteHeader(string method)
        {
            Emit($"controller probe #64 ({method}), spec-64 + td-64 changes M1-M7 S1-S2");
            Emit($"capture dt {dt:F5}  Unity {Application.unityVersion}  mode (slide/flight-wall collider) {Mode()}");
            Emit($"capsule now: r {cc.radius:F3} h {cc.height:F3} step {cc.stepOffset:F3} skin {cc.skinWidth:F3} slope {cc.slopeLimit:F0}  walkSpeed {player.WalkSpeed:F2}  WalkSpeedCap {player.WalkSpeedCap:F3}");
            var others = player.GetComponentsInChildren<Collider>(false).Where(c => c is not CharacterController).ToList();
            Emit("other colliders on the Player: " + (others.Count == 0 ? "none" : string.Join("; ", others.Select(c => $"{c.GetType().Name} '{c.name}' trigger {c.isTrigger} layer {c.gameObject.layer}"))));
            int pl = WashedAshore.World.WorldLayers.Player;
            string towerLayer = "n/a";
            if (towerRoot)
            {
                var c = towerRoot.GetComponentsInChildren<Collider>(true).FirstOrDefault(x => x.name.StartsWith("CLIP_CVX_Flight01"));
                if (c) towerLayer = $"{c.gameObject.layer} ({LayerMask.LayerToName(c.gameObject.layer)}) ignored={Physics.GetIgnoreLayerCollision(pl, c.gameObject.layer)}";
            }
            Emit($"layer collision: Player({pl}) vs Default(0) ignored={Physics.GetIgnoreLayerCollision(pl, 0)}; Player vs tower flight wall layer {towerLayer}");
            if (Physics.GetIgnoreLayerCollision(pl, 0)) validity.Add("Player does not collide with Default (fixture colliders)");
            if (towerRoot)
                foreach (var c in towerRoot.GetComponentsInChildren<Collider>(true).Where(x => x.name.StartsWith("CLIP_CVX_Flight") || x.name.StartsWith("WALK_Ramp")).GroupBy(x => x.gameObject.layer).Select(g => g.First()))
                    if (Physics.GetIgnoreLayerCollision(pl, c.gameObject.layer)) validity.Add($"Player does not collide with layer {c.gameObject.layer} ({c.name})");
        }

        // ---- the run engine --------------------------------------------------------------------------------------------

        float SurfaceY(Vector3 feet)
        {
            var hits = Physics.RaycastAll(feet + Vector3.up * 0.5f, Vector3.down, 3f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue; bool found = false;
            foreach (var h in hits)
            {
                if (h.collider.transform.IsChildOf(player.transform)) continue;
                if (h.distance < best) { best = h.distance; found = true; }
            }
            return found ? feet.y + 0.5f - best : float.NaN;
        }

        IEnumerator WaitStill()
        {
            Vector3 prev = player.transform.position; int still = 0;
            for (int i = 0; i < Mathf.CeilToInt(1f / dt) && still < 3; i++)
            {
                yield return null;
                Vector3 p = player.transform.position;
                still = Flat(p - prev).magnitude / dt < 0.01f ? still + 1 : 0;
                prev = p;
            }
        }

        IEnumerator WaitGroundedAndIdle(ProbeResult res)
        {
            int g = 0;
            yield return null;
            while (!player.IsGrounded && g++ < Mathf.CeilToInt(3f / dt)) yield return null;
            if (!player.IsGrounded) { res.invalid = "not grounded"; yield break; }
            for (int i = 0; i < Mathf.RoundToInt(SettleSeconds / dt); i++) yield return null;
        }

        /// <summary>Places, settles, runs forward with input (0,1) and measures. Fills <paramref name="res"/>.</summary>
        IEnumerator ExecuteRun(ProbeRun s, ProbeResult res)
        {
            res.run = s; res.id = ++idCounter; res.dt = dt; res.mode = Mode();
            player.ExternalMoveInput = Vector2.zero;
            yield return WaitStill();
            if (s.useAt) yield return TestSpawn.At(s.start, s.yaw, false);
            else TestSpawn.Reposition(player, s.start, s.yaw);
            yield return WaitGroundedAndIdle(res);
            if (res.invalid != null) yield break;

            Vector3 feet = player.transform.position;
            Vector3 miss = Flat(feet - s.start);
            res.miss1 = miss.magnitude;
            if (res.miss1 > 0.03f)
            {
                TestSpawn.Reposition(player, new Vector3(s.start.x - miss.x, feet.y, s.start.z - miss.z), s.yaw);   // requested + (requested - actual), horizontal only
                yield return WaitGroundedAndIdle(res);
                if (res.invalid != null) yield break;
                feet = player.transform.position;
                res.miss2 = Flat(feet - s.start).magnitude;
            }
            if (res.miss1 > 0.03f) res.flags.Add("MOVED");
            res.startLat = Lateral(s, feet);
            if (Mathf.Abs(res.startLat - Lateral(s, s.start)) > OffLineLimit)
            {
                Vector3 m2 = Flat(feet - s.start);
                res.detail = $"    start off line: requested {s.start:F3}; first miss along {Vector3.Dot(miss, s.d):F3} lateral {Vector3.Dot(miss, s.toWall):F3} ({res.miss1:F3}); after correction {(float.IsNaN(res.miss2) ? "none" : $"along {Vector3.Dot(m2, s.d):F3} lateral {Vector3.Dot(m2, s.toWall):F3} ({res.miss2:F3})")}; intended lateral {Lateral(s, s.start):F3} actual {res.startLat:F3}; feet y {feet.y:F3} (requested {s.start.y:F3})";
                res.invalid = "start off line"; yield break;
            }
            if (Mathf.Abs(player.WalkSpeedCap - s.expectedWalkCap) > 1e-4f) { res.invalid = $"{(s.tower ? "not on ramp" : "cap not set")} (WalkSpeedCap {player.WalkSpeedCap:F3}, expected {s.expectedWalkCap:F3})"; yield break; }

            float cap = s.cap;
            float tMax = 2f * (s.aEnd - s.aStart) / (cap * Mathf.Max(s.noYaw ? 1f : Mathf.Cos(s.yawOff * Mathf.Deg2Rad), 0.5f)) + 1f;
            int tMaxF = Mathf.CeilToInt(tMax / dt), after3 = Mathf.RoundToInt(3f / dt);
            var f = new List<Vector3> { feet }; var frameOf = new List<int>(); var grounded = new List<bool> { player.IsGrounded };
            var surf = new List<float> { SurfaceY(feet) };
            var onRamp = new List<bool> { zones == null || zones.OnRamp };
            frameOf.Add(Time.frameCount);
            rec.hits.Clear(); snapCount = 0; teleCount = 0;
            res.stepAtStart = cc.stepOffset;
            player.ExternalMoveInput = new Vector2(0f, 1f);

            int k = 0, kE = -1;
            while (true)
            {
                yield return null; k++;
                Vector3 p = player.transform.position;
                f.Add(p); frameOf.Add(Time.frameCount); grounded.Add(player.IsGrounded); surf.Add(SurfaceY(p)); onRamp.Add(zones == null || zones.OnRamp);
                float a = Along(s, p);
                if (s.endTime >= 0f) { if (k * dt >= s.endTime - 1e-4f) { kE = k; break; } continue; }
                if (kE < 0)
                {
                    if (a >= s.aEnd) kE = k;
                    else if (k >= tMaxF) { res.stuck = true; kE = k; break; }
                }
                else if (s.afterEnd >= 0f) { if (k - kE >= Mathf.RoundToInt(s.afterEnd / dt)) break; }
                else if (a >= s.runEnd || k - kE >= after3) break;
            }
            player.ExternalMoveInput = Vector2.zero;
            Analyse(s, res, f, frameOf, grounded, surf, onRamp, kE, k);
        }

        void Analyse(ProbeRun s, ProbeResult res, List<Vector3> f, List<int> frameOf, List<bool> grounded, List<float> surf, List<bool> onRamp, int kE, int kLast)
        {
            int wf = Mathf.Max(1, Mathf.RoundToInt(WindowSeconds / dt));
            float VW(int k) => k >= wf ? Flat(f[k] - f[k - wf]).magnitude / (wf * dt) : float.NaN;
            int kS = Mathf.Min(Mathf.CeilToInt((s.cap / 20f + WindowSeconds) / dt - 1e-3f), kE);
            kS = Mathf.Max(kS, wf);
            res.sectionEndFeet = f[kE];

            double sum = 0; int n = 0; float min = float.MaxValue;
            for (int k = kS; k <= kE; k++) { float v = VW(k) / s.cap; sum += v; n++; min = Mathf.Min(min, v); }
            res.keptH = n > 0 ? (float)(sum / n) : float.NaN; res.minV = n > 0 ? min : float.NaN;
            float secT = (kE - kS) * dt;
            res.keptPath = secT > 0f ? (Along(s, f[kE]) - Along(s, f[kS])) / (secT * s.cap) : float.NaN;
            ScanStops(s, res, VW, kS, kE, f, true);
            if (s.endTime < 0f && s.afterEnd < 0f) ScanStops(s, res, VW, kE + 1, kLast, f, false);

            // THROUGH (spec r3 s3 + part4 + part10 P1): ComputePenetration against the named walls, every frame from k = 1; the lateral test only while along is inside the wall's span.
            int latFrames = 0, penFrames = 0, firstK = -1; string firstTest = "", firstCol = "-"; float firstA = 0f, firstLat = float.NaN, firstDepth = 0f;
            float maxOver = float.NegativeInfinity, maxDepth = 0f;
            Quaternion rot = Quaternion.Euler(0f, s.yaw, 0f);
            penCapsule.radius = cc.radius; penCapsule.height = cc.height; penCapsule.center = cc.center; penCapsule.direction = 1;
            for (int k = 1; k <= kLast; k++)   // every frame from the first one: starts are 0.02-0.056 from a wall
            {
                bool latHit = false, penHit = false; string col = "-"; float dep = 0f, latV = float.NaN;
                float a = Along(s, f[k]);
                if (s.lateralThrough)
                    foreach (var w in s.walls)
                    {
                        if (a < w.aMin || a > w.aMax) continue;
                        float l = Vector3.Dot(Flat(f[k] - s.refPt), w.dir);
                        maxOver = Mathf.Max(maxOver, l - (w.face - s.r));
                        if (l > w.face - s.r + ThroughSlack) { latHit = true; latV = l; }
                    }
                Vector3 centre = f[k] + rot * cc.center;
                foreach (var wc in s.wallCols)
                {
                    if (!wc || wc.bounds.SqrDistance(centre) > 1f) continue;
                    if (!Physics.ComputePenetration(penCapsule, f[k], rot, wc, wc.transform.position, wc.transform.rotation, out _, out float depth)) continue;
                    maxDepth = Mathf.Max(maxDepth, depth);
                    if (depth > ThroughSlack && depth > dep) { penHit = true; dep = depth; col = wc.name; }
                }
                if (latHit) latFrames++;
                if (penHit) penFrames++;
                if ((latHit || penHit) && firstK < 0) { firstK = k; firstA = a; firstLat = latV; firstTest = latHit && penHit ? "lateral+penetration" : latHit ? "lateral" : "penetration"; firstCol = col; firstDepth = dep; }
            }
            // TowerStep rows (spec r9 + part25): THROUGH counts only at the part23 bar, penetration depth > 0.03; lateral and shallower hits stay in the through: line
            res.through = s.variant == "step0.25" ? maxDepth > 0.03f : firstK >= 0;
            string diag = $"    through: by {(res.through ? (latFrames > 0 && penFrames > 0 ? "lateral+penetration" : latFrames > 0 ? "lateral" : "penetration") : "none")}, frames lateral {latFrames} penetration {penFrames}, first k {(firstK < 0 ? "-" : firstK.ToString())} along {(firstK < 0 ? "-" : F(firstA, 3))} lateral {F(firstLat, 3)} test {(firstK < 0 ? "-" : firstTest)} collider {firstCol} depth {(firstK < 0 ? "-" : F(firstDepth, 3))}; max lateral overshoot {(float.IsNegativeInfinity(maxOver) ? "-" : F(maxOver, 3))} max penetration depth {F(maxDepth, 3)}";

            float rise0 = surf[kS] is float s0 && !float.IsNaN(s0) ? f[kS].y - s0 : 0f, rmax = 0f;
            for (int k = kS; k <= kE; k++) { if (!float.IsNaN(surf[k])) rmax = Mathf.Max(rmax, f[k].y - surf[k] - rise0); if (!grounded[k]) res.air++; }
            res.riseMax = rmax;

            var frameK = new Dictionary<int, int>(); for (int k = 0; k < frameOf.Count; k++) frameK[frameOf[k]] = k;
            var touch = new Dictionary<string, HashSet<int>>();
            foreach (var h in rec.hits)
            {
                if (!frameK.TryGetValue(h.frame, out int k) || Mathf.Abs(h.normal.y) >= SideNormalY) continue;
                if (float.IsNaN(res.contactAlong)) res.contactAlong = Along(s, f[k]);
                if (k < kS || k > kE) continue;
                if (!touch.TryGetValue(h.name, out var set)) touch[h.name] = set = new HashSet<int>();
                set.Add(k);
            }
            if ((s.caseName == "T1" || s.caseName == "F5t" || s.caseName == "REPEAT") && s.yawOff > 0f && s.wallCols.Count > 0)
            {
                // spec r4: share of main-section frames with a side contact on the target wall; information only
                var target = s.wallCols.Where(c => c).OrderByDescending(c => Vector3.Dot(Flat(c.bounds.center - s.refPt), s.toWall)).First();
                int touchFrames = touch.TryGetValue(target.name, out var tset) ? tset.Count : 0;
                float wallFrac = touchFrames / (float)Mathf.Max(1, kE - kS + 1);
                if (wallFrac < 0.50f) res.flags.Add("SHORT CONTACT " + F(wallFrac, 2));
            }
            if (touch.Count > 0) res.touched = string.Join(", ", touch.OrderByDescending(t => t.Value.Count).Take(3).Select(t => $"{t.Key}x{t.Value.Count}"));
            if (s.seated != null)
            {
                res.seatFail = !s.seated(f[kLast]);
                res.seatGap = $"{F(origin.x + 1f - f[kLast].x, 3)}/{F(origin.z + 5f - f[kLast].z, 3)}";
            }
            if (!s.tower && rec.hits.Any(h => Mathf.Abs(h.normal.y) < SideNormalY && !h.name.StartsWith("PROBE64_"))) res.flags.Add("FOREIGN");
            if (snapCount > 0 || teleCount > 0) res.invalid = "snapped";
            if (res.through) res.flags.Add("THROUGH");
            if (res.riseMax > PopLimit) res.flags.Add("POP");
            if (res.seatFail) res.flags.Add("NOSEAT");

            if (s.logContacts)
            {
                var sb = new StringBuilder();
                foreach (var h in rec.hits)
                {
                    int kk = frameK.TryGetValue(h.frame, out int kq) ? kq : -1;
                    float feetY = h.feetY;
                    sb.Append($"{res.id},{kk},{F(kk * dt, 4)},{h.name},{F(h.normal.x, 3)},{F(h.normal.y, 3)},{F(h.normal.z, 3)},{F(h.point.x, 3)},{F(h.point.y, 3)},{F(h.point.z, 3)},{F(h.point.y - feetY, 3)}\n");
                }
                File.AppendAllText(contactsPath, sb.ToString());
            }
            if (s.spanL > 0f) diag += "\n" + TowerChecks(s, res, f, onRamp, kS, kE, kLast);
            res.detail = diag;
            if (res.stops > 0 || res.stuck) res.detail += "\n" + Detail(s, res, f[kLast], VW(kLast) / s.cap, grounded[kLast]);
            if (res.endStops > 0 && res.endStopK >= 0) res.detail += $"\n    end-zone stop first at frame {res.endStopK}, along {F(Along(s, f[res.endStopK]), 3)} (section ends at {kE}):\n" + Detail(s, res, f[res.endStopK], VW(res.endStopK) / s.cap, grounded[res.endStopK]);
        }

        void ScanStops(ProbeRun s, ProbeResult res, Func<int, float> vw, int from, int to, List<Vector3> f, bool main)
        {
            int run = 0, count = 0, firstK = -1; float longest = 0f; float firstAlong = float.NaN;
            void Flush(int endK)
            {
                if (run > 0 && run * dt > StopSeconds + 1e-4f)
                {
                    count++; longest = Mathf.Max(longest, run * dt);
                    if (float.IsNaN(firstAlong)) { firstAlong = Along(s, f[endK - run + 1]); firstK = endK - run + 1; }
                }
                run = 0;
            }
            for (int k = from; k <= to; k++)
            {
                float v = vw(k);
                if (!float.IsNaN(v) && v < 0.5f * s.cap) run++; else Flush(k - 1);
            }
            Flush(to);
            if (main) { res.stops = count; res.longest = longest; res.firstStopAlong = firstAlong; } else { res.endStops = count; res.endStopK = firstK; }
        }

        string Detail(ProbeRun s, ProbeResult res, Vector3 p, float vw, bool grounded)
        {
            string local = towerRoot ? towerRoot.InverseTransformPoint(p).ToString("F3") : "n/a";
            var names = Physics.OverlapSphere(p + Vector3.up * 0.9f, 0.7f).Where(c => !c.transform.IsChildOf(player.transform)).Select(c => c.name).Distinct();
            return $"    at world {p:F3} tower-local {local} lateral {Lateral(s, p):F3} grounded {grounded} vW/cap {vw:F2} overlap: {string.Join(", ", names)}";
        }

        // ---- verdict and report ----------------------------------------------------------------------------------------

        void Judge(ProbeResult r)
        {
            var s = r.run;
            if (s.extraFlag.Length > 0) r.flags.Add(s.extraFlag);
            if (r.invalid != null) { r.verdict = "INVALID"; r.flags.Add("INVALID:" + r.invalid); return; }
            bool pass = r.stops == 0 && r.endStops == 0 && !r.stuck && !r.through && !r.seatFail;
            if (s.variant == "step0.25") pass = pass && !r.flags.Contains("POP") && !r.flags.Contains("CLIMB") && !r.flags.Contains("OFF RAMP");   // spec r9 5d: each fails a TowerStep row
            if (s.caseName == "FX") r.verdict = r.stops >= 1 && r.longest >= 1f ? "EXPECTED-FAIL" : "INVALID";
            else if (s.caseName == "F0") r.verdict = pass && r.keptH >= 0.98f ? "PASS" : "INVALID";
            else r.verdict = pass ? "PASS" : "FAIL";
            if (r.verdict == "INVALID") r.flags.Add("INVALID:control did not behave");
            if (s.caseName == "F3" && Mathf.Approximately(s.yawOff, 30f)) r.flags.Add("NEAR THRESHOLD");
        }

        string RowText(ProbeResult r, bool csv)
        {
            var s = r.run; string sep = csv ? "," : " | ";
            string flags = string.Join(csv ? ";" : " ", r.flags);
            bool measured = r.invalid == null;
            string miss1 = float.IsNaN(r.miss1) ? "-" : F(r.miss1, 3), miss2 = float.IsNaN(r.miss2) ? "-" : F(r.miss2, 3);
            string dtText = "1/" + Mathf.RoundToInt(1f / r.dt);
            var cols = new[] {
                r.id.ToString(), s.caseName, s.flight, s.dir, s.side, s.noYaw ? "-" : F(s.yawOff, 0), dtText, F(s.cap, 2), F(r.refKept, 2), miss1, miss2, F(r.startLat, 3),
                F(r.contactAlong, 3), measured ? F(r.keptH, 2) : "-", measured ? F(r.keptPath, 2) : "-", F(s.idealPath, 2), measured ? F(r.minV, 2) : "-",
                measured ? r.stops.ToString() : "-", measured ? F(r.longest, 2) : "-", F(r.firstStopAlong, 3), measured ? r.endStops.ToString() : "-",
                measured ? (r.stuck ? "STUCK" : "no") : "-", measured ? F(r.riseMax, 3) : "-", measured ? r.air.ToString() : "-", r.seatGap, flags, csv ? r.touched.Replace(",", ";") : r.touched,
                r.verdict, r.mode };
            return string.Join(sep, s.variant.Length > 0 ? cols.Concat(new[] { s.variant }) : cols);
        }

        void Record(ProbeResult r)
        {
            Judge(r);
            results.Add(r);
            Emit(RowText(r, false));
            File.AppendAllText(csvPath, RowText(r, true) + "\n");
            if (r.detail.Length > 0) Emit(r.detail);
        }

        /// <summary>Runs one spec and records it. Keeps a reference row per refKey so the theta-0 run's kept_h prints as ref and REF LOW flags its flight.</summary>
        IEnumerator Go(ProbeRun s, Dictionary<string, ProbeResult> refs)
        {
            var r = new ProbeResult();
            yield return ExecuteRun(s, r);
            if (s.refKey.Length > 0)
            {
                if (s.isRef) { refs[s.refKey] = r; if (r.invalid == null && r.keptH < 0.80f) r.flags.Add("REF LOW"); }
                else if (refs.TryGetValue(s.refKey, out var rr) && rr.invalid == null) { r.refKept = rr.keptH; if (rr.flags.Contains("REF LOW")) r.flags.Add("REF LOW"); }
            }
            Record(r);
        }

        static bool Stopped(ProbeResult r) => r.invalid == null && (r.stops > 0 || r.stuck);

        void Summary(string method, bool includeK1)
        {
            int pass = results.Count(r => r.verdict == "PASS"), fail = results.Count(r => r.verdict == "FAIL"), exp = results.Count(r => r.verdict == "EXPECTED-FAIL"), inv = results.Count(r => r.verdict == "INVALID");
            foreach (var c in results.Where(r => r.run.control && r.verdict == "INVALID")) validity.Add($"control {c.run.caseName} row {c.id}");
            if (inv > 0) validity.Add($"{inv} INVALID rows: {string.Join(", ", results.Where(r => r.verdict == "INVALID").Take(8).Select(r => r.id + " " + r.invalid))}");   // td ruling: any INVALID row = PROBE VALID no
            Emit("");
            Emit($"PROBE VALID: {(validity.Count == 0 ? "yes" : "no")}{(validity.Count == 0 ? "" : " (" + string.Join("; ", validity) + ")")}");
            var t1 = results.Where(r => r.run.caseName == "T1" && Mathf.Abs(r.dt - 1f / 60f) < 1e-4f).ToList();   // INVALID rows stay in the denominators
            if (includeK1 && t1.Count > 0)
            {
                bool Up10or20(ProbeResult r) => r.run.dir == "up" && (r.run.yawOff == 10f || r.run.yawOff == 20f);
                int u1020 = t1.Count(r => Up10or20(r) && Stopped(r));
                int uAll = t1.Count(r => r.run.dir == "up" && r.run.yawOff > 0f && Stopped(r));
                int dAll = t1.Count(r => r.run.dir == "down" && r.run.yawOff > 0f && Stopped(r));
                int u0 = t1.Count(r => r.run.dir == "up" && r.run.yawOff == 0f && Stopped(r)), d0 = t1.Count(r => r.run.dir == "down" && r.run.yawOff == 0f && Stopped(r));
                Emit($"K1 #41 reproduced: {(u1020 > 0 ? "yes" : "no")}  (up theta10/20 stopped: {u1020} of {t1.Count(Up10or20)}; up theta!=0 stopped: {uAll} of {t1.Count(r => r.run.dir == "up" && r.run.yawOff > 0f)}; down theta!=0 stopped: {dAll} of {t1.Count(r => r.run.dir == "down" && r.run.yawOff > 0f)}; theta 0 references stopped: up {u0} of {t1.Count(r => r.run.dir == "up" && r.run.yawOff == 0f)}, down {d0} of {t1.Count(r => r.run.dir == "down" && r.run.yawOff == 0f)})");
            }
            var rep6 = results.Where(r => r.run.caseName == "REPEAT").ToList();
            if (rep6.Count > 0)
            {
                var bad = rep6.Where(r => r.flags.Contains("NONDETERMINISTIC")).Select(r => r.id).ToList();
                Emit($"repeat: {rep6.Count - bad.Count} of {rep6.Count} matched{(bad.Count > 0 ? " | NONDETERMINISTIC: rows " + string.Join(",", bad) : "")}");
            }
            foreach (var g in results.Where(r => r.invalid == null && !r.run.control).GroupBy(r => $"{r.run.caseName}{(r.run.variant.Length > 0 ? " variant " + r.run.variant : "")}{(r.run.caseName.StartsWith("F4") ? " h" + r.run.note : "")} {r.run.dir} theta={(r.run.noYaw ? "-" : r.run.yawOff.ToString("F0"))}{(r.run.caseName == "F3" && r.run.yawOff == 30f ? " (near threshold)" : "")} dt=1/{Mathf.RoundToInt(1f / r.dt)}"))
            {
                var l = g.ToList();
                Emit($"by case and theta: {g.Key}: FAIL {l.Count(r => r.verdict == "FAIL")}/{l.Count}, mean kept_path {l.Average(r => r.keptPath):F2}, worst min_vW {l.Min(r => r.minV):F2}");
            }
            Emit($"total: PASS {pass}, FAIL {fail}, EXPECTED-FAIL {exp}, INVALID {inv} of {results.Count}");
            if (method == "Probe64_Fixture") SideBySide();
            Emit($"result {method}: {(validity.Count == 0 && (fail == 0 || InfoOnly(method)) ? "pass" : "FAIL")}");
        }

        /// <summary>Supplementary methods: they fail only when PROBE VALID is no.</summary>
        static bool InfoOnly(string method) => method == "Probe64_DtSweep" || method == "Probe64_F5tDiag" || method == "Probe64_TowerStep";

        void Verdict(string method)
        {
            Assert.IsEmpty(validity, $"{method}: PROBE VALID: no: {string.Join("; ", validity)} (see {logPath})");
            if (InfoOnly(method)) return;
            int fail = results.Count(r => r.verdict == "FAIL");
            Assert.AreEqual(0, fail, $"{method}: {fail} of {results.Count} runs FAIL (fails on main by design; see {logPath})");
        }
    }
}
