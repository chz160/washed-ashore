using System.Collections.Generic;
using UnityEngine;

namespace WashedAshore.Fish
{
    /// <summary>
    /// Spontaneous surface signs (spec F7, adr/fish-1 §5.5) on a wider ring than bodies: every cell within eventRadius
    /// keeps its plan (the same pure function of (seed, cell) as the live set). Signs follow f-designer's band-rate rule:
    /// each cell fires at its bands' surfaceEventsPerMinPer100m per m² of wet band area (structure areas x their density
    /// multiplier) x the tuning's calibrated surfaceEventMultiplier; the group that makes each sign is picked by members x
    /// its species' per-fish rate (plus whole-school rates), which are relative weights only. Jumps keep their per-fish
    /// rates (capped in FishSimWorld). Every draw is seeded by (seed, cell, slot, tick).
    /// A group whose cell is live performs the sign with a real fish; otherwise a sign appears at its home (no body is
    /// drawn that far out). Caps (signs at once, jumps per minute) are in FishSimWorld. Pre-sized pools; no allocation
    /// once the ring is full.
    /// </summary>
    public sealed class FishEventScheduler
    {
        sealed class Entry
        {
            public long id;
            public int cx, cz;
            public readonly List<FishGroupPlan> groups = new List<FishGroupPlan>(16);
            public float[] signArea = new float[4];
            public float signsPerMin;       // the cell's band-rate sign rate before the multiplier
            public float farSignsPerMin;    // open-depth water beyond the open edge, at the open band's rate (staged, off)
        }

        readonly FishTuning t;
        readonly IFishWater water;
        readonly int seed;
        readonly Dictionary<long, Entry> cells = new Dictionary<long, Entry>(128);
        readonly Stack<Entry> pool = new Stack<Entry>(128);
        readonly List<long> drop = new List<long>(64);
        readonly List<Vector2Int> candidates = new List<Vector2Int>(256), order = new List<Vector2Int>(256);

        /// <summary>Far-water signs raised, and refused (by the live-sign cap or no far-water point found).</summary>
        public int FarRaised { get; private set; }
        public int FarRefused { get; private set; }
        uint tick;

        public FishEventScheduler(FishTuning tuning, IFishWater water, int seed)
        {
            t = tuning;
            this.water = water;
            this.seed = seed;
            for (int i = 0; i < 128; i++) pool.Push(new Entry());
        }

        public int Cells => cells.Count;

        /// <summary>
        /// Keeps the ring around the player: drops cells beyond eventRadius + one cell and plans new ones nearest-first,
        /// spending at most <paramref name="planBudget"/> plans (shared with the live set). Returns the plans used.
        /// </summary>
        public int UpdateRing(Vector3 player, int planBudget)
        {
            float s = t.cellSize, keep = t.eventRadius + s;
            drop.Clear();
            foreach (var kv in cells)
                if (CellDistance(kv.Value.cx, kv.Value.cz, player, s) > keep) drop.Add(kv.Key);
            foreach (long id in drop)
            {
                var e = cells[id];
                e.groups.Clear();
                pool.Push(e);
                cells.Remove(id);
            }
            if (planBudget <= 0) return 0;
            FishLiveOrder.Nearest(player, s, t.eventRadius, candidates);
            int used = 0;
            foreach (var c in candidates)
            {
                long id = FishRandom.CellId(c.x, c.y);
                if (cells.ContainsKey(id)) continue;
                if (used >= planBudget) break;
                var e = pool.Count > 0 ? pool.Pop() : new Entry();
                e.id = id; e.cx = c.x; e.cz = c.y;
                if (e.signArea.Length < t.bands.Length + 1) e.signArea = new float[t.bands.Length + 1];
                FishPlan.Cell(t, water, seed, c.x, c.y, e.groups, e.signArea);
                e.signsPerMin = 0f;
                for (int b = 0; b < t.bands.Length; b++) e.signsPerMin += e.signArea[b] * t.bands[b].PerM2(t.bands[b].surfaceEventsPerMinPer100m);
                e.farSignsPerMin = e.signArea[t.bands.Length] * t.bands[t.bands.Length - 1].farSignsPerM2PerMin;
                cells[id] = e;
                used++;
            }
            return used;
        }

