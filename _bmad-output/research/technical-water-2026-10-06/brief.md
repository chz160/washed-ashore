# Brief: technical water for Washed Ashore (Bells Bend)

Decision: which water rendering + motion approach the water pod builds in Unity 6000.6.4f1 / URP 17.6.0, for Windows (>=60 FPS) and WebGL2 (>=30 FPS).
Type: technical. Shape: select. Preset: deep (6 workers, 12 sources/round, depth up to 3). Validation: normal (+ two-source on version/compat and perf numbers per pack). Red team: off (headless; logged as assumption).
Lead: w-td. Contributors: w-artist (items 2, 5), w-designer (items 4, 7). Headless plan-and-proceed (no interactive user; team-lead set scope).

## Requirements frame (from project: spec, TD note §3, greenlight/water, adr/web-target-webgl2)
Hard gates:
- G1 Free; no Asset Store login; licence recorded and allows commercial use and keeping source in repo.
- G2 Runs on Unity 6.6 / URP 17.6 (Render Graph path).
- G3 Builds and runs on WebGL2 (standing ADR). WebGPU noted, not required.
- G4 Agents can build/configure it from scripts (batchmode / editor scripts, no GUI-only steps).
- G5 No planar-reflection camera in the shipped config (TD §3).
- G6 Surface motion expressible as a pure deterministic f(x,z,t) evaluable in C# off-GPU (server, buoyancy).
Weighted preferences (1-5): look fit to greenlight 5; perf headroom 5; determinism/server parity 4; maintenance & upgrade risk 4; agent build effort 3; future boats/debris/floods extensibility 3; web size 2.

## Dimensions
D1 Candidate landscape + licences (w-td fan-out)
D2 Unity 6.6 / URP 17 Render Graph + WebGL2/WebGPU compatibility per candidate (w-td fan-out)
D3 Deterministic motion f(x,z,t), buoyancy, server evaluation (w-td fan-out)
D4 Reflections, transparency cost, budgets, per-tile quads, LOD, web size (w-td fan-out)
D5 Look + underwater (w-artist, items 2 and 5)
D6 Player in water + swim/drown numbers (w-designer, items 4 and 7)
