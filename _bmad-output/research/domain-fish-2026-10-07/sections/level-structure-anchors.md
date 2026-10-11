---
section: level-structure-anchors
author: f-level (level-designer)
date: 2026-10-07
phase: 0 (read-only)
sources:
  - Data/terrain/build/bank_stations.json, map_vectors.json, manifest.json, tile_*.raw, dem_*.raw (bb-terrain-rc4 build)
  - Assets/World/BellsBend/LevelZones.png (zone grid)
  - tools/terrain/heights.py (bed_profile), tools/terrain/README.md
  - _bmad-output/research/domain-bells-bend-map-2026-10-05/digests/landcover-landmarks-r1-1.md (Robertson Island lat/lon)
reproduce: scratchpad fish-anchors/anchors.py and stats.py (python, numpy, PIL; run from the project root)
---

# Structure anchors on the Bells Bend water

All positions are Unity world XZ in game metres (+X east, +Z north). 1 game m = 2 real m horizontally; vertical scale is 0.7.
W = `MapConfig.WaterLevelY` = 1.61. "st" = an index into `Data/terrain/build/bank_stations.json`, the inner bank sampled every 5.1 m.
Depths below come from the built terrain tiles (not WaterMaps G, which clamps at 4 m), averaged over each anchor's stations.

## 1. The bed under every anchor is the same

`tools/terrain/heights.py` (`bed_profile`) gives all water south of the north line one profile, measured outward from the waterline. Every anchor measures within ±0.15 m of this:

| Metres out from waterline | 1 | 3 | 5 | 7 | 10 | 15 | 20 | 30+ |
|---|---|---|---|---|---|---|---|---|
| Depth below W (m) | 0.05 | 0.66 | 1.31 | 1.94 | 3.05 | 7.3 | 10.0 | 10.0 |

- Bank/shelf band (≤ 2 m deep) = 0 to about 7 m out. Drop-off/ramp (2 to 10 m) = 7 to 20 m out. Open water (flat W − 10) = beyond 20 m.
- **Bluffs are sheer above the water only.** They keep the standard lip, the 18° shelf and the ramp underneath; there's no deep water against the face. The bank-cap exemption (`bankBluffExclusionMeters`, the steep-face rule) only spares the land above W.
- South of the north line there is no far bank. Everything outside the playable polygon is flat W − 10 out to the map edge, and LevelMaps shore distance clamps at 127 m. The only opposite bank is north of the line (z > 1986.7), behind the position clamp.
- **No submerged structure exists in the game:** no snags, riprap, piers, pilings or ferry ramp. The bed is purely procedural. The `WreckedVehicle` objects in World.unity are barrier dressing on land, and the arrival-wreck idea (water-td-note §3) isn't built. Anything submerged that the research wants would be new content (art kit, then level placement) and isn't covered by this survey.

So in-game "structure" is a **stretch of shoreline** plus the shelf and ramp in front of it (0–20 m out), not different bathymetry. The real-world reasons fish hold there (rock, current breaks, inflow, woody debris) are lore that the census expresses through density, not geometry.

## 2. Anchor table

Suggested reach: **20 m** out from the waterline for every anchor (shelf + ramp). Length = bank polyline length along the waterline.

