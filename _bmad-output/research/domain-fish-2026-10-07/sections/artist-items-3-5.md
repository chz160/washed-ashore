# Section: seeing fish through the murk (item 3, look side), technique (item 5, render side), G3 stand-in mapping

Author: f-artist (technical-artist). Revision 2, 2026-10-07 (G3 on f-designer's G1 roster; f-td rulings in gate/fish-technique-addendum-1). Phase 0 research only: nothing imported or built yet.

Bound by:
- `greenlight/fish` N1, N2, N5 and N7 (`studio:decisions`)
- w-td's water shader conditions (`studio:art` `pipeline/water-td-rulings`): SRP Batcher CBUFFER, no `_Time`, no inline samplers, no camera depth or opaque texture on web, ≤ 2 local keywords
- ruling r2: the camera never goes underwater
- CLAUDE.md model-facing rule

Evidence tags:
- **[R*]** repo facts, checked directly.
- **[C*]** my own calculations; the scripts are listed so they can be rerun.
- Platform facts were checked by f-web against web sources (web-sources.md, Item 7b) and are marked VERIFIED or UNVERIFIED in place. Numbers with no source are marked **INFERRED**.

## 0. Project facts that decide the design

| # | Fact | Consequence |
|---|---|---|
| R1 | The water is a premultiplied transparent quad (`Blend One SrcAlpha`, ZWrite Off, queue Transparent). Its see-through factor is `trans = (1 − F) · T̄`, where T = exp(−_Extinction · depth / max(V.y, _MinCosView)) and **depth is the baked bed depth** (WaterMaps G, 0–4 m), not the depth of whatever lies under the quad (`BellsBendWater.shader`) | An opaque fish drawn under the quad gets the **bed's** transmittance, wherever it sits in the water column. A fish 0.3 m down over 2 m of water vanishes, and a fish over a 0.3 m shelf looks the same at any depth. **Opaque fish under the water can't meet N1.** See §1 |
| R2 | Shader values: _Extinction (2.2, 2.4, 3.2) /m, _MinCosView 0.15, _F0 0.02, body _ScatterColor (0.29, 0.29, 0.18) | These are the "shader wins" numbers for N1/G5. Everything below is derived from them, not from Secchi |
| R3 | Fish1: 274 positions, 295 faces, materials Top/Bottom/Fins, bones Root, Spine1-3, Face, Tail, Bone, Bone.001. Fish2: 253 positions, 270 faces, materials Body/Front/Fins, bones as Fish1 plus Fin.L/Fin.R. One take each: `Swim` / `Swim.001` (read offline from the vendor zip's FBX/OBJ; nothing extracted into the project) | **Fish1 will report `ambiguous`**: no L/R bones and no bone named head/neck. The detection fix has gone to f-td (§5.1). Three submeshes per model would mean three draws per batch, so the bake merges them (§2.3) |
| R4 | Fish1 proportions: total length 8.0 units, body depth 1.66 (0.21 TL), body width 0.82 (0.10 TL). Fish2: length 10.0, body depth 4.83 (0.48 TL), width ~1.5 (0.15 TL). OBJ export, model units | Fish1 is a fusiform perch/bass shape; Fish2 is a deep sunfish/crappie shape. G3 (§4) maps from these ratios |
| R5 | Bluff-top look pose (`WaterLookShots.cs`, `BellsBendWaterLook.cs`): eye (413.8, 28.17, 1793.2), W = 1.61, so **26.6 m above the water**. Shore pose: 1.7 m eye, 6 m inland of the ferry landing waterline | Two eye heights for the visibility tables and G4 |
| R6 | Web uses Mobile_RPAsset: Forward, no depth or opaque texture (water research R1). `gpuSkinning: 1` in ProjectSettings | The fish fade must work without scene depth (it does: §1 uses the fish's own position) |

## 1. Item 3 (look side): how fish are seen through the murk, and fading them with the landed water

### 1.1 What the landed water allows, in numbers [C1]

Script: `_bmad-output/research/domain-fish-2026-10-07/tools/fish_vis.py`. It applies the water shader's own rule to a fish at depth d below the surface:
- α = (1 − F) · T̄(d / max(V.y, 0.15))
- F = Schlick with F0 0.02, flat normal
- Visibility floor: α = 0.02. That's a Weber-style 2% contrast floor, **INFERRED**; to be tuned in the G4 stills.

| Fish depth | Straight down | 1.7 m shore eye: α at 2 / 4 / 6 m out | Shore eye: last visible | 26.6 m bluff eye: α at 10 / 30 / 50 m out | Bluff: last visible |
|---|---|---|---|---|---|
| 0.3 m | 0.45 | 0.30 / 0.13 / 0.05 | **7.9 m** | 0.43 / 0.31 / 0.19 | 123 m |
| 0.6 m | 0.21 | 0.09 / 0.02 / 0.00 | **4.0 m** | 0.19 / 0.10 / 0.04 | 63 m |
| 1.0 m | 0.08 | 0.02 / 0.00 / 0.00 | **2.0 m** | 0.07 / 0.02 / 0.01 | 32 m |
| 1.5 m | 0.02 | 0.00 | 0.5 m | 0.02 / 0.00 | 8 m |

**Vertical cutoff (α < 0.02 looking straight down): about 1.6 m.** That is the shader's visibility depth for G5. f-designer reconciles it with the Secchi range: by N1 the shader wins, and the Secchi number stays as the reference.

What this means for the look:
- **From the shore, the murk does N1's job by itself.** Fresnel and the slant path leave only fish in the top ~0.6 m, within ~4–8 m of the bank. They read as dim shapes on the shelf. Bodies lower down or further out are physically invisible.
- **From the bluff top, the shader alone shows too much.** The steep view cuts Fresnel and shortens the path, so a 0.3 m fish stays above the floor out to 120 m. N2 says bluff-top views show surface events and wakes only. So the model needs a second rule, a sub-surface draw radius:

  **Rule V2 (proposed): a fish below the surface is drawn only within R_sub = 25 m of the camera.**

  The bluff eye is 26.6 m above the water, so every sub-surface fish is at least that far away, and N2 holds by geometry with no special case for bluffs.
  - The shore view isn't affected; its visible band already ends inside 8 m.
  - A swimmer at the surface sees fish within 25 m (also bounded by the murk).
  - Surface events (rises, dimples, wakes, jumps, basking gar, baitfish rippling) are not sub-surface. They have their own radius, R_event (§2.5).
  - The G4 stills show the bluff views both with and without V2, so f-director rules on it with the images.

Signs the camera can actually see, and how each is drawn:

| Sign (N1) | Where the visibility comes from | How it's drawn |
|---|---|---|
| Dim shape / "shadow" on the shelf | α 0.05–0.3 in the top 0.6 m near the bank. The dark olive back against the olive body reads as a darker moving shape | The fish shader (§1.2). No real cast shadow: the fish are in the Transparent queue with no ShadowCaster pass |
| Flash (a fish rolls and shows its flank) | A short brightening of the flank. Belly swatches are dull silver/cream, value ≤ 0.82 | A per-instance `flash` 0..1 (from f-engineer's sim) lifts the lit side's diffuse. No emissive, no rim (N5) |
| Rise / dimple / swirl | At the surface, so the murk doesn't hide it | An instanced ring quad drawn after the water (§2.5) |
| V-wake, baitfish rippling | At the surface | The same ring shader in a chevron or patch mode (§2.5) |
| Gar snout or back basking | Depth ≈ 0, so α ≈ 1 − F | The fish shader at depth ≤ 0.05 m. Can't break the surface (N7) |
| Jump (carp if in, otherwise rare) | Above the surface: depth < 0, so T = 1 | The same fish shader, fully opaque. Splash = §2.5 |

### 1.2 The fade: draw fish after the water, using the water's own numbers

The fish shader (proposed `FishUnderwater.shader`, hand-written URP HLSL like the water) works like this:

- **Queue Transparent+10.** It draws after the water (Transparent 3000). ZTest LEqual against the opaque depth, so the bank lip and terrain hide fish correctly (N7 clipping). **ZWrite On** (f-td, gate/fish-technique-addendum-1), so a fish's own fins and body sort correctly; surface signs draw after the fish. `Blend SrcAlpha OneMinusSrcAlpha`.
- **Depth below the surface:** d = (MapConfig.WaterLevelY + f(x, z, t)) − y. f comes from `WaterSurface.hlsl` (the same include, globals pushed by WaterBody), sampled at the fish's pivot per vertex. No `_Time`.
- **Transmittance:** T = exp(−_Extinction · max(d, 0) / max(V.y, _MinCosView)). F = Schlick(_F0). α = (1 − F) · T̄. Above the surface (d ≤ 0), α = 1.
- **Colour:** the frame should become water + (1 − F) · T_c · (lit_c − body_c), per channel, so red carries further than blue as in the water. Blending α = (1 − F) T̄ over the water pixel gets there with colour = body + (lit − body) · T / T̄, where body = _ScatterColor × the water's own light term. A fading fish takes on the water's tint instead of going grey.

  Over deep water this matches the physical sum to first order. Over very shallow water it double-counts the bed term slightly. Accepted, because the shallow bed is lighter than the fish and the error makes the fish read darker: a "shadow", which is the N1 read anyway.
- **The water's values are read, not copied.** `_Extinction`, `_MinCosView`, `_F0` and `_ScatterColor` are copied from `BellsBendWater.mat` at runtime into `_FishWater*` globals, by the fish renderer and by the look-dev script. A material can't read another material's properties, and there are no literal copies in the fish material (f-td). Retuning the water retunes the fish.
- **The surface is not double-lit.** The fish shader adds no reflection or specular: the water quad's own reflection is already in the frame buffer under the blend.
- **Web:** none of this needs scene depth or the opaque texture. It's per-instance math only.

**F7 "deeper than the visibility depth isn't drawn"** is enforced on the CPU per instance, before any draw, through f-engineer's `FishMurk.Drawable` (the single implementation, by f-td's ruling; the shader carries a comment that it mirrors `FishMurk.Visibility` term for term). **The cull of record is (1 − F) · T̄ < 0.02**, tested at the shallowest point of the body (conservative). `FishMurk` pins the limits at 1.57 m straight down, 1.10 m from the bluff top, and 0.48 m from the shore eye at 5 m out:
- skip if α(d, V) < 0.02, or
- skip if d > 0 and the distance to the camera is more than R_sub.

The shader fades to 0 at the same floor, so nothing pops in view (N7): the cull only removes instances that are already invisible. The render stats for F7 report drawn versus simulated counts per frame.

### 1.3 G5: a fixed 1.0 m cutoff, or the shader's own fade? (f-designer's question)

f-designer proposed a 1.0 m draw cutoff, a body tier ≤ 0.6 m and an alpha fade from 0.6 to 1.0 m. f-designer also noted that the water quad's alpha comes from the bed. Answers from the shader side:
- **The bed-depth alpha is not the look we want.** Fish need their own depth-based fade, drawn over the water alpha. That is §1.2, approved by f-td. Under the bed-only alpha, a 0.3 m fish over a 2 m bed is invisible, and a fish over a 0.4 m shelf looks the same at any depth. With §1.2, bodies show wherever the fish itself is shallow, over any bed. In practice that's the shelf, because that's where shallow fish live.
- **Don't use a fixed 1.0 m cutoff.** The fade comes from the shader's own rule, and the contrast floor of 0.02 is the cull. Straight down, a 1.0 m fish still has α 0.08, so a hard 1.0 m cut would pop. The real limit depends on the view: 1.57 m straight down, 0.48 m from the shore eye at 5 m out.
- **Body tier ≤ 0.6 m:** agreed as the *design* tier for where V1 bodies are placed. Visibility doesn't need it as a rule: from the shore it falls out of the murk (§1.1).
- **Secchi:** 1.0 m measured (Cheatham, from G1) versus ~0.65 m implied by the shader. The shader wins (N1), and the measured value stays as the reference. The two numbers measure different things: Secchi is a white disc seen to extinction, while ours is a 2% contrast floor on a dark fish.

## 2. Item 5 (render side): animation, instancing, LOD, WebGL2 cost

### 2.1 Single-clip animation

Each model has one `Swim` loop, 31 frames.
- **Playback speed tracks swim speed (F5).** Per-instance phase φ += dt · (base + k · speed) / stride, integrated by f-engineer's sim and passed per instance. The renderer never reads a clock.
- **Amplitude tracks effort.** The bake stores the rest pose and the animated pose. The shader blends `rest + a · (anim − rest)`, with a = clamp(speed / cruise, a_idle 0.25, 1.0). A hovering fish only fans its fins and tail. A drifting fish isn't frantic, and a bursting fish runs the full stroke at 2–3× rate. a_idle and k go to f-designer and f-engineer for tuning.
- **Turning:** per-instance yaw comes from the sim's smoothed heading (F5: ≤ 45° per frame). Optional procedural bend: a lateral offset ∝ turn rate × (object z)², a few ALU only. Off unless the stills show stiff turns.

### 2.2 GPU instancing (vertex animation texture) versus Animators

| | Animator + SkinnedMeshRenderer per fish | VAT + `Graphics.RenderMeshInstanced` (proposed) |
|---|---|---|
| CPU per fish | Animator update plus skinning, on the CPU in wasm. WebGL2 could run Unity's GPU skinning through transform feedback, but Unity disabled it because it was consistently slower than CPU skinning (Unity staff, 2017). Compute shaders are not natively supported on WebGL2 (Unity manual, web graphics API comparison). VERIFIED by f-web (web-sources.md, Item 7b). The skinning post is from 2017; confirm current Unity 6 behaviour in the F9 Profiler capture | Writes one matrix and a float4 or two into the batch arrays |
| Draw calls | One per fish per submesh (3 submeshes per model) | One per model per frame. ≤ 511 instances per call (RenderMeshInstanced docs, VERIFIED). The batch size is the max constant buffer size ÷ the per-instance struct size (Unity manual, VERIFIED). The 16 KiB GLES figure is from secondary sources only, so about 128 instances per batch at 128 B is INFERRED. Fine at ≤ 64 drawn |
| Speed and amplitude | Per Animator speed only | Per instance: phase, rate, amplitude, tint, flash |
| Facing | Inherits the import fix | Baked **after** the import fix, so it inherits it. No yaw anywhere in code (CLAUDE.md) |
| WebGL2 | Works, but costs CPU on a target already near its edge (water TD note: web p95 ≈ 33 ms **without** water) | Instanced draws are core in WebGL2 (Khronos WebGL2 spec), and ES 3.0 guarantees ≥ 16 vertex texture units. VERIFIED (f-web). **RGBA16F VAT sampling in Unity WebGL2 is UNVERIFIED**, so the bake keeps an RGBA32 fallback (f-td 5.4 e) |
| Later fishing | Natural for one hooked fish | Hand off one instance to a skinned prefab when hooked (TD fishing-readiness note) |

**Recommendation: VAT plus RenderMeshInstanced for every ambient fish.** Prefab variants (F2) keep the skinned model plus a `FishLook` data component (model, scale, tints, mottle), so the census and later fishing have a real per-species object. The runtime renderer reads the same data, not GameObjects.

Not proposed:
- BatchRendererGroup / Entities Graphics: not supported on the WebGL platform (Unity 6.0 docs; VERIFIED by f-web). On GLES, BRG uses a UBO mode, so the reason is platform support, not SSBOs.
- RenderMeshIndirect / DrawMeshInstancedIndirect: "only works on platforms that support compute shaders" (Unity docs; VERIFIED by f-web).

### 2.3 The bake (editor script, reproducible)

- `AnimationClip.SampleAnimation` on the imported model, then `SkinnedMeshRenderer.BakeMesh` per frame.
  - Frames 0..N−1. Frame 30 is dropped if it equals frame 0; that's the seam check, see §5.2.
  - Output: positions plus normals into one RGBAHalf texture, rows = frames, plus a rest-pose row.
- The three submeshes merge into one. The region id (body, belly, fins) goes into vertex colour, so it's **one draw per model**.
- Countershading: a back→belly blend over object-space height, split at the lateral line, with optional mottle noise. Per-instance colours: back, belly, fins (§4.2).
- Size: Fish1 ~600 split vertices (INFERRED until import) × 32 rows × 8 B ≈ 150 KB per model, uncompressed half. Both models ≈ 0.3 MB in memory. Web download < 0.3 MB.
- Vertex shader: two point samples (frames ⌊φ⌋ and ⌊φ⌋+1) with a lerp; `SAMPLE_TEXTURE2D_LOD` via a declared sampler, no inline sampler.

### 2.4 LOD and what is simulated

- **Mesh LOD: none.** 270–295 triangles is below any useful LOD step.
- **Draw LOD is visibility** (§1.2): the α floor plus R_sub. Typical drawn fish from the shore is about 0–10, which is G6's "most visible at once", set by f-designer.
- **Sim LOD (f-engineer's side, stated here for budgets):** simulate within R_sim around the player, following tile streaming (F9). R_sim must cover R_event so surface events from the bluff are real fish, not fakes. Beyond R_sim, fish exist only as census numbers.

### 2.5 Surface events (VFX)

- **Rings, dimples, swirls, V-wakes, baitfish rippling:** one instanced quad shader drawn after the water, Transparent+5.
  - Analytic rings: radius = r0 + v·t; thin band alpha; fades by age and distance.
  - It perturbs nothing in the water. It's a visual-only overlay, per the water TD note §2.5 ("wakes and splashes are visual only, never part of f").
  - Muted: brightness capped at the scum colour #A89F84, with no specular of its own.
  - One draw for all events. Drawn within R_event, proposed 150 m (bluff to mid-river).
- **Jump splash:** a small Shuriken burst, ≤ 24 particles, pooled, only for jumps. Jumps are capped by the surface-event rate (N4).
- **API proposal:** f-engineer emits `(kind, position, size, heading)` and owns when. I own what it looks like.

### 2.6 WebGL2 cost (estimates; F9 measures them)

| Item | Estimate | Basis |
|---|---|---|
| Fish draws | 2 (one per model) + 1 ring + ≤ 1 splash | §2.2, §2.5 |
| Instances drawn | ≤ ~16 typical, cap 64 | §1.1, G6 |
| Vertex cost | ≤ 64 × 300 tris, 2 VTF reads per vertex: negligible | INFERRED |
| Fragment cost | Small fish, low overdraw, a few ALU more than an unlit blend | INFERRED |
| CPU (renderer) | Per simulated fish: one α test, plus per drawn fish one matrix and 2 float4s. ≈ 0.05 ms at 400 simulated in IL2CPP wasm | INFERRED; measured in F9 |
| Memory / size | VAT ≈ 0.3 MB, + shaders. Web.data + < 0.5 MB | §2.3 |

**Proposed F9 art budget (for f-td to approve):**
- Fish rendering + VFX ≤ +0.3 ms median on the Windows shore route, ≤ +1.0 ms on web.
- ≤ 4 draw calls.
- ≤ 0.5 MB web download.
- The sim's CPU budget is f-engineer's line item, not this one.

## 3. Item 5 extras on my side

- **Bed clearance and the bank lip** are the sim's job (WaterMaps / TerrainQuery; F4). Rendering adds the depth test against terrain, so a fish that strays never draws through the lip (N7). It's still a sim violation.
- **No highlight, no UI marker, no outline** (N7). Selection and fishing-affordance shaders are out of scope.

## 4. G3: stand-in mapping (model, scale, tint)

The rows follow **f-designer's G1 roster** (17 species, received 2026-10-07):
- Adult TL is the MDC "commonly" range, sourced in G1.
- Spread: sampled uniformly in that range, with a rare tail to the max (G1's rule).
- Out of the roster: walleye, striped/hybrid bass, rainbow trout, and silver/bighead carp (pending G2; f-designer recommends out, with common carp as the stand-in).
- Vis tier is from G1:
  - V1: a body is possible on the shelf, ≤ 0.6 m deep.
  - V2: shadow or flash only.
  - V3: surface signs only.
- **Every species still gets a census prefab variant (F2).**

### 4.1 Model and scale

Rule:
- Pick the model whose body depth / TL (Fish1 0.21, Fish2 0.48; R4) needs the smaller non-uniform Y scale.
- Allowed Y range is 0.6–1.6, pending the G4 stills. X (width) moves with Y, moderated.
- Uniform scale = sampled TL / model nose-to-tail length. The import check measures that length at frame 0.
- Non-uniform scale leaves facing unchanged: scale doesn't rotate, and the VAT is baked in object space after the facing fix.
- Target depth/TL ratios are INFERRED shape reads from G1's descriptions ("deep-bodied", "humped back", "very elongated"), not measured.

| # | Species (variants) | Model | Adult TL cm (G1) | Target depth/TL | Y | X | Tier | Note |
|---|---|---|---|---|---|---|---|---|
| 1 | Bluegill (+ longear, redear) | Fish2 | 13–24 | 0.45 | 0.94 | 1.0 | V1 | Variants differ by tint only |
| 2 | Largemouth bass | Fish1 | 25–50 (max 61) | 0.25 | 1.20 | 1.1 | V1 | |
| 3 | Spotted bass | Fish1 | 25–43 | 0.24 | 1.15 | 1.05 | V2 | |
| 4 | Smallmouth bass | Fish1 | 25–50 | 0.24 | 1.15 | 1.05 | V2 | |
| 5 | White crappie (+ black) | Fish2 | 23–38 | 0.40 | 0.83 | 0.9 | V2 | |
| 6 | Channel catfish | Fish1 | 30–80 | 0.18 | 0.86 | 1.2 | V3 | Round, broad head |
| 7 | Flathead catfish | Fish1 | 38–114 | 0.17 | 0.80 | 1.4 | V3 | Flat, broad |
| 8 | Blue catfish | Fish1 | 50–112 | 0.20 | 0.95 | 1.2 | V3 | |
| 9 | Freshwater drum | Fish1 | 30–50 | 0.30 | 1.40 | 1.15 | V3 | Humped back; Fish2 at Y 0.62 reads too round |
| 10 | Smallmouth buffalo (+ bigmouth) | Fish1 (alt: Fish2 Y 0.70) | 38–76 | 0.33 | 1.55 | 1.2 | V2 | The stills pick between the two models |
| 11 | Common carp | Fish1 | 30–64 | 0.30 | 1.40 | 1.2 | V1 | Backs and tails in the shallows |
| 12 | Longnose gar (+ spotted, shortnose) | Fish1 | 60–120 (max 150) | 0.09 | **0.43** | 0.6 | V1 at the surface | **Below the 0.6 floor.** Tested in G4. If Swim doesn't read or the snout is wrong, gar shows as signs only (basking back, wake) per N5. No beak on Fish1 either way |
| 13 | Sauger | Fish1 | 30–38 | 0.17 | 0.80 | 0.9 | V3 | |
| 14 | White bass | Fish1 | 23–38 | 0.30 | 1.40 | 1.1 | V3 | Busting schools = surface signs |
| 15 | Gizzard shad (+ threadfin, 10–13) | Fish2 | 23–36 | 0.36 | 0.75 | 0.8 | V3 + shelf flicker | The flicker is the flash term (§1.1) |
| 16 | Skipjack herring | Fish1 | 30–41 | 0.24 | 1.15 | 0.85 | V3 | Jumps |
| 17 | Paddlefish | none (census prefab on Fish1, never drawn as a body) | 100–180 | | | | V3 | The paddle snout can't be honestly approximated (N5). Shows only as a wake |

V3 species are still drawn by the murk rule, so they can appear if they come shallow. G1's tiers say where they live, and the fade does the rest. If f-director wants V3 bodies **never** drawn, that's a per-variant `drawBody` flag, off for V3. That's a ruling for G4.

Patterns: "faint dark bars" (bluegill, smallmouth), "dark lateral band" (largemouth), "rows of spots" (spotted bass) and "stripes" (white bass) aren't in this POC. At ≤ 0.6 m through this murk (α ≤ 0.2) they wouldn't read, so the shader has mottle only. INFERRED; the G4 close stills check it.

### 4.2 Tint swatches (muted river palette)

Card: `TestResults/fish-lookdev/g3-swatches.png`, opaque RGB, columns back / belly / fins. Generator: `tools/fish_swatches.py`.
- Every swatch is capped at **HSV saturation 0.40 and value 0.82**: no saturation, nothing near white (N5). `*` marks a swatch the cap pulled in.
- Mottle is the strength of the low-frequency noise on the back.
- The belly swatch is also the "dull-silver" target for the flash lerp (seam contract with f-engineer).
- No emissive, no rim, no specular on the fish (the water's reflection is over them).

| Species | Back | Belly | Fins | Mottle |
|---|---|---|---|---|
| Bluegill / sunfish | #4F5A48 | #A08F60* | #4A4E40 | 0.10 |
| Largemouth bass | #5C6440 | #C9C4A0 | #6A6A48 | 0.20 |
| Spotted bass | #646040 | #C8C0A0 | #64604A | 0.25 |
| Smallmouth bass | #6E6042* | #C2B48E | #6A5A40* | 0.25 |
| Crappie | #5E6656 | #B9BCB2 | #5A5E52 | 0.30 |
| Channel catfish | #5A6052 | #B8B49C | #4E5048 | 0.10 |
| Flathead catfish | #6B5B40* | #B5A783 | #5A4C36 | 0.35 |
| Blue catfish | #5E6670 | #C2C4BE | #565C62 | 0.00 |
| Freshwater drum | #6E7270 | #BEBEB4 | #6A6C68 | 0.00 |
| Buffalo | #5E5A48 | #B0AA92 | #56524A | 0.05 |
| Common carp | #6C6241* | #B8A570 | #6A5640* | 0.10 |
| Gar | #5E5A3E | #B3A986 | #5A5438 | 0.40 |
| Sauger | #66603E | #CFC8AA | #6A6448 | 0.35 |
| White bass | #6A706A | #C4C6BE | #646862 | 0.00 |
| Gizzard / threadfin shad | #646C68 | #C6C8C0 | #60645E | 0.00 |
| Skipjack herring | #566A66 | #C8CCC4 | #5E6462 | 0.00 |
| Paddlefish (census only) | #5E6468 | #B4B6B2 | #585E62 | 0.00 |

Variants within a species (longear/redear, black crappie, bigmouth buffalo, spotted/shortnose gar, threadfin) use the species swatch with ±5% value. Per-instance jitter: ±6% value and ±3° hue (INFERRED, checked in the stills).

## 5. F1 import plan (pre-gate, baton slot only)

### 5.1 Import

- `tools/fish/extract_fish.ps1` (same pattern as `tools/birds/extract_robin.ps1`) extracts **only** `FBX/Fish1.fbx`, `FBX/Fish2.fbx` and `License.txt` into `Assets/ThirdParty/Quaternius/AnimatedFishPack/`.
  - It uses a whitelist, so the marine models can't be extracted.
  - It never writes to `vendor/`.
- `Assets/Editor/Fish/FishImportSettings.cs` (same pattern as `BirdImportSettings`):
  - Generic rig, no material import, no cameras or lights.
  - The clip is named `Swim`, `loopTime` on.
  - It runs before the ModelFacingPostprocessor fix, which still applies.
- **Facing:** Fish1 will be `ambiguous` under the current rule (R3). Proposed fix, sent to f-td for approval:
  - Add `"face"` at the end of `HeadNames`, plus one EditMode test case.
  - No `Version` bump. All 14 models in the current report are decided by L/R, and the head rule only runs when L/R doesn't decide, so their results can't change.
  - Fish2 is expected to decide by `Fin.L`/`Fin.R`, if the offset clears the 5% epsilon; otherwise the head rule decides it.
  - Pass condition: both models show `flip` or `keep` in `TestResults/model-facing-report.json`.

### 5.2 Swim seam check

- Sample every bone's local TRS at frame 0 and at the last frame. Report the max position and rotation deltas, and the frame-to-frame delta across the wrap compared with the clip's median frame-to-frame delta.
- Pass: the wrap step is ≤ 1.5× the median step (no pop).
- If the last frame duplicates the first, the VAT bake drops it (§2.3) and the report says so.
