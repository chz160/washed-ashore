---
type: decision
status: accepted
tech: [character-controller]
area: [player]
issues: [64, 41, 46]
prs: [82]
source: "studio:engineering adr/64-controller-choice; PR #82"
decided_by: "technical-director (td-64r)"
created: 2026-10-30
updated: 2026-10-30
supersedes: ""
superseded_by: ""
---
# 0006 CharacterController kept

_The player stays on Unity's `CharacterController`. Neither Kinematic Character Controller (KCC) nor the custom slide prototype is adopted. The tower rail stall turned out to be one of our own tuning values._

## Context
Noah, 2026-10-25 (#64): in confined spaces, "if any part of the character drags across another surface, it stops me ... instead of funneling me in the direction of that surface." The fixed tick (#46) and the stair feel work (#8, #41) both sit on the controller, so the choice had to come first. A probe (`ControllerProbe64`) measured walls, corners, a doorway, low lips and the tower's ramp-and-rail crease before the ruling.

## Options considered
- **A:** CharacterController plus our own slide (wall-normal projection, in-tick re-moves).
- **B:** KCC. Assessed from public docs only, nothing downloaded (`kcc-assessment-64.md`).
- A Rigidbody-driven player was an automatic FAIL in #64.

## Decision
Technical director, 2026-10-27 (`adr/64-controller-choice`):

> DECISION: OPTION A. KEEP UNITY'S CharacterController. Do not move to KCC (option B). The option A slide code (PlayerController.Slide.cs, A1/A2) is NOT adopted as written; it is saved in optionA.patch for reference only. The #41 fix path is our own tuning value: the tower ramp stepOffset (TowerWalkZones.upperStepOffset 0.05, TowerWalkZones.cs:23).

> Why not adopt A2's code anyway: it breaks a doorway that passes today, its in-tick re-moves make one head stop non-reproducible (a risk for #46 replay), and it causes a hatch-rim ride-up. The stall it fixes is fixed more cleanly by the step value.

## Consequences
- Good, because #46 built the fixed tick on `CharacterController.Move`, with replay matching to 0.000 mm ([[fixed-tick-player-sim]]).
- Good, because #41 was fixed by the step offset 0.25 ([[stairs-and-ramps-with-a-charactercontroller]]).
- Good, because there is no third-party licence, Unity 6000.6 compatibility check or 31-file migration.
- Bad, because the PhysX controller still has known weak spots: creases, internal mesh edges and low lips. Snags are fixed in collider shape ([[collider-geometry-for-a-steered-capsule]]).
- Any slide code added later must pass the #64 fixture with no regression and 4 of 4 identical repeats.
- Noah's general "confined spaces" stop did not reproduce on primitives at normal capsule settings. The measured case was the tower crease (ADR K1).
- The slide prototype is on branch `spike-64-option-a`, not for merge.

## Related
[[player-controller]], [[probe-and-playmode-test-gotchas]] (the probe's known limits, #81)
