using System;
using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;
using WashedAshore.Gameplay;
using WashedAshore.Level;
using WashedAshore.World;

namespace WashedAshore.Fish
{
    /// <summary>
    /// The scene's fish, on the "Fish" root (spec F3-F9, adr/fish-1). The population is virtual: each 32 m cell's groups
    /// are a pure function of (seed, cell) (<see cref="FishPlan"/>). Cells within simRadiusIn of the player (nearest
    /// point) go live in <see cref="FishSimWorld"/>, nearest first, at most maxCellPlansPerFrame plans per frame; live
    /// cells beyond simRadiusOut are dropped, and re-entering regenerates them from the seed. A cell only goes live, and
    /// only goes away, while none of its fish could be seen (<see cref="FishView"/>: N7); a cell that can't go live (cap
    /// or in view) backs off for blockedRetrySeconds. A group whose home has no terrain tile is dropped at once (F9).
    /// Surface signs run on the wider event ring (<see cref="FishEventScheduler"/>). No colliders, no rigidbodies, no
    /// GameObject per fish: <see cref="States"/> is what the renderer draws. Disabling this root, or
    /// <see cref="OverrideEnabled"/>(false) before load, turns every fish off. Missing tuning, bodies, water material or
    /// wave settings is an error and disables fish (no stand-in numbers).
    /// </summary>
    [DefaultExecutionOrder(-80)]
    public class FishPopulation : MonoBehaviour
    {
        static readonly ProfilerMarker SimMarker = new ProfilerMarker("Fish.Sim");
        static readonly ProfilerMarker EventsMarker = new ProfilerMarker("Fish.Events");
        static bool? enabledOverride;

        [SerializeField] FishTuning tuning;
        [SerializeField] FishBodies bodies;
        [SerializeField] MapConfig map;
        [Tooltip("The water material: the murk numbers are read from it (FishMurk.FromWater).")]
        [SerializeField] Material waterMaterial;
        [Tooltip("The shipped water motion: its waves' MaxAmplitude is the trough the depth clamp allows for.")]
        [SerializeField] WaterMotionSettings waterMotion;
        [Tooltip("f-level's structure anchors (bluffs, landing, slipway, island face, chute and hollow mouths).")]
        [SerializeField] BellsBendStructureAnchors anchors;
        [Tooltip("LevelMaps: shore distance for the open band's 50 m outer edge only. Serialized so it ships in builds.")]
        [SerializeField] BellsBendLevelMaps levelMaps;

        struct LiveCell { public int cx, cz; }

        readonly Dictionary<long, LiveCell> live = new Dictionary<long, LiveCell>(128);
        readonly Dictionary<long, long> blockedUntilTick = new Dictionary<long, long>(BlockedCapacity * 2);
        readonly List<long> drop = new List<long>(64);
        readonly List<FishGroupPlan> plan = new List<FishGroupPlan>(64);
        readonly List<Vector2Int> candidates = new List<Vector2Int>(64);
        FishTick tick;              // a field, never a property: Advance mutates it
        FishView view;
        PlayerController player;
        Camera viewCamera;
        readonly Plane[] frustum = new Plane[6];
        int plansThisFrame;

        public static FishPopulation Active { get; private set; }
        public FishTuning Tuning => tuning;
        public FishBodies Bodies => bodies;
        public MapConfig Map => map;
        public BellsBendLevelMaps LevelMaps => levelMaps;
        public BellsBendStructureAnchors Anchors => anchors;
        public FishSceneWater Water { get; private set; }
        public FishSimWorld World { get; private set; }
        public FishEventScheduler Events { get; private set; }
        public FishMurk Murk { get; private set; }
        public float WaveAmplitude { get; private set; }
        public int ActiveSeed { get; private set; }
        /// <summary>The renderer seam: <see cref="Count"/> live fish this frame, read-only to everyone else.</summary>
        public FishState[] States { get; private set; } = new FishState[0];
        public int Count { get; private set; }
        /// <summary>Written by the renderer each frame, aligned with <see cref="States"/>: how each fish was drawn (f-qa dump).</summary>
        public FishDrawMode[] DrawModes { get; private set; } = new FishDrawMode[0];
        /// <summary>Written by the renderer each frame: FishMurk visibility at each body's shallowest point (f-qa dump).</summary>
        public float[] Visibility { get; private set; } = new float[0];
        public int LiveCells => live.Count;
        /// <summary>Structure lookup: f-level's anchors by default; tests may replace it before Awake.</summary>
        public FishStructureQuery Structure { get; set; }

        /// <summary>Raised when a group goes live or away: (group slot, anchor, true = added). For f-qa's spawn log.</summary>
        public event Action<int, Vector3, bool> GroupChanged;

        /// <summary>
        /// Tests: the player as the fish see it (feet position and water state), instead of the PlayerController. F6 and
        /// F7 drive fish reactions directly; the controller's own wade/swim rules are covered by the water suites.
        /// </summary>
        public FishPlayer? PlayerOverride { get; set; }

