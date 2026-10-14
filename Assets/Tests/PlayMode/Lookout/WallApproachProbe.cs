using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using WashedAshore.Gameplay;

namespace WashedAshore.Tests.PlayMode.Lookout
{
    /// <summary>
    /// A measurement, not a check (tower-td-note, lane-end ruling; definition agreed with t-qa): how close the centre
    /// of the player's capsule gets to a wall when the real PlayerController walks into it on ordinary floor. It
    /// decides the wall-side half-width of every join mouth. The only assertions are that every case ran and gave
    /// samples. The floor and walls are made and destroyed here; nothing is saved to a scene.
    /// Results: TestResults/tower-eng/wall-approach-dt{N}.json, .txt (one table) and -frames.csv (every frame).
    /// </summary>
    [Category("LookoutProbe")]
    public class WallApproachProbe : InputTestFixture
    {
        const float HoldSeconds = 5f;         // W held this long per case
        const float SettleSeconds = 2f;       // at most this long for the capsule to land and take its radius
        const float ClearStart = 1f;          // capsule surface starts at least this far from the target, m
        const float RunUpSeconds = 0.5f;      // plus this much travel at the case's speed
        const float RigHeight = 120f;         // floor this far above the Player's start, clear of terrain, water and trees
        const float ZoneSpacing = 20f;        // between the three targets, m
        const float WallHeight = 2.5f, WallThickness = 0.2f, WallLength = 8f;
        const float StubLength = 0.152f, StubThickness = 0.05f;   // landing 6's short guard rail
        const float SideClearStart = 0.3f;    // and at least this far from the wall's plane at a grazing start, m
        const float SweepDegrees = 60f, SweepDegPerSec = 90f;     // the moving-heading case: yaw swings this far either side of head-on
        const float CalibrationCounts = 20f;  // mouse counts sent once to measure degrees of yaw per count
        const float EntryLaneRise = 0.3f, EntryLaneRun = 0.3f;    // the short lane the player rides to take up the area's rules, m
        const float EntrySeconds = 10f;       // at most this long to ride it
        const float FloorThickness = 4f, FloorSpans = 2.5f;        // the floor: this thick, and this many times the area's span across

        enum Target { FlatWall, InsideCorner, WallEnd }
        enum Mode { TowerWalk, Walk, Sprint }

        class Case
        {
            public Target target; public Mode mode; public float angle; public string layer; public bool sweep;
            public float radiusMin = float.MaxValue, radiusMax, skin, speedPeak;
            public float min = float.MaxValue, minTime, settled, minOther = float.MaxValue, firstPercentile;
            public int samples, minFrame, underRadiusPlusSkin;
            public readonly List<float> distances = new List<float>();
            public string Id => $"{target}/{(sweep ? "sweep" : angle.ToString("F0") + "deg")}/{mode}/{layer}";
        }

        Keyboard keyboard;
        Mouse mouse;
        float degPerCount;
        PlayerController player;
        CharacterController capsule;
        ClimbArea area;
        ClimbTuning tuning;
        GameObject rig;
        float savedCapture;
        readonly List<Collider> walls = new List<Collider>();

        public override void Setup()
        {
            base.Setup();
            keyboard = InputSystem.AddDevice<Keyboard>();
            mouse = InputSystem.AddDevice<Mouse>();
            savedCapture = Time.captureDeltaTime;
        }

        public override void TearDown()
        {
            Time.captureDeltaTime = savedCapture;
            if (rig != null) Object.Destroy(rig);
            if (tuning != null) Object.Destroy(tuning);
            base.TearDown();
        }

        [UnityTest, Timeout(1800000)]
        public IEnumerator WallApproach_At30Hz_IsMeasured() => Run(30);

        [UnityTest, Timeout(1800000)]
        public IEnumerator WallApproach_At60Hz_IsMeasured() => Run(60);

        [UnityTest, Timeout(1800000)]
        public IEnumerator WallApproach_At240Hz_IsMeasured() => Run(240);

