using System.Collections;
using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using WashedAshore.Fish;
using WashedAshore.Gameplay;
using WashedAshore.World;

namespace WashedAshore.Tests.PlayMode.Fish
{
    /// <summary>
    /// Spec F7 with the brief's sighting definition (fish-targets.json sightings.definition): the scripted shore walk at
    /// 3 seeds (route robertson_mccord, 5 m/s, eye 1.7 m, yaw travel + 35 deg) and the 60 s McCord bluff look. A body
    /// sighting is a fish (or a whole school) the renderer drew, not terrain-occluded from the eye, continuously for
    /// minSightingSec, recounted only after bodyRecountSec unseen; a surface event counts once at spawn when its origin is
    /// in the frustum, within the view radius and unoccluded; a body seen with its own sign counts once. Render stats check
    /// that nothing past the visibility rules was drawn. Writes TestResults/fish-sightings.json.
    /// </summary>
    public class FishSightingTests
    {
        // Every target and definition value comes from the brief of record at test time (FishBrief; f-qa review 6).
        static FishBrief.Sightings Brief => FishBrief.Sighting;
        static float MinSightingSec => Brief.minSightingSec;
        static float RecountSec => Brief.bodyRecountSec;

        static readonly List<string> results = new List<string>();

        /// <summary>Per key (fish id, or a school's group): the current run in view and the last counted sighting.</summary>
        class Track
        {
            public float runStart = -1f;     // start of the current continuous run in view, -1 when out of view
            public bool countedThisRun;
            public float lastCountedEnd = float.NegativeInfinity; // when a counted run left view (or +inf while it's still in view)
        }

        class Counter
        {
            public readonly Dictionary<long, Track> tracks = new Dictionary<long, Track>();
            public readonly Dictionary<long, float> eventAt = new Dictionary<long, float>();
            public readonly List<float> times = new List<float>();
            public readonly Dictionary<long, long> keyOfFish = new Dictionary<long, long>(); // fish id -> sighting key
            public readonly HashSet<long> countedKeys = new HashSet<long>();                // distinct keys counted as bodies
            public Counter perGroup;
            public int signsDrawn, signsUndrawn;
            public int signsInViewPreLegibility, signsIllegible, signsNoLegibilityRow;   // F17 legibility accounting                     // counted signs checked against FishSurfaceFx after its LateUpdate
            public readonly List<float> signsPending = new List<float>();   // count time of each counted sign not yet seen drawn   // information only: every group (loose ones too) as one sighting (f-designer's question)
            public int bodies, events, spontaneous, jumps, flush, drawnPastFloor, v3Bodies, v2Bodies, v1DeepBodies, maxDrawn, frames, drawnBeyondRange;
            public readonly SortedDictionary<int, int> drawnHistogram = new SortedDictionary<int, int>();
            public readonly List<string> eventLog = new List<string>();

            public Track Get(long key)
            {
                if (!tracks.TryGetValue(key, out var tr)) tracks[key] = tr = new Track();
                return tr;
            }
        }

        [TearDown]
        public void TearDown() => FishTestKit.Restore();

        [OneTimeTearDown]
        public void WriteResults() => FishTestKit.Write("fish-sightings.json", "{\"runs\":[" + string.Join(",", results) + "]}");

