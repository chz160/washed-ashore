using System.Collections.Generic;
using UnityEngine;

namespace WashedAshore.Birds
{
    /// <summary>
    /// Plans a robin's flush flight (B4; brief 3.3) as a polyline the robin follows exactly: lift-off, a climb at the
    /// tuning's angle to 4-10 m above the ground, cruise, descent, flutter-down. Up to 8 headings away from the player
    /// (+/-40 degrees jitter) are tried, each with a landing spot 25-40 m out on robin habitat (grass, slope, tree and
    /// rock rules of brief 3.2) at least 12 m from the route. A path is accepted only if every airborne segment's
    /// capsule cast (r 0.3 m) is clear of colliders, it stays out of every crown, and it stays above the ground.
    /// With no clear heading the robin gets a fly-away path and despawns out of view.
    /// </summary>
    public static class RobinFlightPlanner
    {
        const float Step = 1f;
        static readonly float[] Offsets = { 0f, 30f, -30f, 60f, -60f, 90f, -90f, 120f };

        /// <summary>
        /// Hits one flight-segment cast can hold (#44). A cast that fills the buffer is treated as BLOCKED (see
        /// <see cref="Blocks"/>), so any size of 2 or more gives the same flight results as the old SphereCastAll and a robin
        /// never flies through something because a hit was dropped; 64 (about 3 KB) is margin, so the fail-closed branch is
        /// rare. <see cref="MaxCastHits"/> records the largest real cast, and PhysicsAllocTests logs it.
        /// </summary>
        public const int CastBufferSize = 64;
        static readonly RaycastHit[] castHits = new RaycastHit[CastBufferSize];

        /// <summary>How many casts filled the buffer and so blocked their heading (a test or a log can read it).</summary>
        public static int FullBufferHits { get; private set; }

        /// <summary>The largest hit count any cast has returned so far (supporting only; safety rests on full = blocked).</summary>
        public static int MaxCastHits { get; private set; }

        /// <summary>How many casts have run, so a test can show the flush path really cast (MaxCastHits of 0 alone cannot).</summary>
        public static int CastCount { get; private set; }

        /// <summary>The solid world, the terrain's layer and the player, never Water or the invisible WorldBounds fences (water W4; #43 C2).</summary>
        public static int ObstacleMask(Terrain terrain) => WashedAshore.World.WorldLayers.RobinObstacleMask(terrain);

        /// <summary>Robin habitat for a landing or a hop: inside the map, grass, gentle, no rock, no collider, and A4.</summary>
        public static bool GroundOk(BirdPlacementRules.Site site, BirdTerrain ground, RobinTuning t, Vector3 p, int mask, float minFromRoute)
        {
            if (!ground.Inside(p, 5f) || site.Slope(p) > t.maxSlope || site.Grass(p) < t.minGrass) return false;
            if (site.RouteDistance(p) < minFromRoute || site.RockNear(p, t.landRockClear)) return false;
            if (ground.InCrown(p + Vector3.up * t.bodyCenterHeight, t.bodyRadius)) return false;
            float r = 0.3f;
            return !Physics.CheckCapsule(p + Vector3.up * (r + 0.15f), p + Vector3.up * 1.5f, r, mask, QueryTriggerInteraction.Ignore);
        }

        public static bool LandingOk(BirdPlacementRules.Site site, BirdTerrain ground, RobinTuning t, Vector3 p, int mask)
        {
            if (!GroundOk(site, ground, t, p, mask, t.landMinFromRoute)) return false;
            float tree = site.NearestTree(p, t.landTreeDistance.y);
            return tree >= t.landTreeDistance.x && !float.IsInfinity(tree);
        }

        /// <summary>Returns true with a landing path, or false with a fly-away path.</summary>
        public static bool Plan(BirdPlacementRules.Site site, BirdTerrain ground, RobinTuning t, System.Random rng,
            Vector3 start, Vector3 threat, Transform ignore, int mask, List<Vector3> path)
        {
            Vector3 away = start - threat;
            away.y = 0f;
            away = away.sqrMagnitude > 0.01f ? away.normalized : Vector3.forward;
            float jitter = rng.Range(-t.headingJitterDegrees, t.headingJitterDegrees);
            float threatD = Flat(start, threat);

            for (int h = 0; h < Mathf.Min(t.headings, Offsets.Length); h++)
            {
                Vector3 dir = Quaternion.Euler(0f, jitter + Offsets[h], 0f) * away;
                float drawn = rng.Range(t.landDistance); // drawn once per heading, as before: the random sequence is unchanged
                for (int k = 0; k < 3; k++)
                {
                    float d = k == 0 ? drawn : k == 1 ? t.landDistance.y : t.landDistance.x;
                    Vector3 land = ground.OnGround(start + dir * d);
                    if (Flat(land, threat) <= threatD || !LandingOk(site, ground, t, land, mask)) continue;
                    if (TryPath(ground, t, rng, start, land, true, ignore, mask, path)) return true;
                }
            }
            FlyAway(ground, t, start, away, path);
            return false;
        }

        static void FlyAway(BirdTerrain ground, RobinTuning t, Vector3 start, Vector3 dir, List<Vector3> path)
        {
            path.Clear();
            path.Add(start);
            path.Add(start + Vector3.up * t.flutterRise);
            Vector3 far = start + dir * 30f;
            float y = Mathf.Max(CruiseFloor(ground, t, start, far) + 2f, ground.Height(start) + t.climbHeight.y);
            path.Add(new Vector3(start.x, y, start.z) + dir * 2f);
            path.Add(new Vector3(far.x, y, far.z));
        }

