# Engine note: Unity

Read this before you touch anything under `Assets/`, `Packages/` or `ProjectSettings/`. The project's own rules (the profile notes and `CLAUDE.md`) come first.

## One editor owner
- **Only the role named `roles.editor_owner` in the profile changes the live Editor**: scenes, prefabs, materials, import settings, Play mode, menu items and editor scripts that modify assets. It applies changes **one at a time** and verifies each one before the next.
- Anyone else may run read-only queries: `unity status`, reading the console and logs, inspecting the hierarchy. For a change, send the editor owner exact steps: the object, the field, the value, and how to check it.
- Headless batchmode (`build-engineer`) can't open a project that an Editor already has open. Ask the editor owner to close it or wait, and agree in a message who goes first.

## Scenes, prefabs and `.meta` files: single owner
- Each scene or prefab has one owner for the pod, named in the task. Two agents never change the same scene or prefab in one pod.
- Change them through the Editor or the Unity API, never by hand-editing their YAML. Small, reviewed exceptions are the editor owner's call.
- Never create, delete or hand-edit `.meta` files. Move and rename assets through the Editor or `AssetDatabase`, so their GUIDs survive.
- `ProjectSettings/` and `Packages/manifest.json` belong to `technical-director`'s rulings. Engineers change them only in a task that says so.

## Tools
- **unity-cli skill** (user-level, so teammates load it from settings):
  - `unity status` to confirm the connected Editor
  - `unity command eval` for C# experiments and batch asset operations
  - `unity recompile` after code edits
  - The skill's **Play mode verification loop**. An unfocused Editor can freeze on frame 1, so never trust one screenshot.
  - Add `--project-path` when more than one Editor may be open.
- **Unity MCP**, where it's configured: same ownership rules. Use `ToolSearch` to load its tools.
- **Headless:** `Unity -batchmode -projectPath <repo> -runTests -testPlatform EditMode|PlayMode -testResults TestResults/<name>.xml -logFile -`. Use the editor version in `ProjectSettings/ProjectVersion.txt`. Don't pass `-quit` with `-runTests`.
- If the Editor is in Safe Mode, fix the compile errors first. Nothing else is trustworthy until you do.

## Tests
- Use the Unity Test Framework: EditMode for logic, PlayMode for behavior. Write tests for the code you own.
- For `TESTS: PASS`, record the exact command or test filter, how many tests passed out of the total, and the results file.

## Code
- C# names and folder layout follow `technical-director`'s standards and the assembly definitions in the repo.
- Every number a designer might tune is a `[SerializeField]` or lives in a ScriptableObject.
