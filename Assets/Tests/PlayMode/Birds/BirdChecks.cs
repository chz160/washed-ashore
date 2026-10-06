using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using WashedAshore.Birds;
using static System.FormattableString;
using static WashedAshore.Tests.Birds.BirdTestKit;

namespace WashedAshore.Tests.Birds
{
    /// <summary>
    /// B3 measurement: what each robin's Animator is actually playing, and where it stands. Every robin must play at
    /// least <see cref="MinDistinct"/> distinct clips; no two neighbours (within the tuning's neighbour radius at the
    /// time of the sample) may play the same clip within <see cref="PhaseTolerance"/> of the same normalised time for
    /// <see cref="MaxLockstepSeconds"/> or longer; and no robin on the ground may stand within the A4 keep-out of the
    /// route. Failures() lists every violation; the positive controls assert that it is not empty.
    /// </summary>
    class GroundCycleCheck
    {
        public const int MinDistinct = 3;
        public const float PhaseTolerance = 0.05f, MaxLockstepSeconds = 0.5f;

        readonly List<RobinAgent> robins;
        readonly List<Vector3> route;
        readonly float neighbourRadius, offTrail;
        public readonly Dictionary<RobinAgent, HashSet<string>> Clips = new Dictionary<RobinAgent, HashSet<string>>();
        readonly Dictionary<(int, int), float> run = new Dictionary<(int, int), float>();
        public readonly Dictionary<(int, int), float> MaxRun = new Dictionary<(int, int), float>();
        public readonly Dictionary<RobinAgent, float> MinRouteDistance = new Dictionary<RobinAgent, float>();
        public int Samples, NeighbourPairSamples, CoincidentSamples;

        public GroundCycleCheck(IEnumerable<RobinAgent> robins, float neighbourRadius, List<Vector3> route, float offTrail)
        {
            this.robins = robins.ToList();
            this.neighbourRadius = neighbourRadius;
            this.route = route;
            this.offTrail = offTrail;
            foreach (var r in this.robins) { Clips[r] = new HashSet<string>(); MinRouteDistance[r] = float.MaxValue; }
        }

        public void Sample(float window)
        {
            Samples++;
            var now = robins.Select(r => (r, d: Dominant(r.Animator))).ToList();
            foreach (var (r, d) in now)
            {
                if (r.IsFlying || r.Despawned) continue;
                if (d.clip != null) Clips[r].Add(d.clip);
                MinRouteDistance[r] = Mathf.Min(MinRouteDistance[r], RouteDistance(route, r.transform.position));
            }
            for (int i = 0; i < now.Count; i++)
            for (int j = i + 1; j < now.Count; j++)
            {
                var a = now[i];
                var b = now[j];
                var key = (i, j);
                if ((a.r.transform.position - b.r.transform.position).sqrMagnitude > neighbourRadius * neighbourRadius) { run[key] = 0f; continue; }
                NeighbourPairSamples++;
                float dp = Mathf.Abs(a.d.phase - b.d.phase);
                bool together = a.d.clip != null && a.d.clip == b.d.clip && Mathf.Min(dp, 1f - dp) < PhaseTolerance;
                if (together) CoincidentSamples++;
                run[key] = together ? (run.TryGetValue(key, out var x) ? x : 0f) + window : 0f;
                MaxRun[key] = Mathf.Max(MaxRun.TryGetValue(key, out var m) ? m : 0f, run[key]);
            }
        }

        public List<string> Failures()
        {
            var f = new List<string>();
            foreach (var kv in Clips)
                if (kv.Value.Count < MinDistinct)
                    f.Add($"{kv.Key.name} played {kv.Value.Count} distinct clips ({string.Join(",", kv.Value)})");
            foreach (var kv in MaxRun)
                if (kv.Value >= MaxLockstepSeconds - 1e-4f)
                    f.Add($"{robins[kv.Key.Item1].name} and {robins[kv.Key.Item2].name} in lockstep for {kv.Value:F1} s");
            foreach (var kv in MinRouteDistance)
                if (kv.Value < offTrail)
                    f.Add($"{kv.Key.name} stood {kv.Value:F2} m from the route (A4 keep-out {offTrail} m)");
            return f;
        }

        public override string ToString() =>
            Invariant($"robins={robins.Count} samples={Samples} neighbourPairSamples={NeighbourPairSamples} coincidentSamples={CoincidentSamples} ") +
            Invariant($"maxLockstep={(MaxRun.Count == 0 ? 0f : MaxRun.Values.Max()):F1}s minDistinct={(Clips.Count == 0 ? 0 : Clips.Values.Min(c => c.Count))} ") +
            Invariant($"minRouteDistance={(MinRouteDistance.Count == 0 ? 0f : MinRouteDistance.Values.Min()):F2}m");
    }

    /// <summary>
    /// B4 measurement for one robin, sampled every frame from before its flush until it lands or despawns: the player
    /// distance when it flushed (measured here, not read from the robin), player distance after, height gained,
    /// collider overlaps (Physics.OverlapSphere of its body), terrain penetration, crown intrusion
    /// (<see cref="TestCrowns"/>), landing distance and landing distance from the route, and head-bone facing.
    /// </summary>
    class FlightProbe
    {
        public const float MinGain = 2f, MinGrowth = 10f, MinLand = 20f;

        readonly RobinAgent r;
        readonly Transform head;
        readonly float bodyRadius;
        readonly Collider[] hits = new Collider[8];
        public readonly Facing Facing = new Facing();
        public bool Flushed, Social, Landed, Despawned, InViewAtDespawn;
        public float TriggerDistance = -1f, StartY, MaxY, LandDistance = -1f, LandRouteDistance = -1f;
        public float DistanceAtFlush, DistanceAt2s = -1f, DistanceAtEnd, SinceFlush;
        public int Overlaps, TerrainHits, CrownHits, FlightFrames;
        public string FirstOverlap;
        bool lastInView;

