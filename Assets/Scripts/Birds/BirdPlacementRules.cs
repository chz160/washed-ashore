using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using WashedAshore.Wildlife;

namespace WashedAshore.Birds
{
    /// <summary>
    /// Robin patches and flock POIs from bird-density-brief.md 3.1/3.2 (authored by level-designer).
    /// UnityEngine-only and NavMesh-free, so the editor bake (World.unity, seed 101) and per-seed test runs
    /// share one path. The route comes in as world points (the wildlife 3.5 loop, closed: last == first).
    /// Bird RNG is its own stream, so wildlife placement is unchanged. No rule is relaxed: a seed that can't
    /// place within 200 attempts per patch/POI and 20 assignment draws fails.
    /// </summary>
    public static partial class BirdPlacementRules
    {
        // 3.1 population.
        static readonly (PatchType type, int size)[] PatchSpec =
            { (PatchType.Trail, 3), (PatchType.Trail, 2), (PatchType.Trail, 2),
              (PatchType.Meadow, 3), (PatchType.Meadow, 2), (PatchType.Meadow, 2), (PatchType.Meadow, 2) };
        public static readonly int[] FlockSizes = { 6, 5, 4 };

        // 3.2 rules.
        public const int Attempts = 200, MaxDraws = 20, RobinArcs = 8, FlockArcs = 3, SeedSalt = 0x0B12D5;
        public const float MinGrass = 0.6f, MaxSlope = 15f, TreeMin = 6f, TreeMax = 30f, RockClear = 3f,
            MinFromSpawn = 20f, MaxFromCentre = 170f, PatchGap = 40f, WildlifeGap = 10f,
            StartRadius = 6f, StartGap = 1f, OffTrail = 3f, TrunkClear = 1.5f,
            EyeHeight = 1.65f, TargetHeight = 0.2f, SightStep = 2f, FoliageRadius = 2f,
            RidgeRadius = 40f, PoiMaxFromCentre = 190f, PoiGap = 120f;
        public static readonly Vector2 TrailBand = new Vector2(4f, 6f), MeadowBand = new Vector2(10f, 25f),
            SightBack = new Vector2(10f, 30f), PoiBand = new Vector2(50f, 150f), TerrainCentre = new Vector2(256f, 256f);

        // B1 amendment A5 (studio:decisions / amendment/birds-brief-A5): the 30 m clearing left no flock POIs on
        // World.unity for any seed. For the flock-POI clearing rule ONLY, the radius is 20 m and "tree" excludes Bush_*
        // and Rock_* (WildlifeRules' convention). Robin rules (nearest tree 6-30 m, trunk clearance) count bushes;
        // foliage occlusion counts bushes.
        public const float ClearingRadius = 20f;

        public static Vector2 Band(PatchType t) => t == PatchType.Trail ? TrailBand : MeadowBand;

