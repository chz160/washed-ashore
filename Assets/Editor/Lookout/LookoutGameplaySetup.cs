using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using WashedAshore.Gameplay;
using WashedAshore.Interaction;

namespace WashedAshore.Lookout.Editor
{
    /// <summary>
    /// Adds the gameplay parts to the built tower (spec C1-C3, I1-I4; tower-td-note §6): the climb area with its lanes,
    /// the trap door, the hinged sashes and the firefinder, all from the model's lane empties and the build manifest.
    /// TowerBuilder.Build calls <see cref="Apply(GameObject)"/> as its last step. Nothing is placed by hand: a missing
    /// node or key throws, and running it twice changes nothing. <see cref="ApplyToPlayer"/> adds the Interactor and
    /// the prompt to the Player in the open scene.
    /// </summary>
    public static class LookoutGameplaySetup
    {
        // ---- names: the one table to change if the build's names change ----
        public const string LaneFootFormat = "LANE_F{0:00}_Foot", LaneHeadFormat = "LANE_F{0:00}_Head";
        public const string ClimbTuningPath = "Assets/Settings/ClimbTuning.asset";
        public const string InteractionSettingsPath = "Assets/Settings/InteractionSettings.asset";
        public const string LookoutTuningPath = "Assets/Settings/LookoutTuning.asset";
        /// <summary>
        /// Serialized reference fields on the gameplay components that may legitimately be empty in the saved prefab. None:
        /// every reference this script sets is required, so the builder treats any null one as a failed build.
        /// </summary>
        public static readonly string[] OptionalNullFields = new string[0];
        public const string HatchId = "lookout.hatch", SashIdPrefix = "lookout.sash.", FirefinderId = "lookout.firefinder";
        public const string PlayerTag = "Player";

        /// <summary>A lane empty must match the manifest's point within this, m.</summary>
        const float LaneDataTolerance = 0.001f;

        [MenuItem("Washed Ashore/Lookout/Apply Gameplay To Selected Tower")]
        static void ApplyToSelection()
        {
            if (Selection.activeGameObject == null) throw new InvalidOperationException("Select the tower root first");
            Apply(Selection.activeGameObject);
        }

        /// <summary>Adds or refreshes every gameplay component under <paramref name="towerRoot"/> from the manifest beside the FBX.</summary>
        public static void Apply(GameObject towerRoot) => Apply(towerRoot, LookoutManifest.Load());

        public static void Apply(GameObject towerRoot, LookoutManifest m)
        {
            if (towerRoot == null) throw new ArgumentNullException(nameof(towerRoot));
            Transform root = towerRoot.transform;
            var climbTuning = LoadOrCreate<ClimbTuning>(ClimbTuningPath);
            var lookoutTuning = LoadOrCreate<LookoutTuning>(LookoutTuningPath);
            string problem = lookoutTuning.Validate();
            if (problem != null) throw new InvalidOperationException($"LookoutTuning: {problem}");

            var area = GetOrAdd<ClimbArea>(towerRoot);
            StairLane[] lanes = ReadLanes(root, m, climbTuning);
            var bounds = new Bounds();
            bounds.SetMinMax(LookoutManifest.V(m.area_min, "area_min"), LookoutManifest.V(m.area_max, "area_max"));
            area.Configure(climbTuning, lanes, bounds);
            EditorUtility.SetDirty(area);

            area.Rebuild();
            if (area.LinkProblem != null) throw new InvalidOperationException($"Lanes: {area.LinkProblem}");
            string taper = ClimbRules.TaperProblem(lanes, climbTuning);
            if (taper != null) throw new InvalidOperationException($"ClimbTuning: {taper}");
            // The hatch is on the top flight; an exit lane, if the model has one, comes after it and is linked to its head.
            int topLane = m.flights.Length - 1;
            ApplyHatch(root, m, area, topLane, lanes[topLane], lookoutTuning);
            int hinged = 0;
            foreach (var sash in m.sashes)
            {
                if (!sash.hinged) continue;
                Transform leaf = Require(root, sash.name);
                Transform proxy = Require(root, sash.collider);
                RequireCollider(proxy);
                Adopt(proxy, leaf);
                var component = GetOrAdd<WindowSash>(leaf.gameObject);
                component.Configure(SashIdPrefix + sash.name, lookoutTuning, leaf, leaf.InverseTransformDirection(root.TransformDirection(LookoutManifest.V(sash.hinge_axis, sash.name + ".hinge_axis"))));
                EditorUtility.SetDirty(component);
                hinged++;
            }
            if (hinged == 0) throw new InvalidOperationException("Lookout manifest: no hinged sash");

            // The firefinder sits on its pedestal node, with its collider proxies under it, so the aim ray finds it from any of them.
            Transform alidade = Require(root, m.firefinder.alidade);
            Transform pedestal = Require(root, m.firefinder.pedestal);
            foreach (string name in m.firefinder.colliders)
            {
                Transform proxy = Require(root, name);
                RequireCollider(proxy);
                Adopt(proxy, pedestal);
            }
            Vector3 axis = LookoutManifest.V(m.firefinder.axis, "firefinder.axis");
            var firefinder = GetOrAdd<Firefinder>(pedestal.gameObject);
            firefinder.Configure(FirefinderId, lookoutTuning, alidade, root, new Vector3(axis.x, m.targets.cab_floor_y, axis.z));
            EditorUtility.SetDirty(firefinder);
            Debug.Log($"LookoutGameplaySetup: {lanes.Length} lanes, hatch on lane {topLane + 1}, {hinged} hinged sashes, firefinder on '{pedestal.name}' under '{towerRoot.name}'.");
        }

