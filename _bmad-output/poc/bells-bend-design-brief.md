---
title: 'Design brief: Bells Bend land (zones, roads, walkability, spawn, L2 points)'
owner: systems-designer (pod name "designer")
created: '2026-10-06'
spec: _bmad-output/planning-artifacts/poc-spec-washed-ashore-bells-bend-land.md
greenlight: studio:decisions / greenlight/bells-bend-land (1:2 horizontal, 0.7 vertical, band 0.6-0.85)
inputs: _bmad-output/research/domain-bells-bend-map-2026-10-05/ (adaptation-r1-1, landcover-landmarks-r1-1, elevation-r1-1, shoreline.geojson, roads.geojson)
---

# Design brief: Bells Bend land

**Player problem.** The player washes up in a place that must read as the real Bells Bend 25 years after a 1987 collapse: a forested central ridge, old fields gone to cedar and briar, roads still traceable but swallowed at the edges. Every nook should be worth walking into, and the ground has to stay walkable except where the real land says "cliff".

**Loop it serves.** Explore → spot landmarks and wildlife → traverse varied cover (open gaps, cedar thickets, forest) → find the next thing. The zones below are what make traversal varied; the roads are the readable spine; the spawn is the first view of the ridge.

**Ground rules for everything in this brief.**
- Scripts only. Every rule here is a deterministic, seeded function of data (DEM, polygon, roads, MapConfig). No hand sculpting, no hand placement (automatic FAIL).
- All scale numbers come from `MapConfig`. Nothing in this brief is a hardcoded game coordinate; game positions quoted are for orientation only.
- Player-scale things (road widths, tree sizes, falloff distances, clearing radii) are in **game metres and are not halved** by the 1:2 squeeze.
- **Zones are classified in DEM space** (real elevation, real slope at true vertical), so a vertical-scale retune does not move zone boundaries. Only L4 and the cliff texture use game slope.

---

## (a) L5 zone plan: 25 years after the collapse

### Inputs per land cell (5 m game grid inside the playable polygon, south of the north line)

| Input | Definition |
|---|---|
| `hRel` | DEM elevation − 117.3 m (Cheatham pool), real metres |
| `demSlope` | Slope of the DEM at true vertical, after a 30 m (real) box blur of the DEM, degrees. Blur is for classification only, never written to the heightmap |
| `gameSlope` | Terrain steepness in the built game terrain, degrees |
| `dRoad` | Distance (game m) to the nearest painted road centreline (section b) |
| `dShore` | Distance (game m) to the playable polygon edge (excluding the straight north-line edge) |
| OSM landcover | **Not used.** The landcover digest found it too sparse (38 wood, 19 grass, 6 farmland polygons) to drive proportions |

### Zones — rev 2, shared by qa-2, artist and level-2. Precedence (rev 5): Z0 > Z6 > Z1 > Z2 > Z4a > Z4b > Z5; Z3 is an overlay

| # | Zone (qa name) | Rule | Cover (25-year state) |
|---|---|---|---|
| Z0 | Road | Inside the levelled road cross-section (core + verges, section b) | Road texture; no trees; crack-grass detail only |
| Z1 | Road buffer (kudzu-like) | `dRoad` ≤ **20 m** from the centreline, not Z0 | Dense broadleaf vine ground cover blanketing the verge; smothered/dead tree shapes; trees mainly at the outer edge |
| Z2 | Bank buffer (riparian scrub) | `dShore` ≤ **30 m** and `hRel` < 15 m (the hRel test keeps bluff tops out) | Shrub thicket (honeysuckle/privet-like) plus broadleaf trees; no pine |
| Z3 | Cliff overlay | `gameSlope` > 40° | Rock texture; trees only where `gameSlope` ≤ 45°. Overlay only: the cell is still scored in its Z4/Z5 zone |
| Z4a | Ridge forest | `hRel` ≥ 60 m | Closed hardwood forest with some pine |
| Z4b | Hollows forest | `demSlope` ≥ 8° and `hRel` < 60 m | Same as ridge; bush-honeysuckle understorey at edges |
| Z5 | Former fields | Everything else (`demSlope` < 8°, `hRel` < 60 m): west bottom strip, southern bowl/park, flat bottoms. **Rule-based, not OSM farmland polygons** | **Patchy cedar woodland** (pine as cedar stand-in, 5.5-8 m tall) in clumps, blackberry/sumac/broomsedge in the gaps. Not lawn |
| Z6 | Kept clearings | 40 m radius round the spawn (section d), 20 m round each L7 marker except the bluffs | Broomsedge meadow with briar edge, no trees. Excluded from every open-share metric |
| ZN | North vista (B1, unreachable) | North of the line, same Z4/Z5 rules | Mostly forest (satellite: continuous forest north of TN-12) |

