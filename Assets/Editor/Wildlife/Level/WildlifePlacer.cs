using System.Collections.Generic;
using System.Linq;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using WashedAshore.Wildlife.Editor;
using WashedAshore.World;

namespace WashedAshore.Wildlife.Level
{
    /// <summary>
    /// Level-designer tool (A2/A3): retunes NavMesh agent 0 for the animals, bakes a NavMeshSurface
    /// over the habitat window (512 m around PlayerSpawn, across terrain tiles) into an asset, checks the 3.5 walk route, then places the seeded
    /// population through wl-engineer's WildlifePlacement and re-checks it against the brief.
    /// Re-runnable; same seed = same layout. The bake can outlast the CLI's 5 s eval, so:
    ///   unity command eval "WashedAshore.Wildlife.Level.WildlifePlacer.RunDeferred(101); return \"queued\";"
    ///   unity command eval "return WashedAshore.Wildlife.Level.WildlifePlacer.LastReport;"
    /// </summary>
    public static class WildlifePlacer
    {
        const string NavMeshPath = "Assets/Scenes/World/WildlifeNavMesh.asset";
        public const int SceneSeed = 101;

        // Agent 0 for quadrupeds (fox to stag): narrow, low, wolves' 30 deg slope plus margin.
        const float AgentRadius = 0.4f, AgentHeight = 1.5f, AgentSlope = 35f, AgentClimb = 0.4f;

        public static string LastReport = "not run";

        /// <summary>Every layer except Water.</summary>
        public static int BakeLayerMask => ~(1 << WorldLayers.Water);

        public static void RunDeferred(int seed = SceneSeed)
        {
            LastReport = "running";
            EditorApplication.delayCall += () =>
            {
                if (LastReport != "running") return;
                try { LastReport = Run(seed); }
                catch (System.Exception e) { LastReport = "ERROR " + e; }
            };
            // An unfocused Editor doesn't tick, so delayCall would otherwise wait for focus.
            EditorApplication.QueuePlayerLoopUpdate();
        }

        public static string Run(int seed = SceneSeed)
        {
            var spawn = GameObject.Find("PlayerSpawn");
            if (!spawn) return "ERROR no PlayerSpawn";
            var map = AssetDatabase.LoadAssetAtPath<MapConfig>(WildlifePlacement.MapConfigPath);
            if (!map) return "ERROR no MapConfig";
            var ground = WildlifePopulation.Habitat(map, spawn.transform);
            if (ground.Tiles.Count == 0) return "ERROR no terrain tiles under the habitat window";
            var tuning = AssetDatabase.LoadAssetAtPath<WildlifeTuning>(WildlifePrefabBuilder.TuningPath);
            if (!tuning) return "ERROR no WildlifeTuning";

            // Animals must not be baked into the NavMesh as obstacles.
            var old = GameObject.Find(WildlifePlacement.RootName);
            if (old) Object.DestroyImmediate(old);

            ConfigureAgent();
            var surface = Bake(ground);
            WildlifeRules.CheckRoute(tuning, spawn.transform.position, out var routeNote);
            var placed = WildlifePlacement.BakeIntoScene(seed);
            EditorSceneManager.SaveScene(spawn.scene);
            return $"BAKE {BakeReport(surface)}\nROUTE {routeNote}\nPLACE {placed}\nVERIFY {Verify(tuning, ground, spawn.transform.position)}";
        }

        static void ConfigureAgent()
        {
            var asset = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/NavMeshAreas.asset")[0];
            var so = new SerializedObject(asset);
            var s = so.FindProperty("m_Settings").GetArrayElementAtIndex(0);
            s.FindPropertyRelative("agentRadius").floatValue = AgentRadius;
            s.FindPropertyRelative("agentHeight").floatValue = AgentHeight;
            s.FindPropertyRelative("agentSlope").floatValue = AgentSlope;
            s.FindPropertyRelative("agentClimb").floatValue = AgentClimb;
            so.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();
        }

