using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using WashedAshore.World;

namespace WashedAshore.Wildlife.Editor
{
    /// <summary>
    /// Bakes the seeded population (density brief 3.1/3.2) into the open scene under a "Wildlife"
    /// root with a WildlifePopulation component. Runs the same planner the runtime uses, so a test
    /// seed override re-places with identical rules. Needs a baked NavMesh. Re-runnable.
    /// Run via: unity command eval "return WashedAshore.Wildlife.Editor.WildlifePlacement.BakeIntoScene(101);"
    /// </summary>
    public static class WildlifePlacement
    {
        public const string RootName = "Wildlife";
        public const string MapConfigPath = "Assets/World/MapConfig.asset";

        public static string BakeIntoScene(int seed)
        {
            var tuning = AssetDatabase.LoadAssetAtPath<WildlifeTuning>(WildlifePrefabBuilder.TuningPath);
            if (!tuning) return "MISSING WildlifeTuning; run WildlifePrefabBuilder.Build() first";
            var prefabs = WildlifePrefabBuilder.Species
                .Select(s => AssetDatabase.LoadAssetAtPath<GameObject>(WildlifePrefabBuilder.PrefabPath(s))).ToArray();
            if (prefabs.Any(p => !p)) return "MISSING wildlife prefab; run WildlifePrefabBuilder.Build() first";
            var spawn = GameObject.Find("PlayerSpawn");
            if (!spawn) return "MISSING PlayerSpawn";
            if (NavMesh.CalculateTriangulation().indices.Length == 0) return "NO NavMesh baked";

            var map = AssetDatabase.LoadAssetAtPath<MapConfig>(MapConfigPath);
            if (!map) return "MISSING MapConfig at " + MapConfigPath;
            var route = WildlifePopulation.Route(tuning, spawn.transform.position);
            var ground = WildlifePopulation.Habitat(map, spawn.transform);
            var plan = WildlifeRules.PlanGroups(tuning, seed, ground, spawn.transform.position, route, out string error);
            if (plan == null)
            {
                Debug.LogError($"WildlifePlacement: seed {seed} failed: {error}");
                return "FAILED: " + error;
            }

            var root = GameObject.Find(RootName);
            if (!root) root = new GameObject(RootName);
            var pop = root.GetComponent<WildlifePopulation>();
            if (!pop) pop = root.AddComponent<WildlifePopulation>();
            pop.Configure(tuning, spawn.transform, seed, prefabs, map);
            pop.Clear();
            pop.Spawn(plan, (s, parent, pos, rot) =>
            {
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefabs[(int)s], parent);
                go.transform.SetPositionAndRotation(pos, rot);
                return go;
            });
            EditorUtility.SetDirty(pop);
            EditorSceneManager.MarkSceneDirty(root.scene);
            EditorSceneManager.SaveScene(root.scene);
            return $"seed={seed}\n" + Report();
        }

        /// <summary>A3 evidence: counts per species and group, cohesion and NavMesh distance per group.</summary>
        public static string Report()
        {
            var agents = Object.FindObjectsByType<WildlifeAgent>(FindObjectsInactive.Include);
            var sb = new StringBuilder($"total={agents.Length} navMeshTriangles={NavMesh.CalculateTriangulation().indices.Length / 3}\n");
            foreach (var g in agents.GroupBy(a => a.Species).OrderBy(g => g.Key))
                sb.AppendLine($"{g.Key}={g.Count()}");
            foreach (var h in Object.FindObjectsByType<WildlifeHerd>(FindObjectsInactive.Include).OrderBy(h => h.name))
            {
                var members = h.GetComponentsInChildren<WildlifeAgent>(true);
                var centroid = members.Aggregate(Vector3.zero, (acc, m) => acc + m.transform.position) / Mathf.Max(1, members.Length);
                float spread = members.Select(m => Vector3.Distance(m.transform.position, centroid)).DefaultIfEmpty(0f).Max();
                float navGap = members.Select(m => NavMesh.SamplePosition(m.transform.position, out var hit, 10f, NavMesh.AllAreas)
                    ? Vector3.Distance(hit.position, m.transform.position) : float.PositiveInfinity).DefaultIfEmpty(0f).Max();
                sb.AppendLine($"{h.name} n={members.Length} anchor={h.transform.position.ToString("F1")} " +
                              $"maxFromCentroid={spread:F1} maxDistToNavMesh={navGap:F2}");
            }
            return sb.ToString();
        }
    }
}
