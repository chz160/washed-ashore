---
title: 'POC Spec: Washed Ashore walk-around — Believable wildlife'
status: ready-for-pod
owner: John (PM)
created: '2026-10-04'
depends_on:
  - _bmad-output/planning-artifacts/poc-spec-washed-ashore-walkaround.md
  - _bmad-output/poc/qa-report.md   # S1 not met: prefabs imported and rescaled, but none placed and no NavMesh
---

# POC Spec: Believable wildlife

**The question this answers:** can Ruflo's agents populate the world with animals so that a 3-minute walk **feels** like wild country? That means not empty and not a petting zoo, with the right number of animals decided by research and checked by measurement.

**Starting point:** 12 Quaternius animal prefabs are already imported and rescaled (`Assets/ThirdParty/Quaternius/Animals/`). None are placed, and there's no NavMesh (see `qa-report.md` S1).

## Phase 0: Density research (gate, comes before any placement)

**systems-designer** owns the research, using web research with sources cited. It writes `_bmad-output/poc/wildlife-density-brief.md`, which answers:

1. **Real-world baseline:** typical densities of the species we have (deer and stag, fox, wolf) per km², and how they group (herds, pairs, solitary). Our map is **512 m × 512 m (about 0.26 km²)**.
2. **What games do:** how open-world survival and exploration games (for example Red Dead Redemption 2, The Long Dark, theHunter, Far Cry) make wildlife feel present without crowding. Look at spawn bubbles around the player, grouping, how often you meet animals versus how many exist, and density by habitat.
3. **The target, as a number we can measure:** a recommended **sighting rate** (how often at least one animal is in view during a walk), how long the gaps between sightings can be, how many animals are in view at once, and the total population and grouping per species.
4. **Which species fit:** the pack includes farm animals (Cow, Bull, Donkey, Alpaca, Horse, Husky, Shiba Inu). **The default is wild species only (Deer, Stag, Fox, Wolf).** The brief may argue for others only with a reason tied to the setting.

**Gate:** game-director approves the brief's target numbers before Phase 1 starts. The research may tighten the outer bounds below but may not go past them.

## Fixed inputs and outer bounds

| Item | Value |
|---|---|
| Species | Deer, Stag, Fox, Wolf (default; see Phase 0, item 4) |
| Tech | `com.unity.ai.navigation` (already in manifest), NavMeshSurface over the terrain, NavMeshAgent + Animator per animal |
| Behaviour scope | Wander, idle or graze, keep to the herd, **flee the player** (deer and fox) or **keep their distance** (wolf). No combat, no AI attacks, no sound. |
| Outer bounds | Total population **6–40**. At most **8 animals in view at once**. Every species appears at least once in a 3-minute walk. |
| Reproducibility | **All animal setup scripts live in `Assets/Editor/`.** This fixes the TD-note gap where import and prefab scripts existed only in the agents' scratchpad. |

## Pass criteria (all must pass)

| # | Criterion | Evidence qa-lead checks |
|---|---|---|
| A1 | The density brief exists, cites sources, sets measurable targets, and has the game-director's approval | Brief file plus an approval line from game-director in `studio/decisions` |
| A2 | The NavMesh is baked over the walkable terrain, and every placed animal is on it | `eval`: NavMesh triangle count > 0; each agent's `isOnNavMesh` |
| A3 | The population matches the brief, within the outer bounds, grouped as the brief says (for example deer in herds of 3–6, wolves in pairs) | `eval`: count per species and group, cross-checked against the brief |
| A4 | Animals move with matching animations (idle or graze while still, walk or run while moving) and don't slide, float, get stuck or clip into the terrain for more than 5 s | PlayMode test: over 60 s, every agent's velocity and animation state agree, and its distance to the ground stays within tolerance |
| A5 | Deer and fox flee when the player comes within the brief's threshold, and wolves keep their distance | PlayMode test: move the player toward a herd and check that the distance grows |
| A6 | **Sighting metrics hit the brief's targets.** A scripted 3-minute walk along a fixed route samples every 0.5 s and counts animals that are in the camera frustum, within 80 m, and not blocked from view (raycast). Run it 3 times with different seeds. | PlayMode test writes `TestResults/wildlife-sightings.json`. Every run must land inside the target bands. |
| A7 | **Noah's feel check.** Noah walks for 3 minutes in the Windows build and rates it **too empty / right / too busy**. Only "right" passes. If he says "too empty" or "too busy", the pod tunes the numbers and re-runs A6 (at most 2 retunes). | Rating logged in the QA report |
| A8 | No regression: the existing WorldWalkTests pass, the Windows build exits 0, and FPS is no more than 10% below the build without animals. **If the web build exists, the web spec's W7 (30 FPS or more) still holds.** | NUnit XML, build log, FPS comparison |

## Automatic FAIL (stop and report)

- Placing animals before Phase 0 is approved, or going past the outer bounds.
- Leaving any animal setup only in a temporary scratchpad instead of `Assets/Editor/`.
- The same blocker goes 3 attempts with no progress, or there are more than 3 assists.

## What the pod hands back

1. A verdict from qa-lead (**PASS**, **PASS WITH ASSISTS (n)** or **FAIL**) with evidence for A1–A8.
2. **The density brief and the sighting JSON**, which become the starting point for tuning wildlife in the real game.
3. Friction-log rows appended to `_bmad-output/poc/friction-log.md`.

## Pod

Run `/studio-pod` from `C:\Tools\ruflo\studio`, with the project path set to `E:\GitHub\washed-ashore`. **Wait until the web-build pod has finished,** because both pods change `World.unity`.

| Role | Owns |
|---|---|
| systems-designer | Phase 0 research and brief, behaviour tuning numbers (A1, A7 retunes) |
| game-director | Approves the brief (quality gate, not a pod member) |
| level-designer | NavMesh bake, herd placement by habitat (A2, A3) |
| gameplay-engineer | Wander, flee and distance behaviour, Animator wiring, sighting and behaviour tests (A4, A5, A6) |
| technical-artist | Animator culling, frame-rate budget (A8) |
| qa-lead | Verdict, runs the feel check with Noah (A6, A7, A8) |
| executive-producer | Schedule, assist count, final report |
