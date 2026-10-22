**Bug (Noah's manual tower check, 2026-10-21):** on the lookout tower, the hatch, the window sashes and the firefinder do nothing. There's no prompt and E does nothing. The hatch already stands open and can't be closed from the cab.

## What we know

- **They were never added to the walkable tower.** No scene or prefab references `TrapDoor`, `WindowSash`, `Firefinder`, `Interactor` or `InteractionPrompt`. Checked by script GUID on `main` 144f54c and on the `player-character` branch. `Assets/World/Lookout/LookoutTower.prefab` has only `TowerWalkZones`.
- **This dates from the restart (commit 99a9d73, PR #7).** Its message says the interaction and lookout scripts from the first attempt were included, but "hatch, sash and firefinder interactions are not yet tested on this tower". The older Lookout EditMode and PlayMode suites "compile but were not run and mostly test the retired guided path".
- **The old setup step won't work as is.**
  - `LookoutGameplaySetup.Apply` (`Assets/Editor/Lookout/LookoutGameplaySetup.cs:44-46`) adds the trap door, sashes and firefinder, but it also builds the `ClimbArea` lanes of the retired guided path.
  - `WalkableTowerBuilder.cs:19` says the restart deliberately adds no `ClimbArea`.
  - `TrapDoor` blocks the cab exit lane through `climbArea` (`TrapDoor.cs:138`).
  - So the interactions need setting up without the guided path, and the trap door needs another way to stop the player while it's closed (for example its leaf collider).
- **The player has no Interactor either.** "Washed Ashore/Lookout/Apply Interactor To Player" (`LookoutGameplaySetup.cs:243`) adds the `Interactor` and `InteractionPrompt` to the player camera and saves the scene, but it was never run on `World.unity`.
- **Not a player-character pod regression.** The pod's only tower change is `TowerWalkZones.cs:61` (it saves `StandingHeight` on entry). The pod's H8 check, "hatch, sash and firefinder still work", rests on a manual check that fails for this reason; the director rules on how H8 records it.

## Pass criteria

| # | Criterion | Evidence |
|---|---|---|
| T1 | The builder adds the interactions with no `ClimbArea` or guided path, from the model's nodes and the manifest, with nothing placed by hand. Running it twice changes nothing | Builder source; a rebuild diff with no changes |
| T2 | The player has the `Interactor` and prompt, set by script, saved in `World.unity` | Scene YAML holds the GUIDs; an EditMode test |
| T3 | The hatch starts closed. Aim at it and the prompt reads "Open"; press E and it opens; step into the cab; from the cab it reads "Close"; press E and it closes. Closed, it holds the player like a floor, from above and below | PlayMode test on the real tower; Noah's manual check |
| T4 | Every sash opens and closes on E with the right prompt, and the body doesn't clip through the frame | PlayMode test; manual check |
| T5 | Firefinder: "Use", E enters the view, the alidade turns by mouse or stick, and E or the leave key exits back to walking at normal eye height, standing, with the body back | PlayMode test; manual check |
| T6 | The Lookout suites are brought up to date for the walkable tower: guided-path tests retired by name with a reason, and interaction tests passing | Lookout EditMode and PlayMode results |
| T7 | Tower walking is unchanged (TowerWalkTests within its bar) | TowerWalk results |

## Automatic FAIL

- Bringing back the guided path or `ClimbArea` to make the interactions work.
- Hand-placed components or values in the scene.
- Any HUD beyond the existing single prompt line.

## Pod

**gameplay-engineer**, **level-designer** (builder and scene), **qa-lead**; **technical-director** rules on how the closed hatch stops the player.

Related: #7 (tower restart), #8 (stair traversal), #19 (PlayMode tests broken by the spawn move); the player-character H8 manual check (`TestResults/player-character/h9/noah-session-20261020-ratings.md`).
