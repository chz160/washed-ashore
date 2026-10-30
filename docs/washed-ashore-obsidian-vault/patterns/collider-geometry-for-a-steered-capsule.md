---
type: pattern
status: active
tech: [collider-geometry]
area: [lookout-tower]
issues: [80, 86, 91]
prs: [93]
source: "Issue #86 comment 2026-10-30 (lessons); issue #80 comment 2026-10-30; PR #93; pod86 spec-86 r4"
created: 2026-10-30
updated: 2026-10-30
aliases: [collider cuts, penetration depth, runtime-edit trial]
---
# Collider geometry for a steered capsule

_Shortening a wall to stop a snag usually creates a new corner that the capsule hits harder. Judge a collider change by real penetration depth against the actual hulls, and try every candidate in one runtime-edit session before you rebuild anything._

## Problem
On the lookout tower, a player steering 20-30° into a wall end brushes it, slows sharply or gets stuck. Examples: the rail ends on flights 2, 3 and 6, and the flight 8 head under the hatch.

## When it applies
- Convex hull and box colliders that form walls along a walk lane. The capsule is steered into them at an angle, not pushed straight on.
- Today: the tower's `CLIP_CVX_Flight0N_02` wall hulls, `CLIP_BOX_Landing*` boxes and `COL_BOX_CabFloor_01`, built by `WalkableTowerBuilder` (cleanup in `WalkableTowerBuilder.Cleanup.cs`).
- The same applies to any future building interior with narrow lanes.

## Why it happens
A cut wall end is a fresh corner across the capsule's path. A steered capsule sweeps into it, and the depth rises with the steer angle. Where two thin edges sit close together (the F8 head), moving or removing one just moves the pin to the other.

## Evidence
All from runs on main `5e96292`, 17 runtime configurations tried one change at a time (issues #86 and #80, comments 2026-10-30):
- **End cuts make it worse.** 0.05 m cuts on the three rail ends read 0.0594 / 0.0594 / 0.0598 against a 0.0300 bar (main: 0.0301 / 0.0300 / 0.0304). Six neighbouring rows rose by 0.009-0.018.
- **Depth grows with steer angle.** 0.0090 / 0.0177 / 0.0294 at 10° / 20° / 30°.
- **The fix can move the problem.** A 0.03 m outward end shift helped the three rows (0.0274 / 0.0274 / 0.0266), but row 84 went from 0.0246 to 0.0311, over the bar.
- **Thin edges trade the pin.** At the F8 head, trimming the rim 5-25 cm pins the capsule on `CLIP_CVX_Flight08_02`. Removing that hull's tail pins it on the rim again.
- **What worked.** Moving the landing 6 boxes 0.063 m outward, so their inner faces stand 0.406 m from the lane like the flight walls (they stood at 0.343). Rows 76 and 94 went from fail to pass, with nothing else changed (PR #93).

## What to do
- **Judge by TowerStep "max penetration depth".** It is real contact, from `Physics.ComputePenetration` against the actual hulls. "Max lateral overshoot" uses an assumed wall line from the flight midpoint, so treat it as information only.
- **Run a runtime-edit trial session before any rebuild.** On the main prefab: edit, measure, restore, and repeat the baseline at the end. One pass covered 17 configurations.
- **Prefer moving a face back to the lane's common clearance over cutting an end.** The landing 6 fix matched the flight walls' 0.406 m.
- **Re-run the whole probe after each change, not only the target rows.** Every rail-end change tried here made another row worse.
- **Check feel tests too.** A 0.223 m cut on `CLIP_CVX_Flight07_02` fixed S4 row 32. It also broke `LookoutHatchWalkTests` B/C and the S1 view-bump bar at 144 fps, so it was dropped.
- **New geometry is a design call, not a trim.** An invisible sloped collider at the hatch opening's west edge (untried) sits near the deferred trap door and cab step-off items. It needs Noah ([[0007-nine-tower-gate-items-deferred]]).

## Does not apply when
The stall is along a ramp-and-rail crease. That is the step offset: see [[stairs-and-ramps-with-a-charactercontroller]].

## Related
- [[lookout-tower-traversal-and-interactions]], [[probe-and-playmode-test-gotchas]]
- None of the 16 flight-side walls is a rectangular box (sheared slabs, inferred, #80). Box walls or rounded rail ends would need the tower export to emit rectangular solids. Nothing so far has needed that.
