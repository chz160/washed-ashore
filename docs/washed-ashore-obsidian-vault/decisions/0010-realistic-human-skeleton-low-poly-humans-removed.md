---
type: decision
status: accepted
tech: [character-art, animation]
area: [player-body]
issues: [49, 36, 96]
prs: []
source: "pod records (retired)"
decided_by: "Noah (P4); director-49 (look approval)"
created: 2026-11-10
updated: 2026-11-11
supersedes: ""
superseded_by: ""
---
# 0010 A realistic human skeleton; the low-poly humans are removed

_Human characters move to a new realistic shared skeleton, `Skeleton_Human`, built from the Survivor's own joints. The low-poly Farmer and every other low-poly human, men and women, are removed rather than refitted. The non-human kit stays._

## Context
The old game skeleton (`Skeleton_Men`, 79 bones) has a short upper arm (0.182 vs forearm 0.235). Fitting the realistic Survivor to it meant squashing his limbs, which the director's look rule L3 forbids ("no limb squash on any shipped body").

## Options considered
- P1: fit him to the old skeleton with segment scaling. Superseded by amend 8; never built.
- P4: a new realistic shared skeleton. Chosen.

## Decision
Noah, 2026-11-03 (amend 8):

> "P4. but something to point out the farmer and any other low poly human characters we've made don't need to be refitted. They can be removed"

Follow-up answers (amend 8 note 1): "Yes, remove women too". The non-human kit stays.

Director's approval of the skeleton (`gate/pod49-p4-skeleton-look-approval`):
- `Skeleton_Human` takes its lengths from the Survivor's joints, symmetrised, and his mesh is fitted by rotation only.
- The same 79 bone names carry #36 (the women's base) and later bodies, with per-body lengths in a later ADR.
- The women's 62-bone skeleton retires with the low-poly humans.
- Every must-keep is re-proven on the new skeleton, not assumed (camera, look-down Bar 1, head hide and shadow, frame time, eye heights, tuned speeds, spawn, `upperStepOffset`).
- Removal is its own commit, after Noah's OK.

## Consequences
- Good, because one realistic bone set can carry future Meshy bodies.
- Bad, because the clips had to be retargeted onto the new skeleton (clip set of record: clips20, `gate/pod49-clips20-jog-look-note2-final`). A reusable retarget script is #96.
- **Not done yet (2026-11-10):** removing the low-poly humans ("item 6"). It follows the pod-49 commit, in a separate worktree, as its own change set. The before-stills in `_bmad-output/poc/character-stills/` stay as the comparison of record (S6 condition C3).

## Related
[[0009-survivor-is-the-player-body]], [[0011-survivor-scaled-to-1-85-m]], [[player-character-body]], [[the-survivor]]
