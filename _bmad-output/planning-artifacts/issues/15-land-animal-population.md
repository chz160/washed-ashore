Land animals exist only in one 512 m patch near Bells Bend Park. Make them populate the whole world around the player as it moves, the way fish do, using a method chosen through research. **It must not block multiplayer.**

## Today (checked 2026-10-14)

- **Baked in the editor, not planned at runtime.** `WildlifePlacer.Run` / `BakeIntoScene` (`Assets/Editor/Wildlife/Level/WildlifePlacer.cs`) bakes the herds into `World.unity` for `bakedSeed` 101. `WildlifePopulation.Awake` only re-plans (`WildlifeRules.PlanGroups`) when a test overrides the seed.
- **Placement window:** `HabitatGround.Around(spawn)` is a **512 × 512 m** window around `PlayerSpawn`. `minFromSpawn` is 40, and wolves have their own `wolfMinFromSpawn` of 100.
- **Species:** Deer, Stag, Fox and Wolf, in herds. **Flee distances differ by species** (alert / trigger / release, in metres):

  | Species | Alert | Trigger | Release |
  |---|---|---|---|
  | Deer | 50 | 35 | 70 |
  | Stag | 55 | 40 | 75 |
  | Fox | 35 | 25 | 50 |
  | Wolf | 60 | 45 | 60 |

- **The NavMesh covers about 1–1.5% of the map.** It's one Volume bake of 512 × 512 m (`WildlifeNavMesh.asset`, centre about (-152, 18, -891)). The map spans X -2087..2009 and Z -2492..2628. The code notes that "baking every Bells Bend tile would take minutes".
- **No fixed start point (Noah, 2026-10-25).** The final game will have no single spawn point; players will likely wash up at random places on the shore. Today the scene's dev start is `TowerBaseSpawn` at the lookout tower, about 1 km from the `PlayerSpawn` marker in Bells Bend Park that animals are still placed around. Tests place the player wherever they need with `TestSpawn` (#19).
- **The model to learn from:** `FishPopulation.cs`:
  - 32 m cells whose contents are a pure function of (seed, cell)
  - Cells go live within `simRadiusIn` 50 and are dropped beyond `simRadiusOut` 60
  - At most 2 cell plans per frame
  - A cell changes only while unseen, and a re-entered cell regenerates identically
  - Blocked cells back off

  The fish pod's QA confirmed **0 spawns or despawns in view and about 0 ms cost** (F9 PASS). Its FAILs were about density and tuning, not streaming.
- **Known open problems:** W-QA-1 (wildlife seed 303 drifts in full-suite runs) and the failing `WildlifeFrameTime` and `BirdFrameTime` perf tests.

## Phase 0: deep research (gate, comes before any code)

Run `bmad-deep-recon` at the **deep** preset, shared with #16. Write to `_bmad-output/research/technical-ambient-life-population-<date>/`. It covers how open-world games populate and stream wildlife: RDR2, BotW and TotK, Far Cry, theHunter, Horizon, Ghost of Tsushima, AC, DayZ, Rust, ARK and Valheim. Primary sources (GDC, postmortems, engine docs) rank first.

- [ ] Spawn and despawn rings, cell or chunk grids, spawn budgets, and density per habitat.
- [ ] Persistent against seeded animals, and hybrids (persistent herds plus seeded ambient animals).
- [ ] Simulation LOD: full AI near the player, cheap or statistical far away, none in unloaded cells.
- [ ] Never spawning in view: line of sight, minimum distance, using fog (linear, 120–520 m) and terrain.
- [ ] Ecology: habitat by terrain and vegetation, predator and prey, time of day, herd cohesion, migration.
- [ ] Depletion in a permadeath world that remembers: does a hunted area stay empty, and for how long?
- [ ] **NavMesh at world scale:** a tiled or runtime NavMesh build per cell or tile, against an offline full bake. Bake time and memory.
- [ ] **Multiplayer:** how others own and sync populations (see below).

**Gate:** a recommendation with a decision matrix. Noah picks the method; technical-director approves the NavMesh approach.

## Multiplayer rule to design for

A spawn out of one player's view can still be in another player's view, or right next to them. For example, a wolf spawns behind one player and lands a few metres from their friend.

- [ ] Spawn and despawn checks consider **every** player's view and distance. A cell is live if **any** player is near it.
- [ ] **The server or host owns** the population and its seed; clients don't.
- [ ] Define how rings merge when players are close, and how they behave when players are far apart.
- [ ] List what must sync (positions, states, deaths) and what can stay seeded and local.

## Pass criteria (Phase 1, after the gate)

| # | Criterion | Evidence |
|---|---|---|
| L1 | **Anywhere on the map.** Animals populate around wherever the player is, with no dependency on `PlayerSpawn`, `TowerBaseSpawn` or any other fixed point. The player placed anywhere on the walkable map (shore, park, tower, ridge) gets the same density | PlayMode: place the player with `TestSpawn` at 3 or more spots, including a shore spot; census within the band |
| L2 | **NavMesh coverage.** The NavMesh covers all land where the habitat rules allow animals (≥ 95% of eligible cells), using the method from the gate | Coverage scan |
| L3 | **Never in view.** **0** spawns or despawns inside any tested camera's frustum within line of sight and fog range, over a 20-minute walk at 3 seeds | A PlayMode spawn and despawn log, cross-checked against the frustum |
| L4 | **Density holds.** On a long route crossing at least 6 tiles, animals per km² per habitat stay in the brief's band for ≥ 90% of samples | `TestResults/wildlife-density-route.json` |
| L5 | **Deterministic.** The same seed and route give the same population | A hash compared across two runs |
| L6 | **Multiplayer-ready.** A **simulated second viewer** (a test camera at a distance) blocks spawns and despawns in its view, and keeps cells live | A PlayMode two-camera test |
| L7 | **Budgets.** Windows frame time within the brief's budget at peak population. The `WildlifeFrameTime` test passes, or is re-baselined with technical-director's sign-off | FPS JSON; perf tests |
| L8 | **No regression.** Existing wildlife behaviour tests pass (flee per species), and W-QA-1 is fixed or isolated. **Noah's feel check:** "right" | Test XML; Noah's verdict |

## Automatic FAIL

- Code before the gate.
- Any spawn or despawn in view.
- Client-owned population logic.

## Pod

**systems-designer** (research lead), **gameplay-engineer**, **level-designer** (NavMesh and habitat), **technical-director**, **qa-lead** and **executive-producer**.

Related: #12, #13, #16 (shared research and code), #9 and #11.
