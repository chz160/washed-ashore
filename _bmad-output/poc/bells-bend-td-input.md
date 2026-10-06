# Bells Bend land: technical-director input

## tools

Measured on 2026-10-06 with `/c/Python313/python.exe tools/terrain/build_terrain.py` (Git Bash, repo root; `py` only works in PowerShell) at the default MapConfig (1:2 horizontal, vertical 0.7, datum 115 m).

**Tile size and heightmap resolution.** 4 x 5 tiles of 1024 m, heightmap resolution 1025, which gives one sample per game metre (2 m of real ground). The footprint is the polygon (2.75 x 3.85 km) plus a 600 m margin, rounded up to whole tiles: 4096 x 5120 m. 1 m spacing keeps the 1:2-squeezed hollow walls and bluffs (72–77 degrees) without stair-stepping, and the build-side shoreline deviation is 0.33 m. The 16-bit range is 103.2 m (Y -9.4 to 93.8; built max 92.7), so the vertical step is 1.6 mm. Halving to 513 per tile (2 m spacing) is a single MapConfig change (`heightmapResolution`) if R2 frame time needs it; I haven't measured it.

**Cache and rerun cost.** The DEM is 9 chunks of 4096 x 4096 px (1 m, F32, 67 MB each, 604 MB total) cached in `Data/terrain/cache/`, which is git-ignored. A cold run took 235 s and 2 transient HTTP 502s, retried automatically. A cached run takes about 28 s. The chunks sit on a fixed UTM lattice, so a changed footprint (margin, north line +100 m) reuses them. Outputs total about 84 MB: 20 height RAWs and 20 unshaped-DEM RAWs at 2.1 MB each, routed to Git LFS via `.gitattributes`.

**Streaming.** 20 tiles at 1025 is about 21M height samples. Land share per tile (above WaterLevelY): 5 tiles are 0% (x0_z0..x0_z3 and x3_z3), and 3 more are under 10% (x3_z0 1.7%, x1_z0 6.5%, x3_z2 9.4%). The 4 north-row tiles (z4) are mostly vista land. Options, in order of cost:
1. Ship as is, with the far tiles at a higher pixel error and `drawInstanced` on.
2. Rebuild the all-water tiles (x0_z0..z3, x3_z3) at 257 or 129 resolution. They are flat lake bed, so nothing is lost. This needs a per-tile resolution override in the pipeline.
3. Addressables or scene-per-tile streaming. Not needed at 4 x 5 km on Windows.

A matching-edge rule applies to option 2: neighbouring tiles at different resolutions need edge stitching, or the lake tiles must be fully under water so seams are hidden.

**Datum and water for the water pass.** `MapConfig` is the only carrier: `WaterLevelY => (poolElevationMeters - datumMeters) * verticalScale` = 1.61, and `DemToUnityY(m)` maps any real elevation. Pipeline guarantees:
- Inside the polygon, land is at or above WaterLevelY + 1.5. 2.84% of land cells were clamped up; there's no other inside edit.
- Shoreline bank (brief (c)): low banks within 12 m inland slope at 20 deg from a lip at WaterLevelY + 0.3; outside, a 20 deg shelf runs to WaterLevelY - 2, then reaches WaterLevelY - 10 by 20 m out. No point outside the polygon is above WaterLevelY. 78.7% of shore stations are climbable (35 deg or less), and 100% of the low-bank ones are.
- Opposite banks north of the line (brief (f)): a seeded wavy lake edge (up to 57 m north of northLineZ) and a 25 deg bank cap replace the straight cut.
- North of the line, the real DEM river (hydro-flattened at 117.75–118.75 m) is carved to the same profile.

The water plane goes at `MapConfig.WaterLevelY`. A vertical retune moves the land and water together, so nothing is hardcoded. The lake-mask hash in report.json lets the water pass detect any terrain change.

## level

Measured on 2026-10-06 in the live Editor (6000.6.4f1, PID 51068) with `BellsBendLevel.BuildAll()`, run 06:19:17Z, built from tools' bb-terrain-rc4 (tile RAW sha256_16 `98a53eb7e0e3fea8`). World.unity sha256_16 after the save: `7156344f117eda10`. Full log: `Logs/bells-bend-level-build.txt`.