        IEnumerator Run(int hz)
        {
            Time.captureDeltaTime = 1f / hz;
            SceneManager.LoadScene(LookoutTargets.WorldScene, LoadSceneMode.Single);
            yield return null;
            var tagged = GameObject.FindGameObjectsWithTag("Player");
            Assert.AreEqual(1, tagged.Length, "Expected exactly one object tagged Player");
            player = tagged[0].GetComponent<PlayerController>();
            capsule = tagged[0].GetComponent<CharacterController>();
            Assert.IsNotNull(player, "Player has no PlayerController");
            yield return null;

            Vector3 origin = player.transform.position + Vector3.up * RigHeight;
            BuildRig(origin);
            // Degrees of yaw per mouse count, measured once (the moving-heading cases turn by mouse, as a player does).
            float yawBefore = player.Yaw;
            Set(mouse.delta, new Vector2(CalibrationCounts, 0f), queueEventOnly: true);
            yield return null;
            yield return null;
            degPerCount = Mathf.Abs(Mathf.DeltaAngle(yawBefore, player.Yaw)) / CalibrationCounts;
            Assert.Greater(degPerCount, 0f, "Mouse delta did not turn the player");
            var cases = Cases();
            var frames = new StringBuilder("case,frame,time,distance,otherDistance,radius,speed\n");
            for (int i = 0; i < cases.Count; i++)
            {
                yield return RunCase(cases[i], origin, i, frames);
                Write(hz, cases, i + 1, frames);
            }
            foreach (var c in cases) Assert.Greater(c.samples, 0, $"{c.Id}: no samples");
        }

        static List<Case> Cases()
        {
            var list = new List<Case>();
            foreach (Target target in System.Enum.GetValues(typeof(Target)))
                // Head-on, 30 and 60 degrees from it; in the corner the heading is measured from one wall's normal
                // toward the other wall, so 45 is straight into the corner and all three start inside it.
                foreach (float angle in target == Target.InsideCorner ? new[] { 30f, 45f, 60f } : new[] { 0f, 30f, 60f, 80f })
                    foreach (Mode mode in System.Enum.GetValues(typeof(Mode)))
                        list.Add(new Case { target = target, mode = mode, angle = angle, layer = "Default" });
            if (LayerMask.NameToLayer("WorldBounds") >= 0)
                foreach (float angle in new[] { 0f, 30f, 60f, 80f })
                    list.Add(new Case { target = Target.FlatWall, mode = Mode.TowerWalk, angle = angle, layer = "WorldBounds" });
            // The moving heading: W held while the yaw swings either side of head-on, on the tower capsule.
            foreach (Target target in System.Enum.GetValues(typeof(Target)))
                list.Add(new Case { target = target, mode = Mode.TowerWalk, angle = target == Target.InsideCorner ? 45f : 0f, layer = "Default", sweep = true });
            return list;
        }

        // ---- the rig ----------------------------------------------------------------------------------------------

        static Vector3 Zone(Vector3 origin, Target target) => origin + Vector3.right * (ZoneSpacing * (int)target);

        /// <summary>Foot of the entry lane: on the probe floor, half a zone to the side of the first target.</summary>
        static Vector3 EntryFoot(Vector3 origin) => origin - Vector3.right * (ZoneSpacing * 0.5f);

        void BuildRig(Vector3 origin)
        {
            walls.Clear();
            rig = new GameObject("WallApproachRig");
            float span = ZoneSpacing * 4f;
            // Wide and thick enough that a sprint case sliding off a wall for the whole hold stays on it (a player who ran off
            // the first floor fell fast enough to pass through it at the next start pose, at the coarser frame steps).
            Box("Floor", origin + new Vector3(ZoneSpacing, -FloorThickness * 0.5f, 0f), new Vector3(span * FloorSpans, FloorThickness, span * FloorSpans));
            // Every target's contact face passes through its zone's origin; the player walks in from -Z.
            Vector3 flat = Zone(origin, Target.FlatWall), corner = Zone(origin, Target.InsideCorner), stub = Zone(origin, Target.WallEnd);
            walls.Add(Box("Flat", flat + new Vector3(0f, WallHeight * 0.5f, WallThickness * 0.5f), new Vector3(WallLength, WallHeight, WallThickness)));
            walls.Add(Box("CornerA", corner + new Vector3(-WallLength * 0.5f + WallThickness, WallHeight * 0.5f, WallThickness * 0.5f), new Vector3(WallLength, WallHeight, WallThickness)));
            walls.Add(Box("CornerB", corner + new Vector3(WallThickness * 0.5f, WallHeight * 0.5f, -WallLength * 0.5f + WallThickness), new Vector3(WallThickness, WallHeight, WallLength)));
            walls.Add(Box("Stub", stub + new Vector3(0f, WallHeight * 0.5f, StubLength * 0.5f), new Vector3(StubThickness, WallHeight, StubLength)));

            tuning = ScriptableObject.CreateInstance<ClimbTuning>();
            var areaObject = new GameObject("ProbeClimbArea");
            areaObject.transform.SetParent(rig.transform, false);
            areaObject.transform.position = origin;
            area = areaObject.AddComponent<ClimbArea>();
            // One short lane with nothing built under it: the game takes up an area's rules (small capsule, area walk
            // speed) when a lane is joined, so the tower-capsule cases ride this lane first and step off its head.
            Vector3 foot = EntryFoot(origin) - origin;
            var entry = new StairLane(foot, foot + new Vector3(0f, EntryLaneRise, EntryLaneRun), 0.61f);
            area.Configure(tuning, new[] { entry }, new Bounds(new Vector3(ZoneSpacing, 2f, 0f), new Vector3(span, 8f, span)));
        }

