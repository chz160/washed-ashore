---
type: decision
status: proposed
tech: [multiplayer-ready, physics]
area: [player, lookout-tower]
issues: [13]
prs: []
source: "review pod 2026-11-11: task #1 (technical-director), #3 (network-engineer), #4 finding 3 (qa-lead)"
decided_by: "proposed by technical-director (#1); not accepted by the Studio Head"
created: 2026-11-11
updated: 2026-11-11
supersedes: ""
superseded_by: ""
---
# 0019 Blocking world state runs on the sim tick

_Proposed, not accepted. World state that can block the player (hatches, sashes, gates) advances on the player sim tick. It is replicated as state plus progress, and applied only through its owning component (`TrapDoor`, `WindowSash`) so its collider rules still run._

## Context
`TrapDoor` advances on frame time (`TrapDoor.cs:165-169`, `Update` with `Time.deltaTime`), and `WindowSash` does the same (`WindowSash.cs:99`). Both sit outside `PlayerSimState`. A replay of the predicted player would collide with the wrong hatch pose (network-engineer, #3: the biggest real risk for reconciliation). A network snap would also bypass the swing blocker and the headroom stop (technical-director, #1).

## Options considered
- Leave hinges on frame time and snap them over the network.
- Hinges on the sim tick, replicated as state plus progress (proposed).

## Decision
Proposed by technical-director (#1): the tick-driven hinge model above. Names `TrapDoor.cs:169` and `WindowSash.cs:99` as the code to change.

## Ranking
technical-director: HIGH. qa-lead: HIGH (#4 finding 3). skeptic: HIGH (#2 H3, partly tested: `PlayerReplayTests` run alone in a static world).

## Consequences
- Good, because replay sees the same hatch pose as the original tick.
- Bad, because the tower hatch and sash code change after the tower gates were tuned ([[lookout-tower-traversal-and-interactions]]); the tests that guard them would need a re-run (assumed).

## Related
- [[0018-one-sim-clock-60hz]], [[0020-replay-is-side-effect-free]], [[lookout-tower-traversal-and-interactions]], [[player-controller]]
- Issue: https://github.com/chz160/washed-ashore/issues/13