        public static BirdPlan Plan(int seed, HabitatGround g, Vector3 spawn, List<Vector3> route, IList<Vector3> wildlifeAnchors,
            Func<Vector3, bool> inTestLane, out string error)
        {
            error = null;
            var site = new Site(g, spawn, route, wildlifeAnchors, inTestLane);
            var rng = new System.Random(unchecked(seed * 486187739 + SeedSalt));
            var plan = new BirdPlan { seed = seed, tallestCrown = site.TallestCrown, tallestCrownTopY = site.TallestCrownTop };

            // Robins: trail-edge patches first (tightest band), then meadow; arcs drawn from the valid set.
            var pool = ValidPatchAssignments();
            for (int draw = 1; draw <= MaxDraws && pool.Count > 0 && plan.patches.Count < PatchSpec.Length; draw++)
            {
                int pick = rng.Next(pool.Count);
                int[] arcOf = pool[pick];
                pool.RemoveAt(pick);
                plan.patchDraws = draw;
                plan.patches.Clear();
                string failed = null;
                for (int k = 0; k < PatchSpec.Length && failed == null; k++)
                {
                    var p = new PatchPlan { type = PatchSpec[k].type, size = PatchSpec[k].size, arc = arcOf[k],
                        name = $"RobinPatch_{k + 1}_{PatchSpec[k].type}" };
                    if (site.PlacePatch(p, plan.patches, rng)) plan.patches.Add(p);
                    else failed = $"{p.name} arc {p.arc + 1}";
                }
                if (failed != null) plan.redraws.Add($"patch draw {draw} [{string.Join(",", arcOf.Select(a => a + 1))}]: {failed} after {Attempts} attempts");
            }
            if (plan.patches.Count < PatchSpec.Length) { error = $"robin patches failed: {string.Join("; ", plan.redraws)}"; return null; }

            // Flocks: one primary POI per route third (shuffled), secondary in the next third.
            var perms = new List<int[]> { new[] { 0, 1, 2 }, new[] { 0, 2, 1 }, new[] { 1, 0, 2 }, new[] { 1, 2, 0 }, new[] { 2, 0, 1 }, new[] { 2, 1, 0 } };
            for (int draw = 1; draw <= MaxDraws && perms.Count > 0 && plan.flocks.Count < FlockSizes.Length; draw++)
            {
                int pick = rng.Next(perms.Count);
                int[] arcOf = perms[pick];
                perms.RemoveAt(pick);
                plan.flockDraws = draw;
                plan.flocks.Clear();
                string failed = null;
                for (int k = 0; k < FlockSizes.Length && failed == null; k++)
                {
                    var f = new FlockPlan { name = $"Flock_{k + 1}", size = FlockSizes[k], arc = arcOf[k], secondaryArc = (arcOf[k] + 1) % FlockArcs };
                    if (site.PlacePoi(f, plan.flocks, true, rng)) plan.flocks.Add(f);
                    else failed = $"{f.name} primary arc {f.arc + 1}";
                }
                for (int k = 0; k < plan.flocks.Count && failed == null; k++)
                    if (!site.PlacePoi(plan.flocks[k], plan.flocks, false, rng)) failed = $"{plan.flocks[k].name} secondary arc {plan.flocks[k].secondaryArc + 1}";
                if (failed == null) break;
                plan.redraws.Add($"flock draw {draw} [{string.Join(",", arcOf.Select(a => a + 1))}]: {failed} after {Attempts} attempts");
                plan.flocks.Clear();
            }
            if (plan.flocks.Count < FlockSizes.Length) { error = $"flock POIs failed: {string.Join("; ", plan.redraws)}"; return null; }
            return plan;
        }

        /// <summary>Every arc-per-patch permutation (7 of 8 arcs) whose 3 trail-edge arcs are pairwise non-adjacent (cyclic).</summary>
        static List<int[]> ValidPatchAssignments()
        {
            int n = RobinArcs;
            bool Adjacent(int a, int b) => (a - b + n) % n == 1 || (b - a + n) % n == 1;
            var result = new List<int[]>();
            var cur = new int[PatchSpec.Length];
            var used = new bool[n];
            void Recurse(int k)
            {
                if (k == PatchSpec.Length) { result.Add((int[])cur.Clone()); return; }
                for (int a = 0; a < n; a++)
                {
                    if (used[a]) continue;
                    if (PatchSpec[k].type == PatchType.Trail && Enumerable.Range(0, k).Any(j => PatchSpec[j].type == PatchType.Trail && Adjacent(cur[j], a))) continue;
                    used[a] = true; cur[k] = a;
                    Recurse(k + 1);
                    used[a] = false;
                }
            }
            Recurse(0);
            return result;
        }

        /// <summary>Terrain facts and the rule checks. Public so the editor Verify re-checks the saved scene with the same predicates.</summary>
        public class Site
        {
            const float Cell = 10f;
            struct Tree { public Vector2 xz; public float baseY, topY; }

            const int GridRes = 513; // the old 512 m terrain's heightmap: 1 m steps over the window
            readonly HabitatGround g;
            readonly Vector3 o, spawn;
            readonly Vector2 centre;
            float[,] grid;
            readonly IList<Vector3> wildlife;
            readonly Func<Vector3, bool> inLane;
            readonly List<Tree> trees = new List<Tree>();
            readonly Dictionary<Vector2Int, List<Tree>> treeCells = new Dictionary<Vector2Int, List<Tree>>();     // trees + bushes (robin rules)
            readonly Dictionary<Vector2Int, List<Tree>> clearingCells = new Dictionary<Vector2Int, List<Tree>>(); // trees only (A5 clearing)
            readonly Dictionary<Vector2Int, List<Vector2>> rockCells = new Dictionary<Vector2Int, List<Vector2>>();
            List<(Vector3 p, string kind, float routeDist, int arc)> pois;

