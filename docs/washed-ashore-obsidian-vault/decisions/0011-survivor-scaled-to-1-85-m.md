---
type: decision
status: accepted
tech: [character-art]
area: [player-body]
issues: [49]
prs: []
source: "studio:decisions gate/pod49-uniform-scale-1027-eye; pod49 HANDOFF section 3a (Noah accepted 1.85 m)"
decided_by: "td-49 (tech), director-49 (look); Noah accepted"
created: 2026-11-10
updated: 2026-11-10
supersedes: ""
superseded_by: ""
---
# 0011 The Survivor is scaled to 1.85 m so the eye matches the camera

_The Survivor gets a uniform skeleton scale of 1.0266, so his eyes sit at the camera's standing eye height. He becomes about 1.85 m tall instead of the modelled 1.80 m._

## Context
The camera's standing eye height is a must-keep (1.65 m). td-49 found the body-frame eye target is 1.70 m (1.65 + footDrop 0.05). The Survivor's modelled eye is 1.656 m, 4.4 cm low.

## Options considered
- Move the camera. Not allowed: eye heights are a must-keep.
- Stretch parts of the body. Breaks look rule L3 (his proportions).
- Uniform scale. Chosen. The Farmer used 1.03.

## Decision
- Tech (td-49): the skeleton stays at his true size; the appearance gets `skeletonScale` 1.0266.
- Look (director-49): approved. "Uniform scale keeps his proportions (L3); 1.85 m is still an ordinary man; the eye-match must-keep outranks his exact height."
- Noah accepted a height of 1.85 m (pod 49 handoff, section 3a).

## Consequences
- Good, because the camera sits at his eyes with no camera change. S6 confirmed the eye heights are unchanged: stand 1.65, crouch 1.20, squat 1.00, sit 0.80, sleep 0.20 (b23 log, `gate/pod49-s6-look-gate`).
- Bad, because he stands about 5 cm taller than modelled.

## Related
[[the-survivor]], [[player-character-body]], [[0010-realistic-human-skeleton-low-poly-humans-removed]]