        /// <summary>Tests: force fish on or off for the next scene load; null restores the scene's own state.</summary>
        public static void OverrideEnabled(bool? on) => enabledOverride = on;

        /// <summary>
        /// Process-level fish off (f-qa's regression A/B, without editing other suites): environment variable
        /// WA_FISH_OFF=1 or the command-line argument -fishOff disables every population exactly as OverrideEnabled(false).
        /// An explicit OverrideEnabled(true) wins (a fish test can still run in a fish-off session).
        /// </summary>
        public static bool ProcessFishOff =>
            enabledOverride != true &&
            (System.Environment.GetEnvironmentVariable(FishOffEnv) == "1" || System.Array.IndexOf(System.Environment.GetCommandLineArgs(), FishOffArg) >= 0);

        public const string FishOffEnv = "WA_FISH_OFF", FishOffArg = "-fishOff";

        void Awake()
        {
            // The plan v1 switch is for the F3 census diagnostic only; a leak into play would place fish by the old plan.
            if (FishPlan.LegacyRelocation) { Fail("FishPlan.LegacyRelocation is set (test-only plan v1 switch leaked)"); return; }
            if (enabledOverride == false) { enabled = false; return; }
            if (ProcessFishOff)
            {
                // One line f-qa's A/B can find in Editor.log / Player.log: the fish-off arm really ran fish-off.
                Debug.Log($"[Fish] disabled by {FishOffEnv}/{FishOffArg}");
                enabled = false;
                return;
            }
            string missing = !tuning ? "FishTuning" : !bodies ? "FishBodies" : !waterMaterial ? "water material" : !waterMotion ? "WaterMotionSettings"
                : !map ? "MapConfig" : !anchors ? "BellsBendStructureAnchors" : !levelMaps ? "BellsBendLevelMaps" : null;
            if (missing != null) { Fail($"no {missing}"); return; }
            WaveAmplitude = WaterMotion.MaxAmplitude(waterMotion.waves);
            string problem = tuning.Validate(bodies, WaveAmplitude);
            if (problem != null) { Fail($"tuning: {problem}"); return; }
            ActiveSeed = FishRandom.RunSeed(tuning.bakedSeed);
            var query = new FishAnchorQuery(anchors, tuning);
            if (query.Unmapped > 0) Debug.LogWarning($"FishPopulation: {query.Unmapped} anchors have a kind no structure type lists; they hold no extra fish", this);
            if (Structure == null) Structure = query.StructureAt;
            float maxZ = tuning.southOfNorthLineOnly ? map.northLineZ : float.PositiveInfinity;
            Water = new FishSceneWater(map, levelMaps, maxZ, QueryStructure);
            World = new FishSimWorld(tuning, bodies, Water, ActiveSeed, WaveAmplitude);
            Events = new FishEventScheduler(tuning, Water, ActiveSeed);
            States = new FishState[tuning.MaxLive];
            DrawModes = new FishDrawMode[tuning.MaxLive];
            Visibility = new float[tuning.MaxLive];
            tick = new FishTick(tuning.simHz);
            Murk = FishMurk.FromWater(waterMaterial, tuning.visibilityFloor);
        }

        void Fail(string why)
        {
            Debug.LogError($"FishPopulation: {why}; fish are off.", this);
            enabled = false;
        }

        int QueryStructure(float x, float z, out float anchorArea)
        {
            anchorArea = 0f;
            return Structure != null ? Structure(x, z, out anchorArea) : -1;
        }

        void OnEnable()
        {
            if (World != null) Active = this;
        }

        void OnDisable()
        {
            if (Active == this) Active = null;
            Count = 0;
        }

        void Update()
        {
            using (SimMarker.Auto())
            {
                FishPlayer p;
                if (PlayerOverride.HasValue) p = PlayerOverride.Value;
                else
                {
                    if (!player) player = FindAnyObjectByType<PlayerController>();
                    if (!player) { Count = 0; return; }
                    p = new FishPlayer { position = player.transform.position, mode = player.WaterMode };
                }
                p.nearWaterline = p.mode == WaterMode.Dry && NearWaterline(p.position);
                RefreshView();
                int ticks = tick.Advance(Time.deltaTime);
                if (ticks > 0)
                {
                    plansThisFrame = 0;
                    UpdateLiveSet(p.position);
                    using (EventsMarker.Auto()) plansThisFrame += Events.UpdateRing(p.position, tuning.maxCellPlansPerFrame - plansThisFrame);
                }
                for (int i = 0; i < ticks; i++)
                {
                    World.Tick(p);
                    using (EventsMarker.Auto()) Events.Tick(World, p.position, World.Step);
                }
                Count = World.Render(tick.Alpha, Time.deltaTime, States);
                // States were rewritten: last frame's draw decisions no longer line up with them (the renderer refills).
                System.Array.Clear(DrawModes, 0, DrawModes.Length);
                System.Array.Clear(Visibility, 0, Visibility.Length);
            }
        }

