---
type: pattern
status: active
tech: [multiplayer-ready, character-controller]
area: [player]
issues: [46, 41]
prs: [83, 85]
source: "Issue #46 (body and comment 2026-10-28); PR #83 and #85"
created: 2026-10-30
updated: 2026-10-30
aliases: [fixed tick, PlayerCommand, PlayerTickClock, PlayerRegistry]
---
# Fixed 60 Hz player tick

_The player simulates on a fixed 60 Hz tick from an input command, and rendering interpolates between ticks. This makes movement the same at any frame rate and replayable to within 1 mm, which is what server-authoritative multiplayer needs. No networking is built yet (#13)._

## Problem
Before #46, the player moved in `Update` with a variable `Time.deltaTime`, reading input inside the controller. Results then depend on frame rate. One case: at sub-millisecond frames, the landing 6 join stepped the player up by the full 0.25 m step offset. A server and client also can't replay the same steps.

## When it applies
- All player movement, stance, swim and tower-zone code.
- Anything that hooks the sim: tower zones, the hatch duck, the bounds clamp.
- Standing rules 1, 2 and 26 in [physics-guidelines](https://github.com/chz160/washed-ashore/blob/main/docs/physics-guidelines.md).

## How it works (PR #85)
- **Input:** a `PlayerCommand` is gathered each frame. The sim never reads InputActions directly.
- **Tick:** a per-player `PlayerTickClock` runs `Simulate(command)` at 60 Hz, at most 10 ticks a frame. `CharacterController` has no internal loop, so the tick calls `controller.Move` itself.
- **State:** `PlayerSimState`, with `CaptureState` and `RestoreState`, is the snapshot for replay.
- **Presentation:** rendering interpolates between ticks. The rendered eye is clamped so it never sits above the current sim eye. That is a recorded departure from plain interpolation, so a stance drop can't show the camera above the capsule.
- **Animator:** reads the sim velocity and grounded flag, not the transform change between frames.
- **Many players (PR #83):** a `PlayerRegistry` replaces `FindGameObjectWithTag("Player")`. Players register in Awake and leave in OnDestroy. `TowerWalkZones` keeps per-player state.

## Evidence
- Same end position from the same command stream at 30, 60 and 144 fps. Tick-by-tick replay within 1 mm for ground, tower, swim and a walk off the tower (`PlayerReplayTests`, PR #85).
- Tower walk up/down: 24.6/23.9 s uncapped and at 144 fps, 25.7/24.5 s at 60 fps. Largest one-frame rise going down: 0.001 m (PR #85).
- Before the tick, repeated probe runs on main already matched to 0.000 mm (4 of 4, same machine and build; #46 comment 2026-10-28).

## What to do
- **Avoid a 30 Hz tick.** At 1/30 the bare-ramp climb loses about 18% of its speed (#46 comment 2026-10-28). Re-verify tower timing at any tick rate other than 60.
- **Expect a stair change to need the tick, and the tick to need the stair change.** See [[stairs-and-ramps-with-a-charactercontroller]].
- **Pin legacy frame-count tests** with `TickRatePin`.
- **Make known-fails bite on a moving state:** [[probe-and-playmode-test-gotchas]].

## Known residuals (PR #85)
- Above 60 fps the camera moves in 60 Hz steps during stance drops (crouch, sit, lie down).
- The jump now peaks at 1.258 m at any frame rate. It was about 1.224 m at 144 fps before.
- PR #83 follow-up: fish still use `FindAnyObjectByType<PlayerController>`, and the `playerTag` tuning fields are unused.

## Does not apply when
Physics bodies that aren't the player. They run on `FixedUpdate` at 0.02 s (rule 24).

## Related
[[player-controller]], [[0006-charactercontroller-kept]]
