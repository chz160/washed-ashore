using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using WashedAshore.Gameplay;
using WashedAshore.World;

namespace WashedAshore.Tests.PlayMode
{
    /// <summary>
    /// Spec W8 (water-swim-spec.md §7): at both barrier ends the real PlayerController tries to swim round the
    /// north line, 5 patterns x 5 variants per end (direct, hugging the bank within 5 m (steered along the edge), mid-channel, outward diagonal
    /// 30/60, sprint-jump off the bank then swim). Each run may take up to 60 s or its path at swim speed,
    /// whichever is longer, and ends early once snapped or held at the backstop for 2 s. A crossing is
    /// any frame where the raw position (after Update, before WorldBoundsClamp's LateUpdate) is north of
    /// the line. A run counts as an attempt only if it was snapped, reached the backstop's south face, or
    /// was stopped against the barrier; a run stopped short is replaced by a shifted variant. Nothing here
    /// uses any out-of-bounds penalty (spec W8): the test also fails on any fall-through guard reset or a swimmer
    /// off the float height. Runs go to TestResults/water-w8-runs.csv.
    /// </summary>
    public class WaterSwimAroundTests : InputTestFixture
    {
        public enum End { West, East }
        public enum Pattern { Direct, HugBank, MidChannel, Diagonal, SprintThenSwim }

        const string WorldScene = "World";
        const string BarrierRoot = "NorthBarrier";
        const int Variants = 5;
        const int MinAttemptsPerEnd = 20;
        const int Replacements = 3;      // shifted retries for a run stopped short of the barrier
        const float StartSouth = 45f;
        const float PushSeconds = 4f;
        const float MinRunSeconds = 60f;  // §7 W8: slow swims are not mis-scored as short runs
        const float HoldSeconds = 2f;     // held at the backstop this long ends a run early
        const float FaceSlack = 1.5f;

        Keyboard keyboard;
        Camera disabledCamera;
        PlayerController player;
        SwimTuning tuning;
        CharacterController cc;
        WaterBody water;
        WorldBoundsRule rule;
        float wall, w;
        int playerSnaps, fallThroughs;
        GameObject crate;
        int crateSnaps;

        public override void Setup()
        {
            base.Setup();
            keyboard = InputSystem.AddDevice<Keyboard>();
            Time.captureDeltaTime = 1f / 60f;
            Application.logMessageReceived += OnLog;
        }

        public override void TearDown()
        {
            Application.logMessageReceived -= OnLog;
            WorldBoundsClamp.Snapped -= OnSnap;
            Time.captureDeltaTime = 0f;
            if (crate) Object.Destroy(crate);
            if (disabledCamera) disabledCamera.enabled = true;
            base.TearDown();
        }

        void OnLog(string message, string stack, LogType type)
        {
            if (message.StartsWith("PlayerController: fell below terrain")) fallThroughs++;
        }

        void OnSnap(GameObject go, Vector3 from, Vector3 to)
        {
            if (player && go == player.gameObject) playerSnaps++;
            if (crate && go == crate) crateSnaps++;
        }

        IEnumerator LoadWorld()
        {
            SceneManager.LoadScene(WorldScene, LoadSceneMode.Single);
            yield return null;
            yield return null;
            disabledCamera = Camera.main;
            if (disabledCamera) disabledCamera.enabled = false;
            var clamp = WorldBoundsTestKit.Clamp();
            rule = clamp.Rule;
            wall = WorldBoundsTestKit.BackstopThickness(clamp);
            player = WorldBoundsTestKit.Player();
            cc = player.GetComponent<CharacterController>();
            tuning = player.SwimTuning;
            Assert.IsNotNull(tuning, "PlayerController has no SwimTuning asset");
            water = WaterTestKit.Water();
            w = water.Config.WaterLevelY;
            playerSnaps = fallThroughs = crateSnaps = 0;
            WorldBoundsClamp.Snapped += OnSnap;
        }

        /// <summary>Outward sign of an end: -1 west, +1 east.</summary>
        static float Out(End e) => e == End.West ? -1f : 1f;

        /// <summary>X of the water's edge at <paramref name="z"/>, scanning outward from 100 m inside the barrier end.</summary>
        float EdgeX(End e, float z)
        {
            var (minX, maxX) = WorldBoundsTestKit.BarrierSpanX();
            float s = Out(e), x0 = (e == End.West ? minX : maxX) - s * 100f;
            for (float d = 0f; d < 600f; d += 0.5f)
            {
                float x = x0 + s * d;
                if (WaterTestKit.Ground(new Vector3(x, 0f, z), out float h) && h < w) return x;
            }
            Assert.Fail($"No water edge found at the {e} end, z={z:F0}");
            return x0;
        }