        static NavMeshSurface Bake(HabitatGround ground)
        {
            var go = GameObject.Find("WildlifeNavMesh");
            if (!go) go = new GameObject("WildlifeNavMesh");
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var surface = go.GetComponent<NavMeshSurface>();
            if (!surface) surface = go.AddComponent<NavMeshSurface>();
            surface.agentTypeID = 0;
            surface.collectObjects = CollectObjects.Volume;
            // The habitat window only: baking every Bells Bend tile would take minutes and the animals never leave it.
            var h = ground.Heights(129, out _);
            float lo = float.MaxValue, hi = float.MinValue;
            foreach (var v in h) { lo = Mathf.Min(lo, v); hi = Mathf.Max(hi, v); }
            var c = ground.Centre;
            surface.center = new Vector3(c.x, (lo + hi) * 0.5f, c.y);
            surface.size = new Vector3(ground.Size.x, hi - lo + 40f, ground.Size.y);
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.layerMask = BakeLayerMask; // animals never path on water (water W4/W11)
            surface.ignoreNavMeshAgent = true;
            surface.ignoreNavMeshObstacle = true;
            surface.overrideVoxelSize = true;
            surface.voxelSize = AgentRadius / 2f;
            surface.overrideTileSize = true;
            surface.tileSize = 128;
            surface.minRegionArea = 20f;
            surface.buildHeightMesh = true;

            surface.RemoveData();
            surface.navMeshData = null;
            surface.BuildNavMesh();
            var data = surface.navMeshData;
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(NavMeshPath));
            AssetDatabase.DeleteAsset(NavMeshPath);
            AssetDatabase.CreateAsset(data, NavMeshPath);
            AssetDatabase.SaveAssets();
            surface.navMeshData = AssetDatabase.LoadAssetAtPath<NavMeshData>(NavMeshPath);
            surface.RemoveData();
            surface.AddData();
            EditorUtility.SetDirty(surface);
            return surface;
        }

        public static string BakeReport(NavMeshSurface surface)
        {
            var tri = NavMesh.CalculateTriangulation();
            var s = NavMesh.GetSettingsByID(0);
            return $"navMeshTriangles={tri.indices.Length / 3} verts={tri.vertices.Length} asset={AssetDatabase.GetAssetPath(surface.navMeshData)} " +
                   $"agent0=r{s.agentRadius}/h{s.agentHeight}/slope{s.agentSlope}/climb{s.agentClimb} voxel={surface.voxelSize}";
        }

