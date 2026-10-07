## D1. Rendering candidates and licences (spec item 1)

Unity 6 URP still has no first-party water system. The HDRP Water System is HDRP-only and compute-based [13]; Unity's URP SSR preview thread demos water in the URP "Oasis" sample, but as a Shader Graph material, not a system [20][48].

| Candidate | Licence (read this run) | Free, no store login | Last activity | Target | Verdict |
|---|---|---|---|---|---|
| Crest (GitHub, MIT) | MIT [1] | yes | push 2026-06-18, tag 4.23.0 [1] | **Built-in renderer only**; URP and HDRP editions and Crest 5 are paid Asset Store products [2] | **Cut** (G1: the URP build is paid) |
| KWS Water URP | commercial [14] | no | n/a | Unity 6 | **Cut** (G1) |
| Staggart Stylized Water 2/3 | commercial [15] | no | n/a | Unity 6, WebGL2 supported [15] | **Cut** (G1) |
| Unity HDRP Water System | Unity package | n/a | n/a | HDRP only [13] | **Cut** (wrong pipeline) |
| boat-attack-water (com.unity.urp-water-system) | Unity Companion License, Unity-dependent projects only [3][8] | yes | push 2024-10-17; package 2.0.0-preview.5 for 2021.3 / URP 12.1 [3] | Gerstner, planar reflection (default), caustics; also cubemap/probe/SSR modes [4] | Finalist, weak: open Unity 6 shader compile error (#25, 6000.0.31f1, DirectBDRF signature) [5]; "not supported in any official capacity" |
| Uber-Stylized-Water (MatrixRex) | MIT (c) 2025 [9] | yes | push 2026-10-05, v1.1.6, 8 releases, 1 contributor [9][47] | Unity 6000.0.30+, URP only; authored on 6000.0.72f1 [9] | Finalist: freshest MIT option |
| YuanYuSQ/Unity6_WaterShader | **no licence file** (all rights reserved by default) [12] | n/a | 2026-09-23 | Unity 6 URP, Render Graph feature + SSR [12] | **Cut** (G1) |
| daniel-ilett/water-urp, mozankatip/InteractiveStylizedWater | MIT | yes | 2020 / 2023; Unity 2019.3 / 2022.1 | old | Cut as candidates (stale); reference only |
| Shader Graph "Production Ready" water sample (WaterLake, WaterStream, WaterStreamFalls) | not confirmed this run (package sample; Unity Companion License assumed, **unverified**) | yes, Package Manager sample | SG 14 docs page [16] | Shader Graph: scrolling normals, flow mapping, depth fog, reflection, refraction [16] | Finalist as a **reference/base** for a custom shader |
| Custom Shader Graph / HLSL water (ours) | ours | yes | n/a | whatever we target | Finalist |

Licence notes. The Unity Companion License allows commercial use; games made with the Work are not derivative works; use must be tied to a valid Unity licence; keep the notice; changes to the Work itself are assigned to Unity [8]. That's acceptable for a Unity game but it's a stickier licence than MIT for code we expect to port server-side (see D3).

Uber-Stylized-Water, read at source level [9][10][11]:
- No ScriptableRendererFeature or RenderPass at all, so Render Graph churn doesn't touch it. The only pipeline call is RenderPipeline.SubmitRenderRequest for its planar camera.
- Planar reflection **is a second camera** but is opt-in per material (_ENABLEPLANERREFLECTION) and only renders when a PlanarReflectionVolume exists. It can be fully disabled (G5 passes).
- **Camera Depth Texture is mandatory** ("water can be completely invisible" without it) [10]. The Opaque Texture is only needed for refraction.
- Waves are two closed-form Gerstner waves in the vertex stage. They're mirrorable in C#, but there's **no C# height query**; the maintainer said so in Nov 2025 [11]. The horizontal Gerstner term means a C# mirror needs an inversion step (D3).
- The generated shader is about 1.28 MB with many keywords, and has had "too many variants" and "doesn't compile on 6000.1.21f" issues (since closed) [47]. No WebGL mention anywhere. Untested on 6000.6.

boat-attack-water, read at source level [4][6]:
- All four passes implement both Execute and RecordRenderGraph.
- The planar reflection is a separate static path driven by beginCameraRendering, and probe/cubemap modes exist.
- ComputeBuffer is used only as a StructuredBuffer for wave data and is disabled on WebGL (vector-array fallback) [4].
- The CPU Burst height job evaluates Gerstner **at the query point without inversion**, timed by Time.time [6]. So its CPU height is not the rendered height at that (x,z) (D3).

## D2. Unity 6.6 / URP 17.6 and web compatibility (spec item 1)

- **Render Graph only.** URP Compatibility Mode was deprecated in 6.0, removed in 6.3, and fully removed (including the restore define) in 6.4 [18][19]. On 6000.6, any third-party pass that only implements Execute() does not run. That eliminates most pre-2023 URP water packages. Packages with no custom passes (Uber, or a plain material) are unaffected.
- **SSR is not available.** URP's built-in Screen Space Reflections is a *preview* targeted at Unity 6.7, present only in alpha builds from 6000.6.0a7 [20]. The thread says nothing about WebGL2 or transparent receivers, and a user notes that low-res SSR on water looks "very obvious" [20]. Third-party URP SSR repos exist, but none was confirmed for Render Graph plus WebGL2. **Reflections for this pod = sky/reflection probe + fresnel.**
- **WebGPU** came out of experimental in 6000.6 (announced 2026-08-24). It's opt-in, with a compatibility fallback, and enables the GPU Resident Drawer, compute and VFX Graph on web [21][22]. The standing ADR (studio:engineering adr/web-target-webgl2) keeps the web on WebGL2. Nothing here needs compute, so a WebGL2-clean shader also runs on WebGPU. No source confirmed Shader Graph transparent plus scene depth on WebGPU (open question).
- **WebGL2 constraints that matter:**
  - No compute, and no GPU Resident Drawer (it needs compute); the SRP Batcher works [23].
  - FetchSceneDepth (depth as an input attachment) is DX12/Vulkan only [24].
  - GLES depth copy is reliable only without MSAA; with MSAA a prepass is the alternative [30].
  - Derivative nodes are fragment-only, and vertex texture reads need Sample Texture 2D LOD.
- **Camera Depth and Opaque textures are per URP asset (per quality level)** [26][28]. With Depth Texture off, the Shader Graph *Scene Depth* node silently returns 0.5 (constant mid depth, no error); with Opaque off, *Scene Color* returns black [27]. **Project fact:** the WebGL quality level uses Mobile_RPAsset, which has Depth Texture **off** and Opaque Texture **off**; PC_RPAsset has both on (read from Assets/Settings/*.asset). So any approach that depends on scene depth (Uber, Unity's sample water depth fog, BoatAttack) either changes the web asset, adding CopyDepth on the web, or renders wrong on web without erroring. That's exactly the kind of silent failure W10 would catch late.
- No source gave a measured millisecond cost for CopyDepth or CopyColor on any hardware [25][29]; Unity's guidance is "enable only if a shader samples it" [29]. Cost must be measured in Phase 1.

## D3. Motion: one deterministic f(x, z, t) (spec item 3)

What the field does:
- Analytic sums of sines or Gerstner waves are closed-form in (x, z, t), so CPU and GPU agree up to float rounding [33].
- Crest reads heights back from the GPU asynchronously and uses a *baked* FFT for headless servers ("limited support") [34][35]. It warns that dynamic wave sims are not network-synchronised [36].
- Multiplayer titles (Crest's networked time provider, UE water, Sea of Thieves per a forum answer) sync one thing, the **server clock**, and evaluate the same function on each end [35][39][40].

Two traps, both evidenced:
1. **Gerstner horizontal displacement.** The rendered point above rest position p0 is p0 + d(p0), so "height at world (x, z)" needs the inverse. BoatAttack's CPU job skips the inversion [6], and a Godot implementation accepts the error [45]. Unity users have also diverged by feeding displaced positions back in [37]. One implementation inverts with a capped Newton iteration and notes that **steepness 0 reproduces vertical-only results exactly** [38].
2. **Clock drift and float time.** BoatAttack's CPU side uses Time.time [6] while shaders read their own _Time. A float32 clock loses precision as it grows: at t = 3600 s one ULP is 2^-12 s, about 0.24 ms, and at a week it's 62.5 ms. That's arithmetic, matching a forum report [42]. The fix in the sources is to wrap time [42] and drive it from one clock [35][39].

**Ruling for this project (river, not sea).** This reconciles with the pod's staged plans: w-engineer's WaterMotion, w-level's quads, and w-artist's §2.5.
- **f(x, z, t) = WaterLevelY + a sum of at most 4 vertical sines.** No Gerstner horizontal term, so the height at (x, z) is exact with no inversion [38]. Amplitudes sum to 3 cm or less; wavelengths are 1.5-6 m; speeds are 0.35-0.6 m/s; the 4 directions are spread out (w-artist §2.5).
- **The geometry stays flat.** Each tile gets one 1x1 quad at WaterLevelY. The shader uses f only for **analytic normals** (the GPU Gems derivative form [33]), so the ripple you see is the function the crate samples.
  - The rendered plane is at most sum(A) = 3 cm from f by construction, which is inside W9's 5 cm.
  - W9 still has to prove that C#'s f equals the shader's f (EditMode test against the analytic formula, plus one GPU readback).
- **Micro ripples are visual only and not part of f.** Two normal maps are advected along the bank-tangent flow with two-phase blending [41].
  - Flow is texture advection and adds no height [41].
  - The current pushes nothing (ruling/water-swim-numbers-r2). A physical current later needs a flow field and a W8 re-sweep.
- **One clock, and no Time inside f.**
  - WaterMotion.Offset(waves, x, z, t) is pure. WaterBody publishes the time and wave globals; the shader never reads _Time.
  - For server readiness, the POC's timeSinceLevelLoad has to sit behind one clock seam that a server clock can replace (Crest's TimeProviderNetworked pattern [35]).
  - **Precision condition (W9 review):** compute each wave's temporal phase (omega_i t) mod 2 pi in double on the CPU and push it per wave. Don't push a raw float t. That keeps precision constant for any session length. A float32 t degrades to a 0.24 ms ULP at 1 h and 62.5 ms at a week (arithmetic; [42]).
  - The spatial phase k·x reaches about 1.1e4 rad at x = 2.6 km for a 1.5 m wavelength. Its float ULP is about 1e-3 rad, so the height error is A x 1e-3, which is negligible. GPU sin() accuracy at large arguments isn't documented in any source read, so W9's GPU readback must sample near the world corners, not just near the origin.
- **Shared definition.** The wave table is one ScriptableObject (WaterMotionSettings). C# reads it directly. The shader gets it through globals set by WaterBody, so there's no second copy of the numbers. The HLSL include WaterSurface.hlsl mirrors WaterMotion.Offset line for line.
- **Buoyancy, later.** Point probes sample f (the Godot pattern [45], Crest's per-point queries [34]). No GPU readback, so a headless server can run it. If boats ever need visible swell, add a gridded mesh with displacement wavelengths of 32 m or more: on an 8 m grid, the linear-interpolation error is about A k² h² / 8 = 0.31 A.

## D4. Budgets, meshes, streaming (spec item 6)

The binding numbers: at most +1.5 ms over the R2 median of 6.68 ms at 1080p, p99 under 16.6 ms (TD note §3); web at 30 FPS or more.

**No published source gave per-feature millisecond costs for URP water** on any hardware. Crest and Staggart rank features qualitatively only [15][32]. So the budget is protected by *design choices with known cost direction*, then measured:

| Feature | Cost direction (source) | Choice |
|---|---|---|
| Planar reflection | re-renders the scene, roughly doubles draws [15] | banned (TD §3) |
| SSR | screen-space ray march, preview only [20] | not available on 6.6 |
| Refraction (Opaque Texture) | full-screen CopyColor plus a resample [15][29] | **off**: water is opaque within 1-2 m |
| Scene depth (Depth Texture) | CopyDepth or prepass; prepass ~doubles geometry draws [15]; GLES+MSAA problems [30] | **not required**: baked WaterMaps (Crest "depth cache" pattern: baked = no per-frame cost, static only [31]). PC may use the scene depth it already pays for, for soft edges on dynamic objects (optional, first cut) |
| Vertex waves | cost scales with vertex count x screen coverage [15] | **none**: flat quads, analytic normals |
| Normal maps | per-pixel samples [15] | 2 maps x 2 flow phases on PC; 1 phase on web |
| Draw calls | Crest's LOD rings are "draw call heavy" [32] | 20 quads, one material, SRP Batcher [23] |

Mesh and streaming:
- **One 1x1 quad per 1024 m terrain tile** (w-level plan): 20 quads, 40 triangles.
  - It's on layer 4 Water, with no collider, no shadows cast and none received. Wade and swim are analytic from f and TerrainQuery, not from physics (w-tools W4 plan).
  - Pixels over land are depth-rejected under the terrain, so no cut mesh is needed. Coverage and seams (W2) are trivially exact on a shared 1024 m grid.
- **Streaming.** Water lives in the persistent scene (TD note §2, step 3), so it doesn't stream. The quads are per tile, so they could move into tile scenes later with no format change.
- **Distant LOD.** Fade the normal strength to flat between 40 and 200 m (w-artist §2.5) and let URP linear fog (120-520 m) take the horizon. No primary source was found for distant-water LOD practice; this is standard and has no cost.
- **Baked WaterMaps** (w-artist §2.1):
  - One global RG8 bilinear texture: R = signed shore distance copied from LevelMaps, G = bed depth clamped to 0-4 m. 2 m cells, 2048 x 2560, about 10.5 MB on GPU.
  - Built by the water build in batchmode. It sits behind the W3 guard (lake-mask hash 8f8a637cd4657d82, north-line check), which **throws on mismatch** before any scene change.
  - Web: keep 2 m unless W10 flags web memory or size; the fallback is a 4 m bake (about 2.6 MB).
- **Web.**
  - No depth or opaque copy on WebGL2, so Mobile_RPAsset stays unchanged.
  - SRP Batcher on; no MSAA dependence; no compute; no inline samplers (they fail on GLES, w-artist R4).
  - One hand-written shader with 2 keywords or fewer, and normal maps at 512² or smaller [43][44].
  - Expected web size delta: under 2 MB (estimate).
- **Cost estimate** (w-artist §4; estimates, not measurements): PC 0.25-0.45 ms and web 0.4-0.75 ms at 1080p, with water covering about half the screen.
  - The binding check is the R2 walk re-run (W10).
  - Cut order if it's over: PC soft intersection, then the second flow phase, then drift lines, then render scale.
