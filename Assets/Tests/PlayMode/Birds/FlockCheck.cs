using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using WashedAshore.Birds;
using static System.FormattableString;
using static WashedAshore.Tests.Birds.BirdTestKit;

namespace WashedAshore.Tests.Birds
{
    /// <summary>
    /// B5 measurement over every flock bird (brief 3.4 TARGETS, A2). Per sample and bird: height above the terrain
    /// under it inside [floor - 5, ceiling + 10] of the configured band and the absolute 20-90 m; at least crown top +
    /// 5 m (highest crown within 30 m, <see cref="TestCrowns"/>); wing clip against the vertical speed measured from
    /// the bird's own positions over <see cref="RateWindow"/> s (Flapping iff vz > the configured threshold, obeyed
    /// in at least 95% of samples); centre spacing never under 1.5 m and wing meshes never overlapping; each flock
    /// both flaps and glides; glide share 40-75%; head-bone facing along the flight.
    /// </summary>
    class FlockCheck
    {
        public const float RateWindow = 0.2f, CrownSearch = 30f;

        class Track
        {
            public FlockBird bird;
            public Transform head;
            public float radius;
            public readonly List<(float t, Vector3 p)> history = new List<(float, Vector3)>();
            public string lastClip;
            public float clipSince;
        }

        readonly List<Track> tracks;
        readonly BirdTuning tuning;
        readonly TestCrowns crowns;
        readonly Vector2 gate;
        float time;
        public readonly Dictionary<FlockBird, Facing> PerBirdFacing = new Dictionary<FlockBird, Facing>();
        public int CommuteSamples, CommuteMisses, DwellMisses; // non-gating flap-rule diagnostics
        public int Samples, BandOut, BelowCrown, SpacingLow, MeshOverlaps, RuleSamples, RuleOk, GlidingSamples, WingSamples;
        public float MinAgl = float.MaxValue, MaxAgl = float.MinValue, MinSpacing = float.MaxValue, MinCrownClearance = float.MaxValue;
        public string FirstBandIssue, FirstSpacingIssue, FirstWingIssue, FirstCrownIssue;
        readonly HashSet<Flock> sawFlap = new HashSet<Flock>(), sawGlide = new HashSet<Flock>();

        public FlockCheck(IEnumerable<FlockBird> birds, BirdTuning tuning, TestCrowns crowns)
        {
            tracks = birds.Select(b => new Track { bird = b, head = HeadBone(b), radius = BodyRadius(b) }).ToList();
            foreach (var t in tracks) PerBirdFacing[t.bird] = new Facing();
            this.tuning = tuning;
            this.crowns = crowns;
            var s = tuning.sighting;
            var band = tuning.flock.altitudeBand;
            gate = new Vector2(Mathf.Max(band.x - s.altitudeBelow, s.altitudeAbsolute.x), Mathf.Min(band.y + s.altitudeAbove, s.altitudeAbsolute.y));
        }

        public Vector2 Gate => gate;
        public float GlideShare => WingSamples == 0 ? 0f : (float)GlidingSamples / WingSamples;
        public float RuleCompliance => RuleSamples == 0 ? 0f : (float)RuleOk / RuleSamples;

