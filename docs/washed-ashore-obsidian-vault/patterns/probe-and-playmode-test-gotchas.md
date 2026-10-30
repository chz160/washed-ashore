---
type: pattern
status: active
tech: [probe-tests]
area: [player]
issues: [19, 46, 78, 81, 91]
prs: [65, 79, 82, 85]
source: "PR #65, #79, #85; issues #78, #81, #91; studio:engineering adr/lookout-interactions-40 parts 80-98 (input and teleport items, not re-read for this note)"
created: 2026-10-30
updated: 2026-10-30
aliases: [test gotchas, TestSpawn, known-fail]
---
# Probe and PlayMode test gotchas

_Unity PlayMode tests and movement probes give false results in a few repeat ways: lost key presses, start poses pushed by overlap, tests that depend on where the scene starts, and known-fail cases that never really bite. Each has a known fix._

## Problem
A test passes or fails for a reason that has nothing to do with what it is meant to check.

## When it applies
Any PlayMode test that places the player, drives input, or measures movement. That covers the Lookout suites, `TowerWalkTests`, the #64 `ControllerProbe64` and the #46 replay tests.

## The gotchas

**1. Tests must choose their own start point.**
- What went wrong: PR #7 moved the scene start about 1,030 m to `TowerBaseSpawn`, and 20 tests broke (#19). They assumed the player starts at `PlayerSpawn`, where wildlife and walk lanes are laid out.
- Fix: place the player through `TestSpawn` (`Assets/Tests/Support/`): `At("PlayerSpawn")`, `AtTowerBase()`, `At(position, yaw)`, `Reposition`. It runs `PlayerController.SpawnAt`, which snaps to ground and turns the `CharacterController` off and on.
- Never write the player's transform directly. The two recorded exceptions are `StanceTests.Respawn_ResetsTheStance` and `LookoutMovers.Place` (PR #65, #79). See [[0004-no-single-spawn-point]].

**2. Start poses inside geometry get pushed.**
- What happens: a capsule placed slightly inside a wall, rail or floor is pushed out by the controller. The Lookout start poses move 0.12-0.15 m (example: miss 0.131 m, #78). F8 probe starts miss by 0.111-0.113 m, scaling with ramp slope (#80).
- `TestSpawn.At` checks placement to 0.1 m. That check failed 8 Lookout tests in #63, so `LookoutFixture.StartAt` falls back to `Reposition` and logs the miss (#78, still open).
- Fix: move the pose clear and assert that the start overlaps only the floor. A probe start that is off the lane makes the row invalid. Row 30 was 0.056 m off and read INVALID until #91 moved it to lateral 0.

**3. Key presses in the same frame are lost** (adr/lookout-interactions-40, #40).
- With `InputTestFixture`, two Press/Hold calls in one frame lose the first key. Put a frame between them.
- A Release lands only on the next input update.

**4. Known-fail cases must bite on a live state** (PR #85, #46).
- What went wrong: in #46 the scripted tower stream walked into a wall at tick 221 of 360. Every run then ended at the same point, so the frame-rate and replay tests passed without testing anything.
- Fix: print position and speed during the run, and measure while the stream is still moving.
- Order the asserts so the known-fail trips the assert it exists for, not an earlier guard.
- Every new test in PR #83 and #85 carries an `[Explicit]` known-fail case that fails by a printed margin.

**5. Tests that wait a fixed number of frames break under a fixed tick.** PR #85 pinned 18 such tests to one tick per frame (`TickRatePin`). No assert values changed.

**6. Know a probe's limits before using it as an acceptance test.** Open in #81 for `ControllerProbe64`:
- The overlap check reads only the first collider on each wall object.
- The repeat check compares end positions, not stop timing.
- The 30° fixture ramp gives false stops.
- `Detail()` calls `OverlapSphere` with no mask, which breaks physics-guidelines rule 16.

## Does not apply when
The test checks the start itself, for example C2a "the scene's dev start puts the player on the ground" (PR #65).

## Related
[[stairs-and-ramps-with-a-charactercontroller]], [[collider-geometry-for-a-steered-capsule]], [[fixed-tick-player-sim]], [[player-controller]]