            public Arcs Robin { get; }
            public Arcs Flock { get; }
            public List<Vector3> Route { get; }
            public float TallestCrown { get; private set; }
            public float TallestCrownTop { get; private set; }

            public Site(HabitatGround ground, Vector3 spawnPos, List<Vector3> route, IList<Vector3> wildlifeAnchors, Func<Vector3, bool> inTestLane)
            {
                g = ground; o = g.Origin; spawn = spawnPos;
                centre = g.ToWorld(TerrainCentre); // TerrainCentre is in habitat-window (old terrain) coordinates
                wildlife = wildlifeAnchors ?? Array.Empty<Vector3>();
                inLane = inTestLane ?? (_ => false);
                Route = route;
                Robin = new Arcs(route, RobinArcs);
                Flock = new Arcs(route, FlockArcs);
                var heights = new Dictionary<GameObject, float>();
                foreach (var inst in g.Trees())
                {
                    var prefab = inst.prefab;
                    if (!prefab) continue;
                    if (!heights.TryGetValue(prefab, out float protoHeight))
                        heights[prefab] = protoHeight = prefab.name.StartsWith("Rock_") ? 0f : PrototypeHeight(prefab);
                    var w = inst.world;
                    var xz = new Vector2(w.x, w.z);
                    if (prefab.name.StartsWith("Rock_")) { Bucket(rockCells, xz, xz); continue; }
                    var tree = new Tree { xz = xz, baseY = w.y, topY = w.y + protoHeight * inst.heightScale };
                    trees.Add(tree); // foliage occluders include bushes
                    Bucket(treeCells, xz, tree);
                    if (!prefab.name.StartsWith("Bush_")) Bucket(clearingCells, xz, tree);
                    if (tree.topY - tree.baseY > TallestCrown) TallestCrown = tree.topY - tree.baseY;
                    TallestCrownTop = Mathf.Max(TallestCrownTop, tree.topY);
                }
            }

            public float Height(Vector3 p) => g.Height(p);
            public Vector3 OnGround(Vector3 p) { p.y = Height(p); return p; }
            public float Slope(Vector3 p) => g.Steepness(p);
            public bool IsLand(Vector3 p) => g.IsLand(p);

            public float Grass(Vector3 p) => g.Splat(p, 0);

            /// <summary>Flat distance to the nearest non-rock tree instance (bushes included), or +inf beyond <paramref name="max"/>.</summary>
            public float NearestTree(Vector3 p, float max) => Nearest(treeCells, p, max);

            /// <summary>A5 flock-POI clearing: no tree within 20 m, where bushes and rocks are not trees.</summary>
            public bool IsClearing(Vector3 p) => float.IsInfinity(Nearest(clearingCells, p, ClearingRadius));

            static float Nearest(Dictionary<Vector2Int, List<Tree>> cells, Vector3 p, float max)
            {
                float best = float.PositiveInfinity; var xz = Flat(p);
                Visit(cells, xz, max, tr => best = Mathf.Min(best, Vector2.Distance(tr.xz, xz)));
                return best <= max ? best : float.PositiveInfinity;
            }

            public bool RockNear(Vector3 p, float r)
            {
                bool hit = false; var xz = Flat(p);
                Visit(rockCells, xz, r, q => hit |= Vector2.Distance(q, xz) <= r);
                return hit;
            }

            public float RouteDistance(Vector3 p) => Robin.Distance(Flat(p));

            /// <summary>Every 3.2 patch rule <paramref name="p"/> breaks (empty = valid). Starts are checked separately.</summary>
            public List<string> PatchFaults(PatchPlan p, IEnumerable<PatchPlan> others)
            {
                var c = p.centre; var f = new List<string>();
                var band = Band(p.type);
                float rd = RouteDistance(c);
                if (rd < band.x || rd > band.y) f.Add($"route {rd:F1} not {band.x}-{band.y}");
                if (Robin.ArcOf(Flat(c)) != p.arc) f.Add("arc");
                if (Vector2.Distance(Flat(c), Flat(spawn)) < MinFromSpawn) f.Add("spawn<20");
                if (inLane(c)) f.Add("testLane");
                if (Vector2.Distance(Flat(c), centre) > MaxFromCentre) f.Add("centre>170");
                if (Grass(c) < MinGrass) f.Add("grass<0.6");
                if (Slope(c) > MaxSlope) f.Add("slope>15");
                if (!IsLand(c)) f.Add("notLand");
                float nt = NearestTree(c, TreeMax);
                if (nt < TreeMin || float.IsInfinity(nt)) f.Add($"nearestTree {nt:F1} not 6-30");
                if (RockNear(c, RockClear)) f.Add("rock<3");
                if (others.Any(q => q != p && Vector2.Distance(Flat(q.centre), Flat(c)) < PatchGap)) f.Add("patchGap<40");
                if (wildlife.Any(w => Vector2.Distance(Flat(w), Flat(c)) < WildlifeGap)) f.Add("wildlife<10");
                if (SightlineBack(c) < 0f) f.Add("sightline");
                return f;
            }

