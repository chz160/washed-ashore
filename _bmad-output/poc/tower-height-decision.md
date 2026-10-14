# Lookout tower: height decision

Owner: t-artist (technical artist). Date: 2026-10-10. Status: decided.

## Decision

**86'3" (26.289 m) from the top of the bed plates to the cab floor.** The count matches the 86'3" standard, not the 79'6" default.

| Item | Imperial | Metres above pad top |
|---|---|---|
| Base square (back to back of leg angles) | 20'0¼" | 6.1024 wide |
| Landing 1 (lowest) | 13'6" | 4.1148 |
| Landing 2 | 27'0" | 8.2296 |
| Landing 3 | 40'6" | 12.3444 |
| Landing 4 | 53'3" | 16.2306 |
| Landing 5 | 63'9" | 19.4310 |
| Landing 6 | 72'9" | 22.1742 |
| Landing 7 (highest) | 79'6" | 24.2316 |
| Cab floor (top of tower steel, 7'0" square) | 86'3" | 26.2890 |
| Window sill (top of the 3'6" lower wall) | 89'9" | 27.3558 |
| Eaves (top of the 4'0" window band; roof 8'0" square) | 93'9" | 28.5750 |
| Roof apex (6:12 over a 4'0" half span, before the ventilator) | 95'9" | 29.1846 |

- **8 flights, 7 landings, 115 risers.** Risers per flight, bottom up: 18, 18, 18, 17, 14, 12, 9, 9.
- Riser 9" (0.2286 m), going 6 15/16" (0.1762 m), pitch 12 in 9¼ = 52.4°. Stair 2'0" (0.6096 m) between stringers.
- Landings are numbered bottom up here (the 1935 sheets number them top down).

## What the standards predict

Read from USFS T-1002-A (`mc39-usfs-1935-tower-internal-stairs.png`) and IDECO E-4898 (`mc39-intl-derrick-1933-schematic.png`). Landing levels measured down from the cab floor are 6'9", 13'6", 22'6", 33'0", 45'9", 59'3", 72'9" and 86'3". Every flight's rise divided by its step count on the sheet gives 9" exactly, so the "9" tread spacing" on T-1006 is the riser.

| Standard | Flights | Risers, bottom up | Bottom panel |
|---|---|---|---|
| 72'9" | 7 | 18, 17, 14, 12, 9, 9 + 18 | 13'6" |
| 79'6" | 8 | **9**, 18, 18, 17, 14, 12, 9, 9 | **6'9"**, one X, no mid horizontal (IDECO "79-6 Tower" detail) |
| 86'3" | 8 | **18**, 18, 18, 17, 14, 12, 9, 9 | **13'6"**, with a mid horizontal (IDECO "86-3 Tower" detail) |
| 99'9" | 9 | 18, 18, 18, 18, 17, 14, 12, 9, 9 | 13'6" |

The 79'6" and 86'3" towers have the same number of flights and landings. They differ only in the bottom flight: 9 risers against 18.

## The count

**1. Panels on the nearest leg, `burns-nhlr-lutes-main.jpg`.** The heavy girts (gusset plate, both X diagonals meeting) on the front leg sit at these full-resolution pixel rows, read off ruled crops: cab floor 547, then 683, 854, 1054, 1327, 1731, 2262, 3006, and the footing at 3912. That is **8 panels, so 8 flights and 7 landings.** A lighter horizontal sits near the middle of every panel (rows 765, 965, 1196, 1531, 1981, 2603, 3490).

**2. Risers in the bottom flight.** Counting treads from the ground to the first landing in the same photo gives 18 to 20. It is a full 13'6" flight, not the 9-riser flight the 79'6" standard has.

**3. Projective fit.** Points on one straight leg project to the image by a one-dimensional projective map, `y = (a·h + b) / (c·h + 1)`, which has 3 unknowns. Fitting the 9 rows above to each standard's level heights:

| Hypothesis | RMS residual |
|---|---|
| **86'3"** | **14 px** |
| 99'9" with one landing dropped | 31 px |
| equal panels (no standard) | 46 px |
| 79'6" | 77 px |
| 72'9" with one level added | 82 px |

The readings are good to about ±15 px, so 86'3" fits within reading error and 79'6" does not. Under the 86'3" fit the lighter horizontals fall 47% to 64% of the way down their panels, which is where mid-panel horizontals belong.

**4. YouTube storyboards (VpTGZRPrPN0, M0 to M3).** Plank landings appear at about 0:08, 0:20, 0:32, 0:42, 0:50, 0:58 and 1:04, then the top level with the grid dish. That is 7 landings, with the gaps shortening as the flights do. It agrees with 8 flights but can't separate 79'6" from 86'3", because the clip starts with the climber already on the stair.

**5. Other sources.** NGS GC2560 (1953, 1959) says "about 90 feet". NHLR says "approximately 80 ft". 86'3" to the cab floor is 93'9" to the eaves, which suits the NGS figure.

Script and crops: scratchpad `t-artist/fit.py`, `strip.py`, `strip2.py`. The numbers above are enough to repeat the fit.

## Consequences

- **Base square is 20'0¼" (6.1024 m), not 19'0½".** The 19'0½" in the spec's T1 row belongs to the 79'6" tower. The IDECO table's fractions are hard to read; a straight taper through the 79'6" and 99'9" entries gives 20'0" to 20'0¾", so 20'0¼" is inside the spec's ±5 cm either way.
- T1 "height to the eaves" is 28.575 m (cab floor 26.289 m + 3'6" + 4'0").
- The height stays a parameter (`mc39_params.json`, `height_standard`); 79'6" still builds.

## Stair layout the model uses (an interpretation, flagged)

- Flights 1 to 7 are a dogleg on the tower's diagonal: they run parallel to one diagonal, alternate direction, and sit on alternate sides of the diagonal's centre line (USFS "diagonal views showing inside stairway", IDECO views EE and GG). Landings are triangular plank platforms in the two opposite corners, carried on the frame turned 45°.
- The top flight is turned to run along one face and comes up through the trap door beside that wall (USFS T-1004 "cab landing" detail, and the Burns photo). This keeps the cab centre clear for the firefinder.
- **Top flight pitch.** T-1006 says the top flight "is 8 in 12". Read as 8 rise in 12 run, its run would be 9'0", which doesn't fit a tower 8'0" wide at that level. The model uses the common 52.4° pitch for all eight flights, so the spec's 52° ±2° holds everywhere. It is a parameter.
- Landing depths are solved so each fixed-pitch flight meets its two landings as the tower narrows. They come out between 1.1 m and 2.2 m, against the 5'2" (1.57 m) marked on the lower landings of T-1004.