        Collider Box(string name, Vector3 centre, Vector3 size)
        {
            var box = new GameObject(name);
            box.transform.SetParent(rig.transform, false);
            box.transform.position = centre;
            var collider = box.AddComponent<BoxCollider>();
            collider.size = size;
            return collider;
        }

        // ---- input and area entry ---------------------------------------------------------------------------------

        /// <summary>The whole keyboard in one state: exactly these keys down. (Two separate presses in one frame lost the first.)</summary>
        void Keys(params Key[] down) => InputSystem.QueueStateEvent(keyboard, new KeyboardState(down));

        void Place(Vector3 feet, float yaw)
        {
            // The start pose: disable, write, enable, as Respawn does.
            capsule.enabled = false;
            player.transform.SetPositionAndRotation(feet, Quaternion.Euler(0f, yaw, 0f));
            capsule.enabled = true;
        }

        /// <summary>Walks onto the entry lane, rides it and steps off its head, which leaves the player in the area on the tower capsule.</summary>
        IEnumerator EnterArea(Vector3 origin, string id)
        {
            StairLane entry = area.Lane(0);
            Place(entry.Point(-ClimbRules.FootRun(entry, tuning) * 0.5f, 0f) + Vector3.up * (capsule.skinWidth + 0.1f), 0f);
            for (float t = 0f; t < SettleSeconds && !player.IsSupported; t += Time.deltaTime) yield return null;
            Keys(Key.W);
            bool rode = false;
            for (float t = 0f; t < EntrySeconds; t += Time.deltaTime)
            {
                yield return null;
                rode |= player.OnLane;
                if (rode && !player.OnLane && player.IsSupported) break;
            }
            Keys();
            yield return null;
            Assert.IsTrue(rode, $"{id}: the player never joined the entry lane (at {player.transform.position:F2})");
            Assert.IsNotNull(player.ClimbArea, $"{id}: the area's rules are not in force after riding the entry lane");
        }

        // ---- one case ---------------------------------------------------------------------------------------------