            public List<string> StartFaults(PatchPlan p, int i)
            {
                var s = p.starts[i]; var f = new List<string>();
                if (Vector2.Distance(Flat(s), Flat(p.centre)) > StartRadius) f.Add("start>6");
                if (RouteDistance(s) < OffTrail) f.Add("start route<3");
                if (Grass(s) < MinGrass) f.Add("start grass<0.6");
                if (inLane(s)) f.Add("start testLane");
                if (NearestTree(s, TrunkClear) <= TrunkClear) f.Add("start trunk<1.5");
                for (int j = 0; j < p.starts.Count; j++)
                    if (j != i && Vector2.Distance(Flat(p.starts[j]), Flat(s)) < StartGap) f.Add("start gap<1");
                return f;
            }

            /// <summary>3.2 approach sightline: first clear route point 10-30 m before the patch (terrain/trunks + foliage), or -1.</summary>
            public float SightlineBack(Vector3 c)
            {
                float s = Robin.NearestS(Flat(c));
                var target = new Vector3(c.x, Height(c) + TargetHeight, c.z);
                for (float back = SightBack.x; back <= SightBack.y + 0.01f; back += SightStep)
                {
                    var from = Robin.PointAt(s - back);
                    from.y = Height(from) + EyeHeight;
                    if (!TerrainBlocks(from, target) && FoliageClear(from, target)) return back;
                }
                return -1f;
            }

            public bool PlacePatch(PatchPlan p, List<PatchPlan> placed, System.Random rng)
            {
                var band = Band(p.type);
                for (int attempt = 0; attempt < Attempts; attempt++)
                {
                    float s = (p.arc + (float)rng.NextDouble()) * Robin.ArcLength;
                    var on = Robin.PointAt(s); var tan = Robin.Tangent(s);
                    var normal = new Vector3(-tan.z, 0f, tan.x) * (rng.Next(2) == 0 ? -1f : 1f);
                    p.centre = OnGround(on + normal * Mathf.Lerp(band.x, band.y, (float)rng.NextDouble()));
                    if (PatchFaults(p, placed).Count > 0) continue;
                    if (!PlaceStarts(p, rng)) continue;
                    p.routeDistance = RouteDistance(p.centre);
                    p.nearestTree = NearestTree(p.centre, TreeMax);
                    p.slope = Slope(p.centre);
                    p.grass = Grass(p.centre);
                    p.sightlineBack = SightlineBack(p.centre);
                    return true;
                }
                return false;
            }

            bool PlaceStarts(PatchPlan p, System.Random rng)
            {
                p.starts.Clear();
                for (int i = 0; i < p.size; i++)
                {
                    bool ok = false;
                    for (int attempt = 0; attempt < 50 && !ok; attempt++)
                    {
                        float a = (float)rng.NextDouble() * Mathf.PI * 2f, r = StartRadius * Mathf.Sqrt((float)rng.NextDouble());
                        p.starts.Add(OnGround(p.centre + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r)));
                        ok = StartFaults(p, i).Count == 0;
                        if (!ok) p.starts.RemoveAt(i);
                    }
                    if (!ok) return false;
                }
                return true;
            }

            /// <summary>Faults of a flock POI against 3.2 (kind = clearing/ridge; distance to the other flocks' POIs checked by the caller).</summary>
            public List<string> PoiFaults(Vector3 p, int arc)
            {
                var f = new List<string>();
                float rd = RouteDistance(p);
                if (rd < PoiBand.x || rd > PoiBand.y) f.Add($"route {rd:F0} not 50-150");
                if (Vector2.Distance(Flat(p), centre) > PoiMaxFromCentre) f.Add("centre>190");
                if (Flock.ArcOf(Flat(p)) != arc) f.Add("arc");
                if (PoiKind(p) == null) f.Add("notClearingOrRidge");
                if (!IsLand(p)) f.Add("notLand");
                return f;
            }

