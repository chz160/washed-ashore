---
type: feature
status: shipped
tech: [character-controller, multiplayer-ready]
area: [player]
issues: [64, 46, 19, 43, 81, 13]
prs: [82, 83, 85, 65, 50]
source: "PR #82, #83, #85, #65, #50"
created: 2026-10-30
updated: 2026-11-11
---
# Player controller

_The player is one Unity `CharacterController` capsule, simulated on a fixed 60 Hz tick from an input command and placed by an explicit spawn call. It is built to be replayable for multiplayer later, with no networking yet._

## Shipped
- **Controller choice:** keep `CharacterController`. No KCC, no slide code (PR #82, [[0006-charactercontroller-kept]]).
- **Fixed tick (PR #85):**
  - `PlayerCommand`, `PlayerTickClock` (60 Hz) and the `PlayerSimState` snapshot.
  - Rendering interpolates between ticks, and the animator reads sim velocity.
  - Replay matches within 1 mm, and the result is the same at 30, 60 and 144 fps.
  - See [[fixed-tick-player-sim]].
- **Any number of players (PR #83):** `PlayerRegistry` replaces tag lookups. Tower zones, the hatch, sashes, wildlife and robins handle every player.
- **Placement (PR #65):**
  - `PlayerController.SetSpawnPoint` and `SpawnAt(position, yaw, snapToGround)`. Both run the controller's own place steps.
  - Tests use `TestSpawn`. See [[0004-no-single-spawn-point]].
- **Layers (PR #50):** the controller and both bodies are on the Player layer, and queries use named masks ([[0003-player-off-default-layer]]).
- **Tuned values that tests guard:** walk 5, sprint 8, jump 1.2, tower stairs 1.15 and landings 1.5 m/s, tower ramp step offset 0.25. The automatic-FAIL lists in #46, #64 and #86 forbid changing these.

## Open
- **#81:** `ControllerProbe64` limits before it is used as an acceptance test.
- **#89:** intermittent stance tests (CrouchLow, Speeds_CrouchWalkSprint).
- **Stance camera:** above 60 fps the camera moves in 60 Hz steps during stance drops (PR #85 residual).
- **Fish lookups:** fish still find the player with `FindAnyObjectByType` (PR #83 follow-up).
- **Networking (#13):** not built (no netcode package; task #3). Prediction and reconciliation are still to write. NGO has no built-in prediction for `CharacterController` ([physics-guidelines](https://github.com/chz160/washed-ashore/blob/main/docs/physics-guidelines.md) rule 2). A pod review on 2026-11-11 (tasks #1-#4) found the guidelines assume one authority and one clock before #13 is settled. Proposed, not accepted: [[0017-one-authoritative-simulation-assumed]], [[0018-one-sim-clock-60hz]], [[0019-blocking-world-state-on-sim-tick]], [[0020-replay-is-side-effect-free]], [[0021-server-validates-client-input]]. Hatch and sash hinges tick on `Time.deltaTime` outside `PlayerSimState` (`TrapDoor.cs:169`, `WindowSash.cs:99`). Cross-machine replay determinism is unmeasured (assumed).

## Rulings
[[0006-charactercontroller-kept]], [[0004-no-single-spawn-point]], [[0003-player-off-default-layer]]
Proposed (not accepted): [[0017-one-authoritative-simulation-assumed]], [[0018-one-sim-clock-60hz]], [[0019-blocking-world-state-on-sim-tick]], [[0020-replay-is-side-effect-free]], [[0021-server-validates-client-input]]

## Patterns
[[fixed-tick-player-sim]], [[stairs-and-ramps-with-a-charactercontroller]], [[probe-and-playmode-test-gotchas]], [[physics-layers-and-collision-masks]]
