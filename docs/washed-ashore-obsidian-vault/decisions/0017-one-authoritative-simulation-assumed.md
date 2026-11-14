---
type: decision
status: proposed
tech: [multiplayer-ready, physics]
area: [player]
issues: [13]
prs: []
source: "review pod 2026-11-11: task #1 (technical-director), #2 H1 (skeptic), #4 findings 1 and 8 (qa-lead)"
decided_by: "proposed by technical-director (#1); not accepted by the Studio Head"
created: 2026-11-11
updated: 2026-11-11
supersedes: ""
superseded_by: ""
---
# 0017 One authoritative simulation is assumed

_Proposed, not accepted. The physics rules assume one authoritative simulation: a dedicated server, or a host acting as the server, which includes solo play as a local host. If #13 picks Distributed Authority or Netcode for Entities, rules 2, 4, 12, 13 and 15 are re-ruled before any netcode lands._

## Context
`docs/physics-guidelines.md` line 3 states "server-authoritative ... will use NGO" as fact, while #13 still has the topology (dedicated, Relay host, Distributed Authority, hybrid) and the stack (NGO or Entities) open. README "Multiplayer Direction" says it is open to iteration. Nothing is built: no netcode package in `Packages/manifest.json` (network-engineer, #3).

With a player host, the server-only rules 12, 13 and 15 run on a player's machine. That weakens anti-cheat and permadeath integrity (README, qa-lead #4).

## Options considered
- Leave the doc as written and settle topology later (rules stay unconditional).
- Make the doc say which rules hold per mode, and tie them to this assumption (proposed).

## Decision
Proposed by technical-director (#1): the guidelines assume one authoritative simulation, solo play runs the same code as a local host (README "Multiplayer Direction", one simulation path), and the doc states which rules hold per mode. A topology change re-rules rules 2, 4, 12, 13 and 15 first.

## Ranking
technical-director: HIGH. qa-lead: HIGH (#4 finding 1; strengthened for the player-host case). skeptic: HIGH (#2 H1). Testable now: no, untestable until netcode exists (#2).

## Consequences
- Good, because the doc stops presenting an open question as settled.
- Bad, because rules 2, 4, 12, 13, 15 may need a rewrite after #13.

## Related
- Issue: https://github.com/chz160/washed-ashore/issues/13
- Doc: https://github.com/chz160/washed-ashore/blob/main/docs/physics-guidelines.md
- [[0018-one-sim-clock-60hz]], [[0019-blocking-world-state-on-sim-tick]], [[0020-replay-is-side-effect-free]], [[0021-server-validates-client-input]]
- [[player-controller]]
- Open: the NGO behavior claims in rules 12 and 13 were checked against the 2.4/2.5 manual only (qa-lead #4, network-engineer #3); the NetworkRigidbody claim is assumed.
