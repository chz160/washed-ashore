---
title: 'Technical research: Free Unity assets for the Washed Ashore POC'
type: 'technical'
topic: 'Free Unity assets for the Washed Ashore POC'
decision: 'Which free asset sources (terrain, flora, fauna, FP controller) Ruflo agents use for a Unity 6.6 walk-around POC, and whether agents can drive Unity headlessly'
source: 'native run'
status: complete
preset: 'standard (subagents=5)'
validation: 'normal'
claims_verified: 7
claims_unverified: 5
created: '2026-10-04'
updated: '2026-10-04'
---

# Technical research: Free Unity assets for the Washed Ashore POC

**Decision this research serves:** which free asset sources (terrain, flora, fauna, FP controller) Ruflo agents use for a Unity 6.6 walk-around POC, and whether agents can drive Unity headlessly.

## Executive summary

**Verdict: it's feasible, and you don't need a third-party MCP.** Unity's own `unity` CLI (1.0.0-beta.12, already installed here) drives a running Unity 6 Editor directly. It creates the project with the agent bridge already in (`projects create --with-pipeline`), runs C# (`command eval`), enters Play mode, tails logs, and runs PlayMode tests to NUnit XML [29][30][37]. Unity has deprecated its own in-Editor MCP server in favour of this CLI [31].

**The asset stack to give Ruflo is all CC0, with no Asset Store involved:**

| Need | Pick | How an agent gets it |
|---|---|---|
| Terrain shape | A script-generated heightmap (`TerrainData.SetHeights`) | Its own C#; no package needed [6] |
| Terrain textures | **ambientCG** (backup: Poly Haven) | JSON API, then curl zip (verified live) [1][2] |
| Trees, bushes, rocks, grass | **Quaternius Stylized Nature MegaKit** (FBX) | **One manual itch.io download by Noah**, then agents take over [10] |
| Extra photoreal rocks and ferns | Poly Haven models | JSON API, then direct FBX URLs (verified live) [3][5] |
| Animals (stretch goal) | **Quaternius Ultimate Animated Animal Pack** + `com.unity.ai.navigation` | One manual download; NavMesh wander [15][20] |
| First-person controller | **A hand-written CharacterController + Input System script** | Agent writes the .cs and edits manifest.json [23][27] |

**The three findings that decide it:**

1. **No Asset Store package can be fetched without a GUI.** Free packages still need a Unity ID login and a download in the Package Manager window, and the CLI has no download command [9][37]. That rules out Starter Assets and Unity's Terrain Sample packs as agent-sourced inputs.
2. **The only step that needs a person is getting the Quaternius packs.** They're CC0, but distributed through itch.io's download flow, with no static URL [10][37]. Everything after that is headless.
3. **"Playing" isn't proof.** An unfocused Editor can freeze on frame 1 while `unity status` still reports playing. Recompiles and entering Play mode kill eval calls that are still running [30][32]. Verification has to assert movement in code: a PlayMode test that simulates W and checks the player moved [35][36].

**The biggest caveat:** none of the sources reports an AI agent building a terrain FPS end to end in Unity 6. This proof of concept would be the first evidence either way.

## Terrain

**The terrain shape needs no assets.** `TerrainData.SetHeights` takes a normalised `[y,x]` float array, so an editor script run through `command eval` or `-executeMethod` can generate noise-based terrain and paint splatmaps by slope and height [6]. `com.unity.terrain-tools` 5.3.3 (2026-08-19) adds erosion and noise brushes. It's maintained for the 6000.4 and 6000.5 lines, but support for 6000.6 is only inferred [7] (confidence: medium). The POC doesn't need it.

**Ground textures: ambientCG.** It's CC0 1.0 and explicitly allows embedding the files in games with no attribution [1]. The v3 API returns zip URLs at each resolution with no authentication, which this run confirmed with a live call [2]. **Poly Haven** is the CC0 alternative [3]. Its API terms require a User-Agent naming your software, and a credit is needed only when showing content through the live API, not when shipping downloaded files [4]. Both give plain PBR maps, so the agent builds the `TerrainLayer` assets itself.

