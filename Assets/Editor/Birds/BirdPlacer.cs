using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using WashedAshore.World;
using WashedAshore.Wildlife;
using WashedAshore.Wildlife.Editor;

namespace WashedAshore.Birds.Editor
{
    /// <summary>
    /// Level-designer tool (B3/B5 placement): plans robin patches and flock POIs with
    /// <see cref="BirdPlacementRules"/> for a seed, writes them under the "Birds" root of the open scene
    /// (the BirdLayout record that BirdPopulation spawns from), saves, then re-checks the
    /// saved scene against brief 3.1/3.2. Re-runnable; same seed = same layout. Needs the baked wildlife NavMesh
    /// (route snapping) and WildlifeTuning (route offsets, wildlife anchors). Planning can outlast the CLI's
    /// 5 s eval, so:
    ///   unity command eval "WashedAshore.Birds.Editor.BirdPlacer.RunDeferred(101); return \"queued\";"
    ///   unity command eval "return WashedAshore.Birds.Editor.BirdPlacer.LastReport;"
    /// Dry run of other seeds (no scene change): BirdPlacer.PlanSeedsDeferred(101, 202, 303, 404, 505, 606, 707, 808).
    /// </summary>
    public static class BirdPlacer
    {
        public const int SceneSeed = 101;
        public const string RootName = "Birds";

        public static string LastReport = "not run";

        public static void RunDeferred(int seed = SceneSeed) => Defer(() => Run(seed));

        public static void PlanSeedsDeferred(params int[] seeds) => Defer(() => PlanSeeds(seeds));

        static void Defer(System.Func<string> job)
        {
            LastReport = "running";
            EditorApplication.delayCall += () =>
            {
                if (LastReport != "running") return;
                try { LastReport = job(); }
                catch (System.Exception e) { LastReport = "ERROR " + e; }
            };
            // An unfocused Editor doesn't tick, so delayCall would otherwise wait for focus.
            EditorApplication.QueuePlayerLoopUpdate();
        }

        class Context
        {
            public HabitatGround ground;
            public GameObject spawn;
            public WildlifeTuning wildlife;
            public List<Vector3> route;
            public string routeNote;
        }

        static Context Load(out string error)
        {
            error = null;
            var c = new Context { spawn = GameObject.Find("PlayerSpawn"),
                wildlife = AssetDatabase.LoadAssetAtPath<WildlifeTuning>(WildlifePrefabBuilder.TuningPath) };
            var map = AssetDatabase.LoadAssetAtPath<MapConfig>(WildlifePlacement.MapConfigPath);
            if (!c.spawn) error = "no PlayerSpawn";
            else if (!map) error = "no MapConfig";
            else if (!c.wildlife) error = "no WildlifeTuning";
            if (error != null) return null;
            c.ground = WildlifePopulation.Habitat(map, c.spawn.transform);
            if (c.ground.Tiles.Count == 0) { error = "no terrain tiles under the habitat window"; return null; }
            c.route = WildlifeRules.CheckRoute(c.wildlife, c.spawn.transform.position, out c.routeNote);
            return c;
        }

        /// <summary>The wildlife group anchors the same seed produces (bird patches keep >= 10 m from them).</summary>
        static List<Vector3> WildlifeAnchors(Context c, int seed, out string error)
        {
            var groups = WildlifeRules.PlanGroups(c.wildlife, seed, c.ground, c.spawn.transform.position, c.route, out error);
            return groups?.Select(g => g.anchor).ToList();
        }

        static BirdPlan PlanFor(Context c, int seed, out string error)
        {
            var anchors = WildlifeAnchors(c, seed, out error);
            if (anchors == null) { error = "wildlife plan failed: " + error; return null; }
            var spawn = c.spawn.transform.position;
            return BirdPlacementRules.Plan(seed, c.ground, spawn, c.route, anchors, c.ground.InTestLane, out error);
        }

