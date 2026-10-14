using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using WashedAshore.Gameplay;
using WashedAshore.Interaction;
using WashedAshore.Lookout;

namespace WashedAshore.Tests.PlayMode.Lookout
{
    /// <summary>
    /// Shared driver for the lookout PlayMode suites. The real PlayerController is driven through the Input System
    /// only (W/S/A/D, Shift, Space, E, Q, Esc, mouse delta); the transform is written once, for the start pose.
    /// Everything about the tower is found by name or type in the World scene; nothing is typed in.
    /// </summary>
    public abstract partial class LookoutFixture : InputTestFixture
    {
        const float AimDone = 0.4f;        // degrees: close enough when turning by mouse
        const float AimTimeout = 6f;       // seconds: a full turn at the script's yaw rate, three times over
        const float ReachedWithin = 0.04f; // metres: close enough when walking to a point on a landing

        protected Keyboard keyboard;
        protected Mouse mouse;
        protected PlayerController player;
        protected CharacterController capsule;
        protected Interactor interactor;
        protected Transform tower;
        protected ClimbArea area;
        protected ClimbTuning climb;
        protected LookoutTuning lookout;
        protected TrapDoor hatch;
        protected Firefinder firefinder;
        protected WindowSash[] sashes;
        protected LookoutWatch watch;

        float savedCapture, degPerCount;
        /// <summary>
        /// How many scripted waits ended on a condition sampled once per frame (walks, turns, untils). Each adds up to
        /// one frame, half a frame on average; T-C1e subtracts waits x dt / 2 from a run's total (agreed with t-qa before any run).
        /// </summary>
        protected int ConditionWaits;
        readonly List<Key> held = new List<Key>();

        protected virtual float DeltaTime => LookoutTargets.SuiteDeltaTime;

        public override void Setup()
        {
            base.Setup();
            keyboard = InputSystem.AddDevice<Keyboard>();
            mouse = InputSystem.AddDevice<Mouse>();
            savedCapture = Time.captureDeltaTime;
            Time.captureDeltaTime = DeltaTime;
        }

        public override void TearDown()
        {
            Time.captureDeltaTime = savedCapture;
            held.Clear();
            base.TearDown();
        }

        // ---- scene ------------------------------------------------------------------------------------------------

