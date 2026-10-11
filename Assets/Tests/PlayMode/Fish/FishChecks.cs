using System.Collections.Generic;
using UnityEngine;
using WashedAshore.Fish;
using WashedAshore.Gameplay;

namespace WashedAshore.Tests.PlayMode.Fish
{
    /// <summary>
    /// The F4 and F5 checks every fish suite runs while it plays (f-qa review 3-4). F4, every 0.25 s per fish: the
    /// pivot, nose and tail over water (no land, no missing tile), south of the north line, inside its band (the open
    /// band's 50 m edge), body bottom >= bed + clearance, and body top <= moving surface - margin unless in a scripted
    /// surface event (the only exemption). F5, every frame: rendered turn <= the cap, clip rate = the brief's rule from
    /// the rendered displacement speed, spacing between groupmates >= the brief minimum, and no two body boxes overlap.
    /// </summary>
    sealed class FishChecks
    {
        public int samples, eventSamples, frames;
        public readonly List<string> violations = new List<string>();
        public float worstTurn, worstSpacing = float.MaxValue;
        public int rateSamples, rateWithin2Pct;
        public int boxOverlaps, movingSamples, headingMisses;
        public readonly List<float> speeds = new List<float>(), rates = new List<float>();
        readonly Dictionary<long, Vector3> lastPos = new Dictionary<long, Vector3>(), lastFwd = new Dictionary<long, Vector3>();
        readonly Dictionary<long, int> lastFrame = new Dictionary<long, int>();
        float nextSample;

        void Fail(string what)
        {
            if (violations.Count < 200) violations.Add(what);
            else if (violations.Count == 200) violations.Add("...");
        }

        public void Frame(FishPopulation pop, float time)
        {
            var t = pop.Tuning;
            var st = pop.States;
            int n = pop.Count;
            frames++;
            for (int i = 0; i < n; i++)
            {
                var s = st[i];
                float len = FishTestKit.Length(pop, s);
                // Tracking only carries over from the previous frame (a despawned fish that returns starts afresh).
                if (!lastFrame.TryGetValue(s.fishId, out int lf) || lf != frames - 1) { lastFwd.Remove(s.fishId); lastPos.Remove(s.fishId); }
                lastFrame[s.fishId] = frames;
                if (lastFwd.TryGetValue(s.fishId, out var pf)) worstTurn = Mathf.Max(worstTurn, Vector3.Angle(pf, s.forward));
                lastFwd[s.fishId] = s.forward;
                if (lastPos.TryGetValue(s.fishId, out var pp))
                {
                    // Rendered displacement speed (not the sim's own number), so a slide with an idle tail can't hide.
                    Vector3 v = s.position - pp;
                    v.y = 0f;
                    float speed = v.magnitude / FishTestKit.Step;
                    float expected = t.AnimRate(speed, len);
                    if (!s.inSurfaceEvent)
                    {
                        speeds.Add(speed / len);
                        rates.Add(s.animRate);
                        rateSamples++;
                        if (Mathf.Abs(s.animRate - expected) <= 0.02f * Mathf.Max(expected, 1e-3f)) rateWithin2Pct++;
                    }
                    if (speed > t.restSpeed * 2f)
                    {
                        movingSamples++;
                        if (Vector3.Dot(v.normalized, s.forward) < 0.9f) headingMisses++;   // no sideways or backwards slide
                    }
                }
                lastPos[s.fishId] = s.position;
                var bi = Box(pop, s);
                for (int j = i + 1; j < n; j++)
                {
                    var o = st[j];
                    float lo = FishTestKit.Length(pop, o);
                    if (o.group == s.group)
                    {
                        float need = t.species[s.speciesIndex].spacingBodyLengths.x * 0.5f * (len + lo);
                        worstSpacing = Mathf.Min(worstSpacing, Vector3.Distance(s.position, o.position) / need);
                    }
                    if ((s.position - o.position).sqrMagnitude > (len + lo) * (len + lo)) continue;
                    if (bi.Overlaps(Box(pop, o))) boxOverlaps++;
                }
            }
            if (time < nextSample) return;
            nextSample = time + 0.25f;
            for (int i = 0; i < n; i++) Sample(pop, st[i], time);
        }