        /// <summary>Lane ends from the FBX empties (tower-td-note §6 condition 8), checked against the manifest; width from the manifest.</summary>
        static StairLane[] ReadLanes(Transform root, LookoutManifest m, ClimbTuning climbTuning)
        {
            var lanes = new List<StairLane>();
            for (int i = 0; i < m.flights.Length; i++)
            {
                var flight = m.flights[i];
                if (flight.index != i + 1) throw new InvalidOperationException($"Lookout manifest: flights[{i}].index is {flight.index}, expected {i + 1} (bottom up)");
                Vector3 foot = root.InverseTransformPoint(Require(root, string.Format(LaneFootFormat, flight.index)).position);
                Vector3 head = root.InverseTransformPoint(Require(root, string.Format(LaneHeadFormat, flight.index)).position);
                CheckPoint(foot, LookoutManifest.V(flight.foot, "flights.foot"), $"LANE_F{flight.index:00}_Foot");
                CheckPoint(head, LookoutManifest.V(flight.head, "flights.head"), $"LANE_F{flight.index:00}_Head");
                if (head.y <= foot.y) throw new InvalidOperationException($"Lane {flight.index}: head is not above its foot");
                if (flight.width <= 0f) throw new InvalidOperationException($"Lookout manifest: flights[{i}].width missing");
                if (flight.foot_run_room < 0f) throw new InvalidOperationException($"Lookout manifest: flights[{i}].foot_run_room missing");
                if (flight.head_run_room < 0f) throw new InvalidOperationException($"Lookout manifest: flights[{i}].head_run_room missing");
                var lane = new StairLane(foot, head, flight.width, footLeavesArea: i == 0, flight.foot_run_room, flight.head_run_room);
                // A built-in run's mouth is the flight's own; a linked run-out's goes on its outermost segment below.
                if (flight.foot_run_out == null || !flight.foot_run_out.Present) SetMouth(ref lane, false, flight.foot_mouth, ClimbRules.FootRun(lane, climbTuning), $"flight {flight.index} foot");
                if (flight.head_run_out == null || !flight.head_run_out.Present) SetMouth(ref lane, true, flight.head_mouth, ClimbRules.HeadRun(lane, climbTuning), $"flight {flight.index} head");
                lanes.Add(lane);
            }
            // Exit lanes after the flights: positions from their empties, no level run of their own (the hand-over is at the head empty).
            if (m.exit_lanes != null)
                foreach (var exit in m.exit_lanes)
                {
                    Vector3 foot = root.InverseTransformPoint(Require(root, exit.foot_empty).position);
                    Vector3 head = root.InverseTransformPoint(Require(root, exit.head_empty).position);
                    CheckPoint(foot, LookoutManifest.V(exit.foot, "exit_lanes.foot"), exit.foot_empty);
                    CheckPoint(head, LookoutManifest.V(exit.head, "exit_lanes.head"), exit.head_empty);
                    if (exit.width <= 0f) throw new InvalidOperationException($"Lookout manifest: exit lane {exit.index} has no width");
                    var lane = new StairLane(foot, head, exit.width, false, 0f, 0f);
                    if (!ClimbRules.IsLevel(lane)) throw new InvalidOperationException($"Exit lane {exit.index} is not level ({lane.Pitch:F1} deg); it runs on the floor plane");
                    SetMouth(ref lane, true, exit.head_mouth, lane.Length, $"exit lane {exit.index}");
                    lanes.Add(lane);
                }
            // Linked run-outs after those: one level lane per segment, chained from the flight end to the hand-over point.
            for (int i = 0; i < m.flights.Length; i++)
            {
                var flight = m.flights[i];
                AddRunOut(root, lanes, lanes[i].head, flight.head_run_out, flight.head_mouth, flight.width, head: true, $"flight {flight.index} head");
                AddRunOut(root, lanes, lanes[i].foot, flight.foot_run_out, flight.foot_mouth, flight.width, head: false, $"flight {flight.index} foot");
            }
            return lanes.ToArray();
        }

