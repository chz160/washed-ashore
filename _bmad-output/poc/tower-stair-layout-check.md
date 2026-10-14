# Lookout tower: stair layout, drawings against generator

Owner: t-artist. Date: 2026-10-11. Asked for by team-lead after three symptoms on panels 6-7 (landing 6 slack, a brace grazing flight 7, landing depths off the drawing). Status: check done, **the layout changes**; generator not yet changed.

Sheets read (enlarged crops of the research scans): USFS T-1002-A (tower with inside stairs), T-1004-A (landing details), T-1006-A (stair details), IDECO E-4898 (landing plans, View EE, View GG). Landings are numbered bottom up here; the sheets number them top down (sheet "1st" = landing 7, "2nd" = 6, ... "7th" = 1 on the 86'3" tower).

## Finding

The topology was right. The landings and the stair's one free number were wrong, and both wrongs were mine, not forced by the drawing.

1. **The drawing fixes the free number.** T-1004-A's enlarged detail of the cab, 1st and 2nd landings dimensions the flight between them from the tower's centre line: **3'-1"** to the 2nd landing's edge and **2'-1 3/8"** to the 1st landing's edge. Their sum is 1.5843 m. Nine goings of 6 15/16" are 1.5859 m. So a flight's plan length between landing edges is **n goings for n risers** (I had n - 1), and the top landing's edge is 0.645 m from the centre line (I had 0.484 m, derived from a clearance rule of my own).
2. **Landings are small rectangular platforms, not corner-filling triangles.** T-1004-A marks **5'-2" x 2'-6"** on the 6th, 7th and 8th landings and 2'-6" on the 5th; E-4898 draws the same platform frame (713F) at every landing. 5'-2" is two 2'-6" flights side by side with a 2" gap; 2'-6" is the platform's depth from its inner edge toward the corner. I filled the whole corner out to the leg, which is where the 2.3 m "depths" came from.
3. **Where the platform is deeper than the corner, the tower faces cut it**, which is why the upper landings read as triangles in the nomination text and the photos. With the drawn anchors, the room from inner edge to corner on landings 6 and 7 is 1.01 m and 1.08 m; the digest's "0.94 m" for the upper landings is this.

## Per flight, 86'3" tower

Plan direction: every flight but the top one runs on one tower diagonal, alternating; landings alternate between the two opposite corners on that diagonal (T-1004-A plans: 8th, 6th, 4th, 2nd in one corner, 7th, 5th, 3rd in the opposite one; E-4898 View EE zig-zag and View GG). Turn on each landing is a half turn onto the flight beside the one just climbed. Top flight is turned to run along a wall to the cab landing and trap door (T-1004-A enlarged detail).

| Flight (bottom up) | Risers (T-1002-A) | Runs between | Drawing: plan length between landing edges | Generator today | Difference |
|---|---|---|---|---|---|
| 1 | 18 | ground to landing 1 | 18 goings = 3.172 m | 17 goings = 2.996 m | one going short |
| 2 | 18 | landing 1 to 2 | 3.172 | 2.996 | one going short |
| 3 | 18 | 2 to 3 | 3.172 | 2.996 | one going short |
| 4 | 17 | 3 to 4 | 2.996 | 2.819 | one going short |
| 5 | 14 | 4 to 5 | 2.467 | 2.291 | one going short |
| 6 | 12 | 5 to 6 | 2.115 | 1.938 | one going short |
| 7 | 9 | 6 to 7 | 1.586 (dimensioned: 3'-1" + 2'-1 3/8" = 1.584) | 1.410 | one going short |
| 8 (top) | 9 | landing 7 to cab landing | about 4'-6" scaled off the detail; note "8 in 12" on T-1006-A; **2'-4" wide** | 1.410 at 52.4 deg; 2'-0" between stringers | see below |

Riser counts, 9" rise, 12 in 9 1/4 pitch, 2'-0" between stringers, corners and alternation: no difference.

## Per landing

Distance from the tower centre line to the landing's inner edge, measured along the flight. Drawing column: the two dimensioned values, then the chain `edge(k-1) = n(k) x going - edge(k)` down the tower (re-runnable: going 0.17621 m; risers 18, 18, 18, 17, 14, 12, 9). "Plan-frame edge" is where the frame turned 45 degrees sits at that level (half width / sqrt 2), which is where the sheets draw the platforms.

| Landing | Sheet name | Level m | Drawing: inner edge from centre line | Plan-frame edge | Generator today | Drawing: platform | Generator today |
|---|---|---|---|---|---|---|---|
| 7 | 1st | 24.232 | **0.645 (2'-1 3/8", dimensioned)** | 0.864 | 0.484 | along the wall beside the top flight; no size marked | triangle, 1.24 deep |
| 6 | 2nd | 22.174 | **0.940 (3'-1", dimensioned)** | 0.974 | 0.925 | room to the corner 1.01 m: the faces cut it to a triangle | triangle, 1.02 deep |
| 5 | 3rd | 19.431 | 1.175 (chain) | 1.120 | 1.013 | 2'-6" deep, cut by the faces | triangle, 1.23 deep |
| 4 | 4th | 16.231 | 1.292 (chain) | 1.291 | 1.278 | 2'-6" deep, cut by the faces | triangle, 1.31 deep |
| 3 | 5th | 12.344 | 1.703 (chain) | 1.499 | 1.542 | 2'-6" marked; cut by the faces | triangle, 1.46 deep |
| 2 | 6th | 8.230 | 1.468 (chain) | 1.718 | 1.454 | **5'-2" x 2'-6"** marked | triangle, 1.98 deep |
| 1 | 7th "86'3" tower only" | 4.115 | 1.703 (chain) | 1.938 | 1.542 | **5'-2" x 2'-6"**; its own frame with 6x4x3/8 beams and 4x4x1/4 struts | triangle, 2.33 deep |
| ground | foot of flight 1 | 0 | 1.468 (chain) | - | 1.454 | concrete landing for stairs (E-4898) | pad |

The chain lands landing 4 on its plan-frame edge to 1 mm and landing 5 within 0.06 m, which is where the sheets draw them. Landings 1 and 2 come out 0.24 m and 0.25 m inside their plan-frame edges; the 86'3" tower's own 7th-landing detail carries extra beams, which is what a platform off the frame edge needs. I can't read a dimension for those two, so their position rests on the chain.

## Differences and what happens to each

| # | Difference | Action |
|---|---|---|
| D1 | Flight plan length is n goings between landing edges, not n - 1 | Fix the generator. The lane's foot then sits on the lower landing's edge |
| D2 | Free number: top landing edge 0.645 m from the centre line (drawn), not 0.484 m | Fix: the table carries the drawn 2'-1 3/8" with the sheet reference; my clearance rule goes |
| D3 | Landings are 5'-2" x 2'-6" platforms cut by the faces, not corner triangles | Fix. Landings 1-2 become free rectangles on two beams; 3-7 are cut by the faces. Guard rails go on the platform's own edges (T-1006-A typical landing), not on the tower faces |
| D4 | Two flights side by side make 5'-2": centres 0.406 m either side of the diagonal, not 0.365 m | Fix |
| D5 | Top flight: the detail scales to about 4'-6" of plan for 9 risers, so "8 in 12" on T-1006-A is 8 run in 12 rise (56.3 deg), not the 33.7 deg the digest read. The ruled 52.4 deg +/- 2 on all flights does not match the sheet | Not changed. T1's pitch row is ruled; this goes to t-qa and team-lead as a target question |
| D6 | Top flight is 2'-4" wide on the sheet; the generator uses 2'-0" between stringers like the others | Not changed without a ruling: it may be the overall width of a 2'-0" stair |
| D7 | Top landing shape and exactly where the top flight starts on it | The sheet gives no dimension I can read beyond the 2'-1 3/8". The generator's arrangement (landing 7 in its corner, top flight along the next wall to a hatch in the far corner) has the sheet's topology; positions stay an interpretation and are disclosed |

## The three symptoms

- **Landing depths 0.98-2.37 m against 1.57 m:** explained by D3. 1.57 m (5'-2") is the platform's width; its depth is 2'-6" (0.762 m).
- **Landing 6 slack and the lane-end margins of 5 mm and 1 mm:** the walls were on the tower faces of corner triangles. With D1-D4 the lane ends move (landing 6's edge 0.925 to 0.940, landing 7's 0.484 to 0.645) and the walls are the platform's own rails. The margins have to be re-measured after the change; I am not predicting them.
- **The panel 6 diagonal grazing flight 7 and the 0.162 m eye distance on flight 8:** both are at lane ends that move under D1, D2 and D4. Re-measured after the change, reported as found.

## What I could not establish

- No dimension for landings 1-5 other than the platform size; their positions follow from the two dimensioned edges and the riser counts.
- Which side of the diagonal each flight is on, and the turn direction, are read from sketches without dimensions; the generator keeps the arrangement that matched the Burns photos at G1.
- The 1700 px scans do not resolve the 1st-landing frame well enough to dimension it.

## After the change (2026-10-11, wrapper build in tools/blender/lookout/out/build/)

D1 to D4 are in the generator, with the sheet values in the parameter table (`stairs.top_landing_edge_in` 25.375, `second_landing_edge_in` 37 as a cross-check that fails the build if the chain misses it by more than 5 mm, `lateral_offset_in` 16, `landings.platform_width_in` 62, `platform_depth_in` 30).

Measured by the build's own sweeps, same method as before (member sections, not centre lines):

| Check | Before | After D1-D4, top flight as it was (52.4 deg, head 0.37 m from the wall) | After D1-D4 and the top flight to the sheet |
|---|---|---|---|
| Lane ends to the nearest rail plane, flights 1-7 | 0.305 to 0.432 m | 0.346 m at all thirteen | 0.346 m at all thirteen |
| Lane end, flight 8 foot | 0.301 | 0.266 | **0.203** (fails the 0.32 check) |
| Eye path to nearest member | 0.162 (north-face diagonal, flight 8) | 0.162 | **0.302** |
| Frame members in a 0.61 x 1.90 corridor | 2 grazes on flight 7 | 0 on flight 7; flight 8's stringer 0.10 m into flight 7's head | **0** |

So the symptoms on landing 6 and flight 7 were the landing shape and the missing going. What was left sat at the top flight, and that is D5:

- **D5 is now changed to the sheet, and it needs a ruling because it moves a ruled T1 target.** With a 52.4 deg top flight (9 goings of 6 15/16" = 1.586 m of plan), the flight cannot fit between the 1st landing and a cab landing 9" off the far wall without overlapping the head of flight 7; that overlap was the stringer in flight 7's corridor and the foot standing against the north face. At the sheet's 12 rise in 8 run, 9 goings of 6" are 1.372 m = 4'-6", which is what the detail scales to, and the top flight's start line then meets the end of flight 7's head edge, as drawn. The table now has `top_flight.pitch_rise` 12, `pitch_run` 8, `head_from_wall_in` 9. Flight 8 is 56.3 deg; flights 1-7 are 52.4 deg. Reverting is those three values.
- Hatch opening moves with it: still 1.50 x 0.66 m, now 0.229 m (9") from the south wall instead of 0.37 m. The floor beyond the lane head is 0.18 m to the wall's collision face.
- **Still failing: flight 8's foot is 0.203 m from an invisible wall of my own**, the one on landing 7's inner edge under the top flight (omission OM2's stretch). The sheet's 1st landing is drawn along the wall and is larger than my 5'-2" x 2'-6" platform there (D7); I have no dimension for it, so I have not reshaped it or moved the wall.
- Rails inside a corridor (all drawn rails, listed in `manifest.corridor.exceptions`): flight 7's right handrail ends 0.06 m inside flight 8's corridor over its first 0.08 m; one landing 7 guard rail and its post touch flight 8's corridor edge by 5 mm and 2 mm.
- OM1 becomes 0.67 m short of the hatch (same rule, steeper flight).

Landing platforms as built: 1.575 m wide at the inner edge and 0.762 m deep on all seven; the faces cut landings 3-7.

## Top flight pitch: reason and supporting arithmetic (added after t-qa's review)

The reason flight 8 is 56.3 deg is that the sheet says so: T-1006-A's pitch triangle is labelled 9 1/4 horizontal by 12 vertical with the note "for all except top flight which is 8 in 12", so by the same construction the top flight is 8 run in 12 rise. t-qa reads it the same way. T1's pitch row is open with team-lead until ruled; the value is a table parameter.

Supporting arithmetic, re-runnable from the table:
- Cab landing 9" off the far wall: flight 8's head at Z = -(1.0668 - 0.2286) = -0.838 m.
- Flight 8's start line cannot be north of the end of flight 7's head edge. Flight 7's head: 0.645 m along the diagonal, centre 0.432 m off it, half width 0.305 m, so the edge's end nearest flight 8 is 0.127 m off the diagonal: Z = (0.645 + 0.127) / sqrt 2 = 0.546 m.
- Plan length available: 0.546 + 0.838 = 1.384 m.
- 9 goings at 12 in 9 1/4: 9 x 0.17621 = 1.586 m, 0.20 m too long (flight 8's first step would stand in flight 7's head).
- 9 goings at 12 in 8: 9 x 0.1524 = 1.372 m, which fits with 0.012 m to spare.

## Flight centre offset (settled with t-qa)

17" (0.432 m): 2'-4" overall stair (2'-0" between 2" stringer angles, marked across the top flight) + 6" between adjacent flights (marked at the 2nd landing) + 2'-4" = 5'-2". My first reading, two 2'-6" flights 2" apart, gave 16" (0.406 m); with 2'-4" flights that would leave a 4" gap, which the sheet's 6" contradicts. D4 and D6 above are superseded by this: the stair stays 2'-0" between stringers.

## Figures on the build with 17" centres and guard rails on the platform edge

Lane ends, flights 1-7: 0.356 m. Flight 8 foot: 0.203 m, to an invisible wall of mine on landing 7's inner edge that stands for nothing drawn; to the nearest rail plane it is 0.52 m. Flight 8 head in the cab: 0.177 m to the south collision plane. Eye path over lanes: 0.299 m. Landing walk areas: 0.435, 0.435, 0.435, 0.178, 0.253, 0.162, 0.163 m for landings 1-7. Frame members in a corridor: none. Rails: flight 7's right handrail 0.047 m into flight 8's corridor over its first 0.05 m. Cab and landing 7 plan: TestResults/tower-art/cab-landing7-clearance-map.txt.

## Interpretations awaiting t-director by name

Positions of landings 1-5 (from the two dimensioned edges and the riser counts); which side of the diagonal each flight takes and the turn direction; the top landing built as a standard platform; two bearers and three joists under each platform.