**Tile grid as built.** 20 Terrain tiles (`Terrain/Terrain_{ix}_{iz}`), 4 x 5 of 1024 m, heightmap 1025 (1 m), extent X -2087..2009, Z -2492..2628. Shared groupingID 7 with allowAutoConnect on, neighbours also set explicitly (20/20 connected). Max height step across shared tile edges: 0.000 m. `drawInstanced` on, heightmapPixelError 5. The margin is tools' (600 m minimum around the polygon). TerrainData is `Assets/World/BellsBend/Tiles/TD_{ix}_{iz}.asset`, 2.1 MB each (42 MB), heights only; art's splat, trees and details will grow these. The old 512 m WorldBuilder terrain is gone, and `WorldBuilder.Build()` now returns a "retired" message.

**Build and load cost.** The whole level build (RAW read, road levelling, tiles, spawn, markers, zones, LevelMaps, barrier, backstop, scene save) takes 13.4 s. With L1/L4/L6 evidence and 4 look shots it takes 21.4 s. Writing the tiles alone takes about 2 s. Editor-side load and streaming cost was **not measured**: no profiler or player run. R2 FPS is art's and qa's measurement.

**Level edits to tools' heights.** Road levelling is the only one: 5 painted roads, 30 m smoothed centreline, flat cross-section, smoothstep falloff. Cut/fill is capped at ±3 m per cell: 570 cells hit the cap, 0 m of centreline was capped. Levelling never goes below WaterLevelY + 1.5, and skips 12 m inland of the shore and 30 m around the bluffs. All 16 L2 sample points have a level-edit delta of 0.000 m. Inside the polygon, nothing else changes.

**North vista (B1).** Terrain runs 641 m north of northLineZ (minimum 400), real DEM from tools. Heights north of the line run -8.4..82.2 m (river carve to ridge). Zone ZN covers 99,057 cells of 5 m. The barrier is see-through post-and-rail (timber posts, 3 rails, 5 barbed-wire strands to 2.85 m, low scavenged sheets on about 22% of bays) with an open-frame gate, so the north view reads past it. The look shot from OHB 30 m south of the line shows that. It will be retaken after art's pass.

**Water-pass readiness (L3 side).** L1 measured on the live tiles: the WaterLevelY contour sits a mean of 0.51 m (p95 0.75, max 1.25) from the scaled inner bank, sampled every 10 m (955 samples). Road levelling never touches cells outside the polygon or below the floor, so tools' L3 guarantees hold after the level build. `LevelMaps` carries a signed shore distance (2 m grid, ±127 m) for the water pass to use for foam/shallows masks.

**Data the other disciplines read.** `Assets/World/BellsBend/LevelMaps.asset` (`WashedAshore.Level.BellsBendLevelMaps`): zone id (5 m grid, 820 x 1024) with a cliff bit; road id, paint weight and shore distance (2 m grid, 2048 x 2560). Stored as point-filtered, uncompressed, readable PNGs (LFS), round-trip checked byte for byte. `ZoneConfig.asset` holds designer-2's final parameters (forestSlopeDeg 6, ridgeHRel 60).

**Risks.**
1. **Repo weight.** `.asset` files are not in LFS, and the project uses force-text serialization. The tiles are 42 MB now and will grow with splat and detail. The baked barrier meshes (`Assets/World/BellsBend/Barrier/Meshes`, 150 assets) were 62 MB of YAML. After the 06:25Z fix (wire as boxes, no UVs/tangents; RebuildBarrier only, backstop unchanged) they are **19 MB**. World.unity sha256_16 was 4ec49452c57c4909 after that fix, and is bd97d7d8aa9dd5c6 after the 06:5xZ post-art slot (steep-bay fence, Player relinked to PlayerSpawn; backstop hash BB4BFEA70D466512 unchanged throughout). TD call: add LFS rules for `Assets/World/BellsBend/Tiles/*.asset` and `Assets/World/BellsBend/Barrier/Meshes/*.asset`, or generate the barrier mesh at load time instead.
2. **`Terrain.activeTerrain` call sites.** engineer-2 has moved runtime and editor code to `TerrainQuery.TileAt`. Any new code that samples one tile will read edge heights outside it.
3. **Zone shares.** Fields are 47.3% of land (designer-2 widened that band to 20–50%). That's a classifier outcome, not a defect.
4. **Long Editor commands.** A synchronous `unity command eval` hits a 5 s main-thread limit. Run builds with `--detach` and poll the log (friction BB-L1, BB-L2).
