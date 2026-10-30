---
type: pattern
status: active
tech: [stairs-ramps, character-controller]
area: [lookout-tower, player]
issues: [8, 41, 63, 64, 86]
prs: [79, 82, 85, 92]
source: "PR #82 and #85; issue #41 comments 2026-10-28/29; studio:engineering adr/64-controller-choice"
created: 2026-10-30
updated: 2026-10-30
aliases: [tower stairs, ramp step offset, rail snag]
---
# Stairs and ramps with a CharacterController

_Walk stairs as invisible ramps under a plain `CharacterController`, and give the ramps a real step offset (0.25 m on the tower). A tiny step offset makes the capsule stall in the crease where the ramp meets a rail. The fix was that tuning value, not a new controller or slide code._

## Problem
Going up the lookout tower stairs angled 10-30° toward the railing, the player stopped dead at the first rail contact. Going down was fine (#41, Noah's walk 2026-10-21).

## When it applies
- Any slope built as a ramp collider with a wall or rail beside it, walked by the player's `CharacterController`.
- Today: the eight tower flights (`WalkableTowerBuilder` builds one invisible ramp per flight under `WALK_Ramps`), with speed and step values set per zone by `TowerWalkZones`.
- The guided-path lanes that came before are gone; see [[0001-guided-path-climb-dropped]].

## Why it happens
The capsule's lower sphere meets the ramp floor (normal y about 0.611) and the vertical rail at its equator: a crease, not a step or edge hit. With `TowerWalkZones.upperStepOffset` at 0.05 the controller can't resolve that crease and stops (adr/64-controller-choice, K1 mechanism).

Not the cause:
- **Mesh vs box colliders.** A box copy of flight 1 with the same tower settings stalls row for row the same way.
- **Frame time.** The stall is the same at dt 1/30, 1/60 and 1/144, so a fixed tick alone doesn't fix it.

## Evidence
- Main before the fix: 42 of 42 angled up-runs on flights 1-7 stuck at the first rail contact, keeping 0-12% of speed (`Probe64_TowerStep`, #41 comment 2026-10-28).
- Step 0.25, nothing else changed: the same 42 runs climb at full angled speed. 0 pops, 0 climbs onto the rail, 0 falls off the ramp in 113 measured runs (same source).
- A slide prototype (project wall-hit normals out of velocity, plus in-tick re-moves) cleared the crease but broke a 1.0 m doorway and made one stop non-reproducible. Not adopted ([[0006-charactercontroller-kept]]).
- Shipped in PR #85 with the fixed tick: tower walk up/down 24.6/23.9 s uncapped and at 144 fps, 25.7/24.5 s at 60 fps (band 23.94-29.26 s). Largest one-frame rise going down: 0.001 m.

## What to do
- Put the ramp step offset at 0.25 (`TowerWalkZones.upperStepOffset`, also set in `LookoutTower.prefab`). `TowerStepOffsetGuardTests` guards it.
- Change step offset and the fixed tick together. Each fails alone (PR #85): a 60 Hz tick with 0.05 sticks at the flight 7 head; 0.25 with per-frame movement pops the player 0.25 m at the landing 6 join at tiny frame steps.
- Don't hide a snag by changing tuned speeds or by making rails walk-through. Both are automatic FAILs in #41 and #64.
- Measure with the `[Explicit]` `ControllerProbe64` probe before and after. Read its limits first: [[probe-and-playmode-test-gotchas]].
- Any slide code added later must pass the #64 fixture with no regression and 4 of 4 identical repeats. If you try slide again, project only the movement lost within the tick, not the stored velocity, and treat door-jamb edge normals separately (#8 comment 2026-10-28).

## Does not apply when
The snag is at a collider end or corner rather than along a ramp-and-rail crease. That is collider shape: see [[collider-geometry-for-a-steered-capsule]].

## Related
- [[lookout-tower-traversal-and-interactions]], [[player-controller]], [[fixed-tick-player-sim]]
- The 0.05 value came from PR #7, set against rail-end pop-up that the probe didn't show (#41 comment 2026-10-28).
