using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using WashedAshore.Gameplay;
using WashedAshore.Wildlife;

namespace WashedAshore.Tests.Wildlife
{
    /// <summary>Shared setup for the wildlife PlayMode tests: seeded scene load, the scripted walker
    /// (density brief 3.5) and the visibility rule (brief 3.4).</summary>
    static class WildlifeTestKit
    {
        public const string WorldScene = "World";

        /// <summary>designer-2: wildlife and bird PlayMode tests run at a pinned 60 fps step (the R2 target) instead of
        /// unthrottled batchmode frame rates, so NavMesh steering and the 0.2 s / 1 s probes see the same frames every run.
        /// qa-2: environment variable WA_TEST_CAPTURE_FPS overrides it (0 = unpinned); the value used is logged per fixture.
        /// Set only in the wildlife and bird test fixtures, never in game code or ProjectSettings.</summary>
        public const int TestFrameRate = 60;
        public static void PinFrameStep()
        {
            int fps = int.TryParse(System.Environment.GetEnvironmentVariable("WA_TEST_CAPTURE_FPS"), out int v) && v >= 0 ? v : TestFrameRate;
            Time.captureFramerate = fps;
            Debug.Log($"WildlifeTestKit: captureFramerate={fps} (WA_TEST_CAPTURE_FPS={System.Environment.GetEnvironmentVariable("WA_TEST_CAPTURE_FPS") ?? "unset"})");
        }
        public static void UnpinFrameStep() => Time.captureFramerate = 0;

        public static IEnumerator LoadWorld(int seed)
        {
            WildlifeRandom.OverrideSeed(seed);
            SceneManager.LoadScene(WorldScene, LoadSceneMode.Single);
            yield return null;
            yield return null;
        }

        public static PlayerController Player()
        {
            var tagged = GameObject.FindGameObjectsWithTag("Player");
            Assert.AreEqual(1, tagged.Length, "Expected exactly one object tagged Player");
            return tagged[0].GetComponent<PlayerController>();
        }

        public static WildlifePopulation Population()
        {
            var pop = Object.FindAnyObjectByType<WildlifePopulation>();
            Assert.IsNotNull(pop, "No WildlifePopulation in World");
            return pop;
        }

        /// <summary>The habitat window the population planned on (512 m around PlayerSpawn, across terrain tiles).</summary>
        public static HabitatGround Ground() => Population().Ground;

        public static IEnumerator WaitGrounded(PlayerController player, float timeout = 5f)
        {
            float waited = 0f;
            while (!player.IsGrounded && waited < timeout)
            {
                waited += Time.deltaTime;
                yield return null;
            }
            Assert.IsTrue(player.IsGrounded, "Player never grounded");
        }

        public static void Teleport(PlayerController player, Vector3 xz)
        {
            var cc = player.GetComponent<CharacterController>();
            xz.y = TerrainQuery.Height(xz) + 0.5f;
            cc.enabled = false;
            player.transform.position = xz;
            cc.enabled = true;
        }

        public static float Flat(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));

        /// <summary>One walker stall recovery: the player made less than 30% of walk speed over 1 s.
        /// type "sidestep": moved perpendicular to its heading with CharacterController.Move, then re-pathed.
        /// type "teleport": fallback after 4 failed sidesteps, moved straight to the next path corner.
        /// Times are walker game-time seconds since it started.</summary>
        [System.Serializable]
        public class StallEvent
        {
            public string type;
            public float t;
            public string goal;
            public int cornerIndex, cornerCount;
            public Vector3 from, to;
            public float sidestepRequested; // m, signed (+ = right of heading); 0 for a teleport
            public float movedInLastSecond, displacement;
        }

        /// <summary>Moves the player with CharacterController.Move along NavMesh path corners (brief 3.5).
        /// PlayerController stays enabled for gravity and grounding; with no input it adds no horizontal motion.</summary>
        public class Walker
        {
            readonly CharacterController cc;
            readonly Transform body;
            readonly float speed;
            readonly List<Vector3> corners = new List<Vector3>();
            readonly NavMeshPath path = new NavMeshPath();
            int next;
            float stallTimer;
            Vector3 stallFrom;

            public Vector3 Heading { get; private set; } = Vector3.forward;
            public int StallRecoveries { get; private set; }

            public Walker(PlayerController player, float speed)
            {
                body = player.transform;
                cc = player.GetComponent<CharacterController>();
                this.speed = speed;
                stallFrom = body.position;
            }