        [UnityTest]
        public IEnumerator F7_ShoreWalk_HitsSightingAndSurfaceEventBands([ValueSource(nameof(Seeds))] int seed)
        {
            yield return FishTestKit.Load(seed, "f7");
            var pop = FishPopulation.Active;
            var c = new Counter();
            var cam = Camera.main;
            float time = 0f, walked = 0f, length = FishTestKit.RouteLength();
            void OnEvent(FishSurfaceEvent e) => CountEvent(c, e, cam, Brief.surfaceEventViewRadius, time, "shore");
            FishEvents.Surface += OnEvent;
            // Count after the renderer's LateUpdate, so States and DrawModes are the same frame's (f-artist's triage).
            var hook = FishLateHook.Attach(() => CountBodies(c, pop, cam, time));
            try
            {
                for (; walked < length; time += FishTestKit.Step, walked = time * FishTestKit.WalkSpeed)
                {
                    FishTestKit.OnRoute(walked, out var feet, out var look);
                    FishTestKit.Place(feet, look, WaterMode.Dry);
                    yield return null;
                }
            }
            finally { FishEvents.Surface -= OnEvent; Object.Destroy(hook.gameObject); }
            float minutes = time / 60f;
            float sightingsPerMin = (c.bodies + c.events) / minutes, eventsPerMin = c.spontaneous / minutes, jumpsPerMin = c.jumps / minutes;
            var report = Report("shore", seed, pop, c, time, walked, out float longestGap, out int maxIn15);
            results.Add(report);
            c.signsUndrawn += c.signsPending.Count;
            Assert.AreEqual(0, c.signsUndrawn, "F7: a counted surface sign was not drawn (FishSurfaceFx inactive or drew nothing)");
            Assert.AreEqual(0, c.drawnPastFloor, "a fish below the FishMurk floor was drawn");
            Assert.AreEqual(0, c.v3Bodies, "a V3 body was drawn");
            Assert.AreEqual(0, c.v2Bodies, "a V2 fish was drawn as a lit body");
            Assert.AreEqual(0, c.v1DeepBodies, "a V1 body deeper than bodyTierDepth was drawn as a body");
            Assert.AreEqual(0, c.drawnBeyondRange, "a body drawn beyond bodyDrawDistance");
            Assert.LessOrEqual(c.maxDrawn, FishBrief.Visible.maxVisibleBodiesShoreHard, "most bodies drawn at once (hard cap)");
            Assert.GreaterOrEqual(SoftCapShare(c, FishBrief.Visible.maxVisibleBodiesShore), 0.99f, "frames within the soft cap of bodies drawn");
            Assert.That(sightingsPerMin, Is.InRange(Brief.sightingsBand[0], Brief.sightingsBand[1]), "F7 sightings per minute");
            Assert.That(eventsPerMin, Is.InRange(Brief.surfaceEventsBand[0], Brief.surfaceEventsBand[1]), "F7 surface events per minute");
            Assert.LessOrEqual(jumpsPerMin, Brief.jumpsBand[1], "jumps per minute");
            Assert.LessOrEqual(longestGap, Brief.longestGapSec, "longest gap between sightings");
            Assert.LessOrEqual(maxIn15, Brief.maxSightingsIn15s, "most sightings in any 15 s");
        }

        static IEnumerable<string> Bluffs() => new[] { "McCord", "Buzzard" };

        [UnityTest]
        public IEnumerator F7_BluffLook_SurfaceEventsOnly([ValueSource(nameof(Seeds))] int seed, [ValueSource(nameof(Bluffs))] string bluff)
        {
            yield return FishTestKit.Load(seed, $"f7bluff-{bluff.ToLowerInvariant()}");
            var pop = FishPopulation.Active;
            var c = new Counter();
            var cam = Camera.main;
            float time = 0f;
            FishTestKit.BluffPose($"LM_{bluff}Bluff", bluff, cam, out var feet, out var look, out string pose);
            float seconds = Brief.bluffDurationSec;
            Assert.Greater(seconds, 0f, "sightings.bluffDurationSec missing");
            int subSurfaceDrawFrames = 0, near = 0, far = 0;
            int farRaised0 = pop.Events.FarRaised, farRefused0 = pop.Events.FarRefused;
            int admitted0 = pop.World.SignsAdmitted, refused0 = pop.World.SignsRefusedLiveCap;
            void OnEvent(FishSurfaceEvent e)
            {
                int before = c.spontaneous;
                CountEvent(c, e, cam, Brief.bluffViewRadius, time, bluff);
                if (c.spontaneous > before) { if (e.farWater) far++; else near++; }
            }
            FishEvents.Surface += OnEvent;
            var hook = FishLateHook.Attach(() =>
            {
                CountBodies(c, pop, cam, time);
                // N2: from a bluff top, surface signs only. A jumping body in the air is a sign, not a sub-surface draw.
                for (int i = 0; i < pop.Count; i++) if (pop.DrawModes[i] != FishDrawMode.None && !pop.States[i].inSurfaceEvent) { subSurfaceDrawFrames++; break; }
            });
            try
            {
                for (; time < seconds; time += FishTestKit.Step)
                {
                    FishTestKit.Place(feet, look, WaterMode.Dry);
                    yield return null;
                }
            }
            finally { FishEvents.Surface -= OnEvent; Object.Destroy(hook.gameObject); }
            int admitted = pop.World.SignsAdmitted - admitted0, refused = pop.World.SignsRefusedLiveCap - refused0;
            string report = Report($"bluff-{bluff}", seed, pop, c, time, 0f, out _, out _);
            report = report.Substring(0, report.Length - 1) +
                     $",\"pose\":{pose},\"subSurfaceDrawFrames\":{subSurfaceDrawFrames},\"seenNear\":{near},\"seenFar\":{far}," +
                     $"\"farRaised\":{pop.Events.FarRaised - farRaised0},\"farRefused\":{pop.Events.FarRefused - farRefused0}," +
                     $"\"liveCapBindingShare\":{(admitted + refused > 0 ? refused / (float)(admitted + refused) : 0f):F3}," +
                     $"\"capRefusedFar\":{pop.World.FarRefusedLiveCap},\"capRefusedNear\":{pop.World.NearRefusedLiveCap},\"nearRefusedWhileFarHeld\":{pop.World.NearRefusedWhileFarHeld}}}";
            results.Add(report);
            c.signsUndrawn += c.signsPending.Count;
            Assert.AreEqual(0, c.signsUndrawn, "F7: a counted surface sign was not drawn (FishSurfaceFx inactive or drew nothing)");
            Assert.AreEqual(0, subSurfaceDrawFrames, "N2: from the bluff top no sub-surface body or shadow is drawn");
            Assert.That(c.spontaneous / (time / 60f), Is.InRange(Brief.bluffBand[0], Brief.bluffBand[1]), "bluff surface events per minute");
        }