        /// <summary>Loads World, finds the player and every tower part, and attaches the per-frame watch.</summary>
        protected IEnumerator LoadTower()
        {
            SceneManager.LoadScene(LookoutTargets.WorldScene, LoadSceneMode.Single);
            yield return null;
            var tagged = GameObject.FindGameObjectsWithTag("Player");
            Assert.AreEqual(1, tagged.Length, "Expected exactly one object tagged Player");
            player = tagged[0].GetComponent<PlayerController>();
            capsule = tagged[0].GetComponent<CharacterController>();
            Assert.IsNotNull(player, "Player has no PlayerController");
            interactor = player.GetComponentInChildren<Interactor>();
            Assert.IsNotNull(interactor, "No Interactor on the Player's camera pivot (LookoutGameplaySetup.ApplyToPlayer not run?)");

            var root = GameObject.Find(LookoutTargets.TowerRoot);
            Assert.IsNotNull(root, $"No '{LookoutTargets.TowerRoot}' in {LookoutTargets.WorldScene}");
            tower = root.transform;
            area = root.GetComponentInChildren<ClimbArea>();
            hatch = root.GetComponentInChildren<TrapDoor>();
            firefinder = root.GetComponentInChildren<Firefinder>();
            sashes = root.GetComponentsInChildren<WindowSash>();
            Assert.IsNotNull(area, "Tower has no ClimbArea");
            Assert.IsNotNull(hatch, "Tower has no TrapDoor");
            Assert.IsNotNull(firefinder, "Tower has no Firefinder");
            climb = area.Tuning;
            lookout = hatch.Tuning;
            Assert.IsNotNull(climb, "ClimbArea has no ClimbTuning");
            Assert.IsNotNull(lookout, "TrapDoor has no LookoutTuning");
            Assert.Greater(area.LaneCount, 0, "ClimbArea has no lanes");

            watch = player.gameObject.AddComponent<LookoutWatch>();
            watch.Player = player;
            watch.Area = area;
            watch.Tower = tower;
            watch.AuthoredSlack = climb.laneLateralSlack;
            watch.AuthoredTaper = climb.laneTaperLength;
            watch.AuthoredGlideDegrees = climb.laneGlideMaxDegrees;
            watch.AuthoredMouthFactor = climb.laneMouthWidthFactor;
            watch.AuthoredAscendSpeed = climb.stairAscendSpeed;
            watch.AuthoredMouthHeight = climb.laneMouthHeight;
            watch.AuthoredCatchBand = climb.laneCatchBand;
            watch.HatchLane = hatch.LaneIndex;
            var built = ReadManifest();
            var plan = built.targets;
            // The detector's own lanes, from the manifest; and before anything else, the live lanes must be those lanes.
            watch.Model = BuildModel(built);
            AssertLiveLanesAreTheManifests(watch.Model);
            watch.BaseHalf = plan.base_square * 0.5f;
            watch.TopHalf = plan.top_square * 0.5f;
            watch.CabFloorY = plan.cab_floor_y;
            yield return null;
        }

#pragma warning disable 0649 // filled by JsonUtility
        [System.Serializable] protected class ManifestTargets { public float base_square, top_square, cab_floor_y; }
        [System.Serializable] protected class ManifestSide { public float[] reach; }
        [System.Serializable] protected class ManifestMouth { public float stretch_length = -1f, reach_spacing = -1f; public float[] left_dir; public ManifestSide left, right; }
        [System.Serializable] protected class ManifestRunOut { public string[] empties; public float[] points_flat; }
        [System.Serializable] protected class ManifestFlight { public int index; public float width; public float[] foot, head; public ManifestMouth foot_mouth, head_mouth; public ManifestRunOut foot_run_out, head_run_out; }
        [System.Serializable] protected class ManifestExit { public float[] foot, head; public float width; public ManifestMouth head_mouth; }
        [System.Serializable] protected class Manifest { public ManifestTargets targets; public ManifestFlight[] flights; public ManifestExit[] exit_lanes; }
#pragma warning restore 0649

        /// <summary>The build manifest beside the FBX: the tower's plan and heights for the geometric checks.</summary>
        protected static Manifest ReadManifest()
        {
            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", LookoutTargets.ManifestPath));
            Assert.IsTrue(File.Exists(path), $"No build manifest at {path}");
            var manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(path));
            Assert.IsNotNull(manifest?.targets, $"No 'targets' in {path}");
            Assert.IsNotNull(manifest.flights, $"No 'flights' in {path}");
            Assert.Greater(manifest.targets.cab_floor_y, 0f, "manifest cab_floor_y");
            return manifest;
        }

        /// <summary>The one transform write: the start pose. Disable, write, enable, as Respawn does.</summary>
        protected IEnumerator StartAt(Vector3 feet, float yaw)
        {
            capsule.enabled = false;
            player.transform.SetPositionAndRotation(feet, Quaternion.Euler(0f, yaw, 0f));
            capsule.enabled = true;
            watch.Forget();
            yield return WaitGrounded(3f);
        }

        /// <summary>Start pose at the placement script's base spawn, facing the first flight.</summary>
        protected IEnumerator StartAtBase()
        {
            var spawn = GameObject.Find(LookoutTargets.BaseSpawn);
            Assert.IsNotNull(spawn, $"No '{LookoutTargets.BaseSpawn}' in the scene");
            Vector3 p = spawn.transform.position;
            if (TerrainQuery.TryGroundHeight(p, out float ground)) p.y = Mathf.Max(p.y, ground) + capsule.skinWidth + capsule.stepOffset;
            yield return StartAt(p, spawn.transform.eulerAngles.y);
        }

        protected IEnumerator WaitGrounded(float timeout)
        {
            for (float t = 0f; t < timeout && !player.IsSupported; t += Time.deltaTime) yield return null;
            Assert.IsTrue(player.IsSupported, $"Player not on the ground or a lane after {timeout} s at {player.transform.position:F2}");
        }