| Anchor | Bank stations | Waterline from → to (XZ) | Centre (XZ) | Suggested length along shore | Water side (unit normal) | Bank above water | Land height above W at 2 / 10 / 40 m inland |
|---|---|---|---|---|---|---|---|
| **McCord Bluff face** | 1562–1645 | (550.6, 522.8) → (314.8, 867.9) | (443.3, 697.9); top landmark (401.36, 717.27), W+27 | 420 m | NE (0.70, 0.71) | Sheer (station max slope median 56°) | 2.6 / 12.8 / 41.1 |
| **Buzzard Bluff face** | 1763–1868 | (253.6, 1439.6) → (519.0, 1910.7) | (371.9, 1678.1); landmark (413.84, 1793.19), W+24.7 | 543 m | E/SE (0.87, −0.49) | Sheer (61°) | 3.6 / 12.5 / 44.0 |
| Unnamed E bluff face A | 1681–1693 | (233.8, 1034.2) → (217.9, 1092.3) | (221.9, 1062.1) | 61 m | E (0.95, 0.32) | Sheer (61°) | 2.6 / 12.1 / 27.4 |
| Unnamed E bluff face B | 1709–1723 | (211.2, 1173.9) → (213.1, 1244.4) | (209.2, 1209.5) | 71 m | E (1.00, −0.06) | Sheer (58°) | 3.3 / 12.4 / 39.2 |
| Unnamed E bluff face C | 1739–1753 | (221.9, 1324.1) → (238.4, 1392.0) | (229.0, 1358.3) | 70 m | E (0.96, −0.27) | Sheer (54°) | 1.5 / 11.7 / 34.5 |
| Unnamed NW bluff face | 65–92 | (−1173.2, 1777.9) → (−1077.3, 1681.5) | (−1127.0, 1731.5) | 136 m | SW (−0.71, −0.71) | Sheer (65°) | 5.7 / 17.8 / 30.8 |
| Bluff stub at the north line | 1879–1888 | (555.1, 1952.7) → (587.7, 1984.0) | (569.6, 1966.6) | 45 m | SE (0.70, −0.72) | Sheer (70°), 20 m south of the line | 3.7 / 11.4 / 28.6 |
| **Cleeces Ferry landing** (closed 1990, position approximate) | 327–337 | ±25 m about the centre | landmark (−980.91, 512.23); waterline (−985.9, 511.8) | 50 m | W (−0.99, −0.12) | Standard low bank (lip, 18° slope; 55% low bank) | 1.0 / 3.6 / 2.2 |
| **Boat slipway** (east bank, OSM) | 1116–1126 | ±25 m about the centre | landmark (1156.86, −1418.08); waterline (1161.7, −1419.4) | 50 m | E/SE (0.93, −0.38) | Standard, slightly higher bank (W+3.6 at 10 m); no ramp geometry | 1.5 / 3.6 / 3.2 |
| **Robertson Island river face** (see §3) | 1408–1522 | (1088.9, −54.1) → (658.9, 347.9) | lat/lon point (860.5, 163.1) | 592 m | NE (0.61–0.80, 0.60–0.79) | Standard low bank (most of it lowBank) | island land W+1.4 to W+3.4 |
| Robertson Island, south chute mouth (island tail) | 1404–1412 | — | (1088.9, −54.1) | 40 m | NE | Standard | — |
| Robertson Island, north chute mouth (island head) | 1518–1526 | — | (658.9, 347.9) | 40 m | NE | Standard | — |

### Hollow and creek mouths

Detection rule: the mean land height 30–120 m inland is a local minimum along the bank, with at least 8 m of higher ground on both sides within 250 m along the bank (prominence below). Each anchor is about 40 m of bank. "Real mouth" means the valley floor comes down to the water (land under W+4 at 10 m inland). "Hanging" means the hollow ends above a bluff (land about W+12 at 10 m inland), so there's no low mouth at the waterline.