        /// <summary>
        /// f-td: one radius rule, three callers. From the McCord bluff eye (27.7 m up), a sign just inside the bluff radius on
        /// the flat (and so beyond it in 3D) must be counted by the F7 counter (before legibility) and drawn by FishSurfaceFx.
        /// The sign is raised through FishEvents like the sim's own, on a frame when no other sign is drawn.
        /// </summary>
        [UnityTest]
        public IEnumerator RadiusRule_SignJustInsideFromARaisedEye_IsCountedAndDrawn()
        {
            yield return FishTestKit.Load(FishTestKit.Seeds[0], "radius-rule");
            var pop = FishPopulation.Active;
            var cam = Camera.main;
            FishTestKit.BluffPose("LM_McCordBluff", "McCord", cam, out var feet, out var look, out _);
            float radius = Brief.bluffViewRadius;
            Vector3 at = default;
            bool found = false;
            // Batchmode has no WaitForEndOfFrame: read FishSurfaceFx after its LateUpdate through the late hook.
            int drawnNow = -1;
            var hook = FishLateHook.Attach(() => drawnNow = FishSignDraws.Active(out int n) ? n : -1);
            // Wait until no sign is drawn, so the one raised below is the only candidate for the draw.
            for (int k = 0; k < 600 && !found; k++)
            {
                FishTestKit.Place(feet, look, WaterMode.Dry);
                yield return null;
                if (drawnNow != 0) continue;
                Vector3 eye = cam.transform.position, fwd = cam.transform.forward;
                fwd.y = 0f;
                fwd.Normalize();
                var planes = GeometryUtility.CalculateFrustumPlanes(cam);
                for (float r = radius - 0.25f; r > radius - 10f && !found; r -= 0.25f)
                    for (int a = -20; a <= 20 && !found; a += 2)
                    {
                        var dir = Quaternion.Euler(0f, a, 0f) * fwd;
                        var p = eye + dir * r;
                        if (!TerrainQuery.TryGroundHeight(p, out float g) || g >= pop.Water.WaterLevelY) continue;
                        p.y = pop.Water.SurfaceY(p.x, p.z);
                        if (!GeometryUtility.TestPlanesAABB(planes, new Bounds(p, Vector3.one * 0.5f))) continue;
                        if (FishOcclusion.Blocked(eye, p + Vector3.up * 0.05f, out _)) continue;
                        at = p;
                        found = true;
                    }
            }
            Assert.IsTrue(found, "no open-water, in-view, unoccluded point just inside the bluff radius");
            Vector3 eyeNow = cam.transform.position;
            Assert.Greater(Vector3.Distance(eyeNow, at), radius - 10f, "the point is near the radius");
            var e = new FishSurfaceEvent
            {
                kind = FishSurfaceKind.Swirl, position = at, size = 0.5f, heading = Vector3.forward, fishId = -1, species = 0,
                startTime = WaterClock.Now, duration = 3f, spontaneous = true,
            };
            var c = new Counter();
            FishEvents.Raise(e);
            CountEvent(c, e, cam, radius, 0f, "McCord");
            Assert.AreEqual(1, c.signsInViewPreLegibility, $"counted in view (flat {FishEventDistance.Flat(at, eyeNow):F2} m, 3D {Vector3.Distance(at, eyeNow):F2} m)");
            bool drawn = false;
            for (int k = 0; k < 30 && !drawn; k++)
            {
                FishTestKit.Place(feet, look, WaterMode.Dry);
                yield return null;
                drawn = drawnNow >= 1;
            }
            Object.Destroy(hook.gameObject);
            Assert.IsTrue(drawn, "FishSurfaceFx drew the sign counted just inside the radius");
        }