        protected IEnumerator Wait(float seconds)
        {
            for (float t = 0f; t < seconds; t += Time.deltaTime) yield return null;
        }

        // ---- input ------------------------------------------------------------------------------------------------

        protected void Hold(Key key)
        {
            if (held.Contains(key)) return;
            held.Add(key);
            Press(keyboard[key]);
        }

        protected void Let(Key key)
        {
            if (!held.Remove(key)) return;
            Release(keyboard[key]);
        }

        protected void LetAll()
        {
            foreach (var key in held) Release(keyboard[key]);
            held.Clear();
        }

        /// <summary>One press and release of a key, a frame apart.</summary>
        protected IEnumerator Tap(Key key)
        {
            Press(keyboard[key]);
            yield return null;
            Release(keyboard[key]);
            yield return null;
        }

        /// <summary>Degrees of yaw per mouse count, measured once by sending a small delta.</summary>
        IEnumerator Calibrate()
        {
            if (degPerCount > 0f) yield break;
            const float probe = 20f;
            float before = player.Yaw;
            Set(mouse.delta, new Vector2(probe, 0f), queueEventOnly: true);
            yield return null;
            yield return null;
            degPerCount = Mathf.Abs(Mathf.DeltaAngle(before, player.Yaw)) / probe;
            Assert.Greater(degPerCount, 0f, "Mouse delta did not turn the player");
        }

        /// <summary>
        /// Turns by mouse, at the script's yaw rate in degrees per second (so the time taken doesn't depend on the frame
        /// rate), until the player looks along <paramref name="direction"/> (yaw and pitch). The yaw turned is in <see cref="LastTurn"/>.
        /// </summary>
        protected IEnumerator Face(Vector3 direction)
        {
            yield return Calibrate();
            float turned = 0f, lastYaw = player.Yaw;
            float wantYaw = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
            float wantPitch = -Mathf.Asin(Mathf.Clamp(direction.normalized.y, -1f, 1f)) * Mathf.Rad2Deg;
            Vector2 pending = Vector2.zero;   // sent last frame, applied this frame
            for (float t = 0f; t < AimTimeout; t += Time.deltaTime)
            {
                turned += Mathf.Abs(Mathf.DeltaAngle(lastYaw, player.Yaw));
                lastYaw = player.Yaw;
                var error = new Vector2(Mathf.DeltaAngle(player.Yaw, wantYaw), wantPitch - player.Pitch) - pending;
                if (Mathf.Abs(error.x) < AimDone && Mathf.Abs(error.y) < AimDone && pending == Vector2.zero) break;
                float step = LookoutTargets.ScriptTurnDegPerSec * Time.deltaTime;
                pending = new Vector2(Mathf.Clamp(error.x, -step, step), Mathf.Clamp(error.y, -step, step));
                // Mouse up looks up: pitch falls as delta.y rises.
                Set(mouse.delta, new Vector2(pending.x, -pending.y) / degPerCount, queueEventOnly: true);
                yield return null;
            }
            yield return null;
            ConditionWaits++;
            turned += Mathf.Abs(Mathf.DeltaAngle(lastYaw, player.Yaw));
            LastTurn = turned;
            Assert.Less(Mathf.Abs(Mathf.DeltaAngle(player.Yaw, wantYaw)), AimDone * 3f, $"Could not turn to yaw {wantYaw:F1} (at {player.Yaw:F1})");
        }

        protected float LastTurn { get; private set; }

        /// <summary>Looks at a world point from the eye.</summary>
        protected IEnumerator LookAt(Vector3 point) => Face(point - player.CameraPivot.position);