            public string PoiKind(Vector3 p)
            {
                if (IsClearing(p)) return "clearing";
                return IsRidge(p) ? "ridge" : null;
            }

            /// <summary>Hilltop/ridge: higher than every heightmap sample within 40 m.</summary>
            public bool IsRidge(Vector3 p)
            {
                var h = Grid(out float step, out int pad);
                int res = h.GetLength(0);
                int cx = Mathf.RoundToInt((p.x - o.x) / step) + pad, cz = Mathf.RoundToInt((p.z - o.z) / step) + pad, rr = Mathf.CeilToInt(RidgeRadius / step);
                if (cx < pad || cz < pad || cx >= res - pad || cz >= res - pad) return false; // outside the habitat window
                float me = h[cz, cx];
                for (int z = Mathf.Max(0, cz - rr); z <= Mathf.Min(res - 1, cz + rr); z++)
                    for (int x = Mathf.Max(0, cx - rr); x <= Mathf.Min(res - 1, cx + rr); x++)
                    {
                        if (x == cx && z == cz) continue;
                        if (((x - cx) * (x - cx) + (z - cz) * (z - cz)) * step * step > RidgeRadius * RidgeRadius) continue;
                        if (h[z, x] >= me) return false;
                    }
                return true;
            }

            /// <summary>World heights at the old terrain's 1 m heightmap spacing over the habitat window, padded by
            /// RidgeRadius on every side so a hill that runs past the window edge is not taken for a peak.</summary>
            float[,] Grid(out float step, out int pad)
            {
                step = g.Size.x / (GridRes - 1);
                pad = Mathf.CeilToInt(RidgeRadius / step);
                if (grid != null) return grid;
                int res = GridRes + 2 * pad;
                grid = new float[res, res];
                for (int z = 0; z < res; z++)
                    for (int x = 0; x < res; x++)
                        grid[z, x] = Height(new Vector3(o.x + (x - pad) * step, 0f, o.z + (z - pad) * step));
                return grid;
            }

            public bool PlacePoi(FlockPlan f, List<FlockPlan> flocks, bool primary, System.Random rng)
            {
                int arc = primary ? f.arc : f.secondaryArc;
                var cands = Pois().Where(c => c.arc == arc).ToList();
                // POIs of the other flocks (primary and secondary) stay >= 120 m away.
                var taken = flocks.Where(q => q != f).SelectMany(q => q.secondaryKind != null ? new[] { q.primary, q.secondary } : new[] { q.primary }).ToList();
                for (int attempt = 0; attempt < Attempts && cands.Count > 0; attempt++)
                {
                    var c = cands[rng.Next(cands.Count)];
                    if (taken.Any(q => Vector2.Distance(Flat(q), Flat(c.p)) < PoiGap)) continue;
                    if (primary) { f.primary = c.p; f.primaryKind = c.kind; f.primaryRouteDistance = c.routeDist; }
                    else { f.secondary = c.p; f.secondaryKind = c.kind; f.secondaryRouteDistance = c.routeDist; }
                    return true;
                }
                return false;
            }

            /// <summary>Static POI candidates in a fixed order: clearings on a 4 m grid, then hilltops found by hill-climbing.</summary>
            List<(Vector3 p, string kind, float routeDist, int arc)> Pois()
            {
                if (pois != null) return pois;
                var raw = new List<(Vector3, string)>();
                for (float z = o.z + 4f; z < o.z + g.Size.y; z += 4f)
                    for (float x = o.x + 4f; x < o.x + g.Size.x; x += 4f)
                    {
                        var p = OnGround(new Vector3(x, 0f, z));
                        if (Vector2.Distance(Flat(p), centre) > PoiMaxFromCentre || !IsLand(p)) continue;
                        if (IsClearing(p)) raw.Add((p, "clearing"));
                    }
                foreach (var p in HillTops()) raw.Add((p, "ridge"));
                pois = new List<(Vector3, string, float, int)>();
                foreach (var (p, kind) in raw)
                {
                    float rd = RouteDistance(p);
                    if (rd < PoiBand.x || rd > PoiBand.y || Vector2.Distance(Flat(p), centre) > PoiMaxFromCentre) continue;
                    pois.Add((p, kind, rd, Flock.ArcOf(Flat(p))));
                }
                return pois;
            }

