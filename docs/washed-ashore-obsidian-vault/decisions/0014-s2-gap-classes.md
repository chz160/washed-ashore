---
type: decision
status: accepted
tech: [probe-tests, rendering]
area: [player-body]
issues: [49]
prs: []
source: "td-49 B35, B35b, B35c, B38"
decided_by: "director-49; accepted by Noah 2026-11-10"
created: 2026-11-10
updated: 2026-11-11
supersedes: ""
superseded_by: ""
---
# 0014 S2 gap classes: only fold wedges are defects

_One-region (H1) see-through gaps are sorted into classes in a fixed order. Only K-a, the close fold wedges, are defects. Gaps between fingers, at intended fins and fringe, and between far-apart parts of one limb are not._

## Context
H1 ([[0013-hairline-gap-rule]]) mixes real fold wedges with intended openings, for example the gap between two fingers of one hand. The director ruled which H1 kinds are defects from the baton 20 crops.

## Decision
Order of record (B35b, equal to the director's note2), applied to every H1 component:
1. **K-h:** both framing edges' triangles are hand-dominant → not a defect (between fingers).
2. **K-f:** either framing edge is on an intended fin or fringe triangle → not a defect. The list is the director's 19 polygons (18 fin flaps plus loop 19's two faces). Loop 23, the boot-top rim, is NOT K-f. A polygon that doesn't map to Unity triangles fails closed (its rows fall to K-a).
3. **Separation ≤ 60 mm** between the two framing edges → **K-a, DEFECT**, whether open to the background at 4× or enclosed.
4. **Separation > 60 mm** → K-b, not a defect. 60–100 mm is flagged borderline and cropped for the director.
5. **No second edge, or no attribution** → K-a, DEFECT.

"Open at 4×" is printed as evidence only. The 60 mm is td's choice: in baton 20 the enclosed values run up to 55.56 mm and the next is 68.55 mm, so it sits in a gap in the data, not at a measured limit.

Noah was informed 2026-11-09 04:48Z and **accepted it on 2026-11-10** (B38).

## Consequences
- td first put "open at 4×" first (B35). That would have passed 24 close-layer rows as non-defects, so td withdrew that order (B35b). Lesson: don't promote an unmeasured reading into a rule.
- On baton 22 the classifier of record found 39 K-a components (66 px) over 385 frames. They are accepted under [[0015-fold-gaps-accepted-w-49-5]].
- Classifier of record: `HairlineGapSizing` `6c2eca72`, `.Geometry` `da7c6fd2`, `.Hull` `84ac1720`, `.KClass` `70ec01d0`.

## Related
[[0013-hairline-gap-rule]], [[see-through-gap-checks-on-a-skinned-body]], [[fold-silhouette-gaps-in-layered-cloth]]