        /// <summary>Independent re-check of the placed scene against brief 3.1/3.2 (literal values, not tuning).</summary>
        public static string Verify(WildlifeTuning tuning, HabitatGround ground, Vector3 spawn)
        {
            var route = WildlifeRules.CheckRoute(tuning, spawn, out _);
            var trees = new List<Vector2>(); var cover = new List<Vector2>();
            foreach (var i in ground.Trees())
            {
                string name = i.prefab ? i.prefab.name : "";
                var w = new Vector2(i.world.x, i.world.z);
                if (name.StartsWith("Rock_")) continue;
                cover.Add(w);
                if (!name.StartsWith("Bush_")) trees.Add(w);
            }
            var centre = ground.Centre;
            var herds = Object.FindObjectsByType<WildlifeHerd>(FindObjectsInactive.Include).OrderBy(h => h.name).ToList();
            var deer = herds.Where(h => h.name.StartsWith("Deer_")).ToList();
            var expected = new Dictionary<string, (int groups, int size, float cohesion)>
                { { "Deer", (2, 4, 12f) }, { "Stag", (1, 3, 15f) }, { "Fox", (2, 1, 0f) }, { "Wolf", (1, 2, 10f) } };
            var fails = new List<string>(); var lines = new List<string>();
            int total = 0;
            foreach (var kv in expected)
            {
                var hs = herds.Where(h => h.name.StartsWith(kv.Key + "_")).ToList();
                int n = hs.Sum(h => h.GetComponentsInChildren<NavMeshAgent>(true).Length);
                total += n;
                lines.Add($"{kv.Key}: groups={hs.Count} animals={n} sizes=[{string.Join(",", hs.Select(h => h.GetComponentsInChildren<NavMeshAgent>(true).Length))}]");
                if (hs.Count != kv.Value.groups) fails.Add($"{kv.Key} groups {hs.Count}!={kv.Value.groups}");
                if (hs.Any(h => h.GetComponentsInChildren<NavMeshAgent>(true).Length != kv.Value.size)) fails.Add($"{kv.Key} group size");
            }
            foreach (var h in herds)
            {
                var a = h.transform.position; var a2 = Flat(a); string sp = h.name.Split('_')[0];
                float slope = ground.Steepness(a);
                float spawnD = Vector2.Distance(a2, Flat(spawn)), routeD = WildlifeRules.RouteDistance(route, a2);
                float edgeD = ground.EdgeDistance(a);
                float gap = herds.Where(x => x != h).Select(x => Vector2.Distance(Flat(x.transform.position), a2)).DefaultIfEmpty(999f).Min();
                float deerD = deer.Where(x => x != h).Select(x => Vector2.Distance(Flat(x.transform.position), a2)).DefaultIfEmpty(999f).Min();
                var members = h.GetComponentsInChildren<NavMeshAgent>(true);
                var cen = members.Aggregate(Vector3.zero, (s, m) => s + m.transform.position) / Mathf.Max(1, members.Length);
                float coh = members.Select(m => Vector3.Distance(m.transform.position, cen)).DefaultIfEmpty(0f).Max();
                var bad = new List<string>();
                if (!NavMesh.SamplePosition(a, out _, 2f, NavMesh.AllAreas)) bad.Add("anchorOffMesh");
                if (spawnD < 40f) bad.Add("spawn<40");
                if (edgeD < 30f) bad.Add("edge<30");
                if (Vector2.Distance(a2, centre) > 170f) bad.Add("centre>170");
                if (!ground.IsLand(a)) bad.Add("notLand");
                if (gap < 50f) bad.Add("anchorGap<50");
                if (routeD > 60f) bad.Add("route>60");
                if (sp == "Deer")
                {
                    if (ground.Splat(a, 0) < 0.6f) bad.Add("grass<0.6");
                    if (slope > 20f) bad.Add("slope>20");
                    if (!Near(trees, a2, 40f)) bad.Add("noTree40");
                    if (deerD < 100f) bad.Add("deerGap<100");
                }
                if (sp == "Stag" && (slope > 25f || deerD < 60f)) bad.Add("stagRule");
                if (sp == "Fox" && (slope > 25f || !Near(cover, a2, 25f))) bad.Add("foxRule");
                if (sp == "Wolf" && (slope > 30f || spawnD < 100f || deerD < 60f)) bad.Add("wolfRule");
                if (expected[sp].cohesion > 0f && coh > expected[sp].cohesion) bad.Add("cohesion");
                foreach (var m in members)
                {
                    if (!NavMesh.SamplePosition(m.transform.position, out var hit, 0.5f, NavMesh.AllAreas)) bad.Add($"{m.name}OffMesh");
                    if (ground.InTestLane(m.transform.position)) bad.Add($"{m.name}InLane");
                    if (m.agentTypeID != 0) bad.Add($"{m.name}AgentType");
                }
                if (bad.Count > 0) fails.Add($"{h.name}:{string.Join("/", bad)}");
                lines.Add($"{h.name} n={members.Length} anchor=({a.x:F0},{a.z:F0}) spawn={spawnD:F0} route={routeD:F0} edge={edgeD:F0} gap={gap:F0} slope={slope:F1} cohesion={coh:F1} {(bad.Count == 0 ? "OK" : "FAIL")}");
            }
            if (total < 6 || total > 40 || total != 15) fails.Add($"total={total}");
            return $"total={total} (brief 15, bounds 6-40)\n{string.Join("\n", lines)}\nRESULT {(fails.Count == 0 ? "PASS" : "FAIL " + string.Join("; ", fails))}";
        }

        /// <summary>Play mode only: every agent's isOnNavMesh.</summary>
        public static string AgentCheck()
        {
            var agents = Object.FindObjectsByType<NavMeshAgent>();
            var off = agents.Where(a => !a.isOnNavMesh).Select(a => a.name).ToList();
            return $"playing={Application.isPlaying} agents={agents.Length} isOnNavMesh={agents.Length - off.Count}/{agents.Length} off=[{string.Join(",", off)}]";
        }

        static Vector2 Flat(Vector3 v) => new Vector2(v.x, v.z);

        static bool Near(List<Vector2> pts, Vector2 p, float r)
        {
            float r2 = r * r;
            foreach (var q in pts) if ((q - p).sqrMagnitude <= r2) return true;
            return false;
        }
    }
}
