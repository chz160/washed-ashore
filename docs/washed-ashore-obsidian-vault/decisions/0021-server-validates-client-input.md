---
type: decision
status: proposed
tech: [multiplayer-ready, physics]
area: [player]
issues: [13]
prs: []
source: "review pod 2026-11-11: task #2 H4 (skeptic), technical-director rulings messages, #4 finding 6 (qa-lead)"
decided_by: "proposed by technical-director (#1); not accepted by the Studio Head"
created: 2026-11-11
updated: 2026-11-11
supersedes: ""
superseded_by: ""
---
# 0021 The server validates client input

_Proposed, not accepted. The server consumes at most one `PlayerCommand` per player per server tick, within a bounded tick window. It clamps every input to its legal range and runs interaction and hit queries itself. Client-cast results are never trusted._

## Context
The guidelines have no rule for validating input commands or interaction rays (skeptic #2 H4). The scenarios are a speed hack and looting through walls. Both are reasoned, not shown: nothing is built and nothing was run (qa-lead #4). Rule 2 (line 29) is where the command path is described.

## Options considered
- Trust client results and add checks later.
- Define the validation contract before netcode (proposed).

## Decision
Proposed by technical-director (rulings message after #1), from skeptic's H4.

## Ranking
technical-director: HIGH. qa-lead: MEDIUM (#4 finding 6), because nothing is built and the scenario is reasoned. Recorded as an ADR-before-code requirement. Both ranks are kept; the director has the final technical word.

## Consequences
- Good, because speed hacks and wall-looting have a stated defense before code exists.
- Bad, because the server re-runs interaction and hit queries, which costs server CPU (unmeasured, assumed; see M3 in the report).

## Related
- [[0017-one-authoritative-simulation-assumed]], [[0020-replay-is-side-effect-free]], [[player-controller]]
- Issue: https://github.com/chz160/washed-ashore/issues/13
