---
type: decision
status: proposed
tech: [multiplayer-ready, physics]
area: [player]
issues: [13]
prs: []
source: "review pod 2026-11-11: task #1 (technical-director), #2 H2 (skeptic), #3 (network-engineer), #4 findings 2 (qa-lead)"
decided_by: "proposed by technical-director (#1); not accepted by the Studio Head"
created: 2026-11-11
updated: 2026-11-11
supersedes: ""
superseded_by: ""
---
# 0018 One sim clock: the 60 Hz player tick

_Proposed, not accepted. The network tick is the 60 Hz player tick. NGO's tick rate is set to 60. Every sim-relevant time source (player, hinges, water through `WaterClock.Use`) derives from that tick. Physics runs at a stated ratio to it, or is re-ruled._

## Context
Four clocks coexist today (skeptic #2 H2, network-engineer #3, qa-lead #4 finding 2):
- Player: 60 Hz `PlayerTickClock` (`PlayerTickClock.cs:10`).
- Physics: 50 Hz (`TimeManager.asset`, 0.02; rule 24).
- Hatch and sash hinges: `Time.deltaTime` in `Update` (`TrapDoor.cs:169`, `WindowSash.cs:99`).
- Water: per-process level-load time (`WaterClock.cs:13`). A `WaterClock.Use()` seam exists, but nothing sets it.

The guidelines' rule 11 says clocks are "sampled the same way". That does not hold. No network tick is named. NGO defaults to 30 Hz, and [[fixed-tick-player-sim]] records that 30 Hz loses about 18% of ramp speed (vault note, not re-measured in this pod).

## Options considered
- Leave the clocks separate and reconcile each by hand.
- One tick that everything sim-relevant derives from (proposed).

## Decision
Proposed by technical-director (#1, amended in its later message): the 60 Hz player tick is the network tick, and server hit validation and reconciliation index history by player tick, not by `FixedUpdate` step.

## Ranking
technical-director: HIGH. qa-lead: HIGH (#4 finding 2, widened to hinges and water). skeptic: HIGH, testable now (#2 H2).

## Consequences
- Good, because replay and reconciliation have one time base.
- Bad, because hinges, water and the physics ratio all change; the cost is unmeasured (assumed).
- Open: the maximum allowed timestep is still the 0.333 s default (`TimeManager.asset`), not applied (technical-director #1, L6).

## Related
- [[0017-one-authoritative-simulation-assumed]], [[0019-blocking-world-state-on-sim-tick]], [[fixed-tick-player-sim]], [[player-controller]]
- Issue: https://github.com/chz160/washed-ashore/issues/13