        /// <summary>Extends a fly-away path by <paramref name="length"/> m, clear of the ground and crowns.</summary>
        public static Vector3 Extend(BirdTerrain ground, RobinTuning t, Vector3 from, Vector3 dir, float length)
        {
            Vector3 to = from + dir * length;
            float y = Mathf.Max(from.y, CruiseFloor(ground, t, from, to) + 2f);
            return new Vector3(to.x, y, to.z);
        }

        static bool TryPath(BirdTerrain ground, RobinTuning t, System.Random rng, Vector3 start, Vector3 end,
            bool land, Transform ignore, int mask, List<Vector3> path)
        {
            Vector3 flat = end - start;
            flat.y = 0f;
            float dist = flat.magnitude;
            Vector3 dir = flat / Mathf.Max(dist, 0.01f);
            float tan = Mathf.Tan(t.climbAngleDegrees * Mathf.Deg2Rad);
            Vector3 lift = start + Vector3.up * t.flutterRise + dir * 0.3f;
            float groundTop = float.NegativeInfinity;
            int n = Mathf.CeilToInt(dist / Step);
            for (int i = 0; i <= n; i++) groundTop = Mathf.Max(groundTop, ground.Height(Vector3.Lerp(start, end, (float)i / Mathf.Max(n, 1))));

            // Brief 3.3: climb to 4-10 m above the ground; try the drawn height, then the higher ones.
            for (float climb = rng.Range(t.climbHeight); climb <= t.climbHeight.y + 0.01f; climb += 2f)
            {
                float y = Mathf.Max(groundTop, start.y) + climb;
                float climbRun = Mathf.Min((y - lift.y) / tan, dist * 0.45f);
                path.Clear();
                path.Add(start);
                path.Add(lift);
                Vector3 a = start + dir * climbRun;
                path.Add(new Vector3(a.x, y, a.z));
                Vector3 pre = end - dir * t.landFlutterDistance + Vector3.up * t.landFlutterDistance;
                float descRun = Mathf.Min((y - pre.y) / tan, dist * 0.45f);
                Vector3 b = end - dir * descRun;
                path.Add(new Vector3(b.x, y, b.z));
                path.Add(pre);
                path.Add(end);
                if (Clear(ground, t, path, ignore, mask)) return true;
            }
            path.Clear();
            return false;
        }

        /// <summary>Highest ground or crown top on the straight line.</summary>
        static float CruiseFloor(BirdTerrain ground, RobinTuning t, Vector3 a, Vector3 b)
        {
            float top = float.NegativeInfinity;
            int n = Mathf.CeilToInt(Flat(a, b) / Step);
            for (int i = 0; i <= n; i++)
                top = Mathf.Max(top, ground.CanopyTop(Vector3.Lerp(a, b, (float)i / Mathf.Max(n, 1)), t.pathRadius));
            return top;
        }

        static bool Clear(BirdTerrain ground, RobinTuning t, List<Vector3> path, Transform ignore, int mask)
        {
            Vector3 up = Vector3.up * t.bodyCenterHeight;
            for (int i = 1; i < path.Count; i++)
            {
                // Lift-off and touch-down start or end on the ground, so only the body itself must stay clear there.
                bool nearGround = i == 1 || i == path.Count - 1;
                float r = nearGround ? t.bodyRadius : t.pathRadius;
                Vector3 p = path[i - 1] + up, q = path[i] + up;
                Vector3 d = q - p;
                float len = d.magnitude;
                if (len < 0.001f) continue;
                if (CastBlocked(p, r, d / len, len, mask, ignore)) return false;
                int n = Mathf.CeilToInt(len / Step);
                for (int k = 0; k <= n; k++)
                {
                    Vector3 s = Vector3.Lerp(p, q, (float)k / Mathf.Max(n, 1));
                    if (ground.InCrown(s, r)) return false;
                    if (!nearGround && s.y - r < ground.Height(s)) return false;
                }
            }
            return true;
        }

        /// <summary>True if the sphere cast hits anything outside <paramref name="ignore"/>, or fills the buffer.</summary>
        public static bool CastBlocked(Vector3 origin, float radius, Vector3 direction, float length, int mask, Transform ignore)
        {
            int n = Physics.SphereCastNonAlloc(origin, radius, direction, castHits, length, mask, QueryTriggerInteraction.Ignore);
            CastCount++;
            if (n > MaxCastHits) MaxCastHits = n;
            return Blocks(castHits, n, ignore);
        }

        /// <summary>
        /// The blocking rule for <paramref name="count"/> hits in <paramref name="hits"/>: a full buffer may hide a
        /// blocker, so it blocks (counted in <see cref="FullBufferHits"/>); otherwise any hit outside
        /// <paramref name="ignore"/> blocks, as the old SphereCastAll loop did.
        /// </summary>
        public static bool Blocks(RaycastHit[] hits, int count, Transform ignore)
        {
            if (count >= hits.Length)
            {
                FullBufferHits++;
                return true;
            }
            for (int i = 0; i < count; i++)
                if (!ignore || !hits[i].collider.transform.IsChildOf(ignore)) return true;
            return false;
        }

        public static float Flat(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));
    }
}
