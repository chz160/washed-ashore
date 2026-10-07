---
title: 'technical research: water for Washed Ashore (Bells Bend river)'
type: 'technical'
topic: 'Water rendering, motion, budgets and player-in-water for Unity 6.6 URP 17.6 (Windows + WebGL2)'
decision: 'Which water approach the water pod builds, plus the look, swim numbers and drowning design for later'
source: 'native run (bmad-deep-recon, select shape, deep preset) + w-artist and w-designer sections'
status: complete
preset: 'deep'
validation: 'normal (two-source on version/compat and the top-two deciding cells)'
created: '2026-10-06'
updated: '2026-10-06'
claims_verified: 9
claims_unverified: 3
---

# Technical research: water for Washed Ashore

**Decision this research serves:** what water the pod builds in Unity 6000.6.4f1 / URP 17.6.0 for the Bells Bend river, on Windows (60 FPS or more; at most +1.5 ms over the R2 median of 6.68 ms) and WebGL2 (30 FPS or more). It also records the look, the swim numbers and the drowning-as-boundary design for later.

Lead: w-td (technical-director), items 1, 3 and 6. Sections from w-artist (items 2 and 5) and w-designer (items 4 and 7). Run folder: digests/ (D1-D4, 2 rounds), sections/, brief.md, .memlog.md.

## Executive summary

**Pick: custom, hand-written URP water.**
- One flat 1x1 quad per 1024 m tile.
- Normals come from one deterministic f(x, z, t), a sum of at most 4 vertical sines that C# evaluates identically, plus flow-advected micro-normals.
- Absorption, bed hiding, foam and shallows come from a baked, hash-guarded WaterMaps texture (shore distance + bed depth).
- Reflections are sky or probe with Fresnel. No refraction, no planar camera, and no camera depth or opaque textures on web.

Three findings drive the pick:
1. **There is no free, current, URP-native water package that fits.**
   - Crest's free edition is built-in-renderer only; its URP edition is paid [1][2].
   - KWS and Staggart are paid [14][15]. HDRP Water is HDRP-only [13].
   - BoatAttack's water targets URP 12, uses planar reflection by default, and has an open Unity 6 shader compile error [3][4][5].
   - Uber-Stylized-Water (MIT, active) is the best package, but it *requires* the camera depth texture, has no C# height query, and is untested on 6000.6 or WebGL [9][10][11].
