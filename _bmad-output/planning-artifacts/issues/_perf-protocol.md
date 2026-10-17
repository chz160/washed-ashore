## Measurement protocol (shared by #9, #10, #11)

- **Windows player build only** (`BellsBendWindowsBuild.BuildBatch`). The web build is retired (#14); don't measure or grade it.
- **Fixed spots, each with a saved camera pose:**
  - **Spawn:** Bells Bend Park, the active `PlayerSpawn` at (-202.8, 15.9, -942.1)
  - **River bank**
  - **Valley floor**
  - **Ridge top**
  - **Lookout cab:** `-fvCab`
  - **Route:** the `-bbFpsWalk` route
- **Harness:** `Assets/Scripts/Perf/BellsBendFpsWalk.cs` (`-bbFpsWalk`) and `FarViewProbe` (`-fv*`, `tools/perf/farview_matrix.ps1`). Add counters through `ProfilerRecorder`: batches, SetPass calls, triangles, visible renderers and shadow casters.
- **Method:** an A-B-A order, 3 runs per variant, on a quiet machine with vsync off at 1920×1080. Report median, p95 and p99 frame time, plus the counters.
- **Applying a setting:** a trial change is applied **only for the measurement**, then reverted. These issues commit notes, harness additions and measurement data, never live settings.
