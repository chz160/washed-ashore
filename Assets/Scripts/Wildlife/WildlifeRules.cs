using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;

namespace WashedAshore.Wildlife
{
    /// <summary>
    /// Anchor and member rules from wildlife-density-brief.md 3.1/3.2/3.5 (authored by level-designer).
    /// UnityEngine-only so the runtime placer and the A6 seeds (101, 202, 303) share one path with the
    /// editor bake. Counts, cohesion, distances and the route come from <see cref="WildlifeTuning"/>.
    /// </summary>
    public static class WildlifeRules
    {
        // Mirrors WorldBuilder: spawn clearing 22 m, test lane 10 m x 36 m along yaw 45.
        const float SpawnClear = 22f, SpawnYaw = 45f;

        public class GroupPlan
        {
            public WildlifeSpecies species;
            public string name;
            public Vector3 anchor;
            public float routeDistance, spawnDistance;
            public int arc; // R0 route arc, 0-based from W0
            // R1 approach sightline: the clear route point used (distance before the anchor, arc length, segment).
            public float sightlineBack = -1f, sightlineFromS = -1f;
            public int sightlineFromSegment = -1;
            public List<Vector3> members = new List<Vector3>();
            public List<float> yaws = new List<float>();
        }

        /// <summary>Route waypoints in world space, on the NavMesh where possible (nudged up to 10 m).</summary>
        public static List<Vector3> CheckRoute(WildlifeTuning tuning, Vector3 spawn, out string note)
        {
            var offsets = tuning.sighting.routeOffsets;
            var terrain = Terrain.activeTerrain;
            var pts = new List<Vector3>();
            var notes = new List<string>();
            for (int i = 0; i < offsets.Length; i++)
            {
                var p = spawn + new Vector3(offsets[i].x, 0f, offsets[i].y);
                p.y = terrain.SampleHeight(p) + terrain.transform.position.y;
                if (NavMesh.SamplePosition(p, out var hit, 3f, NavMesh.AllAreas)) { pts.Add(hit.position); continue; }
                Vector3? nudged = null;
                for (float r = 1f; r <= 10f && nudged == null; r += 1f)
                    for (int a = 0; a < 16 && nudged == null; a++)
                    {
                        var q = p + Quaternion.Euler(0f, a * 22.5f, 0f) * Vector3.forward * r;
                        q.y = terrain.SampleHeight(q) + terrain.transform.position.y;
                        if (NavMesh.SamplePosition(q, out var h2, 3f, NavMesh.AllAreas)) nudged = h2.position;
                    }
                if (nudged == null) { notes.Add($"W{i}:OFFMESH"); pts.Add(p); continue; }
                notes.Add($"W{i}:nudged({nudged.Value.x - spawn.x:F0},{nudged.Value.z - spawn.z:F0})");
                pts.Add(nudged.Value);
            }
            note = notes.Count == 0 ? $"all {offsets.Length - 1} waypoints on NavMesh within 3 m" : string.Join(",", notes);
            return pts;
        }

        static int PlanOrder(WildlifeSpecies s) =>
            s == WildlifeSpecies.Deer ? 0 : s == WildlifeSpecies.Wolf ? 1 : s == WildlifeSpecies.Stag ? 2 : 3;

        /// <summary>How a seed's plan was reached (R1-A evidence).</summary>
        public class PlanLog
        {
            public int validAssignments;
            public int draws;
            public List<string> redraws = new List<string>();
            public int[] finalAssignment;
        }

        public static List<GroupPlan> PlanGroups(WildlifeTuning tuning, int seed, Terrain t, Vector3 spawn, List<Vector3> route, out string error) =>
            PlanGroups(tuning, seed, t, spawn, route, out error, out _);

