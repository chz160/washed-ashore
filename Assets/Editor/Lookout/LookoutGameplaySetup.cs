using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using WashedAshore.Gameplay;
using WashedAshore.Interaction;
using WashedAshore.World;

namespace WashedAshore.Lookout.Editor
{
    /// <summary>
    /// Adds the interactions to the built walkable tower (issue #40; td ruling adr/lookout-interactions-40): the trap door, the
    /// hinged sashes and the firefinder, from the model's nodes and the build manifest. There is no guided path:
    /// the closed hatch stops the player by its leaf collider. WalkableTowerBuilder.Build calls <see cref="Apply(GameObject)"/>
    /// before it saves the prefab. Nothing is placed by hand: a missing node or key throws, and running it twice changes
    /// nothing. <see cref="ApplyToPlayer"/> adds the Interactor and the prompt to the Player in the open scene.
    /// </summary>
    public static class LookoutGameplaySetup
    {
        public const string InteractionSettingsPath = "Assets/Settings/InteractionSettings.asset";
        public const string LookoutTuningPath = "Assets/Settings/LookoutTuning.asset";
        public const string HatchId = "lookout.hatch", SashIdPrefix = "lookout.sash.", FirefinderId = "lookout.firefinder";
        public const string PlayerTag = "Player";
        public const string CabBandPrefix = "CLIP_BOX_CabBand_";
        public const int CabBandCount = 4;
        /// <summary>Eye to the leaf's underside the stop must leave at the stopped position, m (director L2, near-plane-eye rule).</summary>
        public const float EyeToLeafMin = 0.15f;

        [MenuItem("Washed Ashore/Lookout/Apply Gameplay To Selected Tower")]
        static void ApplyToSelection()
        {
            if (Selection.activeGameObject == null) throw new InvalidOperationException("Select the tower root first");
            Apply(Selection.activeGameObject);
        }

        /// <summary>Adds or refreshes every interaction component under <paramref name="towerRoot"/> from the manifest beside the FBX.</summary>
        public static void Apply(GameObject towerRoot) => Apply(towerRoot, LookoutManifest.Load());

        public static void Apply(GameObject towerRoot, LookoutManifest m)
        {
            if (towerRoot == null) throw new ArgumentNullException(nameof(towerRoot));
            Transform root = towerRoot.transform;
            var lookoutTuning = LoadRequired<LookoutTuning>(LookoutTuningPath);
            string problem = lookoutTuning.Validate();
            if (problem != null) throw new InvalidOperationException($"LookoutTuning: {problem}");
            int interactable = WorldLayers.Interactable, bounds = WorldLayers.WorldBounds;

            ApplyHatch(root, m, lookoutTuning, interactable, bounds);
            // K1 (ADR part 10): the cab-band clip walls are player-only walls; off Default, the aim ray reaches the sash proxies inside them.
            int bands = 0;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name.StartsWith(CabBandPrefix, StringComparison.Ordinal)) { RequireCollider(t); t.gameObject.layer = bounds; bands++; }
            if (bands != CabBandCount) throw new InvalidOperationException($"Expected {CabBandCount} '{CabBandPrefix}*' nodes, found {bands}");
            int hinged = 0;
            foreach (var sash in m.sashes)
            {
                if (!sash.hinged) continue;
                Transform leaf = Require(root, sash.name);
                Transform proxy = Require(root, sash.collider);
                RequireCollider(proxy);
                Adopt(proxy, leaf);
                proxy.gameObject.layer = interactable;
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
                proxy.gameObject.layer = interactable;
            }
            Vector3 axis = LookoutManifest.V(m.firefinder.axis, "firefinder.axis");
            var firefinder = GetOrAdd<Firefinder>(pedestal.gameObject);
            firefinder.Configure(FirefinderId, lookoutTuning, alidade, root, new Vector3(axis.x, m.cab.floor_y, axis.z));   // walking surface (manifest floor_note), not the steel datum (targets.cab_floor_y)
            EditorUtility.SetDirty(firefinder);
            Debug.Log($"LookoutGameplaySetup: hatch, {hinged} hinged sashes, firefinder on '{pedestal.name}' under '{towerRoot.name}'.");
        }

