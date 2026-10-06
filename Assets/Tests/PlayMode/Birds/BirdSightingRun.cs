using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using WashedAshore.Birds;

namespace WashedAshore.Tests.Birds
{
    /// <summary>One B6 sample (brief 3.5 per-sample array). Foliage-aware counts gate; the *NoFoliage counts are the
    /// non-gating comparison without the foliage test.</summary>
    [Serializable]
    class BirdSample
    {
        public float t;
        public int robins, robinsNoFoliage;
        public List<int> flockBirds = new List<int>();          // visible flock birds per flock
        public List<int> flockBirdsNoFoliage = new List<int>();
        public int inView;                                       // every bird in frame, <= 200 m, unoccluded
        public int gliding, flying;
        public int flushes;                                      // flushes that fired since the previous sample
        public float minAgl, maxAgl, minCrownClearance, minSpacing;
        // Non-gating diagnostics per flock (flock centroid seen from the eye): horizontal distance (m), elevation and
        // azimuth off the camera's forward (degrees), and why it isn't in view: in, far, azimuth, above, occluded.
        public List<float> flockDist = new List<float>(), flockElev = new List<float>(), flockAzim = new List<float>();
        public List<string> flockWhy = new List<string>();
        public int arc = -1; // walker's route arc (8 equal arcs from W0, 0-based), for the per-arc sky cut
    }

    /// <summary>Non-gating B6 diagnostics: where the flocks were relative to the camera across all flock-samples.</summary>
    [Serializable]
    class SkyDiagnostics
    {
        public string method = "per sample and flock, the flock centroid from the eye; 'in' = >= 2 birds visible (gating rule); " +
                               "else far (> flock range), azimuth (outside the horizontal FOV), above (inside it but over the top edge), occluded";
        public int @in, far, azimuth, above, occluded;
        public List<string> distanceBins = new List<string> { "0-25", "25-50", "50-75", "75-100", "100-150", "150-200", ">200" };
        public List<int> distanceCounts = new List<int> { 0, 0, 0, 0, 0, 0, 0 };
        public List<int> distanceInView = new List<int> { 0, 0, 0, 0, 0, 0, 0 };
        // Per route arc (8 equal arcs from W0): samples, empty-sky samples, and the dominant not-in-view reason while empty.
        public List<int> arcSamples = new List<int>(new int[8]), arcEmpty = new List<int>(new int[8]);
        public List<string> arcEmptyReason = new List<string>(new string[8]);

        public void Add(float dist, string why)
        {
            switch (why) { case "in": @in++; break; case "far": far++; break; case "azimuth": azimuth++; break; case "above": above++; break; default: occluded++; break; }
            float[] edges = { 25f, 50f, 75f, 100f, 150f, 200f };
            int bin = 0;
            while (bin < edges.Length && dist >= edges[bin]) bin++;
            distanceCounts[bin]++;
            if (why == "in") distanceInView[bin]++;
        }

        public void AddArcs(List<BirdSample> samples)
        {
            var reasons = new Dictionary<string, int>[8];
            foreach (var x in samples)
            {
                if (x.arc < 0 || x.arc >= 8) continue;
                arcSamples[x.arc]++;
                if (x.flockBirds.Sum() > 0) continue;
                arcEmpty[x.arc]++;
                reasons[x.arc] ??= new Dictionary<string, int>();
                foreach (var w in x.flockWhy) reasons[x.arc][w] = (reasons[x.arc].TryGetValue(w, out var n) ? n : 0) + 1;
            }
            for (int a = 0; a < 8; a++)
                arcEmptyReason[a] = reasons[a] == null ? "-" : string.Join(" ", reasons[a].OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key}:{kv.Value}"));
        }
    }

    [Serializable]
    class FlushEvent
    {
        public string robin;
        public float t;
        public float distance;   // horizontal player distance, measured by the test when the flush fired
        public bool social;
        public bool seen;        // visible in the flush sample or the next
        public string unseenWhy; // designer-2 diagnosis: why an unseen flush was not seen at its last check
    }

    [Serializable]
    class BirdPass
    {
        public bool flockInView, emptySky, firstFlock, groundMet, flushes, flushSeen, maxInView, busy, glideShare, altitude, crown, fidBand, all;
    }

    /// <summary>One seed's B6 walk and its score (brief 3.4 definitions, 3.5 fields).</summary>
    [Serializable]
    class BirdSightingRun
    {
        public int seed;
        public bool gating;
        public string ranAt = DateTime.Now.ToString("o");
        public string briefRevision;
        public float timeScale, runnerAspect;
        public int samples;
        public float flockInViewPct, longestEmptySkySec, firstFlockSec;
        public int groundMetCount;
        public float groundMetPerMin;
        public int flushEvents;
        public float flushSeenPct;
        public List<float> flushDistances = new List<float>();
        public List<FlushEvent> flushLog = new List<FlushEvent>();
        public int maxBirdsInView;
        public float pctSamplesGe15, glideShare, flapRuleCompliance;
        public float altitudeMinAGL, altitudeMaxAGL, minCrownClearance, minFlockSpacing;
        public int maxFlushesPerRobin;
        public float flockInViewPctNoFoliage, longestEmptySkySecNoFoliage, groundMetPerMinNoFoliage;
        public List<FlockPlan> flocks = new List<FlockPlan>();
        public List<PatchPlan> patches = new List<PatchPlan>();
        public List<string> redraws = new List<string>();
        public string placementError;
        public int stallRecoveries;
        public BirdPass pass = new BirdPass();
        public SkyDiagnostics sky = new SkyDiagnostics();
        public List<BirdSample> perSample = new List<BirdSample>();