        public static List<GroupPlan> PlanGroups(WildlifeTuning tuning, int seed, Terrain t, Vector3 spawn, List<Vector3> route,
            out string error, out PlanLog log)
        {
            error = null;
            log = new PlanLog();
            var r = tuning.placement;
            var rng = new System.Random(seed);
            var site = new Site(tuning, t, spawn, route);

            // Deer first: stag, fox and wolf rules reference deer anchors.
            var order = tuning.population.OrderBy(p => PlanOrder(p.species))
                .SelectMany(spec => Enumerable.Range(1, spec.groupCount).Select(g => (spec, g))).ToList();
            if (order.Count > r.routeArcs) { error = $"{order.Count} groups but only {r.routeArcs} route arcs"; return null; }

            // R0/R1: one route arc per group, drawn from the assignments that satisfy the arc rules, so a seed
            // never fails on how arcs combine. R1-A: if an anchor fails geometrically, draw another (no rule relaxes).
            var pool = ValidAssignments(order.Select(o2 => o2.spec.species).ToList(), r);
            log.validAssignments = pool.Count;
            if (pool.Count == 0) { error = "no route-arc assignment satisfies the arc rules"; return null; }

            for (int draw = 1; draw <= r.maxAssignmentDraws && pool.Count > 0; draw++)
            {
                int pick = rng.Next(pool.Count);
                int[] arcOf = pool[pick];
                pool.RemoveAt(pick);
                log.draws = draw;
                log.finalAssignment = arcOf;

                var plans = new List<GroupPlan>();
                string failed = null;
                for (int k = 0; k < order.Count && failed == null; k++)
                {
                    var (spec, g) = order[k];
                    var plan = new GroupPlan { species = spec.species, name = $"{spec.species}_Group{g}", arc = arcOf[k] };
                    if (site.PlaceAnchor(plan, spec.groupSize, tuning.Get(spec.species).cohesionRadius, plans, rng))
                        plans.Add(plan);
                    else
                        failed = $"{plan.name} ({spec.species}) arc {plan.arc + 1} after {r.attemptsPerAnchor} attempts";
                }
                if (failed == null) return plans;
                log.redraws.Add($"draw {draw} [{string.Join(",", arcOf.Select(a => a + 1))}]: {failed}");
            }
            error = $"no valid anchors after {log.draws} arc-assignment draws: {string.Join("; ", log.redraws)}";
            return null;
        }

        /// <summary>Every arc-per-group permutation that satisfies the arc rules, in lexicographic order
        /// (wolf off its forbidden arcs; R1: deer arcs not adjacent, stag not adjacent to deer; cyclic).</summary>
        static List<int[]> ValidAssignments(List<WildlifeSpecies> groups, PlacementRules r)
        {
            int n = r.routeArcs;
            bool Adjacent(int a, int b) => (a - b + n) % n == 1 || (b - a + n) % n == 1;
            bool Valid(int[] arcs)
            {
                var deerArcs = Enumerable.Range(0, groups.Count).Where(k => groups[k] == WildlifeSpecies.Deer).Select(k => arcs[k]).ToList();
                for (int k = 0; k < groups.Count; k++)
                {
                    int a = arcs[k];
                    if (groups[k] == WildlifeSpecies.Wolf && r.wolfForbiddenArcs != null && r.wolfForbiddenArcs.Contains(a)) return false;
                    if (groups[k] == WildlifeSpecies.Deer && r.deerArcsNotAdjacent && deerArcs.Any(b => b != a && Adjacent(a, b))) return false;
                    if (groups[k] == WildlifeSpecies.Stag && r.stagNotAdjacentToDeer && deerArcs.Any(b => Adjacent(a, b))) return false;
                }
                return true;
            }
            var result = new List<int[]>();
            var current = new int[groups.Count];
            var used = new bool[n];
            void Recurse(int k)
            {
                if (k == groups.Count) { if (Valid(current)) result.Add((int[])current.Clone()); return; }
                for (int a = 0; a < n; a++)
                {
                    if (used[a]) continue;
                    used[a] = true;
                    current[k] = a;
                    Recurse(k + 1);
                    used[a] = false;
                }
            }
            Recurse(0);
            return result;
        }

        /// <summary>Terrain facts the habitat rules need, sampled once per plan.</summary>
        class Site
        {
            readonly PlacementRules r;
            readonly Terrain t;
            readonly TerrainData d;
            readonly Vector3 o, spawn;
            readonly List<Vector3> route;
            readonly RouteArcs arcs;
            readonly float[,,] splat;
            readonly List<Vector2> trees = new List<Vector2>(), cover = new List<Vector2>();

