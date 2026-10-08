using UnityEngine;
using UnityEngine.Rendering;

namespace WashedAshore.Fish.Rendering
{
    /// <summary>
    /// Draws the live fish (spec F2, F7, F9; adr/fish-1; f-td 5.4). Each LateUpdate, after FishPopulation's Update, it decides
    /// every fish's draw mode with <see cref="FishDrawRule"/> (FishMurk at the body's shallowest point, tier, R_sub) plus the
    /// camera frustum, applies the visible-body cap (<see cref="FishVisibleCap"/>: admit below 3, no eviction, 1 s fade-in,
    /// 3 s re-admission cooldown), writes
    /// FishPopulation.DrawModes (the mode actually drawn, None while waiting for a slot) and .Visibility, and submits at most
    /// FishTuning.AdrMaxDrawn fish per model through Graphics.RenderMeshInstanced: one call per stand-in model, the Swim
    /// VAT animated per instance (phase and stroke amplitude from the sim). Rotation is LookRotation(forward, up) only; no
    /// yaw constants anywhere (CLAUDE.md model-facing rule). The water's murk values are copied from the water material
    /// into the fish shader's globals every frame. No colliders, no GameObject per fish.
    /// </summary>
    [DefaultExecutionOrder(1000)]
    public sealed class FishRenderer : MonoBehaviour
    {
        public FishRenderSet set;

        static readonly int ExtinctionId = Shader.PropertyToID("_FishWaterExtinction");
        static readonly int MinCosId = Shader.PropertyToID("_FishWaterMinCos");
        static readonly int F0Id = Shader.PropertyToID("_FishWaterF0");
        static readonly int BackId = Shader.PropertyToID("_BackColor"), BellyId = Shader.PropertyToID("_BellyColor"), FinId = Shader.PropertyToID("_FinColor");
        static readonly int MottleId = Shader.PropertyToID("_Mottle"), SilhouetteId = Shader.PropertyToID("_Silhouette"), FlashId = Shader.PropertyToID("_Flash");
        static readonly int PhaseId = Shader.PropertyToID("_Phase"), AmplitudeId = Shader.PropertyToID("_Amplitude"), FadeId = Shader.PropertyToID("_Fade");
        /// <summary>Brief subSurfaceFadeBand at the draw radius (FishDrawRule.RangeFade; accepted in f-td's range-band review).</summary>
        public const bool ApplyRangeFade = true;
        public const int SoftVisible = 3; // brief maxVisibleBodiesShore 3 (f-td cap review: admit only below it, never evict)

        const int Max = FishTuning.AdrMaxDrawn;
        Batch[] batches = new Batch[0];
        readonly Plane[] planes = new Plane[6];

        /// <summary>Fish submitted last frame (all models), for F7/F9 render stats.</summary>
        public int DrawnLastFrame { get; private set; }
        /// <summary>Fish that passed the rule but were dropped by the per-model cap last frame (should stay 0).</summary>
        public int OverCapLastFrame { get; private set; }
        /// <summary>Capped bodies showing last frame (alpha > 0; airborne jumps excluded), logged every frame for f-qa.</summary>
        public int VisibleBodiesLastFrame { get; private set; }

        readonly FishVisibleCap cap = new FishVisibleCap();
        /// <summary>The cap: subscribe to Cap.Changed for every admission / removal (f-qa in-view checks).</summary>
        public FishVisibleCap Cap => cap;

        /// <summary>Per live fish (aligned with FishPopulation.States this frame): the alpha multiplier actually drawn (0 = not
        /// drawn) and whether the cap is holding it back. For FishProbe / f-qa's N7 log.</summary>
        public float[] DrawnAlpha { get; private set; } = new float[0];
        public bool[] CapHeld { get; private set; } = new bool[0];

        /// <summary>A cap transition with its context: kind "fadeIn" (admitted), "hold" (newly held at alpha 0) or "fadeOut"
        /// (left the shown set because the draw rule stopped allowing it), fishId, position, in the camera frustum, distance
        /// to the camera, planned duration (s).</summary>
        public event System.Action<string, long, Vector3, bool, float, float> CapTransition;
        int[] candIndex = new int[256]; long[] candId = new long[256]; float[] candPriority = new float[256], candAlpha = new float[256];
        float[] candRange = new float[256];
        bool[] candExempt = new bool[256]; FishDrawMode[] candMode = new FishDrawMode[256]; Bounds[] candBox = new Bounds[256];

