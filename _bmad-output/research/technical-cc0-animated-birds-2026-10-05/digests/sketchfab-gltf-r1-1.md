# Digest: sketchfab-gltf

Scope: Sketchfab (Data API v3) and glTF sample sources (Khronos glTF-Sample-Assets, three.js example models). Accessed 2026-10-05.

Sources used (7):
- S1 Sketchfab search API: `https://api.sketchfab.com/v3/search?type=models&q=<q>&animated=true&downloadable=true&license=<cc0|by>&sort_by=-likeCount`
- S2 Sketchfab model API: `https://api.sketchfab.com/v3/models/<uid>` (and `/download`)
- S3 three.js repo, examples/models/gltf (GitHub API + raw .glb files, inspected locally): https://github.com/mrdoob/three.js/tree/dev/examples/models/gltf
- S4 three.js webgl_lights_hemisphere.html: https://github.com/mrdoob/three.js/blob/dev/examples/webgl_lights_hemisphere.html
- S5 Unity glTFast 7.0 Features: https://docs.unity3d.com/Packages/com.unity.cloud.gltfast@7.0/manual/features.html
- S6 Khronos glTF-Sample-Assets model-index.json + Duck: https://github.com/KhronosGroup/glTF-Sample-Assets
- S7 Web search for the three.js bird license (discoverthreejs.com): https://discoverthreejs.com/book/first-steps/load-models/