            public Site(WildlifeTuning tuning, Terrain terrain, Vector3 spawnPos, List<Vector3> routePts)
            {
                r = tuning.placement;
                t = terrain;
                d = t.terrainData;
                o = t.transform.position;
                spawn = spawnPos;
                route = routePts;
                arcs = new RouteArcs(route, r.routeArcs);
                splat = d.GetAlphamaps(0, 0, d.alphamapWidth, d.alphamapHeight);
                var protoNames = d.treePrototypes.Select(p => p.prefab ? p.prefab.name : "").ToArray();
                foreach (var inst in d.treeInstances)
                {
                    var w = new Vector2(o.x + inst.position.x * d.size.x, o.z + inst.position.z * d.size.z);
                    string n = protoNames[inst.prototypeIndex];
                    bool bush = r.foxCoverPrefixes.Any(n.StartsWith);
                    bool rock = n.StartsWith("Rock_");
                    if (!bush && !rock && !n.StartsWith("Bush_")) trees.Add(w);
                    if (bush || (!rock && !n.StartsWith("Bush_"))) cover.Add(w);
                }
            }

            public bool PlaceAnchor(GroupPlan plan, int size, float cohesion, List<GroupPlan> plans, System.Random rng)
            {
                var deer = plans.Where(q => q.species == WildlifeSpecies.Deer).ToList();
                for (int attempt = 0; attempt < r.attemptsPerAnchor; attempt++)
                {
                    // Draw in the assigned arc, uniformly over the minFromRoute..maxFromRoute annulus, then test every rule.
                    var onRoute = arcs.PointAt((plan.arc + (float)rng.NextDouble()) * arcs.ArcLength);
                    float ang = (float)rng.NextDouble() * Mathf.PI * 2f;
                    float rad = Mathf.Sqrt(Mathf.Lerp(r.minFromRoute * r.minFromRoute, r.maxFromRoute * r.maxFromRoute, (float)rng.NextDouble()));
                    var c = onRoute + new Vector3(Mathf.Cos(ang) * rad, 0f, Mathf.Sin(ang) * rad);
                    c.y = t.SampleHeight(c) + o.y;
                    if (!NavMesh.SamplePosition(c, out var hit, r.navMeshSnap, NavMesh.AllAreas)) continue;
                    var p = hit.position;
                    var p2 = new Vector2(p.x, p.z);
                    float spawnDist = Vector2.Distance(p2, new Vector2(spawn.x, spawn.z));
                    float nx = (p.x - o.x) / d.size.x, nz = (p.z - o.z) / d.size.z;
                    if (spawnDist < r.minFromSpawn) continue;
                    if (Mathf.Min(p.x - o.x, p.z - o.z, o.x + d.size.x - p.x, o.z + d.size.z - p.z) < r.minFromEdge) continue;
                    if (Vector2.Distance(p2, r.terrainCentre) > r.maxFromCentre) continue;
                    if (plans.Any(q => Flat(q.anchor, p) < r.minBetweenAnchors)) continue;
                    float routeDist = RouteDistance(route, p2);
                    if (routeDist > r.maxFromRoute || routeDist < r.minFromRoute) continue;
                    if (arcs.ArcOf(p2) != plan.arc) continue; // an offset near a corner can land in the next arc
                    float slope = d.GetSteepness(nx, nz);
                    switch (plan.species)
                    {
                        case WildlifeSpecies.Deer:
                            int sx = Mathf.Clamp(Mathf.RoundToInt(nx * (d.alphamapWidth - 1)), 0, d.alphamapWidth - 1);
                            int sz = Mathf.Clamp(Mathf.RoundToInt(nz * (d.alphamapHeight - 1)), 0, d.alphamapHeight - 1);
                            if (splat[sz, sx, 0] < r.deerMinGrass || slope > r.deerMaxSlope || !Near(trees, p2, r.deerMaxFromTree)) continue;
                            if (deer.Any(q => Flat(q.anchor, p) < r.deerHerdsMinApart)) continue;
                            break;
                        case WildlifeSpecies.Stag:
                            if (slope > r.stagMaxSlope || deer.Any(q => Flat(q.anchor, p) < r.stagMinFromDeer)) continue;
                            break;
                        case WildlifeSpecies.Fox:
                            if (slope > r.foxMaxSlope || !Near(cover, p2, r.foxMaxFromCover)) continue;
                            break;
                        case WildlifeSpecies.Wolf:
                            if (slope > r.wolfMaxSlope || spawnDist < r.wolfMinFromSpawn
                                || deer.Any(q => Flat(q.anchor, p) < r.wolfMinFromDeer)) continue;
                            break;
                    }
                    float back = -1f, fromS = -1f;
                    if (r.approachSightline && !ApproachClear(arcs, t, p, r, out back, out fromS)) continue;
                    if (!PlaceMembers(rng, plan, p, size, cohesion, spawn)) continue;
                    plan.anchor = p;
                    plan.routeDistance = routeDist;
                    plan.spawnDistance = spawnDist;
                    plan.sightlineBack = back;
                    plan.sightlineFromS = fromS;
                    plan.sightlineFromSegment = arcs.SegmentAt(fromS);
                    return true;
                }
                return false;
            }
        }

