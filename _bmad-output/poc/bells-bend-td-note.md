---
title: 'Technical-director note: Bells Bend tiles, streaming, water-pass readiness'
owner: technical-director
date: 2026-10-06
inputs: bells-bend-td-input.md, bells-bend-qa-report.md, bells-bend-design-brief.md, friction-log.md (BB-*), tools/terrain/README.md, Data/terrain/build/manifest.json
state_reviewed: terrain rc4 (tiles 98a53eb7e0e3fea8, lake 8f8a637cd4657d82), MapConfig d18800230508b305
---

# Bells Bend land: TD note

**Correction to the input numbers first.** The 42 MB figure for tiles is out of date. That was heights only, at level build. After art's pass, `Assets/World/BellsBend/Tiles/TD_*.asset` is **270 MB on disk: 13.4 to 14.7 MB per tile, Unity binary-serialized** (I checked the header, not YAML), and not covered by `.gitattributes`. The 5 all-water tiles weigh 13.4 MB each, the same as land tiles. The whole `Assets/World/BellsBend/` folder is still untracked. Section 4 depends on this.

## 1. Tile size and resolution

**Verdict: keep 4 x 5 tiles of 1024 m at 1025 (1 m spacing).** No change before production.
- 1 m spacing is what keeps the 72-77 deg hollow walls and bluffs (L4 bluff areas 3,859 / 6,044 m2) and the 0.33 m build-side shoreline deviation. 513 (2 m) would soften exactly the faces the brief says must never be smoothed. Don't take that lever for frame time. R2 is at 149.7 FPS median (6.68 ms) against a 60 FPS bar, so there's no frame-time case for it.
- 1024 m tiles are a sensible streaming unit (section 2) and keep shared edges exact (0.000 m step, 20/20 neighbours).
- The 16-bit range of 103.2 m gives a 1.6 mm step. Fine. If a later retune raises `verticalScale`, check `terrainHeight` headroom, because the lake bed at W-10 sets the floor.

**Recommended changes:**
1. **All-water tiles (x0_z0..x0_z3, x3_z3): cut alphamap and detail resolution, not heightmap resolution.** About 8 MB of each 13.4 MB tile is two 1024x1024 control maps painting a lake bed. Allow a 256 alphamap and the minimum detail resolution on tiles with 0% land. That saves about 35-40 MB on disk and in memory, with no seam risk. `BellsBendGround` clamps alphamap to a minimum of 512 today. That clamp needs a per-tile override.
2. **Keep the heightmap at 1025 on lake tiles until the water pass is done.** The water pod may want bed shape (section 3). Mixed resolutions also need edge stitching.
3. The 3 tiles under 10% land (x3_z0, x1_z0, x3_z2) stay at full resolution. They hold shoreline.

## 2. Streaming

**Not needed now.** 4.1 x 5.1 km, 20 tiles, about 21M height samples and 101,592 trees run at 149.7 FPS with 100% of frames at 60 or above. **What's missing is a memory and load-time number.** Nobody has profiled the release player's memory or startup. That measurement, not FPS, is what decides streaming. My trigger: build streaming when the release player's terrain-plus-vegetation memory goes over 1.5 GB, when load to first frame goes over 20 s on the reference PC, or when a second map area is added. Whichever comes first.

**Path, when triggered (in this order; each step is useful by itself):**
1. **TerrainQuery first** (`Assets/Scripts/Gameplay/TerrainQuery.cs`):
   - Replace the linear scan with an O(1) grid index from the manifest (`gridOrigin`, `tileSize`, `tilesX/Z`).
   - Remove the `Refresh(true); return TileAt(position);` recursion (lines 53-54), as qa and engineer-2 already agreed.
   - Reset `cachedFrame` in a `[RuntimeInitializeOnLoadMethod(SubsystemRegistration)]` hook.
   - Most important: `Height()`/`Steepness()` fall back to the **nearest tile's edge height** when no tile covers the point. Under streaming an unloaded tile would silently return wrong ground. Add an explicit "not loaded" result, and make agents on unloaded tiles sleep instead of sampling.