        sealed class Batch
        {
            public readonly Matrix4x4[] matrices = new Matrix4x4[Max];
            public readonly Vector4[] back = new Vector4[Max], belly = new Vector4[Max], fins = new Vector4[Max];
            public readonly float[] mottle = new float[Max], silhouette = new float[Max], flash = new float[Max], phase = new float[Max], amplitude = new float[Max], fade = new float[Max];
            public readonly MaterialPropertyBlock props = new MaterialPropertyBlock();
            public int count;
            public Bounds bounds;
        }

        void OnEnable()
        {
            if (!SystemInfo.supportsInstancing)
            {
                Debug.LogWarning("FishRenderer: GPU instancing isn't supported on this device; fish bodies are disabled (surface signs still draw).");
                enabled = false;
                return;
            }
            if (!set || set.models.Length == 0) { Debug.LogWarning("FishRenderer: no FishRenderSet; disabled."); enabled = false; return; }
            batches = new Batch[set.models.Length];
            for (int m = 0; m < batches.Length; m++) batches[m] = new Batch();
        }

        void LateUpdate()
        {
            DrawnLastFrame = 0;
            OverCapLastFrame = 0;
            var pop = FishPopulation.Active;
            var cam = Camera.main;
            if (!pop || !cam || pop.Count == 0 || !pop.Tuning) return;
            PushWater();
            var tuning = pop.Tuning;
            var murk = pop.Murk;
            var water = pop.Water;
            var states = pop.States;
            var modes = pop.DrawModes;
            var vis = pop.Visibility;
            Vector3 eye = cam.transform.position;
            GeometryUtility.CalculateFrustumPlanes(cam, planes);
            foreach (var b in batches) b.count = 0;

            if (candIndex.Length < pop.Count) Grow(pop.Count);
            if (DrawnAlpha.Length < pop.Count) { DrawnAlpha = new float[candIndex.Length]; CapHeld = new bool[candIndex.Length]; }
            for (int i = 0; i < pop.Count; i++) { DrawnAlpha[i] = 0f; CapHeld[i] = false; }
            int n = 0;
            for (int i = 0; i < pop.Count; i++)
            {
                ref readonly var s = ref states[i];
                var mode = FishDrawMode.None;
                float visibility = 0f;
                if (s.active && s.speciesIndex >= 0 && s.speciesIndex < tuning.species.Length && s.variantIndex >= 0 && s.variantIndex < set.variants.Length)
                {
                    var sp = tuning.species[s.speciesIndex];
                    float surface = water != null ? water.SurfaceY(s.position.x, s.position.z) : s.position.y;
                    var centre = new Vector3(s.position.x, 0.5f * (s.bodyMinY + s.bodyMaxY), s.position.z);
                    float half = Mathf.Max(0.05f, 0.5f * s.sizeScale * MaxLength(s.variantIndex));
                    float topDepth = surface - s.bodyMaxY;
                    float radius = tuning.BodyDrawRadius(s.mode, topDepth);
                    bool airborne = sp.airborneBodyAllowed && s.mode == FishMode.Jump && topDepth <= 0f;
                    mode = FishDrawRule.Decide(murk, planes, eye, sp.tier, sp.neverDrawBody, surface, s.bodyMinY, s.bodyMaxY, centre, half,
                                               tuning.bodyTierDepth, radius, out visibility, out var box, airborne);
                    if (mode != FishDrawMode.None)
                    {
                        candIndex[n] = i; candId[n] = s.fishId; candMode[n] = mode; candBox[n] = box;
                        // Cap priority: murk alpha x projected size; airborne jump bodies are exempt (f-designer spec 4).
                        candPriority[n] = visibility * (2f * half) / Mathf.Max(0.1f, Vector3.Distance(eye, centre));
                        candExempt[n] = airborne; // f-designer spec 4: airborne jump bodies (airborneBodyAllowed) aren't capped
                        candRange[n] = ApplyRangeFade ? FishDrawRule.RangeFade(Vector3.Distance(eye, centre), radius) : 1f; // brief 3 m edge band
                        n++;
                    }
                }
                if (modes != null && i < modes.Length) modes[i] = FishDrawMode.None;
                if (vis != null && i < vis.Length) vis[i] = visibility;
            }
            VisibleBodiesLastFrame = cap.Apply(n, candId, candPriority, candExempt, candAlpha, Time.deltaTime, SoftVisible);
            for (int c = 0; c < n; c++)
            {
                int i = candIndex[c];
                CapHeld[i] = cap.IsHeld(candId[c]);
                float a = candAlpha[c] * candRange[c];
                if (a <= 0f) continue; // held for a slot, or at the very range edge: not drawn
                DrawnAlpha[i] = a;
                if (!Add(states[i], candMode[c], candBox[c], a)) { OverCapLastFrame++; continue; }
                if (modes != null && i < modes.Length) modes[i] = candMode[c];
            }

            if (CapTransition != null) RaiseTransitions(pop, n, eye);

            for (int m = 0; m < batches.Length; m++)
            {
                var b = batches[m];
                if (b.count == 0) continue;
                b.props.SetVectorArray(BackId, b.back); b.props.SetVectorArray(BellyId, b.belly); b.props.SetVectorArray(FinId, b.fins);
                b.props.SetFloatArray(MottleId, b.mottle); b.props.SetFloatArray(SilhouetteId, b.silhouette); b.props.SetFloatArray(FlashId, b.flash);
                b.props.SetFloatArray(PhaseId, b.phase); b.props.SetFloatArray(AmplitudeId, b.amplitude); b.props.SetFloatArray(FadeId, b.fade);
                var rp = new RenderParams(set.models[m].material)
                {
                    matProps = b.props, worldBounds = b.bounds, shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false, layer = gameObject.layer,
                };
                Graphics.RenderMeshInstanced(rp, set.models[m].mesh, 0, b.matrices, b.count);
                DrawnLastFrame += b.count;
            }
        }

