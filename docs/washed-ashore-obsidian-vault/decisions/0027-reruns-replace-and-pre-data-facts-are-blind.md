---
type: decision
status: accepted
tech: [perf]
area: []
issues: [9, 10, 11]
prs: []
source: "task #25 metadata notes note_technical-director_plan_rerun_rule (addendum 7), _plan_route_addendum6c, _6c_status (named by technical-director, not read by the writer); TestResults/9-studio-head-rulings.md items 2 and 3 (read); TestResults/9-measurement-plan.md blob 710f0d11 section 5; qa-lead 6c finding (run 20261112-1803 summary.json, as reported by technical-director)"
decided_by: "technical-director"
created: 2026-11-13
updated: 2026-11-13
supersedes: ""
superseded_by: ""
---
# 0027 Pre-registration hygiene: reruns replace, pre-data facts are blind

_A plan-mandated rerun replaces the first set for the rows it was run for; sets are never pooled. Facts sent to the rule-setter carry no outcome numbers. Both extend [[0016-frame-time-noise-rule-a1]] and [[0025-savings-rule-s1-perf-trials]]._

## Context
Question: when a pre-registered perf plan needs a rerun or a rule change after some data exists, how is the new data combined with the old, and what may a rule-setter see before setting a rule? In #9 a rerun was needed for INCONCLUSIVE rows, and addendum 6c (route FAR's arm test is the far-plane readback) was set after the rule-setter saw set 1's descriptive route numbers. The Studio Head accepted 6c "with disclosure" (`TestResults/9-studio-head-rulings.md` item 2) and ruled a rerun of the 15 SHADOW and FISHOFF launches (item 3).

## Options considered
- **(a) Pool both sets.** Rejected: it rewards running until the average passes.
- **(b) Let the rule-setter see outcomes and disclose them.** Rejected: in #9 that needed a Studio Head ruling anyway, and blind is cheaper.
- **(c) A third set on a second INCONCLUSIVE.** Rejected: unbounded machine time, and the Studio Head should decide.

## Decision
1. **Reruns replace.** A plan-mandated rerun of an INCONCLUSIVE comparison replaces the first set for exactly the rows it was run for. Sets are never pooled or averaged. The first set stays in the report, labelled "superseded by the rerun". Other rows measured in the rerun are descriptive only and never change a label, rank or saving. If it is still INCONCLUSIVE, it goes to the Studio Head, with no third set.
2. **Pre-data facts are blind.** Facts sent to the rule-setter before a rule is set carry no outcome numbers (no d, no p95 differences, no verdicts under unset rules). Instrument facts (sample counts, speeds, readbacks, counter behaviour) are fine.
3. **Late rules.** A rule set after the rule-setter has seen outcome data that predicts the coming data counts as set after data (0016). It needs the Studio Head's written acceptance and a disclosure in the report.

Reasoning (technical-director): pooling a first set that failed its validity or bracket test with a rerun lets the bad set drag the result; replacing judges each set whole under the same rule. Outcome numbers seen before a rule bias its design even when the rule-setter means well.

## Consequences
- Good, because #10 and #11 inherit the rule, and a late rule is visible as one.
- Bad, because a rerun discards the first set's contribution to that row, and the rule-setter needs facts stripped of results.
- The 6c finding (route FAR triangles 10.32M against A 10.06-10.08M) is qa-lead's, as reported by technical-director; not re-read by the writer.

## Related
[[0016-frame-time-noise-rule-a1]], [[0025-savings-rule-s1-perf-trials]], [[0026-agents-may-launch-the-player]], [[frustum-culling-harness-and-measurement-rule]], [[frustum-culling-investigation]]