        public void Score(BirdTuning tuning, int metCount, int metCountNoFoliage, float ruleCompliance)
        {
            var s = tuning.sighting;
            samples = perSample.Count;
            float minutes = samples * s.sampleInterval / 60f;
            bool FlockIn(List<int> perFlock) => perFlock.Any(n => n >= s.flockInViewMinBirds);
            flockInViewPct = (float)perSample.Count(x => FlockIn(x.flockBirds)) / samples;
            flockInViewPctNoFoliage = (float)perSample.Count(x => FlockIn(x.flockBirdsNoFoliage)) / samples;
            longestEmptySkySec = LongestGap(perSample.Select(x => x.flockBirds.Sum() == 0), s.sampleInterval, out firstFlockSec);
            longestEmptySkySecNoFoliage = LongestGap(perSample.Select(x => x.flockBirdsNoFoliage.Sum() == 0), s.sampleInterval, out _);
            groundMetCount = metCount;
            groundMetPerMin = metCount / minutes;
            groundMetPerMinNoFoliage = metCountNoFoliage / minutes;
            flushEvents = flushLog.Count;
            flushDistances = flushLog.Select(f => f.distance).ToList();
            flushSeenPct = flushEvents == 0 ? 0f : (float)flushLog.Count(f => f.seen) / flushEvents;
            maxBirdsInView = perSample.Max(x => x.inView);
            pctSamplesGe15 = (float)perSample.Count(x => x.inView >= s.busyCount) / samples;
            int flying = perSample.Sum(x => x.flying);
            glideShare = flying == 0 ? 0f : (float)perSample.Sum(x => x.gliding) / flying;
            flapRuleCompliance = ruleCompliance;
            altitudeMinAGL = perSample.Min(x => x.minAgl);
            altitudeMaxAGL = perSample.Max(x => x.maxAgl);
            minCrownClearance = perSample.Min(x => x.minCrownClearance);
            minFlockSpacing = perSample.Min(x => x.minSpacing);

            var band = tuning.flock.altitudeBand;
            float lo = Mathf.Max(band.x - s.altitudeBelow, s.altitudeAbsolute.x), hi = Mathf.Min(band.y + s.altitudeAbove, s.altitudeAbsolute.y);
            float fid = tuning.robin.flightInitiationDistance;
            pass.flockInView = flockInViewPct >= s.flockInView.x && flockInViewPct <= s.flockInView.y;
            pass.emptySky = longestEmptySkySec <= s.maxEmptySkySeconds;
            pass.firstFlock = firstFlockSec <= s.firstFlockMaxSeconds; // R1: gating
            pass.groundMet = groundMetPerMin >= s.groundMetPerMinute.x && groundMetPerMin <= s.groundMetPerMinute.y;
            pass.flushes = flushEvents >= s.flushesPerWalk.x && flushEvents <= s.flushesPerWalk.y;
            pass.flushSeen = flushSeenPct >= s.flushSeenMin;
            pass.maxInView = maxBirdsInView >= s.maxInView.x && maxBirdsInView <= s.maxInView.y;
            pass.busy = pctSamplesGe15 <= s.busyShareMax;
            pass.glideShare = glideShare >= s.glideShare.x && glideShare <= s.glideShare.y;
            pass.altitude = altitudeMinAGL >= lo && altitudeMaxAGL <= hi;
            pass.crown = minCrownClearance >= s.crownMin;
            pass.fidBand = flushLog.Where(f => !f.social).All(f => f.distance >= fid - s.fidBelow && f.distance <= fid + s.fidAbove);
            pass.all = pass.flockInView && pass.emptySky && pass.firstFlock && pass.groundMet && pass.flushes && pass.flushSeen && pass.maxInView
                       && pass.busy && pass.glideShare && pass.altitude && pass.crown && pass.fidBand;
        }

        /// <summary>Longest run of empty samples x interval, leading and trailing runs included; the leading run is the first sighting.</summary>
        static float LongestGap(IEnumerable<bool> empty, float interval, out float leading)
        {
            int gap = 0, longest = 0, lead = -1;
            foreach (bool e in empty)
            {
                if (e) gap++;
                else { if (lead < 0) lead = gap; gap = 0; }
                longest = Mathf.Max(longest, gap);
            }
            leading = (lead < 0 ? gap : lead) * interval;
            return longest * interval;
        }
    }
}
