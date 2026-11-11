---
type: decision
status: accepted
tech: [perf]
area: [player-body]
issues: [49]
prs: []
source: "td-49 B16o (A1), B39; pod49 HANDOFF section 3b"
decided_by: "td-49 (rule); Noah accepted M30 under it, 2026-11-08"
created: 2026-11-10
updated: 2026-11-11
supersedes: ""
superseded_by: ""
---
# 0016 Frame-time noise rule A1 for ON/OFF gates

_Every ON/OFF frame-time gate passes on four tests, all set before any data: every arm valid, no impossibly fast arm, spreads smaller than the margin to the 1.05 bar, and worst pairing ≤ 1.05. A run that misses tests 2–4 is INCONCLUSIVE, not FAIL._

## Context
M30 measured the Survivor's frame-time cost in the release build (`Builds\Windows-BellsBend`), body OFF vs ON, A-B-A. The pre-set rule (B16g) compared the spread with the ON−OFF difference. A body that costs very little can never pass that. Run `m30-20261108b` was valid with R = 1.0169 and worst pairing 1.0303, but formally INCONCLUSIVE under B16g.

## Decision
Noah, 2026-11-08 21:45Z: "accept M30". td's A1 becomes the standing rule from the next pod on. The measure is unchanged: walk-median R ≤ 1.05. All four must hold:
1. **Every arm is valid:** exit 0, fresh, inside the sample band, the expected renderer and animator counts, and the "DONE" line in `progress.txt`.
2. **No impossibly fast arm:** no ON median below the lowest OFF median by more than the OFF spread.
3. **Spread under the margin:** each arm type's spread < 0.05 × mean OFF − (mean ON − mean OFF).
4. **Worst pairing** (max ON / min OFF) ≤ 1.05.

If 2, 3 or 4 fails, the result is INCONCLUSIVE: rerun, or take it to Noah. It is never re-ruled after the data. B16g is retired.

Example: `m30-20261108b`: margin 0.221 ms > spreads 0.03 / 0.14, worst pairing 1.0303 = PASS.

## Consequences
- Good, because a cheap change can pass without a rule written after the data.
- Practical notes from the M30 runs (pod 49 handoff):
  - The runner wrapper's exit code is meaningless; judge by the "DONE" line.
  - In Windows PowerShell 5.1, `$PSScriptRoot` is empty inside a `param()` default, so pass `-Runner` explicitly.
  - Two earlier runs were lost: one stopped by a watcher's file lock, one with two impossibly fast arms. Never open a file a runner is writing.
  - A frame-time run needs the console session active (not RDP), a heads-up to Noah and his OK. Agents never launch the player themselves.
- M31 passed alongside: the debug-only code is absent from the release build.

## Related
[[player-character-body]]
