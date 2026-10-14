using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using WashedAshore.Gameplay;
using WashedAshore.Lookout;
using WashedAshore.Lookout.Editor;
using WashedAshore.Tests.PlayMode.Lookout;
using WashedAshore.World;

namespace WashedAshore.Tests.EditMode.Lookout
{
    /// <summary>
    /// Spec R8 and T-C2c part (1) (tower-design-spec.md §9, as amended): a sphere of radius eyeClearance at eye height,
    /// swept along every lane across the full lateral slack (with the duck curve on the top lane), over every point
    /// the small capsule's centre can reach on each landing, and round the cab, finds no visual mesh. No exceptions.
    /// The smallest clearance found is reported per lane and landing, with the slack applied on both sides, so the
    /// graded number is the worst case (tower-td-note). Part (2): the 0.61 x 1.90 m corridor along every lane finds no
    /// visual mesh either; no exception is granted today, so any intrusion fails and is reported with its depth.
    /// </summary>
    public class LookoutEyeClearanceTests
    {
        const string ScenePath = "Assets/Scenes/World.unity";
        const float LaneStep = 0.02f;        // sample spacing along a lane, m (a script constant)
        const float FloorStep = 0.05f;       // sample grid on landings and in the cab, m (a script constant)
        const float SearchRadius = 0.5f;     // clearances above this are reported as this, m (a script constant)
        const int Bisections = 10;           // 0.5 m / 2^10: the reported minimum is good to half a millimetre
        const float CorridorFloor = 0.05f;   // the corridor starts this far above the nosing line, m (t-artist's sweep uses the same; treads are below it)

        Transform tower;
        ClimbArea area;
        TrapDoor hatch;
        CharacterController capsule;
        LookoutManifest manifest;
        float standingEye, nearPlane, fov;
        int renderMask;
        readonly List<Object> temp = new List<Object>();

        [OneTimeSetUp]
        public void OpenWorld()
        {
            if (SceneManager.GetActiveScene().path != ScenePath) EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var root = GameObject.Find(LookoutTargets.TowerRoot);
            Assert.IsNotNull(root, $"No '{LookoutTargets.TowerRoot}' in {ScenePath}");
            tower = root.transform;
            area = root.GetComponentInChildren<ClimbArea>();
            hatch = root.GetComponentInChildren<TrapDoor>();
            Assert.IsNotNull(area, "Tower has no ClimbArea");
            Assert.IsNotNull(hatch, "Tower has no TrapDoor");
            area.Rebuild();
            var player = GameObject.FindGameObjectWithTag("Player");
            Assert.IsNotNull(player, "No Player in World");
            capsule = player.GetComponent<CharacterController>();
            var camera = player.GetComponentInChildren<Camera>();
            Assert.IsNotNull(camera, "The Player has no camera");
            standingEye = camera.transform.localPosition.y;
            nearPlane = camera.nearClipPlane;
            fov = camera.fieldOfView;
            manifest = LookoutManifest.Load();
        }

        [SetUp]
        public void AddRenderProbes()
        {
            // Render meshes can't be asked for a distance, so each gets a temporary mesh collider on the Water layer.
            foreach (var filter in tower.GetComponentsInChildren<MeshFilter>())
            {
                if (filter.GetComponent<Renderer>() == null || filter.sharedMesh == null) continue;
                var go = new GameObject("RenderProbe_" + filter.name) { layer = WorldLayers.Water };
                go.transform.SetParent(filter.transform, false);
                go.AddComponent<MeshCollider>().sharedMesh = filter.sharedMesh;
                temp.Add(go);
            }
            Assert.IsNotEmpty(temp, "no render meshes under the tower");
            renderMask = 1 << WorldLayers.Water;
            Physics.SyncTransforms();
        }

        [TearDown]
        public void RemoveProbes()
        {
            foreach (var o in temp) if (o) Object.DestroyImmediate(o);
            temp.Clear();
            Physics.SyncTransforms();
        }