        void Sample(FishPopulation pop, in FishState s, float time)
        {
            var t = pop.Tuning;
            var sp = t.species[s.speciesIndex];
            var w = pop.Water;
            float len = FishTestKit.Length(pop, s);
            samples++;
            string id = $"t={time:F2} {s.fishId} {sp.name}";
            foreach (var p in new[] { s.position, s.position + s.forward * (0.5f * len), s.position - s.forward * (0.5f * len) })
                if (!TerrainQuery.TryGroundHeight(p, out float g) || g >= w.WaterLevelY) Fail($"{id} body over land/no tile at {p}");
            if (s.position.z > pop.Map.northLineZ) Fail($"{id} north of the line");
            if (FishPlan.BandAt(t, w, s.position.x, s.position.z, out _) < 0) Fail($"{id} outside every band (open edge {t.bands[t.bands.Length - 1].maxShoreDistance} m)");
            if (!TerrainQuery.TryGroundHeight(s.position, out float bed)) return;
            if (s.bodyMinY < bed + sp.bedClearance - 1e-3f) Fail($"{id} below bed+clearance by {bed + sp.bedClearance - s.bodyMinY:F3}");
            if (s.inSurfaceEvent) { eventSamples++; return; }
            float margin = Mathf.Max(sp.surfaceMargin, pop.WaveAmplitude);
            if (s.bodyMaxY > FishTestKit.Surface(s.position) - margin + 1e-3f) Fail($"{id} above surface-margin by {s.bodyMaxY - FishTestKit.Surface(s.position) + margin:F3}");
        }

        static FishBox Box(FishPopulation pop, in FishState s)
        {
            var b = pop.Bodies.bodies[s.variantIndex].clipBounds;
            var q = Quaternion.LookRotation(s.forward, Vector3.up);
            var c = s.position + q * (b.center * s.sizeScale);
            return new FishBox(new Vector2(c.x, c.z), s.forward, b.extents.x * s.sizeScale, b.extents.z * s.sizeScale, new Vector2(s.bodyMinY, s.bodyMaxY));
        }

        public float SpeedCv()
        {
            if (speeds.Count == 0) return 0f;
            double m = 0, v = 0;
            foreach (float x in speeds) m += x;
            m /= speeds.Count;
            foreach (float x in speeds) v += (x - m) * (x - m);
            return m > 0 ? (float)(System.Math.Sqrt(v / speeds.Count) / m) : 0f;
        }

        /// <summary>Share of samples whose fed clip rate is within 2% of the brief's rule at the rendered speed.</summary>
        public float RateRuleShare => rateSamples > 0 ? rateWithin2Pct / (float)rateSamples : 0f;

        public string Json() =>
            $"\"samples\":{samples},\"eventSamples\":{eventSamples},\"frames\":{frames},\"violations\":{violations.Count},\"worstTurnDeg\":{worstTurn:F2}," +
            $"\"rateSpeedCorrelation\":{(speeds.Count > 1 ? FishTestKit.Correlation(speeds, rates) : 0):F3},\"speedCv\":{SpeedCv():F3},\"rateRuleShareWithin2Pct\":{RateRuleShare:F3}," +
            $"\"worstSpacingRatio\":{(worstSpacing == float.MaxValue ? -1 : worstSpacing):F3},\"boxOverlaps\":{boxOverlaps},\"movingSamples\":{movingSamples},\"headingMisses\":{headingMisses}," +
            "\"firstViolations\":[" + string.Join(",", violations.GetRange(0, Mathf.Min(20, violations.Count)).ConvertAll(v => "\"" + v.Replace("\"", "'") + "\"")) + "]";
    }
}