        /// <summary>Dry player within bankWalkLipDistance of water (8 probes on the ring and the feet).</summary>
        bool NearWaterline(Vector3 p)
        {
            float r = tuning.scatter.bankWalkLipDistance;
            for (int i = 0; i <= 8; i++)
            {
                float a = i * Mathf.PI * 0.25f, rr = i == 8 ? 0f : r;
                if (Water.TryBed(p.x + Mathf.Cos(a) * rr, p.z + Mathf.Sin(a) * rr, out float bed) && bed < Water.WaterLevelY) return true;
            }
            return false;
        }

        void RefreshView()
        {
            if (!viewCamera || !viewCamera.isActiveAndEnabled) viewCamera = Camera.main;
            if (viewCamera)
            {
                GeometryUtility.CalculateFrustumPlanes(viewCamera, frustum);
                view = new FishView(viewCamera.transform.position, frustum, tuning, Murk);
            }
            else view = new FishView(Vector3.zero, null, tuning, Murk);
        }

        /// <summary>
        /// Drops groups that lost their tile, drops far cells that are unseen, then brings in cells within simRadiusIn
        /// nearest first, within the per-frame plan budget, skipping cells still backing off.
        /// </summary>
        void UpdateLiveSet(Vector3 p)
        {
            for (int g = 0; g < World.GroupSlots; g++)
                if (World.GroupLive(g) && !World.GroupHasTile(g)) Remove(g);

            drop.Clear();
            foreach (var kv in live)
                if (CellDistance(kv.Value.cx, kv.Value.cz, p) > tuning.simRadiusOut && !CellSeen(kv.Key)) drop.Add(kv.Key);
            foreach (long id in drop)
            {
                for (int g = 0; g < World.GroupSlots; g++)
                    if (World.GroupLive(g) && World.GroupCell(g) == id) Remove(g);
                live.Remove(id);
            }

            FishLiveOrder.Nearest(p, tuning.cellSize, tuning.simRadiusIn, candidates);
            foreach (var c in candidates)
            {
                if (plansThisFrame >= tuning.maxCellPlansPerFrame) return;
                long id = FishRandom.CellId(c.x, c.y);
                if (live.ContainsKey(id)) continue;
                if (blockedUntilTick.TryGetValue(id, out long until) && tick.Count < until) continue;
                plansThisFrame++;
                if (TryActivate(id, c.x, c.y)) blockedUntilTick.Remove(id);
                else
                {
                    if (blockedUntilTick.Count >= BlockedCapacity) PruneBlocked();
                    blockedUntilTick[id] = tick.Count + Mathf.CeilToInt(tuning.blockedRetrySeconds * tuning.simHz);
                }
            }
        }

        const int BlockedCapacity = 64;

        void PruneBlocked()
        {
            drop.Clear();
            foreach (var kv in blockedUntilTick) if (tick.Count >= kv.Value) drop.Add(kv.Key);
            foreach (long id in drop) blockedUntilTick.Remove(id);
        }

        bool CellSeen(long id)
        {
            for (int g = 0; g < World.GroupSlots; g++)
                if (World.GroupLive(g) && World.GroupCell(g) == id && World.GroupVisible(g, view)) return true;
            return false;
        }

        bool TryActivate(long id, int cx, int cz)
        {
            plan.Clear();
            FishPlan.Cell(tuning, Water, ActiveSeed, cx, cz, plan);
            bool complete = true;
            for (int i = 0; i < plan.Count; i++) complete &= World.AddGroup(plan[i]) >= 0;
            // All or nothing: a cell over the cap, or with any new fish that could be seen, is taken back before a
            // frame is drawn (N7) and retried after the back-off.
            if (!complete || CellSeen(id))
            {
                for (int g = 0; g < World.GroupSlots; g++)
                    if (World.GroupLive(g) && World.GroupCell(g) == id) World.RemoveGroup(g);
                return false;
            }
            for (int g = 0; g < World.GroupSlots; g++)
                if (World.GroupLive(g) && World.GroupCell(g) == id) GroupChanged?.Invoke(g, World.GroupHome(g), true);
            live[id] = new LiveCell { cx = cx, cz = cz };
            return true;
        }

        void Remove(int g)
        {
            if (!World.GroupLive(g)) return;
            Vector3 home = World.GroupHome(g);
            World.RemoveGroup(g);
            GroupChanged?.Invoke(g, home, false);
        }

        float CellDistance(int cx, int cz, Vector3 p)
        {
            float s = tuning.cellSize;
            float dx = Mathf.Max(cx * s - p.x, 0f, p.x - (cx + 1) * s), dz = Mathf.Max(cz * s - p.z, 0f, p.z - (cz + 1) * s);
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        public void Configure(FishTuning t, FishBodies b, MapConfig m, Material water, WaterMotionSettings motion, BellsBendStructureAnchors structure, BellsBendLevelMaps maps)
        {
            tuning = t; bodies = b; map = m; waterMaterial = water; waterMotion = motion; anchors = structure; levelMaps = maps;
        }
    }
}
