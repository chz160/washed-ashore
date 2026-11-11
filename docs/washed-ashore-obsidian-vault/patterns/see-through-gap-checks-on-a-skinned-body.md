---
type: pattern
status: active
tech: [probe-tests, rendering]
area: [player-body]
issues: [49]
prs: []
source: "td-49 B21a, B22, B24, B26-B34i (pod49 td records); qa-49 12jr, 12jv, 12kr; test files HairlineGapSizing*.cs, PinholeD3.cs, PinholeD4.cs"
created: 2026-11-10
updated: 2026-11-11
aliases: [S2, S2 frame check, HairlineGapSizing, pinhole check]
---
# See-through gap checks on a skinned body

_A grid of test rays proves "no holes" only where the rays land. One-pixel see-through gaps slip between them. To find them, render the body alone in the same frame as the lit view, flood-find the enclosed empty pixels, and classify each one by what borders it._

## Problem
In first person the player sees the world through a 1-px speck in his own leg. The ray-grid gate had passed.

## When it applies
Any check that a first-person or shadow body has no holes: the look-down gate (`LookDownTests`), the pose stills and the S2 frame check (`HairlineGapSizing`, PlayMode, `[Explicit]`, test-only).

## Why it happens
- The look-down gate casts 32×18 rays (plus 16×9 and 9×5 grids). A pinhole smaller than the ray spacing is never hit. The Sit and Squat pinholes were missed this way (qa 12jr).
- A colour threshold misses them too: on the lit still the Sit pinhole differed from its neighbours by at most 11 per channel. A draft threshold of 24 would have passed it (qa 12jv).

## Evidence
- Gate ×3 (b14-gate1/2/3): 381 cells each, 0 Bar 1 defects. Baton 23 added 2,934 rows, 0 defects. Yet S2 on baton 22 found 175 enclosed components over 385 frames, 39 of them K-a defects (66 px) (qa sign-off, `pod/49/qa-signoff`).
- **Ruled out by measurement:**
  - Two-sided rendering: with culling off the pixel stays empty (D3, baton 17b).
  - GPU skinning differing from `BakeMesh`: 0.000 mm difference (D3).
  - Triangle order changing in the bake: index-order FNV-1a `7e3eaf8a` matches (D3).
  - MSAA or supersampling: at 4× the gaps persist (D4, baton 18).
  - CPU float error at the world position (about 622 m from the origin): about 0.05 px, too small for a 0.75 px slit (B30c, arithmetic, not measured).

## What to do
**S2 frame check (the method of record):**
1. Walk every gate frame (each stance and gait, every table pitch ≥ +45, 8 cycle points, idles at 3 phases) plus the 4 first-person poses.
2. In each frame, render a body-only image and a lit image in the same frame, with no yield between.
3. A background pixel with all 8 neighbours drawn body is enclosed. Flood-fill the components.
4. Cast rays through the border pixels (3×3 sub-sample vote each) to name the regions around the gap. Classify by [[0013-hairline-gap-rule]] and [[0014-s2-gap-classes]].
5. Attribute each gap: hit triangles, the two nearest framing edges, and their 3-D separation in mm.
6. Write crops (plain, marked, lit) for the director.

**Make it fail closed:**
- A frame that throws writes an EXCEPTION row, and the summary's first line prints `EXCEPTIONS n`. The run counts only with 0 (B34c F4).
- A control must bite: the known Sit (391, 397) and Squat (373, 490) pixels must be flagged in their poses, or that pose is VOID (K1).
- Read outputs in this order: EXCEPTIONS, K1, the unresolved/unclassified/no-region columns, then the crops.
- State the reading of every result before the run.

**Report the limit honestly:** "Bar 1 is proven on sampled rays only." Say so in any close-out.

**Did not work:** a size cut ("small components are fine") and "any enclosed component is a pinhole". The first passes real holes; the second condemns the gaps between the legs (B24).

## Does not apply when
Checking something other than visibility, for example mesh topology. Position-only edge joins are normal in this body (17,002 of 35,540 edges, B33a); they're not a hole signature.

## Related
[[fold-silhouette-gaps-in-layered-cloth]], [[reading-a-non-readable-mesh-in-tests]], [[probe-and-playmode-test-gotchas]], [[0015-fold-gaps-accepted-w-49-5]], [[player-character-body]]
