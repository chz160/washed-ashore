---
title: 'POC Spec: Bells Bend land + north boundary'
status: ready-for-pod
owner: John (PM)
created: '2026-10-05'
inputs:
  - _bmad-output/research/domain-bells-bend-map-2026-10-05/research.md   # + digests/, shoreline.geojson, roads.geojson
  - _bmad-output/research/domain-bells-bend-map-2026-10-05/noah-boundary-sketch.png
---

# POC Spec: Bells Bend land and north boundary

**The question this answers:** can Ruflo's agents replace the 512 m test terrain with the real shape and relief of **Bells Bend**, aged 25 years after a 1987 collapse, and stop players going north past Noah's red line, all through scripts that can be run again?

**Water is out of scope**, but the terrain has to be ready for it (see L3).

## Fixed inputs (decided)

| Item | Value |
|---|---|
| Project | `E:\GitHub\washed-ashore` (replaces the 512 m `WorldBuilder` terrain) |
| Playable land | The OSM inner-bank polygon `bells_bend_playable_polygon` in `shoreline.geojson`, south of the **north line at 36.2055 N** |
| **Horizontal scale** | **1:2.** About 3.85 km N–S × 2.75 km E–W of land. The Unity origin is at the polygon's centroid, and 1 Unity unit is 1 m |
| **Vertical scale** | A parameter, **default 0.7**. A 1:2 horizontal squeeze doubles slopes, so true vertical would push about 10% of the land past 40° |
| Elevation source | USGS 3DEP ImageServer `exportImage` (public domain), requested in UTM 16N so the pixels are square in metres. Fetch about 1 m data, cache the raw GeoTIFF under `Data/terrain/`, and resample for the game |
| Pipeline | `tools/terrain/` (Python + numpy only; no GDAL is installed): fetch, clip, compress, write 16-bit RAW. Then `Assets/Editor/Level/` builds the tiles. **Everything runs again from scratch with one command each** |
| Terrain layout | A grid of Terrain tiles with heightmaps of 2^n+1 resolution, sharing a Grouping ID with Auto Connect on, and covering the land plus at least a **600 m margin** on every side (see L3) |
| Data, not code | `MapConfig` ScriptableObject: scale factors, datum offset, **`WaterLevelY`** (the 117.3 m pool mapped to Unity Y), the north line in Unity Z, and the gate open/closed state |
| Credits | `CREDITS.md` gets "Elevation: USGS 3DEP (public domain)" and **"© OpenStreetMap contributors (ODbL)"** |

## Pass criteria: the land

| # | Criterion | Evidence qa-lead checks |
|---|---|---|
| L1 | **It reads as Bells Bend.** A top-down orthographic capture of the built terrain, overlaid on the scaled OSM polygon, shows a mean shoreline deviation of **≤ 15 m** (in game metres) and a matching outline: the pear shape, the waist and the east lobe | Overlay PNG plus a deviation number from the build script. Noah does a side-by-side check with the satellite reference |
| L2 | **Real relief.** The central N–S ridge, the hollows and the flat bottoms are where USGS puts them. At the 14 labelled sample points in `elevation-r1-1.md`, the in-game heights are within **±3 m** of (DEM − datum) × the vertical scale | An `eval` table of sample point, expected value and actual value |
| L3 | **Ready for water.** Every terrain point outside the playable polygon falls to **at least 8 m below `WaterLevelY`** within 150 m of the shore, including the opposite banks the sketch turns to water. No land outside the polygon rises above `WaterLevelY`, except north of the line (see B1) | A height scan of the outside region: maximum height below the threshold |
| L4 | **It's walkable.** At least **90%** of the land surface is ≤ 40°, and the steep hollow walls and the bluffs (Buzzard Bluff, McCord Bluff) are still there | Slope histogram from the build script |
| L5 | **25 years after the collapse** (the texture layers and plants follow `adaptation-r1-1.md`): forest on the ridge and hollows; **former fields as patchy cedar-like woodland and shrub**, not open lawn; kudzu-like overgrowth along roadsides; riparian scrub along the bank. Texture layers come from ambientCG and plants from the MegaKit (pine as a stand-in for cedar) | `eval` coverage by zone. Noah's look check |
| L6 | **Roads.** Old Hickory Blvd, Pecan Valley Rd, Tidwell Hollow Rd and Cleeces Ferry Rd are painted from `roads.geojson` (scaled) as cracked asphalt or overgrown gravel, and the ground is levelled along them | Overlay check against the GeoJSON (≤ 10 m deviation) |
| L7 | **Landmark markers.** Empty, named marker objects (no buildings yet) at the scaled positions of: the Outdoor Center, the Buchanan House, the boat slipway, the Cleeces Ferry landing, the viewpoint, the wastewater plant, Buzzard Bluff, McCord Bluff, Potato Hill and the west-bank ponds | A marker list with coordinates |

