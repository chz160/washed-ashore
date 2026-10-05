# Digest: fauna

## Claims
- claim: Quaternius "Ultimate Animated Animal Pack" is CC0, ships FBX, OBJ, Blend and glTF, has 12 animals each with "more than 12 unique animations (Attack, Death, Kicks, Gallops, Walk, Jump and many more!)", released July 2021. | source: https://quaternius.com/packs/ultimateanimatedanimals.html | publisher: Quaternius (official) | pub_date: 2021-07 | accessed: 2026-10-04 | confidence: high | class: license
- claim: The 12 animals in that pack are Cow, Donkey, Deer, Alpaca, Bull, Fox, Shiba Inu, Stag, Husky, Wolf, White Horse, Horse. Poly Pizza lists it as Public Domain (CC0), updated 2022-07-09. | source: https://poly.pizza/bundle/Animated-Animal-Pack-ILAPXeUYiS | publisher: Poly Pizza (mirror of Quaternius) | pub_date: 2022-07-09 | accessed: 2026-10-04 | confidence: high | class: quality
- claim: On the official Quaternius page the Download button points to an in-page anchor (#inline), not a plain file URL, so the real download target (probably a JS popup or external host) could not be resolved with a static fetch. A headless agent may need the human to fetch the zip once. | source: https://quaternius.com/packs/ultimateanimatedanimals.html | publisher: Quaternius | pub_date: n/a | accessed: 2026-10-04 | confidence: med | class: import
- claim: Whether Poly Pizza needs a login to download is unclear. The fetch only saw a login link in the header, which is not proof that downloading needs one. The guessed API docs URL (poly.pizza/docs/api) returned 404. | source: https://poly.pizza/bundle/Animated-Animal-Pack-ILAPXeUYiS | publisher: Poly Pizza | pub_date: n/a | accessed: 2026-10-04 | confidence: low | class: import
- claim: Quaternius "LowPoly Animated Animals" on itch.io is CC0 and "name your own price" (free). It contains farm animals (cow, horse, llama, pig, pug and others; 6 total) with Death, Idle, Jump, Run and Walk listed. Some commenters report finding only Idle and Death. It ships FBX, OBJ and Blend in a single zip of 6.6 MB. | source: https://quaternius.itch.io/lowpoly-animated-animals | publisher: Quaternius on itch.io | pub_date: n/a | accessed: 2026-10-04 | confidence: med | class: license
- claim: The itch.io page text suggested logging in for download. This is unverified: on itch.io, free downloads usually go through a JS "download" flow. Treat the headless download as uncertain. | source: https://quaternius.itch.io/lowpoly-animated-animals | publisher: itch.io | pub_date: n/a | accessed: 2026-10-04 | confidence: low | class: import
- claim: Kenney "Cube Pets" is CC0, animated, with 24 files and a direct zip download (no login): https://kenney.nl/media/pages/assets/cube-pets/44e58e945f-1774520254/kenney_cube-pets_1.0.zip. The page mentions a 2.0 "complete remake, added animals & animations". The file formats inside (FBX/GLB) were NOT confirmed. | source: https://kenney.nl/assets/cube-pets | publisher: Kenney (official) | pub_date: 2026 | accessed: 2026-10-04 | confidence: med (license/URL high; formats low) | class: import
- claim: Mixamo does not support quadrupeds or animals today. Its auto-rigger and library are biped-only. Mixamo offered dog, horse and wolf motions in 2010, but Adobe dropped them. | source: https://community.adobe.com/questions-696/quadrupeds-in-mixamo-589917 | publisher: Adobe Community forum | pub_date: 2021-01-22 | accessed: 2026-10-04 | confidence: med (forum source, consistent with 2010 PR Newswire headline seen in search) | class: other
- claim: The Sketchfab Download API requires an authenticated user with an OAuth bearer token. It returns temporary links (about 300 s expiry) to a glTF zip or USDZ, so it is not anonymous and needs a human-provisioned token. | source: https://sketchfab.com/developers/download-api/downloading-models | publisher: Sketchfab (official) | pub_date: n/a | accessed: 2026-10-04 | confidence: high | class: import
- claim: The AI Navigation package is `com.unity.ai.navigation`, version 2.0.15, status Released for Unity 6.3 LTS. It provides NavMeshSurface, NavMeshAgent, NavMeshObstacle, NavMeshModifier and NavMeshLink, and supports navmesh building at runtime and in the editor. | source: https://docs.unity3d.com/6000.3/Documentation/Manual/com.unity.ai.navigation.html | publisher: Unity (official) | pub_date: n/a | accessed: 2026-10-04 | confidence: high | class: version
- claim: The package manual (2.0 branch) also shows version 2.0.15 with NavMeshSurface and NavMeshAgent as the core components. | source: https://docs.unity3d.com/Packages/com.unity.ai.navigation@2.0/manual/index.html | publisher: Unity | pub_date: n/a | accessed: 2026-10-04 | confidence: high | class: version

## Candidate summary table
| Source | License | Formats | Animations | Rig | Pipelines | Headless import method | Login? | Style | Notes |
|---|---|---|---|---|---|---|---|---|---|
| Quaternius Ultimate Animated Animal Pack | CC0 | FBX, OBJ, glTF, Blend | 12+ per animal (Walk, Gallop, Jump, Attack, Death, Idle likely) | Unverified; quadrupeds would import as Generic | Unverified; low-poly flat-color materials are probably easy to remap to URP Lit | Drop the FBX into Assets/; Unity imports FBX natively. Get the zip once, possibly by hand. | Official page: unclear (#inline JS). Poly Pizza: unclear. | Low-poly | Best wildlife fit: Deer, Stag, Fox, Wolf. Best match for low-poly nature packs. |
| Quaternius LowPoly Animated Animals (itch) | CC0 | FBX, OBJ, Blend | Idle, Walk, Run, Jump, Death (disputed) | Unverified (Generic) | Unverified | itch zip (6.6 MB) | Possibly | Low-poly | Farm animals only. Weak fit for wildlife. |
| Kenney Cube Pets | CC0 | Unverified (Kenney usually ships FBX/GLB/OBJ) | Animated (clip list unverified) | Unverified | Unverified | Direct zip URL via curl | No | Cube/voxel-ish, cute | Style clash with a nature POC. Fallback only. |
| Sketchfab CC0/CC-BY animated | Per model | glTF zip, USDZ | Per model | Per model | Needs a glTF importer in Unity | REST API with OAuth token | Yes (OAuth) | Mixed | Fails the GUI-free and no-login gate without a human-supplied token. |
| Mixamo | Adobe terms | n/a | Humanoid only | Humanoid | n/a | n/a | Adobe login | n/a | No animals. Excluded. |
| Unity Asset Store free animals | Asset Store EULA | .unitypackage | Varies | Varies | Varies | Needs a Unity ID "add to My Assets" step | Yes | Varies | Not researched this round (budget). Likely blocked for headless use. |

Simplest viable ambient wildlife approach: add `com.unity.ai.navigation` (2.0.x) to Packages/manifest.json. Bake a NavMeshSurface over the terrain, at edit time via script or at runtime. Give each animal prefab a NavMeshAgent plus a small wander script that calls SetDestination to a random NavMesh.SamplePosition point, and an Animator that blends Idle and Walk by agent speed. Rig type and Animator wiring for the Quaternius FBX are unverified, so the importer may need `animationType = Generic` set by AssetPostprocessor or ModelImporter script.

## Leads worth chasing
- Resolve the actual download URL behind the Quaternius #inline button (likely a Google Drive or similar host) or Poly Pizza's per-model GLB/FBX links. This decides whether the agent can fetch headlessly.
- Confirm the Quaternius FBX clip names, the rig (Generic), and whether embedded materials import cleanly into URP.
- Kenney Cube Pets 2.0: confirm formats and clip list. Check the archive.org Kenney mirrors as no-login alternatives.
- glTF route: confirm the Unity glTFast package name and Unity 6 status if Poly Pizza or Sketchfab GLBs are used.
- Poly Pizza API: find the correct docs URL and auth requirement (the /docs/api guess 404'd).
- Birds and fish: Quaternius likely has other packs (for example animated fish or birds). Not verified this run.

## Looked for but could not find
- A direct, login-free file URL for the Quaternius Ultimate Animated Animal Pack.
- Poly Pizza API documentation.
- Rig type and URP/Unity 6 compatibility statements from any animal-pack publisher. None of the pages read state them.
- Free animated birds, rabbits and fish from a primary source (not searched; out of budget).
- Unity Asset Store free animal packs and their headless obtainability (not searched).
