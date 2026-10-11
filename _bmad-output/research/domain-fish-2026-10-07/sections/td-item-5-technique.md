# 5. Technique (f-td, technical-director, 2026-10-07)

Decision of record: `gate/fish-technique` (studio:decisions); ADR `adr/fish-1-instanced-steering` (studio:engineering).
Project evidence is cited as file paths in E:\GitHub\washed-ashore. Numbered sources are listed at the end; f-web verified them on 2026-10-07 (web-sources.md item 7), with one correction (source 5).

## 5.1 Findings from the landed code that shape the technique

| # | Finding | Evidence | Consequence |
|---|---|---|---|
| T1 | The water shader attenuates whatever is behind it by the **bed** depth, not by the depth of the object: Beer-Lambert over `depth = WaterMaps.G * 4` along the view path, premultiplied blend `One SrcAlpha`, extinction RGB (2.2, 2.4, 3.2)/m, "opaque within ~1.5 m". | `Assets/World/BellsBend/Water/BellsBendWater.shader:2,14,78,173,218-221` | An opaque fish drawn before the water is darkened as if it sat on the bed: a fish 0.3 m deep over a 2 m bed transmits ~1%. Fish must draw **after** the water with their own Beer-Lambert over their own depth (5.4). |
| T2 | WaterMaps is GPU-only (`isReadable = false`) and its depth channel saturates at 4 m. | `Assets/Editor/Art/BellsBendWaterMaps.cs:11,23,221` | C# fish read the bed from `TerrainQuery.TryGroundHeight` (exact, unsaturated) and shore distance from `BellsBendLevelMaps.ShoreDistance` (2 m grid, CPU-readable, the same source WaterMaps.R was copied from). Do not make WaterMaps readable for fish. |
| T3 | Surface = `MapConfig.WaterLevelY + WaterMotion.Offset(waves, x, z, WaterClock.Now)`; shipped total amplitude 0.028 m. | `Assets/Scripts/World/WaterMotion.cs`, water-td-note section 1 | Depth clamp uses the analytic surface, never a flat Y; margin >= `WaterMotion.MaxAmplitude` + half the body height. |
| T4 | There is no runtime tile streaming today: all 20 tiles are loaded; the Addressables split is deferred. `TerrainQuery.Height` falls back to the nearest tile edge. | `Assets/Scripts/World/MapConfig.cs:27`, `Assets/Scripts/Gameplay/TerrainQuery.cs:84-95`, water-td-note section 4 gap 1 | "Follow tile streaming" = a cell is live only if `TryGroundHeight` succeeds at its samples. When streaming lands, unloaded tiles mean dead cells with no code change. Never call `Height()` for fish. |
| T5 | Wildlife and birds use one GameObject + Animator + SkinnedMeshRenderer per animal; their measured cost is ~0 on Windows (counts are low). | `Assets/Scripts/Wildlife/WildlifeAgent.cs:21-29`, `Assets/Scripts/Birds/FlockBird.cs:15`; memory perf/art/bells-bend-r2 | Fine at tens of animals. Fish run at hundreds of live agents, and web is already CPU-bound (p95/p99 ~33 ms without water in view, water-td-note section 1). |
| T6 | WebGL2: no compute, C# jobs run on the main thread, GC is expensive. | `_bmad-output/poc/td-web-note.md:16,21` | No VFX Graph, no compute skinning, no GPU-driven indirect draws; zero per-frame allocations. CPU-supplied instanced draws are allowed. |
| T7 | Murk visibility depth comes from the shader, not Secchi. Cull of record (f-artist proposal, adopted): a body is not drawn when (1 - F) x Tavg < 0.02, with Tavg ~ exp(-2.6 x path) and path = depth / max(V.y, 0.15). That puts the path limit at ~1.5 m: ~1.5-1.6 m straight down, ~1.0 m from the bluff top (V.y ~0.7), ~0.5 m from the 1.7 m shore eye looking 5 m out (V.y ~0.32), less farther out. A Secchi of ~1.7/Kd with Kd ~2.6/m gives ~0.65 m [7] (INFERRED mapping of shader extinction to Kd). Measured Bells Bend Secchi (USACE, n=121) is median 1.0 m, P10-P90 0.8-1.2 m (web-sources.md item 7): the shader is murkier than the 10th percentile; per N1 the shader wins and the measured range stays the reference. | `BellsBendWater.shader:14,17,219-221` | Input to G5. Bodies are only ever drawable on the shelf within about 0.5-1 m of the surface, which matches N1. The shader extinction is the single source (N1: the shader wins). |

## 5.2 Animation: instanced static mesh + baked Swim texture (APPROVED); Animators (REJECTED)

