---
type: decision
status: accepted
tech: [perf]
area: []
issues: [9, 10, 11]
prs: []
source: "task #25; TestResults/9-measurement-plan.md git blob 710f0d11 (rev 2), section 5"
decided_by: "technical-director"
created: 2026-11-11
updated: 2026-11-11
supersedes: ""
superseded_by: ""
---
# 0025 Savings rule S1 for perf trial A/B

_A trial counts as a saving only if every run's p95 beats the live settings by at least S_min = max(0.20 ms, 2% of the spot's A p95), with valid arms and brackets tighter than the saving. Set before any #9 data. It extends [[0016-frame-time-noise-rule-a1]]; it does not replace it._

## Context
[[0016-frame-time-noise-rule-a1]] decides whether something costs too much (R <= 1.05). It cannot say whether a change saves enough to be worth it. Issue [#9](https://github.com/chz160/washed-ashore/issues/9) asks for recommendations "with measured savings", and #10 and #11 share the protocol. 0016 says a rule is never re-ruled after the data, so S1 was fixed first. Plan: `TestResults/9-measurement-plan.md` rev 2 (blob 710f0d11); S1 is section 5.

## Options considered
- **(a) Launch-only A brackets everywhere.** Rejected for static spots: in-session brackets are minutes apart, and the 3 launches still expose launch-to-launch variance through the worst-run test. Kept for the route, which cannot change settings mid-walk.
- **(b) Mean-of-runs saving.** Rejected: one lucky run could carry it, so the worst run must clear S_min.
- **(c) Median as the measure.** Rejected: #9 asks for p95.

## Decision
The measure is p95 frame time, ms, judged per spot (and per route). For run r, `d_r` is the mean of p95 of the A window before and after the variant V, minus p95 of V. At static spots the A windows are inside the same launch (A V1 A V2 .. Vk A, 3 launches per spot); on the route they are the A launches either side. Positive means faster.

**S_min = max(0.20 ms, 2% of the mean A p95 at that spot).** SAVING needs all four:
1. Every launch and window valid (exact DONE line, no argument errors, same exe hash, readback equals request, at least 600 samples, runner not void).
2. The arm is the arm: readback equals request, enabled renderer count within +-2% of the bracketing A (not for FISHOFF), and the target counter moved. Otherwise NO EFFECT.
3. In every run, |A before - A after| < d_r.
4. min over the 3 runs of d_r >= S_min.

COST is the mirror image. NO SAVING: every bracket < S_min and max d_r < S_min. INCONCLUSIVE: rerun the spot once unchanged, then the Studio Head; never re-ruled. Counters alone never make a saving. A recommendation also needs technical-artist's visual check (no visible difference, or no pop-in).

Why 0.20 ms: 1.2% of the 16.67 ms 60 Hz budget, and about the M30 launch-to-launch noise (0016: margin 0.221 ms, spreads 0.03 and 0.14 ms). The 2% term is for slower machines. **Assumed:** that the M30 noise level carries over to static-spot launches; the #9 A brackets will show it.

Method rulings in plan section 3 (small props through SMALLMESH, fish through FISHOFF as an upper bound, no GPU Resident Drawer-off arm) are not part of S1; they are in [[frustum-culling-investigation]] and [[frustum-culling-harness-and-measurement-rule]].

## Consequences
- Good, because the pass rule is fixed before data, and #10 and #11 reuse it unless their own plan replaces plan section 4.
- Bad, because a #9-sized matrix costs about 2 h 45 min of the Studio Head's session (plan estimate, assumed until the first launch is timed).

## Related
[[0016-frame-time-noise-rule-a1]], [[frustum-culling-harness-and-measurement-rule]], [[frustum-culling-investigation]]