Smoothing of the zone mask: one 3×3 majority filter on the 5 m grid, over Z1-Z5 cells only (it never creates or removes Z0/Z6 cells), nothing else.

Tunable classification parameters (designer-owned, in `Assets/World/BellsBend/ZoneConfig.asset` with [Range] attributes, created by level-2; not in code): `forestSlopeDeg` **6** (allowed 6-10; rev 15 pick from the rc4 sweep: ridge 16.8 / hollows 27.9 / fields 47.3%), `ridgeHRel` 60 m (50-70), `bankBuffer` 30 m, `bankHRel` 15 m, `roadBuffer` 20 m, `clearingSpawnRadius` 40 m, `clearingMarkerRadius` 20 m.

### Species mix (MegaKit prototypes from `WorldBuilder.TreeNames`)

| Zone | Mix by count | Scale notes |
|---|---|---|
| Z4a/Z4b forest | CommonTree_1/_3 70%, Pine_1/_3 15%, TwistedTree_1 7%, DeadTree_1 8% | Mature canopy |
| Z5 former fields | Pine_1/_3 80% (cedar stand-in), CommonTree 10%, DeadTree_1 10% | Pine scaled to **5.5-8 m** instance height (artist calibrates against the prefab bounds) |
| Z2 bank | CommonTree 70%, TwistedTree_1 30% | — |
| Z1 road buffer | DeadTree_1 50%, TwistedTree_1 50% | Read as smothered |

Z5 clumping: seeded Perlin mask, about 1 clump per 40-60 m, so it reads as patches of dense cedar with briar gaps, not an orchard.

### Coverage targets (MEASURABLE, the single definition qa-2 and artist both use)