## Pass criteria: the north boundary

| # | Criterion | Evidence |
|---|---|---|
| B1 | **The world doesn't just end.** The terrain continues **at least 400 m north** of the line, using real DEM data, so the view north looks like more country. That ground stays unreachable | Height scan; Noah looks north from the line |
| B2 | **A barrier you can see** runs along the full line, bank to bank. It's a grounded, post-1987 barricade: fence, rubble, wrecked vehicles, a washed-out cut. It includes **one closed gate** (a checkpoint where Old Hickory Blvd crosses) whose open state comes from `MapConfig.gateOpen` for future updates. Blockout or CC0 kit pieces only (the Downtown City and Survival packs in `vendor/`) | Screenshot; the gate prefab and its config flag |
| B3 | **A backstop you can't get past:** a continuous hidden collider wall at least 30 m tall and 2 m thick on a `WorldBounds` layer, running into the water margin on both ends. **No terrain holes within 50 m** | EditMode raycast scan every 1 m along the line: no gaps |
| B4 | **An authoritative clamp:** a `WorldBoundsClamp` component written so it can later run server-side. It snaps any player or `Rigidbody` that ends up north of the line back south of it and logs the event | PlayMode test: teleport the player 10 m north and check it's snapped back within 1 frame |
| B5 | **The sweep test:** at **50 points** along the line, the player walks, sprints, jumps and approaches at 30° and 60° angles, and gets one high-speed `Move` (50 m/s). **Zero crossings** | PlayMode `[UnityTest]` results, 0 failures |
| B6 | **Moving the line moves everything.** Shifting the line in `MapConfig` by +100 m and rebuilding moves the barrier, the backstop and the clamp, and B5 still passes | Test run against the shifted config |

## Regression and budget

- **R1:** The player spawns on land, in the south near Bells Bend Park. WorldWalkTests and the wildlife and bird tests pass, with the wildlife and bird habitats **regenerated on the new terrain** and the sighting targets still met.
- **R2:** The Windows build exits 0 and runs at **≥ 60 FPS** at 1080p on Noah's machine while walking from the park to the ridge.
- **R3:** The web build still meets **W4 (≤ 50 MB)** and **W7 (≥ 30 FPS)**. If it doesn't, cut tree and detail density and terrain resolution first, and report the numbers.

## Automatic FAIL

- Sculpting the terrain or placing things by hand outside the scripts.
- A barrier that's only an invisible wall, with no visible element.
- Any player crossing in B5 or B6.
- The same blocker 3 times in a row.
- More than 3 assists.

## What the pod hands back

1. A verdict from qa-lead (**PASS**, **PASS WITH ASSISTS (n)** or **FAIL**) with evidence for L1–L7, B1–B6 and R1–R3, including the overlay PNG and the slope histogram.
2. Friction-log rows. Flag the DEM fetch and resample in particular.
3. A technical-director note on tile size, streaming, and readiness for the water pass.

## Pod

Run `/studio-pod` from `C:\Tools\ruflo\studio`, with the project path set to `E:\GitHub\washed-ashore`. **The game-director agent signs off the 1:2 and 0.7 scale before the build starts.**

| Role | Owns |
|---|---|
| platform-tools-engineer | `tools/terrain/` DEM pipeline, `MapConfig`, caching (L1–L3) |
| level-designer | Tile build, zones, roads, landmark markers, barrier layout, north vista (L1–L7, B1, B2) |
| technical-artist | Texture layers and overgrowth, the post-collapse look, frame-rate and size budgets (L5, R2, R3) |
| gameplay-engineer | Backstop, clamp, sweep and config-shift tests, spawn (B3–B6, R1) |
| qa-lead | Verdict, overlay and look checks with Noah |
| executive-producer | Schedule, assist count, final report |