## Claims
- With the Sketchfab search filters animated=true, downloadable=true and license=cc0, these queries returned 0 bird results: "low poly bird", crow, raven, seagull, eagle, duck, pigeon, sparrow and hawk. "owl" and "bird" each returned only a museum scan (Owl 'Zun' wine vessel, artsmia, 65,082 faces). The search turned up no usable CC0 animated bird on Sketchfab. | https://api.sketchfab.com/v3/search | Sketchfab | live API | accessed: 2026-10-05 | high | license
- The same queries with license=by (CC Attribution) returned 17–24 results each, so animated birds on Sketchfab are almost all CC-BY. | https://api.sketchfab.com/v3/search | Sketchfab | live API | accessed: 2026-10-05 | high | license
- Sketchfab's `animationCount` is unreliable as a signal on its own. Static museum scans (the artsmia owl vessel and cabinet) report animationCount 1. | https://api.sketchfab.com/v3/search | Sketchfab | live API | accessed: 2026-10-05 | medium | quality
- Without OAuth, `GET /v3/models/{uid}/download` returns HTTP 401. Download needs a logged-in user, or a person downloading the zip in a browser. | https://api.sketchfab.com/v3/models/797d27b68af3453e865149435df6aa30/download | Sketchfab | live API | accessed: 2026-10-05 | high | other
- "Animated Bird, Pigeon" (dudecon, uid 797d27b68af3453e865149435df6aa30): listed as CC Attribution, 710 faces / 357 verts, 5 animations: gliding, flapping, ground idle (looping), takeoff and landing. The description says "Actual license is Public Domain, see https://ip.tryop.com/" and links the .blend at https://peripheralarbor.com/bird.blend. | https://api.sketchfab.com/v3/models/797d27b68af3453e865149435df6aa30 | Sketchfab/dudecon | 2023-09-22 | accessed: 2026-10-05 | high (the listing says this); unverified (that the PD claim is legally effective) | license
- "Sparrow - Quirky Series" (omabuarts, uid 289e7db66cfa45fbbe7624b8f48f6c8c): CC Attribution, rigged, 18 animations (Attack, Bounce, Clicked, Death, Eat, Fear, Fly, Hit, Idle_A/B/C, Jump, Roll, Run, Sit, Spin/Splash, Swim, Walk), 29 blendshapes, 4 LODs (300–9,000 tris), 16x4 px texture, vertex color. The style is cute or chibi, not naturalistic. | https://api.sketchfab.com/v3/models/289e7db66cfa45fbbe7624b8f48f6c8c | Sketchfab/omabuarts | 2023-02-19 | accessed: 2026-10-05 | high | animation
- "Low Poly Bird (Animated)" (Tnkii, 82ada91f0ac64ab595fbc3dc994a3590): CC-BY, 1,134 faces, "Rigged and animated in Maya 2016"; 1 animation. | https://api.sketchfab.com/v3/models/82ada91f0ac64ab595fbc3dc994a3590 | Sketchfab/Tnkii | 2017-02-05 | accessed: 2026-10-05 | high | animation
- "American Crow V1" (warrenblyth, 99ed138bb3d64d67bc9ddc1400b211a9): CC-BY, 469 faces. The description says the author applied a coworker's rig and imported it into Unity3D to test layering clips. | https://api.sketchfab.com/v3/models/99ed138bb3d64d67bc9ddc1400b211a9 | Sketchfab/warrenblyth | 2016-08-25 | accessed: 2026-10-05 | high | animation
- "Seagulls animated" (deathcow, 73aed843190a4dfda55f2b65cc0f8d63): CC-BY, 808 faces, description "(fbx)", tagged UE4 and unity. "1 seagull" (c6017a4c52a544a6a3f8244c80bb9e2c): CC-BY, 202 faces. | https://api.sketchfab.com/v3/models/73aed843190a4dfda55f2b65cc0f8d63 | Sketchfab/deathcow | 2020-12-06 | accessed: 2026-10-05 | high | quality
- "Pigeon" (FourthGreen, 5884a0f5200c44ceaa7d0399bea577f9): CC-BY, 1,482 faces, tagged animated-rigged, Game-ready, lowpoly. | https://api.sketchfab.com/v3/models/5884a0f5200c44ceaa7d0399bea577f9 | Sketchfab/FourthGreen | 2020-09-02 | accessed: 2026-10-05 | high | quality
- The `archives` field (formats) came back empty from the public model API for every candidate, so source formats beyond the description text are not visible without auth. Sketchfab offers glTF/GLB as a download option for downloadable models (unverified this run). | https://api.sketchfab.com/v3/models/{uid} | Sketchfab | live API | accessed: 2026-10-05 | medium | import
- three.js ships Flamingo.glb (77 KB), Parrot.glb (97 KB) and Stork.glb (77 KB) in examples/models/gltf. All three are morph-target animated (skins: 0). Flamingo: 14 targets, clip "flamingo_flyA_", 337 verts. Parrot: 12 targets, clip "parrot_A_", 497 verts. Stork: 13 targets, clip "storkFly_B_", 358 verts. Each has a single flight loop that animates only `weights`, and the generator is THREE.GLTFExporter. | https://github.com/mrdoob/three.js/tree/dev/examples/models/gltf | three.js | dev branch | accessed: 2026-10-05 | high (inspected binaries) | animation
- three.js credits them as "flamingo by mirada from ro.me". No explicit license for the models was found. The three.js repo is MIT, but nothing retrieved states that the model assets are MIT, CC0 or no-attribution. | https://github.com/mrdoob/three.js/blob/dev/examples/webgl_lights_hemisphere.html | three.js | n/d | accessed: 2026-10-05 | medium | license
- glTFast: Skins are supported. Morph Targets (Blend Shapes) import is supported, with the note "morph targets and vertex positions only". Animation is supported "via legacy Animation System". Mecanim is only partial: "Animation clips can be imported Mecanim compatible, but they won't be assigned and cannot be played back without further work." | https://docs.unity3d.com/Packages/com.unity.cloud.gltfast@7.0/manual/features.html | Unity | v7.0 docs | accessed: 2026-10-05 | high | import
- Khronos glTF-Sample-Assets (150 models) contains no animated bird. "Duck" has 0 animations and 0 skins, and its license is SCEA Shared Source 1.0, not CC0. | https://github.com/KhronosGroup/glTF-Sample-Assets | Khronos | main | accessed: 2026-10-05 | high | license