        IEnumerator RunCase(Case c, Vector3 origin, int index, StringBuilder frames)
        {
            bool inArea = c.mode == Mode.TowerWalk;
            area.gameObject.SetActive(inArea);
            int layer = LayerMask.NameToLayer(c.layer);
            foreach (var wall in walls) wall.gameObject.layer = layer;

            float speed = inArea ? tuning.towerWalkSpeed : (c.mode == Mode.Sprint ? player.SprintSpeed : player.WalkSpeed);
            float radius = inArea ? tuning.towerCapsuleRadius : player.AuthoredRadius;
            Vector3 heading = Quaternion.Euler(0f, c.angle, 0f) * Vector3.forward;
            Vector3 aim = Zone(origin, c.target);
            // Far enough back to arrive at speed, and (at a grazing angle) to start clear of the wall's plane.
            float back = radius + capsule.skinWidth + ClearStart + speed * RunUpSeconds;
            if (c.target != Target.InsideCorner) back = Mathf.Max(back, (radius + capsule.skinWidth + SideClearStart) / Mathf.Max(Mathf.Cos(c.angle * Mathf.Deg2Rad), 0.05f));
            Vector3 start = aim - heading * back + Vector3.up * (capsule.skinWidth + 0.1f);

            if (inArea && player.ClimbArea == null) yield return EnterArea(origin, c.Id);
            Place(start, c.angle);
            for (float t = 0f; t < SettleSeconds && !(player.IsSupported && Mathf.Approximately(capsule.radius, radius)); t += Time.deltaTime) yield return null;
            Assert.IsTrue(player.IsSupported, $"{c.Id}: the player never landed on the probe floor (at {player.transform.position:F2})");
            Assert.IsFalse(player.OnLane, $"{c.Id}: the probe is for ordinary floor");
            Assert.AreEqual(radius, capsule.radius, 1e-4f, $"{c.Id}: the capsule radius this mode is defined by is not in force");
            Assert.AreEqual(inArea, player.ClimbArea != null, $"{c.Id}: area rules in force");

            if (c.mode == Mode.Sprint) Keys(Key.W, Key.LeftShift); else Keys(Key.W);
            c.skin = capsule.skinWidth;
            Vector3 last = player.transform.position;
            int frame = 0;
            float swing = 1f;
            for (float t = 0f; t < HoldSeconds; t += Time.deltaTime, frame++)
            {
                if (c.sweep)
                {
                    float off = Mathf.DeltaAngle(c.angle, player.Yaw);
                    if (off >= SweepDegrees) swing = -1f; else if (off <= -SweepDegrees) swing = 1f;
                    Set(mouse.delta, new Vector2(swing * SweepDegPerSec * Time.deltaTime / degPerCount, 0f), queueEventOnly: true);
                }
                yield return null;
                Vector3 feet = player.transform.position;
                float moved = Time.deltaTime > 0f ? Flat(feet - last).magnitude / Time.deltaTime : 0f;
                last = feet;
                Distances(c.target, feet + capsule.center, out float nearest, out float other);
                c.samples++;
                c.distances.Add(nearest);
                if (nearest < capsule.radius + capsule.skinWidth) c.underRadiusPlusSkin++;
                c.radiusMin = Mathf.Min(c.radiusMin, capsule.radius);
                c.radiusMax = Mathf.Max(c.radiusMax, capsule.radius);
                c.speedPeak = Mathf.Max(c.speedPeak, moved);
                c.minOther = Mathf.Min(c.minOther, other);
                c.settled = nearest;
                if (nearest < c.min) { c.min = nearest; c.minFrame = frame; c.minTime = t; }
                frames.Append(index).Append(',').Append(frame).Append(',').Append(F(t)).Append(',').Append(F(nearest)).Append(',')
                      .Append(F(other)).Append(',').Append(F(capsule.radius)).Append(',').Append(F(moved)).Append('\n');
            }
            Keys();
            c.distances.Sort();
            if (c.distances.Count > 0) c.firstPercentile = c.distances[Mathf.FloorToInt(0.01f * (c.distances.Count - 1))];
            yield return null;
        }

        /// <summary>
        /// Horizontal distance from the capsule's axis to the nearest point of the case's wall, and (in the corner) to
        /// the farther of its two walls. Against a flat wall this is the distance to the wall's plane.
        /// </summary>
        void Distances(Target target, Vector3 centre, out float nearest, out float other)
        {
            float a = Distance(walls[target == Target.FlatWall ? 0 : (target == Target.InsideCorner ? 1 : 3)], centre);
            float b = target == Target.InsideCorner ? Distance(walls[2], centre) : a;
            nearest = Mathf.Min(a, b);
            other = Mathf.Max(a, b);
        }

        static float Distance(Collider wall, Vector3 centre) => Flat(wall.ClosestPoint(centre) - centre).magnitude;

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        // ---- results ----------------------------------------------------------------------------------------------

        static string F(float value) => value.ToString("F4", CultureInfo.InvariantCulture);

