---
title: 'POC acceptance checklist: Washed Ashore walk-around'
source_spec: _bmad-output/planning-artifacts/poc-spec-washed-ashore-walkaround.md
owner: systems-designer
consumer: qa-lead
created: '2026-10-04'
---

# Acceptance checklist (P1–P9 blocking, S1–S2 non-blocking)

Rules for qa:
- Every check is PASS/FAIL. No partial credit. Record the exact command, raw output, and timestamp per check.
- `eval` snippets are C# bodies; wrap them in whatever form `unity command eval` accepts and paste the raw return value. If an `eval` hangs or errors 3 times with no progress, that is an Automatic FAIL blocker — log it, don't work around it.
- Run all `eval` checks in Edit mode with `Assets/Scenes/World.unity` open, after a clean domain reload (no compile errors pending).
- Naming contract (agreed with level / techart): terrain tree prototypes keep the MegaKit source FBX name in the prefab name. Rocks and bushes are placed **as terrain tree prototypes** (not loose GameObjects) so P4 is checkable from `TerrainData` alone.

Shared helper used below:
```csharp
var t = UnityEngine.Terrain.activeTerrain; var d = t.terrainData;
```

## P1 — Project created by CLI, 6000.6.4f1, URP, bridge connected
| ID | Check | Pass condition |
|---|---|---|
| P1.1 | Command log contains the create call | A line matching `unity projects create .*E:\\GitHub\\washed-ashore.* --with-pipeline` and its exit code 0 |
| P1.2 | `ProjectSettings/ProjectVersion.txt` | Contains `m_EditorVersion: 6000.6.4f1` exactly |
| P1.3 | `Packages/manifest.json` | Has keys `com.unity.render-pipelines.universal`, `com.unity.pipeline`, `com.unity.inputsystem` |
| P1.4 | eval `UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline?.GetType().Name` | Returns `UniversalRenderPipelineAsset` |
| P1.5 | eval `UnityEngine.QualitySettings.renderPipeline == null \|\| UnityEngine.QualitySettings.renderPipeline is UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset` | `true` |
| P1.6 | `unity status` | Reports the editor for `E:\GitHub\washed-ashore` as connected |

## P2 — Single scene, straight into gameplay
| ID | Check | Pass condition |
|---|---|---|
| P2.1 | eval `string.Join("\|", System.Array.ConvertAll(UnityEditor.EditorBuildSettings.scenes, s => s.path + ":" + s.enabled))` | Exactly one entry: `Assets/Scenes/World.unity:True` |
| P2.2 | eval count of `UnityEngine.Canvas`, `UnityEngine.UIElements.UIDocument`, `UnityEngine.EventSystems.EventSystem` via `Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length` | All three = 0 |
| P2.3 | eval root object names of active scene (`SceneManager.GetActiveScene().GetRootGameObjects()`) | List recorded as evidence; contains a Terrain, a `Player` (tag `Player`, has `CharacterController`), a `PlayerSpawn`, a Directional Light. No menu/HUD/inventory objects |
| P2.4 | `PlayerSettings.SplashScreen` | Unity's own splash is allowed; no custom logos (`PlayerSettings.SplashScreen.logos.Length == 0`) |

## P3 — Terrain
| ID | Check | Pass condition |
|---|---|---|
| P3.1 | eval `d.size.x + "x" + d.size.z` | Both ≥ 500 |
| P3.2 | eval height range: `var h = d.GetHeights(0,0,d.heightmapResolution,d.heightmapResolution); float mn=1,mx=0; foreach (var v in h){ if(v<mn)mn=v; if(v>mx)mx=v; } return (mx-mn)*d.size.y;` | ≥ 20.0 (metres) |
| P3.3 | eval `d.terrainLayers.Length` | ≥ 3 |
| P3.4 | eval each layer's `UnityEditor.AssetDatabase.GetAssetPath(layer.diffuseTexture)` | All non-empty; ≥ 3 resolve to ambientCG-sourced textures (path or file name carries the ambientCG asset ID, e.g. `Ground037_1K_Color`) |
| P3.5 | eval painted coverage: `var a = d.GetAlphamaps(0,0,d.alphamapWidth,d.alphamapHeight);` sum weight per layer / (w*h) | ≥ 3 layers each with coverage ≥ 0.01 (1%) |
| P3.6 | eval `t.materialTemplate.shader.name` | Starts with `Universal Render Pipeline/Terrain/` |

## P4 — Vegetation (MegaKit)
Classification by prefab name (case-insensitive): **tree** = matches `tree|pine|birch|oak|maple|willow|palm|dead`; **rock/bush** = matches `rock|stone|boulder|pebble|bush|shrub`. A prototype matching neither is ignored.
| ID | Check | Pass condition |
|---|---|---|
| P4.1 | eval `d.treePrototypes` → names `p.prefab.name` | ≥ 3 distinct **tree** prototypes |
| P4.2 | same list | ≥ 2 distinct **rock/bush** prototypes |
| P4.3 | eval prefab source: `AssetDatabase.GetAssetPath(p.prefab)` and its mesh source paths | All P4.1/P4.2 prototypes trace back to MegaKit FBX imported from `vendor/` (mesh asset path under the techart import folder) |
| P4.4 | eval count of `d.treeInstances` whose `prototypeIndex` is a **tree** prototype | ≥ 200 |
| P4.5 | eval count of instances per rock/bush prototype | Each P4.2 prototype has ≥ 1 instance |
| P4.6 | eval `d.detailPrototypes.Length` | ≥ 1 |
| P4.7 | eval total density: `for each layer i: sum of d.GetDetailLayer(0,0,d.detailWidth,d.detailHeight,i)` | ≥ 1 layer with sum > 0 |
| P4.8 | eval tree instance bounds | All instance positions within [0,1] on x/z (no off-terrain instances) |