## Candidates table
| asset | author | species | license | formats | animation type | clips | polys | download path | login? | style | notes |
|---|---|---|---|---|---|---|---|---|---|---|---|
| Animated Bird, Pigeon (797d27b6...) | dudecon | pigeon | CC-BY on Sketchfab; author's description claims Public Domain (unverified) | Sketchfab download (glTF/FBX options not visible without auth); .blend at peripheralarbor.com/bird.blend | skeletal (rigged) | 5: glide, flap, ground idle, takeoff, land | 710 faces | Sketchfab "Download", or the .blend direct link (not fetched) | Sketchfab yes; .blend link possibly not | low-poly, plain; retexturable to other species | Best fit for a no-attribution gate if the PD claim holds. Verify ip.tryop.com and the .blend. |
| Sparrow - Quirky Series (289e7db6...) | omabuarts | sparrow | CC-BY | Sketchfab download | skeletal + 29 blendshapes | 18 (incl. Fly, Idle_A/B/C, Eat, Walk, Death, Hit) | 2,006 faces; LODs 300–9k tris | Sketchfab Download | yes | cute or chibi, vertex color | Richest clip set, but CC-BY and its style may clash with a naturalistic survival game |
| Low Poly Bird (Animated) (82ada91f...) | Tnkii | generic bird | CC-BY | Sketchfab download | skeletal (Maya rig) | 1 | 1,134 faces | Sketchfab | yes | low-poly | Single clip |
| American Crow V1 (99ed138b...) | warrenblyth | crow | CC-BY | Sketchfab | skeletal | 1 viewer anim (author mentions multiple Unity clips) | 469 faces | Sketchfab | yes | low-poly, painted texture | Tested in Unity by the author |
| Seagulls animated / 1 seagull (73aed843 / c6017a4c) | deathcow | seagull | CC-BY | FBX per the description | rigged (assumed from "animated") | 1 | 808 / 202 faces | Sketchfab | yes | very low-poly | Tagged unity/UE4 |
| Pigeon (5884a0f5...) | FourthGreen | pigeon | CC-BY | Sketchfab | rigged (tag animated-rigged) | 1 | 1,482 faces | Sketchfab | yes | low-poly, game-ready | |
| Flamingo / Parrot / Stork .glb | mirada (ro.me), hosted by three.js | flamingo, parrot, stork | Unclear: credit only, no explicit asset license found | GLB (scriptable curl from raw.githubusercontent.com) | morph-target (weights), no skin | 1 flight loop each | 337 / 497 / 358 verts | raw.githubusercontent.com/mrdoob/three.js/dev/examples/models/gltf/<Name>.glb | no | low-poly flat-shaded, close to Quaternius style | Fully scriptable, but the license fails the gate unless clarified. Morph animation in glTFast plays through the legacy Animation component, not Mecanim. Flight only; no idle or ground clip. |
| Duck (Khronos) | Sony/COLLADA | rubber duck | SCEA Shared Source | glTF | none | 0 | – | GitHub | no | – | Not usable |

## Leads
- Verify dudecon's Public Domain claim at https://ip.tryop.com/ and fetch https://peripheralarbor.com/bird.blend directly. If both work, it is a scriptable, no-login, PD, skeletal 5-clip bird that can be retextured as a crow or gull.
- three.js models: search the ro.me / "3 Dreams of Black" project (2011, mirada) for an asset license. Its code was released openly, but the model terms were not found this run.
- On Sketchfab, try license=cc0 with broader queries ("animal animated", "flying") or check the Quaternius Sketchfab account. Quaternius packs are CC0 but may be hosted elsewhere.
- Testing the glTFast Mecanim path: per the docs, clips import as Mecanim-compatible but need manual wiring. For skeletal birds, importing an FBX through Unity's native importer avoids that.

## Looked for but could not find
- Any CC0 animated bird on Sketchfab across 10 species/keyword queries (0 genuine hits).
- File formats per Sketchfab model (`archives` was empty without auth).
- An explicit license for the three.js Flamingo, Parrot and Stork models.
- Any animated bird in Khronos glTF-Sample-Assets.