        /// <summary>R1 approach sightline: from at least one route point 40-80 m before the anchor's nearest
        /// route point (walk direction, every 5 m), an eye-height ray to the anchor must miss the terrain and
        /// its tree colliders.</summary>
        public static bool ApproachClear(RouteArcs arcs, Terrain t, Vector3 anchor, PlacementRules r) =>
            ApproachClear(arcs, t, anchor, r, out _, out _);

        /// <param name="usedBack">Distance before the anchor of the first clear route point, or -1.</param>
        /// <param name="usedS">Arc length (from W0) of that route point, or -1.</param>
        public static bool ApproachClear(RouteArcs arcs, Terrain t, Vector3 anchor, PlacementRules r, out float usedBack, out float usedS)
        {
            float s = arcs.NearestS(new Vector2(anchor.x, anchor.z));
            Vector3 target = new Vector3(anchor.x, t.SampleHeight(anchor) + t.transform.position.y + r.sightlineTargetHeight, anchor.z);
            for (float back = r.sightlineBack.x; back <= r.sightlineBack.y + 0.01f; back += r.sightlineStep)
            {
                Vector3 from = arcs.PointAt(s - back);
                from.y = t.SampleHeight(from) + t.transform.position.y + r.sightlineEyeHeight;
                if (TerrainBlocks(from, target)) continue;
                usedBack = back;
                usedS = arcs.Wrap(s - back);
                return true;
            }
            usedBack = usedS = -1f;
            return false;
        }

        static readonly RaycastHit[] SightHits = new RaycastHit[16];

        /// <summary>Only the terrain (including its tree colliders) blocks the approach sightline, matching
        /// A6, which ignores the player and animals. The player's capsule sits at W0 on the same layer.</summary>
        static bool TerrainBlocks(Vector3 from, Vector3 to)
        {
            Vector3 d = to - from;
            float len = d.magnitude;
            int n = Physics.RaycastNonAlloc(from, d / len, SightHits, len, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
                if (SightHits[i].collider is TerrainCollider) return true;
            return false;
        }

        /// <summary>The closed route split into equal-length arcs, numbered from W0 (0-based).</summary>
        public class RouteArcs
        {
            readonly List<Vector3> route;
            readonly float[] cum;
            readonly int count;

            public float Length { get; }
            public float ArcLength => Length / count;

            public RouteArcs(List<Vector3> closedRoute, int arcCount)
            {
                route = closedRoute;
                count = Mathf.Max(1, arcCount);
                cum = new float[route.Count];
                for (int i = 1; i < route.Count; i++) cum[i] = cum[i - 1] + Flat(route[i - 1], route[i]);
                Length = cum[route.Count - 1];
            }

            /// <summary>Point at arc length <paramref name="s"/>; wraps around the closed loop.</summary>
            public float Wrap(float s) => Length > 0f ? ((s % Length) + Length) % Length : 0f;

            /// <summary>Route segment (waypoint index i, from Wi to Wi+1) that holds arc length s, or -1.</summary>
            public int SegmentAt(float s)
            {
                if (s < 0f) return -1;
                s = Wrap(s);
                for (int i = 0; i + 1 < route.Count; i++)
                    if (s <= cum[i + 1]) return i;
                return route.Count - 2;
            }

            public Vector3 PointAt(float s)
            {
                s = Wrap(s);
                for (int i = 0; i + 1 < route.Count; i++)
                    if (s <= cum[i + 1])
                        return Vector3.Lerp(route[i], route[i + 1], Mathf.InverseLerp(cum[i], cum[i + 1], s));
                return route[route.Count - 1];
            }

            /// <summary>Arc length of the single nearest route point; ties break to the lower segment.</summary>
            public float NearestS(Vector2 p)
            {
                float best = float.MaxValue, bestS = 0f;
                for (int i = 0; i + 1 < route.Count; i++)
                {
                    Vector2 a = new Vector2(route[i].x, route[i].z), b = new Vector2(route[i + 1].x, route[i + 1].z), ab = b - a;
                    float u = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-4f));
                    float dist = Vector2.Distance(p, a + ab * u);
                    if (dist < best) { best = dist; bestS = cum[i] + u * ab.magnitude; }
                }
                return bestS;
            }

