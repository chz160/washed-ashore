using UnityEngine;
using UnityEngine.Rendering;
using WashedAshore.World;

namespace WashedAshore.Fish.Rendering
{
    /// <summary>
    /// Draws the sim's surface signs (FishEvents.Surface; the sim decides when, this decides how they look): soft, irregular,
    /// expanding and fading rings, V wakes and nervous-water patches on flat quads over the water (FishRing.shader;
    /// ruling/fish-roster-look L4). One RenderMeshInstanced call for all signs within FishTuning.eventRadius. Ages come from
    /// WaterClock, the clock the sim stamps events with. A wake follows its fish while the fish is live. Visual only.
    /// </summary>
    [DefaultExecutionOrder(1001)]
    public sealed class FishSurfaceFx : MonoBehaviour
    {
        public FishRenderSet set;

        const int MaxSigns = 32;
        static readonly int RadiusId = Shader.PropertyToID("_Radius"), StrengthId = Shader.PropertyToID("_Strength"), SeedId = Shader.PropertyToID("_Seed");
        static readonly int ModeId = Shader.PropertyToID("_Mode"), CountId = Shader.PropertyToID("_RingCount");

        readonly FishSurfaceEvent[] signs = new FishSurfaceEvent[MaxSigns];
        int signCount;
        readonly Matrix4x4[] matrices = new Matrix4x4[MaxSigns];
        readonly float[] radius = new float[MaxSigns], strength = new float[MaxSigns], seed = new float[MaxSigns], mode = new float[MaxSigns], rings = new float[MaxSigns];
        MaterialPropertyBlock props;

        /// <summary>Signs drawn last frame (F7 render stats).</summary>
        public int DrawnLastFrame { get; private set; }
        /// <summary>Draw calls issued since enable, and the last call's parameters (the in-game sign check reports them).</summary>
        public int DrawCallsIssued { get; private set; }
        public RenderParams LastParams { get; private set; }

        /// <summary>The live instance (f-engineer's probe reads quad sizes through QuadSizeMetres).</summary>
        public static FishSurfaceFx Active { get; private set; }

        void OnEnable()
        {
            // Slot #17: World.unity shipped with no set, so no sign was ever drawn and nothing said so (f-td: error and off,
            // as FishPopulation.Fail; never a silent per-frame early-out).
            string missing = !set ? "FishRenderSet" : !set.ring ? "ring material" : !set.ringMesh ? "ring mesh" : null;
            if (missing != null)
            {
                Debug.LogError($"FishSurfaceFx: no {missing}; surface signs are off.", this);
                enabled = false;
                return;
            }
            props = new MaterialPropertyBlock();
            FishEvents.Surface += OnSign;
            Active = this;
        }

        /// <summary>Tests: queue a sign as if the sim had raised it (FishEvents.Raise is internal to the sim).</summary>
        public void InjectForTests(FishSurfaceEvent e) => OnSign(e);

        void OnDisable()
        {
            FishEvents.Surface -= OnSign;
            if (Active == this) Active = null;
        }

        /// <summary>Quad edge (m) drawn for an event, sized exactly as LateUpdate sizes it; 0 with no live FishSurfaceFx.</summary>
        public static float QuadSizeMetres(FishSurfaceEvent e) => Active ? Active.EventQuad(e, FishPopulation.Active) : 0f;

        float EventQuad(in FishSurfaceEvent e, FishPopulation pop)
        {
            var look = Look(e.kind);
            if (!set || !set.TryFootprint(e.kind, out var foot)) return QuadSize(look, e.size);
            float burst = pop && pop.Tuning && e.species >= 0 && e.species < pop.Tuning.species.Length ? pop.Tuning.species[e.species].burstSpeed : 1.5f;
            return FootprintQuad(foot, look, e.size, burst);
        }

        void OnSign(FishSurfaceEvent e)
        {
            if (e.kind == FishSurfaceKind.None) return;
            if (signCount == MaxSigns) { System.Array.Copy(signs, 1, signs, 0, MaxSigns - 1); signCount--; } // drop the oldest
            signs[signCount++] = e;
        }

