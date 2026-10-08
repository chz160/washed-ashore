---
title: 'POC Spec: Fish in the waters around Bells Bend (research, then build)'
status: ready-for-pod
owner: John (PM)
created: '2026-10-07'
depends_on:
  - _bmad-output/planning-artifacts/poc-spec-washed-ashore-water.md
  - _bmad-output/poc/water-td-note.md      # surface, WaterMotion, WaterMaps (shore distance + bed depth), camera never underwater (r2)
  - _bmad-output/poc/water-qa-report.md    # swim/wade rulings
  - CLAUDE.md                              # model-facing rule for rigged FBX
---

# POC Spec: Fish

**The question this answers:** can Ruflo's agents fill the river around Bells Bend with fish that seem fairly realistic? That means the right kinds of fish in the right places by distance from shore and depth, behaving and showing themselves the way river fish do, with numbers set by research rather than guesswork.

## Fixed inputs and constraints (decided)

| Item | Value |
|---|---|
| Asset | `vendor/Animated Fish Pack by @Quaternius-...zip`, **CC0**. Contents: **Fish1** (295 faces), **Fish2** (270), **Fish3** (a clownfish, 356), Shark, Whale, Dolphin, Manta ray. **Each has a single `Swim` clip** (31 frames) and a 6–8 bone armature |
| Species fit | The water is the **Cumberland River (fresh water)**. **Default: Fish1 and Fish2 only**, as stand-ins for real river species through scale, tint and material variants. The clownfish, shark, whale, dolphin and manta ray are **excluded** unless game-director rules the lore has turned this water brackish or marine |
| Water facts the fish must respect | Surface = `MapConfig.WaterLevelY` + `WaterMotion.Offset(x, z, t)`. `WaterMaps` gives shore distance and bed depth. **Bed profile:** 0.3 m lip → shelf to wading depth (W − 2) → ramp to W − 10 by 20 m out → **flat bed at W − 10** beyond. **The camera never goes underwater (ruling r2).** The water is murky |
| Import rule | **CLAUDE.md model-facing rule applies.** Import through `ModelFacingPostprocessor`, then run **Model Facing Report**. Every fish must show `flip` or `keep`; `ambiguous` is a bug. No yaw offsets in code |
| Out of scope | Fishing, catching, eating, killing fish, fish as loot, fish attacking the player, the underwater camera, sound |
| Reproducibility | All import and setup scripts in `Assets/Editor/` or `tools/`. Copy the license into the imported folder |

## Phase 0: Comprehensive fish research (gate, comes before any placement)

**systems-designer** leads it, with **technical-artist** and **technical-director**, using web research with sources cited. They run `bmad-deep-recon` at the **standard or deep** preset and write `_bmad-output/research/domain-fish-<date>/research.md`. It has to answer:

1. **Which species, and where:** the fish of the Cumberland River and Cheatham Lake (state wildlife and USGS sources). Examples include channel and flathead catfish, largemouth, smallmouth and spotted bass, sauger, crappie, bluegill and sunfish, freshwater drum, gar, buffalo, common carp and **invasive silver and bighead carp**. **After 25 years with no fishing and no dam operation, how do the populations change?**
2. **Distribution by distance from shore and depth,** mapped onto our bed profile:
   - **Bank and shelf** (shore distance 0–~10 m, depth to 2 m)
   - **Drop-off and ramp** (2–10 m deep)
   - **Open water** (flat bed at 10 m)
   - **Structure:** the bluff faces, the ferry landing, the slipway, Robertson Island, the mouths of the hollows

   For each band, give the species mix, group size (solitary, loose group or school), typical swimming depth, and **relative density**.
3. **How you'd actually see them:** the camera stays above murky water, so what's realistic? Cover:
   - shadows and flashes in the shallows
   - fish rising, dimpling the surface and jumping (silver carp are famous for it)
   - schools of baitfish disturbing the surface
   - gar basking at the surface
   - how many seconds between sightings is believable

   Also give how far into murky river water you can see (a Secchi depth range), as a target for how deep fish stay visible.
4. **What games do:** how open-world and survival games stage ambient fish without an underwater camera, and how that's received. Examples: RDR2, The Long Dark, Valheim, Sons of the Forest, theHunter: Call of the Wild. Cover spawn bubbles around the player, total population versus what's in view, and LOD.
5. **Technique:** single-clip animation (playback speed tied to swim speed, procedural body sway), boids versus simple steering, **GPU instancing versus Animators** for many fish, whether to simulate only near the player, keeping fish inside their depth band and bed clearance using `WaterMaps`, and **WebGL2 cost**.
6. **Reacting to the player:** how fish scatter when the player wades or swims close, and at what distance.
7. **The targets, as numbers we can measure:** fish per band per 100 m of shoreline, sightings per minute of shore walking, surface events (rises and jumps) per minute, and the most fish visible at once.

