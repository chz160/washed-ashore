Prepare player movement and the world systems for server-authoritative multiplayer: **a fixed-tick, input-command player simulation**, and **no single-player assumptions**. Rules 1, 2 and 26 in [`docs/physics-guidelines.md`](https://github.com/chz160/washed-ashore/blob/main/docs/physics-guidelines.md). This is groundwork for #13. It doesn't add networking.

## What we know (audit 2026-10-20)

- **The player simulates in `Update` with a variable `Time.deltaTime`.**
  - `PlayerController.cs:184-235` reads input from InputActions inside the controller, then calls `CharacterController.Move`.
  - Stance, swim and climb-lane code runs there too.
  - There's no input command struct, fixed tick or state snapshot. `ExternalMoveInput` (`PlayerController.cs:60`) is the only way in.
- **NGO has no built-in prediction for a CharacterController.** Prediction and reconciliation have to replay the same input over the same fixed steps on the client and the server.
- **Single-player assumptions:**
  - `FindGameObjectWithTag("Player")` is used in `TowerWalkZones.cs:49`, `TrapDoor.cs:166`, `WildlifeAgent.cs:166` (it treats one player as the only threat) and `WorldBoundsClamp.cs:59`.
  - `TowerWalkZones` caches one player's controller and saved values.
  - `WorldBoundsClamp.Snapped` and `ClimbArea.ActiveCount` are static fields shared by everyone.
  - `Respawn` falls back to `GameObject.Find` (`PlayerController.cs:155`).
- **Animator parameters come from the owner's transform change between frames** (`PlayerCharacterView.cs:151`), so remote copies would jitter. Related to #34.

## Pass criteria

| # | Criterion | Evidence |
|---|---|---|
| P1 | **An input command struct** (move, look, jump, sprint and stance requests, tick number). It's gathered in `Update` and used by the simulation, and the simulation never reads InputActions directly | Diff; code review |
| P2 | **A fixed-tick simulation.** Player movement, stance, swim and lanes run in fixed steps with a fixed `dt`, and the camera and body are interpolated for rendering | Diff; a PlayMode test that gets the same end position at 30, 60 and 144 fps from the same command stream (±1 cm) |
| P3 | **Replay works.** Re-running a recorded command stream from a saved state gives the same result (±1 mm), which is the basis for reconciliation | EditMode/PlayMode replay test |
| P4 | **Many players.** `TowerWalkZones`, `TrapDoor`, `WildlifeAgent` and `WorldBoundsClamp` handle any number of players, and no static per-player state remains. A test with 2 spawned players: both use the tower and both are clamped | Grep for `FindGameObjectWithTag("Player")` and `GameObject.Find` returns 0 hits in runtime code; PlayMode test |
| P5 | **The feel is unchanged:** walk 5, sprint 8, jump 1.2, tower speeds 1.15 and 1.5, TowerWalkTests up time 26.6 s ±10%. Noah's feel check says "same as before" | Test XML; Noah's verdict |
| P6 | **Animator drivers come from simulated velocity and stance,** not the transform change between frames | Diff |

## Automatic FAIL

- Adding networking packages or NGO code; that's #13.
- Changing tuned speeds, or the jump or stair feel.
- A Rigidbody-driven player.

## Pod

**gameplay-engineer** (lead), **technical-director** (reviews the command and tick design against #13), **qa-lead**: 3 agents.

Related: #13, #34, #39.