        /// <summary>A run-out's level must match its flight end's within this, m (tower-td-note §12 condition 3).</summary>
        const float RunOutLevelTolerance = 0.01f;

        /// <summary>
        /// Level lanes for a linked run-out. Every lane runs "up" toward the flight's head side, like the flights:
        /// beyond a head the lanes lead away (foot at the junction, head toward the hand-over point); below a foot they
        /// lead in (foot toward the hand-over point, head at the junction). The outermost end is free and hands over there.
        /// </summary>
        static void AddRunOut(Transform root, List<StairLane> lanes, Vector3 laneEnd, LookoutManifest.RunOut runOut, LookoutManifest.Mouth mouth, float width, bool head, string what)
        {
            if (runOut == null || !runOut.Present) return;
            Vector3 inner = laneEnd;
            for (int k = 0; k < runOut.empties.Length; k++)
            {
                string name = runOut.empties[k];
                Vector3 outer = root.InverseTransformPoint(Require(root, name).position);
                if (Mathf.Abs(outer.y - laneEnd.y) > RunOutLevelTolerance)
                    throw new InvalidOperationException($"{name} ({what} run-out) is {outer.y - laneEnd.y:+0.000;-0.000} m off its deck; a run-out is level");
                // No built-in run on a run-out lane: its free end is the hand-over point itself.
                var lane = head ? new StairLane(inner, outer, width, false, 0f, 0f) : new StairLane(outer, inner, width, false, 0f, 0f);
                if (lane.Length <= ClimbArea.LinkTolerance) throw new InvalidOperationException($"{name} ({what} run-out) is on top of the point before it");
                // The outermost segment's free end is the hand-over point: the mouth is there.
                if (k == runOut.empties.Length - 1) SetMouth(ref lane, head, mouth, lane.Length, $"{what} run-out");
                lanes.Add(lane);
                inner = outer;
            }
        }

        /// <summary>The manifest's left_dir must point to the side this reader takes for "left" at least this closely (a dot product).</summary>
        const float SideAgreement = 0.99f;

