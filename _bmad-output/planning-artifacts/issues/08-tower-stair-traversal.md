Follow-up to #7. The tower stairs are walkable and the speed is approved, but the traversal can be better. **Keep this small, driven by how it feels:** one gameplay engineer per round. Noah walks the Windows build and names what feels wrong, the engineer fixes it, reruns the tests and rebuilds. No pods. The first attempt (guided lanes plus a whole-tower layout search) ran a ten-agent pod and ended in a spec FAIL.

## Where it stands (checked 2026-10-14)

- **Ramps:** `Assets/Editor/Lookout/WalkableTowerBuilder.cs` builds one invisible ramp per flight under `WALK_Ramps` (`RampWidth` 0.70, `RampThickness` 0.30).
- **Movement:** `Assets/Scripts/Lookout/TowerWalkZones.cs` sets `stairWalkSpeed` 1.15 m/s and `landingWalkSpeed` 1.5 m/s, with `stairGroundSnap` 0.35 and a slim capsule on the tower (radius 0.25, height 1.6). Ground speed is normal around the tower.
- **Tests:** `Assets/Tests/PlayMode/TowerWalkTests.cs` passes a base-to-cab climb of **20–40 s** (about 27 s measured). It also checks a grounded descent, and that the player stops within 0.05 m when W is released on flight 3.
- **Interactions:** `Assets/Scripts/Lookout/` (`TrapDoor`, `WindowSash`, `Firefinder`, `Hinge`) and `Assets/Scripts/Interaction/` (`Interactor`, `InteractionPrompt`, `InteractionRules`, `InteractionSettings`).
- **Build:** `BellsBendWindowsBuild.BuildBatch` (`Assets/Editor/Art/BellsBendWindowsBuild.cs`) builds to `Builds/Windows-BellsBend/` and writes a `.provenance.json`.
- **Climb speed is final (Noah's ruling, 2026-10-14).** The current tuning is the target: `stairWalkSpeed` **1.15 m/s** and `landingWalkSpeed` **1.5 m/s**. The last `TowerWalkTests` run (`TestResults/tower-walk.xml`, 2026-10-14) measured **up 26.6 s and down 24.5 s**. The tower spec's C1 bar of 60–120 s is **superseded**, and the test's 20–40 s bar stands. Record this ruling in `_bmad-output/poc/tower-restart-rulings.md`.

## Gate (before round 1)

- [x] **Climb time:** ruled. Keep the current speeds (about 27 s up). See above.
- [ ] **Noah answers the nine re-decide items** in `_bmad-output/poc/tower-restart-rulings.md` §4, or defers each one:
  1. Jump and sprint on the tower
  2. A 0.25 m capsule against the normal one
  3. 0.20 m eye clearance
  4. Invisible 2.2 m edge walls against visible members
  5. Landing guards 3–7
  6. Trap door 1.50 × 0.66 m, duck and hinge setback
  7. Cab step-off
  8. Firefinder eye and sash
  9. Flight mid rails off

## Pass criteria

| # | Criterion | Evidence |
|---|---|---|
| S1 | **No view bump.** On every hairpin turn, while rubbing rail ends at full speed, the camera's vertical jitter stays at **≤ 1 cm** (today it reaches 5 cm) | A TowerWalkTests case logging camera Y per frame on each turn |
| S2 | **The open hatch is safe.** The player can't fall through the open trap door from the cab. Climbing down through it is deliberate (look down plus W), not an accident | PlayMode: walk across the open hatch in 8 directions; 0 falls |
| S3 | **Interactions work on the walkable tower.** Hatch open and close, sash open and close, and firefinder enter, rotate 90° and exit all pass on the current tower | PlayMode cases added to TowerWalkTests (or a sibling suite) |
| S4 | **No steering bumps.** In flights with 0.62–0.72 m clearance, holding W plus 30° of mouse steer gives **0 contacts that change speed by more than 20%** | A PlayMode contact log per flight |
| S5 | **Climb speed unchanged.** `stairWalkSpeed` stays 1.15 and `landingWalkSpeed` stays 1.5. Up time stays within 26.6 s ±10%, and the TowerWalkTests 20–40 s bar is kept. **Any change to these speeds is an automatic FAIL**, unless Noah asks for it | Diff of `TowerWalkZones.cs`; TowerWalkTests up and down times |
| S6 | **Old approach removed.** `Assets/Scripts/Gameplay/Climb/` (ClimbArea, ClimbRules, ClimbTuning, PlayerController.Climb, StairLane) and the Lookout suites that test it are run once. Then every file is either deleted or rewritten for the ramps, with a one-line reason each. The Lookout suites are in `Assets/Tests/EditMode/Lookout/` (7 files) and `Assets/Tests/PlayMode/Lookout/` (10 files). Today these fail 21 EditMode and 24 PlayMode tests (`TestResults/pod14`, 2026-10-17). The 20 failures outside the Lookout suites are tracked in #19, not here | Triage table in the PR; 0 failures in the Lookout suites (EditMode and PlayMode), and no new failures elsewhere |
| S7 | **Colliders are pre-baked.** The 40 tower collision meshes are pre-baked (builder import settings), with **0 "not pre-baked" warnings** in the build log | Build log |
| S8 | **Noah's feel check:** "the climb feels right" | Noah's verdict in the PR |

## Out of scope

Changing the climb speed (tuned by Noah), the old guided-path approach, any spec criterion outside these items, and multiplayer sync of doors.

## Session prompt

> Pick up issue #8 in E:\GitHub\washed-ashore. Start from the invisible ramps, `TowerWalkZones` and `TowerWalkTests`; don't bring back the guided path. One gameplay engineer only. This round, fix: [what felt wrong]. Run TowerWalkTests, build Windows, and I'll walk it.