            /// <summary>Arc holding the single nearest route point.</summary>
            public int ArcOf(Vector2 p) => Mathf.Min(count - 1, Mathf.FloorToInt(NearestS(p) / ArcLength));
        }

        static bool PlaceMembers(System.Random rng, GroupPlan plan, Vector3 anchor, int size, float cohesion, Vector3 spawn)
        {
            plan.members.Clear();
            plan.yaws.Clear();
            // Start well inside the cohesion radius so wander has room before the A3 check bites.
            float spread = cohesion * 0.6f;
            var path = new NavMeshPath();
            for (int i = 0; i < size; i++)
            {
                Vector3? pos = null;
                if (size == 1) pos = anchor;
                for (int attempt = 0; attempt < 50 && pos == null; attempt++)
                {
                    float a = (float)rng.NextDouble() * Mathf.PI * 2f, rr = 1.5f + (float)rng.NextDouble() * (spread - 1.5f);
                    var q = anchor + new Vector3(Mathf.Cos(a) * rr, 0f, Mathf.Sin(a) * rr);
                    if (!NavMesh.SamplePosition(q, out var hit, 1.5f, NavMesh.AllAreas)) continue;
                    if (Flat(hit.position, anchor) > spread || InTestLane(hit.position, spawn)) continue;
                    if (plan.members.Any(m => Flat(m, hit.position) < 1.8f)) continue;
                    if (!NavMesh.CalculatePath(anchor, hit.position, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete) continue;
                    pos = hit.position;
                }
                if (pos == null) return false;
                plan.members.Add(pos.Value);
                plan.yaws.Add((float)rng.NextDouble() * 360f);
            }
            return true;
        }

        // WorldWalkTests' ground: the spawn clearing and the 10 m x 36 m lane ahead of PlayerSpawn.
        public static bool InTestLane(Vector3 p, Vector3 spawn)
        {
            var rel = new Vector2(p.x - spawn.x, p.z - spawn.z);
            if (rel.magnitude <= SpawnClear) return true;
            float yaw = SpawnYaw * Mathf.Deg2Rad;
            var f = new Vector2(Mathf.Sin(yaw), Mathf.Cos(yaw));
            float along = Vector2.Dot(rel, f), side = Mathf.Abs(rel.x * f.y - rel.y * f.x);
            return along > -2f && along < 36f && side < 5f;
        }

        public static float RouteDistance(List<Vector3> route, Vector2 p)
        {
            float best = float.MaxValue;
            for (int i = 0; i < route.Count - 1; i++)
            {
                Vector2 a = new Vector2(route[i].x, route[i].z), b = new Vector2(route[i + 1].x, route[i + 1].z), ab = b - a;
                float u = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-4f));
                best = Mathf.Min(best, Vector2.Distance(p, a + ab * u));
            }
            return best;
        }

        static bool Near(List<Vector2> pts, Vector2 p, float r)
        {
            float r2 = r * r;
            foreach (var q in pts) if ((q - p).sqrMagnitude <= r2) return true;
            return false;
        }

        static float Flat(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));
    }
}
