---
type: log
updated: 2026-11-13
---
# Log

One entry per pod or vault change, newest first. Format: `## YYYY-MM-DD | <what> | <issue>`.

## 2026-11-13 | Tech/review pod: frustum culling investigation | #9
- Decisions: new [[0025-savings-rule-s1-perf-trials]], [[0026-agents-may-launch-the-player]] (amended same day), [[0027-reruns-replace-and-pre-data-facts-are-blind]] (all accepted; 0016 unedited, 0026 replaces only its "never launch" sentence).
- Patterns: new [[frustum-culling-harness-and-measurement-rule]] (flags, GRD limit, lessons for #10 and #11).
- Features: new [[frustum-culling-investigation]] (in-progress). Home.md: link lines appended.
- Result: qa-lead #32: Q1, Q2, Q4, Q6 PASS; Q3 and Q5 INCONCLUSIVE; no automatic FAIL. No change recommended as measured: a 600 m far clip saves 1.6-3.5 ms p95 but changes the skyline at 3 of 4 spots. Five rows still INCONCLUSIVE after the rerun; the Studio Head accepted them and finished #9 (rulings file item 4). Issue text corrected: 4 cascades, fish drawn procedurally, terrain tiles are not Renderers.
- Sources: tasks #23-#33, `_bmad-output/poc/perf-frustum-note.md` (b0bdfce9), `TestResults/9-qa-verdict.md`. Written on branch `frustum-culling-9`, not committed. Not run by the writer: tests and measurements.

## 2026-11-11 | Feature pod: tree collision and bush slowdown | #21
- Decisions: new [[0023-bush-slowdown-design]], [[0024-tree-collider-sized-to-trunk]] (both accepted; bush multipliers assumed, not playtested).
- Features: new [[tree-collision-and-bush-slowdown]] (in-progress until committed). [[player-controller]] not edited; a proposed line is in the feature note.
- Result: qa-lead PASS on T1-T3 and B1-B4 (tests 10/10 PlayMode, 11/11 and 34/34 EditMode; regression tower 7/8 as in baseline, water 13/13 after one named test edit, wildlife 20/20, spawn 8/8; FPS 6.77 vs 7.00 ms, -3.3%). Known limits and open items are in the feature note.
- Sources: tasks #12-#21 (build-engineer, systems-designer, technical-artist, gameplay-engineer, qa-lead, technical-director). Written on branch `trees-bushes-21`, not committed.

## 2026-11-11 | Review pod: decision 0019 checked against the code | #13
- Decisions: new [[0022-hinge-replication-corrected]] (proposed; would supersede 0019 if accepted). [[0019-blocking-world-state-on-sim-tick]] is unchanged and still proposed.
- Result: direction of 0019 stands, mechanism and scope need correcting. qa-lead verified 9 findings (F1-F9: leaf angle never reaches collision so replicate state plus ticks; SetState skips anti-trap; no world tick; hinges outside PlayerSimState; Interact and Firefinder outside the command stream; no update order; "gates" wrong; WaterFloat latent), downgraded 2 (D1, D2), rejected none, left 3 unverified (sash reachability, WaterClock, whether 0020 forbids mid-replay SyncTransforms).
- Caveat: code read only, nothing run.
- Sources: tasks #8 (technical-director), #9 (skeptic), #10 (qa-lead). Written on branch `player-body-49`, not committed.

## 2026-11-11 | Review pod: multiplayer and server-authority rules | #13
- Decisions (all proposed, none accepted): [[0017-one-authoritative-simulation-assumed]], [[0018-one-sim-clock-60hz]], [[0019-blocking-world-state-on-sim-tick]], [[0020-replay-is-side-effect-free]], [[0021-server-validates-client-input]].
- Features: [[player-controller]] (Networking bullet, issues, rulings).
- Result: technical-director ranked 5 high, 9 medium, 5 low; qa-lead's list has 15 de-duplicated findings (none rejected, 5 downgraded). Nothing was run; NGO claims were checked against the 2.4/2.5 manual only.
- Sources: tasks #1-#4 (technical-director, skeptic, network-engineer, qa-lead). Written on branch `player-body-49`, not committed. `docs/physics-guidelines.md` is not changed.

## 2026-11-10 | Pod 49: the Survivor as the player character | #49
- Decisions: [[0009-survivor-is-the-player-body]], [[0010-realistic-human-skeleton-low-poly-humans-removed]], [[0011-survivor-scaled-to-1-85-m]], [[0012-look-down-bar-2-waivers]], [[0013-hairline-gap-rule]], [[0014-s2-gap-classes]], [[0015-fold-gaps-accepted-w-49-5]], [[0016-frame-time-noise-rule-a1]].
- Patterns: new [[see-through-gap-checks-on-a-skinned-body]], [[fold-silhouette-gaps-in-layered-cloth]], [[reading-a-non-readable-mesh-in-tests]]; updated [[probe-and-playmode-test-gotchas]] (items 7 and 8).
- Features: [[player-character-body]] (in-progress until the commit lands).
- World: [[the-survivor]].
- Sources: pod 49 records (td B-items to B40, qa log to 12mm, director gate notes to S6), and the agent records named in each note. Written on branch `player-body-49`, not committed.

## 2026-10-30 | Backfill from finished pods | #8 #14 #19 #40 #41 #43-#46 #61-#64 #75 #80 #86
- Patterns: [[stairs-and-ramps-with-a-charactercontroller]], [[collider-geometry-for-a-steered-capsule]], [[probe-and-playmode-test-gotchas]], [[fixed-tick-player-sim]], [[physics-layers-and-collision-masks]], [[moving-colliders]], [[line-endings-and-content-hashes]].
- Decisions: [[0001-guided-path-climb-dropped]], [[0002-desktop-targets-web-retired]], [[0003-player-off-default-layer]], [[0004-no-single-spawn-point]], [[0005-json-stays-lf]], [[0006-charactercontroller-kept]], [[0007-nine-tower-gate-items-deferred]], [[0008-dir315-hatch-fall-is-a-normal-fall]].
- Features: [[lookout-tower-traversal-and-interactions]], [[player-controller]].
- World: [[lookout-tower]].
- Sources: merged PRs #20-#93, issue comments to 2026-10-30, agent pod records, pod briefs. Approved by Noah 2026-10-30.

## 2026-10-30 | Vault set up | -
- Created the folder layout, rules ([[CLAUDE]]), templates and index views.
