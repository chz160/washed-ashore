---
type: decision
status: accepted
tech: [character-art, rendering]
area: [player-body]
issues: [49]
prs: []
source: "td-49 B37 (studio:engineering pod49/B37-w49-5-x1-failed, adr/pod49-x1-backing-hull-closed); pod49 noah-answers-20261110.md; studio:decisions gate/pod49-s6-look-gate; run TestResults/player-character/runs/b22-p0/s2-p0/ (s2-hull-summary.txt e2b52473, s2-summary.txt 36b34564)"
decided_by: "Noah"
created: 2026-11-10
updated: 2026-11-10
supersedes: ""
superseded_by: ""
---
# 0015 Fold-silhouette gaps accepted (W-49-5); the backing hull is dropped

_The 39 small fold gaps on the Survivor's body (66 px over 385 test frames) are accepted as they are. The backing-hull fix (X1) failed its first test and nothing of it ships. The Blender corrective (X2) was not started._

## Context
- Two 1-px see-through pinholes were found on the left leg in the Sit and Squat look-down poses. They turned out to be fold-silhouette gaps between cloth layers, not mesh holes ([[fold-silhouette-gaps-in-layered-cloth]]).
- The S2 frame check found 39 K-a components ([[0014-s2-gap-classes]]) in 385 frames, mostly on the left shin and knee.
- td designed X1: a runtime, skinned, inset inner tube per arm and leg segment (never the hand), about 1,440 triangles, in a cloth-interior colour, drawn in the first-person body only (B35, B35a).

## Options considered
- **X1, backing hull.** P0 (baton 22, valid) swept the inset at +3, 0, −3 and −6 mm:
  - K-a closed at best 7 of 39 (−6 mm); the known spots 0 of 2; the target #2499/#16175 not closed.
  - LEAK (pixels changed outside the gaps) 5.0M to 9.4M px over 330–349 frames; 0.76M+ even with SSAO kept out, so not an SSAO artefact.
  - "P0 VERDICT: X1 FAILS". Baton 21 was VOID (the mesh wasn't readable, the hull had 0 triangles) and isn't counted.
- **X2,** a scripted pose-driven corrective at the fold sites (ta, Blender, re-export, full re-gate). High cost.
- **Accept the gaps.**

## Decision
Noah, 2026-11-10 15:12:32Z, asked "accept gaps / X2 / see crops":

> "Accept gaps, close #49"

W-49-5, text of record (B37):
- The K-a fold-silhouette hairline gaps of `Survivor_Body.fbx` `459fa2f6` are accepted for #49: 39 components, 66 px over the 385 S2 frames of baton 22.
- That includes the Sit (391, 397) and Squat (373, 490) pinholes and the K-a target #2499/#16175 (Walk1.5, cycle point 5, +85, left leg).
- The known pixels are accepted, not reclassified. They stay K-a defects on the record, covered by a named waiver.

## Consequences
- **Scope:** tied to Survivor_Body `459fa2f6` and the classifier of record. A new body mesh or FBX hash ends it: S2 runs again and any K-a goes back to the director and Noah.
- **Not covered:** an H3 hole at a joint, a K-b on a solid surface, or any hole outside these 39 components. Bar 1 and "real holes stay banned" apply to them as before.
- **Bar 1 is still proven on sampled rays only**, not exhaustively; these 39 gaps can be seen through at pixel level (qa condition C4).
- **Look:** at play size the target gap reads as a highlight in a dark fold crease, not a hole (director, S6).
- **X1 cancelled:** no `BackingHull.cs`, prefab change or material. The P0 harness stays as test code (`HairlineGapSizing.Hull.cs`).

## Related
[[fold-silhouette-gaps-in-layered-cloth]], [[see-through-gap-checks-on-a-skinned-body]], [[0013-hairline-gap-rule]], [[0014-s2-gap-classes]], [[player-character-body]]