            List<Vector3> HillTops()
            {
                // Hill-climb inside the window only (as on the old terrain); IsRidge sees the padding.
                var h = Grid(out float step, out int pad);
                int res = GridRes;
                var peaks = new HashSet<Vector2Int>();
                for (int z = 4; z < res; z += 8)
                    for (int x = 4; x < res; x += 8)
                    {
                        int cx = x, cz = z;
                        while (true)
                        {
                            int bx = cx, bz = cz;
                            for (int dz = -1; dz <= 1; dz++)
                                for (int dx = -1; dx <= 1; dx++)
                                {
                                    int nx = cx + dx, nz = cz + dz;
                                    if (nx < 0 || nz < 0 || nx >= res || nz >= res) continue;
                                    if (h[nz + pad, nx + pad] > h[bz + pad, bx + pad]) { bx = nx; bz = nz; }
                                }
                            if (bx == cx && bz == cz) break;
                            cx = bx; cz = bz;
                        }
                        peaks.Add(new Vector2Int(cx, cz));
                    }
                return peaks.OrderBy(q => q.y).ThenBy(q => q.x)
                    .Select(q => OnGround(new Vector3(o.x + q.x * step, 0f, o.z + q.y * step)))
                    .Where(p => IsLand(p) && IsRidge(p)).ToList();
            }

            public bool FoliageClear(Vector3 from, Vector3 to)
            {
                var a = Flat(from); var ab = Flat(to) - a;
                float len2 = Mathf.Max(ab.sqrMagnitude, 1e-4f);
                foreach (var tr in trees)
                {
                    float u = Mathf.Clamp01(Vector2.Dot(tr.xz - a, ab) / len2);
                    if ((a + ab * u - tr.xz).sqrMagnitude > FoliageRadius * FoliageRadius) continue;
                    float y = Mathf.Lerp(from.y, to.y, u);
                    if (y >= tr.baseY && y <= tr.topY) return false;
                }
                return true;
            }

            static readonly RaycastHit[] Hits = new RaycastHit[16];

            /// <summary>Terrain and its tree (trunk) colliders; matches the wildlife sightline.</summary>
            static bool TerrainBlocks(Vector3 from, Vector3 to)
            {
                var dir = to - from; float len = dir.magnitude;
                int n = Physics.RaycastNonAlloc(from, dir / len, Hits, len, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < n; i++) if (Hits[i].collider is TerrainCollider) return true;
                return false;
            }

            static void Bucket<T>(Dictionary<Vector2Int, List<T>> cells, Vector2 xz, T item)
            {
                var k = new Vector2Int(Mathf.FloorToInt(xz.x / Cell), Mathf.FloorToInt(xz.y / Cell));
                if (!cells.TryGetValue(k, out var list)) cells[k] = list = new List<T>();
                list.Add(item);
            }

            static void Visit<T>(Dictionary<Vector2Int, List<T>> cells, Vector2 xz, float r, Action<T> onItem)
            {
                int x0 = Mathf.FloorToInt((xz.x - r) / Cell), x1 = Mathf.FloorToInt((xz.x + r) / Cell);
                int z0 = Mathf.FloorToInt((xz.y - r) / Cell), z1 = Mathf.FloorToInt((xz.y + r) / Cell);
                for (int x = x0; x <= x1; x++)
                    for (int z = z0; z <= z1; z++)
                        if (cells.TryGetValue(new Vector2Int(x, z), out var list)) foreach (var i in list) onItem(i);
            }

            static float PrototypeHeight(GameObject prefab)
            {
                float top = 0f;
                foreach (var mf in prefab.GetComponentsInChildren<MeshFilter>())
                {
                    if (!mf.sharedMesh) continue;
                    var bb = mf.sharedMesh.bounds;
                    for (int i = 0; i < 8; i++)
                    {
                        var c = new Vector3(i & 1, (i >> 1) & 1, (i >> 2) & 1);
                        top = Mathf.Max(top, prefab.transform.InverseTransformPoint(mf.transform.TransformPoint(bb.min + Vector3.Scale(bb.size, c))).y);
                    }
                }
                return top;
            }
        }

        static Vector2 Flat(Vector3 v) => new Vector2(v.x, v.z);
    }
}