        public static string Run(int seed = SceneSeed)
        {
            var c = Load(out var error);
            if (c == null) return "ERROR " + error;
            var plan = PlanFor(c, seed, out error);
            if (plan == null) return $"FAILED seed={seed}: {error}";

            var root = GameObject.Find(RootName);
            if (!root) root = new GameObject(RootName);
            var pop = root.GetComponent<BirdPopulation>();
            if (!pop) pop = root.AddComponent<BirdPopulation>();
            WireDefaults(pop);
            var layout = root.GetComponent<BirdLayout>();
            if (!layout) layout = root.AddComponent<BirdLayout>();
            layout.Set(plan);

            // BirdPopulation spawns from the BirdLayout record at runtime, so the root carries no child markers in the
            // scene; clear any left from the old RobinHabitat/FlockAnchor layout.
            for (int i = root.transform.childCount - 1; i >= 0; i--) Object.DestroyImmediate(root.transform.GetChild(i).gameObject);
            EditorUtility.SetDirty(layout);
            EditorUtility.SetDirty(pop);
            EditorSceneManager.MarkSceneDirty(root.scene);
            EditorSceneManager.SaveScene(root.scene);
            return $"seed={seed} ROUTE {c.routeNote}\nPLAN {Describe(plan)}\nVERIFY {Verify()}";
        }

