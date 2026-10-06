# tools/terrain: Bells Bend DEM pipeline

One command rebuilds everything from scratch (Python 3.13 + numpy only):

```
/c/Python313/python.exe tools/terrain/build_terrain.py            # Git Bash, from the repo root
/c/Python313/python.exe tools/terrain/build_terrain.py --refetch  # redownload the DEM
py tools/terrain/build_terrain.py                                  # PowerShell (py launcher)
```

About 4 min with an empty cache (9 x 67 MB downloads), about 15 s from cache.

## Inputs

- `Assets/World/MapConfig.asset`: the single source of truth for scale, datum, water level,
  north line and tile layout. The script parses the asset YAML; edit it in the Inspector and rerun.
- `_bmad-output/research/domain-bells-bend-map-2026-10-05/digests/shoreline.geojson`
  (playable polygon, inner bank) and `roads.geojson` (OSM, ODbL).
- `landmarks.json` (L7 markers) and `l2_sample_points.json` (L2 EPQS points), both in this folder.
- USGS 3DEP ImageServer `exportImage`, 1 m, EPSG:26916 (NAD83 / UTM 16N), fetched in
  4096 m chunks on a fixed lattice and cached in `Data/terrain/cache/` (git-ignored).

## What it does

1. Game frame: origin at the playable polygon's area centroid (rounded to a whole UTM metre),
   `x = (E - Ec) * horizontalScale`, `z = (N - Nc) * horizontalScale`, +X east, +Z north.
2. Grid: tiles of `tileSizeMeters` covering the polygon plus `marginMeters` on every side and at
   least `northVistaMinMeters` beyond the north line. One sample per game metre at the defaults.
3. Resample: [1,2,1] low-pass on the 1 m DEM, then bilinear at each game sample.
4. Heights: `Y = (DEM - datumMeters) * verticalScale`, `WaterLevelY = (poolElevationMeters - datumMeters) * verticalScale`.
   - Inside the polygon: `Y >= WaterLevelY + landMinAboveWater`, except in the shoreline bank band
     (design brief rev 3 (c)): within `bankBandMeters` of the bank, low banks are capped at
     `W + bankLipAboveWater + s * tan(bankSlopeDeg)`, blended out between `bankHighStart` and
     `bankHighEnd` of pre-bank height so bluffs keep their faces.
   - Outside the polygon, south of the line: lake. The bed continues the bank slope down to
     `W - wadeDepth`, then smoothsteps to `W - lakeDepthBelowWater` by `shoreRampMeters`, and is
     never above `W - 0.01`.
   - North of the line, outside the neck (design brief (f)): the lake edge is pushed north by a
     seeded smooth offset (`oppositeBank*`), and opposite-bank land is capped at
     `W + bankLipAboveWater + d * tan(oppositeBankSlopeDeg)` from the nearest lake cell.
   - North of the line: real DEM. River cells (DEM below pool + 1.5 m, the hydro-flattened
     surface) are carved to the same lake profile.
5. Output in `Data/terrain/build/`:
   - `tile_x{i}_z{j}.raw`: uint16 little-endian, `heightmapResolution`^2, row 0 = south (min Z),
     column 0 = west. Load with `TerrainData.SetHeights(0, 0, h[row, col] / 65535f)`.
     Every tile shares `terrainBaseY` and `terrainHeight`, so edges match.
   - `manifest.json`: tile grid, positions, base and height, config snapshot, DEM provenance.
   - `map_vectors.json`: playable polygon, inner bank, north line, roads (with a `required` flag for
     the four L6 roads) and L7 landmarks, all in game XZ.
   - `l2_samples.json`: the 14 L2 points with game XZ, region and `demMeters`. Compute
     `expectedY = MapConfig.DemToUnityY(demMeters)` at test time; `previewExpectedY` is only a snapshot.
   - `report.json`: L1 shoreline deviation, L3 outside scan, L4 slope histogram preview, bank
     acceptance, condition 3 (floor >20 m inland), floor clamp count. `bank_stations.json`: per-station table.
   - `overlay.png`: top-down preview with the OSM polygon (red) and `northLineZ` (yellow).

The terrain mask uses the data north line (36.2055 N). Moving `MapConfig.northLineZ` (B6) moves
the barrier, backstop and clamp and extends the north margin; it doesn't reshape the land.