        public void Sample(float window)
        {
            time += window;
            Samples++;
            float threshold = tuning.flock.flapAboveClimbRate;
            foreach (var tr in tracks)
            {
                var b = tr.bird;
                Vector3 p = b.transform.position;
                // Keep the newest position that is at least RateWindow old, so every sample after the first has a rate.
                // (Dropping everything older than RateWindow left a window just under it whenever the sample spacing ran
                // a hair over 0.1 s, and silently skipped most samples.)
                tr.history.Add((time, p));
                while (tr.history.Count > 2 && time - tr.history[1].t >= RateWindow - 1e-4f) tr.history.RemoveAt(0);

                float agl = p.y - TerrainY(p);
                MinAgl = Mathf.Min(MinAgl, agl);
                MaxAgl = Mathf.Max(MaxAgl, agl);
                if (agl < gate.x || agl > gate.y) { BandOut++; FirstBandIssue ??= Invariant($"{b.name} {agl:F1} m AGL at {p}"); }
                float crown = crowns.TopWithin(p, CrownSearch);
                if (!float.IsNegativeInfinity(crown))
                {
                    MinCrownClearance = Mathf.Min(MinCrownClearance, p.y - crown);
                    if (p.y - crown < tuning.sighting.crownMin) { BelowCrown++; FirstCrownIssue ??= Invariant($"{b.name} {p.y - crown:F1} m above crown top {crown:F1}"); }
                }

                var (clip, _) = Dominant(b.Animator);
                WingSamples++;
                if (IsGlide(clip)) { GlidingSamples++; sawGlide.Add(b.Flock); }
                if (IsFlap(clip)) sawFlap.Add(b.Flock);

                var first = tr.history[0];
                float dt = time - first.t;
                if (dt < RateWindow - 1e-4f) continue;
                Vector3 v = (p - first.p) / dt;
                RuleSamples++;
                bool commute = b.Flock && b.Flock.Current == Flock.Mode.Commute;
                if (commute) CommuteSamples++;
                if (clip != tr.lastClip) { tr.lastClip = clip; tr.clipSince = time; }
                bool shouldFlap = v.y > threshold;
                if (shouldFlap == IsFlap(clip) && shouldFlap != IsGlide(clip)) RuleOk++;
                else
                {
                    FirstWingIssue ??= Invariant($"{b.name} vz {v.y:F2} m/s on {clip}");
                    if (commute) CommuteMisses++;
                    // Diagnostics: a miss within the 1 s minimum dwell of the last clip change is the dwell holding the clip.
                    if (time - tr.clipSince < tuning.flock.minDwellSeconds + 0.15f) DwellMisses++;
                }
                if (tr.head) PerBirdFacing[b].Add(ModelForward(tr.head, b.VisualBounds), v);
            }

            for (int i = 0; i < tracks.Count; i++)
            for (int j = i + 1; j < tracks.Count; j++)
            {
                var a = tracks[i];
                var c = tracks[j];
                if (a.bird.Flock != c.bird.Flock) continue;
                float d = Vector3.Distance(a.bird.transform.position, c.bird.transform.position);
                MinSpacing = Mathf.Min(MinSpacing, d);
                if (d < tuning.sighting.minSpacing) { SpacingLow++; FirstSpacingIssue ??= Invariant($"{a.bird.name} / {c.bird.name} {d:F2} m apart"); }
                if (d - a.radius - c.radius < 0f) MeshOverlaps++;
            }
        }

        public List<string> Failures(IEnumerable<Flock> flocks)
        {
            var s = tuning.sighting;
            var f = new List<string>();
            if (BandOut > 0) f.Add($"Outside the {gate.x}-{gate.y} m AGL gate in {BandOut} bird-samples (first: {FirstBandIssue})");
            if (BelowCrown > 0) f.Add($"Below crown top + {s.crownMin} m in {BelowCrown} bird-samples (first: {FirstCrownIssue})");
            if (SpacingLow > 0) f.Add($"Spacing under {s.minSpacing} m in {SpacingLow} pair-samples (first: {FirstSpacingIssue})");
            if (MeshOverlaps > 0) f.Add($"Wing meshes overlapped in {MeshOverlaps} pair-samples");
            if (RuleCompliance < s.flapRuleMin) f.Add(Invariant($"Flap rule obeyed in {RuleCompliance:P1} of {RuleSamples} samples (< {s.flapRuleMin:P0}; first miss: {FirstWingIssue})"));
            if (GlideShare < s.glideShare.x || GlideShare > s.glideShare.y) f.Add(Invariant($"Glide share {GlideShare:P1} outside {s.glideShare.x:P0}-{s.glideShare.y:P0}"));
            foreach (var fl in flocks)
                if (!sawFlap.Contains(fl) || !sawGlide.Contains(fl)) f.Add($"{fl.name} did not alternate (flap {sawFlap.Contains(fl)}, glide {sawGlide.Contains(fl)})");
            foreach (var kv in PerBirdFacing)
            {
                var face = kv.Value.Failure(kv.Key.name);
                if (face != null) f.Add(face);
            }
            return f;
        }

        public override string ToString() =>
            Invariant($"birds={tracks.Count} samples={Samples} agl={MinAgl:F1}-{MaxAgl:F1} gate={gate.x}-{gate.y} out={BandOut} ") +
            Invariant($"crownClear={(MinCrownClearance == float.MaxValue ? float.NaN : MinCrownClearance):F1} belowCrown={BelowCrown} minSpacing={MinSpacing:F2} low={SpacingLow} meshOverlaps={MeshOverlaps} ") +
            Invariant($"flapRule={RuleCompliance:P1} of {RuleSamples} (misses {RuleSamples - RuleOk}: commute {CommuteMisses}/{CommuteSamples} samples, within dwell {DwellMisses}) ") +
            Invariant($"glideShare={GlideShare:P1} facingMin={(PerBirdFacing.Count == 0 ? 1f : PerBirdFacing.Values.Min(x => x.Share)):P0}");
    }
}
