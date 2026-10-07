# Section: items 2 (look) and 5 (underwater)

Author: w-artist (technical-artist). Revision 2, 2026-10-06. Phase 0 research only: nothing built.

Bound by:
- greenlight notes 1-2 (`studio:decisions` `greenlight/water`)
- TD note §3: no planar camera; ≤1.5 ms over the R2 median of 6.68 ms; p99 < 16.6 ms
- w-director ruling `water-swim-numbers-r2`: no duck-under, the camera never goes underwater
- w-td's direction: baked depth and shore texture; no camera depth or opaque texture; no refraction

Source numbers [101]..[119] are listed in the table at the end. [R1]..[R5] are repo facts checked directly.

## 0. Project facts that decide the design

| # | Fact | Consequence |
|---|---|---|
| R1 | `Assets/Settings/PC_RPAsset.asset` (Standalone, quality 1): Forward+, Depth Texture ON, Opaque Texture ON (2x downsample), MSAA off, HDR on, SSAO. `Mobile_RPAsset.asset` (WebGL, quality 0): Forward, **Depth Texture OFF, Opaque Texture OFF** (`QualitySettings.asset` m_PerPlatformDefaultQuality) | Scene Depth and Scene Color need those textures [111][112][113]. With the baked approach below, the water needs neither, so **Mobile_RPAsset stays unchanged** and web gets the same look as PC. |
| R2 | `LevelMaps` shore distance is the road grid's B channel (128 + signed m), **quantised to 1 m**, 2 m cells, RGB24, point-filtered and readable (`BellsBendLevelMaps.cs:7,50`). It measures the distance to `v.innerBank`, negated outside the polygon (`BellsBendLevel.cs:47-48`) | It is the foam source (W5). The real waterline sits a mean 0.51 m (p95 0.75, max 1.25) outside the inner bank (td-input, L1), so the waterline is near sd ≈ −0.5. |
| R3 | North of the line, the vista land counts as "outside the polygon", so its shore distance is negative (`BellsBendGround.cs:133`). The carved river banks there are not on innerBank | LevelMaps has **no valid foam data on the north-of-line banks**. Fallback in §2.4. |
| R4 | Inline sampler states don't work when targeting OpenGL ES or GL Core [114] (the WebGL2 path) | The shader can't bilinear-sample the point-filtered LevelMaps texture on web. The bake has to emit **its own bilinear texture**. |
| R5 | `World.unity`: linear fog 120-520 m, colour (0.72, 0.80, 0.88). Reflections come from the **default procedural skybox** at 128 px | A raw sky reflection is blue, which pushes the water toward turquoise (forbidden). The shader must desaturate and tint it. |

## 1. Rendering constraints (Unity 6000.6.4f1, URP 17.6)

- URP has **no SSR, no planar reflections, no screen-space refraction and no water system**. The Unity 6.6 feature comparison lists all four as HDRP-only [110].
- URP SSR exists only as a preview aimed at Unity 6.7 [119], so it isn't available here.
- Reflection probes (baked or real-time, box projection, blending) and the skybox are supported in URP [110].
- R8 and RG8 are core texture formats in WebGL2 / GLES3 [116][117].

## 2. Item 2: the look, built on the baked depth and shore texture

### 2.1 The bake (w-td's direction; I agree)

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

### 2.2 Colour and absorption from bed depth

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
- **Dropped in Phase 1: the PC-only soft intersection.** One material serves both platforms, so a Scene-Depth path can't be enabled for PC alone (w-td agreed). Dynamic objects (the player's legs, the crate) get the surface alpha from bed depth on both platforms, which is about 0.9 or more at wading depth.

### 2.3 Bed hiding (TD gap 1)

- **Absorption alone, no seeded bed.** At k ≈ 2.6, T(1.5 m) ≈ 2%.
  - The W−2 shelf beyond about 1.5 m and the flat W−10 floor are invisible from the bank, from the bluff tops and at water level. That is well inside the "4-6 m" bar.
  - Only the first ~0.5 m of the 20° shelf shows, as a muddy fringe.
- A useful side effect: G clamps at 4 m, so the 5 all-water tiles are uniformly opaque and the flat bed can't show at all.
- No L3 re-run is needed for bed edits, because there are none.

### 2.4 Foam and shallows

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

### 2.5 Wind ripples and current

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

### 2.6 Reflections

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

### 2.7 Refraction

**None, on either platform.** w-td ruled this, and I agree: with T ≤ 2% past 1.5 m, refraction would only show in a fringe of about 0.3 m. Real bank geometry behind the alpha-blended fringe gives the parallax-free "seen through water" read without an Opaque Texture tap [101][112].

## 3. Item 5: underwater and the camera crossing (research only, per ruling r2)

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

## 4. Cost estimate per feature (to be measured in Phase 1)

Estimates, not measurements:
- PC: GTX 1660 / RTX 3060-class GPU at 1080p, worst case with the water covering about 50% of the screen at water level (about 1M pixels).
- Web: an integrated or low-mid GPU in Chrome, WebGL2, 1080p.
- One transparent layer with no overlap. Quads under land are rejected early by the depth test. Twenty quads on one material are 20 draws through the SRP batcher.

| Feature | PC (ms) | Web (ms) |
|---|---|---|
| Base: analytic 4-sine normals, Fresnel, sky/probe tap, per-pixel fog | 0.10-0.15 | 0.20-0.35 |
| WaterMaps tap (absorption, shallows) + scum and drift noise (≈3 taps) | 0.05-0.10 | 0.10-0.20 |
| Micro normals: two-phase flow on PC (4 taps + 2 gradient taps); web 1 phase, 2 taps | 0.10-0.15 | 0.10-0.20 |
| Back-face murk safety net | ≈0 | ≈0 |
| **Total** | **≈0.25-0.45** (budget ≤1.5) | **≈0.4-0.75** |
| *Dropped:* PC soft intersection (one material for both platforms) | 0.03 | n/a |
| *Avoided:* Depth or Opaque Texture on Mobile_RPAsset | — | +0.1-0.3 per copy; each copy breaks the native render pass on weak GPUs |
| *Rejected:* refraction | +0.03-0.05 | +copy cost |
| *Rejected:* planar | +4-6 | not viable |
| *Rejected:* SSR | +0.5-1.5 | not viable |

**Cut order if W10 misses** (applies to both platforms; one material):
1. The second micro-normal layer (`_RippleTiling`.y layer).
2. Drift lines (`_DriftLine` coverage 0).
3. Render scale.

## 5. Look recipe (summary)

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

## Questions for w-td

1. One global WaterMaps texture rather than per tile, since water lives in the persistent scene. OK?
2. North-of-line scum from D, as a recorded exception to "foam from LevelMaps"?
3. Hand-written URP HLSL (`.shader` plus a `WaterSurface.hlsl` include mirroring the C# f) rather than Shader Graph?

## Sources

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