        /// <summary>
        /// Start XZ, yaw, sprint and bank-hug offset for one variant (index 0..4, shifted by <paramref name="shift"/> m outward
        /// on a retry). hug >= 0 steers every frame to stay that many metres off the water's edge (bank-hugging); -1 = fixed yaw.
        /// </summary>
        (Vector3 start, float yaw, bool sprint, float hug) Variant(End e, Pattern p, int i, float shift)
        {
            float s = Out(e);
            var (minX, maxX) = WorldBoundsTestKit.BarrierSpanX();
            float zLine = rule.LineZAt(e == End.West ? minX : maxX); // the line runs flat past the ends
            float z0 = zLine - StartSouth, edge = EdgeX(e, z0);
            switch (p)
            {
                case Pattern.Direct:
                    return (new Vector3(edge + s * (new[] { 8f, 15f, 30f, 60f, 100f }[i] + shift), 0f, z0), 0f, false, -1f);
                case Pattern.HugBank:
                {
                    // Within 5 m of the bank (wading where shallow), steered along the water's edge as it bends toward the line.
                    float off = new[] { 0.5f, 1f, 2f, 3.5f, 5f }[i] + shift * 0.25f;
                    return (new Vector3(edge + s * off, 0f, z0), 0f, false, off);
                }
                case Pattern.MidChannel:
                {
                    // Out toward the middle of the river, kept 50 m inside the terrain edge.
                    var (tMin, tMax) = WorldBoundsTestKit.TerrainXRange();
                    float far = e == End.West ? edge - (tMin + 50f) : (tMax - 50f) - edge;
                    float frac = new[] { 0.2f, 0.35f, 0.5f, 0.65f, 0.8f }[i];
                    return (new Vector3(edge + s * (Mathf.Max(20f, far * frac) + shift), 0f, z0), 0f, false, -1f);
                }
                case Pattern.Diagonal:
                {
                    // Outward 30/45/60 degrees (toward open water past the barrier end), starting in the water.
                    float yaw = new[] { 30f, 60f, 30f, 60f, 45f }[i] * s;
                    float off = new[] { 10f, 10f, 40f, 40f, 20f }[i] + shift;
                    return (new Vector3(edge + s * off, 0f, z0), yaw, false, -1f);
                }
                default:
                    // Sprint across the bank toward the open water past the barrier end, jump at the first wet step, swim on.
                    return (new Vector3(edge - s * (new[] { 10f, 15f, 20f, 30f, 40f }[i] + shift), 0f, z0), s * 45f, true, -1f);
            }
        }

        [UnityTest, Timeout(3600000)]
        public IEnumerator SwimAround_BothEnds_ZeroCrossings([Values] End end)
        {
            yield return LoadWorld();
            var rows = new List<string>();
            var crossings = new List<string>();
            var shortRuns = new List<string>();
            var drift = new List<string>();
            int attempts = 0, cornerRuns = 0;
            corner = Corner(end);
            try
            {
                foreach (Pattern p in System.Enum.GetValues(typeof(Pattern)))
                    for (int i = 0; i < Variants; i++)
                        for (int retry = 0; retry <= Replacements; retry++)
                        {
                            var (start, yaw, sprint, hug) = Variant(end, p, i, retry * 2f);
                            var r = new Run();
                            yield return Attempt(end, start, yaw, sprint, hug, r);
                            rows.Add(Row(end, p.ToString(), i, retry, start, yaw, r));
                            if (r.maxNorth > 0f) crossings.Add($"{p}#{i}.{retry} start={start:F0} maxNorth={r.maxNorth:F2}");
                            if (r.floatError > 0.1f) drift.Add($"{p}#{i}.{retry} float error {r.floatError:F2} m");
                            if (r.validity != null) { attempts++; break; }
                            if (retry == Replacements) shortRuns.Add($"{p}#{i}: {r.note}");
                        }

                // w-qa extra runs (not counted toward the >= 20): from open water, inward at 30/45/60 degrees straight at the
                // corner where the barrier leaves the bank and the backstop takes over in the water. Any crossing is a FAIL.
                foreach (float angle in new[] { 30f, 45f, 60f })
                {
                    float yaw = -Out(end) * angle;
                    var dir = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
                    var start = corner - dir * (StartSouth / Mathf.Cos(angle * Mathf.Deg2Rad));
                    var r = new Run();
                    yield return Attempt(end, start, yaw, false, -1f, r);
                    cornerRuns++;
                    rows.Add(Row(end, "InwardCorner", (int)angle, 0, start, yaw, r));
                    if (r.maxNorth > 0f) crossings.Add($"InwardCorner{angle:F0} start={start:F0} maxNorth={r.maxNorth:F2}");
                    if (r.floatError > 0.1f) drift.Add($"InwardCorner{angle:F0} float error {r.floatError:F2} m");
                }
            }
            finally
            {
                if (keyboard.wKey.isPressed) Release(keyboard.wKey);
                if (keyboard.leftShiftKey.isPressed) Release(keyboard.leftShiftKey);
                if (keyboard.spaceKey.isPressed) Release(keyboard.spaceKey);
            }

            WaterTestKit.AppendCsv("water-w8-runs-v2.csv", "end,pattern,variant,retry,startX,startZ,yaw,startDepth,validity,maxNorth,clampSnaps,endZ,minCornerDist,note", rows);
            Debug.Log($"WaterSwimAroundTests[{end}]: corner=({corner.x:F1},{corner.z:F1}) attempts={attempts} cornerRuns={cornerRuns} crossings={crossings.Count} playerSnaps={playerSnaps} fallThroughs={fallThroughs} short={shortRuns.Count}");
            Assert.IsEmpty(crossings, $"{end}: crossings: " + string.Join("; ", crossings));
            Assert.GreaterOrEqual(attempts, MinAttemptsPerEnd, $"{end}: runs stopped short: " + string.Join("; ", shortRuns));
            Assert.IsEmpty(drift, $"{end}: swimmer off the surface: " + string.Join("; ", drift));
            Assert.AreEqual(0, fallThroughs, "fall-through guard fired");
        }