**Rejected: Unity Terrain Sample Asset Pack.** It's free and supports URP, but only through the Asset Store [8][9].

## Plants

**Primary pick: Quaternius Stylized Nature MegaKit.** It's CC0, with 116 models (40 trees, 35 plants, 27 rocks, plus grass and bushes) in FBX, OBJ and glTF [10]. The free tier has no Unity setup, so agents must create the URP Lit materials and wrap the trees in LODGroup prefabs for terrain painting [10][12] (confidence: medium). The download goes through itch.io, which needs one manual fetch [37].

**Wind is the main visual gap.** In Unity 6.6, Tree Editor trees work only in the Built-in pipeline, which is now deprecated. SpeedTree is the only tree type with documented URP wind. Generic LODGroup trees need a Shader Graph sway shader to move [12] (the "no wind otherwise" part is an inference, confidence: medium). For grass, **Instanced Mesh** detail mode is recommended and works in all pipelines if the shader supports GPU instancing [13]. That's fine for a POC with no wind.

**Supplement: Poly Haven.** 110 CC0 nature models (photoreal, high-poly, no LODs). Its API serves direct FBX and glTF URLs [5][37], which makes it a good scripted source for rocks, ferns and grass clumps, but too heavy for forest trees. **Kenney Nature Kit** is CC0 with a direct zip, but its toy-like style is wrong for a survival game [11]. glTF needs `com.unity.cloud.gltfast`; FBX imports with no extra package [14].

## Animals (stretch goal)

**Pick: Quaternius Ultimate Animated Animal Pack.** It's CC0, with 12 animals (including Deer, Stag, Fox and Wolf), each with more than 12 animations, in FBX and glTF [15][16]. Its low-poly style matches the MegaKit. The rig type isn't stated, and quadrupeds are expected to import as Generic (unverified). Getting it needs the same one manual download [15].

**Making animals wander:** `com.unity.ai.navigation` 2.0.15 (NavMeshSurface, NavMeshAgent) is Released for Unity 6.3 LTS and bakes at runtime or in the editor [20]. Agents can write a wander script that sets random destinations on the NavMesh.

**Excluded:**
- Mixamo has no quadrupeds [18] (confidence: medium).
- Sketchfab downloads need OAuth and the links expire in about 300 seconds [19].
- Kenney Cube Pets is CC0 with a direct zip, but its cube style clashes with the nature assets [17].

## First-person controller and headless import

**Pick: a hand-written controller.** It's a CharacterController plus `com.unity.inputsystem`, added through `Packages/manifest.json`, so there's no login and no EULA to clear [27].

**Starter Assets 196526 is out.** It's free and lists 6000.3 URP, but it's on the Asset Store under a non-standard EULA, it's 76.7 MB, and it pulls in Cinemachine [21]. Related Starter Assets listings are marked deprecated on marketplace.unity.com [22]. **Gold Player** (MIT, installs by git URL) is abandoned [28].

**Headless paths that work** (documented for 6000.3; 6000.6 not explicitly confirmed):
- `-batchmode`, `-createProject` with `-cloneFromTemplate`, `-importPackage` (no dialog) and `-executeMethod` [23].
- `unity assets import` for local .unitypackage files [37].
- Git URLs and scoped registries in manifest.json [27].
- Built-in-to-URP material conversion through `Converters.RunInBatchMode` [26] (confidence: medium).

URP is the Unity 6 default, and the Universal 3D template comes with URP set up [25] (confidence: medium).

**A known batch-mode gotcha:** with `-quit`, scripted `AssetDatabase.ImportPackage` gets killed before its `delayCall` runs. Call `EditorApplication.Exit()` instead [24] (confidence: medium).

## Agent control of the Editor and verifying Play mode

**Unity CLI (recommended).** Unity staff posted beta.12 on 2026-10-01, and its release notes name Unity 6.6 [29]. It talks to the Editor through `com.unity.pipeline` with no MCP [30], and Unity says it's faster and uses fewer tokens than MCP [31]. `unity mcp configure claude` exists if an MCP surface is ever wanted [31][37].