2. **Tile neighbours.** Call `SetNeighbors` explicitly after every load or unload, not only through `groupingID`/`allowAutoConnect`, so LOD seams are deterministic.
3. **Additive scene per tile.** `Tile_{ix}_{iz}.unity` holds the Terrain, its TerrainData, and tile-local props. The persistent scene keeps the player, MapConfig, barrier, backstop, LevelMaps, water and the wildlife and bird managers. Load the 3x3 ring around the player and unload at 2 tiles' distance with hysteresis. The level build must emit the tile scenes, so scripts-only (AF1) still holds.
4. **Addressables.** One group per tile scene, so a tile's TerrainData and its trees travel together. Only do this once tile scenes exist. Addressables on a single-scene world buys nothing.
5. **Global systems** that read every tile (`BellsBendFpsWalk`, `WorldBoundsBuilder`, `WorldBoundsShiftTrees`, the zone report, `Terrain.activeTerrains` loops) must be either editor-only or tolerant of partial sets.

## 3. Water-pass readiness

**Ready:**
- **One water height.** `MapConfig.WaterLevelY` = (117.3 - 115) x 0.7 = **1.61**. Python and C# agree (BB-T10). A vertical retune moves land and water together.
- **Bed depth.** Outside the polygon, the bed follows the 18 deg shelf to W-2 (wading), then a smoothstep to W-10 by 20 m out. qa measured: max 1.598 (at or below W) within the ramp band, and at or below -8.364 beyond the ramp.
- **Banks.**
  - 766/766 low-bank stations are at 35 deg or less.
  - The lip step is 0.31 m, below stepOffset 0.4.
  - Bluffs and cut banks meet the water as faces, by ruling.
  - The north-of-line river is carved to the same profile.
- **Change detection.** `LevelMaps` has a signed shore distance (2 m grid, +/-127 m) for foam and shallows. `report.json` carries a lake-mask hash, so terrain changes are detectable.