        void Write(int hz, List<Case> cases, int done, StringBuilder frames)
        {
            string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "TestResults", "tower-eng"));
            Directory.CreateDirectory(dir);
            var json = new StringBuilder();
            json.Append("{\n  \"probe\": \"wall approach: calibration, not a result of record\",\n")
                .Append($"  \"captureDeltaTime\": \"1/{hz}\",\n  \"holdSeconds\": {F(HoldSeconds)},\n")
                .Append($"  \"towerCapsuleRadius\": {F(tuning.towerCapsuleRadius)},\n  \"towerWalkSpeed\": {F(tuning.towerWalkSpeed)},\n")
                .Append($"  \"authoredRadius\": {F(player.AuthoredRadius)},\n  \"walkSpeed\": {F(player.WalkSpeed)},\n  \"sprintSpeed\": {F(player.SprintSpeed)},\n")
                .Append($"  \"height\": {F(capsule.height)},\n  \"stepOffset\": {F(capsule.stepOffset)},\n  \"slopeLimit\": {F(capsule.slopeLimit)},\n")
                .Append("  \"tuningSource\": \"ClimbTuning class defaults (CreateInstance), not the asset\",\n")
                .Append("  \"distance\": \"horizontal, capsule axis to the nearest point of the wall; in the corner the nearer of its two walls (otherMin = the farther)\",\n")
                .Append("  \"angle\": \"degrees from the wall's normal (0 = head-on); in the corner, from wall A's normal toward wall B (45 = into the corner); sweep = W held while the yaw swings 60 degrees either side of head-on at 90 deg/s\",\n")
                .Append("  \"firstPercentile\": \"the distance 1 percent of the hold's frames were at or under\",\n")
                .Append("  \"wallReachRule\": \"t-qa: the smallest min over every TowerWalk case of every frame step, stated with its case; Walk and Sprint are information for the ground end\",\n")
                .Append($"  \"casesDone\": {done},\n  \"casesTotal\": {cases.Count},\n  \"cases\": [\n");
            var table = new StringBuilder($"Wall approach probe, captureDeltaTime 1/{hz}, {done} of {cases.Count} cases. Calibration, not a result of record.\n");
            table.Append("case                                        radius  skin   min     min-(r+skin)  p1      under(r+skin)  settled  minFrame  minTime  peakSpeed  samples\n");
            for (int i = 0; i < done; i++)
            {
                var c = cases[i];
                json.Append($"    {{ \"case\": \"{c.Id}\", \"target\": \"{c.target}\", \"angleDeg\": {F(c.angle)}, \"mode\": \"{c.mode}\", \"layer\": \"{c.layer}\", ")
                    .Append($"\"radiusMin\": {F(c.radiusMin)}, \"radiusMax\": {F(c.radiusMax)}, \"skinWidth\": {F(c.skin)}, ")
                    .Append($"\"min\": {F(c.min)}, \"minOverRadiusPlusSkin\": {F(c.min - c.radiusMin - c.skin)}, \"firstPercentile\": {F(c.firstPercentile)}, \"framesUnderRadiusPlusSkin\": {c.underRadiusPlusSkin}, \"settled\": {F(c.settled)}, \"otherMin\": {F(c.minOther)}, ")
                    .Append($"\"minFrame\": {c.minFrame}, \"minTime\": {F(c.minTime)}, \"peakSpeed\": {F(c.speedPeak)}, \"samples\": {c.samples} }}")
                    .Append(i + 1 < done ? ",\n" : "\n");
                table.Append($"{c.Id,-43} {F(c.radiusMin)}  {F(c.skin)}  {F(c.min)}  {F(c.min - c.radiusMin - c.skin),12}  {F(c.firstPercentile)}  {c.underRadiusPlusSkin,13}  {F(c.settled)}   {c.minFrame,7}  {F(c.minTime)}   {F(c.speedPeak),8}  {c.samples,7}\n");
            }
            // The figure t-td's rule takes: the smallest minimum over the tower-capsule cases done so far.
            Case smallest = null;
            for (int i = 0; i < done; i++)
                if (cases[i].mode == Mode.TowerWalk && cases[i].samples > 0 && (smallest == null || cases[i].min < smallest.min)) smallest = cases[i];
            json.Append("  ],\n  \"towerWalkSmallestMin\": ").Append(smallest == null ? "null" : F(smallest.min)).Append(",\n  \"towerWalkSmallestMinCase\": \"").Append(smallest == null ? "" : smallest.Id).Append("\"\n}\n");
            if (smallest != null) table.Append($"smallest min over the TowerWalk cases so far: {F(smallest.min)} m ({smallest.Id})\n");
            File.WriteAllText(Path.Combine(dir, $"wall-approach-dt{hz}.json"), json.ToString());
            File.WriteAllText(Path.Combine(dir, $"wall-approach-dt{hz}.txt"), table.ToString());
            if (done == cases.Count) File.WriteAllText(Path.Combine(dir, $"wall-approach-dt{hz}-frames.csv"), frames.ToString());
        }
    }
}