The known failure modes from Unity's own skill [30]:
- Compile errors at startup put the Editor in Safe Mode, and the CLI exits with code 7.
- An unfocused Editor freezes on frame 1.
- In agent sandboxes the CLI may not see the running Editor.

One third-party hands-on test of beta.3 saw `eval` freeze the Editor, and recompiles and Play-mode entry kill calls in flight [32] (confidence: medium; an older beta).

**Community MCP fallbacks:**
- **CoplayDev/unity-mcp:** MIT, about 14.7k stars, v10.0.0 on 2026-06-30, 48 tools [33]. There are open issues about disconnects (confidence: low–medium).
- **IvanMurzak/Unity-MCP:** Apache-2.0, 70+ tools [34].

**Verifying without screenshots:** UTF PlayMode tests with `-runTests -testPlatform PlayMode -testResults` give NUnit XML and a non-zero exit code on failure [35]. The CLI wraps this as `unity test --mode PlayMode` [37]. Input System's `InputTestFixture` simulates keyboard and mouse so a test can assert the player moved [36]. The UTF docs say nothing about `-nographics`, and rendering isn't exercised that way, so the terrain still needs a human look or a capture.

## Cross-dimension insights

- **Asset Store gating is the common thread.** It removes Unity's best free terrain, plants and controller packs at once [8][9][21]. The result is a stack that's entirely CC0 and community-sourced, plus one manual download step.
- **Choosing URP is decided for us, and it creates the wind gap.** URP is the Unity 6 default [25], Tree Editor trees are Built-in only and Built-in is deprecated [12], and Quaternius assets ship without URP setup in the free tier [10]. Agents will have to write shaders or materials. That's a good test of the technical-artist agent, but also the step most likely to fail.
- **The CLI's test runner is the verification spine.** PlayMode tests [35][37] avoid the frame-1 freeze problem [30]. The qa-lead agent can sign off on the NUnit XML result instead of a screenshot.

## Recommendations