2. **The platform removes the expensive options anyway.**
   - Unity 6.4+ is Render Graph only [18][19].
   - URP SSR is a 6.7 preview [20], and planar reflection is banned.
   - The web quality level's URP asset has the depth and opaque textures **off**, and Scene Depth silently returns 0.5 when they're off [27][28]. A baked depth texture (Crest's "depth cache" pattern [31]) gives the murk on both platforms with no extra passes.
3. **Determinism is cheap if the motion is vertical-only and the clock is single.** Gerstner's horizontal term forces an inversion that shipped code skips [6][45]. Vertical-only sines are exact [38]. Server sync is "same function + server time" across Crest, UE and Sea of Thieves [35][39][40].

**Biggest caveat:** no source published a millisecond cost for any URP water feature. The budget figures here (PC 0.25-0.45 ms, web 0.4-0.75 ms) are **estimates**. They're bound by the R2 walk re-run in W10, with a cut order ready.

Runner-up: Uber-Stylized-Water with planar off. It wins only if the brief changes to a stylised look and the web gets the depth texture. Hedge: the water is one shader, one include and one baked texture behind WaterBody, so swapping the renderer later touches no gameplay code.

## Requirements frame (from the project, not from research)

Hard gates:
- **G1** Free; no Asset Store login; licence recorded.
- **G2** Unity 6.6 / URP 17.6 (Render Graph).
- **G3** WebGL2 (standing ADR adr/web-target-webgl2).
- **G4** Agents can build it from scripts.
- **G5** No planar camera (TD note §3).
- **G6** Motion is a pure f(x, z, t) that C# can evaluate off the GPU.

Weights (1-5): look fit to greenlight/water 5; performance headroom 5; determinism and server parity 4; maintenance and upgrade risk 4; agent build effort 3; extensibility to boats, debris and floods 3; web size 2. Total weight 26.

## Weighted decision matrix

Scores run 1-5. Cells marked * rest on a single source or an estimate.

| Criterion (weight) | A. Custom HLSL + baked WaterMaps | B. Uber-Stylized-Water, planar off | C. boat-attack-water, probe mode | D. Shader Graph sample water as base |
|---|---|---|---|---|
| Gates G1-G6 | pass all (ours) | G1 pass [9]; G2 untested on 6000.6*; G3 unverified, needs a web depth texture [10]; G5 pass [9]; G6 partial: no C# query, Gerstner horizontal [11] | G1 pass, UCL [3]; G2 open Unity 6 compile error [5]; G3 unverified; G5 pass via probe mode [4]; G6 partial: CPU job without inversion [6] | G1 licence unverified*; G2 SG 17.6 availability unverified*; depth fog needs a web depth texture [16][27] |
| Look fit (5) | 4: built to the brief's numbers (D5) | 3: stylised; a "Murky" template exists but isn't tuned to the brief [9] | 2: ocean, caustics, planar default [4] | 4: has stream, flow and depth fog [16]* |
| Perf headroom (5) | 5: no extra passes; 20 draws; est. ≤0.45 ms PC* | 3: depth texture mandatory; 1.28 MB generated shader, variant issues [10][47] | 3: water buffers and caustics passes [4] | 4: needs the depth copy (refraction can be dropped)* |
| Determinism (4) | 5: vertical sines, one clock [38] | 2: Gerstner without a query [11] | 3: Burst CPU job exists but no inversion [6] | 3: normal maps; f has to be added |
| Maintenance (4) | 4: we own about 1 shader; URP include churn only | 2: single maintainer, untested on 6.6 [9][47] | 1: unofficial, URP 12, open bug [3][5] | 3: Unity sample, drifts with SG versions* |
| Agent effort (3) | 3: must write it, about 400 lines | 4: drop-in material | 2: port and fix | 4: import by script* |
| Extensibility (3) | 4: f is server-ready; WaterMaps feeds flood and rain masks later | 2 | 4: buoyancy jobs [6] | 3 |
| Web size (2) | 5: one small shader, RG8 + 2 normals | 2: large variant set [47] | 3 | 4 |
| **Weighted total (/130)** | **112 (4.31)** | **68 (2.62)** | **65 (2.50)** | **93 (3.58)** |

Arithmetic:
- A = 4·5 + 5·5 + 5·4 + 4·4 + 3·3 + 4·3 + 5·2 = 112.
- B = 3·5 + 3·5 + 2·4 + 2·4 + 4·3 + 2·3 + 2·2 = 68.
- C = 2·5 + 3·5 + 3·4 + 1·4 + 2·3 + 4·3 + 3·2 = 65.
- D = 4·5 + 4·5 + 3·4 + 3·4 + 4·3 + 3·3 + 4·2 = 93.

Re-weighting check: "agent effort" is the only criterion where a rival beats A (B and D score 4, A scores 3). D would overtake A only if that weight rose from 3 to 23 or more, so no plausible re-weight changes the pick. D is a reference, not a rival: A may borrow its flow-map ideas.

**Cuts and why:**
- Crest URP, Crest 5, KWS, Staggart: paid or need a store login (G1) [2][14][15].
- HDRP Water: wrong pipeline [13].
- YuanYuSQ/Unity6_WaterShader: no licence [12].
- daniel-ilett and mozankatip repos: Unity 2019/2022 era.
## D1. Rendering candidates and licences (spec item 1)

Unity 6 URP still has no first-party water system. The HDRP Water System is HDRP-only and compute-based [13]; Unity's URP SSR preview thread demos water in the URP "Oasis" sample, but as a Shader Graph material, not a system [20][48].

| Candidate | Licence (read this run) | Free, no store login | Last activity | Target | Verdict |
|---|---|---|---|---|---|
| Crest (GitHub, MIT) | MIT [1] | yes | push 2026-06-18, tag 4.23.0 [1] | **Built-in renderer only**; README (per w-web, scratchpad water-web-sources.md) says Crest does not support OpenGL or WebGL; URP and HDRP editions and Crest 5 are paid Asset Store products [2] | **Cut** (G1: the URP build is paid) |
| KWS Water URP | commercial [14] | no | n/a | Unity 6 | **Cut** (G1) |
| Staggart Stylized Water 2/3 | commercial [15] | no | n/a | Unity 6, WebGL2 supported [15] | **Cut** (G1) |
| Unity HDRP Water System | Unity package | n/a | n/a | HDRP only [13] | **Cut** (wrong pipeline) |
| boat-attack-water (com.unity.urp-water-system) | Unity Companion License, Unity-dependent projects only [3][8] | yes | push 2024-10-17; package 2.0.0-preview.5 for 2021.3 / URP 12.1 [3] | Gerstner, planar reflection (default), caustics; also cubemap/probe/SSR modes [4] | Finalist, weak: open Unity 6 shader compile error (#25, 6000.0.31f1, DirectBDRF signature) [5]; "not supported in any official capacity" |
| Uber-Stylized-Water (MatrixRex) | MIT (c) 2025 [9] | yes | push 2026-10-05, v1.1.6, 8 releases, 1 contributor [9][47] | Unity 6000.0.30+, URP only; authored on 6000.0.72f1 [9] | Finalist: freshest MIT option |
| YuanYuSQ/Unity6_WaterShader | **no licence file** (all rights reserved by default) [12] | n/a | 2026-09-23 | Unity 6 URP, Render Graph feature + SSR [12] | **Cut** (G1) |
| daniel-ilett/water-urp, mozankatip/InteractiveStylizedWater | MIT | yes | 2020 / 2023; Unity 2019.3 / 2022.1 | old | Cut as candidates (stale); reference only |
| Shader Graph "Production Ready" water sample (WaterLake, WaterStream, WaterStreamFalls) | not confirmed this run (package sample; Unity Companion License assumed, **unverified**) | yes, Package Manager sample | SG 14 docs page [16] | Shader Graph: scrolling normals, flow mapping, depth fog, reflection, refraction [16] | Finalist as a **reference/base** for a custom shader |
| Custom Shader Graph / HLSL water (ours) | ours | yes | n/a | whatever we target | Finalist |

Licence notes. w-web reported BoatAttack as MIT; the raw LICENSE.md read this run says Unity Companion License [3], and the raw file wins (logged as disputed, resolved). The Unity Companion License allows commercial use; games made with the Work are not derivative works; use must be tied to a valid Unity licence; keep the notice; changes to the Work itself are assigned to Unity [8]. That's acceptable for a Unity game but it's a stickier licence than MIT for code we expect to port server-side (see D3).

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

## D5. Look and underwater (spec items 2 and 5; author w-artist)

Author: w-artist (technical-artist). Revision 2, 2026-10-06. Phase 0 research only: nothing built.

Bound by:
- greenlight notes 1-2 (`studio:decisions` `greenlight/water`)
- TD note §3: no planar camera; ≤1.5 ms over the R2 median of 6.68 ms; p99 < 16.6 ms
- w-director ruling `water-swim-numbers-r2`: no duck-under, the camera never goes underwater
- w-td's direction: baked depth and shore texture; no camera depth or opaque texture; no refraction

Source numbers [101]..[119] are listed in the table at the end. [R1]..[R5] are repo facts checked directly.

#### 0. Project facts that decide the design

| # | Fact | Consequence |
|---|---|---|
| R1 | `Assets/Settings/PC_RPAsset.asset` (Standalone, quality 1): Forward+, Depth Texture ON, Opaque Texture ON (2x downsample), MSAA off, HDR on, SSAO. `Mobile_RPAsset.asset` (WebGL, quality 0): Forward, **Depth Texture OFF, Opaque Texture OFF** (`QualitySettings.asset` m_PerPlatformDefaultQuality) | Scene Depth and Scene Color need those textures [111][112][113]. With the baked approach below, the water needs neither, so **Mobile_RPAsset stays unchanged** and web gets the same look as PC. |
| R2 | `LevelMaps` shore distance is the road grid's B channel (128 + signed m), **quantised to 1 m**, 2 m cells, RGB24, point-filtered and readable (`BellsBendLevelMaps.cs:7,50`). It measures the distance to `v.innerBank`, negated outside the polygon (`BellsBendLevel.cs:47-48`) | It is the foam source (W5). The real waterline sits a mean 0.51 m (p95 0.75, max 1.25) outside the inner bank (td-input, L1), so the waterline is near sd ≈ −0.5. |
| R3 | North of the line, the vista land counts as "outside the polygon", so its shore distance is negative (`BellsBendGround.cs:133`). The carved river banks there are not on innerBank | LevelMaps has **no valid foam data on the north-of-line banks**. Fallback in §2.4. |
| R4 | Inline sampler states don't work when targeting OpenGL ES or GL Core [114] (the WebGL2 path) | The shader can't bilinear-sample the point-filtered LevelMaps texture on web. The bake has to emit **its own bilinear texture**. |
| R5 | `World.unity`: linear fog 120-520 m, colour (0.72, 0.80, 0.88). Reflections come from the **default procedural skybox** at 128 px | A raw sky reflection is blue, which pushes the water toward turquoise (forbidden). The shader must desaturate and tint it. |

#### 1. Rendering constraints (Unity 6000.6.4f1, URP 17.6)

- URP has **no SSR, no planar reflections, no screen-space refraction and no water system**. The Unity 6.6 feature comparison lists all four as HDRP-only [110].
- URP SSR exists only as a preview aimed at Unity 6.7 [119], so it isn't available here.
- Reflection probes (baked or real-time, box projection, blending) and the skybox are supported in URP [110].
- R8 and RG8 are core texture formats in WebGL2 / GLES3 [116][117].

#### 2. Item 2: the look, built on the baked depth and shore texture

#### 2.1 The bake (w-td's direction; I agree)

**WaterMaps**, written by the water build and owned by w-artist (`BellsBendWaterMaps.Bake`), is the only data the shader reads besides the shared wave globals. It works like Crest's depth cache [115]: depth is precomputed, so absorption, foam and shallows need no camera depth.

| Channel | Content | Encoding |
|---|---|---|
| R | Signed shore distance, copied from LevelMaps B (not recomputed) | 128 + m, ±127 m, as in LevelMaps |
| G | Bed depth D = clamp(WaterLevelY − terrain height, 0, 4 m), sampled from the tile heightmaps at the cell centre | D / 4 × 255 (about 1.6 cm per step) |

- Format: RG8, **bilinear**, clamp, not readable. 2 m cells over the full extent (2048 × 2560), about 10.5 MB.
- Sampled by positionWS × (1 / size) − origin, with the vector set on the material by Bake.
- **One global texture, not per tile.** Water lives in the persistent scene (TD note, streaming §3), so it never streams. A single texture is seamless by construction and needs no per-quad binding (no MaterialPropertyBlock in w-level's builder). It costs the same memory as 20 × 512² tiles.
  - If water ever moves into the tile scenes, splitting it per tile is a bake-side change only.
- Optional web variant at 4 m cells: about 2.6 MB. LevelMaps is already 1 m-quantised, so little is lost.

#### 2.2 Colour and absorption from bed depth

- **Model: Beer-Lambert transmittance, T = exp(−k · L)** per channel [106]. This is the depth-fog technique of [101], with physical constants.
- **Path length from the baked depth:** L = D / max(N·V, 0.15). That is the vertical depth stretched by the view angle, which is exact for the bluff-top view straight down. At grazing angles Fresnel reflection dominates anyway (§2.6).
- **How much murk.** Greenlight note 1 asks for opaque within about 1-2 m.
  - Poole-Atkins gives an attenuation of about 1.7 / Secchi depth [105].
  - A Secchi depth of 0.5-0.8 m (a silty river) gives k ≈ 2.1-3.4 /m.
- **Per-channel k (1/m):** R 2.2 (range 1.6-2.8), G 2.4 (1.8-3.0), B 3.2 (2.6-4.0).
  - Blue goes first because dissolved organic matter (CDOM) absorbs blue to UV most strongly, which is why inland water looks brown [118].
  - Pure water loses red first [118], which is why the clear-water tutorials put red first. That doesn't apply to this river.
- **Composite, with no scene colour:**
  - colour = scatter × (1 − T) + bedTint × T
  - alpha = 1 − T_avg × (1 − bedVisible)
  - bedTint is the silt colour #5E5238 (range #54492F to #6A5D42), standing in for the bank paint the bed would show.
  - Alpha blending lets the real bank show through in the shallows, and the colour term tints it.
- **Body (scatter) colour:** sRGB #4A4A2E (range #40402A to #565436). The shallow read is close to #6B6040 at D ≈ 0.2-0.4 m.
- **Edge fade:** alpha goes from 0 to 1 over D = 0.10-0.20 m, so there's no hard contact line with the bank.
  - G has 2 m cells, so the fade can't follow sub-metre features like the 0.31 m lip or the 3.41 m west neck step.
  - The scum band (§2.4) covers that contact line. Check it visually in Phase 1 (W5).
- **Optional, PC only (w-td allows it):** soft intersection with dynamic objects (the player's legs, the crate) using the Scene Depth that's already paid for on PC. alpha *= saturate((sceneEye − surfaceEye) / 0.1 m).
  - It costs about 0.03 ms. Web skips it; there, dynamic objects get the surface alpha from bed depth, which is about 0.9 or more at wading depth.
  - It's off by default and is the first thing cut.

#### 2.3 Bed hiding (TD gap 1)

- **Absorption alone, no seeded bed.** At k ≈ 2.6, T(1.5 m) ≈ 2%.
  - The W−2 shelf beyond about 1.5 m and the flat W−10 floor are invisible from the bank, from the bluff tops and at water level. That is well inside the "4-6 m" bar.
  - Only the first ~0.5 m of the 20° shelf shows, as a muddy fringe.
- A useful side effect: G clamps at 4 m, so the 5 all-water tiles are uniformly opaque and the flat bed can't show at all.
- No L3 re-run is needed for bed edits, because there are none.

#### 2.4 Foam and shallows

sd = shore distance (R channel), negative over water. D = bed depth (G channel).

| Layer | Placement | Width | Coverage | Look |
|---|---|---|---|---|
| Bank scum | Peak at sd −0.5..−1.5, fading to 0 by sd −3.0 (range −2.5..−3.5) | 1-3 m band | 30-50% after noise threshold | Patchy noise advected with the flow; never a solid rim |
| Drift lines | Centred at sd −5 (range −4..−8) | 0.3-0.6 m streaks | 15-25% | Noise stretched 6-10:1 along the flow, broken |
| Shallows | D < 0.5 m, from §2.2 itself | — | — | No separate layer |

- **Foam colour:** sRGB #A89F84 (range #9A927A to #B5AC92), albedo ≤ 0.55. Never white.
- **Bluff bases:** the faces are on innerBank, so scum lines the foot of Buzzard and McCord automatically.
- **Quantisation:** sd steps are 1 m on 2 m cells. Bilinear sampling plus ±0.5-1 m of noise distortion on the sample position hides that. If banding still shows, the fix is a finer shore encoding in the LevelMaps build (owned by w-level and w-tools).
- **North-of-line banks (R3).** Proposed recorded exception: where |sd| ≥ 100 or the point is north of `northLineZ`, scum comes from D instead (peak at D 0.1-0.3 m, fading by 0.8 m).
  - This uses the same bake and recomputes no shoreline, but it isn't "from LevelMaps". It needs w-td's ruling.

#### 2.5 Wind ripples and current

- **Height.** The deterministic f(x,z,t) is the sum of 4 sines agreed with w-engineer [104]:
  - Σ amplitude ≤ 3 cm, wavelengths 1.5-6 m, speeds 0.35-0.6 m/s.
  - Directions are spread out (default 30°, 75°, 140°, 200°) so no tiling lines show.
  - The mesh stays a flat quad. The shader computes **normals analytically from the same f** [104], so the visible ripple pattern is the function the crate samples.
  - Phases are computed C#-side in double precision.
- **Micro ripples** are visual only, not part of f. Two normal-map layers, advected with two-phase flow-map blending so the texture doesn't stretch [102][103][107]:
  - Tiling 4.1 m and 2.3 m per repeat.
  - Strength 0.15-0.30.
  - Advection 0.15-0.35 m/s.
  - Flow cycle 1.5-3 s.
  - Normals fade to flat between 40 m and 200 m.
  - Web: one layer, or both without the second flow phase.
- **Flow direction:** the bank tangent, from the R gradient rotated 90°, with one global downstream sign [103][107].
  - A single uniform vector would run the wrong way around the meander.
  - Where sd is invalid (north of the line), use the downstream direction of the carved channel as a constant.
- **Normal texture:** generated procedurally by an editor script, so there's no licence question.

#### 2.6 Reflections

| Option | Status | Cost at 1080p (estimate) | Verdict |
|---|---|---|---|
| Planar camera | Not in URP [110]; banned by TD §3 | +4-6 ms (re-renders terrain, 101k trees, details) | Rejected |
| SSR | Not in URP 17.6; preview for 6.7 [110][119] | 0.5-1.5 ms with a custom pass | Rejected |
| Sky or probe + Fresnel | Supported [110] | 0.02-0.05 ms | **Pick** |

**Probe recipe:**
- Schlick Fresnel with F0 = 0.02, for water at n ≈ 1.33 [108].
- Desaturate the sky or probe 70-90%, then lerp 50-70% toward an overcast grey of about #8A9096.
- Intensity 0.5-0.8. Perceptual roughness 0.15-0.25.
- Main-light specular 0-0.3 (overcast).
- Optional later: a baked box-projected probe near the bluffs. It costs memory only.

#### 2.7 Refraction

**None, on either platform.** w-td ruled this, and I agree: with T ≤ 2% past 1.5 m, refraction would only show in a fringe of about 0.3 m. Real bank geometry behind the alpha-blended fringe gives the parallax-free "seen through water" read without an Opaque Texture tap [101][112].

#### 3. Item 5: underwater and the camera crossing (research only, per ruling r2)

**Built in Phase 1:** only a flat murk colour on back faces (Cull Off, colour #3A3A24), as a safety net in case the camera ever clips the surface. It costs about 0 ms: back faces are only rasterised when the camera is below the surface.

Research for when diving or a duck-under comes into scope:
1. **State.** The camera is underwater when its Y < f(camX, camZ, t) − 0.03 m, with 0.03 m hysteresis.
2. **Fog and colour.**
   - Switch RenderSettings fog to exponential: density 2.6 (range 2.0-3.0), colour #3A3A24. That's about 1.5 m of visibility, matching the surface absorption.
   - Built-in fog already runs in every URP Lit, terrain and tree shader, including on web.
   - Optionally add a Volume Color Adjustments tint while underwater.
3. **Underside.**
   - The murk colour, about 1.3x brighter inside Snell's window. Snell's window compresses the 180° above-water view to about 97°, with total internal reflection outside it [109].
4. **The crossing frame.**
   - Crest uses a per-pixel waterline mask, a meniscus line and a full-screen underwater pass between transparents and post-processing [115].
   - A cheaper route: keep the eye out of a ±0.08 m band around f, plus the instant fog swap.
   - Upgrade path: the built-in Full Screen Pass Renderer Feature [113b], no custom code.

#### 4. Cost estimate per feature (to be measured in Phase 1)

Estimates, not measurements:
- PC: GTX 1660 / RTX 3060-class GPU at 1080p, worst case with the water covering about 50% of the screen at water level (about 1M pixels).
- Web: an integrated or low-mid GPU in Chrome, WebGL2, 1080p.
- One transparent layer with no overlap. Quads under land are rejected early by the depth test. Twenty quads on one material are 20 draws through the SRP batcher.

| Feature | PC (ms) | Web (ms) |
|---|---|---|
| Base: analytic 4-sine normals, Fresnel, sky/probe tap, per-pixel fog | 0.10-0.15 | 0.20-0.35 |
| WaterMaps tap (absorption, shallows) + scum and drift noise (≈3 taps) | 0.05-0.10 | 0.10-0.20 |
| Micro normals: two-phase flow on PC (4 taps + 2 gradient taps); web 1 phase, 2 taps | 0.10-0.15 | 0.10-0.20 |
| Optional PC soft intersection (Scene Depth, already paid) | 0.03 | n/a |
| Back-face murk safety net | ≈0 | ≈0 |
| **Total** | **≈0.25-0.45** (budget ≤1.5) | **≈0.4-0.75** |
| *Avoided:* Depth or Opaque Texture on Mobile_RPAsset | — | +0.1-0.3 per copy; each copy breaks the native render pass on weak GPUs |
| *Rejected:* refraction | +0.03-0.05 | +copy cost |
| *Rejected:* planar | +4-6 | not viable |
| *Rejected:* SSR | +0.5-1.5 | not viable |

**Cut order if W10 misses:**
1. PC soft intersection.
2. The second flow phase and micro-normal layer.
3. Drift lines.
4. Render scale.

#### 5. Look recipe (summary)

| Parameter | Value (range) | Driven by |
|---|---|---|
| Extinction k R/G/B (1/m) | 2.2 / 2.4 / 3.2 (±25%) | D (G) via L = D / max(N·V, 0.15) |
| Body colour | #4A4A2E (#40402A-#565436) | 1 − T |
| Bed tint | #5E5238 (#54492F-#6A5D42) | T |
| Edge fade | D 0 → 0.10-0.20 m | G |
| Bank scum | sd −0.5..−1.5 peak, 0 by −3.0; 30-50% | R |
| Drift lines | sd −4..−8, centre −5; 0.3-0.6 m wide; 15-25% | R + flow |
| Foam colour | #A89F84, albedo ≤ 0.55 | — |
| North-of-line scum (exception) | D 0.1-0.3 peak, 0 by 0.8 m | G |
| Ripple height f | Σ A ≤ 3 cm, L 1.5-6 m, c 0.35-0.6 m/s, 4 directions | w-engineer globals |
| Micro normals | 4.1 m and 2.3 m tiling, strength 0.15-0.30, 0.15-0.35 m/s, fade 40-200 m | flow from ∇R |
| Reflection | F0 0.02; 70-90% desaturated; 50-70% toward #8A9096; intensity 0.5-0.8; roughness 0.15-0.25 | sky/probe |
| Refraction | none | — |
| Underside | #3A3A24 flat (Cull Off) | — |

Excluded throughout: turquoise, caustics, whitecaps, planar reflections, a seeded bed, refraction, camera depth or opaque textures on web.

### Questions for w-td

1. One global WaterMaps texture rather than per tile, since water lives in the persistent scene. OK? **w-td: APPROVED.** Keep 2 m; drop to a 4 m bake only if W10 flags web memory or size.
2. North-of-line scum from D, as a recorded exception to "foam from LevelMaps"? **w-td: APPROVED as a recorded exception.** It recomputes no shoreline, uses the same guarded bake, and applies only north of northLineZ or where |sd| >= 100.
3. Hand-written URP HLSL (`.shader` plus a `WaterSurface.hlsl` include mirroring the C# f) rather than Shader Graph? **w-td: APPROVED.** Conditions: SRP Batcher-compatible UnityPerMaterial CBUFFER, URP fog include, no inline samplers, at most 2 local keywords, no _Time.

### Sources

All accessed 2026-10-06.

Confidence: **high** = primary or official source, read directly. **medium** = secondary source, or not fetched directly. **low** = inference.

| [n] | Claim it supports | Publisher | Pub date | Accessed | Confidence |
|---|---|---|---|---|---|
| [101] | Depth-based underwater fog over the scene behind water; fake refraction | [Catlike Coding (J. Flick), "Looking Through Water"](https://catlikecoding.com/unity/tutorials/flow/looking-through-water/) | 2018-08-30 | 2026-10-06 | high |
| [102] | Flow-map texture distortion, two-phase blending | [Catlike Coding (J. Flick), "Texture Distortion"](https://catlikecoding.com/unity/tutorials/flow/texture-distortion/) | 2018-05-30 | 2026-10-06 | high |
| [103] | Directional flow aligned to a flow field without stretching | [Catlike Coding (J. Flick), "Directional Flow"](https://catlikecoding.com/unity/tutorials/flow/directional-flow/) | 2018-06-29 | 2026-10-06 | high |
| [104] | Sum-of-sines height and analytic normals from the same function | [NVIDIA, GPU Gems ch. 1 (M. Finch)](https://developer.nvidia.com/gpugems/gpugems/part-i-natural-effects/chapter-1-effective-water-simulation-physical-models) | 2004 | 2026-10-06 | high |
| [105] | Attenuation ≈ 1.7 / Secchi depth (Poole-Atkins) | [Wikipedia, "Secchi disk"](https://en.wikipedia.org/wiki/Secchi_disk) | rev. current at access | 2026-10-06 | medium |
| [106] | Exponential attenuation with path length (Beer-Lambert) | [Wikipedia, "Beer-Lambert law"](https://en.wikipedia.org/wiki/Beer%E2%80%93Lambert_law) | rev. current at access | 2026-10-06 | high |
| [107] | Flow maps for river and water flow in shipped games | [Valve (A. Vlachos), "Water Flow in Portal 2", SIGGRAPH 2010](https://cdn.akamai.steamstatic.com/apps/valve/2010/siggraph2010_vlachos_waterflow.pdf) | 2010 | 2026-10-06 | high |
| [108] | Fresnel reflectance; water normal-incidence F0 ≈ 0.02 at n ≈ 1.33 | [Wikipedia, "Fresnel equations"](https://en.wikipedia.org/wiki/Fresnel_equations) | rev. current at access | 2026-10-06 | high |
| [109] | Snell's window: 180° compressed to about 97°; total internal reflection outside | [Wikipedia, "Snell's window"](https://en.wikipedia.org/wiki/Snell%27s_window) | rev. current at access | 2026-10-06 | medium |
| [110] | URP: no SSR, planar reflections, screen-space refraction or water system; probes and sky supported | [Unity Technologies, Unity 6.6 Manual, "Render pipeline feature comparison"](https://docs.unity3d.com/6000.6/Documentation/Manual/render-pipelines-feature-comparison.html) | 6000.6 docs | 2026-10-06 | high |
| [111] | Scene Depth needs the URP Depth Texture | [Unity Technologies, Shader Graph 17.0, "Scene Depth node"](https://docs.unity3d.com/Packages/com.unity.shadergraph@17.0/manual/Scene-Depth-Node.html) | 17.0.4 docs | 2026-10-06 | high |
| [112] | Scene Color samples the URP Opaque Texture | [Unity Technologies, Shader Graph 17.0, "Scene Color node"](https://docs.unity3d.com/Packages/com.unity.shadergraph@17.0/manual/Scene-Color-Node.html) | 17.0.4 docs | 2026-10-06 | high |
| [113] | URP asset Depth and Opaque Texture settings; MSAA caveat | [Unity Technologies, Unity 6 Manual, "URP asset reference"](https://docs.unity3d.com/6000.0/Documentation/Manual/urp/universalrp-asset.html) | 6000.0 docs | 2026-10-06 | high |
| [113b] | Built-in Full Screen Pass Renderer Feature (underwater upgrade path) | [Unity Technologies, Unity 6 Manual, "Full Screen Pass Renderer Feature"](https://docs.unity3d.com/6000.0/Documentation/Manual/urp/renderer-features/renderer-feature-full-screen-pass.html) | 6000.0 docs | 2026-10-06 | high |
| [114] | Inline samplers don't work on OpenGL ES or GL Core | [Unity Technologies, Unity 6 Manual, "Using sampler states"](https://docs.unity3d.com/6000.0/Documentation/Manual/SL-SamplerStates.html) | 6000.0 docs | 2026-10-06 | high |
| [115] | Underwater meniscus, full-screen underwater pass, portals; precomputed depth (depth cache) technique | [Wave Harmonic, Crest docs, "Underwater"](https://crest.readthedocs.io/en/stable/user/underwater.html) | stable docs | 2026-10-06 | medium (depth-cache point is from w-td's dimension; underwater facts read directly) |
| [116] | WebGL2 texture formats follow GLES 3.0 | [Khronos, WebGL 2.0 Specification](https://registry.khronos.org/webgl/specs/latest/2.0/) | latest | 2026-10-06 | high |
| [117] | GL_RG8 is a GLES 3.0 sized internal format | [Khronos, OpenGL ES 3.0 reference, glTexImage2D](https://registry.khronos.org/OpenGL-Refpages/es3.0/html/glTexImage2D.xhtml) | ES 3.0 refpages | 2026-10-06 | high |
| [118] | CDOM absorbs blue to UV most strongly (brown inland water); pure water absorbs red | [Wikipedia, "Colored dissolved organic matter"](https://en.wikipedia.org/wiki/Colored_dissolved_organic_matter) | rev. current at access | 2026-10-06 | medium |
| [119] | URP SSR is a preview aimed at Unity 6.7, not in 6000.6 | [Unity Discussions, "Preview of Screen Space Reflections for URP"](https://discussions.unity.com/t/preview-of-screen-space-reflections-for-urp/1721494) | 2026-05-29 | 2026-10-06 (via w-web; direct fetch returned 403) | medium |

## D6. Player in water, swim numbers, drowning design (spec items 4 and 7; author w-designer)

Author: w-designer (systems-designer), 2026-10-06. Owner of the merge: w-td.
Build spec that uses these numbers: `_bmad-output/poc/water-swim-spec.md` (r3, approved: `ruling/water-swim-numbers`, `-r2`).

### Source status

I drafted this section without a web tool. **w-web then checked every citation live on 2026-10-06**
(scratchpad `water-web-sources.md`, section "Item 4/7"), and §4a carries w-web's corrections. Sources are numbered [201]–[210];
the source table is at the end of this section. None of the conclusions and none of the §7 numbers changed.

Player-reception statements in §4a are design readings of the games' known reputations, not quotes from these pages.

### 4a. How survival games handle wading and swimming

| Game | Wading | Swimming | Breath / drowning | Boundary over water | Reception | Src |
|---|---|---|---|---|---|---|
| The Long Dark | Falling into water soaks clothing (raises hypothermia/frostbite risk) | **None**: no swimming at all | No drowning. Weak ice cracks on a hidden 5 s timer; falling through means a fade to black, then a safe spot: Warmth 0%, Hypothermia, −10% Condition, all clothing 100% wet | Weak ice "serves as boundary along the ocean" | Accepted: water is a cold hazard, never a route. It shows a survival game can make water a hazard with no swimming | [201] |
| DayZ | Slower with depth | Surface swim; fast swimming costs −5 stamina/s, normal swimming recovers +1/s | Normal swimming can't drown you. Drowning exists only if your head stays under without entering the swim state (e.g. prone in water) for minutes. Clothing and items get soaked | Sea at the map edge leads nowhere | Players swim to escape pursuers; soaked gear is the real cost | [202] |
| Rust | Wetness rises with submersion and lowers body temperature | Surface swim; dive with a tank | Fully submerged at wetness 100: "Drowning", fast health loss and death (status effect) | Ocean beyond the map (edge kill and sharks unverified, M) | Clear but status/HUD-driven | [203] |
| Subnautica | n/a (ocean game) | Core verb | Oxygen used per second rising with depth (1/s shallower than 100 m, 3/s at 100–200 m, 5/s deeper); PDA alerts at 30 and 10; out of air: ~4 s fade plus ~4 s to death. 45 s base is M | **The Void (Crater Edge)**: terrain ends near 4000 m. PDA: "Warning: Entering ecological dead zone. Adding report to databank." One Ghost Leviathan on entry, a second at 40 s, a third at 80 s; they retreat when you return | The standard-bearer for a diegetic soft boundary: you can go, the world warns, then escalates, and backing off ends it | [204] |
| The Forest | Shallows slow you; swimming stows items | Surface swim; can dive (breath meter underwater; a rebreather and tank give 300 s) | Breath shown only underwater | Sharks in deep ocean "where the water is too deep to stand in", visible long before they're a danger; they charge and bite when close | Sharks read naturally as "don't go out there" | [205] |
| Green Hell | Leeches attach while walking through forest or water (−1 sanity each, not fatal) | Swimming; stamina drain is M | 25 s of breath underwater, then ~10 s (at full health) before drowning | Rivers are hazards inside the map (piranhas; the black caiman kills instantly without armour) | Water feels dangerous through fauna and after-effects, not meters | [206] |

Comparators outside the brief (they define the boundary pattern):

| Game | Pattern | Lesson | Src |
|---|---|---|---|
| Red Dead Redemption (2010) | John drowns almost at once above waist depth (stamina vanishes, he sinks); RDR2 deliberately repeats it for John in the epilogue | **Cautionary tale.** Sudden, unexplained drowning reads as arbitrary. Drowning must be staged and legible | [207] |
| Zelda: Breath of the Wild | When the stamina wheel empties while swimming, Link sinks, loses one heart and returns to land | Drowning as a **soft fail with a shore respawn** keeps exploring safe to try | [208] |
| Sea of Thieves | Devil's Shroud at the edge: sea and sky turn red, the hull takes repeated damage, and the ship sinks and respawns. Loading-screen line: "Beyond the edge you may stray, a fateful end if you stay..." | Out-of-bounds made **diegetic and escalating**, not a wall | [209] |
| GTA V | Sharks in deep ocean | Fauna as the boundary for an open sea (M) | [210] |

**Takeaways.**
1. Water is a cost, not a route. Every accepted example makes water expensive: soaking and cold ([201], [203]), stamina ([202]), fauna ([205], [206]).
2. Drowning must be staged and survivable if you turn back ([207] vs [204], [208]). [204]'s leviathans retreat when you return, which is the same turn-back rule our 4b design uses.
3. The boundary warns before it kills, and escalates ([204]: one leviathan, then more at 40 s and 80 s; [209]; [201]'s weak-ice ocean edge). Our backstop stays the hard floor, and drowning is the readable layer on top.
4. Fail forward, on theme: a drowned player **washes ashore** on the nearest bank. That's [208]'s return to land, and [201]'s "fade, then a safe spot", in our premise.
5. No meters. [203] and [204] lean on HUD meters; we can't. [201], [205] and [206] show hazard can read without them.

### 4b. Drowning as a soft world boundary (CONCRETE DESIGN; research only, NOT built)

**Why it matters here.** L3 makes everything outside the polygon water (5 whole tiles; terrain X −2087..2009, Z −2492..2628).
South of the line there is **no far bank**. Over water, drowning is a boundary in every direction, not only at the north line.
Until it is built, the backstop and `WorldBoundsClamp` are the boundary (W8).

**Inputs (already built):** signed shore distance from `BellsBendLevelMaps.ShoreDistance` (2 m grid, ±127 m, negative in water,
measured to the playable-polygon edge); `MapConfig.northLineZ`; `BarrierBuilder.LineOffset` (20); swim speed 1.8 m/s (§7).
The real Cumberland at Bells Bend is roughly 150-250 m wide, so ~75-125 game m after the 1:2 squeeze. Mid-channel is ≤ ~60 m from a bank.

**Model: one hidden pull value P in [0, 1].** There is no bar. P only drives cues, and its inputs are position and time, so it is deterministic and server-checkable.

| Zone | Where | dP/dt |
|---|---|---|
| Safe | ≤ **40 m** out from shore | −1/10 s (full recovery in 10 s) |
| Warning | 40-**70 m** out | +1/40 s |
| Drowning | > 70 m out | +1/8 s |
| North water | in water north of `northLineZ − LineOffset` (past the fence line) | +1/8 s at any distance |
| Grounded (wading or standing) | anywhere | −1/5 s |

**Worked numbers at 1.8 m/s:**
- Straight out from 40 m: P = 0.42 at 70 m (16.7 s); drowns at ~78 m.
- Turn back at 60 m (P 0.28): reach safety with P 0.56. Turn back at 70 m (P 0.42): reach safety with P **0.83**, alive but struggling.
- **Point of no return ≈ 72 m out. Anyone who turns back anywhere in the warning band survives.**
- North water: P fills after 8 s, about 14 m past the fence line and 6 m short of the north line. The clamp remains the authoritative floor (teleports, Rigidbodies, multiplayer).

**Cue timeline (no HUD bars):**

| Stage | Trigger | Cues |
|---|---|---|
| 0 Uneasy | enter the warning band | River sound swells; the character's breathing becomes audible; stroke cadence slows; haze over open water thickens (TA fog) |
| 1 Tiring | P ≥ 0.33 | Ragged breathing; speed ×0.85; water slaps the lens every ~6 s (0.3 s splash, brief low-pass) |
| 2 Struggling | P ≥ 0.66 | Eye above water drops 0.35 → 0.10 m; slaps every ~3 s; speed ×0.7; 20% vignette, 30% desaturation; heartbeat |
| 3 Going under | P ≥ 1 | Input lost; sink over 3 s into murk; muffled audio; black at 5 s |
| Wash ashore | after the fade | Respawn lying on the nearest **in-bounds low-bank station** (L3c stations, south of the line), facing inland. The cost (time, soaked gear, item loss) waits for the death/inventory systems |

**Stamina and breath, later.** Keep P place-based. Don't feed a land stamina system into it, so the boundary stays deterministic.
If diving ever comes in scope: hidden breath 20 s, cues from 10 s (tight-chest audio, darkening), forced surfacing, no damage before 20 s.
The deferred duck-under (≤ 2.0 s, see the swim spec) needs no breath system.

**Later tunables:** `safeDistance` 40, `drownDistance` 70, `warnRate` 1/40, `drownRate` 1/8, `recoverRate` 1/10, `groundRecoverRate` 1/5,
stage thresholds 0.33 / 0.66 / 1.0, `sinkSeconds` 3, `fadeSeconds` 5.
**Later acceptance:** a turn-back sweep at every distance ≤ 70 m never drowns; with the clamp disabled in a test build, 0 swims reach the north line; every stage cue fires in order.

### 7. Recommended swim numbers (BUILT NOW; approved: ruling/water-swim-numbers and -r2)

Baseline, live controller (`PlayerController.cs`, World.unity, `controller-tuning.md`): walk 5, sprint 8 m/s, accel 20, decel 25,
jump 1.2 m, gravity −20; CC height 1.8, radius 0.4, eye 1.65, stepOffset 0.4, slopeLimit 45.
Bank (design brief c, rev 14): lip W + 0.3 with a 0.31 m riser; 18° shelf to the bed at W − 2 (~7.1 m out), so depth = 0.325·s − 0.3.
Depth = water surface − ground under the player. Per w-td's tech pick, the surface is near-flat (vertical-only displacement of a few cm,
no Gerstner horizontal shift) and one C# function returns the exact height and a current vector at any (x, z, t). Swim/wade logic queries
that height; the current vector is not applied to the player (no push, `ruling/water-swim-numbers-r2`).

| Parameter | Value | Why |
|---|---|---|
| Wade starts | depth > 0.05 m | Ignore ripple at the lip |
| Wade speed multiplier (on walk) | 0.9 @ 0.05 m · 0.75 @ 0.3 (shin) · 0.6 @ 0.6 (knee) · 0.5 @ 1.0 (waist) · 0.4 @ 1.35 (chest), linear | Heavy and clearly slower: 4.5 → 2.0 m/s |
| Sprint in water | below 0.3 m only (× curve); sprint-stroke while swimming | You can't sprint chest-deep |
| Jump in water | below 0.6 m only; never while swimming | |
| Wade accel / decel | 8 / 10 m/s² | Land is 20 / 25 |
| **Swim enters / exits** | depth **1.35 / 1.15 m** | Chest depth; 0.2 m hysteresis. Not at the brief's W − 2: at 2 m deep the eyes are 0.35 m under water |
| Eye above water while swimming | **0.35 m** (float depth 1.30) | Camera never clips the surface (near plane 0.1) |
| **Swim speed** | **1.8 m/s** (36% of walk) | Below the deepest wade (2.0). With normal input, speed only falls with depth (stroke means). Sprint-stroke 2.4 beats chest-deep wading, which the director accepted (ruling/water-swim-numbers note a) |
| Sprint-stroke | **2.4 m/s** | Still under half of walk; no stamina |
| Back / strafe swim | ×0.6 | |
| Stroke surge | ±20% at 0.9 Hz | The laboured read without a HUD |
| Swim accel / decel; overspeed decel | 3 / 2; 6 m/s² | A sprint-jump in (8 m/s) bleeds to swim speed in ~1 s, ~5 m |
| Current push | none | Visual only (one vector is wrong on a bend) |
| Duck-under | deferred (not built in the POC) | Allowed, not required; less W8 surface |
| Exit on low banks | no special climb: swim ends ~4.5 m out, wade up the 18° shelf, 0.31 m lip < stepOffset 0.4 | Ledge assist only if W6 snags, capped at 0.45 m |
| Phase 1 tuning band (no re-approval) | swim 1.5-2.2, sprint-stroke ≤ 2.8, chest wade multiplier 0.35-0.5 | |

Real-world anchors (general physiology, not a cited study): walking ~1.4 m/s; recreational swimming ~0.5-1.0 m/s; waist-deep
walking roughly half of land speed. Our walk is 3.6× real, so a proportional swim would be ~2-3 m/s. We go lower on purpose (director: hazard, not shortcut).

### Sources

| [n] | Claim it supports | Publisher | Pub date | Accessed | Confidence |
|---|---|---|---|---|---|
| [201] | TLD has no swimming; falling through weak ice = fade, safe spot, Warmth 0%, Hypothermia, −10% Condition, clothing 100% wet; weak ice is the ocean boundary; wet clothing raises hypothermia risk | [The Long Dark Wiki (Fandom): Weak Ice](https://thelongdark.fandom.com/wiki/Weak_Ice); [Clothing](https://thelongdark.fandom.com/wiki/Clothing) | living wiki, undated | 2026-10-06 (w-web) | high |
| [202] | DayZ fast swimming −5 stamina/s, swimming recovery +1/s; normal swimming can't drown you, but drowning exists with the head submerged outside the swim state | [DayZ Wiki (Fandom): Stamina](https://dayz.fandom.com/wiki/Stamina); [DayZ Forums: "Drowning is real"](https://forums.dayz.com/topic/176790-drowning-is-real/); [Steam Community discussion](https://steamcommunity.com/app/221100/discussions/0/144513524086083205/) | wiki living; forum threads undated in check | 2026-10-06 (w-web) | high (stamina); medium (drowning detail, forum sources) |
| [203] | Rust wetness from submersion lowers body temperature; Drowning status when fully submerged at wetness 100; diving tank | [Rust Wiki (Fandom): Status Effects](https://rust.fandom.com/wiki/Status_Effects); [Diving Tank](https://rust.fandom.com/wiki/Diving_Tank) | living wiki, undated | 2026-10-06 (w-web) | high; ocean-edge kill and sharks medium (not on page) |
| [204] | Subnautica Void (Crater Edge): terrain ends; PDA "Entering ecological dead zone"; Ghost Leviathans at 0/40/80 s, retreat on return; oxygen rates by depth, alerts, ~8 s to death out of air | [Subnautica Wiki (Fandom): Crater Edge](https://subnautica.fandom.com/wiki/Crater_Edge); [Oxygen](https://subnautica.fandom.com/wiki/Oxygen) | living wiki, undated | 2026-10-06 (w-web) | high; 45 s base oxygen medium |
| [205] | The Forest sharks in water too deep to stand in, visible before the danger; swimming stows items; underwater breath meter, 300 s with a rebreather | [The Forest Wiki (Fandom): Shark](https://theforest.fandom.com/wiki/Shark); [Swimming](https://theforest.fandom.com/wiki/Swimming) | living wiki, undated | 2026-10-06 (w-web) | high |
| [206] | Green Hell 25 s breath + ~10 s to drowning; piranhas, black caiman; leeches picked up in water (−1 sanity) | [Green Hell Wiki (Fandom): Swimming](https://greenhell.fandom.com/wiki/Swimming); [Leeches](https://greenhell.fandom.com/wiki/Leeches) | living wiki, undated | 2026-10-06 (w-web) | high; stamina drain while swimming medium |
| [207] | RDR1: John Marston drowns almost at once above waist depth; repeated deliberately in RDR2's epilogue | [rdr2.org: Can John Marston swim?](https://www.rdr2.org/news/red-dead-redemption-john-marston-swim-actually/); [Wikipedia: John Marston](https://en.wikipedia.org/wiki/John_Marston) | article undated in check; Wikipedia living | 2026-10-06 (w-web) | high |
| [208] | BotW: empty stamina wheel while swimming → sink, lose one heart, return to land | [Zelda Wiki (Fandom): Stamina Wheel](https://zelda.fandom.com/wiki/Stamina_Wheel) | living wiki, undated | 2026-10-06 (w-web) | high |
| [209] | Sea of Thieves Devil's Shroud: red sea and sky, repeated hull damage, sink and respawn; loading-screen warning line | [Sea of Thieves Wiki (Fandom): Devil's Shroud](https://seaofthieves.fandom.com/wiki/Devil%27s_Shroud) | living wiki, undated | 2026-10-06 (w-web) | high |
| [210] | GTA V has sharks in deep ocean | [GTA Wiki (Fandom): Sharks (animal)](https://gta.fandom.com/wiki/Sharks_(animal)) | living wiki, undated | URL found 2026-10-06 (w-web), page content not fetched | medium |

Uncited by design: player-reception lines in §4a (design readings of reputation), the real-world speed anchors in §7 (general
physiology, not a study), and the Cumberland width estimate in §4b (order of magnitude only; it doesn't drive any number).

## Cross-dimension insights

1. **The web asset settles the look question.** D2 found that Mobile_RPAsset has no depth texture and that Scene Depth fails silently [27][28]. D5's murk is entirely depth-driven. Taken alone, each finding pushes toward "turn the depth texture on for web". Taken together, they point to the baked WaterMaps instead. That gives identical murk on both platforms with zero extra passes, and the guarded bake (W3) is a stronger correctness check than a runtime copy that can silently go grey.
2. **The opacity brief makes refraction and SSR worthless.** w-artist's numbers (T about 2% at 1.5 m) mean the expensive features in the D4 table only show in a 0.3 m fringe. The greenlight's opacity rule removes most of the budget risk.
3. **Choosing a river over a sea makes determinism nearly free.** With centimetre ripples and no swell, vertical-only sines on flat quads are visually enough (D5 §2.5), exactly invertible (D3), and stay within W9's tolerance by construction (D4). The sea-style options (Gerstner, FFT, Crest) only bring the inversion and readback problems that shipped code has skipped [6][45] or solved with GPU queries [34]. UE ships separate CPU wave queries for the same reason [46].
4. **The drowning boundary and the motion function share one input model.** D6's pull P depends only on position and time, and f(x, z, t) depends only on position and time. Both can therefore be checked on a server without any client state. That's the premise of the multiplayer note in Phase 1.
5. **Package age is now a hard filter, not a soft risk.** Render Graph-only URP (6.4+) [18][19] turns "last updated for URP 12" into "does not run": BoatAttack's master is on 2020.3 / URP 10 [7]. Only packages without custom passes (Uber [9]) or ours survive.

## Contrary evidence

The red-team pass was off (headless run; logged as an assumption). These are the strongest counter-arguments found in the material:
- **Owning a shader is a maintenance cost that a package spreads across users.** Uber is active, with 8 releases and a push the day before this run [9]. A custom shader has to follow URP include changes alone. Mitigation: one shader of about 400 lines, no render passes, and the hedge described in the summary.
- **Budgets are estimates.** No source gave millisecond costs (D4). The pick could still miss W10 on a weak web GPU, so the cut order is pre-agreed.
- **Flat quads can't show swell.** If boats or floods later need visible waves, a gridded mesh is required (D3 note). That's a forward cost, not a POC one.

## Recommendations

| # | Recommendation | Confidence basis | Feeds |
|---|---|---|---|
| R1 | **Build A**: a hand-written URP HLSL water shader with a WaterSurface.hlsl include mirroring WaterMotion.Offset, flat per-tile quads, a baked WaterMaps texture (RG8: shore distance, bed depth), sky or probe + Fresnel, no refraction or SSR, and no planar camera. Reference reading: Catlike flow [41], GPU Gems [33], Cyanilux breakdown [17] | high on the gates and platform facts [18][19][20][27]; costs are estimates | W2, W5, W9, W10; w-artist, w-level, w-engineer |
| R2 | **Leave Mobile_RPAsset unchanged** (depth and opaque off on web). PC may use its existing scene depth only for optional soft intersection | high [26][27][28] | W10; w-tools (no ProjectSettings change for water) |
| R3 | **f(x, z, t) is vertical sines only, with at most 4 waves and a summed amplitude of 3 cm or less. Per-wave temporal phase is computed in double on the CPU and pushed as globals; nothing reads _Time.** Time sits behind one clock seam | high [33][38]; precision is arithmetic | W9; future server physics |
| R4 | **The W3 guard throws before any scene change**: lake-mask hash 8f8a637cd4657d82, the north-line check, and the baked WaterMaps stamped with the hash | high (TD note §3 checklist) | W3; w-level |
| R5 | **Look recipe of record = D5 §5. Swim numbers of record = D6 §7** (already approved: ruling/water-swim-numbers, -r2). **Drowning design of record (later) = D6 §4b** | design ranges sourced in D5/D6; game-director approval pending for the look | greenlight/water-phase0 |
| R6 | **Re-run the R2 walk (W10) with water on, in both builds, before any look polish.** If PC goes over +1.0 ms, apply the cut order | n/a (measurement) | W10; qa |

## Greenlight traceability (greenlight/water notes 1-6)

| Note | What it asks | Answered by |
|---|---|---|
| 1 Look | Silty olive river, opaque within 1-2 m, bed hidden by absorption, no turquoise, caustics or whitecaps, dull overcast reflections, scum and drift-line foam | R1, R5; D5 §2.2-2.7 and §5 (k 2.2/2.4/3.2 per m, T about 2% at 1.5 m, desaturated probe + Fresnel, foam #A89F84); D4 table (no planar, SSR or refraction) |
| 2 Motion | Slow downstream current, near-flat with wind ripples, one deterministic f(x, z, t) | R3; D3 ruling (at most 4 vertical sines, 3 cm or less, shared C#/HLSL, one clock, phase in double); D5 §2.5 (flow along the bank tangent, two-phase micro-normals, visual current) |
| 3 Swim feel | Wading heavy and slower, swimming slow and laboured, surface only, no HUD bars | R5; D6 §7 (approved ruling/water-swim-numbers, -r2) |
| 4 Exits | Climbing out on any low bank is reliable; no shelf snag | D6 §7 exit row (18 deg shelf, 0.31 m lip < stepOffset 0.4, ledge assist only if W6 snags); water has no collider (D4) |
| 5 Drowning design | A concrete drowning-as-boundary design, research only | D6 §4b (40/70 m, rates, cue timeline, wash ashore); not built |
| 6 W7 | Record W7 as dropped on purpose or fill it | Not a research item. w-qa and w-producer own this in the verdict |

## Open questions

- **Measured cost** of the water pass on the reference PC and in a WebGL2 browser. Answered by W10 (R2 walk + web FPS JSON).
- **GPU sin() precision** at arguments around 1e4 rad (world corners) on WebGL2 and DX11. Answered by the W9 readback test sampling near the corners. Fallback: compute k·(x - tileOrigin) with tile-local coordinates.
- **WebGPU** (opt-in in 6000.6 [21]): water was not tested there. It's out of scope under the ADR; re-check if the web target moves.
- **The Shader Graph sample water's licence and its availability in SG 17.6** [16] (relevant only if A borrows from it).
- **The exact URP compatibility define name** (URP_ vs UPM_) in the 6.3 guide [18]. Not decision-relevant.

## Source appendix

These are the w-td dimensions. D5 sources [101]-[119] and D6 sources [201]-[210] are in their sections' tables above.

| [n] | Claim/finding it supports | Publisher | Pub date | Accessed | Confidence |
|---|---|---|---|---|---|
| [1] | Crest repo MIT, not archived, push 2026-06-18, tag 4.23.0 | [GitHub API: wave-harmonic/crest](https://api.github.com/repos/wave-harmonic/crest) | live 2026-10-06 | 2026-10-06 | high |
| [2] | Crest 4 on GitHub is built-in renderer; URP/HDRP editions and Crest 5 are paid Asset Store | [wave-harmonic/crest README](https://github.com/wave-harmonic/crest) | live | 2026-10-06 | medium |
| [3] | boat-attack-water: Unity Companion License for Unity-dependent projects; 2.0.0-preview.5, Unity 2021.3, URP 12.1.10; push 2024-10-17 | [Unity-Technologies/boat-attack-water LICENSE.md](https://raw.githubusercontent.com/Unity-Technologies/boat-attack-water/main/LICENSE.md) | n/d (live) | 2026-10-06 | high |
| [4] | Passes implement Execute and RecordRenderGraph; reflection modes Cubemap/Probe/Planar(default)/SSR; ComputeBuffer disabled on WebGL | [Unity-Technologies/boat-attack-water source](https://github.com/Unity-Technologies/boat-attack-water) | n/d (live) | 2026-10-06 | high |
| [5] | Open issue #25: water shader error in Unity 6 URP (6000.0.31f1, DirectBDRF) | [boat-attack-water issue #25](https://github.com/Unity-Technologies/boat-attack-water/issues/25) | 2024-12-17 | 2026-10-06 | high |
| [6] | CPU Gerstner height evaluated at query xz without inversion; time = Time.time | [boat-attack-water GerstnerWaves.cs](https://raw.githubusercontent.com/Unity-Technologies/boat-attack-water/main/Runtime/Modifiers/GerstnerWaves.cs) | n/d (live) | 2026-10-06 | high |
| [7] | BoatAttack master targets Unity 2020.3.23f1 / URP 10.7 | [Unity-Technologies/BoatAttack ProjectVersion](https://raw.githubusercontent.com/Unity-Technologies/BoatAttack/master/ProjectSettings/ProjectVersion.txt) | n/d | 2026-10-06 | medium |
| [8] | Unity Companion License v1.4 terms | [Unity: Unity Companion License](https://unity.com/legal/licenses/unity-companion-license) | v1.4 | 2026-10-06 | high |
| [9] | Uber-Stylized-Water: MIT, 1 contributor, 8 releases, push 2026-10-05; no render passes; planar opt-in second camera; Unity 6000.0.30+ | [MatrixRex/Uber-Stylized-Water](https://github.com/MatrixRex/Uber-Stylized-Water) | 2026-10-05 | 2026-10-06 | high |
| [10] | Uber requires Depth Texture (water can be invisible without it); Opaque only for refraction | [Uber getting-started.md](https://raw.githubusercontent.com/MatrixRex/Uber-Stylized-Water/main/docs/usage-guide/getting-started.md) | 2026 | 2026-10-06 | high |
| [11] | Uber has no wave-height query (maintainer, issue #11) | [Uber issue #11](https://github.com/MatrixRex/Uber-Stylized-Water/issues/11) | 2025-11-21 | 2026-10-06 | high |
| [12] | Unity6_WaterShader has no licence file | [YuanYuSQ/Unity6_WaterShader](https://github.com/YuanYuSQ/Unity6_WaterShader) | 2026-09-23 | 2026-10-06 | high |
| [13] | HDRP Water System is HDRP-only, compute-based | [Unity HDRP Water System docs](https://docs.unity3d.com/Packages/com.unity.render-pipelines.high-definition@14.0/manual/WaterSystem-Overview.html) | c. 2023 | 2026-10-06 | medium |
| [14] | KWS Water URP is a paid Asset Store product | [Unity Asset Store: KWS Water System URP](https://assetstore.unity.com/packages/tools/particles-effects/kws-water-system-urp-rendering-203144) | n/d | 2026-10-06 | medium |
| [15] | Stylized Water 2 paid; planar doubles scene cost; feature cost ranking; WebGL2 support | [Staggart Creations: SWS2 docs](https://staggart.xyz/unity/stylized-water-2/sws-2-docs/?section=performance-guidelines-3) | undated | 2026-10-06 | medium |
| [16] | Shader Graph Production Ready water samples (lake, stream, falls): normals, flow, depth fog, refraction | [Unity: Shader Graph Production Ready Water](https://docs.unity3d.com/Packages/com.unity.shadergraph@14.0/manual/Shader-Graph-Sample-Production-Ready-Water.html) | SG 14 docs | 2026-10-06 | medium |
| [17] | Water shader breakdown in Shader Graph (depth, scene colour, caustics) | [Cyanilux: Water Shader Breakdown](https://www.cyanilux.com/tutorials/water-shader-breakdown/) | undated | 2026-10-06 | medium |
| [18] | URP Compatibility Mode removed in 6.3 (define restores temporarily) | [Unity Manual 6000.3: Upgrade to 6.3](https://docs.unity3d.com/6000.3/Documentation/Manual/UpgradeGuideUnity63.html) | 6000.3 docs | 2026-10-06 | high |
| [19] | Compatibility Mode fully removed in 6.4 incl. define | [Unity Manual: Upgrade to 6.4](https://docs.unity3d.com/6000.5/Documentation/Manual/UpgradeGuideUnity64.html) | 6000.5 docs | 2026-10-06 | high |
| [20] | URP SSR preview targeted 6.7, alpha 6000.6.0a7+; low-res SSR on water looks obvious | [Unity Discussions: Preview of SSR for URP](https://discussions.unity.com/t/preview-of-screen-space-reflections-for-urp/1721494) | 2026-05-29 | 2026-10-06 | medium-high |
| [21] | WebGPU out of experimental in 6000.6, opt-in, enables GRD/compute | [Unity Discussions: WebGPU out of experimental in 6.6](https://discussions.unity.com/t/webgpu-out-of-experimental-in-unity-6-6/1734694) | 2026-08-24 | 2026-10-06 | high |
| [22] | Second source on 6.6 WebGPU production status | [TechNetBooks: Unity 6.6 arrives](https://www.technetbooks.com/2026/09/unity-66-arrives-with-coreclr.html) | 2026-09 | 2026-10-06 | medium |
| [23] | GPU Resident Drawer needs compute, unavailable on WebGL; SRP Batcher works | [Unity Manual 6000.0: GPU Resident Drawer](https://docs.unity3d.com/6000.0/Documentation/Manual/urp/gpu-resident-drawer.html) | 6000.0 docs | 2026-10-06 | medium-high |
| [24] | FetchSceneDepth input attachment DX12/Vulkan only | [Unity Manual 6000.6: read depth input attachment](https://docs.unity3d.com/6000.6/Documentation/Manual/urp/read-depth-input-attachment.html) | 6000.6 docs | 2026-10-06 | high |
| [25] | Depth Texture Mode options: After Opaques / After Transparents / Force Prepass | [Unity Manual 6000.3: Universal Renderer](https://docs.unity3d.com/6000.3/Documentation/Manual/urp/urp-universal-renderer.html) | 6000.3 docs | 2026-10-06 | medium |
| [26] | cameraDepthTexture/cameraOpaqueTexture exist only when enabled in the URP asset | [Unity Manual 6000.3: Render Graph frame data reference](https://docs.unity3d.com/6000.3/Documentation/Manual/urp/render-graph-frame-data-reference.html) | 6000.3 docs | 2026-10-06 | medium |
| [27] | Scene Depth node returns 0.5 when Depth Texture is disabled; Transparent surface needed | [Shader Graph 17.0: Scene Depth node](https://docs.unity3d.com/Packages/com.unity.shadergraph@17.0/manual/Scene-Depth-Node.html) | SG 17.0 docs | 2026-10-06 | medium-high |
| [28] | Depth Texture is per URP asset / quality level (mobile asset off broke Scene Depth) | [Unity Discussions: Scene Depth not working on mobile](https://discussions.unity.com/t/urp-scene-depth-node-in-shadergraph-not-working-on-mobile/878659) | c. 2022 | 2026-10-06 | medium |
| [29] | Disable Depth/Opaque Texture unless a shader samples them | [Unity Manual 6000.3: configure for better performance](https://docs.unity3d.com/6000.3/Documentation/Manual/urp/configure-for-better-performance.html) | 6000.3 docs | 2026-10-06 | high |
| [30] | GLES depth copy only reliable without MSAA | [Unity Discussions: why URP bans depth copy for GLES](https://discussions.unity.com/threads/why-urp-bans-depth-copy-for-all-gles.1320279/) | c. 2022 | 2026-10-06 | medium |
| [31] | Crest depth cache: baked or realtime top-down depth heightfield; baked has no per-frame cost | [Crest docs: Shallows and shorelines](https://crest.readthedocs.io/en/stable/user/shallows-and-shorelines.html) | stable | 2026-10-06 | medium-high |
| [32] | Crest LOD rings draw-call heavy; underwater renders water twice | [Crest docs: Performance guide](https://crest.readthedocs.io/en/latest/user/performance-guide.html) | current | 2026-10-06 | high |
| [33] | Sum of sines / Gerstner formulas; analytic normals; steepness limit | [NVIDIA GPU Gems ch. 1](https://developer.nvidia.com/gpugems/gpugems/part-i-natural-effects/chapter-1-effective-water-simulation-physical-models) | 2004 (canonical) | 2026-10-06 | high |
| [34] | Crest async GPU queries; per-point buoyancy queries | [Crest docs: Collision shape and buoyancy](https://crest.readthedocs.io/en/stable/user/collision-shape-and-buoyancy-physics.html) | stable | 2026-10-06 | high |
| [35] | Crest networked time = server time offset; custom ITimeProvider; baked FFT for headless | [Crest docs: Time providers](https://crest.readthedocs.io/en/stable/user/time-providers.html) | stable | 2026-10-06 | high |
| [36] | Crest: animated waves deterministic; dynamic waves not synchronised | [Crest docs: FAQ](https://crest.readthedocs.io/en/stable/user/faq.html) | stable | 2026-10-06 | high |
| [37] | CPU wave replication diverged when fed displaced positions | [Unity Discussions: calculating waves in C#](https://discussions.unity.com/t/calculating-waves-in-c-to-get-water-height-resolved/899499) | 2022-11-09 | 2026-10-06 | medium |
| [38] | Newton inversion to rest point; steepness 0 reproduces vertical-only exactly | [hideoutgames/BabylonSlate PR 807](https://github.com/hideoutgames/BabylonSlate/pull/807) | n/d | 2026-10-06 | medium |
| [39] | Sync server time and feed it to material and buoyancy | [Epic forums: 4.26 water and replication](https://forums.unrealengine.com/t/4-26-new-water-system-and-replication/158657) | c. 2021 | 2026-10-06 | medium |
| [40] | Sea of Thieves: wave params + server time + shared seed | [Sea of Thieves forum topic 33278](https://www.seaofthieves.com/community/forums/topic/33278/how-does-the-game-client-server-synchronise-the-state-of-the-sea-across-multiple-clients) | n/d | 2026-10-06 | low |
| [41] | Two-phase flow-map distortion; look not simulation | [Catlike Coding: Texture Distortion](https://catlikecoding.com/unity/tutorials/flow/texture-distortion/) | 2018 (canonical) | 2026-10-06 | high |
| [42] | Float shader time precision loss; frac/wrap fix | [Unity forum: multiplying _Time problem](https://forum.unity.com/threads/multiplying-_time-problem.529325/) | n/d | 2026-10-06 | low-medium |
| [43] | WebGL texture compression: DXT desktop, ASTC/ETC2 mobile | [Unity Manual 2021.3: WebGL texture compression](https://docs.unity3d.com/2021.3/Documentation/Manual/webgl-texture-compression.html) | 2021.3 docs | 2026-10-06 | medium |
| [44] | WebGL graphics optimisation: variant stripping, always-included shaders | [Unity Manual 2022.3: Web graphics optimisation](https://docs.unity3d.com/2022.3/Documentation/Manual/web-optimization-graphics.html) | 2022.3 docs | 2026-10-06 | high |
| [45] | Gerstner buoyancy sampled directly at (x,z), no inversion | [Sea Creature Game blog](https://www.seacreaturegame.com/blog/gerstner-waves-with-buoyancy-godot) | 2024-08-08 | 2026-10-06 | medium |
| [46] | UE Water exposes CPU wave queries (GetWaveHeightAtPosition / GetWaveInfoAtPosition) | [Epic docs: GetWaveHeightAtPosition](https://dev.epicgames.com/documentation/en-us/unreal-engine/API/Plugins/Water/UWaterWavesAssetReference/GetWaveHeightAtPosition) | n/d | 2026-10-06 | low-medium |
| [47] | Uber issues: variant explosion on 6.2, compile failure on 6000.1.21f, Quest untested | [Uber-Stylized-Water issues](https://github.com/MatrixRex/Uber-Stylized-Water/issues?q=is%3Aissue) | 2025 | 2026-10-06 | high |
| [48] | URP 3D Sample (Oasis) has Shader Graph water; Hub template | [Unity: URP 3D Sample](https://unity.com/demos/urp-3d-sample) | 2023-11 | 2026-10-06 | medium |

## Staleness map

The table below was computed by recon_kit staleness (windows: version-compat 1 month, ecosystem 6, patterns 24; today 2026-10-06; raw output in staleness.json).

| Claim | Class | Pub | Re-check by |
|---|---|---|---|
| boat-attack-water open Unity 6 compile error | ecosystem | 2024-12 | 2025-06-01 (past; issue state read open on 2026-10-06) |
| URP SSR is preview only (6.7 target) | version-compat | 2026-05 | 2026-06-01 (past; thread read 2026-10-06) |
| WebGPU out of experimental in 6000.6 | version-compat | 2026-08 | 2026-09-01 (past; read 2026-10-06) |
| Render Graph only on 6.4+ | version-compat | 2026-10 | 2026-11-01 |
| Scene Depth returns 0.5 when Depth Texture off | version-compat | 2026-10 | 2026-11-01 |
| Server sync = same function + server time | patterns | 2025 | 2027-01-01 |
| Crest URP paid; Uber MIT, no height query | ecosystem | 2026-10 | 2027-04-01 |
| Vertical-only sines exact without inversion | patterns | 2026 | 2028-01-01 |

The three "past" rows are dated by their source's publication. Their status was re-read live on the access date, so they hold as of 2026-10-06.

**Earliest forward re-check: 2026-11-01** (the Unity version facts). Re-check the SSR and WebGPU rows at every Unity minor upgrade: URP SSR shipping in 6.7 would reopen the reflection row of the D4 table. Per the select-shape rule, refresh this selection before acting on it after 2027-04.