        // Look per kind: (mode, rings, quad size in body lengths, minimum quad size in metres). Public so the legibility
        // measurements (FishSignLegibility) draw exactly what the game draws.
        public static (float mode, int rings, float sizeBodies, float minSize) Look(FishSurfaceKind k)
        {
            switch (k)
            {
                case FishSurfaceKind.Dimple: return (0f, 2, 1.5f, 0.5f);
                case FishSurfaceKind.GarGulpOrBask: return (0f, 1, 1.0f, 0.5f);
                case FishSurfaceKind.Swirl: return (0f, 2, 2.5f, 0.8f);
                case FishSurfaceKind.Rise: // F16 far-water rise: the roll's three soft rings
                case FishSurfaceKind.Roll:
                case FishSurfaceKind.RollOrTail: return (0f, 3, 3.0f, 1.0f);
                case FishSurfaceKind.Jump: return (0f, 3, 4.0f, 1.2f);
                case FishSurfaceKind.Wake:
                case FishSurfaceKind.FleeWake: return (1f, 1, 4.0f, 1.0f);
                case FishSurfaceKind.Busting:
                case FishSurfaceKind.NervousWaterOrFlip: return (2f, 1, 2.0f, 2.0f); // size is the school radius
                default: return (0f, 1, 1.5f, 0.5f);
            }
        }

        void LateUpdate()
        {
            DrawnLastFrame = 0;
            if (!set || !set.ring || !set.ringMesh) return;
            var pop = FishPopulation.Active;
            var cam = Camera.main;
            if (!cam) return;
            double now = WaterClock.Now;
            float eventRadius = pop && pop.Tuning ? pop.Tuning.eventRadius : 150f;
            Vector3 eye = cam.transform.position;
            int n = 0, keep = 0;
            Bounds bounds = default;
            for (int i = 0; i < signCount; i++)
            {
                var e = signs[i];
                float age = (float)(now - e.startTime);
                bool hasFoot = set.TryFootprint(e.kind, out var foot);
                float life = Mathf.Max(0.5f, hasFoot && e.duration <= 0f ? FootprintLife(foot, e.size) : e.duration);
                if (age > life || age < -1f) continue; // expired, or from another clock
                signs[keep++] = e;
                if (age < 0f) continue;
                var look = Look(e.kind);
                Vector3 p = e.position;
                Vector3 heading = e.heading.sqrMagnitude > 1e-6f ? e.heading : Vector3.forward;
                if (look.mode == 1f && pop && e.fishId >= 0 && TryFish(pop, e.fishId, out var follow)) { p.x = follow.position.x; p.z = follow.position.z; heading = follow.forward; }
                if (pop && pop.Water != null) p.y = pop.Water.SurfaceY(p.x, p.z);
                if (!InEventRadius(p, eye, eventRadius)) continue;
                float t = age / life;
                float size = EventQuad(e, pop);
                heading.y = 0f;
                var rot = Quaternion.LookRotation(heading.normalized, Vector3.up) * Quaternion.Euler(90f, 0f, 0f);
                matrices[n] = Matrix4x4.TRS(p + Vector3.up * 0.004f, rot, new Vector3(size, size * (look.mode == 1f ? 1.6f : 1f), 1f));
                if (hasFoot) ShapeFootprint(foot, look.mode, age, t, size, out radius[n], out strength[n]);
                else Shape(look.mode, t, out radius[n], out strength[n]);
                seed[n] = (e.fishId & 0xffff) * 0.37f + (float)(e.startTime % 97.0);
                mode[n] = look.mode;
                rings[n] = look.rings;
                var box = new Bounds(p, new Vector3(size * 1.6f, 0.1f, size * 1.6f));
                if (n == 0) bounds = box; else bounds.Encapsulate(box);
                n++;
            }
            signCount = keep;
            if (n == 0) return;
            props.SetFloatArray(RadiusId, radius); props.SetFloatArray(StrengthId, strength); props.SetFloatArray(SeedId, seed);
            props.SetFloatArray(ModeId, mode); props.SetFloatArray(CountId, rings);
            var rp = new RenderParams(set.ring) { matProps = props, worldBounds = bounds, shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false, layer = gameObject.layer };
            Graphics.RenderMeshInstanced(rp, set.ringMesh, 0, matrices, n);
            DrawnLastFrame = n;
            DrawCallsIssued++;
            LastParams = rp;
        }

