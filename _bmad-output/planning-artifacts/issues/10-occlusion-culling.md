Find out whether occlusion culling (baked Umbra, GPU, or both) would pay off, and recommend a setup. **This is an investigation only:** no committed bake or setting change.

## What we know (checked 2026-10-14)

- **No baked data.** `World.unity` has `m_OcclusionCullingData: {fileID: 0}`, with the defaults `smallestOccluder` 5, `smallestHole` 0.25 and `backfaceThreshold` 100. The camera has occlusion culling on, but with no data it does nothing. The `com.unity.modules.umbra` module is installed.
- **GPU occlusion is off.** The setting is on the **RP asset**, not the renderer: `PC_RPAsset` has `m_GPUResidentDrawerEnableOcclusionCullingInCameras: 0`. PC uses the GPU Resident Drawer with Forward+, so it's available.
- **An open world:** a river bend, ridges, trees, and linear fog from 120 to 520 m. Hills and the ridge may hide large areas from low viewpoints. Unity warns that GPU occlusion can cost more than it saves in scenes with little occlusion ([URP GPU culling](https://docs.unity3d.com/6000.4/Documentation/Manual/urp/gpu-culling.html)).
- **Vegetation:** trees and grass are terrain trees and details on 20 tiles.

## Pass criteria (what makes this investigation complete)

| # | Criterion |
|---|---|
| Q1 | **GPU occlusion A/B.** Measured at every protocol spot, plus behind-the-ridge and valley-floor spots. Report the frame time (CPU and GPU), the change in visible renderers, and the occlusion pass's own cost |
| Q2 | **A trial Umbra bake.** Bake time, data size, and the same A/B. Say whether terrain occludes in each approach, and how terrain trees, details, and moving objects (wildlife, birds, fish) are handled |
| Q3 | **Compatibility.** A list of objects that would need **Disallow GPU Driven Rendering**, or that break under the Resident Drawer: per-instance render callbacks, `MaterialPropertyBlock` users, VAT fish, birds |
| Q4 | **The recommendation:** none, baked, GPU or both, for the PC quality level (Mobile is being removed in #14), each with its measured saving and its cost. Written to `_bmad-output/poc/perf-occlusion-note.md` |

**Gate:** technical-director approves the note, and Noah picks.

## Automatic FAIL

- A recommendation with no A/B numbers.
- Committing the bake or the setting change.
- Grading the web build.

## Pod

**platform-tools-engineer** and **technical-director**: 2 agents.

Related: #9 and #11 (same protocol), #14.
