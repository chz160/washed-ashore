---
type: decision
status: accepted
tech: [perf]
area: []
issues: [9, 10, 11]
prs: []
source: "Studio Head ruling relayed by team-lead, 2026-11-12; validity conditions from technical-director, 2026-11-12; first agent launch: TestResults/9-runs/20261112-1018-posecheck"
decided_by: "Noah (Studio Head)"
created: 2026-11-12
updated: 2026-11-12
supersedes: ""
superseded_by: ""
---
# 0026 Agents may launch the player

_Agents may launch built players for measurement, pose checks and verification. This replaces only the sentence "Agents never launch the player themselves" in [[0016-frame-time-noise-rule-a1]]. The A1 noise rule in 0016 stands._

## Context
[[0016-frame-time-noise-rule-a1]] (line 39) says a frame-time run needs the console session active, a heads-up and an OK from Noah, and that agents never launch the player. In #9 the Studio Head would otherwise have to start every launch of a 2 h 45 min matrix (plan estimate, assumed) by hand.

## Options considered
- Keep the rule: the Studio Head starts every launch. Rejected by the Studio Head.

## Decision
Noah, 2026-11-12: "you can definitely launch the player for stuff like this. that rule should be changed."

Agents may launch built players for measurement, pose checks and verification. Conditions that remain (technical-director, 2026-11-12):
- The console session is active (not RDP).
- The runner's load samples must pass.
- The launching agent holds `.studio/unity.lock` from the first launch to the last exit of the block. No other session starts Unity, a build or Blender while it is held.
- Each launch's `run.json` load record decides validity, whoever pressed start.
- ~~Before each timed block, a heads-up and the Studio Head's OK~~ replaced 2026-11-12 (see Amendment): no ask is needed for pre-reviewed measurement runs of the perf player.
- No agent opens, tails or watches a file a running slot is writing (`Player.log`, `frustum-windows.jsonl`, `progress.txt`) until that slot's DONE or FAILED line. The launcher waits on the runner's exit, not on file polling. ([[0016-frame-time-noise-rule-a1]] line 38: an earlier run was stopped by a watcher's file lock; "Never open a file a runner is writing".)
- The launcher records in the run folder who started each block and when. The run notes list it.

## Amendment, 2026-11-12
Noah, ~23:58Z, on the heads-up condition: "go ahead, run both back to back. you don't need to wait for me on these type of runs". Agents may start pre-reviewed measurement runs of the perf player without asking first. Unchanged: hold `.studio/unity.lock`, no watching slot files while they are written, record who started each run, and the run's own machine-load record decides validity. Source: Studio Head via team-lead. The heads-up for runs that are not pre-reviewed is not covered by this ruling (assumed still required).

## Consequences
- Good, because agents can run and rerun matrices without waiting for the Studio Head to start each launch.
- Bad, because a timed block now needs the Studio Head to keep off the machine; validity rests on the runner's load record, not on who started it.
- Partial change: only the "never launch" sentence of 0016 is replaced. 0016 is not marked superseded and its text is not edited (vault rule: decisions are never rewritten). Read 0016 together with this note.

## Related
[[0016-frame-time-noise-rule-a1]], [[0025-savings-rule-s1-perf-trials]], [[frustum-culling-harness-and-measurement-rule]]
