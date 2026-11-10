---
type: decision
status: accepted
tech: [probe-tests, rendering]
area: [player-body]
issues: [49]
prs: []
source: "studio:decisions gate/pod49-hairline-gap-rule (+ note1-qa-conditions); td-49 B33, B33b, B34a, B34c, B38 (studio:engineering pod49/B38)"
decided_by: "director-49; accepted by Noah 2026-11-10"
created: 2026-11-10
updated: 2026-11-10
supersedes: ""
superseded_by: ""
---
# 0013 Hairline-gap rule: when a see-through gap is a hole

_A see-through gap bordered by one body region is a hole candidate. A gap between two separate limbs is a hairline gap, not a defect. A gap where two parts are joined (hip, armpit, neck) is a hole candidate again._

## Context
The earlier rule (td B24 G2) called every pixel fully enclosed by drawn body a pinhole. That also condemned the normal gaps between the legs, or between an arm and the body. The S2 frame check ([[see-through-gap-checks-on-a-skinned-body]]) needed a rule that tells the two apart.

## Decision
For each see-through component, cast rays through its border pixels and take the body region each one hits (a 3×3 sub-sample vote per pixel, B33b):
- **H1:** border in ONE region → pinhole / hole candidate. One-region layer wedges count here.
- **H2:** border in 2+ regions that are NOT joined at that spot → hairline gap between parts, not a defect. The director spot-checks lit crops.
- **H3:** border in 2+ regions that ARE joined at that spot → hole candidate.
  - Joined pairs: TORSO and LEG near the UpperLeg bone head; TORSO and ARM near the UpperArm bone head; HEAD and TORSO near the Neck bone.
  - "Near" = the hit of the border pixel closest to the centroid lies within 0.05 m (world) of that bone. The 0.05 m is the director's figure.
- If the joint test can't run (no hit, no bone, destroyed bone), the component counts as H3 UNRESOLVED, never H2 (fail-closed, B34a, B34c).

Noah was informed 2026-11-09 04:19Z and **accepted it on 2026-11-10** (B38). From then on it is the rule of record, not a labelled deviation.

## Consequences
- Not applied to past results. The two known pinholes (Sit and Squat) stay defects. They are covered by [[0015-fold-gaps-accepted-w-49-5]], not reclassified.
- Baton 20: 385 frames, 175 components: H1 67, H2 108, H3 0 (B34i).
- H1 alone can't tell a fold wedge from the gap between two fingers, which share a region. [[0014-s2-gap-classes]] splits H1 further.

## Related
[[0014-s2-gap-classes]], [[see-through-gap-checks-on-a-skinned-body]], [[player-character-body]]
