using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Serialization;
using WashedAshore.Gameplay;
using WashedAshore.Wildlife;

namespace WashedAshore.Birds
{
    /// <summary>
    /// The scene's birds, on the "Birds" root (bird density brief 3.1, 3.2). The plan comes from the
    /// <see cref="BirdLayout"/> baked by level design (seed 101); when a test overrides the seed
    /// (<see cref="BirdRandom.OverrideSeed"/>), Awake re-plans with <see cref="BirdPlacementRules.Plan"/>, after the
    /// wildlife has re-planned (execution order). Robins spawn at the plan's validated start points, one
    /// <see cref="Flock"/> per flock plan, all before the first frame and on the Birds layer. A plan outside the
    /// spec's outer bounds, or a seed that can't be placed, spawns nothing and logs an error.
    /// Disabling this root turns every bird off (the B8 birds ON/OFF toggle).
    /// </summary>
    [DefaultExecutionOrder(-90)]
    public class BirdPopulation : MonoBehaviour
    {
        [SerializeField] BirdTuning tuning;
        [SerializeField] GameObject robinPrefab;
        [FormerlySerializedAs("flockBirdPrefab")] [SerializeField] GameObject crowPrefab;
        [Tooltip("Seed of the baked layout (World.unity and builds: 101).")]
        public int seed = 101;

        public BirdTuning Tuning => tuning;
        public int ActiveSeed { get; private set; }
        public BirdPlan Plan { get; private set; }
        public BirdPlacementRules.Site Site { get; private set; }
        /// <summary>Why the plan was rejected, or null. A failed seed spawns no birds.</summary>
        public string PlacementError { get; private set; }
        public List<RobinAgent> Robins { get; } = new List<RobinAgent>();
        public List<Flock> Flocks { get; } = new List<Flock>();

        void Awake()
        {
            ActiveSeed = BirdRandom.RunSeed(seed);
            var wild = FindAnyObjectByType<WildlifePopulation>();
            if (wild && !wild.PlayerSpawn)
            {
                // Same stale-habitat case as WildlifePopulation: report it, don't throw.
                PlacementError = "WildlifePopulation has no PlayerSpawn; regenerate the habitats";
                Debug.LogWarning($"BirdPopulation: {PlacementError}", this);
                return;
            }
            var ground = wild ? wild.Ground : null;
            if (!tuning || !robinPrefab || !crowPrefab || !wild || ground == null || ground.Tiles.Count == 0)
            {
                Fail("missing BirdTuning, a bird prefab, the terrain or the WildlifePopulation");
                return;
            }
            Vector3 spawn = wild.PlayerSpawn.position;
            var route = WildlifePopulation.Route(wild.Tuning, spawn);
            var anchors = wild.GetComponentsInChildren<WildlifeHerd>().Select(h => h.Anchor).ToList();
            bool InLane(Vector3 p) => ground.InTestLane(p);
            Site = new BirdPlacementRules.Site(ground, spawn, route, anchors, InLane);

            var layout = GetComponent<BirdLayout>();
            if (layout && layout.seed == ActiveSeed && layout.patches.Count > 0)
                Plan = new BirdPlan { seed = layout.seed, patches = layout.patches, flocks = layout.flocks,
                    tallestCrown = layout.tallestCrown, tallestCrownTopY = layout.tallestCrownTopY };
            else
            {
                Plan = BirdPlacementRules.Plan(ActiveSeed, ground, spawn, route, anchors, InLane, out string error);
                if (Plan == null) { Fail($"seed {ActiveSeed} failed the placement rules: {error}"); return; }
            }
            string bounds = CheckBounds(Plan);
            if (bounds != null) { Fail(bounds); return; }

            int layer = LayerMask.NameToLayer(tuning.birdLayer);
            if (layer < 0) Debug.LogWarning($"BirdPopulation: no '{tuning.birdLayer}' layer; birds stay on their prefab layer", this);
            int index = 0;
            foreach (var patch in Plan.patches)
            {
                var group = new GameObject(patch.name);
                group.transform.SetParent(transform, false);
                for (int i = 0; i < patch.starts.Count; i++, index++)
                {
                    var rng = BirdRandom.For(ActiveSeed, 5000 + index);
                    var go = Instantiate(robinPrefab, patch.starts[i], Quaternion.Euler(0f, rng.Range(0f, 360f), 0f), group.transform);
                    go.name = $"{patch.name}_Robin_{i + 1}";
                    if (layer >= 0) SetLayer(go.transform, layer);
                    var robin = go.GetComponent<RobinAgent>();
                    if (!robin) robin = go.AddComponent<RobinAgent>(); // no '??': GetComponent can return a fake null in the Editor
                    robin.Init(tuning, Site, patch, ActiveSeed, index);
                    Robins.Add(robin);
                }
            }
            for (int i = 0; i < Plan.flocks.Count; i++)
            {
                var go = new GameObject(Plan.flocks[i].name);
                go.transform.SetParent(transform, false);
                var flock = go.AddComponent<Flock>();
                flock.Init(tuning, Plan.flocks[i], crowPrefab, ActiveSeed, i, layer);
                Flocks.Add(flock);
            }
        }

        // Load evidence in Player.log (as the wildlife does): what spawned, then at 5 s that robins are cycling clips
        // and the flocks are inside the band.
        System.Collections.IEnumerator Start()
        {
            if (PlacementError != null) yield break;
            string layer = Robins.Count > 0 ? LayerMask.LayerToName(Robins[0].gameObject.layer) : "-";
            Debug.Log($"Birds: seed={ActiveSeed} robins={Robins.Count} flocks={Flocks.Count} flockBirds={Flocks.Sum(f => f.Birds.Count)} layer={layer}");
            yield return new WaitForSeconds(5f);
            float lo = float.MaxValue, hi = float.MinValue;
            foreach (var b in Flocks.SelectMany(f => f.Birds))
            {
                Vector3 p = b.transform.position;
                float agl = p.y - TerrainQuery.Height(p);
                lo = Mathf.Min(lo, agl);
                hi = Mathf.Max(hi, agl);
            }
            int clips = Robins.Select(r => r.CurrentClip).Distinct().Count();
            Debug.Log($"Birds: t=5 robinsOnGround={Robins.Count(r => !r.IsFlying && !r.Despawned)} distinctClips={clips} flockAgl={lo:F1}-{hi:F1}");
        }

        void Fail(string why)
        {
            PlacementError = why;
            Debug.LogError($"BirdPopulation: {why}", this);
        }

        string CheckBounds(BirdPlan plan)
        {
            int robins = plan.patches.Sum(p => p.starts.Count);
            if (robins < tuning.robinTotal.x || robins > tuning.robinTotal.y)
                return $"{robins} robins outside {tuning.robinTotal.x}-{tuning.robinTotal.y}";
            if (plan.flocks.Count < tuning.flockCount.x || plan.flocks.Count > tuning.flockCount.y)
                return $"{plan.flocks.Count} flocks outside {tuning.flockCount.x}-{tuning.flockCount.y}";
            foreach (var f in plan.flocks)
                if (f.size < tuning.flockSize.x || f.size > tuning.flockSize.y)
                    return $"{f.name}: {f.size} birds outside {tuning.flockSize.x}-{tuning.flockSize.y}";
            return null;
        }

        static void SetLayer(Transform root, int layer)
        {
            root.gameObject.layer = layer;
            foreach (Transform c in root) SetLayer(c, layer);
        }

        public void Configure(BirdTuning t, GameObject robin, GameObject crow)
        {
            tuning = t;
            robinPrefab = robin;
            crowPrefab = crow;
        }
    }
}