        /// <summary>
        /// Writes a free end's reach profiles onto its lane. The manifest gives left and right standing at the free end
        /// looking toward the stair; a lane's own sides are those of someone facing up it (foot to head). The two agree
        /// at a free foot and are mirrored at a free head; the manifest's left_dir is checked against that, so a
        /// profile can't land on the wrong side. Profiles are stored never increasing from the free end.
        /// </summary>
        static void SetMouth(ref StairLane lane, bool head, LookoutManifest.Mouth mouth, float stretchLength, string what)
        {
            if (mouth == null || !mouth.Present) throw new InvalidOperationException($"Lookout manifest: no join mouth (left.reach / right.reach) for {what}");
            if (mouth.reach_spacing <= 0f) throw new InvalidOperationException($"Lookout manifest: no reach_spacing in the join mouth for {what}");
            if (Mathf.Abs(mouth.stretch_length - stretchLength) > LaneDataTolerance)
                throw new InvalidOperationException($"{what}: the final level stretch is {stretchLength:F4} m here but {mouth.stretch_length:F4} m in the manifest's join mouth");
            // Left of someone at the free end looking toward the stair: up the lane from a free foot, down it from a free head.
            Vector3 facing = head ? -lane.AxisHorizontal : lane.AxisHorizontal;
            Vector3 left = -Vector3.Cross(Vector3.up, facing);
            if (Vector3.Dot(left, LookoutManifest.V(mouth.left_dir, "mouth.left_dir").normalized) < SideAgreement)
                throw new InvalidOperationException($"{what}: the manifest's left_dir {LookoutManifest.V(mouth.left_dir, "mouth.left_dir"):F3} is not this reader's left {left:F3}");
            foreach (var side in new[] { mouth.left, mouth.right })
            {
                if ((side.reach.Length - 1) * mouth.reach_spacing < stretchLength - mouth.reach_spacing - LaneDataTolerance)
                    throw new InvalidOperationException($"{what}: {side.reach.Length} reach samples at {mouth.reach_spacing} m do not cover the {stretchLength:F3} m stretch");
                foreach (float sample in side.reach)
                    if (sample < 0f || float.IsNaN(sample)) throw new InvalidOperationException($"{what}: a reach sample is {sample}");
            }
            float[] mine = ClimbRules.NonIncreasing(head ? mouth.right.reach : mouth.left.reach), other = ClimbRules.NonIncreasing(head ? mouth.left.reach : mouth.right.reach);
            lane.reachSpacing = mouth.reach_spacing;
            if (head) { lane.headReachLeft = mine; lane.headReachRight = other; }
            else { lane.footReachLeft = mine; lane.footReachRight = other; }
        }

        static void CheckPoint(Vector3 empty, Vector3 manifest, string name)
        {
            if (Vector3.Distance(empty, manifest) > LaneDataTolerance)
                throw new InvalidOperationException($"{name} is at {empty:F4} in the FBX but {manifest:F4} in the manifest");
        }

        static void ApplyHatch(Transform root, LookoutManifest m, ClimbArea area, int laneIndex, StairLane lane, LookoutTuning tuning)
        {
            Transform leaf = Require(root, m.hatch.name);
            Transform proxy = Require(root, m.hatch.collider);
            Collider leafCollider = RequireCollider(proxy);
            Adopt(proxy, leaf);
            Transform blocker = Require(root, m.hatch.swing_volume);
            Collider swingBlocker = RequireCollider(blocker);
            swingBlocker.enabled = false;

            Vector3 a = LookoutManifest.V(m.hatch.opening_min, "hatch.opening_min"), b = LookoutManifest.V(m.hatch.opening_max, "hatch.opening_max");
            Vector3 min = Vector3.Min(a, b), max = Vector3.Max(a, b);
            // Along the top flight's run the opening is its clear length; the leaf's width (hinge to free edge) is the manifest's.
            Vector3 run = lane.AxisHorizontal;
            Vector3 size = max - min;
            float length = Mathf.Abs(run.x) * size.x + Mathf.Abs(run.z) * size.z;
            float width = m.hatch.leaf_width;
            if (length <= 0f) throw new InvalidOperationException("Lookout manifest: hatch opening has no length along the top flight");
            Vector3 hinge = leaf.InverseTransformDirection(root.TransformDirection(LookoutManifest.V(m.hatch.hinge_axis, "hatch.hinge_axis")));

            var door = GetOrAdd<TrapDoor>(leaf.gameObject);
            door.Configure(HatchId, tuning, leaf, hinge, leafCollider, swingBlocker, area, laneIndex, root, min, max, length, m.cab.floor_thickness, width);
            EditorUtility.SetDirty(door);
            EditorUtility.SetDirty(swingBlocker);
        }

