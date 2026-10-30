---
type: decision
status: accepted
tech: []
area: [lookout-tower]
issues: [8, 86, 80]
prs: [92, 93]
source: "pod8-brief.md 'Noah's rulings for this pod (2026-10-29)'; pod86-brief.md 'Hard rules'; studio:production sprint/pod86/ruling-noah; _bmad-output/poc/tower-restart-rulings.md section 4"
decided_by: "Noah"
created: 2026-10-30
updated: 2026-10-30
supersedes: ""
superseded_by: ""
---
# 0007 Nine tower gate items deferred

_The nine "re-decide on the walkable tower" items from the restart rulings are deferred. Today's behaviour stays for all of them. Trimming or reshaping a collider is allowed; changing one of these items is not, without Noah._

## Context
When the tower restarted ([[0001-guided-path-climb-dropped]]), the director listed nine rulings to re-decide once the walkable tower existed. Each came with what the director would need to see (`tower-restart-rulings.md` section 4). #8 gated its feel round on Noah answering or deferring each one.

The nine items:
1. Jump and sprint on the tower
2. The 0.25 m tower capsule
3. 0.20 m eye clearance
4. Invisible 2.2 m landing-edge walls
5. Landing guards 3-7
6. Trap door 1.50 × 0.66 m, the duck and the hinge setback
7. Cab step-off
8. Firefinder eye and sash
9. Flight mid rails off

## Decision
Noah, 2026-10-29, #8 pod brief:

> **All nine gate items in `_bmad-output/poc/tower-restart-rulings.md` section 4 are DEFERRED.** Keep today's behaviour for all of them (jump/sprint on tower, tower capsule 0.25, eye clearance, invisible landing walls, landing guards 3-7, trap door size/duck/hinge, cab step-off, firefinder eye/sash, flight mid rails). Don't change any of them. If a fix seems to need one, stop and tell team-lead.

Restated for #86 under "Hard rules (Noah's standing rules)":

> Trimming or reshaping a collider is in scope. Removing an invisible landing wall or a landing guard, or changing the trap door's size or hinge, is not.

Noah ruled the #86 cab-floor and landing 6 trims "IN SCOPE as collider trims/moves, not gate items", with the "Deferred list otherwise unchanged" (`sprint/pod86/ruling-noah`).

## Consequences
- Good, because #8 and #86 could fix feel and snags without reopening design questions.
- Bad, because some snags may only be fixable by touching a gate item. The F8 head under the hatch is the open case: the untried invisible sloped collider at the opening's west edge sits near "trap door" and "cab step-off" (#80 comment 2026-10-30).
- No date or trigger is recorded for revisiting the nine.

## Related
[[lookout-tower-traversal-and-interactions]], [[0008-dir315-hatch-fall-is-a-normal-fall]], [[lookout-tower]]
