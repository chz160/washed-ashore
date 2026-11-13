# #9 visible-difference and pop-in review (technical-artist, task #28)

Run: `TestResults/9-runs/20261112-1803` (stills table: `TestResults/9-analysis.md`, "Stills"). Method: eye on the trial/authored pair plus a numeric diff. Eye is the judgement; numbers support it.

- Numeric: `tools/perf/popin_diff.py` (delta over 24/255, speckle removed, regions of 150 px or more). FAIL rule: a region of 500 px or more with peak delta 40 or more whose top is above y 700, because flipping between the two stills shows a changed silhouette. This is my judgement rule, stated here, not an engine fact.
- Command: `python tools/perf/popin_diff.py TestResults/9-runs/20261112-1803` (all three runs, cab skipped). Composite for a pair: `... --composite <still.png> <out.png> x0,y0,x1,y1`.
- The same verdict came out in runs r1, r2 and r3 for every still.

## FAR 600 m against 1000 m (Q2, ground spots)

| spot | h000 | h090 | h180 | h270 | own | spot verdict |
|---|---|---|---|---|---|---|
| park | FAIL | FAIL | PASS | PASS | FAIL | **FAIL** |
| ridge_top | FAIL | FAIL | FAIL | PASS | FAIL | **FAIL** |
| river_bank | PASS | PASS | PASS (vacuous) | PASS (vacuous) | PASS (mostly vacuous) | **PASS, on sea-facing h000 and h090 only** |
| valley_floor | FAIL | PASS | PASS | PASS | PASS | **FAIL** (h000) |

What is visible where it fails (authored = far 1000, trial = far 600; paths are `s<NN>-r1-<spot>/<spot>-r1-w01-FAR-h<heading>.png` and its `-authored.png` twin):
- **ridge_top h000, h090, h180, own** (`s04-r1-ridge_top`): the beige horizon band grows from about 30 px to about 75 px tall across the whole width (delta 62/255, regions up to 25,329 px). The pale fogged forest and hills that sat below it in the authored still are gone. Clearly visible.
- **valley_floor h000** (`s03-r1-valley_floor`): a pale fogged mass above the tree line (846-1071 x 444-504, 9,203 px, delta 52) is replaced by sky. Visible.
- **park own** (826-932 x 432-479, 2,392 px, delta 66): the pale distant mass behind the trees is replaced by sky. Visible. **park h000** (1120, 758 and 687 px regions at the skyline, delta 49-64) and **park h090** (a 551 px patch at the left edge, delta 47): smaller, same cause; a flip shows them.
- **river_bank (corrected after qa-lead's check of h270):** the evidence is the sea-facing headings only. `s02-r1-river_bank/river_bank-r1-w00-A-h000.png` and `-h090.png` show sea to a clear horizon (trunk and shrubs on one side), and FAR 600 changes nothing there (h000: 2,324 px over 8/255, no region over 24/255). h180 and h270 are closed-in forest and path, with trees and shrubs blocking the view; nothing past 600 m is visible, so they pass only because there is nothing to cull (vacuous). `-hown` (yaw 323) is mostly a trunk (about 17% by the bark mask) and forest with a sliver of sea at the right edge, so it is mostly vacuous too. The verdict applies to sea-facing river_bank headings, not to "river_bank-type views" in general. **Why the sea band does not change at 600 m (ridge_top's horizon band did) is assumed, not checked:** the sea quads or the fog colour already match at that point.
- **Other PASS stills:** nothing above the speckle level at the skyline. park h180/h270, valley h090/h180/h270/own show no skyline region (some of these are also vacuous: nothing far is in view).
- **Every FAR still, including the PASS ones,** has low-amplitude differences on near grass and ground (peak about 48/255, a soft shift of shadow edges, for example `s01-r1-park/park-r1-w01-FAR-h090.png` at 260-460 x 880-960). I judge these not visible to a player. The noise floor between two untouched stills is 1-790 px, so something does differ; the cause is unconfirmed (assumed: shadow cascade or depth precision changing with the far plane).

Smallest passing distance: **not measured.** The run tested only 600 m (and 1150 m in the cab). Assumed: the fogged geometry out to 1000 m still stands out against the sky (fog colour is paler than the sky at the horizon), so any distance short of the farthest visible content changes the skyline. No intermediate far-clip distance can be called passing without a run.

## FAR in the cab (1150 m, cost reading, not a candidate)

Not graded as a candidate. Diff restricted to the lower panes (y 570-830, frames masked by eye, HUD prompt "E - Open window" near y 735 present in both arms): the horizon band edge moves about 8 px in every pane (`s05-r1-cab/cab-r1-w01-FAR-h090.png` against its authored twin). Small, but it is a changed edge.

## LAYER and SMALLMESH (Q3)

- **LAYER: PASS at every spot and heading.** Trial against authored stays at the noise floor (for example ridge_top h000 394 px against noise 351; park 1-7 px; `9-analysis.md`). There is nothing to pop in. No LAYER `-edge-` stills exist in the run folder (only SMALLMESH has them), so the edge check for LAYER is not done; the "no effect" reading stands on the trial-against-authored stills only.
- **SMALLMESH: PASS, no pop-in seen.** The `-edge-smallMesh-culled/-shown` pairs differ by a few small leaf or bark flecks (ridge_top 1,337 px, peak 70, inspected as small isolated flecks; river_bank about 1,100 px; park and valley 1-26 px; cab 5,300-5,600 px, within the cab noise floor of 6,500-9,100). No object appears or disappears. Weak evidence: the ridge_top edge frame is a close-up of a trunk, so it contains no small meshes near their cull edge. The measured effect is none, so there is nothing to pop.
- **ridge_top edge pairs, checked after qa-lead asked** (`s04-r1-ridge_top`, `s07-r2-ridge_top`, `s14-r3-ridge_top`, `ridge_top-r<N>-w0<N>-SMALLMESH-edge-smallMesh-culled/-shown.png`): 1,337 / 1,385 / 1,554 px over 8/255 between culled and shown, but only 31 / 27 / 26 px over 24/255, and at most 10/255 in the lattice-tower zone (780-1060 x 560-860). No coherent region, no drop-out found. **The target cannot be identified in frame:** the aim is a steep look-up with the survivor's boot filling the top centre (confirmed by platform-tools-engineer: `FrustumProbe.EdgeStills` aims at the target's bounds centre with no line-of-sight check and no hiding of the player body). I cannot say the ring (`MC39_Firefinder_Ring`, 62.5 m) was in view, and an edge pair has no same-frame repeat, so the over-8 counts have no noise floor. So this is a limitation, not a pop-in clearance. SMALLMESH's conclusion rests on its counters (no effect). A clean pop-in frame needs an occlusion-checked, re-aimed launch, which was not run.
- Pop-in "inside fog" could not be shown for either, because neither changed what was drawn.

## Limits
- Judged by eye with numeric support, no player-run video. A still pair shows a static view; the report says nothing about movement.
- Cab diff region masks were chosen by eye, not generated.
