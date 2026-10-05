---
title: 'POC Spec: Washed Ashore walk-around — Birds (close-up and overhead)'
status: ready-for-pod
owner: John (PM)
created: '2026-10-05'
depends_on:
  - _bmad-output/planning-artifacts/poc-spec-washed-ashore-wildlife.md   # run after it: shared World.unity + sighting harness
  - _bmad-output/research/technical-cc0-animated-birds-2026-10-05/research.md
---

# POC Spec: Birds

**The question this answers:** can Ruflo's agents add two kinds of birds so the sky and the ground feel alive? Robins hop and peck nearby and fly off when you get close. Flocks circle overhead. The goal is that a 3-minute walk feels natural: never an empty sky, never a swarm.

## Phase 0: Bird density and behaviour research (gate, comes before any placement)

**systems-designer** owns this, using web research with sources cited. It writes `_bmad-output/poc/bird-density-brief.md`, which answers:

1. **Real-world baseline:** how many songbirds like robins there typically are per hectare in woodland edges and clearings, and how close a person can get before a ground-feeding bird flushes (its flight-initiation distance). It also covers how big corvid and pigeon flocks are, how high they fly, and how they move (circling, soaring, commuting). Our map is 512 m × 512 m.
2. **What games do:** how open-world and survival games stage ambient birds. Examples include The Long Dark's crows circling carcasses, RDR2 birds by habitat that flush from riverbanks, and distant flocks in Firewatch or Valheim. The brief should cover how often you see birds versus how many exist, spawn bubbles around the player, and where birds get placed (clearings and edges, not deep forest).
3. **The target, as a number we can measure:** how often a flock is in view (% of samples), the longest the sky can stay empty, how many ground birds you meet per minute, how many flush events happen per 3-minute walk, the most birds in view at once, flock count and size, and what share of the time the flocks glide versus flap.
4. **Where they go:** which kinds of terrain get ground birds (open grass, edges near trees), and where flocks hang (over clearings, ridgelines, any point of interest).

**Gate:** game-director approves the brief's numbers before Phase 1 starts. The brief may tighten the outer bounds below but may not go past them.

## Fixed inputs and outer bounds

| Item | Value |
|---|---|
| **Close-up bird** | SoltorchGames American Robin: `vendor/LookToTheBirds_FreeSample_AmericanRobin_v1.0.zip` (FBX, 22 clips) |
| **Overhead bird** | Peripheral Arbor Pigeon (public domain): `vendor/Pigeon - Peripheral Arbor (Public Domain)/bird.blend`. Clips: Flapping, Gliding, Takeoff, Landing, Standing Idle |
| Pigeon export | **A script exports the FBX** with `blender.exe -b` (Blender 5.2.2). It keeps only the 76 deform bones and leaves out `metarig` and `WGT-*`. **The export script lives in the project** (`tools/blender/export_pigeon.py`) |
| Pigeon look | **Recolour it dark (crow or raven)** with a URP Lit material variant. Don't edit the source texture |
| Outer bounds | Ground robins **4–30** total. Flocks **1–4** at once, **3–12 birds each**. At most **20 birds in view at once** |
| Out of scope | Bird sounds, birds landing in trees (unless trivial), birds the player can interact with or hunt |
| **License rule** | Copy the Robin's `LICENSE.txt` into its imported folder. If the project gets a git repo, `vendor/` and the Robin's asset folder are **git-ignored or the repo is private**. The license bans redistributing the files |

## Pass criteria (all must pass)