        /// <summary>
        /// The leaf stays as the model has it (closed): Hinge takes the authored rotation as closed. The leaf collider goes under
        /// the leaf on Interactable; the swing blocker stays outside it, on WorldBounds, disabled at rest (ADR R1a, R2).
        /// </summary>
        static void ApplyHatch(Transform root, LookoutManifest m, LookoutTuning tuning, int interactable, int bounds)
        {
            Transform leaf = Require(root, m.hatch.name);
            Transform proxy = Require(root, m.hatch.collider);
            Collider leafCollider = RequireCollider(proxy);
            Adopt(proxy, leaf);
            proxy.gameObject.layer = interactable;
            Transform blocker = Require(root, m.hatch.swing_volume);
            if (blocker == leaf || blocker.IsChildOf(leaf)) throw new InvalidOperationException($"'{blocker.name}' is under the hatch leaf; the swing blocker must stay outside it");
            Collider swingBlocker = RequireCollider(blocker);
            swingBlocker.enabled = false;
            blocker.gameObject.layer = bounds;

            Vector3 a = LookoutManifest.V(m.hatch.opening_min, "hatch.opening_min"), b = LookoutManifest.V(m.hatch.opening_max, "hatch.opening_max");
            Vector3 min = Vector3.Min(a, b), max = Vector3.Max(a, b);
            // Along the top flight's run the opening is its clear length; the leaf's width (hinge to free edge) is the manifest's.
            var top = m.flights[m.flights.Length - 1];
            Vector3 run = LookoutManifest.V(top.head, "top flight head") - LookoutManifest.V(top.foot, "top flight foot");
            run.y = 0f;
            if (run.sqrMagnitude < 1e-6f) throw new InvalidOperationException("Lookout manifest: the top flight has no horizontal run");
            run.Normalize();
            Vector3 size = max - min;
            float length = Mathf.Abs(run.x) * size.x + Mathf.Abs(run.z) * size.z;
            if (length <= 0f) throw new InvalidOperationException("Lookout manifest: hatch opening has no length along the top flight");
            Vector3 hinge = leaf.InverseTransformDirection(root.TransformDirection(LookoutManifest.V(m.hatch.hinge_axis, "hatch.hinge_axis")));

            var walk = root.GetComponent<TowerWalkZones>();
            if (walk == null) throw new InvalidOperationException("The tower has no TowerWalkZones; the headroom stop's depth needs its capsule height");
            float standingEye = StandingEyeHeight();
            float depth = StopDepth(tuning, top, length, m.cab.floor_thickness, walk.towerHeight, standingEye);
            Debug.Log($"LookoutGameplaySetup: headroom stop depth {depth:F4} m (standing eye {standingEye:F4}, capsule top {walk.towerHeight:F4}, hatchStopHeadroom {tuning.hatchStopHeadroom:F4}).");
            Collider headroomStop = EnsureHeadroomStop(root, min, max, m.hatch.opening_min[1] - m.cab.floor_thickness, depth, bounds);

            var door = GetOrAdd<TrapDoor>(leaf.gameObject);
            door.Configure(HatchId, tuning, leaf, hinge, leafCollider, swingBlocker, root, min, max, length, m.cab.floor_thickness, m.hatch.leaf_width,
                LookoutManifest.V(top.foot, "top flight foot"), LookoutManifest.V(top.head, "top flight head"), top.width, headroomStop);
            EditorUtility.SetDirty(door);
            EditorUtility.SetDirty(swingBlocker);
        }

        /// <summary>
        /// The depth the build gives the headroom stop, and what the tests compare it with (td part 15, director amend4):
        /// <see cref="HeadroomDepth"/> from the game's own eye height and capsule top. Above <see cref="StopDepthCeiling"/>
        /// it throws: that goes back to the director.
        /// </summary>
        public static float StopDepth(LookoutTuning tuning, LookoutManifest.Flight top, float openingLength, float floorThickness, float capsuleHeight, float standingEye)
        {
            float depth = HeadroomDepth(top, openingLength, floorThickness, tuning, capsuleHeight, standingEye);
            if (depth > StopDepthCeiling) throw new InvalidOperationException($"Headroom stop depth {depth:F4} m is above {StopDepthCeiling} m: back to the director (amend4)");
            return depth;
        }

        /// <summary>Director amend4: a derived stop deeper than this goes back to the director.</summary>
        public const float StopDepthCeiling = 0.25f;

        /// <summary>
        /// The derived depth of the stop's lower face below the floor's underside (ADR part 10 K3): the larger of hatchStopHeadroom and
        /// EyeToLeafMin + how far the eye, ducked at the stopped position, stands above the tower capsule's top. The stopped
        /// position depends on the depth (the capsule top meets the stop's lower face), so this iterates to a fixed point.
        /// Same curve TrapDoor uses for the duck (LookoutRules.PassEyeHeight and HatchEyeHeight).
        /// </summary>
        public static float HeadroomDepth(LookoutManifest.Flight top, float openingLength, float floorThickness, LookoutTuning tuning, float capsuleHeight, float standingEye)
        {
            Vector3 foot = LookoutManifest.V(top.foot, "top flight foot"), head = LookoutManifest.V(top.head, "top flight head");
            float horizontal = new Vector2(head.x - foot.x, head.z - foot.z).magnitude;
            if (horizontal <= 1e-4f || head.y <= foot.y) throw new InvalidOperationException("Lookout manifest: the top flight does not rise");
            float pitch = Mathf.Atan2(head.y - foot.y, horizontal) * Mathf.Rad2Deg;
            float pass = LookoutRules.PassEyeHeight(openingLength, pitch, floorThickness, tuning.eyeClearance, tuning.hatchDuckMinEyeHeight, standingEye);
            float depth = tuning.hatchStopHeadroom;
            for (int i = 0; i < 100; i++)
            {
                // Feet below the cab floor top when the capsule top meets the stop's lower face; the run is that drop along the flight.
                float run = (floorThickness + depth + capsuleHeight) / Mathf.Tan(pitch * Mathf.Deg2Rad);
                float eye = LookoutRules.HatchEyeHeight(run, openingLength, pitch, pass, standingEye, tuning.hatchDuckRun, tuning.eyeClearance);
                float next = Mathf.Max(tuning.hatchStopHeadroom, EyeToLeafMin + Mathf.Max(0f, eye - capsuleHeight));
                if (Mathf.Abs(next - depth) < 1e-5f) return next;
                depth = next;
            }
            throw new InvalidOperationException("The headroom stop's depth did not settle");
        }

