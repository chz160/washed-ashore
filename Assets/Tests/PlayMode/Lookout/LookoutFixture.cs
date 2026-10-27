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
using WashedAshore.Tests.Support;

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
        protected LookoutTuning lookout;
        protected TrapDoor hatch;
        protected Firefinder firefinder;
        protected WindowSash[] sashes;
        protected LookoutWatch watch;

        float savedCapture, degPerCount;
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
            hatch = root.GetComponentInChildren<TrapDoor>();
            firefinder = root.GetComponentInChildren<Firefinder>();
            sashes = root.GetComponentsInChildren<WindowSash>();
            Assert.IsNotNull(hatch, "Tower has no TrapDoor");
            Assert.IsNotNull(firefinder, "Tower has no Firefinder");
            lookout = hatch.Tuning;
            Assert.IsNotNull(lookout, "TrapDoor has no LookoutTuning");

            watch = player.gameObject.AddComponent<LookoutWatch>();
            watch.Player = player;
            yield return null;
        }

#pragma warning disable 0649 // filled by JsonUtility
        [System.Serializable] protected class ManifestTargets { public float base_square, top_square, cab_floor_y; }
        [System.Serializable] protected class ManifestFlight { public int index; public float width; public float[] foot, head; }
        [System.Serializable] protected class Manifest { public ManifestTargets targets; public ManifestFlight[] flights; }
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

        /// <summary>
        /// The start pose, through TestSpawn.Reposition: the same operations as the inline write before #63 (controller off, set the pose, on).
        /// TestSpawn.At's placement check fails on these tower poses (adr/63-climb-removal-plan-review-amend2). Logs the target, the final feet and the horizontal miss; no assert.
        /// </summary>
        protected IEnumerator StartAt(Vector3 feet, float yaw)
        {
            TestSpawn.Reposition(player, feet, yaw);
            watch.Forget();
            yield return WaitGrounded(3f);
            Vector3 end = player.transform.position;
            float miss = Flat(end - feet).magnitude;
            Debug.Log($"LookoutFixture.StartAt: target {feet:F3} -> feet {end:F3}, miss {miss:F3} m");
        }

        protected IEnumerator WaitGrounded(float timeout)
        {
            for (float t = 0f; t < timeout && !player.IsGrounded; t += Time.deltaTime) yield return null;
            Assert.IsTrue(player.IsGrounded, $"Player not on the ground after {timeout} s at {player.transform.position:F2}");
        }

        protected IEnumerator Wait(float seconds)
        {
            for (float t = 0f; t < seconds; t += Time.deltaTime) yield return null;
        }

        /// <summary>Waits a frame at a time until <paramref name="done"/> holds; fails with <paramref name="what"/> after <paramref name="timeout"/> seconds.</summary>
        protected IEnumerator Until(System.Func<bool> done, float timeout, string what)
        {
            for (float t = 0f; t < timeout; t += Time.deltaTime)
            {
                if (done()) yield break;
                yield return null;
            }
            Assert.IsTrue(done(), $"Timed out after {timeout:F0} s waiting to {what}. Player at {player.transform.position:F2}, hatch {hatch.State}. {watch.Summary()}");
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
        /// rate), until the player looks along <paramref name="direction"/> (yaw and pitch). 
        /// </summary>
        protected IEnumerator Face(Vector3 direction)
        {
            yield return Calibrate();
            float wantYaw = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
            float wantPitch = -Mathf.Asin(Mathf.Clamp(direction.normalized.y, -1f, 1f)) * Mathf.Rad2Deg;
            // The delta set at the end of a frame is applied in the next frame's Update, before this loop resumes, so the yaw and pitch read here
            // already contain it (K40 diagnostic, td part 44): the error is the measured one, with no 'pending' term. When the error is within one step the
            // correction is damped (half the error) so it converges whether the input lags by none or by one frame.
            for (float t = 0f; t < AimTimeout; t += Time.deltaTime)
            {
                var error = new Vector2(Mathf.DeltaAngle(player.Yaw, wantYaw), wantPitch - player.Pitch);
                if (Mathf.Abs(error.x) < AimDone && Mathf.Abs(error.y) < AimDone) break;
                float step = LookoutTargets.ScriptTurnDegPerSec * Time.deltaTime;
                Vector2 send = new Vector2(Correction(error.x, step), Correction(error.y, step));
                // Mouse up looks up: pitch falls as delta.y rises.
                Set(mouse.delta, new Vector2(send.x, -send.y) / degPerCount, queueEventOnly: true);
                yield return null;
            }
            yield return null;
            Assert.Less(Mathf.Abs(Mathf.DeltaAngle(player.Yaw, wantYaw)), AimDone * 3f, $"Could not turn to yaw {wantYaw:F1} (at {player.Yaw:F1})");
        }

        static float Correction(float error, float step) => Mathf.Abs(error) >= step ? Mathf.Sign(error) * step : error * 0.5f;

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
                if (d < ReachedWithin || d > best + ReachedWithin) break;
                best = Mathf.Min(best, d);
                yield return null;
            }
            Let(Key.W);
            yield return null;
        }

        protected static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        // ---- the cab ----------------------------------------------------------------------------------------------

        /// <summary>From a fresh scene: place the player in the cab, clear of the hatch, and open the hatch.</summary>
        protected IEnumerator ReachCab()
        {
            yield return LoadTower();
            // The walk up is TowerWalkTests' job; here the player is placed on the cab floor, clear of the hatch.
            var spot = ClearOfHatch;
            yield return StartAt(spot, 0f);
            // Reaching the cab means coming up through the hatch, which leaves it open (the Cab tests close it from here): open it through the real state path.
            hatch.SetState(HingeState.Open, 0f);
            yield return null;
            Debug.Log($"LookoutFixture.ReachCab: hatch opened by SetState(Open, 0f): state {hatch.State}, feet {player.transform.position:F3}");
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
