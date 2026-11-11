---
type: pattern
status: active
tech: [character-art, rendering]
area: [player-body]
issues: [49]
prs: []
source: "td-49 B30, B30c, B31, B32 (3), B34d, B35 C, B37; ta-49 d1_seam.py 4cc2fe4a (pod49 out/d1_seam.txt)"
created: 2026-11-10
updated: 2026-11-11
aliases: [fold gap, K-a, hairline wedge, backing hull, X1]
---
# Fold-silhouette gaps in layered cloth meshes

_When a body's clothes are separate layers baked into one mesh, deep bends make an outer layer's rim fold back. A hairline strip of world can show between that fold edge and the next surface. Nothing is missing from the mesh, so patching seams or adding an inner hull doesn't close it._

## Problem
At steep look-down in deep flexion (sit, squat, walk at +85) the player sees 1–2 px specks of the world through his trouser leg.

## When it applies
Meshy-style bodies with cloth as stacked shells: the Survivor has 6,990 shells, with 17,002 of 35,540 edges joined by position only (B31, B33a). Mostly the left shin and knee; recurring edges #2499/#16175, #2545/#17297, #2739/#2741 (B35).

## Why it happens
- The pinhole edges join their partners exactly, at rest and posed (offset 0.0, B31). Weights on position duplicates are identical (D1). So there is no crack.
- In the pose, the fold puts both sheets of the outer layer on one side in screen space: a fold **silhouette**.
- The world shows through between that silhouette and the next covering surface. Sit: the framing surface is 7.0 mm away in depth; Squat: 17–19 mm (B32 (3)).
- It is self-occlusion between layers crossing in the image, not a hole.

## Evidence
X1 backing hull, baton 22 P0 (valid): an inset skinned tube per arm and leg segment, swept at +3, 0, −3, −6 mm.
- K-a closed at best 7 of 39; the known spots 0 of 2.
- LEAK 5.0M–9.4M px over 330–349 of 385 frames: the hull drew over visible body (poke-through).
- Both failure modes at every inset, so no inset could fix them together (B37).

## What to do
- **Measure where the see-through rays travel before designing a fix.** A K-a ray passes between two cloth layers and may never enter the volume under the innermost layer. A cheap P0 sweep showed this; keep that order.
- Size the problem first with the S2 frame check ([[see-through-gap-checks-on-a-skinned-body]]).
- **Tried, did not work:**
  - A seam-closing strip (B30 N2): there is no gap at the seam to close (B31).
  - Two-sided rendering, MSAA or supersampling: the gap persists (D3, D4).
  - A skinned inner proxy hull (X1): misses the between-layer rays and pokes through.
  - Double-siding the fold (FC1): dropped with Noah's Bar 1 ruling, 2026-11-04.
- **Untried options:** a pose-driven corrective at the fold sites (X2, scripted Blender, re-export and full re-gate), or a weld or fill of the layers at the recurring edges (a mesh edit; conflicts with the director's "fill scope NONE").
- For pod 49 the gaps are accepted ([[0015-fold-gaps-accepted-w-49-5]]).

## Does not apply when
- The gap is bordered by two separate limbs (the gap between the legs): that is a hairline gap between parts, not a defect ([[0013-hairline-gap-rule]]).
- A real mesh hole with an open edge. Check open-edge loops first.

## Related
[[see-through-gap-checks-on-a-skinned-body]], [[0014-s2-gap-classes]], [[0015-fold-gaps-accepted-w-49-5]], [[the-survivor]]