        /// <summary>The Player's authored camera pivot height in World.unity (the standing eye height), read from the scene; opened additively and closed again when it was not open.</summary>
        public static float StandingEyeHeight()
        {
            var scene = UnityEditor.SceneManagement.EditorSceneManager.GetSceneByPath(WalkableTowerBuilder.WorldScene);
            bool opened = false;
            if (!scene.isLoaded) { scene = EditorSceneManager.OpenScene(WalkableTowerBuilder.WorldScene, OpenSceneMode.Additive); opened = true; }
            try
            {
                var players = new System.Collections.Generic.List<PlayerController>();
                foreach (var go in scene.GetRootGameObjects()) players.AddRange(go.GetComponentsInChildren<PlayerController>(true));
                if (players.Count != 1) throw new InvalidOperationException($"Expected exactly one Player in {WalkableTowerBuilder.WorldScene}, found {players.Count}");
                var pivot = new SerializedObject(players[0]).FindProperty("cameraPivot")?.objectReferenceValue as Transform;
                if (pivot == null) throw new InvalidOperationException("The Player's cameraPivot is not set");
                Debug.Log($"LookoutGameplaySetup: standing eye height {pivot.localPosition.y:F4} read from {WalkableTowerBuilder.WorldScene} (read only).");
                return pivot.localPosition.y;
            }
            finally { if (opened) EditorSceneManager.CloseScene(scene, true); }
        }

        public const string HeadroomStopName = "HATCH_HeadroomStop";

        /// <summary>
        /// The stop from below (ADR part 4 B): a box over the opening's footprint from hatchStopHeadroom under the cab floor's
        /// underside up to the underside, on WorldBounds, under the tower root (never under the leaf). Disabled at build (td part 6); TrapDoor
        /// switches it on while the hatch is closed and off while it swings or stands open. One object, found by name on a rerun.
        /// </summary>
        static Collider EnsureHeadroomStop(Transform root, Vector3 openMin, Vector3 openMax, float undersideY, float headroom, int bounds)
        {
            Transform t = root.Find(HeadroomStopName);
            if (t == null)
            {
                t = new GameObject(HeadroomStopName).transform;
                t.SetParent(root, false);
            }
            t.localRotation = Quaternion.identity;
            t.localScale = Vector3.one;
            t.localPosition = new Vector3((openMin.x + openMax.x) * 0.5f, undersideY - headroom * 0.5f, (openMin.z + openMax.z) * 0.5f);
            t.gameObject.layer = bounds;
            var box = GetOrAdd<BoxCollider>(t.gameObject);
            box.isTrigger = false;
            box.center = Vector3.zero;
            box.size = new Vector3(openMax.x - openMin.x, headroom, openMax.z - openMin.z);
            box.enabled = false;
            return box;
        }

        /// <summary>
        /// Adds the Interactor and the prompt to the Player's camera in the open scene and saves it. Rerunnable.
        /// After it, grep the scene YAML for "fileID: 0" on the Interactor's settings and prompt fields.
        /// </summary>
        [MenuItem("Washed Ashore/Lookout/Apply Interactor To Player")]
        public static void ApplyToPlayer()
        {
            var settings = LoadRequired<InteractionSettings>(InteractionSettingsPath);
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

        /// <summary>Batchmode entry (-executeMethod): opens World.unity, runs <see cref="ApplyToPlayer"/>, exits 0, or 1 on any error.</summary>
        public static void ApplyToPlayerAndExit()
        {
            try
            {
                EditorSceneManager.OpenScene(WalkableTowerBuilder.WorldScene, OpenSceneMode.Single);
                ApplyToPlayer();
                EditorApplication.Exit(0);
            }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }

        // ---- helpers ----

        /// <summary>The tuning assets are layers-engineer's: they must exist; this script never creates them.</summary>
        static T LoadRequired<T>(string path) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null) throw new InvalidOperationException($"{path} is missing");
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
