Find out whether Level of Detail (LOD) is doing as much as it can across the world, and recommend changes with measured savings. **This is an investigation only.**

## What we know (checked 2026-10-14)

- **Quality levels:** PC has `lodBias` 2, `maximumLODLevel` 0 and LOD cross-fade on. The Mobile level (`lodBias` 1) is being removed in #14, so PC is the only level.
- **Trees:** built by `Assets/Editor/Art/BellsBendVegetation.cs` and `BellsBendTreeLods.cs`. There are 11 prefabs in `Assets/World/BellsBend/Art/TreeLods/`, each with one LODGroup (the kit mesh plus impostor cards). The kit trees are 3–10k triangles and drawn to the cull distance. Terrain trees: distance 450, billboards from 80, `treeMaximumFullLODCount` 50.
- **Grass and details:** detail prototypes reject LODGroups, so they get stripped. Detail distance 70.
- **Third-party prefabs:** **27 of 37** NatureMegaKit prefabs have LODGroups (20 trees, 4 bushes, 3 rocks).
- **Lookout tower:** one LODGroup with 4 levels (screen heights 0.4, 0.12, 0.035 and 0.0015) and animated cross-fade, built by `Assets/Editor/Lookout/LookoutTowerArt.cs`.
- **Terrain:** the live pixel error is **5**, from `BellsBendPerf` through `BellsBendGround`. `WorldBuilder`'s 3 is retired, so there's **no conflict**.
- **Animation culling:**
  - Wildlife: `CullUpdateTransforms`, with `updateWhenOffscreen` off (`WildlifePerfSetup.cs`).
  - Birds: set the same way by `BirdSetup.cs`, but the FBX variants aren't confirmed.
  - Runtime fish: unknown.
- `World.unity` holds no LODGroups directly; they all come from prefabs or tree prototypes.

## Pass criteria (what makes this investigation complete)

| # | Criterion |
|---|---|
| Q1 | **Renderer inventory.** A table of every renderer type in the live scene: LODGroup (yes/no), number of levels, transition distances at `lodBias` 2, and triangles per level |
| Q2 | **`lodBias` A/B.** 1 against 2 on PC, at every protocol spot: triangles, frame time, and screenshot pairs |
| Q3 | **Popping and wasted detail.** A list of the full-detail meshes still drawn past 150 m, and of any pops visible inside the fog (under 520 m) |
| Q4 | **A middle tree LOD.** A trial middle LOD for the 11 tree prefabs (between the kit mesh and the impostor), with its triangle and frame-time saving measured |
| Q5 | **Animation culling.** Confirm whether far or offscreen wildlife, birds and fish still animate or skin, using Profiler animation and skinning counters, and give the saving from fixing any that do |
| Q6 | **The recommendation.** A ranked list with measured savings, risk and effort, in `_bmad-output/poc/perf-lod-note.md` |

**Gate:** technical-director approves the note, and Noah picks.

## Automatic FAIL

- Unmeasured recommendations.
- Committing a live setting change.
- Grading the web build.

## Pod

**technical-artist** (lead), **platform-tools-engineer** and **technical-director**: 3 agents.

Related: #9 and #10 (same protocol), #14.