        float MaxLength(int variant)
        {
            var bodies = FishPopulation.Active.Bodies;
            return bodies && bodies.Has(variant) ? Mathf.Max(bodies.bodies[variant].clipBounds.size.z, bodies.bodies[variant].clipBounds.size.x) : 1f;
        }

        // Only runs when someone subscribed (a probe); a removed fish is looked up in States, the others are candidates.
        void RaiseTransitions(FishPopulation pop, int n, Vector3 eye)
        {
            foreach (var id in cap.AdmittedThisFrame) Raise("fadeIn", id, n, pop, eye, FishVisibleCap.FadeSeconds);
            foreach (var id in cap.NewlyHeldThisFrame) Raise("hold", id, n, pop, eye, 0f);
            foreach (var id in cap.RemovedThisFrame) Raise("fadeOut", id, n, pop, eye, 0f);
        }

        void Raise(string kind, long id, int n, FishPopulation pop, Vector3 eye, float planned)
        {
            for (int i = 0; i < pop.Count; i++)
            {
                if (pop.States[i].fishId != id) continue;
                var p = pop.States[i].position;
                bool inView = GeometryUtility.TestPlanesAABB(planes, new Bounds(p, Vector3.one * 0.5f));
                CapTransition(kind, id, p, inView, Vector3.Distance(eye, p), planned);
                return;
            }
            CapTransition(kind, id, Vector3.zero, false, -1f, planned); // no longer live
        }

        void Grow(int count)
        {
            int c = Mathf.NextPowerOfTwo(count);
            candIndex = new int[c]; candId = new long[c]; candPriority = new float[c]; candAlpha = new float[c];
            candExempt = new bool[c]; candRange = new float[c]; candMode = new FishDrawMode[c]; candBox = new Bounds[c];
        }

        bool Add(in FishState s, FishDrawMode mode, Bounds box, float fade)
        {
            var v = set.variants[s.variantIndex];
            var b = batches[v.model];
            if (b.count >= Max) return false;
            int k = b.count++;
            var fwd = s.forward.sqrMagnitude > 1e-8f ? s.forward : Vector3.forward;
            b.matrices[k] = Matrix4x4.TRS(s.position, Quaternion.LookRotation(fwd, Vector3.up), v.axisScale * s.sizeScale);
            // Instanced colour arrays bypass the material colour conversion: swatches are sRGB, the shader works in linear.
            b.back[k] = v.back.linear; b.belly[k] = v.belly.linear; b.fins[k] = v.fins.linear;
            b.mottle[k] = v.mottle;
            b.silhouette[k] = mode == FishDrawMode.Shadow ? 1f : 0f;
            b.flash[k] = Mathf.Clamp01(s.flash);
            b.phase[k] = s.animPhase;
            b.amplitude[k] = Mathf.Clamp01(s.amplitude);
            b.fade[k] = fade;
            if (k == 0) b.bounds = box; else b.bounds.Encapsulate(box);
            return true;
        }

        // A material can't read another material's properties: copy the water's murk terms (f-td addendum 1).
        void PushWater()
        {
            var w = set.water;
            if (!w) return;
            Shader.SetGlobalVector(ExtinctionId, w.GetVector("_Extinction"));
            Shader.SetGlobalFloat(MinCosId, w.GetFloat("_MinCosView"));
            Shader.SetGlobalFloat(F0Id, w.GetFloat("_F0"));
        }
    }
}