Grid: 5 m cells on land inside the polygon, south of the line. Hectares are game hectares.
- **Open cell** = no tree instance within **8 m** of the cell centre (qa-2's definition, adopted).
- **Shrub-covered cell** = at least one shrub/briar/kudzu detail instance in the cell (detail layers the artist lists in the report).
- **Open-and-bare cell** = open AND not shrub-covered. This is the "lawn" the spec forbids.

| Zone | Share of land (sanity band) | Trees/ha target / floor / ceiling | Open share | Open-and-bare max | Shrub / kudzu cover |
|---|---|---|---|---|---|
| Z4a ridge | 15-35% | 150 / 100 / 250 | ≤ 10% | ≤ 5% | report |
| Z4b hollows | 25-45% | 150 / 100 / 250 | ≤ 10% | ≤ 5% | report |
| Z5 former fields | 20-50% (rev 15: widened, as no in-range parameters reach 40%; the bend is mostly floodplain bottoms) | 70 / 40 / 110 | **15-50%** (patchy: gaps must exist for habitat, never > 50%) | ≤ 20% | Shrub/briar in ≥ 60% of open cells |
| Z2 bank | 3-12% | 100 / 60 / 160 | ≤ 30% | ≤ 10% | Shrub in ≥ 50% of cells |
| Z1 road buffer | 3-8% | 10 / 0 / 40 | report | ≤ 15% | **Kudzu-like layer weight ≥ 0.5 or kudzu detail in ≥ 60% of cells** |
| Z0 road | ≤ 3% | 0 | — | — | Road splat ≥ 0.6 on ≥ 90% of cells |
| Z3 cliff | report | — | — | — | Rock splat ≥ 0.5 on ≥ 80% of cells |
| ZN vista | report | ≥ 60 | report | — | Reads as more country in the B1 capture |
| **Whole land** | 100% | report | ≤ 35% | **≤ 12%** | Total tree instances reported for R2 |

qa-2's draft "fields ≤ 70% open" is superseded by "15-50% open" above. Zone-share bands are classifier sanity checks (a miss means I retune parameters within their ranges, not a fail). Density floors, open-share maxima and open-and-bare maxima are pass criteria.

**Performance relief order (R2):** if the Windows build misses 60 FPS, technical-artist may lower densities toward the floors (forest first, then riparian), shorten tree/detail distances and billboard distance. Going below a floor needs a message to designer first.

**Habitat contract (R1).** Wildlife and bird placement read splat layer 0 as grass (`deerMinGrass`, birds `MinGrass = 0.6`) and need slope ≤ 15-20° with trees 6-30 m away. Keep **splat layer 0 = grass/broomsedge**. Z5 gaps and Z6 clearings are where those habitats will land; if regeneration cannot meet sighting targets, tell designer before raising open share.

### Report format (build script output, one JSON)

Level-designer's build writes a zone report (path of their choosing under `Logs/` or `_bmad-output/poc/`), with per zone: `cells`, `areaHa`, `pctLand`, `trees`, `treesPerHa`, `openPct`, `openBarePct`, `shrubPct`, `dominantSplatShare`, the shrub/kudzu detail layer names, plus whole-land totals, the classification parameter values and the seed. qa compares it with the table above.

---

## (b) Road treatment (L6)

Paint along the **scaled GeoJSON centrelines with no snapping** (L6 tolerance ≤ 10 m). Widths are game metres, not halved.

| Road | OSM | Treatment | Core (carriageway) | Verge/shoulder each side | Falloff each side | Notes |
|---|---|---|---|---|---|---|
| Old Hickory Blvd | tertiary/residential, 10.1 km in bend + north segment | **Cracked asphalt**, narrowed by verges; grass and saplings in cracks | 5.5 m | 1.5 m cracked edge | 8 m | The spine. Paint through the north line to the edge of the vista terrain (the gate sits on it) |
| Pecan Valley Rd | residential, `surface=asphalt`, north of the line | **Cracked asphalt** | 5.0 m | 1.0 m | 6 m | Vista only (unreachable). Paint only where it falls on built terrain |
| Tidwell Hollow Rd | residential, no surface tag | **Overgrown gravel** (hollow lane) | 3.5 m | 1.0 m | 6 m | Heavier crack-grass/detail on the surface than asphalt |
| Cleeces Ferry Rd (OSM "Cleeces Rerry Road", 2 ways, 36.1662-36.1809 N at about -86.931) | residential 1.7 km, no track segment | **Cracked asphalt**, heavily overgrown | 4.5 m | 1.0 m | 6 m | Crack-grass detail density 2× OHB |
| "Cleeces Ferry Road" track (north) | track 0.21 km at 36.2044-36.2062 N, -86.928 (by Potato Hill, straddles the north line) | **Overgrown gravel** | 3.0 m | 0.5 m | 4 m | Not the ferry approach. Paint wherever it falls on built terrain; the part north of the line is vista. The Cleeces Ferry landing marker sits at the **bank end of "Cleeces Rerry Road"**, not here |

Other OSM service roads and tracks: not painted in this POC.

**Levelling rule (scripted).**
1. Road height along the centreline = game terrain height sampled on the centreline, smoothed with a 30 m moving average.
2. The cross-section is flat across core + verges; the falloff blends to the untouched terrain with a smoothstep over the falloff width.
3. Cut/fill limit: ±3 m from the original terrain. Where the limit would be exceeded, keep the terrain-following grade instead and log the segment.
4. Never level below `WaterLevelY + minLandAboveWater` (1.5 m). Never level within 30 m of the Buzzard Bluff or McCord Bluff markers.
5. Levelling runs after the vertical scale is applied, so it re-runs on every retune.

---

## (c) Walkability tuning rule (L4)

**Measure:** game slope on every heightmap sample inside the playable polygon and south of the north line. Exclude the outside-polygon shore drop (L3). Report a histogram in 5° bins (0-5 … 35-40, 40-45, >45), the % ≤ 40°, and the vertical scale used.

**Pass:** ≥ 90% of land ≤ 40° **and** both bluffs still present: within 60 m of each of the Buzzard Bluff and McCord Bluff markers, at least **200 m² of surface > 40°** (report the max slope and area per bluff).

**Expected numbers.** A game slope of 40° corresponds to a true slope of atan(tan 40° / (2 × v)):

| Vertical v | Net slope factor | True slope that becomes 40° |
|---|---|---|
| 0.60 | 1.2× | 35.0° |
| 0.65 | 1.3× | 32.8° |
| 0.70 | 1.4× | 30.9° |
| 0.75 | 1.5× | 29.2° |
| 0.80 | 1.6× | 27.7° |
| 0.85 | 1.7× | 26.3° |

**Step order.**
1. Build at **0.70**. Measure.
2. If L4 fails, first rule out artifacts: the land mask must exclude shoreline falloff cells, and the DEM must be area-averaged to the heightmap spacing (a uniform resample, applied everywhere). No other smoothing.
3. Still failing: step **0.65**, then **0.60**. Full pipeline rebuild each step (heights, roads, zones, L2 table). Stop at the first pass. **0.60 has zero floodplain margin** (west_bottom lands exactly on the floor), so 0.60 is only accepted if the floor clamp raises ≤ 5% of land cells; otherwise escalate instead of using it.
4. If L4 passes but a bluff falls below the bluff check: step **up** 0.75 → 0.80 → 0.85 until the bluff check passes while L4 still passes.
5. If no value in 0.60-0.85 passes both: **stop and escalate** to team-lead for the game-director. Do not go outside the band and do not smooth.
6. Report the final value, its histogram and the clamped-cell count to designer-2, qa-2 and producer-2.

**Must not be smoothed, ever:** Buzzard Bluff, McCord Bluff, and the hollow walls (Z4b cells with `demSlope` ≥ 20°). No targeted smoothing, terracing or slope clamping anywhere in the land. The only height edits allowed are the uniform resample, road levelling (b), the floodplain floor below, the shoreline bank rule below (the only in-polygon lowering, limited to a 12 m band at low banks), the L3 shore falloff outside the polygon, and the B3 no-holes rule.

**Floodplain floor (condition 3).** Land inside the polygon is clamped *up* to `WaterLevelY + minLandAboveWater` (1.5 m, in MapConfig), except inside the shoreline bank band below. Floodplain bottoms are never lowered toward the water. Note the interaction: west_bottom (119.8 m) sits at (119.8 − 117.3) × v = 1.5 m at v = 0.6, so 0.60 leaves no margin there; below 0.7 more bottom cells get clamped, and the build reports the count and % of clamped cells (≤ 5% allowed, see step 3).

**Measuring condition 3 (qa-2):** check in-polygon samples more than **20 m inland** of the shoreline (bank slope excluded): all must be ≥ `WaterLevelY + 1.5`. Report the minimum and its position, plus west_bottom and park_floodplain_W.

**Shoreline bank rule (rev 3, water-ready banks).** Without this rule, the floor (W+1.5) next to the first water cell (W−1) leaves a step of at least 2.5 m at every shoreline, about 68°, which nobody can climb out of. The rule builds one continuous bank profile across the polygon edge. `s` is the distance from the polygon edge (shoreline segments only, not the north line), measured inland (+) or outward (−). W = `WaterLevelY`.
1. **Lip:** the bank meets the water at `W + bankLipAboveWater` (0.3 m).
2. **Inland cap**, for 0 ≤ s ≤ `bankBandMeters` (12 m): `cap = W + 0.3 + s · tan(bankSlopeDeg)`, with `bankSlopeDeg` 20 (allowed 15-30). A cell above the cap is lowered to it. Inside the band, the 1.5 m floor yields to the cap wherever the cap is lower (the first ~3.3 m).
3. **High banks keep their cliffs.** Blend the cap out by the cell's pre-bank height h: full cap when h ≤ W + 4 m, no cap when h ≥ W + 6 m, smoothstep between. Bluffs and tall cut banks stay unclimbable faces (correct for Buzzard/McCord). Low floodplain banks become walkable.
4. **Underwater shelf**, outside the polygon: the bed follows `W + 0.3 − |s| · tan(bankSlopeDeg)` down to wading depth `W − 2`, then smoothsteps to `W − lakeDepthBelowWater` (10) by `shoreRampMeters`. Raise shoreRampMeters to **20**. The old "first water cell at W − 1" step goes away.
5. **Game-director ruling `ruling/bells-bend-shoreline-bank`: complies with condition 3.** Limit: `bankBandMeters` stays a MapConfig parameter. Widening it past 12 m, or letting the cap reach the bluff faces, goes back to the director. Nothing beyond 12 m inland is touched. park_floodplain_W (17.6 m inland) and every L2 point are outside the band. Condition 3 is unaffected: its check already excludes the 20 m inland band, and it protects the flat bottoms, not the bank lip.
6. Parameters live in MapConfig (tools): `bankLipAboveWater` 0.3, `bankSlopeDeg` 20, `bankBandMeters` 12 (max 15, so it stays inside qa-2's 20 m band), `bankHighStart` 4, `bankHighEnd` 6, `wadeDepth` 2, `shoreRampMeters` 20.

**Bluff exclusion (rev 6, safe reading of the director's limit).** The bank rule does not touch any cell within `bankBluffExclusionMeters` (60 m, game) of the Buzzard Bluff or McCord Bluff marker. Its weight fades from 0 at 60 m to 1 at 80 m (`bankBluffFadeMeters` 20), so there's no seam at the boundary. Shoreline stations inside 60 m are excluded from the low-bank ≤ 35° check and flagged "excluded: bluff". The bluffs meet the water as cliffs, which is correct. Test: zero bank-lowered cells within 60 m of either marker. **Steep-face exemption (rev 7, BB-QA-3):** the cap also skips any cell whose pre-bank game slope is > 40°. Its weight is multiplied by 1 − smoothstep(35°, 40°, preSlope), so tall cut banks keep their faces everywhere, not just at the bluffs. Test: zero lowered cells with pre-bank slope > 40° land-wide.
**Face definition (rev 10): APPROVED by the game-director as option B (`ruling/bells-bend-bank-faces`).** The ruling requires zero lowered cells on qa-2's flagged cliff toes (BB-QA-3 east cliff and west-neck faces), zero on banks whose ground rises more than 6 m above W within 12 m, and zero in the 60 m bluff zones. Low banks must be 100% ≤ 35°. The values 6 m, 60 m and 18° stay as MapConfig parameters; raising 6 m or shrinking 60 m goes back to the director. Built default: `bankSteepExemptHighBanksOnly` = 1. The strict exemption (every pre-bank cell > 40°) cannot coexist with 100% climbable low banks: 98 short cut banks fail at any bank slope. So a **protected face** is defined as either:
- pre-bank slope > 40° on a bank whose ground within 12 m inland rises above W + `bankHighEnd` (6 m game, about 8.6 m real above the pool); or
- any cell within 60 m of a bluff marker.

Short cut banks (topping out ≤ W + 6 within 12 m) are banks, not faces, and get the walkable slope. `bankSlopeDeg` is 18 (stepped from 20). Measured at 0.70:
- 0 lowered cells on protected faces, as tools claims. verified by qa-2 (0 on the toes; the matching count was a coincidence);
- 732 low-bank stations, 100% ≤ 35° (max 34.9°);
- 69.3% of all stations climbable;
- 4,906 lowered cells on short cut banks;
- bluffs 4,323 / 6,513 m² > 40° within 60 m.

Pass lines (qa-2): (1) 0 lowered cells on the flagged cliff toes; (2) 0 lowered **steep (pre-bank > 40°)** cells on banks rising > 6 m within 12 m. Gentle lip cells below a taller bank may be lowered (rev 12 reading; team-lead confirmed it matches the director's ruling); (3) 0 in the bluff zones; (4) low-bank 100% ≤ 35°. Readings considered: strict (0 lowered > 40° anywhere; 86.6% low-bank) was rejected; option B was approved. The 4,906 lowered short-bank cells (qa-2 counts 4,530) are allowed only if none of them is a flagged toe. **qa-2 confirmed 0 lowered on the toes, the bluff zones and the big faces: compliant.**
**Lip-step exclusion (rev 14):** the low-bank ≤ 35° line excludes the single lip-step segment at s = 0, the 0.31 m riser from W − 0.01 to W + 0.3. It is below the player's CharacterController stepOffset (0.4 m, slopeLimit 45 in World.unity). The exclusion lapses if `bankLipAboveWater` + 0.01 exceeds stepOffset. Lowering the lip deepens the shelf by the west corner. A lip of 0.20 raises the excepted corner step to 3.507 m and breaks its 3.5 m limit, so any lip change must re-check the corner exception.
**Station measure of record:** qa-2's raycast profiles on the Unity terrain at 0.25 m steps. tools' station report switches to the same 0.25 m method. If any low-bank station still exceeds 35°, step `bankSlopeDeg` 20 → 18 → 16 (allowed 15-30) and rerun. Below 15 comes back to designer-2.

**Measured at 0.70 (tools build, before the bluff exclusion; to be re-measured):**
- 73,839 cells lowered, max s 12.0 m.
- 732 low-bank stations, 100% ≤ 35°; all stations 78.7% climbable.
- Outside cells clamped to ≤ W − 0.01 (L3a wins), giving a 0.31 m lip step at the edge.
- Slope window counted by segment midpoint. Station 603's 38.1° is a natural 0.4 m DEM terrace above the window and is left alone.
- 19 stations within 22 m of the north-line corners are excluded and flagged.

**Bank acceptance (qa-2):** sample shoreline stations every 5 m. At each station, take the profile along the inward normal from 10 m outside to 12 m inside, and its max slope between heights W − 2 and W + 1.5.
- **Low-bank stations** (pre-bank max height within 12 m inland ≤ W + 4): **100% must be ≤ 35°**.
- All stations: report the % climbable. That figure is informational for the water pass.
- Also report the count of in-polygon cells lowered by the bank rule. They must all lie within 12 m of the shoreline.

---

## (d) R1 spawn: "south near Bells Bend Park"

| Item | Value |
|---|---|
| Object name | `Spawn_BellsBendPark` |
| Anchor | lat **36.1535, lon -86.9235** (park campus, just SW of the Outdoor Center at 36.1543, -86.9219), projected through MapConfig. Orientation only: about x = -220, z = -895 from the polygon centroid |
| Placement rule | Search outward from the anchor (max 150 m) for the first point that has: height range ≤ 1.5 m and max slope ≤ 10° in a 25 m radius; height ≥ `WaterLevelY` + 3 m; no tree within 20 m; on land, south of the line, not on Z0 road |
| Facing (yaw) | Toward the central ridge: yaw = compass bearing from the spawn to the projected central_ridge_S point (36.1660, -86.9060), computed at build. Expect about **49° (north-east)**. Same direction as the director's Outdoor Center capture |
| Clearing | 40 m Z6 clearing around it (section a) |
| Pass | Spawn on land inside the polygon, within 150 m of the anchor; existing WorldBuilder spawn checks (`treesWithin20mOfSpawn = 0`, `spawnFlat25m`) pass |

---

## (e) L2: the 14 sample points

**Table:** `_bmad-output/research/domain-bells-bend-map-2026-10-05/digests/elevation-r1-1.md`, "Sampled elevations table" (14 labelled EPQS points).

**Expected height (no hardcoding).** The test reads lat/lon and EPQS elevation from that table (or a copy of it as test data, generated from the table, not typed) and computes:

```
expectedY = max( MapConfig.DemToWorldY(demAt(lat, lon)),
                 MapConfig.WaterLevelY + MapConfig.minLandAboveWater )   // floor applies only inside the polygon
DemToWorldY(z) = terrainBaseY + (z - datum) * verticalScale            // the one function the pipeline also uses
```

`demAt` samples the **cached DEM** used by the build (bilinear). The EPQS value from the table is reported alongside; a DEM-vs-EPQS difference over 2 m is flagged as a data note, not an L2 fail. Pass is |actualY − expectedY| ≤ 3 m. The Python pipeline and C# must share one MapConfig source of truth (tools-engineer exports one from the other), and a test asserts they agree.

**Director ruling (studio:decisions `ruling/bells-bend-L2-points`): 10 graded points, do not pad to 14.**

| Point | Lat, lon | EPQS (m) | Where it lands (provisional projection) | Graded as |
|---|---|---|---|---|
| central_ridge_S | 36.1660, -86.9060 | 239.2 | Inside polygon | **L2 ±3 m** |
| central_ridge_mid | 36.1820, -86.9104 | 235.2 | Inside | **L2 ±3 m** |
| central_ridge_N | 36.1940, -86.9220 | 230.6 | Inside | **L2 ±3 m** |
| park_floodplain_W | 36.1578, -86.9389 | 122.3 | Inside (17 m from edge) | **L2 ±3 m** |
| west_bottom | 36.1800, -86.9360 | 119.8 | Inside | **L2 ±3 m** |
| south_fields | 36.1480, -86.9150 | 150.6 | Inside | **L2 ±3 m** |
| south_tip_bottom | 36.1410, -86.9200 | 120.6 | Inside | **L2 ±3 m** |
| neck_north | 36.2100, -86.9150 | 168.9 | North of the line, about 250 m into the vista | **L2 ±3 m** |
| **east_bank_bottom_in** (new) | **36.1710, -86.9030** | **123.9** | Inside, about 190 game m from the edge; ±20 m EPQS range 0.9 m | **L2 ±3 m** |
| **east_lobe_in** (new) | **36.2000, -86.9166** | **147.9** | Inside, about 250 game m from the edge; ±20 m EPQS range 2.6 m | **L2 ±3 m** |
| east_bottom | 36.1800, -86.8970 | 127.1 | Outside the OSM polygon | L3 (≥ 8 m below WaterLevelY) |
| east_lobe_bottom | 36.2000, -86.9000 | 121.8 | Outside | L3 |
| river_W_arm | 36.1700, -86.9430 | 122.2 | Outside | L3 |
| river_S | 36.1370, -86.9250 | 122.6 | Outside | L3 |
| check_E_of_east_arm | 36.1664, -86.8780 | 132.7 | Outside | L3 |
| scottsboro_ridge_N | 36.2561, -86.9006 | 277.0 | About 2.8 km north of the line | N/A; L2 ±3 m only if the built vista reaches it |

**Why the two new points sit where they do.** The director asked for an east inner-bank bottom near 36.18 N and an east-lobe point near 36.20 N, each at least 100 m inside the polygon edge. I scanned EPQS on a grid from 36.170 to 36.190 N at least 100 game m inside the edge. At 36.18 the in-polygon east side is the McCord Bluff ridge (176-197 m, 13-20 m relief within ±20 m), so there is no bottom there. The nearest true east-bank bottom inside the polygon is at **36.171 N** (123-124 m, flat). At 36.20 the OSM polygon's east edge is at about -86.907, so the digest's "east lobe" points fall outside it. east_lobe_in is the easternmost locally flat ground at least 100 m inside (148 m, gentle). Flat sites were chosen so a small projection difference can't move the expected value by metres.

**DEM value.** tools samples both new points from the cached DEM (bilinear) and reports the DEM value next to the EPQS value above. A difference over 2 m is a data note for designer-2, not a fail. The L2 table is emitted by tools (`tools/terrain/l2_sample_points.json` plus its expected-Y output) and graded by qa-2.

---

## (f) North-line opposite banks (bug BB-QA-1): RATIFIED by the game-director (`ruling/bells-bend-north-cut`)

**Problem.** On the opposite banks (west of the west-arm crossing and east of the east-arm crossing), real DEM land north of the line meets the L3 lake south of it along a dead-straight east-west cut, up to 31.5 m high. Seen from the playable land across the east arm, that is the "horizon cliff" greenlight condition 4 rules out.

**Ruling: option (a) plus an irregular shore.** Ratified with qa-2's acceptance checks as written. **Director condition: the 0-60 m offset noise is seeded from `oppositeBankSeed` stored in MapConfig, so a from-scratch rebuild reproduces the identical edge.** Changing the seed is a config change that qa-2 re-grades. Not (b), and not (c) on its own. (b) needs a spec reading; (c) can't work because the B2 barrier runs only across the neck, not along the opposite banks. Applied only to opposite-bank cells north of the line and outside the neck segment between the two river crossings. The neck (B1/B2/B3, neck_north L2) is untouched.
1. **Irregular shoreline.** The lake/land boundary on the opposite banks is moved north of the straight line by a seeded, smooth 1D noise offset of 0-60 m (wavelength 80-200 m). The offset tapers to 0 m within 100 m of each river crossing, so the new shore meets the real river bank without a notch. More land becomes lake; no land is added south of the line, so L3's "no land outside the polygon above water except north of the line" still holds.
2. **Shore slope.** Land north of that boundary gets a height cap of `W + 0.3 + d · tan(oppositeBankSlopeDeg)`, where d is the 2D distance to the nearest lake cell and `oppositeBankSlopeDeg` is 25 (allowed 20-30). The real DEM rises out of the lake as a hillside instead of a cut. Beyond where the cap exceeds the DEM, the real DEM is kept.
3. Underwater, the existing shelf and shoreRampMeters rules apply from this boundary.
3a. **No seams (rev 8/12, BB-QA-4).** (The rev 11 corner fix is dropped in rev 12; the west neck corner falls under the director's corner-step exception.) **Rev 13 (as built in bb-terrain-rc4, accepted):**
- The cap's crossing taper is replaced by a guard: the cap never drops faster than 25° away from the untouched neck columns, so the lip reaches the cap everywhere. The taper had left 2-3.1 m lake-edge steps 10-75 m from the west crossing. The offset taper is unchanged.
- The bank cap fades out within 2 m of the north line, which removes 8 cells at 64° on the neck side.
- rc4: cap-induced = 0 everywhere; steps ≤ 1.47 m except the 4 excepted corner edges; lake hash 8f8a637cd4657d82.
- "Rises above W+6" is evaluated as the max pre-bank height (before the cap and the floor) over a 12 m-radius square window. The cap has no hard distance cutoff. If the distance transform needs a limit, use d_max = (max opposite-bank Y − W) / tan(slope) + 20 m, so the cap is already above all terrain where it ends. At the neck boundary, the cap weight smoothsteps from 0 at each river crossing's x to 1 at `oppositeBankTaper` (100 m) outward, matching the offset taper, so capped and uncapped land meet without a step.
4. The far-bank lake edge and cap are relative to `northLineZ` in MapConfig. **Known limitation (rev 16):** the neck/opposite-bank split and the land mask stay on the data line's river crossings. A terrain rebuild with the line shifted +100 m leaves a lake strip beside the untouched neck columns, with steps up to 22.7 m (tools scratch run). B6's evidence of record is qa-2's full chain on the real MapConfig. That chain **does** rebuild terrain at +100 m, so the neck-side cut will appear in the shifted build. qa-2 grades B6 on the spec's terms (barrier, backstop and clamp move; B5 passes with 0 crossings), **confirms the backstop covers the cut**, and records the cut as a limitation. Accepting it as a follow-up is with the game-director via team-lead. **Before the north line is ever moved for real, the crossings must be re-derived from the shifted bank geometry** (post-POC follow-up). New MapConfig fields (tools): `oppositeBankSlopeDeg` 25, `oppositeBankOffsetMax` 60, `oppositeBankWavelength` 80-200, `oppositeBankTaper` 100, `oppositeBankSeed`.

**Lake-bed bypass (qa-2 note).** With walkable banks, a dry lake bed in the POC, and climbable opposite-bank hillsides, a player could walk round the backstop's ends. engineer-2 extends the WorldBounds backstop across the full terrain width, or proves the ends unreachable; qa-2 probes the lake bed under B3/B5. **No visible B2 dressing on the lake-bed stretch.** It will be under water in the water pass, and the spec's "bank to bank" barrier is satisfied by the neck barrier.

**Acceptance (qa-2, B1):**
- Max step between adjacent heightmap samples across the opposite-bank lake boundary ≤ 2 m. No above-water slope over 35° within 100 m of that boundary, unless the real DEM is already steeper there (report those cells).
- The boundary is not straight: across opposite-bank columns, its z offset from the line has a standard deviation ≥ 10 m, and no run of constant offset exceeds 100 m.
- L3 is unchanged: no land outside the polygon, south of the line, above W.
- B1 (≥ 400 m of real DEM north of the line) is graded on the neck segment, which this rule does not touch.
- Noah's north-facing capture: no straight earth cut visible on either side.
- Max adjacent step across the boundary ≤ 2 m, except where the director's `ruling/bells-bend-corner-step` (rev 12) applies. There the step may be up to 3.5 m, only where all three hold: within 10 m of a river crossing, on out-of-bounds ground, and screened by the barrier. Today that covers only the 4 cell edges at the west neck corner (3.41 m, x −1422…−1427, z 1988-1990, behind level-2's barrier river corner at x ≈ −1419). The corner must appear in level-2's west-corner look shot. **If Noah reads it as a cut, the exception is void and tools reworks it.**
- The corner-step exception covers the 4 step edges plus the cells that form the step itself within 10 m of the crossing; a 3.41 m step can't exist without them (team-lead, same ruling). Any steep cell outside that is a tools fix.
- Cap-induced steepness: zero cells **anywhere in the opposite-bank region** (including the neck transitions and the cap's far edge), whose final slope is > 35° while their pre-cap slope is ≤ 35°.
- Determinism: two from-scratch builds with the same MapConfig give the identical boundary (hash of the lake mask matches).

---

## Acceptance summary for qa-lead

| Check | Pass condition |
|---|---|
| L5 zones | Zone report JSON exists; density floors/ceilings, open share, open-and-bare maxima and shrub/kudzu cover in section (a) met; whole land open-and-bare ≤ 12% |
| L6 roads | Four roads painted per table (b); overlay ≤ 10 m; levelling rule obeyed (log of cut/fill-limited segments); no levelling below the floodplain floor |
| L4 | ≥ 90% ≤ 40°, bluff check passes, final vertical in 0.60-0.85, histogram reported |
| Floodplain | No in-polygon sample > 20 m inland below `WaterLevelY + 1.5`; clamped cells ≤ 5% and reported |
| Banks | Low-bank shoreline stations (bluff zone excluded) 100% ≤ 35° between W−2 and W+1.5; bank-lowered cells all within 12 m of shore and **none on a protected face (rev 9 definition)**; % climbable reported |
| L3 | No outside cell above WaterLevelY; every outside cell farther than `shoreRampMeters` (≤ 150) from land at or below WaterLevelY − 8 |
| R1 spawn | Section (d) rule met |
| L2 | Section (e), director ruling: 10 points within ±3 m of MapConfig-computed values (8 original + 2 new east points); 5 points graded under L3; scottsboro_ridge_N N/A unless on built terrain |
| B5 inputs | Sweep uses the live `PlayerController` serialized values (today walk 5, sprint 8 m/s, jump 1.2 m), read at test time, never assumed |
| B2 placement | The visible barrier is set 20 m south of the line (code constant `BarrierBuilder.LineOffset` = 20, applied as northLineZ − 20, so B6 still moves it; moving it into MapConfig is a post-POC follow-up), continuous bank to bank, including the river-corner turns. The gate is where OHB crosses it, and the backstop sits on the line. The 20 m strip is unreachable by design. Noah's north shot is taken from just south of the fence by the gate. Fence bay, gate-leaf and gate-post colliders extend invisibly, on layer 0, to top = max(own ground + 4.0, highest **playable (south-side)** ground within a 12 m plan radius + 3.6). A sprint-jump (8 m/s, 1.2 m) launched from further out reaches the fence below its launch height. Fence of record (rev 18, team-lead): scene e8e3c28ed8d3a783, min clearance 3.52 m, tallest invisible extension 16.0 m, 51 of 796 bays over 10 m. B5: one rerun on this scene. Post-POC: put the invisible part on a layer that robin flight casts ignore |
| B5 starts | Start 45 m south of the line (the fence is at −20 m), with ≥ 20 m run-up to the first face. A non-walkable start slides along the approach ray to the first walkable point. If none exists, the run is first replaced from the nearest valid start within 25 m. At most 15 of 300 runs (5%) may stay SKIPPED, and at most 3 per approach type. **A skipped 0° approach means insufficient coverage, unless qa-2's terrain-protected rule applies:** slope > 45° across the whole ±25 m window (qa-2's profile), the backstop solid there (B3 probe), no route around, at most 1 per 0° case and 2 overall. Those are reported as "terrain-protected, untestable on foot". Skips are NUnit warnings, never passes; crossings fail outright. (qa-2's rule governs.) |
| B1 opposite banks | Section (f): boundary step ≤ 2 m, slope ≤ 35° within 100 m (or the DEM already steeper), offset std ≥ 10 m, no straight run > 100 m |
| R1 evidence | Wildlife/bird PlayMode gating runs use `Time.captureFramerate = 60`, set in the test fixtures only, never in game code or ProjectSettings. Deer `angularSpeed` is 720 (was 600) for facing margin. A5a must pass 5/5 consecutive (engineer-2) and 3/3 (qa-2, session 3). An informational run at 30 fps is reported as a low-frame-rate risk only |
| Scripts only | Every number above regenerates from one command; no manual edits |
