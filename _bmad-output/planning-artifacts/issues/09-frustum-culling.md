Find out whether frustum culling in `World.unity` is doing as much as it can, and recommend changes with measured savings. **This is an investigation only:** measure and recommend; change no live settings here.

## What we know (checked 2026-10-14)

- **Scene:** `World.unity` is the only scene in the build. `LookDev/FishLookDev.unity` exists but isn't built. It has 20 terrain tiles of 1024 m and **1,278** static objects.
- **Camera:** one camera, far clip **1000 m**, near clip 0.1. The lookout cab view accepts 1.15 km. **Linear fog runs from 120 to 520 m.** Everything past about 520 m is fully fogged but still drawn up to 1000 m.
- **No custom culling.** Nothing uses `Camera.layerCullDistances` or `CullingGroup`.
- **Terrain distances on the live tiles** come from `BellsBendPerf`, through `BellsBendGround.ApplyRenderSettings`:
  - basemap 250
  - trees 450
  - billboards 80
  - details **70**
  - pixel error 5
  - `treeMaximumFullLODCount` 50
  - instanced on

  `WorldBuilder.cs` (600/90/120) is **retired, so there's no conflict**.
- **Shadows:** the applied shadow distance is **50 m**, from the URP assets (`m_ShadowDistance`). QualitySettings' 40 is ignored by URP. 2 cascades, with a 2048 shadowmap on PC.
- **Renderer:** PC uses the GPU Resident Drawer with Forward+.

## Pass criteria (what makes this investigation complete)

| # | Criterion |
|---|---|
| Q1 | **Submitted against visible.** For each spot in the protocol, the number of renderers submitted against visible, with batches, SetPass calls, triangles and shadow casters |
| Q2 | **Draw distance against fog.** A measured A/B of the 1000 m far clip against fog-matched distances (about 550–600 m at ground level, 1.15 km in the cab, for example a per-spot or cab-only far clip). Report frame time and a screenshot pair per spot showing no visible difference |
| Q3 | **Per-layer cull distances** for small props, wildlife, birds, fish and details: savings measured with a trial, plus a pop-in check (screenshots at the cull edge, inside fog) |
| Q4 | **Oversized bounds.** Every renderer with bounds over 200 m (water quads, merged meshes, tile-wide renderers), and whether it defeats culling |
| Q5 | **Shadow casters.** Shadow-caster culling verified at 50 m, with the shadow pass cost per spot |
| Q6 | **The recommendation.** A ranked list of changes, each with its measured saving (ms, p95), risk and effort, in `_bmad-output/poc/perf-frustum-note.md` |

**Gate:** technical-director approves the note. Noah picks which changes become a follow-up implementation issue.

## Automatic FAIL

- A recommendation with no measurement behind it.
- Committing a live setting change.
- Grading the web build.

## Pod

A small pod: **platform-tools-engineer** (harness and measurement), **technical-artist** (pop-in judgement), **technical-director** (review). No more than 3 agents.

Related: #10 and #11 (same protocol), #14 (web retired).
