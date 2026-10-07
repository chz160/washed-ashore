---
title: 'POC Spec: Water around Bells Bend (research, then build)'
status: ready-for-pod
owner: John (PM)
created: '2026-10-06'
depends_on:
  - _bmad-output/planning-artifacts/poc-spec-washed-ashore-bells-bend-land.md
  - _bmad-output/poc/bells-bend-td-note.md          # §3 Water-pass readiness + checklist (binding)
  - _bmad-output/poc/bells-bend-td-input.md         # datum, bank profile, LevelMaps
  - _bmad-output/poc/bells-bend-design-brief.md     # shoreline bank rule (rev 3/6/14)
  - _bmad-output/poc/bells-bend-qa-report.md        # Noah's design intent: water + drowning = out-of-bounds deterrent
---

# POC Spec: Water

**The question this answers:** what's the best way to do water in Washed Ashore, and can the agents build it so that the river bend reads as real water, the player can wade and swim, and nobody can swim around the north line?

**Out of scope: drowning, stamina, breath, death and respawn.** The game has no player-character model or concept of death yet. Noah's ruling (water plus drowning as the out-of-bounds deterrent) still stands **for the future**. This pod researches it (Phase 0, item 4) but doesn't build it. Until then, the existing backstop and `WorldBoundsClamp` are the boundary over water.

## What already exists (from the Bells Bend pod; don't redo it)

- **One water height:** `MapConfig.WaterLevelY` = (117.3 − 115) × 0.7 = **1.61**. It's derived, never typed in as a number.
- **Banks are ready for water:** a 0.3 m lip, 18–20° banks out to 12 m inland, an underwater shelf to wading depth (W − 2), then a ramp to W − 10 by 20 m out. Bluffs (Buzzard, McCord) meet the water as sheer faces by ruling.
- **Everything outside the polygon is below W,** and the river north of the line is carved to the same profile. **5 tiles are entirely water** (x0_z0..x0_z3, x3_z3).
- **`LevelMaps` signed shore distance** (2 m grid, ±127 m) is there for foam and shallows. `report.json` holds the **lake-mask hash `8f8a637cd4657d82`**.
- **Known gaps the TD handed to this pod** (`bells-bend-td-note.md` §3):
  - The lake bed is flat and looks like a swimming pool.
  - Moving the line can cut the neck.
  - Fence extensions and robin raycasts sit on the Default layer.
  - **The player has never been in water.**
  - The west neck corner step sits at the waterline.
- **Noah's ruling** (`bells-bend-qa-report.md`): **water plus drowning is the out-of-bounds deterrent** for anyone who swims around the barrier. **This is future work.** It's researched here, not built (see the scope note above).

## Phase 0: Comprehensive water research (gate, comes before any build)

**technical-director** leads it, with **technical-artist** and **systems-designer**, using web research with sources cited. They run `bmad-deep-recon` in the **select** shape at the **deep** preset (≥ 6 dimensions). The output is `_bmad-output/research/technical-water-<date>/research.md` with a weighted decision matrix and a pick. It has to answer:

1. **Rendering in Unity 6 URP**, which has no built-in water system (the HDRP Water System is HDRP-only). Compare: a custom Shader Graph or HLSL surface; Unity's BoatAttack URP water; open-source or free URP water such as Crest; free stylized-water assets. For each, record the license (no Asset Store login, nothing that isn't free), Unity 6.6 and URP 17 status, **WebGL2 and WebGPU support**, and whether agents can build it from scripts.
2. **What should this water look like?** A slow, murky, post-collapse Cumberland and drowned lowlands, not a tropical sea. Cover colour and absorption by depth, how much it hides the bed (TD gap 1: fog or absorption versus a seeded bed), foam and shallows from `LevelMaps`, wind ripples, reflections (planar versus screen-space versus probe) and their cost, and refraction.
3. **Movement and waves:** a flat surface with normal maps versus a slight current (it's a river) versus small waves. Whatever is chosen has to be **a deterministic function of time and position** that the server can evaluate too (multiplayer, future boats and floating debris).
4. **The player in water:** how survival games handle wading and swimming, with player reception. Examples: The Long Dark, DayZ, Rust, Subnautica, The Forest, Green Hell. **For later work, research only:** stamina or breath, drowning, feedback without HUD bars, and drowning as a soft world boundary. Write it up as a recommendation for when the character and death systems exist.
5. **Underwater:** the camera crossing the surface, fog and colour, whether diving is in scope for the POC (default: **surface swimming only**, a short duck under allowed).
6. **Budgets and streaming:** the cost of per-tile water quads on the 1024 m grid, the Windows target of 60 FPS or more, the web budgets (≥ 30 FPS, size), and LOD for distant water.
7. **Recommended swim numbers:** wading slowdown and swim speed (built now). Proposed stamina and drowning numbers, and the distance from shore at which you drown, are **recorded for later and not built**.

**Gate:** technical-director approves the tech pick and game-director approves the look, feel and swim numbers. Both are recorded in `studio:decisions`.

## Phase 1: Pass criteria (all must pass)

| # | Criterion | Evidence qa-lead checks |
|---|---|---|
| W1 | **One source of truth.** The surface height comes from `MapConfig.WaterLevelY` at build. There's an EditMode test that the plane Y equals `cfg.WaterLevelY`, and **no literal 1.61 in the code** | The test, plus a grep |
| W2 | **Covers everything and streams.** Per-tile water quads on the 1024 m grid cover the whole terrain extent (X −2087..2009, Z −2492..2628), including the river carved north of the line. No gaps at tile seams | EditMode coverage scan; seam screenshot |
| W3 | **Guarded against terrain changes.** The water build records the lake-mask hash it was built against and **fails on a mismatch**. The "north line moved" warning becomes a **build error** in the water path | Tests that deliberately mismatch the hash and move the line |
| W4 | **Layers.** Water is on built-in layer 4 (Water). Fence extensions move to a new `WorldBounds` layer. Robin, bird and ground raycasts exclude both. **Birds 17/17 (42/60/78) and B5 9/9 re-run green** | Layer audit; test XML |
| W5 | **It looks like the brief.** Foam and shallows come from `LevelMaps` shore distance (no shoreline recomputed in the shader). The flat bed is hidden by about 4–6 m of depth, or a seeded bed goes in per the brief. Bluff faces meet the water cleanly. The west neck step and the 0.31 m lip look right at the waterline | Screenshots from the bank, a bluff top and at water level. **Noah's look check: "reads as the river: right"** |
| W6 | **Wading and swimming.** Shallower than the brief's wading depth, the player wades more slowly. Deeper, the player swims at the surface with the brief's speed. They can climb out on every low bank (the bank rule's lip of 0.31 m or less, under stepOffset 0.4). No getting stuck at the shelf | PlayMode: walk into water, swim 50 m, exit at 10 random low-bank stations |
| W8 | **Swimming around the barrier is impossible.** At both barrier ends, swim attempts along the water past the north line (direct, hugging the bank, diagonal, sprint-then-swim) are all stopped by the full-width backstop or the `WorldBoundsClamp`, **with zero crossings**. Nothing here relies on drowning | PlayMode sweep, at least 20 attempts per end |
| W9 | **The motion model is deterministic.** Surface motion is a pure function `(x, z, t)`, shared by shader and C#, so it's ready for server-side buoyancy. One floating test crate bobs in step with the visual surface (within 5 cm) | EditMode test comparing the C# sample with a shader readback or the analytic formula; PlayMode crate test |
| W10 | **Budgets.** Windows build ≥ 60 FPS at 1080p walking along the shore, park to bluff. Web build ≥ 30 FPS, and it still builds and deploys through `deploy-web.ps1`. If either misses, cut reflections and resolution before failing | FPS JSON for both, build logs |
| W11 | **No regression.** WorldWalk, wildlife, birds, WorldBounds (B5, clamp) and the Bells Bend L1–L4 checks all pass. Animals don't path into water. Robins don't land on it | Full test XML; NavMesh check |

## Automatic FAIL

- Building anything before Phase 0 is approved.
- Any tool that needs an Asset Store login or isn't free, or a license that isn't recorded.
- A hard-coded water height.
- A swim-around crossing (W8).
- Building drowning, stamina, death or respawn (out of scope).
- Breaking the web build.
- The same blocker 3 times in a row, or more than 3 assists.

## What the pod hands back

1. **The water research report and decision matrix,** a reusable baseline for boats, rain and floods later.
2. A verdict from qa-lead covering W1–W11, Noah's look and feel ratings, and friction-log rows.
3. A technical-director note on what this water choice means for boats, the arrival-by-wreck spawn, and the server-side physics.

## Pod

Run `/studio-pod` from `C:\Tools\ruflo\studio`, with the project path set to `E:\GitHub\washed-ashore`.

| Role | Owns |
|---|---|
| technical-director | Phase 0 lead, tech pick, W3 and W9 review |
| technical-artist | Shader and look, foam and shallows, bed hiding, budgets (W2, W5, W10) |
| systems-designer | Swim research and numbers. Write up drowning and stamina research for later (Phase 0 items 4 and 7) |
| gameplay-engineer | Wading, swimming, the crate, swim-around tests (W6, W8, W9) |
| level-designer | Water quads per tile, hash and line guards (W1–W3) |
| platform-tools-engineer | Layers and ProjectSettings as integration owner (W4), web deploy check |
| qa-lead | Verdict, Noah's look and feel checks, regression (W11) |
| game-director | Approves the look and the swim numbers (gate) |
| executive-producer | Schedule, assist count, final report |
