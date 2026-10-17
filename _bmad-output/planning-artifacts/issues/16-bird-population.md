Birds come from one saved layout in fixed patches, so most of the map has none. Make them populate the world around the player as it moves, like fish and the planned land-animal system (#15), with **settings for the sky**: flocks are visible much farther away, and for longer, than animals on the ground. **It must not block multiplayer.**

## Today (checked 2026-10-14)

- **Saved layout:** `BirdPopulation.cs` uses the saved `BirdLayout` when `layout.seed == ActiveSeed` (seed 101), and re-plans with `BirdPlacementRules.Plan` otherwise. It places robins at validated start points and one crow `Flock` per flock plan, all before the first frame. The rules are `MinGrass` 0.6, `MaxSlope` 15, trees 6–30 (bushes count), `RockClear` 3, and one flock POI per third of the wildlife route.
- **Robins:** `RobinAgent.cs` flushes and flies away, then hides when far and unseen. It reappears at the **same start point** after `respawnSeconds` **30**, once the player is beyond `minRespawnDistance` **40** and not looking. The view test is `GeometryUtility.TestPlanesAABB` (frustum only, no line of sight). At most 2 flushes per run.
- **View limits:** the camera far clip is 1000 m, and the lookout cab sees about 1.15 km. **Fog is linear from 120 to 520 m.**
- **The model:** `FishPopulation.cs` (32 m seeded cells; live within 50 m, dropped beyond 60 m; changes only while unseen; F9 PASS).
- **Known open problem:** the `BirdFrameTime` perf test is failing.

## Why birds need their own settings

- A flock in the sky can be seen past any 50–60 m ring, often against open sky. **Spawns must happen beyond fog (more than 520 m) or out of line of sight, or fly in from the edge,** never appear in place.
- **The lookout cab sees the whole valley.** Spawns must stay hidden from up there too.
- Ground birds (robins) behave like small land animals. Flocks in flight need their own rules and tuning.

## Phase 0: deep research (gate; shared with #15)

Write it into the same `technical-ambient-life-population-<date>/` report, as a bird section. Cover RDR2, BotW and TotK, Far Cry, Horizon, Ghost of Tsushima, Microsoft Flight Simulator and theHunter, with primary sources first.

- [ ] Flying in from beyond fog or behind terrain, against spawning in place. Spawn rings sized by altitude and visibility.
- [ ] Simulation LOD: full flocking near the player, cheap distant flocks (impostors, particles, GPU).
- [ ] Ground birds and flocks: perches, landing spots, roosts, and how they connect.
- [ ] Persistence against seeded birds, matched to #15's method.
- [ ] Time of day, weather and seasons (note what's relevant now).
- [ ] Budgets for flock size, update rate and draw cost.

**Gate:** Noah picks the method, consistent with #15.

## Multiplayer rule to design for

A spawn hidden from one player may be in plain sky for another player far away, or for someone in the lookout cab.

- [ ] Spawn, despawn and the robin's return check **every** player's view and distance, including line of sight to the sky.
- [ ] The server or host owns the seed and the population.
- [ ] List what must sync (flock paths, flushes) and what can stay seeded and local.

## Pass criteria (Phase 1, after the gate)

| # | Criterion | Evidence |
|---|---|---|
| B1 | **Anywhere on the map.** Birds populate the whole map, with no dependence on fixed patches. Starting at the tower or at the park gives the same density | PlayMode: census from 3 spawns |
| B2 | **No pop-in from the ground.** 0 flock or robin appearances or disappearances in view inside fog range, over a 20-minute walk at 3 seeds. Flocks enter by flying in | Spawn log cross-checked against the frustum and line of sight |
| B3 | **No pop-in from the cab.** The same holds for the camera in the lookout cab: a 360° pan held for 10 minutes | Spawn log from the cab |
| B4 | **Robins return realistically.** Robins no longer respawn at the same start point. They return by the new rules: a seeded new perch, or flying in | PlayMode |
| B5 | **Density holds** in the brief's band (sky and ground separately) on a long route, ≥ 90% of samples | `TestResults/bird-density-route.json` |
| B6 | **Deterministic and multiplayer-ready.** Same seed, same birds. A simulated second viewer blocks spawns in its view | Hash; two-camera test |
| B7 | **Budgets.** Frame time within the brief's budget. `BirdFrameTime` passes, or is re-baselined with technical-director's sign-off | FPS JSON; perf test |
| B8 | **Noah's feel check:** sky and ground birds both "right" | Noah's verdict |

## Automatic FAIL

- Code before the gate.
- A bird appearing or disappearing in view, from the ground or the cab.
- Fixed-spot robin respawn left in place.

## Pod

Share it with #15 where possible: **systems-designer**, **gameplay-engineer**, **technical-artist** (distant flocks and cost), **qa-lead** and **executive-producer**.

Related: #15, #12, #13, #9 and #11.
