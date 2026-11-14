---
type: decision
status: proposed
tech: [multiplayer-ready, character-controller]
area: [player]
issues: [13]
prs: []
source: "review pod 2026-11-11: task #2 H3 (skeptic), technical-director rulings message, #4 finding 4 (qa-lead)"
decided_by: "proposed by technical-director (#1); not accepted by the Studio Head"
created: 2026-11-11
updated: 2026-11-11
supersedes: ""
superseded_by: ""
---
# 0020 Replay is side-effect-free

_Proposed, not accepted. Player `Simulate` has no side effects during replay. Gameplay and presentation events fire only on confirmed ticks. A proof-of-concept replay test runs with a swinging hatch and a second player._

## Context
Reconciliation replays player ticks. The guidelines have no rule for what replay must not trigger: `OnControllerColliderHit` forces, tower zones, the hatch duck, or world history for remote players (skeptic #2 H3). Player x Player collision is also undecided (matrix, line 191). Only `PlayerReplayTests` cover replay, with one player in a static world (#2). The 1 mm replay result is from [[fixed-tick-player-sim]] and was not re-run in this pod (assumed).

## Options considered
- Let side effects fire during replay and de-duplicate later.
- Keep `Simulate` pure; fire events on confirmed ticks only (proposed).

## Decision
Proposed by technical-director (#1, raised from skeptic's H3/H4 list).

## Ranking
technical-director: HIGH. qa-lead: MEDIUM (#4 finding 4), because nothing is built and the scenario is reasoned, not shown; qa-lead records it as an ADR-before-code requirement, not a current defect. The director has the final technical word; both ranks are kept.

## Consequences
- Good, because reconciliation cannot double-fire effects.
- Bad, because tower zones and collision hooks move to a confirm-tick path.
- Open: Player x Player is a game-director call (follow-up candidate, not a note).

## Related
- [[0019-blocking-world-state-on-sim-tick]], [[0021-server-validates-client-input]], [[fixed-tick-player-sim]], [[player-controller]]
- Issue: https://github.com/chz160/washed-ashore/issues/13