| Option | Per-fish CPU | WebGL2 | F5 fit | Verdict |
|---|---|---|---|---|
| GameObject + Animator + SkinnedMeshRenderer | Animator update + CPU skinning per fish (Unity WebGL skins on the CPU: its transform-feedback GPU skinning was disabled as slower than CPU; confirm on Unity 6 - web-sources.md 7b) + transform sync | Worst on the platform already at its edge | Direct (Animator speed) | **Rejected for the population.** Allowed later for one caught/hero fish (see the fishing note). |
| `Graphics.RenderMeshInstanced` + **vertex animation texture (VAT) baked from Swim** | Matrix + 3 floats per fish, one draw per model | Instanced draws and vertex texture fetch are core WebGL2 / OpenGL ES 3.0 [6] | Playback rate is literally the clip rate, so the F5 velocity/animation correlation is measurable | **Approved (primary).** [1][2] |
| Instanced mesh + procedural sway in the vertex shader (sine along the body axis, amplitude and frequency from speed) | Same as above, no texture | Same | The clip is not used, so F5 clip playback speed is read as sway frequency | **Approved fallback** only if the VAT bake fails the F1 seam check or a WebGL2 texture-format issue appears. Switching needs an f-td note, not a new gate. |
| BatchRendererGroup / Entities Graphics | Lowest | **Not supported on WebGL** (the constant-buffer mode is for Android GLES 3.x) and requires the SRP Batcher [5] | - | **Rejected**: unsupported on one of our two targets. |

VAT rules:
- Bake from the **imported, facing-corrected** Fish1/Fish2 (after `ModelFacingPostprocessor` and a `flip`/`keep` Model Facing Report). The bake script lives in `Assets/Editor/Fish/` and is rerunnable. The baked mesh forward is +Z, so the instance rotation is `Quaternion.LookRotation(heading, up)` and nothing else. **No yaw, pitch or roll constants anywhere in fish code or shader** (automatic FAIL). A per-species non-uniform scale (gar) is a scale, not a rotation.
- 31 Swim frames, positions + normals, point-sampled with a manual frame lerp in the vertex shader; the bake asserts frame 0 == frame 30 within 1 mm (F1 loop seam), or drops the duplicate end frame. Format RGBAHalf; budget <= 256 KB per model uncompressed, no mips, not readable at runtime. RGBA16F sampling in Unity WebGL2 is not verified [6]: the F9 web build must show the animation, and the bake keeps an RGBA32 fallback (position quantised to the clip-max box) behind one import setting.
- Per-instance data: phase (0-1, seeded), playback rate (= swim speed / species reference speed, clamped; at rest a slow fin cycle set by the designer, never a frozen frame and never a full-speed tail on a drifting fish), tint, scale.

## 5.3 Behaviour: per-fish steering (APPROVED); boids (REJECTED)

- **Rejected: boids** (Reynolds 1987 [3]). Global separation, alignment and cohesion need a neighbour search across all fish (O(n^2) or a spatial hash), are hard to make deterministic and testable, and their natural output is the visible open-water school and circling pack that N2 forbids.
- **Approved: Reynolds steering behaviours** (Reynolds 1999 [4]) driven by a small per-fish state machine: Hold (bottom fish, low and still), Hover (bass and sunfish near structure), Cruise/Wander, SchoolFollow, Scatter, Settle, Rise, Bask (gar), Jump (carp, only if N4 lets carp in). Same shape as the birds Flock (lead point + slots, `Assets/Scripts/Birds/Flock.cs`).
- **Schools** (crappie, baitfish): one anchor per group that wanders inside its band; members hold seeded slot offsets around the anchor with separation **only within the group** (groups are small, so O(k^2) per group is fine). No cross-group neighbour search.
- **Turn rate**: steering output is limited by a per-species max turn rate (deg/s, designer number) and a hard cap of 45 deg per rendered frame (F5). Speeds and accelerations are designer numbers in the tuning asset.
- **Player**: distance checks against the player position only. Fish have **no colliders and no rigidbodies**, so they cannot block or bump the swimmer (F10, N7), and robins and raycasts cannot hit them. A personal-space radius around the player keeps bodies out of the capsule.

## 5.4 Rendering under murk

