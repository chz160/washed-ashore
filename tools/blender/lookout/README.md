# Lookout tower build (MC-39)

The tower is built by script only: `build_mc39.py` and its `mc39_*.py` modules (Blender 5.2.2, headless) make the
model and its textures from the parameter table `mc39_params.json`, and the PowerShell scripts here run and check
it. Never open the Blender UI for this.

`build_mc39.ps1` is the only supported way to build the shipped FBX. It sets `PYTHONHASHSEED=0` itself (and clears
the other `PYTHON*` variables, and uses `--factory-startup`), because Blender's FBX exporter derives node IDs from
Python's string hash. A bare `blender -b --python build_mc39.py` gives different node IDs.

## Build

```powershell
pwsh tools/blender/lookout/build_mc39.ps1
```

- Output: `tools/blender/lookout/out/build/` (staging, gitignored). Log and receipt (input and output SHA-256):
  `tools/blender/lookout/out/build_mc39.log` and `build_mc39.receipt.json`.
- `-Out <dir>` picks another staging folder. It is emptied first if it is under `out/` or `TestResults/`;
  anywhere else it has to be empty. It can never be inside `Assets/`.
- `-Params <json>` uses another parameter table. `-Blender <blender.exe>` overrides the lookup (PATH, then
  `C:\Program Files\Blender Foundation\Blender 5.2\blender.exe`).
- `-NoTextures` builds the grey model only (geometry, no texture step). Both scripts take it; it cannot be combined
  with `-IntoAssets`, and the T3 result of record is the full build.
- Exit codes: `0` built; `1` build failed (Python error, no `BUILD_MC39 OK` line, no FBX, timeout);
  `2` environment (Blender missing or not 5.2.2, script or table missing); `3` refused output path.

## Verify (T3: reproducible from script)

```powershell
pwsh tools/blender/lookout/verify_repro.ps1 -ControlSet "<key>=<value>"
```

Two clean builds into `TestResults/tower-tools/run-a/` and `run-b/`, then a byte comparison of every output file.
`-ControlSet` is the negative control: a third build (`run-control/`) from a copy of the table with that one
parameter changed (dotted key for nested values) has to differ from `run-a` outside the mask.
Result: `TestResults/tower-tools/repro.json` (hashes of both runs, masked fields, control, verdict). Exit `0` PASS,
`1` FAIL, `2` a build failed or no control was given (INCOMPLETE).

The only bytes ignored are the FBX creation time: the seven int32 values `Year`, `Month`, `Day`, `Hour`, `Minute`,
`Second`, `Millisecond` under `FBXHeaderExtension/CreationTimeStamp` (28 bytes per FBX), which Blender fills from
the clock. They are found by walking the FBX node records, and are accepted only if they decode to the time of
that run. Any other differing byte fails the check.

`-ResultDir <dir>` writes somewhere else (it must be under `TestResults/` to be re-used; elsewhere it has to be
new or empty). `-Params <json>` checks another table, for example a copy with a changed height.
`-Against <dir>` compares an existing copy (such as the folder landed under `Assets/`, read-only, `.meta` ignored)
with one fresh build and writes `against.json`.

## Import into Unity

```powershell
pwsh tools/blender/lookout/build_mc39.ps1 -IntoAssets -AssetsDir <folder under Assets/>
```

Builds to staging, then copies the changed files into the given folder under `Assets/`. It never
deletes, so `.meta` files keep their GUIDs, and an unchanged build causes no reimport. This triggers a Unity
import: run it only while you hold the Editor baton.