**Risks:**
1. **The lake bed is flat.** Everything past the 20 m ramp is a uniform plane at W-10 (hydro-flattened DEM, so there's no real bathymetry), including 5 whole tiles. Under clear water, or from the bluffs, that reads as a swimming pool. The water pod must choose one: (a) water fog or absorption that hides the bed by about 4-6 m of depth, or (b) ask tools for a seeded bed variation beyond the ramp, kept at or below W-8 so L3 still holds. Option (a) is cheaper and is my default.
2. **Moved-line cut.** If `northLineZ` moves, the neck crossings are not re-derived, and up to 22.7 m cuts appear beside the neck. Water would flood them into straight-edged inlets. The current guard only warns. For any water build, **it has to fail** (see the checklist).
3. **Robin and fence layers.** The invisible fence extensions (up to 16 m) are on layer 0 (Default). `RobinFlightPlanner` (line 21) casts against `Default`, so robins collide with invisible walls. If the water surface gets a collider on Default, robins and any Default-layer raycasts will land on water. Both need dedicated layers. That means one ProjectSettings change, made by the integration owner in one slot.
4. **The player has never been in water.** The dry lake bed was walkable (B5 lake-bed runs). The full-width backstop still closes the end-arounds, but deep-water behaviour (wade, swim, or push-back) is undesigned.
5. **The west neck corner step** (3.41 m, ruled exception) and the 0.31 m lip will sit right at the waterline. Recheck them visually once water is on.

**Checklist for the water pod (each item needs evidence in the QA report):**
- [ ] The surface Y comes from `MapConfig.WaterLevelY` at build. Add an EditMode test that asserts plane Y == cfg.WaterLevelY. No literal 1.61 anywhere.
- [ ] Coverage spans the whole terrain extent (X -2087..2009, Z -2492..2628), including the north-of-line river carve. Build it as per-tile quads on the 1024 m grid, so it streams with the tiles later.
- [ ] Record the lake-mask hash the water was built against (8f8a637cd4657d82). The water build fails on a mismatch.
- [ ] Promote the north-line-moved warning to an error in the water build path, until the crossings are re-derived.
- [ ] Layers: water on built-in layer 4 (Water), fence extensions on a new `WorldBounds` layer, and robin and other ground or sky casts exclude both. Re-run birds 17/17 (42/60/78) and B5 9/9 afterwards.
- [ ] Foam and shallows come from `LevelMaps` shore distance. Don't recompute shorelines in the shader pipeline.
- [ ] Decide on bed visibility (risk 1). Get Noah's look check from a bluff top and from the OHB fence.
- [ ] Player-in-water behaviour is specified by systems-designer before code. The CharacterController must not ground on the water collider.
- [ ] Habitat stays dry: wildlife and robin land rules keep `ground >= W + 0.5` (HabitatGround). Verify no spawns in shallows.
- [ ] No planar-reflection camera (it doubles terrain and tree draws). Use SSR or a reflection probe. **Budget: at most 1.5 ms added** to the R2 median (6.68 ms) at 1080p on the reference PC, with p99 under 16.6 ms. Re-run the R2 walk.
- [ ] Re-run L1, L3, B3, B5 and AF1 with water in the scene.

## 4. Repo weight and LFS

**Recommendation: add LFS rules now, before `Assets/World/BellsBend/` is first committed.** Converting history later means a rewrite.
- `Assets/World/BellsBend/Tiles/*.asset`: binary, 270 MB, and every terrain or art rebuild rewrites all 20. **LFS, mandatory.**
- `Assets/World/BellsBend/Barrier/Meshes/*.asset`: 19 MB of YAML across 158 assets, every one rewritten on each barrier bake. Text diffs of mesh vertex blobs have no review value. **LFS now.** Don't move to load-time generation in the POC: it moves cost into startup and complicates the AF1 fingerprint. Post-POC, merge to one mesh per material per segment to cut the file count.
- `Data/terrain/build/*.raw` (84 MB) is already LFS. The DEM cache (604 MB) stays git-ignored.
- **Commit cadence:** commit tile and barrier assets only at milestone or QA-of-record points, not on every iteration. Each rebuild costs about 290 MB of LFS storage.
- **Environment:** git-lfs is not on the shared Bash PATH (BB-T9). Fix it in the shared PATH prefix so `git lfs ls-files` works in the pre-Editor pointer check. Owner: build-engineer.

## 5. Top 5 tech follow-ups (ranked)

1. **LFS rules for tiles and barrier meshes, plus git-lfs on PATH, before the first commit of this content** (build-engineer). It's irreversible if missed, and it costs minutes now.
2. **Re-derive the neck crossings from the shifted bank geometry, and make the line-moved warning an error until that lands** (platform-tools / tools). This blocks any real line move and guards the water pass.
3. **Layer split: `WorldBounds` for the invisible fence extensions, Water for the surface, and robin casts exclude both** (gameplay-engineer; ProjectSettings by the integration owner). Do it at the start of the water pass, with bird and B5 re-runs.
4. **TerrainQuery hardening** (gameplay-engineer):
   - Grid-index lookup.
   - No recursion.
   - SubsystemRegistration reset.
   - An explicit "not loaded" result instead of the nearest-edge fallback.
   - Migrate the 3 remaining single-tile `Terrain.activeTerrain` reads in tests (`BirdFrameTimeTests.cs:131`, `WildlifeFrameTimeTests.cs:164`, `WildlifeBehaviourTests.cs:97`).
5. **Release-player memory and load-time profile, plus a per-tile alphamap and detail override for the 5 all-water tiles** (platform-tools-engineer with technical-artist). The memory and load-time profile is what decides streaming (section 2). The override is the cheap memory win (section 1).

Also logged, not ranked:
- Move `BarrierBuilder.LineOffset` into MapConfig.
- Re-baseline or retire `WashedAshore.Tests.Performance` (non-gating per the BB-QA-7 ruling).
- Make long Editor commands (`--detach` plus the compile poll, BB-L1/L2/A1) a documented runbook step.
