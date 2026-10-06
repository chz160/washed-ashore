using System;
using System.Collections.Generic;
using UnityEngine;
using WashedAshore.World;

namespace WashedAshore.Wildlife
{
    /// <summary>
    /// The scene's persistent wildlife population (density brief 3.1, 3.2) on the "Wildlife" root.
    /// Groups are children of this object. The scene ships with the groups baked for
    /// <see cref="bakedSeed"/> (101); when a different run seed is set (tests, see
    /// <see cref="WildlifeRandom.OverrideSeed"/>), Awake re-plans with <see cref="WildlifeRules"/> and
    /// respawns the groups before the first frame, so nothing pops in where the player can see it.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class WildlifePopulation : MonoBehaviour
    {
        [SerializeField] WildlifeTuning tuning;
        [SerializeField] Transform playerSpawn;
        [SerializeField] int bakedSeed = 101;
        [Tooltip("Prefab per species, indexed by WildlifeSpecies.")]
        [SerializeField] GameObject[] prefabs = new GameObject[4];
        [Tooltip("Map scale and water level (Bells Bend). Habitats never plan below WaterLevelY + LandMargin.")]
        [SerializeField] MapConfig map;

        public WildlifeTuning Tuning => tuning;
        public Transform PlayerSpawn => playerSpawn;
        public int BakedSeed => bakedSeed;
        public MapConfig Map => map;

        /// <summary>The habitat window around the spawn on this scene's terrain tiles.</summary>
        public HabitatGround Ground => ground ??= playerSpawn ? Habitat(map, playerSpawn) : null;
        HabitatGround ground;

        /// <summary>Land margin above MapConfig.WaterLevelY, so nothing plans on the shore ramp.</summary>
        public const float LandMargin = 0.5f;

        public static HabitatGround Habitat(MapConfig cfg, Transform spawn) =>
            HabitatGround.Around(spawn.position, spawn.eulerAngles.y, cfg ? cfg.WaterLevelY + LandMargin : float.NegativeInfinity);
        public int ActiveSeed { get; private set; }
        /// <summary>Why this seed's placement failed, or null. A failed seed spawns nothing (no stale groups).</summary>
        public string PlacementError { get; private set; }

        void Awake()
        {
            ActiveSeed = WildlifeRandom.RunSeed(tuning);
            if (!playerSpawn)
            {
                // A terrain rebuild replaced PlayerSpawn; habitats need regenerating (WildlifePlacer.Run).
                PlacementError = "PlayerSpawn not assigned; regenerate the habitat with WildlifePlacer.Run";
                Debug.LogWarning($"WildlifePopulation: {PlacementError}", this);
                return;
            }
            if (ActiveSeed == bakedSeed && GetComponentsInChildren<WildlifeHerd>().Length > 0) return;

            var plan = WildlifeRules.PlanGroups(tuning, ActiveSeed, Ground, playerSpawn.position,
                Route(tuning, playerSpawn.position), out string error);
            if (plan == null)
            {
                PlacementError = error;
                Clear();
                Debug.LogError($"WildlifePopulation: seed {ActiveSeed} failed the placement rules: {error}", this);
                return;
            }
            Clear();
            Spawn(plan, (s, parent, pos, rot) => Instantiate(prefabs[(int)s], pos, rot, parent));
        }

        // Load evidence in Player.log: agents enable in their Start, so check a frame later, then again at
        // 5 s to show they stayed on the NavMesh and are moving rather than warping once.
        System.Collections.IEnumerator Start()
        {
            yield return null;
            Debug.Log($"Wildlife: seed={ActiveSeed} agents={WildlifeAgent.All.Count} onNavMesh={CountOnNavMesh(out _)}");
            yield return new WaitForSeconds(5f);
            int on = CountOnNavMesh(out int moving);
            Debug.Log($"Wildlife: t=5 onNavMesh={on} moving={moving}");
        }

        static int CountOnNavMesh(out int moving)
        {
            int on = 0;
            moving = 0;
            foreach (var a in WildlifeAgent.All)
            {
                if (!a.Agent.isOnNavMesh) continue;
                on++;
                if (a.Agent.velocity.sqrMagnitude > 0.01f) moving++;
            }
            return on;
        }

        public static List<Vector3> Route(WildlifeTuning tuning, Vector3 spawn) => WildlifeRules.CheckRoute(tuning, spawn, out _);

        public void Clear()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                var child = transform.GetChild(i).gameObject;
                child.SetActive(false); // unregisters agents now; Destroy is deferred
                if (Application.isPlaying) Destroy(child);
                else DestroyImmediate(child);
            }
        }

        /// <summary>Builds the groups. <paramref name="instantiate"/> must place the instance at the given
        /// pose on creation, so a NavMeshAgent never enables at the prefab origin.</summary>
        public void Spawn(List<WildlifeRules.GroupPlan> plan, Func<WildlifeSpecies, Transform, Vector3, Quaternion, GameObject> instantiate)
        {
            foreach (var g in plan)
            {
                var group = new GameObject(g.name);
                group.transform.SetParent(transform, false);
                group.transform.position = g.anchor;
                var herd = group.AddComponent<WildlifeHerd>();
                herd.Species = g.species;
                herd.RouteArc = g.arc;
                string prefix = g.name.Replace("_Group", "_");
                for (int i = 0; i < g.members.Count; i++)
                {
                    var go = instantiate(g.species, group.transform, g.members[i], Quaternion.Euler(0f, g.yaws[i], 0f));
                    go.name = $"{prefix}_{i + 1}";
                }
            }
        }

        public void Configure(WildlifeTuning t, Transform spawn, int seed, GameObject[] speciesPrefabs, MapConfig mapConfig)
        {
            map = mapConfig;
            ground = null;
            tuning = t;
            playerSpawn = spawn;
            bakedSeed = seed;
            ActiveSeed = seed;
            prefabs = speciesPrefabs;
        }
    }
}