        /// <summary>Faces a point on the same floor and holds W until the feet are on it. Fails if no progress is made.</summary>
        protected IEnumerator WalkTo(Vector3 point, float timeout)
        {
            Vector3 flat = Flat(point - player.transform.position);
            if (flat.magnitude < ReachedWithin) yield break;
            yield return Face(flat);
            Hold(Key.W);
            float best = flat.magnitude;
            for (float t = 0f; t < timeout; t += Time.deltaTime)
            {
                float d = Flat(point - player.transform.position).magnitude;
                // Past the closest approach: stop rather than circle it.
                if (d < ReachedWithin || d > best + ReachedWithin || player.OnLane) break;
                best = Mathf.Min(best, d);
                yield return null;
            }
            Let(Key.W);
            ConditionWaits++;
            yield return null;
        }

        protected static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        // ---- the cab ----------------------------------------------------------------------------------------------

        /// <summary>From a fresh scene: start at the base, walk to the first flight and climb into the cab (the hatch is opened from below).</summary>
        protected IEnumerator ReachCab()
        {
            yield return LoadTower();
            yield return StartAtBase();
            yield return WalkTo(area.Lane(0).Point(-ClimbRules.FootRun(area.Lane(0), climb) * 0.5f, 0f), 15f);
            yield return ClimbUp(new ClimbLog());
        }

        /// <summary>The pedestal axis at the player's floor height.</summary>
        protected Vector3 CabCentre
        {
            get { Vector3 c = firefinder.Alidade.position; c.y = hatch.OpeningBounds.center.y; return c; }
        }

        /// <summary>Radius of the walking ring round the pedestal: the hatch centre's distance from the axis.</summary>
        protected float CabRing => Flat(hatch.OpeningBounds.center - CabCentre).magnitude;

        /// <summary>A point on the walking ring, <paramref name="degrees"/> round from the hatch (clockwise seen from above).</summary>
        protected Vector3 CabPoint(float degrees)
        {
            Vector3 toHatch = Flat(hatch.OpeningBounds.center - CabCentre).normalized;
            return CabCentre + Quaternion.Euler(0f, degrees, 0f) * toHatch * CabRing;
        }

        /// <summary>The quarter of the ring farther from the opening: a place to stand clear of the hatch's swing.</summary>
        protected Vector3 ClearOfHatch
        {
            get
            {
                Vector3 a = CabPoint(90f), b = CabPoint(-90f);
                var opening = hatch.OpeningBounds;
                return opening.SqrDistance(a) >= opening.SqrDistance(b) ? a : b;
            }
        }

        /// <summary>One swing of a hinged part, sampled every frame from the press to rest.</summary>
        protected class Swing
        {
            public float Seconds, StartAngle, EndAngle, PeakDegPerSec;
            public bool Monotonic = true;
        }

        protected IEnumerator RecordSwing(System.Func<float> angle, System.Func<HingeState> state, Swing swing)
        {
            swing.StartAngle = angle();
            float last = swing.StartAngle, direction = 0f, started = Time.time;
            yield return null;
            while (!LookoutRules.AtRest(state()))
            {
                float a = angle(), step = a - last;
                if (!Mathf.Approximately(step, 0f))
                {
                    if (Mathf.Approximately(direction, 0f)) direction = Mathf.Sign(step);
                    else if (Mathf.Sign(step) != direction) swing.Monotonic = false;
                    swing.PeakDegPerSec = Mathf.Max(swing.PeakDegPerSec, Mathf.Abs(step) / Time.deltaTime);
                }
                last = a;
                yield return null;
                Assert.Less(Time.time - started, LookoutTargets.HatchMaxSeconds * 4f, "the swing never came to rest");
            }
            swing.Seconds = Time.time - started;
            swing.EndAngle = angle();
        }

        // ---- evidence ---------------------------------------------------------------------------------------------

        /// <summary>Writes a small JSON-ish evidence file under TestResults/tower-eng/ (gitignored).</summary>
        protected static void Evidence(string file, string json)
        {
            string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "TestResults", "tower-eng"));
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, file), json);
        }

        protected void AssertClean(string run)
        {
            Debug.Log($"{GetType().Name}: {run}: {watch.Summary()}");
            Assert.IsEmpty(watch.Violations, $"{run}: {watch.Summary()}\n" + string.Join("\n", watch.Violations));
        }
    }
}