        static IEnumerable<string> F8Starts() => new[] { "shore", "bluff" };

        /// <summary>f-qa #26 review: the temporary F8 spawn lands the player there, facing the water, and the facing holds 1 s.</summary>
        [UnityTest]
        public IEnumerator F8Spawn_FacingHolds([ValueSource(nameof(F8Starts))] string where)
        {
            yield return FishTestKit.Load(FishTestKit.Seeds[0], "f8spawn-" + where);
            // FishTestKit.Load parks the player for scripted walks; this check needs the real controller running.
            FishPopulation.Active.PlayerOverride = null;
            var player = Object.FindAnyObjectByType<PlayerController>();
            player.enabled = true;
            if (player.TryGetComponent<CharacterController>(out var cc)) cc.enabled = true;
            float wanted = float.NaN, actual = float.NaN;
            yield return FishF8Spawn.Spawn(where, (w, a) => { wanted = w; actual = a; });
            Assert.IsFalse(float.IsNaN(actual), "no camera");
            Assert.LessOrEqual(Mathf.Abs(Mathf.DeltaAngle(wanted, actual)), 1f, $"camera yaw {actual:F1} vs wanted {wanted:F1}");
        }

        static float SoftCapShare(Counter c, int cap)
        {
            int within = 0;
            foreach (var kv in c.drawnHistogram) if (kv.Key <= cap) within += kv.Value;
            return c.frames > 0 ? within / (float)c.frames : 1f;
        }

        static IEnumerable<int> Seeds() => FishTestKit.Seeds;

        static IEnumerable<string> OldMask()
        {
            foreach (var kv in FishOcclusion.OldMaskFalseBlocks) yield return $"\"{kv.Key}\":{kv.Value}";
        }

        static IEnumerable<string> Hist(Counter c)
        {
            foreach (var kv in c.drawnHistogram) yield return $"\"{kv.Key}\":{kv.Value}";
        }

        static void CountBodies(Counter c, FishPopulation pop, Camera cam, float time)
        {
            var t = pop.Tuning;
            Vector3 eye = cam.transform.position;
            int drawn = 0;
            var seenNow = new HashSet<long>(); // a test may allocate
            var seenGroups = new HashSet<long>();
            if (c.perGroup == null) c.perGroup = new Counter();
            for (int i = 0; i < pop.Count; i++)
            {
                var s = pop.States[i];
                long key = Key(pop, s, false), groupKey = Key(pop, s, true);
                c.keyOfFish[s.fishId] = key;
                c.perGroup.keyOfFish[s.fishId] = groupKey;
                var mode = pop.DrawModes[i];
                if (mode == FishDrawMode.None) continue;
                drawn++;
                var sp = t.species[s.speciesIndex];
                if (pop.Visibility[i] < t.visibilityFloor && !s.inSurfaceEvent) c.drawnPastFloor++;
                if (mode == FishDrawMode.Body && (sp.tier == FishTier.V3 || sp.neverDrawBody)) c.v3Bodies++;
                if (mode == FishDrawMode.Body && sp.tier == FishTier.V2) c.v2Bodies++;
                if (!s.inSurfaceEvent && Vector3.Distance(s.position, eye) > t.bodyDrawDistance + FishTestKit.Length(pop, s)) c.drawnBeyondRange++;
                if (mode == FishDrawMode.Body && sp.tier == FishTier.V1 && FishTestKit.Surface(s.position) - s.bodyMaxY > t.bodyTierDepth) c.v1DeepBodies++;
                var top = new Vector3(s.position.x, s.bodyMaxY, s.position.z);
                if (FishOcclusion.Blocked(eye, top, out _)) continue;
                seenNow.Add(key);
                seenGroups.Add(groupKey);
            }
            c.maxDrawn = Mathf.Max(c.maxDrawn, drawn);
            c.frames++;
            c.drawnHistogram[drawn] = (c.drawnHistogram.TryGetValue(drawn, out int hn) ? hn : 0) + 1;
            CheckSignsDrawn(c, time);
            Advance(c, seenNow, time);
            Advance(c.perGroup, seenGroups, time);
        }