1. **Toolchain:** use the `unity` CLI with `com.unity.pipeline` and no third-party MCP. Create the project with `unity projects create --template <URP template id> --with-pipeline --editor-version 6000.6.4f1` [29][30][37] (confidence: high; the template id isn't confirmed yet). *Feeds the architecture constraints.*
2. **Assets:** use only the CC0 set in the summary table. Noah downloads two Quaternius zips once and drops them in a `vendor/` folder. Agents script everything else: the ambientCG and Poly Haven API fetches, FBX import, URP materials, LODGroup prefabs, and terrain painting [1][2][5][10][15] (confidence: high for licenses, medium for MegaKit prefab work). *Feeds the brief's feasibility section.*
3. **Controller:** hand-written CharacterController + Input System. Don't use Starter Assets [21][27] (confidence: medium–high).
4. **Definition of done for qa-lead:** a PlayMode test that loads the scene, simulates W, and asserts the player moved and stays grounded, passed by `unity test --mode PlayMode` [35][36][37]. Add a frame-advance check if the live Editor is used [30].
5. **Add to the Ruflo studio:** the studio agents rely on the `unity-cli` skill. Run `unity skill install claude` or `unity setup claude` in the studio so they get Unity's current skill, which includes the playmode-verification-loop reference [29][30] (confidence: medium).

## Open questions

- **Exact URP template id for `--template`:** run `unity templates` locally.
- **Rig type and clip names in the Quaternius FBX files:** inspect them after the download.
- **Whether itch.io downloads could be scripted** (the butler tool needs an API key): only worth checking if the manual step becomes a recurring burden.
- **Licensing for the Unity CLI beta and batch mode on a Personal license:** not researched.
- **A sway shader for LODGroup trees in URP:** this is a build task for the technical-artist agent, not a research question.
- **6000.6-specific confirmation of the CLI flags:** the docs read were for 6000.3. The proof of concept itself will confirm them.

## Source appendix

| # | Supports | Publisher | Pub date | Accessed | Confidence |
|---|---|---|---|---|---|
| 1 | ambientCG CC0, game use | [ambientCG](https://docs.ambientcg.com/license/) | n/d | 2026-10-04 | high |
| 2 | ambientCG v3 API zip URLs | [ambientCG](https://ambientcg.com/api/v3/assets?type=material&q=forest&include=downloads) | live | 2026-10-04 | high (verified live) |
| 3 | Poly Haven CC0 | [Poly Haven](https://polyhaven.com/license) | n/d | 2026-10-04 | high |
| 4 | Poly Haven API terms | [Poly Haven](https://github.com/Poly-Haven/Public-API/blob/master/ToS.md) | n/d | 2026-10-04 | high |
| 5 | Poly Haven model API and files | [Poly Haven](https://api.polyhaven.com/assets?type=models&categories=nature) | live | 2026-10-04 | high (verified live) |
| 6 | TerrainData.SetHeights | [Unity](https://docs.unity3d.com/6000.2/Documentation/ScriptReference/TerrainData.SetHeights.html) | 6000.2 docs | 2026-10-04 | high |
| 7 | Terrain Tools 5.3.3 | [needle-mirror](https://github.com/needle-mirror/com.unity.terrain-tools/blob/master/CHANGELOG.md) | 2026-08-19 | 2026-10-04 | medium |
| 8 | Terrain Sample Asset Pack | [Unity Asset Store](https://assetstore.unity.com/packages/3d/environments/landscapes/terrain-sample-asset-pack-145808) | 2026-09-17 | 2026-10-04 | high |
| 9 | Asset Store needs a login and Package Manager | [Unity Docs](https://docs.unity.com/en-us/asset-store/downloads/purchase-asset-packages) | current | 2026-10-04 | high |
| 10 | Quaternius MegaKit CC0, contents, formats | [Quaternius](https://quaternius.com/packs/stylizednaturemegakit.html) | n/d | 2026-10-04 | high (license), medium (tiers) |
| 11 | Kenney Nature Kit | [Kenney](https://kenney.nl/assets/nature-kit) | 2020 | 2026-10-04 | high |
| 12 | Tree types and pipelines, Built-in deprecated | [Unity](https://docs.unity3d.com/Manual/terrain-Trees.html) | 6000.6 docs | 2026-10-04 | high |
| 13 | Detail (grass) modes | [Unity](https://docs.unity3d.com/Manual/terrain-Grass.html) | 6000.6 docs | 2026-10-04 | high |
| 14 | glTFast | [Unity](https://docs.unity3d.com/Packages/com.unity.cloud.gltfast@6.20/manual/index.html) | v6.20 | 2026-10-04 | high |
| 15 | Quaternius animated animals | [Quaternius](https://quaternius.com/packs/ultimateanimatedanimals.html) | 2021-07 | 2026-10-04 | high |
| 16 | Animal list, CC0 | [Poly Pizza](https://poly.pizza/bundle/Animated-Animal-Pack-ILAPXeUYiS) | 2022-07-09 | 2026-10-04 | high |
| 17 | Kenney Cube Pets | [Kenney](https://kenney.nl/assets/cube-pets) | 2026 | 2026-10-04 | medium |
| 18 | Mixamo has no quadrupeds | [Adobe Community](https://community.adobe.com/questions-696/quadrupeds-in-mixamo-589917) | 2021-01-22 | 2026-10-04 | medium |
| 19 | Sketchfab needs OAuth | [Sketchfab](https://sketchfab.com/developers/download-api/downloading-models) | n/d | 2026-10-04 | high |
| 20 | AI Navigation 2.0.15 | [Unity](https://docs.unity3d.com/6000.3/Documentation/Manual/com.unity.ai.navigation.html) | 6000.3 docs | 2026-10-04 | high |
| 21 | Starter Assets 196526 | [Unity Asset Store](https://assetstore.unity.com/packages/essentials/starter-assets-character-controllers-urp-196526) | n/d | 2026-10-04 | medium |
| 22 | Starter Assets listings deprecated | [Unity Marketplace](https://marketplace.unity.com/packages/essentials/starter-assets-character-controllers-urp-267961) | n/d | 2026-10-04 | medium |
| 23 | Editor command-line arguments | [Unity](https://docs.unity3d.com/6000.3/Documentation/Manual/EditorCommandLineArguments.html) | 6000.3 docs | 2026-10-04 | high |
| 24 | ImportPackage and `-quit` gotcha | [Unity Discussions](https://discussions.unity.com/t/import-package-not-running-when-in-batchmode/1544813) | 2024-11 | 2026-10-04 | medium |
| 25 | URP is the Unity 6 default | [Unity](https://docs.unity3d.com/6000.0/Documentation/Manual/urp/creating-a-new-project-with-urp.html) | 6000.0 docs | 2026-10-04 | medium |
| 26 | RP Converter in batch mode | [Unity](https://docs.unity3d.com/6000.0/Documentation/Manual/urp/features/rp-converter.html) | 6000.0 docs | 2026-10-04 | medium |
| 27 | manifest.json git URLs and scoped registries | [Unity](https://docs.unity3d.com/6000.3/Documentation/Manual/upm-scoped-use.html) | 6000.3 docs | 2026-10-04 | medium |
| 28 | Gold Player abandoned | [GitHub/Hertzole](https://github.com/Hertzole/gold-player) | n/d | 2026-10-04 | high |
| 29 | Unity CLI beta.12 release | [Unity Discussions (staff)](https://discussions.unity.com/t/unity-cli-1-0-0-beta-12-is-rolling-out/1738085) | 2026-10-01 | 2026-10-04 | high |
| 30 | CLI drives the Editor through the Pipeline package; gotchas | [Unity Technologies](https://github.com/Unity-Technologies/skills/blob/main/skills/unity-cli/SKILL.md) | main | 2026-10-04 | high |
| 31 | Unity MCP deprecated, replaced by the CLI | [Unity Docs](https://docs.unity.com/en-us/unity-cli/replace-mcp-server-unity-cli) | ~2026-09 | 2026-10-04 | high |
| 32 | eval freezes and kills in-flight calls (beta.3) | [PerfLint](https://perflint.dev/blog/unity-cli-eval-mcp-guide/) | 2026 | 2026-10-04 | medium |
| 33 | CoplayDev/unity-mcp | [GitHub](https://github.com/CoplayDev/unity-mcp) | 2026-06-30 | 2026-10-04 | high |
| 34 | IvanMurzak/Unity-MCP | [GitHub](https://github.com/IvanMurzak/Unity-MCP) | n/d | 2026-10-04 | high |
| 35 | UTF command-line options | [Unity](https://docs.unity3d.com/Packages/com.unity.test-framework@1.4/manual/reference-command-line.html) | 1.4 docs | 2026-10-04 | high |
| 36 | InputTestFixture | [Unity](https://docs.unity3d.com/Packages/com.unity.inputsystem@1.14/manual/Testing.html) | 1.14 docs | 2026-10-04 | high |
| 37 | Local CLI command list and live API checks | [Lead spot-check digest](digests/verify-r1-1.md) | 2026-10-04 | 2026-10-04 | high |

## Staleness map

Fastest-ageing claims (technical pack: versions ≤ 1 month, AI-adjacent landscape ≤ 3 months):

- **Unity CLI capabilities and the MCP deprecation [29][30][31][32]:** re-check by **2026-11-01**. This is beta software that ships every few weeks.
- **Package versions (Terrain Tools [7], AI Navigation [20], Asset Store listings [8][21][22]):** re-check by 2026-11-04.
- **Community MCP state [33][34]:** re-check by 2027-01-04.
- **Licenses (ambientCG, Poly Haven, Quaternius, Kenney) [1][3][10][15]:** stable CC0. Re-check only before shipping.

The earliest re-check is **2026-11-01**, for the Unity CLI claims.