- One hand-written URP HLSL shader for both platforms (as ADR water-1), `#pragma multi_compile_instancing`, no camera depth or opaque texture, no platform keywords. Draws in a queue **after** the water (Transparent + offset), ZTest LEqual against the terrain bed; ZWrite On so a fish's own fins and body sort correctly (surface signs draw after the fish).
- Composite: Beer-Lambert over the fish path `(surfaceY - fishY) / max(V.y, minCos)` using the water material `_Extinction`, `_ScatterColor` and `_MinCosView`, read from the water material at startup (one source: the shader wins). Fish colour fades to the water body colour; the water Fresnel reflection must stay over the fish (f-artist owns the exact blend; the G4 stills are the acceptance). Above the surface (jump) the path is 0, so the same shader handles jumps.
- Draw-call rules [1]: the material must have `enableInstancing` (otherwise the call throws), check `SystemInfo.supportsInstancing` once at startup (log and disable fish if false), at most 511 instances per call with default matrices (we draw <= 64; secondary sources put the WebGL 16 KB uniform-buffer batch at ~128 instances of 128 B, so the 64 cap also stays inside one batch), and do not use `assumeuniformscaling` (gar is non-uniform). Unity culls the whole call by one bounds, so the per-fish CPU cull below is required, not an optimisation. In look-dev, confirm the hand-written shader instances under URP with the SRP Batcher on (Unity's GameObject-instancing caveat does not apply to direct RenderMeshInstanced, but verify).
- CPU culling before submit: frustum, body draw distance, and **(1 - F) x Tavg < 0.02 means not drawn** (this is the F7 rule that fish deeper than the visibility depth are not drawn, provable from counters). Shadows off. No LOD needed at ~300 faces.
- Muted palette, no emissive or rim (N5): the shader has no emissive or rim terms.
- Surface signs (rises, dimples, swirls, V-wakes, baitfish ripples) are pooled surface quads with a ring/wake shader drawn after the water at `WaterLevelY + WaterMotion.Offset`, plus small built-in ParticleSystem splashes for jumps (CPU particles, WebGL2-safe, tiny counts). Never VFX Graph. Signs are visual only.

## 5.5 Population, simulation radius and streaming

- **Virtual population**: the map is divided into fixed cells (designer picks the size, e.g. 32 m) and the fish of each cell (species, count, group, home depth) are a **pure function of (seed, cell id)** from the brief band densities. The F3 census is computed over the whole map from this function without simulating, at 3 seeds.
- **Live set**: only cells within the sim radius of the player are instantiated into the simulation arrays (structs in one manager, no GameObject per fish), with hysteresis (R_in < R_out). A cell is live only if `TryGroundHeight` succeeds at its samples (T4). Leaving the radius discards state; re-entering regenerates it from (seed, cell). Ambient fish remember nothing.
- **No pop in view (N7)**: a fish may enter or leave the live set only while not drawable (below the 0.02 floor or beyond body draw distance). Spawn points are at home depth below the visibility depth or outside the draw distance. PlayMode counter: in-view spawns and despawns = 0.
- **Surface events** run on a wider ring than bodies (they are what the bluff top sees). They are scheduled per cell by a seeded Poisson process at the designer rates and capped by the surface-event rate (N4 for carp).
- **Tick**: fixed simulation step (10-20 Hz, engineer choice) with render interpolation, so outcomes do not depend on frame rate and tests reproduce on any machine.

## 5.6 Staying in the water (F4)

For every fish every tick, after steering:
- `bed = TryGroundHeight(p)` (cached on a grid when the cell goes live), `surface = WaterLevelY + WaterMotion.Offset(waves, x, z, WaterClock.Now)`.
- `y = clamp(y, bed + clearance(species), surface - margin)` with `margin >= MaxAmplitude + half body height`. Only Rise, Jump and Bask may exceed `surface - margin`, and only while that state is active.
- Reject any move into a cell with `ShoreDistance >= -minShore` or `surface - bed < species min depth`; steer back along the shore-distance gradient (no physics).

## 5.7 Determinism and code standards

- RNG: a stateless counter hash `Hash(runSeed, stream, cellId, fishIndex, counter)` (SplitMix or xxHash style), with the BirdRandom/WildlifeRandom conventions (`OverrideSeed` for tests, own stream constant). No `UnityEngine.Random`, no shared mutable `System.Random`. Random access by (cell, fish) is what lets a server recompute any fish later.
- Time: water height from `WaterClock.Now`; the fish sim advances its own fixed-step counter. No `Time.deltaTime` in decisions.
- Layout: `Assets/Scripts/Fish/` with asmdef `WashedAshore.Fish` (refs `WashedAshore.World`, `WashedAshore.Gameplay`, `WashedAshore.Level`); import and bake in `Assets/Editor/Fish/`; a `FishTuning` ScriptableObject in `Assets/World/Fish/`. Files under 500 lines. Pure parts (placement, census, clamp, scheduler, RNG) are static and EditMode-tested; F4-F7 in PlayMode.
- 0 bytes of GC allocation per frame in steady state (pre-sized arrays, no LINQ in the tick).
- Profiler markers `Fish.Sim`, `Fish.Submit`, `Fish.Events` so F9 can attribute cost.

## 5.8 Budgets (F9)

| Budget | Windows (RTX 4060 Ti / i9-14900K, release, 1080p) | Web (Chrome, WebGL2) |
|---|---|---|
| Frame rate | >= 60 FPS on the shore route; p99 < 16.6 ms | Median >= 30 FPS on a 60 s shore-leg walk with water and fish filling the view (closes the water pod gap) |
| Fish cost | Fish markers total <= 0.3 ms median CPU; fish-on vs fish-off median delta <= +0.5 ms | fish-on vs fish-off median delta <= +2 ms; report p95 against the water baseline (33.4 ms) |
| Draws | <= 2 instanced body draws + <= 2 surface-event draws | same |
| Counts (caps; designer numbers sit under them) | live fish <= 256, drawn bodies <= 64, active surface events <= 16 | same |
| Memory | fish runtime <= 4 MB | same |
| Build size | - | fish adds <= 1 MiB to Web.data (Brotli); Web.data stays under the deploy-web.ps1 100 MB warning (now 57.1 MiB); files over 25 MiB already go to R2 |
| GC | 0 B/frame steady state | same |

A budget miss blocks merge. Levers in order: drawn-body cap, sim radius, event cap, VAT to procedural sway.

## 5.9 Not covered

Underwater camera (ruling r2), sound, fishing (see `_bmad-output/poc/fish-td-note.md` at the end of the pod), any physical current (the current is visual only; fish drift is a steering bias at most).

## 5.10 Sizing check against f-designer draft (2026-10-07)

Draft inputs: sim radius ~60 m, <= ~40 simulated fish in radius, <= ~6 visible bodies at once, surface events as particles/decals. All fit under the caps (256 live / 64 drawn / 16 events) with a large margin. Two notes:
- The 60 m body radius is fine for bodies (the murk limits them to the shelf near the viewer), but the bluff top sees surface events much farther out, so surface events need their own, wider ring (designer number), still capped at 16 active.
- With these counts plain C# on the main thread is enough; no Jobs or Burst (WebGL runs C# jobs on the main thread anyway).

## Sources (verified by f-web 2026-10-07, web-sources.md item 7)

1. Unity Scripting API, Graphics.RenderMeshInstanced (Unity 6.6): instance limits, enableInstancing requirement, single-bounds culling. https://docs.unity3d.com/ScriptReference/Graphics.RenderMeshInstanced.html
2. Unity Technologies, Animation-Instancing (GPU-baked skinned animation for instanced crowds; Unity 5.4+, no URP support stated: prior art, not a dependency), GitHub. https://github.com/Unity-Technologies/Animation-Instancing
3. Reynolds, C. W. (1987). Flocks, Herds, and Schools: A Distributed Behavioral Model. SIGGRAPH 87, Computer Graphics 21(4), 25-34.
4. Reynolds, C. W. (1999). Steering Behaviors for Autonomous Characters. Game Developers Conference 1999. https://www.red3d.com/cwr/steer/
5. Unity Manual (6.0), BatchRendererGroup: supported platforms (WebGL not listed; constant-buffer mode is for Android GLES 3.x; requires the SRP Batcher). https://docs.unity3d.com/Manual/batch-renderer-group-how.html
6. Khronos, WebGL 2.0 Specification (instanced drawing and vertex texture fetch as core features). https://registry.khronos.org/webgl/specs/latest/2.0/
7. Poole, H. H. and Atkins, W. R. G. (1929), verified via a 2024 Frontiers review (Kd = 1.7/Secchi; 1.4 for turbid water, Holmes 1970); original not opened. Photo-electric measurements of submarine illumination throughout the year. J. Mar. Biol. Assoc. UK 16, 297-324 (the Secchi ~ 1.7/Kd rule).

Project evidence (read this run, 2026-10-07): `Assets/World/BellsBend/Water/BellsBendWater.shader`, `Assets/Editor/Art/BellsBendWaterMaps.cs`, `Assets/Scripts/World/WaterMotion.cs`, `Assets/Scripts/World/MapConfig.cs`, `Assets/Scripts/Gameplay/TerrainQuery.cs`, `Assets/Scripts/Wildlife/WildlifeAgent.cs`, `Assets/Scripts/Birds/FlockBird.cs`, `Assets/Editor/Wildlife/ModelFacingPostprocessor.cs`, `_bmad-output/poc/water-td-note.md`, `_bmad-output/poc/td-web-note.md`, `deploy-web.ps1`.
