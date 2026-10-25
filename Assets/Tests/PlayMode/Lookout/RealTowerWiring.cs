using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;
using WashedAshore.Gameplay;
using WashedAshore.Lookout;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace WashedAshore.Tests.Lookout.PlayMode
{
    /// <summary>
    /// Wires the REAL tower (the LookoutTower prefab's own geometry, its real TrapDoor, WindowSash, Hinge and Firefinder) through their
    /// public Configure calls, mirroring LookoutGameplaySetup.Apply line by line (#45, director amend2 and amend3). Apply itself cannot
    /// run here: LookoutManifest.Schema is 4 and the manifest is schema 6, and the reader is not edited. The manifest is read here,
    /// test-locally. Each statement below carries the Apply line it mirrors ("Apply :NN" is LookoutGameplaySetup.cs; "ApplyHatch :NN" and
    /// "ReadLanes :NN" are in the same file). Uses only API that exists on main, so the same bytes compile and run BEFORE and AFTER #45.
    ///
    /// The three parts are wired independently: a failure in one is recorded in its own error field and fails only the tests that need
    /// that part (the sash K2, the primary proof, never depends on the hatch).
    ///
    /// STAND-INS (S1 to S4 are the four hatch exceptions the director accepted in amend3, S5 in amend3a; the committed prefab comes from
    /// WalkableTowerBuilder, which drops the hatch's collider and swing blocker and poses the hatch leaf open):
    ///   S1  the hatch pose reset: the prefab's MC39_Hatch leaf is posed OPEN; it is reset to the authored closed pose (identity).
    ///   S2  the hatch leaf collider: COL_BOX_Hatch_01 is not in the prefab; a BoxCollider is built from the manifest's colliders[] vertices.
    ///   S3  the hatch swing blocker: CLIP_BOX_HatchSwing_01 is not in the prefab; a BoxCollider, disabled at rest, from the manifest's vertices.
    ///   S4  the hatch ClimbArea lane data: one lane (the top flight, from the real LANE_F08 empties), no mouths, no other lanes, no exit link.
    ///   S5  the lifecycle: the components are added with the tower root inactive and it is activated once after wiring, so each Awake runs
    ///       configured, as at scene load in a built tower. Added to an active root, TrapDoor.Awake runs before Configure and throws on the
    ///       still-null tuning.
    /// Everything else (the four hinged sashes, the firefinder, the leaf meshes, the walls, the tuning assets) is the real thing.
    /// </summary>
    public static class RealTowerWiring
    {
        public const string ManifestPath = "Assets/World/Lookout/MC39_Lookout.manifest.json";
        public const string TowerPrefabPath = "Assets/World/Lookout/LookoutTower.prefab";
        public const string LookoutTuningPath = "Assets/Settings/LookoutTuning.asset";   // Apply :19-22 (LookoutGameplaySetup.LookoutTuningPath)
        public const string ClimbTuningPath = "Assets/Settings/ClimbTuning.asset";
        public const string HatchId = "lookout.hatch", SashIdPrefix = "lookout.sash.", FirefinderId = "lookout.firefinder";
        const string LaneFootFormat = "LANE_F{0:00}_Foot", LaneHeadFormat = "LANE_F{0:00}_Head";
        const float LaneDataTolerance = 0.001f;
        const float BoxTolerance = 1e-4f;
        const float PrefabAgreement = 0.001f; // td F2: the manifest's collider bounds agree with the prefab's within 1 mm

        /// <summary>The sentence every log and report carries for the stand-ins.</summary>
        public const string StandInSentence = "Real tower prefab, real TrapDoor/WindowSash/Hinge/Firefinder wired through their Configure calls as LookoutGameplaySetup.Apply does; "
            + "four named hatch-only stand-ins (S1 pose reset, S2 leaf collider, S3 swing blocker, S4 one-lane ClimbArea) because the committed prefab drops the hatch collider and blocker and poses the leaf open, "
            + "and S5, the lifecycle (components added under an inactive tower root, activated once after wiring).";

        public struct Parts
        {
            public TrapDoor hatch;
            public WindowSash[] sashes;
            public Firefinder firefinder;
            public ClimbArea area;
            public string[] standIns;
            /// <summary>Null when the part wired; otherwise why it did not. Each fails only the tests that need that part.</summary>
            public string hatchError, sashError, firefinderError;
        }

#pragma warning disable 0649 // filled by JsonUtility
        [Serializable] class MHatch { public string name, collider, swing_volume; public float[] opening_min, opening_max, hinge_axis; public float open_angle_deg, leaf_width; }
        [Serializable] class MSash { public string name, collider; public bool hinged; public float[] hinge_axis; }
        [Serializable] class MFirefinder { public string pedestal, alidade; public string[] colliders; public float[] axis; }
        [Serializable] class MCab { public float floor_thickness; }
        [Serializable] class MTargets { public float cab_floor_y; }
        [Serializable] class MFlight { public int index; public float[] foot, head; public float width, foot_run_room, head_run_room; }
        [Serializable] class MManifest { public int schema; public float[] area_min, area_max; public MTargets targets; public MFlight[] flights; public MHatch hatch; public MSash[] sashes; public MFirefinder firefinder; public MCab cab; }
#pragma warning restore 0649

        static string ManifestText() => File.ReadAllText(Path.GetFullPath(Path.Combine(Application.dataPath, "..", ManifestPath)));

        static Vector3 V(float[] v, string what)   // LookoutManifest.V
        {
            if (v == null || v.Length != 3) throw new InvalidOperationException($"manifest: '{what}' is not a 3-vector");
            return new Vector3(v[0], v[1], v[2]);
        }

        static string Q(Quaternion q) => $"({q.x:F6},{q.y:F6},{q.z:F6},{q.w:F6})";
        static string P(Vector3 v) => $"({v.x:F6},{v.y:F6},{v.z:F6})";

        /// <summary>
        /// The box of the colliders[] entry named <paramref name="name"/>, from its 8 vertices (tower-local metres). JsonUtility cannot read the
        /// nested arrays, and the manifest is pretty-printed (whitespace between every token), so: find the colliders[] array of objects, then
        /// the entry by its exact name (kind, name, pivot, vertices, in that key order), allow whitespace before every bracket, and insist on exactly
        /// 8 vertices forming an axis-aligned box: two values per axis (1e-4) and 8 distinct corners (td F1, c2).
        /// </summary>
        static Bounds ColliderBounds(string json, string name)
        {
            var array = Regex.Match(json, "\"colliders\":\\s*\\[\\s*\\{");
            if (!array.Success) throw new InvalidOperationException("manifest: no colliders[] array of objects");
            var rx = new Regex("\"kind\":\\s*\"[A-Z_]+\",\\s*\"name\":\\s*\"" + Regex.Escape(name) + "\",\\s*\"pivot\":\\s*\\[[^\\]]*\\],\\s*\"vertices\":\\s*\\[\\s*((?:\\[[^\\]]*\\]\\s*,?\\s*)+)\\]");
            var m = rx.Match(json, array.Index);
            if (!m.Success) throw new InvalidOperationException($"manifest: no colliders[] entry '{name}' with vertices");
            var nums = Regex.Matches(m.Groups[1].Value, @"-?\d+(?:\.\d+)?(?:[eE][-+]?\d+)?").Cast<Match>().Select(x => float.Parse(x.Value, CultureInfo.InvariantCulture)).ToArray();
            if (nums.Length != 24) throw new InvalidOperationException($"manifest: '{name}' has {nums.Length / 3f} vertices, expected exactly 8");
            var verts = Enumerable.Range(0, 8).Select(i => new Vector3(nums[3 * i], nums[3 * i + 1], nums[3 * i + 2])).ToArray();
            var lo = verts[0]; var hi = verts[0];
            foreach (var v in verts) { lo = Vector3.Min(lo, v); hi = Vector3.Max(hi, v); }
            foreach (var v in verts)
                for (int axis = 0; axis < 3; axis++)
                    if (Mathf.Abs(v[axis] - lo[axis]) > BoxTolerance && Mathf.Abs(v[axis] - hi[axis]) > BoxTolerance)
                        throw new InvalidOperationException($"manifest: '{name}' is not an axis-aligned box (axis {axis} has a third value {v[axis]})");
            var corners = new HashSet<int>();
            foreach (var v in verts)
            {
                int key = 0;
                for (int axis = 0; axis < 3; axis++) key |= (Mathf.Abs(v[axis] - hi[axis]) < Mathf.Abs(v[axis] - lo[axis]) ? 1 : 0) << axis;
                corners.Add(key);
            }
            if (corners.Count != 8) throw new InvalidOperationException($"manifest: '{name}' does not have the 8 distinct corners of a box ({corners.Count})");
            var b = new Bounds();
            b.SetMinMax(lo, hi);
            return b;
        }

        // ---- the same helpers LookoutGameplaySetup uses (Apply helpers, :301-330) ----
        static T GetOrAdd<T>(GameObject go) where T : Component => go.TryGetComponent(out T existing) ? existing : go.AddComponent<T>();   // :301

        static Transform Require(Transform root, string name)   // :304
        {
            Transform found = null;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name != name) continue;
                if (found != null) throw new InvalidOperationException($"Two objects named '{name}' under '{root.name}'");
                found = t;
            }
            if (found == null) throw new InvalidOperationException($"No object named '{name}' under '{root.name}'");
            return found;
        }

        static Collider RequireCollider(Transform t)   // :318
        {
            if (!t.TryGetComponent(out Collider c)) throw new InvalidOperationException($"'{t.name}' has no collider");
            if (c.isTrigger) throw new InvalidOperationException($"'{t.name}' is a trigger; it must be solid");
            return c;
        }

        static void Adopt(Transform proxy, Transform mover)   // :326
        {
            if (proxy == mover || proxy.parent == mover) return;
            proxy.SetParent(mover, true);
        }

        static void CheckPoint(Vector3 empty, Vector3 manifest, string name)   // :207
        {
            if (Vector3.Distance(empty, manifest) > LaneDataTolerance)
                throw new InvalidOperationException($"{name} is at {empty:F4} in the FBX but {manifest:F4} in the manifest");
        }

        static GameObject BoxProxy(Transform root, string name, Bounds b)
        {
            var go = new GameObject(name) { layer = 0 };   // the builder gives every COL_/CLIP_ node layer 0 (WalkableTowerBuilder.cs :207)
            go.transform.SetParent(root, false);
            go.transform.localPosition = b.center;
            go.AddComponent<BoxCollider>().size = b.size;
            return go;
        }

        /// <summary>
        /// td F2: before S2 and S3 are built from manifest vertices, the same reading is checked against something the prefab really holds:
        /// COL_BOX_SashNHinged_01's manifest bounds must equal its prefab BoxCollider's tower-local bounds within 1 mm, so the vertices are
        /// tower-local and read correctly.
        /// </summary>
        static void CheckManifestAgainstPrefab(string json, GameObject prefabAsset)
        {
            const string probe = "COL_BOX_SashNHinged_01";
            var manifest = ColliderBounds(json, probe);
            Transform node = Require(prefabAsset.transform, probe);
            var box = node.GetComponent<BoxCollider>();
            if (box == null) throw new InvalidOperationException($"prefab: '{probe}' has no BoxCollider to check the manifest against");
            Transform root = prefabAsset.transform;
            Bounds prefab = default;
            bool first = true;
            for (int i = 0; i < 8; i++)
            {
                var corner = box.center + Vector3.Scale(box.size * 0.5f, new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f));
                var local = root.InverseTransformPoint(node.TransformPoint(corner));
                if (first) { prefab = new Bounds(local, Vector3.zero); first = false; } else prefab.Encapsulate(local);
            }
            if (Vector3.Distance(prefab.min, manifest.min) > PrefabAgreement || Vector3.Distance(prefab.max, manifest.max) > PrefabAgreement)
                throw new InvalidOperationException($"manifest and prefab disagree on {probe}: manifest {manifest.min:F4}..{manifest.max:F4}, prefab tower-local {prefab.min:F4}..{prefab.max:F4}");
            Debug.Log($"LookoutMovers: manifest vertices agree with the prefab's {probe} collider within 1 mm: {manifest.min:F4}..{manifest.max:F4}");
        }

        /// <summary>Wires <paramref name="towerRoot"/> (a LookoutTower prefab instance, in the scene or in memory). Each part is wired on its own; see the Parts error fields.</summary>
        public static Parts Wire(GameObject towerRoot)
        {
            if (towerRoot == null) throw new ArgumentNullException(nameof(towerRoot));   // Apply :48
#if UNITY_EDITOR
            Transform root = towerRoot.transform;   // Apply :49
            string json = ManifestText();
            var m = JsonUtility.FromJson<MManifest>(json);
            if (m == null || m.hatch == null || m.sashes == null || m.firefinder == null || m.flights == null || m.flights.Length == 0) throw new InvalidOperationException("manifest: missing hatch, sashes, firefinder or flights");
            var climbTuning = AssetDatabase.LoadAssetAtPath<ClimbTuning>(ClimbTuningPath);        // Apply :50
            var lookoutTuning = AssetDatabase.LoadAssetAtPath<LookoutTuning>(LookoutTuningPath);  // Apply :51
            if (lookoutTuning == null || climbTuning == null) throw new InvalidOperationException("tuning assets missing under Assets/Settings");
            string problem = lookoutTuning.Validate();                                             // Apply :52
            if (problem != null) throw new InvalidOperationException($"LookoutTuning: {problem}"); // Apply :53
            var prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(TowerPrefabPath);
            if (prefabAsset == null) throw new InvalidOperationException("no tower prefab at " + TowerPrefabPath);

            var parts = new Parts();
            var standIns = new List<string>();
            bool wasActive = towerRoot.activeSelf;
            towerRoot.SetActive(false); // S5: every Awake runs once, configured, when the root is activated below
            try
            {
                // ======== the hatch (Apply :55-68, ApplyHatch :213-237) ========
                try
                {
                    // The shipped open hatch leaf as the prefab holds it (amend3): printed so BEFORE and AFTER can be compared, and checked to be
                    // exactly the pose the builder assigned (WalkableTowerBuilder.cs :237), on the instance and on the asset, so "closed = identity"
                    // is proved (td c1).
                    Transform leafBeforeReset = Require(root, m.hatch.name);
                    Quaternion builderOpen = Quaternion.AngleAxis(m.hatch.open_angle_deg, V(m.hatch.hinge_axis, "hatch.hinge_axis"));
                    Debug.Log($"LookoutMovers shipped hatch leaf (instance, before the S1 reset): localPosition={P(leafBeforeReset.localPosition)} localRotation={Q(leafBeforeReset.localRotation)} localScale={P(leafBeforeReset.localScale)}; builder's open pose AngleAxis({m.hatch.open_angle_deg:F4}, axis)={Q(builderOpen)}");
                    Transform leafOnAsset = Require(prefabAsset.transform, m.hatch.name);
                    Debug.Log($"LookoutMovers shipped hatch leaf (prefab asset): localPosition={P(leafOnAsset.localPosition)} localRotation={Q(leafOnAsset.localRotation)} localScale={P(leafOnAsset.localScale)}");
                    if (Quaternion.Angle(leafBeforeReset.localRotation, builderOpen) > 0.01f)
                        throw new InvalidOperationException($"the tower instance's hatch leaf is not at AngleAxis(open_angle_deg, hinge_axis): instance {Q(leafBeforeReset.localRotation)}, builder {Q(builderOpen)}; closed = identity is not proved");
                    if (Quaternion.Angle(leafOnAsset.localRotation, builderOpen) > 0.01f)
                        throw new InvalidOperationException($"the prefab's hatch leaf is not at AngleAxis(open_angle_deg, hinge_axis): asset {Q(leafOnAsset.localRotation)}, builder {Q(builderOpen)}; closed = identity is not proved");
                    CheckManifestAgainstPrefab(json, prefabAsset);   // td F2, before S2 and S3 are built

                    // the climb area: S4, one lane (the top flight) from the real LANE_F08 empties (ReadLanes :108-116 for that flight only)
                    var top = m.flights[m.flights.Length - 1];
                    Vector3 foot = root.InverseTransformPoint(Require(root, string.Format(LaneFootFormat, top.index)).position);   // ReadLanes :108
                    Vector3 head = root.InverseTransformPoint(Require(root, string.Format(LaneHeadFormat, top.index)).position);   // ReadLanes :109
                    CheckPoint(foot, V(top.foot, "flights.foot"), string.Format(LaneFootFormat, top.index));                       // ReadLanes :110
                    CheckPoint(head, V(top.head, "flights.head"), string.Format(LaneHeadFormat, top.index));                       // ReadLanes :111
                    if (head.y <= foot.y) throw new InvalidOperationException("top lane: head is not above its foot");              // ReadLanes :112
                    var lane = new StairLane(foot, head, top.width, false, top.foot_run_room, top.head_run_room);                  // ReadLanes :116 (footLeavesArea is false: the top flight is not flight 1)
                    var area = GetOrAdd<ClimbArea>(towerRoot);                                                                     // Apply :55
                    var bounds = new Bounds();                                                                                     // Apply :57
                    bounds.SetMinMax(V(m.area_min, "area_min"), V(m.area_max, "area_max"));                                        // Apply :58
                    area.Configure(climbTuning, new[] { lane }, bounds);                                                           // Apply :59 (S4: one lane)
                    area.Rebuild();                                                                                                // Apply :62
                    if (area.LinkProblem != null) throw new InvalidOperationException($"Lanes: {area.LinkProblem}");               // Apply :63
                    standIns.Add("S4 hatch ClimbArea: one lane (flight " + top.index + "), no mouths, no other lanes, no exit-lane link");

                    Transform leaf = Require(root, m.hatch.name);                                                                  // ApplyHatch :215
                    leaf.localRotation = Quaternion.identity;                                                                      // S1 (not in Apply: the prefab leaf is open)
                    standIns.Add("S1 hatch leaf reset from its open pose to the closed pose (identity)");
                    var existing = root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == m.hatch.collider);
                    Transform proxy;
                    if (existing != null) proxy = existing;                                                                        // ApplyHatch :216 (Require)
                    else { proxy = BoxProxy(root, m.hatch.collider, ColliderBounds(json, m.hatch.collider)).transform; standIns.Add("S2 hatch leaf collider " + m.hatch.collider + " built from the manifest's vertices"); }   // S2
                    Collider leafCollider = RequireCollider(proxy);                                                                // ApplyHatch :217
                    Adopt(proxy, leaf);                                                                                            // ApplyHatch :218
                    existing = root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == m.hatch.swing_volume);
                    Transform blocker;
                    if (existing != null) blocker = existing;                                                                      // ApplyHatch :219 (Require)
                    else { blocker = BoxProxy(root, m.hatch.swing_volume, ColliderBounds(json, m.hatch.swing_volume)).transform; standIns.Add("S3 hatch swing blocker " + m.hatch.swing_volume + " built from the manifest's vertices, disabled"); }   // S3
                    Collider swingBlocker = RequireCollider(blocker);                                                              // ApplyHatch :220
                    swingBlocker.enabled = false;                                                                                  // ApplyHatch :221

                    Vector3 a = V(m.hatch.opening_min, "hatch.opening_min"), b = V(m.hatch.opening_max, "hatch.opening_max");     // ApplyHatch :223
                    Vector3 min = Vector3.Min(a, b), max = Vector3.Max(a, b);                                                      // ApplyHatch :224
                    Vector3 run = lane.AxisHorizontal;                                                                             // ApplyHatch :226
                    Vector3 size = max - min;                                                                                      // ApplyHatch :227
                    float length = Mathf.Abs(run.x) * size.x + Mathf.Abs(run.z) * size.z;                                          // ApplyHatch :228
                    float width = m.hatch.leaf_width;                                                                              // ApplyHatch :229
                    if (length <= 0f) throw new InvalidOperationException("manifest: hatch opening has no length along the top flight");   // ApplyHatch :230
                    Vector3 hinge = leaf.InverseTransformDirection(root.TransformDirection(V(m.hatch.hinge_axis, "hatch.hinge_axis")));    // ApplyHatch :231
                    var door = GetOrAdd<TrapDoor>(leaf.gameObject);                                                                // ApplyHatch :233
                    door.Configure(HatchId, lookoutTuning, leaf, hinge, leafCollider, swingBlocker, area, 0, root, min, max, length, m.cab.floor_thickness, width);   // ApplyHatch :234 (laneIndex 0: S4's only lane)
                    parts.hatch = door;
                    parts.area = area;
                }
                catch (Exception e) { parts.hatchError = e.GetType().Name + ": " + e.Message; Debug.Log("LookoutMovers: hatch wiring failed (only the hatch tests fail): " + parts.hatchError); }

                // ======== the hinged sashes (Apply :69-82): fully real ========
                try
                {
                    var sashes = new List<WindowSash>();
                    foreach (var sash in m.sashes)                                                                                 // Apply :70
                    {
                        if (!sash.hinged) continue;                                                                                // Apply :72
                        Transform sashLeaf = Require(root, sash.name);                                                             // Apply :73
                        Transform sashProxy = Require(root, sash.collider);                                                        // Apply :74
                        RequireCollider(sashProxy);                                                                                // Apply :75
                        Adopt(sashProxy, sashLeaf);                                                                                // Apply :76
                        var component = GetOrAdd<WindowSash>(sashLeaf.gameObject);                                                 // Apply :77
                        component.Configure(SashIdPrefix + sash.name, lookoutTuning, sashLeaf, sashLeaf.InverseTransformDirection(root.TransformDirection(V(sash.hinge_axis, sash.name + ".hinge_axis"))));   // Apply :78
                        sashes.Add(component);
                    }
                    if (sashes.Count == 0) throw new InvalidOperationException("manifest: no hinged sash");                        // Apply :82
                    parts.sashes = sashes.ToArray();
                }
                catch (Exception e) { parts.sashError = e.GetType().Name + ": " + e.Message; Debug.Log("LookoutMovers: sash wiring failed: " + parts.sashError); }

                // ======== the firefinder (Apply :85-95): fully real ========
                try
                {
                    Transform alidade = Require(root, m.firefinder.alidade);                                                       // Apply :85
                    Transform pedestal = Require(root, m.firefinder.pedestal);                                                     // Apply :86
                    foreach (string name in m.firefinder.colliders)                                                                // Apply :87
                    {
                        Transform p = Require(root, name);                                                                         // Apply :89
                        RequireCollider(p);                                                                                        // Apply :90
                        Adopt(p, pedestal);                                                                                        // Apply :91
                    }
                    Vector3 axis = V(m.firefinder.axis, "firefinder.axis");                                                        // Apply :93
                    var firefinder = GetOrAdd<Firefinder>(pedestal.gameObject);                                                    // Apply :94
                    firefinder.Configure(FirefinderId, lookoutTuning, alidade, root, new Vector3(axis.x, m.targets.cab_floor_y, axis.z));   // Apply :95
                    parts.firefinder = firefinder;
                }
                catch (Exception e) { parts.firefinderError = e.GetType().Name + ": " + e.Message; Debug.Log("LookoutMovers: firefinder wiring failed: " + parts.firefinderError); }

                standIns.Add("S5 lifecycle: components added with the tower root inactive; activated once after wiring");
                parts.standIns = standIns.ToArray();
                return parts;
            }
            finally { towerRoot.SetActive(wasActive); }
#else
            throw new NotSupportedException("RealTowerWiring needs the Editor (AssetDatabase)");
#endif
        }
    }
}