        /// <summary>
        /// f-td / f-qa: a counted sign must have been drawn. Runs in the late hook (after FishSurfaceFx's LateUpdate): the
        /// pending signs were drawn if the component is active and drew at least one this frame. A sign raised this frame
        /// can start a frame later (FishSurfaceFx skips a sign "not yet started"), so it is undrawn only if no frame in
        /// SignDrawGraceSec after its count drew it.
        /// </summary>
        static void CheckSignsDrawn(Counter c, float time)
        {
            if (c.signsPending.Count == 0) return;
            if (FishSignDraws.Active(out int n) && n >= 1) { c.signsDrawn += c.signsPending.Count; c.signsPending.Clear(); return; }
            int expired = c.signsPending.RemoveAll(t0 => time - t0 > SignDrawGraceSec);
            c.signsUndrawn += expired;
        }

        const float SignDrawGraceSec = 0.25f;

        /// <summary>The definition's run rules over one keying: minSightingSec in view, recount after RecountSec unseen.</summary>
        static void Advance(Counter c, HashSet<long> seenNow, float time)
        {
            foreach (long key in seenNow)
            {
                var tr = c.Get(key);
                if (tr.runStart < 0f) { tr.runStart = time; tr.countedThisRun = false; }
                bool longEnough = time - tr.runStart >= MinSightingSec;
                bool recountOk = tr.runStart - tr.lastCountedEnd >= RecountSec;      // out of view RecountSec since its last sighting
                bool ownSign = c.eventAt.TryGetValue(key, out float ev) && time - ev < RecountSec;
                if (longEnough && !tr.countedThisRun && recountOk)
                {
                    tr.countedThisRun = true;
                    tr.lastCountedEnd = float.PositiveInfinity;
                    if (ownSign) continue;                                           // already counted as its sign
                    c.bodies++;
                    c.countedKeys.Add(key);
                    c.times.Add(time);
                }
            }
            foreach (var kv in c.tracks)
            {
                if (seenNow.Contains(kv.Key) || kv.Value.runStart < 0f) continue;
                if (kv.Value.countedThisRun) kv.Value.lastCountedEnd = time;
                kv.Value.runStart = -1f;
            }
        }

        /// <summary>
        /// The brief's "a fish or school" (graded; f-qa's independent recount agrees): a school is one sighting, any other
        /// fish its own. With <paramref name="perGroup"/>, every group (loose groups too) is one: reported only.
        /// </summary>
        static long Key(FishPopulation pop, FishState s, bool perGroup)
        {
            var sp = pop.Tuning.species[s.speciesIndex];
            bool grouped = sp.schoolUnit || sp.grouping == FishGrouping.School || (perGroup && sp.grouping != FishGrouping.Solitary);
            return grouped ? -1 - (pop.World.GroupCell(s.group) * 4096 + pop.World.GroupPlanIndex(s.group)) : s.fishId;
        }

        static void CountEvent(Counter c, FishSurfaceEvent e, Camera cam, float radius, float time, string eyeKey)
        {
            Vector3 eye = cam.transform.position;
            if (!FishEventDistance.Within(e.position, eye, radius)) return;
            var planes = GeometryUtility.CalculateFrustumPlanes(cam);
            if (!GeometryUtility.TestPlanesAABB(planes, new Bounds(e.position, Vector3.one * 0.5f))) return;
            bool blocked = FishOcclusion.Blocked(eye, e.position + Vector3.up * 0.05f, out string by);
            if (c.eventLog.Count < 400)
                c.eventLog.Add($"{{\"t\":{time:F1},\"kind\":\"{e.kind}\",\"far\":{(e.farWater ? "true" : "false")},\"d\":{FishTestKit.Flat(e.position, eye):F1},\"blockedBy\":\"{by}\"}}");
            if (blocked) return;
            // F17 legibilityRule: a sign counts only within its legible distance for this eye (0 = never), measured
            // flat from the eye like the view radius. Signs past it are reported, not counted.
            c.signsInViewPreLegibility++;
            float legible = FishBrief.LegibleDistance(e.kind, e.bask, eyeKey);
            if (legible < 0f) { c.signsNoLegibilityRow++; return; }
            if (legible <= 0f || !FishEventDistance.Within(e.position, eye, legible)) { c.signsIllegible++; return; }
            if (!e.spontaneous) c.flush++;
            if (e.fishId >= 0)
            {
                // A body (or its group) seen together with its own sign counts once: skip the sign if the body was counted
                // in view now or within RecountSec; otherwise remember it so the body isn't counted again.
                long key = c.keyOfFish.TryGetValue(e.fishId, out long k) ? k : e.fishId;
                c.eventAt[key] = time;
                if (c.perGroup != null) c.perGroup.eventAt[c.perGroup.keyOfFish.TryGetValue(e.fishId, out long gk) ? gk : e.fishId] = time;
                if (c.tracks.TryGetValue(key, out var tr) && tr.countedThisRun && (tr.runStart >= 0f || time - tr.lastCountedEnd < RecountSec)) return;
            }
            c.events++;
            c.signsPending.Add(time);
            if (e.spontaneous) c.spontaneous++;
            if (e.kind == FishSurfaceKind.Jump) c.jumps++;
            c.times.Add(time);
        }

