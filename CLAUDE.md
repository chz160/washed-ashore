# washed-ashore (Washed Ashore)

## Web build size (deferred Addressables work)

`deploy-web.ps1` moves files over the 25 MiB Cloudflare Pages limit to the R2 bucket `washed-ashore-game-data`. That's a stopgap. **Before opening the northern region** (`MapConfig.gateOpen`, or any playable land north of the north line), **or when the deploy warns that `Web.data` is over 100 MB**, raise the Addressables split with Noah first. See `_bmad-output/planning-artifacts/deferred-addressables-content-streaming.md`.

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
