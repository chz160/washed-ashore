using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using WashedAshore.Fish;
using WashedAshore.Gameplay;
using WashedAshore.World;

namespace WashedAshore.Tests.PlayMode.Fish
{
    /// <summary>
    /// Calibration of the band-rate surface signs' one global multiplier (f-designer's rule; calibration guard): the
    /// shore walk and the bluff look at seeds 404/505/606, never the graded 101/202/303, with the multiplier forced to 1.
    /// Spontaneous signs in view (frustum, within the brief's view radius, unoccluded) per minute are measured, and the
    /// multiplier that puts the 3-seed shore median at the brief's spontaneousSurfaceEventsPerMin is reported, with
    /// where the bluff would land, to TestResults/fish-calib-events.json. Explicit: run by name, not in the suite.
    /// </summary>
    [Explicit("Calibration run on non-graded seeds; f-designer records the result before the graded run")]
    public class FishSignCalibration
    {
        static readonly int[] CalibrationSeeds = { 404, 505, 606 };

        float shipped = float.NaN;

        [TearDown]
        public void TearDown()
        {
            // The tuning is an asset: put the shipped multiplier back so play mode leaves nothing changed in memory.
            if (FishPopulation.Active && !float.IsNaN(shipped)) FishPopulation.Active.Tuning.surfaceEventMultiplier = shipped;
            FishTestKit.Restore();
        }

        [UnityTest]
        public IEnumerator CalibrateBandRateMultiplier()
        {
            var shore = new List<float>();
            var bluff = new List<float>();
            var raisedShore = new List<float>();
            var capShare = new List<float>();
            var jumpRefused = new List<float>();
            foreach (int seed in CalibrationSeeds)
            {
                yield return FishTestKit.Load(seed, "calib");
                var pop = FishPopulation.Active;
                if (float.IsNaN(shipped)) shipped = pop.Tuning.surfaceEventMultiplier;
                pop.Tuning.surfaceEventMultiplier = 1f;   // runtime only; restored in TearDown and never saved
                var cam = Camera.main;
                int seen = 0, raised = 0;
                float time = 0f, walked = 0f, length = FishTestKit.RouteLength();
                var radius = FishBrief.Sighting.surfaceEventViewRadius;
                void OnShore(FishSurfaceEvent e) { if (!e.spontaneous) return; raised++; if (InView(e, cam, radius)) seen++; }
                FishEvents.Surface += OnShore;
                try
                {
                    for (; walked < length; time += FishTestKit.Step, walked = time * FishTestKit.WalkSpeed)
                    {
                        FishTestKit.OnRoute(walked, out var feet, out var look);
                        FishTestKit.Place(feet, look, WaterMode.Dry);
                        yield return null;
                    }
                }
                finally { FishEvents.Surface -= OnShore; }
                shore.Add(seen / (time / 60f));
                raisedShore.Add(raised / (time / 60f));
                int adm = pop.World.SignsAdmitted, refused = pop.World.SignsRefusedLiveCap;
                capShare.Add(adm + refused > 0 ? refused / (float)(adm + refused) : 0f);
                jumpRefused.Add(pop.World.JumpsRefusedCap);

                yield return FishTestKit.Load(seed, "calib-bluff");
                pop = FishPopulation.Active;
                pop.Tuning.surfaceEventMultiplier = 1f;
                cam = Camera.main;
                FishTestKit.BluffPose("LM_McCordBluff", "McCord", cam, out var bf, out var bl, out _);
                int bseen = 0;
                float bt = 0f;
                var bradius = FishBrief.Sighting.bluffViewRadius;
                void OnBluff(FishSurfaceEvent e) { if (e.spontaneous && InView(e, cam, bradius)) bseen++; }
                FishEvents.Surface += OnBluff;
                try
                {
                    for (; bt < FishBrief.Sighting.bluffDurationSec; bt += FishTestKit.Step)
                    {
                        FishTestKit.Place(bf, bl, WaterMode.Dry);
                        yield return null;
                    }
                }
                finally { FishEvents.Surface -= OnBluff; }
                bluff.Add(bseen / (bt / 60f));
            }
            var sorted = new List<float>(shore);
            sorted.Sort();
            float median = sorted[sorted.Count / 2], target = FishBrief.Sighting.spontaneousSurfaceEventsPerMin;
            float multiplier = median > 0f ? target / median : -1f;
            string List(List<float> v) => "[" + string.Join(",", v.ConvertAll(x => x.ToString("F3", System.Globalization.CultureInfo.InvariantCulture))) + "]";
            FishTestKit.Write("fish-calib-events.json",
                $"{{\"seeds\":[{string.Join(",", CalibrationSeeds)}],\"multiplierUsed\":1,\"shoreSeenPerMin\":{List(shore)},\"shoreRaisedPerMin\":{List(raisedShore)}," +
                $"\"bluffSeenPerMin\":{List(bluff)},\"shoreMedian\":{median:F3},\"target\":{target},\"recommendedMultiplier\":{multiplier:F3}," +
                $"\"bluffAtRecommended\":{List(bluff.ConvertAll(b => b * multiplier))},\"bluffBand\":{List(new List<float>(FishBrief.Sighting.bluffBand))}," +
                $"\"shoreLiveCapRefusedShare\":{List(capShare)},\"shoreJumpsRefusedByCap\":{List(jumpRefused)},\"brief\":\"{FishBrief.Revision}\",\"briefSha16\":\"{FishBrief.Sha16}\"}}");
            Assert.Greater(median, 0f, "no spontaneous signs in view at multiplier 1; check the band rates");
        }

        static bool InView(FishSurfaceEvent e, Camera cam, float radius)
        {
            Vector3 eye = cam.transform.position;
            if (FishTestKit.Flat(e.position, eye) > radius) return false;
            if (!GeometryUtility.TestPlanesAABB(GeometryUtility.CalculateFrustumPlanes(cam), new Bounds(e.position, Vector3.one * 0.5f))) return false;
            return !FishOcclusion.Blocked(eye, e.position + Vector3.up * 0.05f, out _);
        }
    }
}