        /// <summary>The largest sphere at <paramref name="eye"/> that touches no render mesh, up to SearchRadius; and the mesh that limits it.</summary>
        float Clearance(Vector3 eye, out string nearest)
        {
            nearest = null;
            if (!Physics.CheckSphere(eye, SearchRadius, renderMask, QueryTriggerInteraction.Ignore)) return SearchRadius;
            float lo = 0f, hi = SearchRadius;
            for (int i = 0; i < Bisections; i++)
            {
                float mid = (lo + hi) * 0.5f;
                if (Physics.CheckSphere(eye, mid, renderMask, QueryTriggerInteraction.Ignore)) hi = mid; else lo = mid;
            }
            var hits = Physics.OverlapSphere(eye, hi, renderMask, QueryTriggerInteraction.Ignore);
            if (hits.Length > 0) nearest = hits[0].transform.parent != null ? hits[0].transform.parent.name : hits[0].name;
            return lo;
        }

        float EyeHeightOnLane(int index, in StairLane lane, float u, LookoutTuning t)
        {
            if (index != hatch.LaneIndex) return standingEye;
            float pass = LookoutRules.PassEyeHeight(hatch.OpeningLength, lane.Pitch, hatch.FloorThickness, t.eyeClearance, t.hatchDuckMinEyeHeight, standingEye);
            float run = Mathf.Max(0f, lane.Length - u) * lane.RunPerMetre;
            return LookoutRules.HatchEyeHeight(run, hatch.OpeningLength, lane.Pitch, pass, standingEye, t.hatchDuckRun, t.eyeClearance);
        }