        static string Report(string route, int seed, FishPopulation pop, Counter c, float seconds, float metres, out float longestGapOut, out int maxIn15Out)
        {
            c.times.Sort();
            float longestGap = c.times.Count > 0 ? c.times[0] : seconds, maxIn15 = 0;
            for (int i = 1; i < c.times.Count; i++) longestGap = Mathf.Max(longestGap, c.times[i] - c.times[i - 1]);
            if (c.times.Count > 0) longestGap = Mathf.Max(longestGap, seconds - c.times[c.times.Count - 1]);
            for (int i = 0, j = 0; i < c.times.Count; i++)
            {
                while (c.times[i] - c.times[j] > 15f) j++;
                maxIn15 = Mathf.Max(maxIn15, i - j + 1);
            }
            float min = seconds / 60f;
            longestGapOut = longestGap;
            maxIn15Out = (int)maxIn15;
            var sb = new StringBuilder();
            sb.Append($"{{\"route\":\"{route}\",\"seed\":{seed},\"brief\":\"{pop.Tuning.briefRevision}\",\"briefSha16\":\"{pop.Tuning.briefSha16}\",\"seconds\":{seconds:F1},\"metres\":{metres:F0},");
            sb.Append($"\"bodySightings\":{c.bodies},\"countedSignsDrawn\":{c.signsDrawn},\"signsInViewPreLegibility\":{c.signsInViewPreLegibility},\"signsIllegible\":{c.signsIllegible},\"signsNoLegibilityRow\":{c.signsNoLegibilityRow},\"countedSignsUndrawn\":{c.signsUndrawn},\"bodyDistinct\":{c.countedKeys.Count},\"bodyRecounts\":{c.bodies - c.countedKeys.Count},\"bodySightingsPerGroupInfo\":{(c.perGroup != null ? c.perGroup.bodies : 0)},\"surfaceEvents\":{c.events},\"spontaneousEvents\":{c.spontaneous},\"jumps\":{c.jumps},\"flushSigns\":{c.flush},");
            sb.Append($"\"sightingsPerMin\":{(c.bodies + c.events) / min:F2},\"surfaceEventsPerMin\":{c.spontaneous / min:F2},\"jumpsPerMin\":{c.jumps / min:F2},");
            sb.Append($"\"flushSignsPer100m\":{(metres > 0 ? c.flush / (metres / 100f) : 0):F2},\"longestGapSec\":{longestGap:F1},\"maxSightingsIn15s\":{maxIn15},");
            sb.Append($"\"firstSightingSec\":{(c.times.Count > 0 ? c.times[0] : -1):F1},\"maxDrawnAtOnce\":{c.maxDrawn},\"drawnPastFloor\":{c.drawnPastFloor},\"v3Bodies\":{c.v3Bodies},\"v1DeepBodies\":{c.v1DeepBodies},\"v2Bodies\":{c.v2Bodies},\"drawnBeyondRange\":{c.drawnBeyondRange},");
            sb.Append("\"drawnPerFrameHistogram\":{").Append(string.Join(",", new List<string>(Hist(c)))).Append("},");
            sb.Append("\"occlusionExcludedClasses\":[").Append(string.Join(",", new List<string>(FishOcclusion.Excluded).ConvertAll(x => "\"" + x + "\""))).Append("],");
            sb.Append("\"oldMaskFalseBlocksCumulative\":{").Append(string.Join(",", OldMask())).Append("},");
            sb.Append("\"eventsInRange\":[").Append(string.Join(",", c.eventLog)).Append("]}");
            return sb.ToString();
        }
    }
}