**Gate:** game-director approves the species roster and look, systems-designer's numbers are recorded, and technical-director approves the technique. All of it goes in `studio:decisions`.

## Phase 1: Pass criteria (all must pass)

| # | Criterion | Evidence qa-lead checks |
|---|---|---|
| F1 | **A clean import.** Fish1 and Fish2 FBX import, and the Model Facing Report shows `flip` or `keep` for both (no `ambiguous`). The Swim clip loops with no pop at the seam. The license is copied in. The scripts are in the project | `TestResults/model-facing-report.json`; clip check; file check |
| F2 | **A roster that matches the brief.** Every brief species has a prefab variant: one of the two stand-in models, with scale and tint or material per the brief. The excluded models aren't in the scene | Roster table versus prefab list |
| F3 | **Distribution by band.** A census at 3 seeds shows each band's species mix and density **within ±25% of the brief** (bank and shelf, drop-off and ramp, open water, structure). No fish where the bed depth is under the brief's minimum | `TestResults/fish-census.json` with a histogram by shore-distance and depth band |
| F4 | **Stay in the water.** Over 120 s, every fish stays **between the bed + clearance and the surface − margin** (using `WaterMotion`, not a flat Y), never enters land cells, and only breaks the surface during a scripted rise or jump | PlayMode test sampling every fish every 0.25 s: 0 violations |
| F5 | **Believable motion.** Clip playback speed tracks swim speed (no gliding at rest, no frantic tail on a drifting fish). Schooling species keep spacing with no overlaps. Solitary species idle and hover. Fish turn smoothly (no snaps of more than 45° in one frame) | PlayMode: velocity versus animation speed correlation ≥ 0.8; spacing and turn-rate checks |
| F6 | **Scatter.** When the player wades or swims within the brief's distance, fish nearby flee, then settle back over the brief's time | PlayMode: the distance grows, then fish return within the band |
| F7 | **Seen from shore.** A scripted 3-minute shore walk, run at 3 seeds, hits the brief's **sightings per minute** and **surface events per minute**. Fish deeper than the brief's visibility depth aren't drawn | `TestResults/fish-sightings.json` within the target bands; render stats |
| F8 | **Noah's feel check.** Noah does a 3-minute shore walk plus a bluff-top look and rates it **too empty / right / too busy**, and **realistic: yes / no**. He has to say "Right" and "yes". 2 retunes are allowed | Ratings in the QA report |
| F9 | **Budgets and streaming.** Fish are simulated and drawn only within the brief's radius around the player, following the tile streaming. Windows **≥ 60 FPS** on the shore route. Web **≥ 30 FPS** with water and fish in view (this closes the water pod's recorded gap). Web build size is within the deploy limits | FPS JSON for both, build logs |
| F10 | **No regression.** Water W1–W11, swim and wade, birds, wildlife, WorldBounds (B5, clamp) and Bells Bend L1–L4 all pass. Fish don't block or collide with the swimming player. Robins don't target fish | Full test XML |

## Automatic FAIL

- Placing fish before Phase 0 is approved.
- Marine models in the scene without a director ruling.
- A rotation offset in code instead of the facing pipeline.
- Breaking the web build.
- The same blocker 3 times in a row, or more than 3 assists.

## What the pod hands back

1. **The fish research report** (species, bands, numbers), the baseline for fishing later.
2. A verdict from qa-lead covering F1–F10, Noah's ratings, and friction-log rows.
3. A technical-director note on what's needed later for fishing: server authority over fish, hooking a specific fish, and moving from instancing to individual fish when one is caught.

## Pod

Run `/studio-pod` from `C:\Tools\ruflo\studio`, with the project path set to `E:\GitHub\washed-ashore`.

| Role | Owns |
|---|---|
| systems-designer | Phase 0 lead, roster and band numbers, retunes (F2, F3, F8) |
| technical-artist | Import and facing report, variants and tints, instancing and LOD, budgets (F1, F2, F9) |
| gameplay-engineer | Steering and schooling, depth clamping, scatter, rises and jumps, tests (F4–F7) |
| level-designer | Structure anchors (bluffs, landing, slipway, island, hollow mouths) (F3) |
| technical-director | Approves the technique, writes the fishing-readiness note |
| game-director | Approves the roster and look, rules on marine models (gate) |
| qa-lead | Verdict, Noah's checks, regression (F10) |
| executive-producer | Schedule, assist count, final report |