        /// <summary>Fills BirdPopulation's prefab/tuning references if they're empty (techart prefabs, engineer's BirdTuning).</summary>
        static void WireDefaults(BirdPopulation pop)
        {
            var so = new SerializedObject(pop);
            void Fill(string prop, Object value)
            {
                var sp = so.FindProperty(prop);
                if (sp != null && !sp.objectReferenceValue && value) sp.objectReferenceValue = value;
            }
            Fill("robinPrefab", AssetDatabase.LoadAssetAtPath<GameObject>(BirdSetup.RobinPrefab));
            Fill("crowPrefab",AssetDatabase.LoadAssetAtPath<GameObject>(BirdSetup.CrowPrefab));
            var tuningGuid = AssetDatabase.FindAssets("t:BirdTuning").FirstOrDefault();
            if (tuningGuid != null) Fill("tuning", AssetDatabase.LoadAssetAtPath<BirdTuning>(AssetDatabase.GUIDToAssetPath(tuningGuid)));
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>Plans each seed without touching the scene (B6 gating and robustness seeds).</summary>
        public static string PlanSeeds(params int[] seeds)
        {
            var c = Load(out var error);
            if (c == null) return "ERROR " + error;
            var sb = new StringBuilder();
            int failed = 0;
            foreach (var s in seeds)
            {
                var plan = PlanFor(c, s, out error);
                if (plan == null) { failed++; sb.AppendLine($"seed {s}: FAIL {error}"); continue; }
                sb.AppendLine($"seed {s}: OK robins={plan.RobinCount} patches={plan.patches.Count} flocks={plan.flocks.Count} " +
                              $"patchDraws={plan.patchDraws} flockDraws={plan.flockDraws} " +
                              $"trailArcs=[{string.Join(",", plan.patches.Where(p => p.type == PatchType.Trail).Select(p => p.arc + 1))}] " +
                              $"flockArcs=[{string.Join(",", plan.flocks.Select(f => f.arc + 1))}] " +
                              $"poiKinds=[{string.Join(",", plan.flocks.Select(f => f.primaryKind[0] + "/" + f.secondaryKind[0]))}]");
            }
            return $"{sb}RESULT {(failed == 0 ? "PASS" : $"FAIL {failed}/{seeds.Length}")}";
        }

        static string Describe(BirdPlan plan)
        {
            var sb = new StringBuilder($"robins={plan.RobinCount} patches={plan.patches.Count} flocks={plan.flocks.Count} flockBirds={plan.FlockBirdCount} " +
                                       $"patchDraws={plan.patchDraws} flockDraws={plan.flockDraws} tallestCrown={plan.tallestCrown:F1}m (top y={plan.tallestCrownTopY:F1})\n");
            foreach (var r in plan.redraws) sb.AppendLine($"  redraw: {r}");
            foreach (var p in plan.patches)
                sb.AppendLine($"  {p.name} n={p.size} arc={p.arc + 1} centre=({p.centre.x:F1},{p.centre.y:F1},{p.centre.z:F1}) route={p.routeDistance:F1} " +
                              $"tree={p.nearestTree:F1} slope={p.slope:F1} grass={p.grass:F2} sightBack={p.sightlineBack:F0}");
            foreach (var f in plan.flocks)
                sb.AppendLine($"  {f.name} n={f.size} arc={f.arc + 1} primary=({f.primary.x:F0},{f.primary.z:F0}) {f.primaryKind} route={f.primaryRouteDistance:F0} " +
                              $"secondary(arc {f.secondaryArc + 1})=({f.secondary.x:F0},{f.secondary.z:F0}) {f.secondaryKind} route={f.secondaryRouteDistance:F0}");
            return sb.ToString();
        }

        /// <summary>Re-checks the saved scene: bounds, every 3.2 rule per patch/start/POI, and that the children match the record.</summary>
        public static string Verify()
        {
            var c = Load(out var error);
            if (c == null) return "ERROR " + error;
            var root = GameObject.Find(RootName);
            var layout = root ? root.GetComponent<BirdLayout>() : null;
            if (!layout) return "FAIL no Birds root with BirdLayout";
            var spawn = c.spawn.transform.position;
            // The scene's wildlife is baked from its own seed (WildlifePlacer.SceneSeed); check against what is actually there.
            var herds = Object.FindObjectsByType<WildlifeHerd>(FindObjectsInactive.Include).Select(h => h.transform.position).ToList();
            var site = new BirdPlacementRules.Site(c.ground, spawn, c.route, herds, c.ground.InTestLane);
            var fails = new List<string>();
            var patches = layout.patches; var flocks = layout.flocks;

            int robins = patches.Sum(p => p.size);
            if (robins != 16 || patches.Count != 7) fails.Add($"robins {robins} in {patches.Count} (brief 16 in 7)");
            if (robins < 4 || robins > 30) fails.Add("robins outside 4-30");
            if (patches.Any(p => p.starts.Count != p.size)) fails.Add("starts != size");
            if (patches.Select(p => p.arc).Distinct().Count() != patches.Count) fails.Add("patch arcs not distinct");
            var trailArcs = patches.Where(p => p.type == PatchType.Trail).Select(p => p.arc).ToList();
            if (trailArcs.Count != 3) fails.Add("trail patches != 3");
            if (trailArcs.Any(a => trailArcs.Any(b => b != a && ((a - b + 8) % 8 == 1 || (b - a + 8) % 8 == 1)))) fails.Add("trail arcs adjacent");
            foreach (var p in patches)
            {
                var f = site.PatchFaults(p, patches);
                for (int i = 0; i < p.starts.Count; i++) f.AddRange(site.StartFaults(p, i));
                if (f.Count > 0) fails.Add($"{p.name}: {string.Join("/", f.Distinct())}");
            }

            var sizes = flocks.Select(f => f.size).ToList();
            if (!sizes.SequenceEqual(BirdPlacementRules.FlockSizes)) fails.Add($"flock sizes [{string.Join(",", sizes)}] (brief 6/5/4)");
            if (flocks.Count < 1 || flocks.Count > 4 || sizes.Any(s => s < 3 || s > 12)) fails.Add("flocks outside 1-4 x 3-12");
            if (flocks.Select(f => f.arc).Distinct().Count() != flocks.Count) fails.Add("flock arcs not distinct");
            foreach (var f in flocks)
            {
                if (f.secondaryArc != (f.arc + 1) % 3) fails.Add($"{f.name}: secondary not in next third");
                var pf = site.PoiFaults(f.primary, f.arc).Select(x => "primary " + x)
                    .Concat(site.PoiFaults(f.secondary, f.secondaryArc).Select(x => "secondary " + x)).ToList();
                foreach (var g in flocks.Where(g => g != f))
                    foreach (var q in new[] { g.primary, g.secondary })
                    {
                        if (Flat(q, f.primary) < BirdPlacementRules.PoiGap) pf.Add($"primary<120 from {g.name}");
                        if (Flat(q, f.secondary) < BirdPlacementRules.PoiGap) pf.Add($"secondary<120 from {g.name}");
                    }
                if (pf.Count > 0) fails.Add($"{f.name}: {string.Join("/", pf.Distinct())}");
            }

            // Edit-mode scene: BirdPopulation spawns from the record at runtime, so the root has no children.
            if (!Application.isPlaying && root.transform.childCount != 0) fails.Add($"Birds root has {root.transform.childCount} stale children");

            return $"seed={layout.seed} robins={robins} patches={patches.Count} flocks={flocks.Count} flockBirds={sizes.Sum()} " +
                   $"tallestCrown={layout.tallestCrown:F1}m wildlifeAnchors={herds.Count}\nRESULT {(fails.Count == 0 ? "PASS" : "FAIL " + string.Join("; ", fails))}";
        }

        static float Flat(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));
    }
}