        /// <summary>One fixed step of spontaneous signs for the whole ring.</summary>
        public void Tick(FishSimWorld world, Vector3 player, float dt)
        {
            tick++;
            this.player = player;
            // Nearest cells first: when the live-sign cap binds, the nearer signs are the ones admitted (f-td condition 4).
            FishLiveOrder.Nearest(player, t.cellSize, t.eventRadius, order);
            foreach (var c in order)
            {
                if (!cells.TryGetValue(FishRandom.CellId(c.x, c.y), out var e)) continue;
                // One band-rate draw per cell per tick; the sign goes to a group picked by members x per-fish weight.
                float rate = e.signsPerMin * t.surfaceEventMultiplier;
                if (rate > 0f && Fires(e.id, -7, rate, dt))
                {
                    float total = 0f;
                    foreach (var g in e.groups) total += SignWeight(g);
                    float pick = FishRandom.Value(seed, FishRandom.StreamEvents, e.id, -8, tick) * total;
                    foreach (var g in e.groups)
                    {
                        float wgt = SignWeight(g);
                        if (wgt <= 0f) continue;
                        pick -= wgt;
                        if (pick >= 0f) continue;
                        Perform(world, e.id, g, t.species[g.species].surfaceEvent);
                        break;
                    }
                }
                if (t.FarWaterSigns && e.farSignsPerMin > 0f && Fires(e.id, -9, e.farSignsPerMin * t.surfaceEventMultiplier, dt))
                {
                    if (FarSign(world, e)) FarRaised++; else FarRefused++;
                }
                for (int i = 0; i < e.groups.Count; i++)
                {
                    var g = e.groups[i];
                    if (!FishEventDistance.Within(g.anchor, player, t.eventRadius)) continue;
                    float jumps = t.species[g.species].jumpsPerFishPerMin * g.members;
                    if (jumps > 0f && Fires(e.id, g.groupIndex * 2 + 1, jumps, dt))
                        Perform(world, e.id, g, FishSurfaceKind.Jump);
                }
            }
        }

        /// <summary>A group's share of its cell's signs: members x per-fish rate, plus the whole-school rate (relative weights).</summary>
        float SignWeight(in FishGroupPlan g)
        {
            var sp = t.species[g.species];
            return sp.surfaceEvent == FishSurfaceKind.None ? 0f : sp.surfaceEventsPerFishPerMin * g.members + sp.schoolEventsPerMin;
        }

        bool Fires(long cell, int slot, float perMin, float dt) =>
            FishRandom.Value(seed, FishRandom.StreamEvents, cell, slot, tick) < perMin / 60f * dt;

        void Perform(FishSimWorld world, long cell, FishGroupPlan g, FishSurfaceKind kind)
        {
            int live = world.FindGroup(cell, g.groupIndex);
            if (live >= 0)
            {
                // A real fish does it: a seeded member of the live group.
                int n = 0;
                for (int f = world.GroupFirstFish(live); f >= 0; f = world.NextFish(f)) n++;
                int pick = (int)(FishRandom.Value(seed, FishRandom.StreamEvents, cell, -g.groupIndex - 1, tick) * n);
                for (int f = world.GroupFirstFish(live); f >= 0; f = world.NextFish(f))
                    if (pick-- == 0) { world.StartSurfaceEvent(f, kind); return; }
                return;
            }
            var sp = t.species[g.species];
            float ang = FishRandom.Value(seed, FishRandom.StreamEvents, cell, g.groupIndex, tick + 0x9000u) * 2f * Mathf.PI;
            float rad = sp.groupRadius * Mathf.Sqrt(FishRandom.Value(seed, FishRandom.StreamEvents, cell, g.groupIndex, tick + 0xA000u));
            Vector3 at = g.anchor + new Vector3(Mathf.Cos(ang) * rad, 0f, Mathf.Sin(ang) * rad);
            if (!water.TryBed(at.x, at.z, out float bed) || water.WaterLevelY - bed < sp.minBedDepth) at = g.anchor;
            if (!FishEventDistance.Within(at, player, t.eventRadius)) return;   // the one radius rule: raised = drawable
            world.RaiseSign(kind, at, sp.schoolUnit ? sp.groupRadius : 0.5f * (sp.lengthRange.x + sp.lengthRange.y), g.species);
        }

        Vector3 player;   // this tick's player position (admission radius)

        static readonly FishSurfaceKind[] FarKinds = { FishSurfaceKind.Rise, FishSurfaceKind.Swirl, FishSurfaceKind.Dimple };

