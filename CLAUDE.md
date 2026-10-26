# washed-ashore (Washed Ashore)

## Build size (deferred Addressables work)

**Before opening the northern region** (`MapConfig.gateOpen`, or any playable land north of the north line), raise the Addressables split with Noah first. The size trigger is being re-set in #12. See `_bmad-output/planning-artifacts/deferred-addressables-content-streaming.md`.

**Targets: Windows, Linux, macOS (macOS is an unsigned test build).** The web build is retired (2026-10-14, #14): see `_bmad-output/poc/web-retired.md`. Web criteria and "breaking the web build = automatic FAIL" in older specs and reports no longer bind.

## Physics: when to use it and when not to

**Before adding or changing anything that moves, collides or runs physics queries, read [`docs/physics-guidelines.md`](docs/physics-guidelines.md) and follow it.** In short:
- The player is a kinematic CharacterController.
- Animals, birds and fish use scripted or analytic motion, with no colliders unless gameplay needs them.
- Dynamic Rigidbodies are simulated only on the server, and only when something must tumble and settle.
- Cosmetic physics runs on each client.
- Every query uses a named layer mask, `QueryTriggerInteraction` set and NonAlloc.
- No collider moves without a kinematic Rigidbody.

Every spec states which motion model its system uses.

## Model facing (rigged FBX)

Unity's forward is +Z. Most vendor rigged FBX files arrive facing -Z, among them Quaternius animals and characters (see `_bmad-output/poc/vendor-facing-survey.md`). `Assets/Editor/Wildlife/ModelFacingPostprocessor.cs` fixes this at import time: it detects the facing and bakes a 180° yaw into the armature root, the mesh node and the clips.

- **Never compensate in code.** No runtime yaw offsets, tuning fields or wrapper rotations for backwards models. They were removed with WL-BUG-7.
- **After importing new rigged FBX files, run the report:** **Washed Ashore > Art > Model Facing Report**, or call `ModelFacingPostprocessor.FacingReport()`. It writes `TestResults/model-facing-report.json`.
  - Each model must show either `flip` (fixed) or `keep`.
  - Treat an `ambiguous` warning as a bug: fix the detection or the asset. Don't hand-rotate.
- **Unpacked prefabs don't follow a reimport.** After a reimport, run **Washed Ashore > Art > Sync Prefab Facing** (`SyncPrefabs()`).
- **Out of scope for the postprocessor:**
  - Humanoid rigs. They're skipped, because Unity may already orient them.
  - glTF/GLB files.
  - Weapons. These face +X; use a socket offset.
  These need their own handling when they're brought in.
- **Rule and acceptance criteria:** `_bmad-output/planning-artifacts/poc-spec-wildlife-import-facing.md`.

## Line endings and file hashes

Git stores LF for every text file (`.gitattributes`, issue #62). Files Unity writes (`.unity .prefab .asset .meta .mat`, `.asmdef`, `Packages/manifest.json` and similar) are `eol=lf`: LF on disk on every machine. Files people edit (`.cs .md .json .py`) are `text=auto`: CRLF on Windows, LF on Mac and Linux. `.sh` is always LF; `.bat` and `.cmd` always CRLF.

- **Hash approved files with `git hash-object <file>`**, never sha256 of the bytes on disk. A `text=auto` file is CRLF on disk and LF in git, so the two differ.
- **Don't add line-ending-only changes.** If `git status` shows a file changed but `git diff --ignore-cr-at-eol` is empty, restore it; don't commit it.