            public void SetGoal(Vector3 goal)
            {
                this.goal = goal;
                corners.Clear();
                next = 0;
                bool okA = NavMesh.SamplePosition(body.position, out var a, 5f, NavMesh.AllAreas);
                bool okB = NavMesh.SamplePosition(goal, out var b, 5f, NavMesh.AllAreas);
                if (okA && okB && NavMesh.CalculatePath(a.position, b.position, NavMesh.AllAreas, path)
                    && path.status != NavMeshPathStatus.PathInvalid)
                    corners.AddRange(path.corners.Skip(1));
                if (corners.Count == 0) corners.Add(goal);
            }

            /// <summary>One frame of walking. Returns true once the goal is reached.</summary>
            public bool Step(float dt)
            {
                while (next < corners.Count && Flat(body.position, corners[next]) < 0.6f) next++;
                if (next >= corners.Count) return true;

                Vector3 dir = corners[next] - body.position;
                dir.y = 0f;
                Heading = dir.normalized;
                cc.Move(Heading * (speed * dt));

                // Terrain trees and rocks aren't carved into the NavMesh, so their capsules can jam the player on
                // a path line (wl-qa ruling): sidestep perpendicular (+2, -2, +4, -4 m) and re-path to the same goal;
                // only after 4 failed sidesteps on one stall fall back to a logged teleport to the next corner.
                elapsed += dt;
                stallTimer += dt;
                if (stallTimer >= 1f)
                {
                    float moved = Flat(body.position, stallFrom);
                    if (moved < speed * 0.3f) Recover(moved);
                    else sidestepAttempt = 0;
                    stallTimer = 0f;
                    stallFrom = body.position;
                }
                return false;
            }

            static readonly float[] Sidesteps = { 2f, -2f, 4f, -4f };

            void Recover(float moved)
            {
                Vector3 from = body.position;
                var e = new StallEvent { t = elapsed, goal = GoalLabel, cornerIndex = next, cornerCount = corners.Count, from = from, movedInLastSecond = moved };
                if (sidestepAttempt < Sidesteps.Length)
                {
                    float offset = Sidesteps[sidestepAttempt++];
                    Vector3 right = Vector3.Cross(Vector3.up, Heading).normalized;
                    cc.Move(right * offset);
                    SetGoal(goal);
                    e.type = "sidestep";
                    e.sidestepRequested = offset;
                }
                else
                {
                    Teleport(body.GetComponent<PlayerController>(), corners[next]);
                    sidestepAttempt = 0;
                    e.type = "teleport";
                }
                e.to = body.position;
                e.displacement = Flat(from, body.position);
                StallRecoveries++;
                StallLog.Add(e);
            }

            int sidestepAttempt;
            Vector3 goal;

            /// <summary>Label of the current goal (e.g. route waypoint "W3"), for the stall log.</summary>
            public string GoalLabel { get; set; } = "";
            public List<StallEvent> StallLog { get; } = new List<StallEvent>();
            float elapsed;

            public void Face(float yawOffset)
            {
                float yaw = Mathf.Atan2(Heading.x, Heading.z) * Mathf.Rad2Deg + yawOffset;
                body.rotation = Quaternion.Euler(0f, yaw, 0f);
            }
        }

        // ---- Visibility (brief 3.4) ------------------------------------------------------------

        static readonly RaycastHit[] Hits = new RaycastHit[16];

        public static int OccluderMask()
        {
            int mask = 1 << LayerMask.NameToLayer("Default");
            var t = TerrainQuery.TileAtOrNearest(Vector3.zero); // every tile shares one layer
            if (t) mask |= 1 << t.gameObject.layer;
            return mask;
        }

        public static bool IsVisible(Camera cam, Plane[] planes, WildlifeAgent a, float maxDistance, int mask, Transform player)
        {
            Bounds b = a.VisualBounds;
            if (!GeometryUtility.TestPlanesAABB(planes, b)) return false;
            Vector3 eye = cam.transform.position;
            if (Vector3.Distance(eye, b.center) > maxDistance) return false;
            return Unblocked(eye, b.center, mask, player, a.transform)
                   || Unblocked(eye, new Vector3(b.center.x, b.max.y, b.center.z), mask, player, a.transform);
        }

        static bool Unblocked(Vector3 from, Vector3 to, int mask, Transform player, Transform animal)
        {
            Vector3 d = to - from;
            float len = d.magnitude;
            int n = Physics.RaycastNonAlloc(from, d / len, Hits, len, mask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var hitT = Hits[i].collider.transform;
                if (hitT.IsChildOf(player) || hitT.IsChildOf(animal)) continue; // animals and the player never occlude
                if (hitT.GetComponentInParent<WildlifeAgent>()) continue;
                return false;
            }
            return true;
        }
    }
}