        /// <summary>
        /// A far-water sign (brief F16): sign only (no body, no jump, fishId -1, flagged far), at a seeded point of the
        /// cell's far water, a Rise, Swirl or Dimple picked seeded and uniformly; its size comes from an open-band species
        /// picked by open-band share x per-fish rate.
        /// </summary>
        bool FarSign(FishSimWorld world, Entry e)
        {
            int ob = t.bands.Length - 1, pickS = -1;
            float total = 0f;
            for (int s = 0; s < t.species.Length; s++) total += FarWeight(s, ob);
            float pick = FishRandom.Value(seed, FishRandom.StreamEvents, e.id, -10, tick) * total;
            for (int s = 0; s < t.species.Length && pickS < 0; s++)
            {
                float wgt = FarWeight(s, ob);
                if (wgt <= 0f) continue;
                pick -= wgt;
                if (pick < 0f) pickS = s;
            }
            if (pickS < 0) return false;
            for (int k = 0; k < 8; k++)
            {
                float x = (e.cx + FishRandom.Value(seed, FishRandom.StreamEvents, e.id, -11, tick * 16u + (uint)k)) * t.cellSize;
                float z = (e.cz + FishRandom.Value(seed, FishRandom.StreamEvents, e.id, -12, tick * 16u + (uint)k)) * t.cellSize;
                if (!water.TryBed(x, z, out float bed) || FishPlan.BandAt(t, water, x, z, out _) >= 0) continue;
                if (!FishPlan.IsFarWater(t, water.WaterLevelY - bed)) continue;
                if (!FishEventDistance.Within(new Vector3(x, 0f, z), player, t.eventRadius)) continue;
                var sp = t.species[pickS];
                var kind = FarKinds[Mathf.Min(FarKinds.Length - 1, (int)(FishRandom.Value(seed, FishRandom.StreamEvents, e.id, -13, tick) * FarKinds.Length))];
                return world.RaiseSign(kind, new Vector3(x, bed, z), sp.schoolUnit ? sp.groupRadius : 0.5f * (sp.lengthRange.x + sp.lengthRange.y), pickS, far: true);
            }
            return false;
        }

        float FarWeight(int s, int band)
        {
            var sp = t.species[s];
            if (sp.surfaceEvent == FishSurfaceKind.None || sp.surfaceEvent == FishSurfaceKind.Jump) return 0f;
            float presence = sp.schoolUnit ? sp.schoolsPer100mByBand[band] : sp.per100mByBand[band];
            return presence * (sp.surfaceEventsPerFishPerMin + sp.schoolEventsPerMin);
        }

        static float CellDistance(int cx, int cz, Vector3 p, float s)
        {
            float dx = Mathf.Max(cx * s - p.x, 0f, p.x - (cx + 1) * s), dz = Mathf.Max(cz * s - p.z, 0f, p.z - (cz + 1) * s);
            return Mathf.Sqrt(dx * dx + dz * dz);
        }
    }

    /// <summary>Cells within a radius of a point, nearest first (by each cell's nearest point), into a reused list.</summary>
    public static class FishLiveOrder
    {
        static readonly List<float> keys = new List<float>(256);

        public static void Nearest(Vector3 p, float cellSize, float radius, List<Vector2Int> into)
        {
            into.Clear();
            keys.Clear();
            int r = Mathf.CeilToInt(radius / cellSize) + 1;
            int pcx = Mathf.FloorToInt(p.x / cellSize), pcz = Mathf.FloorToInt(p.z / cellSize);
            for (int dz = -r; dz <= r; dz++)
                for (int dx = -r; dx <= r; dx++)
                {
                    int cx = pcx + dx, cz = pcz + dz;
                    float ex = Mathf.Max(cx * cellSize - p.x, 0f, p.x - (cx + 1) * cellSize);
                    float ez = Mathf.Max(cz * cellSize - p.z, 0f, p.z - (cz + 1) * cellSize);
                    float d2 = ex * ex + ez * ez;
                    if (d2 > radius * radius) continue;
                    // Insertion keeps the list sorted; a few hundred cells at most, no allocation once sized.
                    int i = keys.Count;
                    keys.Add(d2);
                    into.Add(new Vector2Int(cx, cz));
                    while (i > 0 && keys[i - 1] > d2)
                    {
                        keys[i] = keys[i - 1]; into[i] = into[i - 1];
                        i--;
                    }
                    keys[i] = d2; into[i] = new Vector2Int(cx, cz);
                }
        }
    }
}
