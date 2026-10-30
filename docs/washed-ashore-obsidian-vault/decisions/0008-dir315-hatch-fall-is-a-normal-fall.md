---
type: decision
status: accepted
tech: [probe-tests]
area: [lookout-tower]
issues: [86, 80, 8]
prs: [93]
source: "pod86 spec-86 r4 section 0 item 1; studio:production sprint/pod86/ruling-noah; PR #93"
decided_by: "Noah"
created: 2026-10-30
updated: 2026-10-30
supersedes: ""
superseded_by: ""
---
# 0008 The dir315 hatch fall is a normal fall

_Walking diagonally (heading dir315) from the cab into the open hatch drops the player onto flight 8. That counts as a normal fall into an open stair well, not a bug. The test still prints it as information, and no curb is added. The F8 head stall (S4 row 29) stays open in #80 with no new geometry._

## Context
#8 criterion S2 asks that the player can't fall through the open hatch by accident: going down should be deliberate (look down plus W). `LookoutHatchFallTests.S2` walks across the open hatch in 8 directions.
- dir315 steps off near tower-local (0.53, -0.15) and drops about 1.08 m onto mid-flight 8 (#86 comment 2026-10-29).
- The lower-sphere gap reads 0.327 through the probe input path and 0.363 through key presses, against a 0.10 bar (spec-86 K8).
- No collider configuration tried in the #86 pod changed it.

## Options considered
- Add a curb or other new geometry at the opening.
- Accept the drop as a normal fall.

## Decision
Spec of record for #86 (spec-86 r4, section 0 "Rulings and waivers in force", item 1):

> Noah: dir315 and dir315+shift are a normal fall into the open hatch; information only, no curb. Noah: row 29 stays open in #80 as the hatch-rim item, no new geometry.

The pod record (`sprint/pod86/ruling-noah`, Noah 2026-10-29 via team-lead):

> dir315 ACCEPTED as normal fall into open hatch stair well; dir315 and dir315+shift leave C2 bar, stay in test as info only; every other startable heading 0 drops, dir225 must clear via K3; dir315 only (not 'stair-top depth > 0.3 m'); no curb.

## Consequences
- Good, because S2 can pass. PR #93 marks dir315 `infoOnly`, and dir225 clears with the 0.05 m cab-floor trim (0.077 / 0.075 against 0.10).
- Good, because the known-fail case `KF_S2_StartAboveTheOpeningReadsDrop` still bites on its own row.
- Bad, because a player can still drop about 1 m onto flight 8 by walking diagonally into the open hatch. There is no fall damage today (out of scope per the tower brief).
- Bad, because the ruling covers dir315 only. Any other heading that starts dropping is a failure, not covered by this ruling.
- Row 29 (F8 up R30, stuck) stays open in #80. The untried fix is new geometry, which needs Noah ([[0007-nine-tower-gate-items-deferred]]).

## Related
[[lookout-tower-traversal-and-interactions]], [[collider-geometry-for-a-steered-capsule]]