## P5 — URP materials only
| ID | Check | Pass condition |
|---|---|---|
| P5.1 | eval: gather materials from (a) all `Renderer`s in the open scene, (b) every renderer inside every `d.treePrototypes[i].prefab`, (c) `d.detailPrototypes[i].prototype` renderers / `prototypeTexture` materials, (d) every `.mat` under `Assets/` via `AssetDatabase.FindAssets("t:Material")`. Filter: `m == null \|\| m.shader == null \|\| m.shader.name == "Hidden/InternalErrorShader" \|\| !m.shader.isSupported \|\| !(m.shader.name.StartsWith("Universal Render Pipeline/") \|\| m.shader.name.StartsWith("Shader Graphs/"))`. Return the offending `assetPath:shaderName` list | Empty list |
| P5.2 | eval null-material slots: any `Renderer` in scene or prototype prefab with a `null` entry in `sharedMaterials` | 0 |
| P5.3 | Built Windows player (P8) visual smoke | Noah reports no magenta during walk-around |

## P6 — Movement, look, gravity, no fall-through
Covered by P7 (b), (c) and the recommended (e). P6 passes iff P7 passes.

## P7 — PlayMode tests
Run: `unity test --mode PlayMode` (results XML saved as evidence). Pass = NUnit XML `failed="0"`, and tests a–d present with `result="Passed"`. Tests must drive input through `InputTestFixture` (Input System test helpers), not by setting transform positions.
| Test | Setup | Pass condition |
|---|---|---|
| (a) LoadsWorld | `SceneManager.LoadScene("World")`, wait 1 frame | Active scene name `World`; exactly one object tagged `Player` with `CharacterController` |
| (b) WMovesPlayer | After load, wait until grounded (max 3 s); record xz position; hold Keyboard W for 2.0 s (`WaitForSeconds(2f)`); release | Horizontal (xz) distance ≥ 3.0 m |
| (c) GroundedAfter3s | After load, wait 3.0 s with no input | `CharacterController.isGrounded == true` AND player base y ≥ `Terrain.activeTerrain.SampleHeight(pos) + terrainY - 0.1` (did not fall through) |
| (d) FramesAdvance | Record `Time.frameCount`; `WaitForSecondsRealtime(1f)` | Delta ≥ 10 frames |
| (e) MouseLooks — recommended, non-blocking | Apply mouse delta (+100, 0) over 1 frame | Player yaw changed by > 1° |

Each test also asserts no unexpected error logs (UTF `LogAssert` default behaviour fails on unhandled `Debug.LogError`/exceptions — do not disable it with `LogAssert.ignoreFailingMessages`).

## P8 — Windows build
| ID | Check | Pass condition |
|---|---|---|
| P8.1 | `unity build` (Windows64) | Exit code 0 |
| P8.2 | Build output folder | `*.exe`, `*_Data/` and `UnityPlayer.dll` exist |
| P8.3 | Launch smoke: start exe with `-logFile <path> -screen-fullscreen 0`, wait 20 s, then kill | Process still alive at 20 s; `Player.log` contains no `Exception` and no `Shader .* is not supported` lines |
| P8.4 | Noah's 60-second walk-around | Noah says "pass". This is the planned sign-off, **not** an assist |

## P9 — Zero errors
| ID | Check | Pass condition |
|---|---|---|
| P9.1 | `unity logs` after final recompile | 0 lines matching `error CS\d+` |
| P9.2 | Console during the P7 test run (`unity logs` window covering the run, plus test log) | 0 entries of type Error / Exception / Assert. Warnings allowed but listed in the friction log |

## Automatic FAIL gates (check at end)
| ID | Check | FAIL if |
|---|---|---|
| F1 | `Packages/manifest.json` + `Packages/packages-lock.json` | Any Asset Store / third-party MCP package, or Starter Assets |
| F2 | Assist log | > 3 assists, or any person-edited file in `Assets/` |
| F3 | Friction log | Same blocker hit 3 times with no progress |

## Stretch (non-blocking — record result, never affects verdict)
| ID | Check | Pass condition |
|---|---|---|
| S1.1 | eval distinct prefab sources of objects with `NavMeshAgent` | ≥ 3 distinct animal types from the Animal Pack |
| S1.2 | eval `UnityEngine.AI.NavMesh.CalculateTriangulation().vertices.Length` | > 0 |
| S1.3 | Play mode 10 s: each agent's xz displacement; `Animator` current state name | Each ≥ 1 m moved; state is a walk/run clip (not idle/T-pose) |
| S2.1 | eval tree prototype materials | Shader exposes a wind/sway property with non-zero value, and a `WindZone` exists or the shader is self-driven |
| S2.2 | Noah's walk-around | Noah confirms visible sway |