        class Run { public string validity, note = ""; public float maxNorth = float.MinValue, endZ, floatError, startDepth, minCorner = float.MaxValue; public int snaps; }

        Vector3 corner;

        static string Row(End e, string pattern, int variant, int retry, Vector3 start, float yaw, Run r) =>
            $"{e},{pattern},{variant},{retry},{start.x:F1},{start.z:F1},{yaw:F0},{r.startDepth:F2},{r.validity ?? "-"},{r.maxNorth:F2},{r.snaps},{r.endZ:F2},{r.minCorner:F1},\"{r.note}\"";

        /// <summary>w-qa: the bank-side end of the backstop's south face, where the barrier leaves the bank and the backstop takes over in the water.</summary>
        Vector3 Corner(End e)
        {
            var (minX, maxX) = WorldBoundsTestKit.BarrierSpanX();
            float zFace = rule.LineZAt(e == End.West ? minX : maxX) - wall;
            return new Vector3(EdgeX(e, zFace - 1f), 0f, zFace);
        }

        IEnumerator Attempt(End e, Vector3 start, float yaw, bool sprint, float hug, Run r)
        {
            if (keyboard.wKey.isPressed) Release(keyboard.wKey);
            if (keyboard.leftShiftKey.isPressed) Release(keyboard.leftShiftKey);
            yield return new WaitForSeconds(0.5f); // let the controller's velocity decay
            if (!WaterTestKit.Ground(start, out float ground)) { r.note = "start off the terrain"; yield break; }
            r.startDepth = water.SurfaceY(start.x, start.z) - ground; // qa: wet column (<= 0 = dry start)
            float feet = Mathf.Max(ground + 0.05f, water.SurfaceY(start.x, start.z) - tuning.floatDepth);
            WaterTestKit.Place(player, start, feet, yaw);
            yield return new WaitForSeconds(0.25f);

            var dir = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            float pathLen = (rule.LineZAt(start.x) - start.z) / Mathf.Max(0.05f, dir.z);
            float dur = Mathf.Max(MinRunSeconds, pathLen / Mathf.Min(tuning.swimSpeed, player.WalkSpeed) + PushSeconds), t = 0f, held = 0f;
            bool jumped = !sprint;
            int snapsBefore = playerSnaps;
            // Shift before W (InputTestFixture loses W when both are pressed in one update).
            if (sprint) Press(keyboard.leftShiftKey);
            Press(keyboard.wKey);
            while (t < dur)
            {
                if (hug >= 0f)
                {
                    // Bank-hugging: aim 4 m ahead at the point that many metres off the water's edge.
                    var at = player.transform.position;
                    float aheadZ = at.z + 4f, aimX = EdgeX(e, aheadZ) + Out(e) * hug;
                    player.transform.rotation = Quaternion.Euler(0f, Mathf.Atan2(aimX - at.x, aheadZ - at.z) * Mathf.Rad2Deg, 0f);
                }
                if (!jumped && player.WaterMode != WaterMode.Dry) { Press(keyboard.spaceKey); jumped = true; }
                else if (keyboard.spaceKey.isPressed) Release(keyboard.spaceKey);
                yield return null; // after Update, before the clamp's LateUpdate
                t += Time.deltaTime;
                var p = player.transform.position;
                r.maxNorth = Mathf.Max(r.maxNorth, rule.NorthOfLine(p));
                r.minCorner = Mathf.Min(r.minCorner, WaterTestKit.Flat(p - corner).magnitude);
                if (player.WaterMode == WaterMode.Swim && player.WaterDepth > tuning.swimEnterDepth + 0.5f && t > 2f)
                    r.floatError = Mathf.Max(r.floatError, Mathf.Abs(p.y - (player.SurfaceY - tuning.floatDepth)));
                if (playerSnaps > snapsBefore) break;
                held = rule.LineZAt(p.x) - wall - p.z <= cc.radius + FaceSlack ? held + Time.deltaTime : 0f;
                if (held >= HoldSeconds) break;
            }
            Release(keyboard.wKey);
            if (keyboard.spaceKey.isPressed) Release(keyboard.spaceKey);
            if (sprint) Release(keyboard.leftShiftKey);

            var end = player.transform.position;
            r.endZ = end.z;
            r.snaps = playerSnaps - snapsBefore;
            float toFace = rule.LineZAt(end.x) - wall - end.z;
            if (r.snaps > 0) r.validity = "snap";
            else if (toFace <= cc.radius + FaceSlack) r.validity = "backstop";
            else if (BarrierAhead(end, hug >= 0f ? player.transform.forward : dir)) r.validity = "barrier";
            else r.note = $"stopped {toFace:F1} m short of the backstop at {end:F1} ({player.WaterMode})";
        }