        /// <summary>
        /// f-qa ruling (slot #20): the event radius is FLAT (horizontal) distance from the eye, as the sim admits signs and F7
        /// counts them, so every counted sign is drawn whatever the eye height (a 3D test dropped a counted sign at 147.5 m flat
        /// / 150.1 m 3D from the 27.7 m bluff eye).
        /// </summary>
        public static bool InEventRadius(Vector3 sign, Vector3 eye, float radius) =>
            FishEventDistance.Within(sign, eye, radius); // f-td: the one definition shared with the scheduler and the F7 count

        /// <summary>Quad edge length (m) of a sign: its body length (or school radius) times the kind's factor, at least the minimum.</summary>
        public static float QuadSize((float mode, int rings, float sizeBodies, float minSize) look, float size) =>
            Mathf.Max(look.minSize, look.sizeBodies * Mathf.Max(0.05f, size));

        /// <summary>A sign's shape at normalised age t: leading ring radius (fraction of the quad half size; rings slow as they
        /// spread) and strength (fade). A wake keeps its full shape.</summary>
        public static void Shape(float mode, float t, out float radius, out float strength)
        {
            radius = mode == 1f ? 1f : Mathf.Lerp(0.15f, 0.9f, Mathf.Sqrt(Mathf.Clamp01(t)));
            strength = (1f - t) * (1f - t);
        }

        // ---- Physical footprints (ruling/fish-sign-footprint-physical; F17). Only size, front speed and life change: the fade
        // law (1 - t)^2 and FishRing's contrast are untouched (f-designer / f-qa binding condition). ----

        /// <summary>Quad edge (m): rings get room for their largest ring plus the band; patches their diameter; wakes their arm.</summary>
        public static float FootprintQuad(in FishRenderSet.Footprint f, (float mode, int rings, float sizeBodies, float minSize) look, float size, float burstSpeed = 1.5f)
        {
            float arm = f.armLength > 0f ? f.armLength : f.armSeconds * burstSpeed;               // F17: arms = burst speed x 3 s
            if (look.mode == 1f) return Mathf.Max(look.minSize, arm / 1.6f);                    // the quad is 1.6x longer along the heading
            if (look.mode == 2f) return Mathf.Clamp(2f * size, f.patchDiameter.x, Mathf.Max(f.patchDiameter.x, f.patchDiameter.y));
            return 2f * Mathf.Max(f.maxRingRadius, f.coreRadius) * 1.15f;
        }

        /// <summary>Fallback life (s) when the event carries none: fixed, or within min..max by the event's size.</summary>
        public static float FootprintLife(in FishRenderSet.Footprint f, float size) =>
            f.lifeSec.y > f.lifeSec.x ? Mathf.Lerp(f.lifeSec.x, f.lifeSec.y, Mathf.InverseLerp(f.patchDiameter.x, f.patchDiameter.y, 2f * size)) : f.lifeSec.x;

        /// <summary>
        /// Rings: leading radius (fraction of the quad half size) = min(coreRadius + ringSpeed x ageSec, maxRingRadius) /
        /// half; patches and wakes keep the legacy shape. Strength is the unchanged (1 - t)^2 at normalised age t.
        /// </summary>
        public static void ShapeFootprint(in FishRenderSet.Footprint f, float mode, float ageSec, float t, float quad, out float radius, out float strength)
        {
            Shape(mode, t, out radius, out strength);
            if (mode == 0f && quad > 1e-4f)
                radius = Mathf.Min(f.coreRadius + f.ringSpeed * Mathf.Max(0f, ageSec), f.maxRingRadius) / (0.5f * quad);
        }

        static bool TryFish(FishPopulation pop, long id, out FishState state)
        {
            var states = pop.States;
            for (int i = 0; i < pop.Count; i++)
                if (states[i].active && states[i].fishId == id) { state = states[i]; return true; }
            state = default;
            return false;
        }
    }
}