        void Write(string file, List<string> rows)
        {
            string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "TestResults", "tower-eng"));
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, file),
                $"{{\"eyeClearance\":{hatch.Tuning.eyeClearance},\"eyeHeight\":{standingEye},\"nearPlane\":{nearPlane},\"fov\":{fov},\"rows\":[\n" + string.Join(",\n", rows) + "]}");
        }

        [Test]
        public void OnEveryLane_TheEyeIsClearOfEveryVisualMesh()
        {
            var t = hatch.Tuning;
            float slack = area.Tuning.laneLateralSlack, need = t.eyeClearance;
            var rows = new List<string>();
            var failures = new List<string>();
            // With the hatch open: the leaf stands against the wall, out of the top lane's way, as it is whenever the lane passes the floor.
            Quaternion leafPose = hatch.transform.localRotation;
            hatch.SetState(HingeState.Open, 0f);
            Physics.SyncTransforms();
            try
            {
                for (int i = 0; i < area.LaneCount; i++)
                {
                    StairLane lane = area.Lane(i);
                    float min = float.MaxValue, minU = 0f, minLateral = 0f;
                    string minMesh = null;
                    // The level run beyond each end is part of the lane: swept with it.
                    float from = -ClimbRules.FootRun(lane, area.Tuning), to = lane.Length + ClimbRules.HeadRun(lane, area.Tuning);
                    foreach (float lateral in new[] { -slack, 0f, slack })
                        for (float u = from; u <= to; u += LaneStep)
                        {
                            Vector3 eye = lane.Point(u, lateral) + Vector3.up * (capsule.skinWidth + EyeHeightOnLane(i, lane, u, t));
                            float clear = Clearance(eye, out string mesh);
                            if (clear < min) { min = clear; minU = u; minLateral = lateral; minMesh = mesh; }
                        }
                    rows.Add($"{{\"lane\":{i + 1},\"minClearance\":{min:F4},\"atDistance\":{minU:F2},\"atLateral\":{minLateral:F2},\"nearestMesh\":\"{minMesh}\"}}");
                    Debug.Log($"LookoutEyeClearanceTests: lane {i + 1}: minimum eye clearance {min:F3} m at u {minU:F2}, lateral {minLateral:+0.00;-0.00}, nearest {minMesh}");
                    if (min < need) failures.Add($"lane {i + 1}: {min:F3} m at u {minU:F2} m, lateral {minLateral:+0.00;-0.00} m, nearest {minMesh}");
                }
            }
            finally
            {
                hatch.SetState(HingeState.Closed, 0f);
                hatch.transform.localRotation = leafPose;
                Physics.SyncTransforms();
            }
            Write("c2c-eye-lanes.json", rows);
            Assert.IsEmpty(failures, $"eye within {need} m of a visual mesh (R8, T-C2c part 1):\n" + string.Join("\n", failures));
        }

        /// <summary>True when the small capsule, skin included, can stand with its centre here (nothing solid or invisible in the way).</summary>
        bool CanStand(Vector3 feet)
        {
            float r = area.Tuning.towerCapsuleRadius + capsule.skinWidth;
            Vector3 bottom = feet + Vector3.up * (r + area.Tuning.capsuleFitLift), top = feet + Vector3.up * (capsule.height - r);
            var hits = Physics.OverlapCapsule(bottom, top, r, WorldLayers.SightMask | (1 << WorldLayers.WorldBounds), QueryTriggerInteraction.Ignore);
            foreach (var hit in hits) if (hit != capsule) return false;
            // And there is a floor under it.
            return Physics.Raycast(feet + Vector3.up * capsule.stepOffset, Vector3.down, capsule.stepOffset * 2f, WorldLayers.SightMask, QueryTriggerInteraction.Ignore);
        }

        /// <summary>The landings and the cab, with the hatch closed (the leaf is floor) and open (the leaf stands against the wall).</summary>
        [Test]
        public void OnEveryLandingAndInTheCab_TheEyeIsClearOfEveryVisualMesh([Values(false, true)] bool hatchOpen)
        {
            float need = hatch.Tuning.eyeClearance;
            var rows = new List<string>();
            var failures = new List<string>();
            Quaternion leafPose = hatch.transform.localRotation;
            hatch.SetState(hatchOpen ? HingeState.Open : HingeState.Closed, 0f);
            Physics.SyncTransforms();
            try { SweepFloors(need, rows, failures); }
            finally
            {
                hatch.SetState(HingeState.Closed, 0f);
                hatch.transform.localRotation = leafPose;
                Physics.SyncTransforms();
            }
            Write(hatchOpen ? "c2c-eye-floors-hatch-open.json" : "c2c-eye-floors-hatch-closed.json", rows);
            Assert.IsEmpty(failures, $"eye within {need} m of a visual mesh (R8, T-C2c part 1), hatch {(hatchOpen ? "open" : "closed")}:\n" + string.Join("\n", failures));
        }

        void SweepFloors(float need, List<string> rows, List<string> failures)
        {
            // Floors: each landing at its lane head's height, and the cab floor; sampled over the area's plan at that height.
            for (int i = 0; i < area.LaneCount; i++)
            {
                float y = area.Lane(i).head.y;
                bool cab = i == area.LaneCount - 1;
                string name = cab ? "cab" : $"landing {i + 1}";
                Vector3 local = tower.InverseTransformPoint(area.Lane(i).head);
                float half = Mathf.Lerp(manifest.targets.base_square, manifest.targets.top_square, Mathf.Clamp01(local.y / manifest.targets.cab_floor_y)) * 0.5f;
                float min = float.MaxValue;
                Vector3 minAt = Vector3.zero;
                string minMesh = null;
                int standable = 0;
                for (float x = -half; x <= half; x += FloorStep)
                    for (float z = -half; z <= half; z += FloorStep)
                    {
                        Vector3 feet = tower.TransformPoint(new Vector3(x, local.y, z)) + Vector3.up * capsule.skinWidth;
                        feet.y = y + capsule.skinWidth;
                        if (!CanStand(feet)) continue;
                        standable++;
                        float clear = Clearance(feet + Vector3.up * standingEye, out string mesh);
                        if (clear < min) { min = clear; minAt = feet; minMesh = mesh; }
                    }
                Assert.Greater(standable, 0, $"nowhere to stand on {name}");
                rows.Add($"{{\"floor\":\"{name}\",\"standablePoints\":{standable},\"minClearance\":{min:F4},\"at\":\"{tower.InverseTransformPoint(minAt):F2}\",\"nearestMesh\":\"{minMesh}\"}}");
                Debug.Log($"LookoutEyeClearanceTests: {name}: {standable} standing points, minimum eye clearance {min:F3} m, nearest {minMesh}");
                if (min < need) failures.Add($"{name}: {min:F3} m at {tower.InverseTransformPoint(minAt):F2} (tower space), nearest {minMesh}");
            }
        }

        /// <summary>
        /// T-C2c part (2), along the lanes: a box the corridor's width and height (from the build manifest), standing on
        /// the nosing line, finds no visual mesh. Each intrusion is reported with where it is and how deep it reaches
        /// in from the corridor's side. No exception exists today, so any intrusion fails.
        /// </summary>
        [Test]
        public void OnEveryLane_TheCorridorHoldsNoVisualMesh()
        {
            Assert.IsNotNull(manifest.corridor, "manifest corridor");
            float width = manifest.corridor.width, height = manifest.corridor.height;
            Assert.Greater(width, 0f, "manifest corridor.width");
            Assert.Greater(height, CorridorFloor, "manifest corridor.height");
            var rows = new List<string>();
            var failures = new List<string>();
            Quaternion leafPose = hatch.transform.localRotation;
            hatch.SetState(HingeState.Open, 0f);
            Physics.SyncTransforms();
            try
            {
                for (int i = 0; i < area.LaneCount; i++)
                {
                    StairLane lane = area.Lane(i);
                    var facing = Quaternion.LookRotation(lane.AxisHorizontal, Vector3.up);
                    float from = float.NegativeInfinity, deepest = 0f, deepestAt = 0f;
                    string mesh = null;
                    // The level run beyond each end is part of the lane: swept with it.
                    float first = -ClimbRules.FootRun(lane, area.Tuning), last = lane.Length + ClimbRules.HeadRun(lane, area.Tuning);
                    for (float u = first; u <= last + LaneStep; u += LaneStep)
                    {
                        string hit = null;
                        bool inside = u <= last && Box(lane, u, width, height, facing, out hit);
                        if (inside)
                        {
                            if (from < -1e5f) { from = u; deepest = 0f; mesh = hit; }
                            // How far in from the corridor's side: narrow the box until it is clear.
                            float lo = 0f, hi = width;
                            for (int b = 0; b < Bisections; b++)
                            {
                                float mid = (lo + hi) * 0.5f;
                                if (Box(lane, u, mid, height, facing, out _)) hi = mid; else lo = mid;
                            }
                            float depth = (width - lo) * 0.5f;
                            if (depth > deepest) { deepest = depth; deepestAt = u; }
                        }
                        else if (from > -1e5f)
                        {
                            rows.Add($"{{\"lane\":{i + 1},\"alongFrom\":{from:F2},\"alongTo\":{u - LaneStep:F2},\"deepest\":{deepest:F4},\"deepestAt\":{deepestAt:F2},\"mesh\":\"{mesh}\"}}");
                            failures.Add($"lane {i + 1}: {mesh} inside the corridor from {from:F2} to {u - LaneStep:F2} m along, up to {deepest:F3} m in from its side");
                            from = float.NegativeInfinity;
                        }
                    }
                }
            }
            finally
            {
                hatch.SetState(HingeState.Closed, 0f);
                hatch.transform.localRotation = leafPose;
                Physics.SyncTransforms();
            }
            Write("c2c-corridor-lanes.json", rows);
            Assert.IsEmpty(failures, $"visual mesh inside the {width} x {height} m corridor (T-C2c part 2; no exception is granted):\n" + string.Join("\n", failures));
        }

        /// <summary>True when a thin upright box across the lane at <paramref name="u"/>, from CorridorFloor above the nosing line to the corridor's height, touches a render mesh.</summary>
        bool Box(in StairLane lane, float u, float width, float height, Quaternion facing, out string mesh)
        {
            mesh = null;
            Vector3 centre = lane.Point(u, 0f) + Vector3.up * ((CorridorFloor + height) * 0.5f);
            var half = new Vector3(width * 0.5f, (height - CorridorFloor) * 0.5f, LaneStep * 0.5f);
            var hits = Physics.OverlapBox(centre, half, facing, renderMask, QueryTriggerInteraction.Ignore);
            if (hits.Length == 0) return false;
            mesh = hits[0].transform.parent != null ? hits[0].transform.parent.name : hits[0].name;
            return true;
        }
    }
}