        /// <summary>A NorthBarrier collider within reach ahead of the player.</summary>
        bool BarrierAhead(Vector3 feet, Vector3 dir)
        {
            foreach (float h in new[] { 0.3f, 0.9f, 1.5f })
                foreach (var hit in Physics.RaycastAll(feet + Vector3.up * h, dir, cc.radius + FaceSlack, ~0, QueryTriggerInteraction.Ignore))
                    if (hit.collider.transform.root.name == BarrierRoot) return true;
            return false;
        }

        /// <summary>qa W8 (c): a floating crate pushed north past each end is snapped back by the clamp and keeps floating.</summary>
        [UnityTest, Timeout(600000)]
        public IEnumerator Crate_PushedNorthPastEnds_IsSnapped([Values] End end)
        {
            yield return LoadWorld();
            float zLine = rule.LineZAt(0f), s = Out(end);
            var at = new Vector3(EdgeX(end, zLine - 20f) + s * 30f, 0f, zLine - 20f);
            crate = GameObject.CreatePrimitive(PrimitiveType.Cube);
            crate.name = "W8TestCrate";
            // Spawned the way a game spawns a floating body: placed first, then floated by WaterFloat.OnEnable.
            crate.transform.position = at;
            var crateBody = crate.AddComponent<Rigidbody>();
            var floater = crate.AddComponent<WaterFloat>();
            // w-td: a body is floated in the frame it spawns, before any physics step.
            Assert.LessOrEqual(Mathf.Abs(crate.transform.position.y + floater.Draft - water.SurfaceY(at.x, at.z, WaterClock.Now)), 0.05f,
                "crate drawn at its spawn height in its first frame");
            // Wait one physics step before sampling, so the samples never depend on frame/physics timing.
            yield return new WaitForFixedUpdate();
            yield return null;
            float worstNorthAfterClamp = float.MinValue, worstFloat = 0f;
            for (float t = 0f; t < 10f; t += Time.deltaTime)
            {
                // Moved the way a game mover moves a kinematic body: physics position and transform together
                // (the clamp reads Rigidbody.position, which a transform-only write updates one physics step late).
                var next = crate.transform.position + Vector3.forward * (5f * Time.deltaTime);
                crateBody.position = next;
                crate.transform.position = next;
                yield return null;
                var p = crate.transform.position;
                // Read on the next frame: the clamp ran in last frame's LateUpdate, the float in this Update.
                worstNorthAfterClamp = Mathf.Max(worstNorthAfterClamp, rule.NorthOfLine(p));
                if (WaterTestKit.Ground(p, out float h) && w - h > 1f)
                    worstFloat = Mathf.Max(worstFloat, Mathf.Abs(p.y + floater.Draft - water.SurfaceY(p.x, p.z)));
            }
            Debug.Log($"WaterSwimAroundTests[crate {end}]: snaps={crateSnaps} worstNorthAfterClamp={worstNorthAfterClamp:F2} worstFloat={worstFloat:F3}");
            Assert.Greater(crateSnaps, 0, "crate was never snapped");
            Assert.LessOrEqual(worstNorthAfterClamp, 0.001f, "crate stayed north of the line after a clamp pass");
            Assert.LessOrEqual(worstFloat, 0.05f, "crate left the surface");
        }
    }
}