| Name (descriptive; no official names in OSM except Tidwell Hollow Rd) | Stations | Centre (XZ) | Water side | Prominence | Land above W at 2 / 10 / 40 m | Type |
|---|---|---|---|---|---|---|
| South McCord hollow (best match for the unresolved "McCord Hollow") | 1543–1551 | (585.4, 456.2) | NE (0.87, 0.49) | 12.9 m | 0.9 / 3.4 / 3.0 | Real mouth, low bank; on the F7 route |
| East hollow below the unnamed faces | 1669–1677 | (249.7, 997.0) | E (0.92, 0.39) | 18.6 m | 0.9 / 8.5 / 27.8 | Real mouth, narrow and steep-sided |
| NW bottom hollow | 52–61 | (−1191, 1819) | SW (−0.79, −0.61) | 19.1 m | 1.0 / 1.7 / 2.0 | Real mouth, wide and low (former fields) |
| W bank hollow | 107–115 | (−1019.2, 1606.0) | SW (−0.79, −0.61) | 17.1 m | 1.1 / 6.9 / 19.9 | Real mouth, short |
| Tidwell Hollow (probable; Tidwell Hollow Rd is 156 m inland) | 1744–1752 | (231.7, 1367.9) | E (0.96, −0.27) | 13.5 m | 1.3 / 11.6 / 38.3 | Hanging, inside bluff face C |
| Buzzard hollow S | 1767–1775 | (268.8, 1476.6) | E/SE (0.93, −0.38) | 14.0 m | 3.6 / 12.4 / 36.1 | Hanging, inside Buzzard Bluff |
| Buzzard hollow N | 1821–1829 | (397.3, 1722.8) | E/SE (0.86, −0.50) | 12.7 m | 3.7 / 11.7 / 43.1 | Hanging, inside Buzzard Bluff |
| NW line hollow (lower confidence, 40 m south of the line) | 16–24 | (−1326.8, 1948.5) | S (−0.38, −0.93) | 11.5 m | 1.0 / 4.2 / 4.3 | Real mouth |
| NE line hollow (lower confidence, 40 m south of the line) | 1872–1880 | (544.3, 1942.3) | SE (0.72, −0.69) | 14.2 m | 2.7 / 8.4 / 20.7 | Part hanging |

Recommendation: count the 4 real mouths (1543, 1669, 52, 107), plus 16 if a north-line anchor is acceptable, as "hollow mouth" structure. Fold the 3 hanging ones into the bluff anchors they sit in.

## 3. Robertson Island is not an island in the game

The map research puts Robertson Island at 36.1726 N, −86.8995 W, which is game (860.5, 163.1). In the built level that point is **land**: W+3.4, zone Bank, 18 m from the waterline. The OSM riverbank polygon includes the island in the playable land. Its back channel shows in the real DEM as hydro-flattened river surface (118.35 m). The terrain build raised it to the land floor (about W+1.4), so it's now a dry, slightly low strip 60–80 m inland. It runs (1087, −58) → (1059, −35) → (955, 12) → (880, 62) → (834, 111) → (784, 162) → (737, 212) → (668, 290) and rejoins the river at about (659, 348), bank st 1522. (Corrected 2026-10-07: an earlier draft put the north mouth at (770, 256), st 1494. A connected-component trace of the chute on the 1 m DEM shows it hugging the bank north of there and reaching the river at st 1518–1526.)

So what the player sees is a low east-bank strip, about 600 m long and up to about 80 m wide. Its river face is the anchor above, and the old channel's two ends are the head and tail "chute mouths". In a real river the island's head and tail eddies and the side channel are prime structure; here only the main-channel face and the two mouths are water. f-director has been asked about the lore (for example, the chute silted in or was filled).

## 4. Shoreline lengths (for "per 100 m of shoreline")

| Measure | Game m |
|---|---|
| Playable inner bank south of the north line (excluding the 19 seam stations) | **9,545** |
| Low bank (`lowBank` flag: pre-shaping bank under the high-bank start, so the cap applied) | **3,589** (37.6%) |
| Bank with pre-shaping height ≤ W+3 / ≤ W+4 / ≤ W+6 | 1,422 / 3,615 / 7,609 |
| Bluff faces in total (the 7 runs above) | 1,346 |
| North of the line (lake edge against the opposite bank, outside the clamp; seen only from the north vista) | about 4,500 (4-neighbour edge count 5,694 m, corrected for staircase overcount; approximate) |

Everything the player can walk and see up close is the 9,545 m south of the line. The north-of-line shoreline is only background.
