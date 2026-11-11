---
type: log
updated: 2026-11-10
---
# Log

One entry per pod or vault change, newest first. Format: `## YYYY-MM-DD | <what> | <issue>`.

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