        public string Name => r.name;
        public bool Done => Landed || Despawned;

        public FlightProbe(RobinAgent robin)
        {
            r = robin;
            head = HeadBone(robin);
            bodyRadius = robin.Tuning.robin.bodyRadius;
        }

        public void Sample(float dt, Transform player, Camera cam, int mask, TestCrowns crowns, List<Vector3> route)
        {
            if (Done) return;
            float d = WashedAshore.Tests.Wildlife.WildlifeTestKit.Flat(player.position, r.transform.position);
            if (!Flushed)
            {
                if (!r.IsFlying) return;
                Flushed = true;
                Social = r.FlushedSocially;
                TriggerDistance = WashedAshore.Tests.Wildlife.WildlifeTestKit.Flat(player.position, r.FlushOrigin);
                DistanceAtFlush = d;
                StartY = MaxY = r.transform.position.y;
            }
            SinceFlush += dt;
            if (DistanceAt2s < 0f && SinceFlush >= 2f) DistanceAt2s = d;
            if (r.Despawned)
            {
                Despawned = true;
                InViewAtDespawn = lastInView;
                return;
            }
            DistanceAtEnd = d;
            MaxY = Mathf.Max(MaxY, r.transform.position.y);
            lastInView = cam && GeometryUtility.TestPlanesAABB(GeometryUtility.CalculateFrustumPlanes(cam), r.VisualBounds);
            if (!r.IsFlying)
            {
                Landed = true;
                LandDistance = WashedAshore.Tests.Wildlife.WildlifeTestKit.Flat(r.FlushOrigin, r.transform.position);
                LandRouteDistance = RouteDistance(route, r.transform.position);
                return;
            }
            FlightFrames++;
            Vector3 body = r.BodyCenter;
            int n = Physics.OverlapSphereNonAlloc(body, bodyRadius, hits, mask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                if (hits[i].transform.IsChildOf(player)) continue;
                Overlaps++;
                FirstOverlap ??= $"{hits[i].name} at {body}";
            }
            if (body.y - bodyRadius < TerrainY(body) - 0.02f) TerrainHits++;
            if (crowns.Inside(body, bodyRadius)) CrownHits++;
            if (head && r.Current == RobinAgent.State.Fly) Facing.Add(ModelForward(head, r.VisualBounds), r.Velocity);
        }

        /// <param name="fid">Configured FID; A1 band is fid - below .. fid + above, player-triggered flushes only.</param>
        /// <param name="landFromRoute">A landing must be at least this far from the route (brief 3.3: 12 m).</param>
        /// <param name="scale">Multiplies the growth, height and landing minimums; only the positive control raises it.</param>
        public List<string> Failures(float fid, float below, float above, float landFromRoute, float scale = 1f)
        {
            float MinGrowth = FlightProbe.MinGrowth * scale, MinGain = FlightProbe.MinGain * scale, MinLand = FlightProbe.MinLand * scale;
            landFromRoute *= scale;
            var f = new List<string>();
            if (!Flushed) { f.Add($"{Name} never flushed"); return f; }
            if (!Social && (TriggerDistance > fid + above || TriggerDistance < fid - below))
                f.Add(Invariant($"{Name} flushed at {TriggerDistance:F2} m (A1 band {fid - below}-{fid + above} m)"));
            if (DistanceAt2s >= 0f && DistanceAt2s <= DistanceAtFlush)
                f.Add(Invariant($"{Name}: player distance did not grow within 2 s ({DistanceAtFlush:F1} -> {DistanceAt2s:F1} m)"));
            if (!Despawned && DistanceAtEnd < DistanceAtFlush + MinGrowth)
                f.Add(Invariant($"{Name}: player distance grew only {DistanceAtEnd - DistanceAtFlush:F1} m"));
            if (MaxY - StartY < MinGain) f.Add(Invariant($"{Name} gained only {MaxY - StartY:F1} m of height"));
            if (Overlaps > 0) f.Add($"{Name} overlapped colliders in {Overlaps} frames (first {FirstOverlap})");
            if (TerrainHits > 0) f.Add($"{Name} was inside the terrain in {TerrainHits} frames");
            if (CrownHits > 0) f.Add($"{Name} flew through a tree crown in {CrownHits} frames");
            if (!Done) f.Add($"{Name} neither landed nor despawned");
            if (Landed && LandDistance < MinLand) f.Add(Invariant($"{Name} landed {LandDistance:F1} m away (< {MinLand} m)"));
            if (Landed && LandRouteDistance < landFromRoute) f.Add(Invariant($"{Name} landed {LandRouteDistance:F1} m from the route (< {landFromRoute} m)"));
            if (Despawned && InViewAtDespawn) f.Add($"{Name} despawned in view");
            var face = Facing.Failure(Name);
            if (face != null) f.Add(face);
            return f;
        }

        public override string ToString() =>
            Invariant($"{Name}: flushed={Flushed} social={Social} trigger={TriggerDistance:F2} dFlush={DistanceAtFlush:F1} d2s={DistanceAt2s:F1} dEnd={DistanceAtEnd:F1} ") +
            Invariant($"gain={MaxY - StartY:F1} landed={Landed} land={LandDistance:F1} landRoute={LandRouteDistance:F1} despawned={Despawned} frames={FlightFrames} ") +
            $"overlaps={Overlaps} terrain={TerrainHits} crown={CrownHits} {Facing}";
    }
}
