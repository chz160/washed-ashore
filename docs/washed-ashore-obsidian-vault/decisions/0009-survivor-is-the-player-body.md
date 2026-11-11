---
type: decision
status: accepted
tech: [character-art]
area: [player-body]
issues: [49, 95]
prs: []
source: "pod49 brief-phase2 'Noah's rulings since phase 1' (2026-11-03)"
decided_by: "Noah"
created: 2026-11-10
updated: 2026-11-11
supersedes: ""
superseded_by: ""
---
# 0009 The Meshy Survivor is the player body, as modelled

_The player character is the Meshy "Survivor" model Noah made, used as modelled: flannel over a T-shirt, cargo trousers, boots and a watch on his RIGHT wrist. Layered clothing is parked. Characters are made in Meshy; Blender is for scripted work only._

## Context
Pod 49 first planned a base body plus a layered clothing pipeline (#49's title still says so). Noah pivoted on 2026-11-03 to a new no-backpack Meshy model and to "what's on this model".

## Options considered
- Base body plus layered garments (phase 1 plan). Parked.
- The model as modelled, with its own clothes. Chosen.

## Decision
Noah, 2026-11-03 (pod 49 phase 2 brief):

> "Ok let's pivot a little then and not worry about changing how clothes are done at the moment."
> "since we can't do clothes right now we'll just use whats on this model."

Earlier, 2026-10-30 (`amend6-meshy-all-in`): go all-in on Meshy for characters. Blender is only for scripted fitting, skinning and hide-mask plumbing, never hand modelling of visible surfaces. Each Meshy asset gets its licence line in `CREDITS.md`.

Director's amend 7 (`amend7-model-as-is-phase2`):
- The watch stays, on his RIGHT wrist, on the view and shadow bodies alike. A watch on the left means the fit mirrored, and fails the look gate.
- No backpack at start. A backpack is a later pickup (#95).
- Parked: layered clothing, garments, the underwear test, spring bones, wind and weather.
- Anything head-worn is shadow only.

## Consequences
- Good, because the pod could ship a realistic body without building a clothing system.
- Bad, because cloth layers are baked into one mesh. Their folds open hairline gaps in deep flexion ([[fold-silhouette-gaps-in-layered-cloth]], [[0015-fold-gaps-accepted-w-49-5]]).
- No normal map for now (Noah, amend 8 note 1), so fabric reads flat up close. That is a follow-up, not a fail.

## Related
[[the-survivor]], [[player-character-body]], [[0010-realistic-human-skeleton-low-poly-humans-removed]]