| # | Criterion | Evidence qa-lead checks |
|---|---|---|
| B1 | The bird brief exists, cites sources, has measurable targets, and has game-director's approval | Brief file plus an approval line in `studio/decisions` |
| B2 | **The pigeon export can be repeated.** Running the export script from a clean checkout produces an FBX with 76 bones and 5 clips that Unity imports as a Generic rig with no errors | Script plus import log; an `eval` listing the clip names and bone count |
| B3 | **The Robin's ground behaviour.** Robins stand in the habitat the brief chose and cycle through Idle, PeckGround, Hop, Preen and ScratchGround, with variations so they don't move in sync | PlayMode test: over 60 s, every Robin plays at least 3 distinct clips, and no two neighbours are in the same clip at the same normalised time |
| B4 | **The Robin flies off when you approach.** When the player comes within the brief's flight-initiation distance, the Robin plays Flutter, then Fly, climbs away from the player, and either lands again 20 m or more away or despawns out of view. It doesn't clip through trees or terrain | PlayMode test: approach a Robin, then check that its distance from the player grows, that it gains height, and that no collider overlaps along the way |
| B5 | **Flocks overhead.** The recoloured Pigeons circle or flock (boids or orbit) at the brief's altitude band. They alternate Gliding and Flapping, with Flapping while climbing and Gliding while level or descending. Birds in a flock never overlap one another and never dip below the tree canopy except when taking off or landing | PlayMode test: sample altitudes, gaps between birds and animation state against climb rate over 60 s |
| B6 | **The sighting numbers hit the brief's targets.** This reuses or extends the wildlife sighting harness: a 3-minute scripted walk, 3 seeds, sampling every 0.5 s. It reports flock-in-view %, the longest empty-sky gap, ground birds met per minute and flush events | `TestResults/bird-sightings.json`. Every run lands inside the target bands |
| B7 | **Noah's feel check.** Noah walks for 3 minutes in the Windows build and rates the **sky** and the **ground birds** separately as too empty, right, or too busy. Both must be "right". Up to 2 retunes are allowed (each retune re-runs B6) | Ratings logged in the QA report |
| B8 | **No regression.** WorldWalkTests and the wildlife tests still pass. The Windows build exits 0. FPS is no more than 10% below the build without birds. **The web build (if it exists) still meets web-spec W7 (30 FPS or more) and W4 (50 MB or less)** | NUnit XML, build logs, FPS comparison, web build size |
| B9 | **License and setup scripts are kept with the project.** The Robin license is copied in, the git rule is honoured, and every import or setup script is in `Assets/Editor/` or `tools/`, not in a scratchpad | File check by qa-lead |

## Automatic FAIL (stop and report)

- Placing birds before Phase 0 is approved, or going past the outer bounds.
- Editing the vendor source files, or putting Robin files into a public repo.
- The same blocker goes 3 attempts with no progress. **Fallback before failing B5:** if the pigeon rig won't import, use C#-rotated wing parts on the pigeon mesh for the flocks, logged as a deviation.
- More than 3 assists.

## What the pod hands back

1. A verdict from qa-lead (**PASS**, **PASS WITH ASSISTS (n)** or **FAIL**) with evidence for B1–B9.
2. **The bird density brief and the sighting JSON**, the tuning baseline for the real game.
3. Friction-log rows appended to `_bmad-output/poc/friction-log.md`. Flag in particular anything about the Blender-to-Unity export.

## Pod

Run `/studio-pod` from `C:\Tools\ruflo\studio`, with the project path set to `E:\GitHub\washed-ashore`. **Run it after the wildlife pod,** because both share `World.unity` and the sighting harness.

| Role | Owns |
|---|---|
| systems-designer | Phase 0 research and brief, flush distance and flock numbers, B7 retunes (B1) |
| game-director | Approves the brief (quality gate) |
| technical-artist | Blender export script, Robin and Pigeon import, crow material, Animator setup (B2, B9) |
| gameplay-engineer | Robin idle and flush behaviour, flock movement and glide/flap logic, behaviour and sighting tests (B3–B6) |
| level-designer | Robin habitat zones, flock anchor points (B3, B5 placement) |
| qa-lead | Verdict, feel check with Noah, regression and license checks (B6–B9) |
| executive-producer | Schedule, assist count, final report |
