using System.Collections;
using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using WashedAshore.Fish;
using WashedAshore.Gameplay;

namespace WashedAshore.Tests.PlayMode.Fish
{
    /// <summary>
    /// Information-only run of the C2 setup on the held-out seeds 404/505/606, before the graded 101/202/303 run, with no
    /// tuning in between (team-lead): the shore walk and both bluff looks (McCord, Buzzard) with the brief as shipped
    /// (multiplier, far-water signs, bluff rule). Spontaneous signs seen per minute (frustum, brief radius, terrain march),
    /// near vs far, raised, cap binding and sub-surface draws go to TestResults/fish-calib-info.json. Nothing is asserted.
    /// </summary>
    [Explicit("Information-only run on the held-out seeds; never graded")]
    public class FishInfoRun
    {
        static readonly int[] InfoSeeds = { 404, 505, 606 };

        [TearDown]
        public void TearDown() => FishTestKit.Restore();

        [UnityTest]
        public IEnumerator HeldOutSeeds_ShoreAndBothBluffs()
        {
            var json = new List<string>();
            foreach (int seed in InfoSeeds)
            {
                // Shore walk.
                yield return FishTestKit.Load(seed, "info-shore");
                var pop = FishPopulation.Active;
                var cam = Camera.main;
                int seen = 0, raised = 0;
                float time = 0f, walked = 0f, length = FishTestKit.RouteLength();
                void OnShore(FishSurfaceEvent e)
                {
                    if (!e.spontaneous) return;
                    raised++;
                    if (InView(e, cam, FishBrief.Sighting.surfaceEventViewRadius)) seen++;
                }
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
                json.Add($"{{\"seed\":{seed},\"look\":\"shore\",\"seconds\":{time:F1},\"seenPerMin\":{seen / (time / 60f):F3},\"raisedPerMin\":{raised / (time / 60f):F3}" + "}");

                // Both bluffs.
                foreach (var bluff in new[] { "McCord", "Buzzard" })
                {
                    yield return FishTestKit.Load(seed, $"info-bluff-{bluff.ToLowerInvariant()}");
                    pop = FishPopulation.Active;
                    cam = Camera.main;
                    FishTestKit.BluffPose($"LM_{bluff}Bluff", bluff, cam, out var bf, out var bl, out string pose);
                    float seconds = FishBrief.Sighting.bluffDurationSec;
                    Assert.Greater(seconds, 0f, "sightings.bluffDurationSec missing");
                    int near = 0, far = 0, braised = 0, drawFrames = 0;
                    int adm0 = pop.World.SignsAdmitted, ref0 = pop.World.SignsRefusedLiveCap;
                    void OnBluff(FishSurfaceEvent e)
                    {
                        if (!e.spontaneous) return;
                        braised++;
                        if (!InView(e, cam, FishBrief.Sighting.bluffViewRadius)) return;
                        if (e.farWater) far++; else near++;
                    }
                    FishEvents.Surface += OnBluff;
                    var hook = FishLateHook.Attach(() =>
                    {
                        for (int i = 0; i < pop.Count; i++) if (pop.DrawModes[i] != FishDrawMode.None && !pop.States[i].inSurfaceEvent) { drawFrames++; break; }
                    });
                    float bt = 0f;
                    try
                    {
                        for (; bt < seconds; bt += FishTestKit.Step)
                        {
                            FishTestKit.Place(bf, bl, WaterMode.Dry);
                            yield return null;
                        }
                    }
                    finally { FishEvents.Surface -= OnBluff; Object.Destroy(hook.gameObject); }
                    int adm = pop.World.SignsAdmitted - adm0, refused = pop.World.SignsRefusedLiveCap - ref0;
                    json.Add($"{{\"seed\":{seed},\"look\":\"bluff-{bluff}\",\"seconds\":{bt:F1},\"pose\":{pose},\"seenPerMin\":{(near + far) / (bt / 60f):F3}," +
                             $"\"seenNearPerMin\":{near / (bt / 60f):F3},\"seenFarPerMin\":{far / (bt / 60f):F3},\"raisedPerMin\":{braised / (bt / 60f):F3}," +
                             $"\"subSurfaceDrawFrames\":{drawFrames},\"liveCapBindingShare\":{(adm + refused > 0 ? refused / (float)(adm + refused) : 0f):F3}" + "}");
                }
            }
            FishTestKit.Write("fish-calib-info.json",
                $"{{\"brief\":\"{FishBrief.Revision}\",\"briefSha16\":\"{FishBrief.Sha16}\",\"informationOnly\":true,\"runs\":[{string.Join(",", json)}]}}");
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