        /// <summary>
        /// Adds the Interactor and the prompt to the Player's camera in the open scene and saves it. Rerunnable.
        /// After it, grep the scene YAML for "fileID: 0" on the Interactor's settings and prompt fields.
        /// </summary>
        [MenuItem("Washed Ashore/Lookout/Apply Interactor To Player")]
        public static void ApplyToPlayer()
        {
            var settings = LoadOrCreate<InteractionSettings>(InteractionSettingsPath);
            string problem = settings.Validate();
            if (problem != null) throw new InvalidOperationException($"InteractionSettings: {problem}");
            var tagged = GameObject.FindGameObjectsWithTag(PlayerTag);
            if (tagged.Length != 1) throw new InvalidOperationException($"Expected exactly one object tagged {PlayerTag} in the open scene, found {tagged.Length}");
            var player = tagged[0].GetComponent<PlayerController>();
            if (player == null) throw new InvalidOperationException("The Player has no PlayerController");
            var camera = tagged[0].GetComponentInChildren<Camera>();
            if (camera == null) throw new InvalidOperationException("The Player has no camera");

            var prompt = GetOrAdd<InteractionPrompt>(camera.gameObject);
            var interactor = GetOrAdd<Interactor>(camera.gameObject);
            interactor.Configure(settings, prompt);
            EditorUtility.SetDirty(interactor);
            EditorUtility.SetDirty(prompt);
            var scene = camera.gameObject.scene;
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException($"Could not save {scene.path}");
            Debug.Log($"LookoutGameplaySetup: Interactor and prompt on '{camera.name}' in {scene.path}.");
        }

        /// <summary>Creates the three tuning assets with their defaults where they don't exist yet, and checks them. Rerunnable; touches no scene.</summary>
        [MenuItem("Washed Ashore/Lookout/Create Tuning Assets")]
        public static void CreateTuningAssets()
        {
            var climbTuning = LoadOrCreate<ClimbTuning>(ClimbTuningPath);
            LoadOrCreate<InteractionSettings>(InteractionSettingsPath);
            var lookoutTuning = LoadOrCreate<LookoutTuning>(LookoutTuningPath);
            string problem = lookoutTuning.Validate();
            if (problem != null) throw new InvalidOperationException($"LookoutTuning: {problem}");
            if (climbTuning == null) throw new InvalidOperationException($"Could not create {ClimbTuningPath}");
            AssetDatabase.SaveAssets();
            Debug.Log($"LookoutGameplaySetup: tuning assets present at {ClimbTuningPath}, {InteractionSettingsPath}, {LookoutTuningPath}.");
        }

        /// <summary>Batchmode entry (-executeMethod): creates the tuning assets and quits with 0, or 1 on any error.</summary>
        public static void CreateTuningAssetsAndExit()
        {
            try { CreateTuningAssets(); EditorApplication.Exit(0); }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }

        // ---- helpers ----

        static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null) return asset;
            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();
            Debug.Log($"LookoutGameplaySetup: created {path} with the spec's defaults.");
            return asset;
        }

        static T GetOrAdd<T>(GameObject go) where T : Component => go.TryGetComponent(out T existing) ? existing : go.AddComponent<T>();

        /// <summary>The one descendant with this exact name. Throws when it is missing or there are two.</summary>
        static Transform Require(Transform root, string name)
        {
            if (string.IsNullOrEmpty(name)) throw new InvalidOperationException("Lookout manifest: an object name is empty");
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

        static Collider RequireCollider(Transform t)
        {
            if (!t.TryGetComponent(out Collider c)) throw new InvalidOperationException($"'{t.name}' has no collider (the builder makes colliders from the COL_/CLIP_ nodes before this runs)");
            if (c.isTrigger) throw new InvalidOperationException($"'{t.name}' is a trigger; it must be solid");
            return c;
        }

        /// <summary>Puts a mover's collider proxy under the mover, keeping its world pose, so it follows the hinge.</summary>
        static void Adopt(Transform proxy, Transform mover)
        {
            if (proxy == mover || proxy.parent == mover) return;
            proxy.SetParent(mover, true);
        }
    }
}
