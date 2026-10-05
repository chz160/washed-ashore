---
title: 'Technical research: CC0 animated birds for the Washed Ashore POC'
type: 'technical'
topic: 'CC0 animated birds'
decision: 'Which free animated bird asset the wildlife pod uses'
source: 'native run'
status: complete
preset: 'quick+ (subagents=4)'
validation: 'normal'
claims_verified: 4
claims_unverified: 2
created: '2026-10-05'
updated: '2026-10-05'
---

# Technical research: CC0 animated birds for the Washed Ashore POC

**Decision this research serves:** which free animated bird asset the wildlife pod uses.

## Executive summary

**Pick two birds, one for each job.**

- **Close birds on the ground or in trees:** the **SoltorchGames American Robin**. It's a hand-animated, low-poly wild songbird with 20 clips (Fly, Flutter, Idle, Hop, PeckGround, Preen). It's free for commercial use with **no credit required** [1][V].
- **Birds flying overhead:** the **"Animated Bird, Pigeon" by Paul Spooner (dudecon)**. It's **public domain** (verified [3][V]), has 394 faces (Sketchfab lists 710; Blender count 394), a skeletal rig, and **glide, flap, takeoff and land** clips [2]. It can be downloaded by script [4][V]. Recolour it dark and, at a distance, it reads as a crow or raven.

**The three findings that decide it:**

1. **There's no strictly CC0 animated *flying* bird in a Quaternius-like style.** Quaternius has no bird pack [5]. Its CC0 birds on Poly Pizza (Chicken, Chick, Pigeon, Birb) have no fly, flap or glide clip [5]. Sketchfab returned **no CC0 animated birds** across 10 species queries [2]. The free flying birds that do exist are public domain, custom no-attribution, or CC-BY.
2. **Both picks pass the hard requirements. Neither is a perfect fit.** The Robin has no glide, takeoff or land, and its license bans reselling the assets as assets, so it isn't strictly CC0 [1]. The Pigeon is a `.blend` only, so it needs an export step. Blender 5.2.2 is installed locally, so `blender -b` can do that export headlessly [V].
3. **Distant flocks don't need a rig at all.** Wings rotated by a C# script, or a URP vertex-shader flap, plus circling or boids, are standard ways to do flocks [6][7]. That's the backup plan if either asset causes trouble.

**The biggest caveat:** none of the sources gives a bird count or density used by shipped games [7], so the wildlife spec's Phase 0 has to set bird numbers by the same research-plus-feel-check method as the other animals.

## Candidates and screen

| Candidate | License | Flies? | Clips | Style | How to get it | Verdict |
|---|---|---|---|---|---|---|
| **SoltorchGames American Robin** [1] | Custom: commercial use OK, credit optional, no resale of the assets | Yes (Fly, Flutter) | 20 clips, no glide/takeoff/land | Hand-animated low-poly, 1,424 tris | itch.io $0 download, **one manual click** (scripting it unverified) | **Finalist** |
| **dudecon "Animated Bird, Pigeon"** [2][3] | **Public domain** (author's 2014 dedication) | Yes | 5: glide, flap, ground idle, takeoff, land | Low-poly, 394 faces (Sketchfab lists 710; Blender count 394) | Direct curl of `bird.blend` [4], then a Blender headless export to FBX | **Finalist** |
| Quaternius Chicken and Chick (Poly Pizza) [5] | CC0 | **No** | Idle, Idle_Peck, Run, Attack, Death | Exact Quaternius style | Anonymous GLB from static.poly.pizza | Cut: can't fly (optional ground filler) |
| OpenGameArt low-poly Pigeon (mujtaba-io) [8] | CC0 | Not verified | Clip names not listed | Low-poly, untextured | .blend only | Cut: no confirmed fly clip |
| Gobkit Duck [8] | CC0 | **No** | idle, attack, dead, walk | Low-poly | Direct GLB | Cut |
| Sherkiz "Hawk Lp Rigged" [5] | **CC-BY 3.0** | Yes (1 clip) | Fly | About 10k tris | Poly Pizza | Cut: needs attribution |
| omabuarts Sparrow, warrenblyth Crow, deathcow Seagulls [2] | **CC-BY** | Yes | Up to 18 | Mixed, some chibi | Sketchfab, OAuth needed | Cut: needs attribution |
| three.js Flamingo, Parrot, Stork [2] | **No stated license** | Morph-target flight loop | 1 | Close to Quaternius | Raw GitHub | Cut: license, and glTFast morph clips need the legacy Animation component [9] |

## Decision matrix (weights agreed at the plan gate; scores 1–5)

| Criterion (weight) | Robin | Pigeon |
|---|---|---|
| Low-poly Quaternius-like style (5) | 4 | 3 (untextured/flat; recolour needed; style detail unverified) |
| Wild species for a survival wilderness (4) | 4 (songbird) | 2 as a pigeon, about 4 recoloured as a distant crow |
| Range of animations (3) | 4 (20 clips, no glide/takeoff/land) | 5 (all the flight phases) |
| CC0 rather than CC-BY (3) | 3 (custom no-attribution) | 5 (public domain) |
| Fewest steps to get it (2) | 3 (manual itch click; FBX ships) | 4 (curl, then Blender CLI export) |
| Several species in one pack (2) | 1 | 1 |
| **Weighted total (out of 95)** | **65** | **63** (about 71 if the recolour-as-crow score is counted) |

**The scores are too close to call, and the two assets do different jobs, so take both.** The Robin wins for birds seen up close; the Pigeon wins for anything airborne.

## How games do ambient birds

The evidence is thin; no GDC talks or postmortems were found.
- **The Long Dark** (fan wiki only): crows circle carcasses as a signal to the player, and birds fly in V formations and vanish in bad weather [7].
- **Red Dead Redemption 2** (Audubon article, no developer quotes): birds are placed by habitat, perch, flush from riverbanks and descend on carcasses [7].
- **Living World Project** (a devlog): distant flocks are cheap sprites or simple meshes [7].

Useful patterns for Washed Ashore: **distant circling flocks**, **perched birds that flush when the player gets close**, and **crows over points of interest**.

**Cheap techniques that skip the rig:**
- **Script-rotated wing children:** the easiest for an agent, and it works with stock URP. It isn't backed by any shipped-game source.
- **A URP HLSL vertex flap** using Cyanilux's maths [6].
- **NVJOB Simple Boids** is MIT licensed, but whether it works in URP is unverified [7].
- **keijiro/Boids has no license**, so don't copy its code [7].

## Recommendations

1. **Add both birds to the wildlife spec's Phase 0 as the approved sources** (confidence: high for licenses and access; medium for how they look next to Quaternius).
   - **Robin:** you download the zip from itch.io into `vendor/`.
   - **Pigeon:** agents `curl` `bird.blend`, then run `blender.exe -b bird.blend --python-expr "<export FBX>"`. The export script goes in the project, under `Assets/Editor/` or `tools/`, so it can be re-run.
2. **Bird behaviours** (separate from the land-animal limits):
   - 1–2 distant circling flocks of recoloured Pigeons, 4–8 birds each.
   - A handful of perched or ground Robins that flush (Fly clip) when the player comes within a threshold.
   - Birds count toward the "animals in view" sighting metric only if the brief says so.
3. **Backup plan:** if the Pigeon export or rig breaks, use script-rotated wings on the Pigeon mesh for the flocks. Don't spend more than 3 attempts on it.
4. **Keep the licensing honest:** keep the Robin's license file next to the asset. If you ever want a strictly CC0 project, replace the Robin.

## Open questions

- **Whether the itch.io $0 download can be scripted.** It's partly proven, but the file-link request failed with "invalid key". A manual click is fine for now.
- **What the Pigeon's material and texture look like after export,** and whether its rig imports cleanly as Generic in Unity. Check during the pod.
- **The exact clip names for the Robin's FBX:** read them after the download.
- **Bird density targets:** no source gives one. Set them through the wildlife spec's Phase 0.

## Source appendix

| # | Supports | Publisher | Pub date | Accessed | Confidence |
|---|---|---|---|---|---|
| 1 | Robin license, clips, formats | [SoltorchGames (itch.io)](https://soltorchgames.itch.io/animated-low-poly-bird-sample) | ~2026-09 | 2026-10-05 | high (verified) |
| 2 | Sketchfab search (no CC0), dudecon Pigeon clips, CC-BY alternatives, three.js birds | [Sketchfab API](https://api.sketchfab.com/v3/models/797d27b68af3453e865149435df6aa30); [digest](digests/sketchfab-gltf-r1-1.md) | live | 2026-10-05 | high |
| 3 | Public-domain dedication | [Peripheral Arbor](https://ip.tryop.com/) | 2014-08-05 | 2026-10-05 | high (verified) |
| 4 | Pigeon .blend download | [Peripheral Arbor](https://peripheralarbor.com/bird.blend) | live | 2026-10-05 | high (verified) |
| 5 | No Quaternius birds; Poly Pizza clip parsing; Sherkiz hawk CC-BY | [Poly Pizza](https://poly.pizza/m/Z3RCoCYss4); [digest](digests/quaternius-polypizza-r1-1.md) | live | 2026-10-05 | high |
| 6 | Vertex-flap maths | [Cyanilux](https://www.cyanilux.com/tutorials/vertex-displacement/) | 2019, upd. 2023 | 2026-10-05 | high |
| 7 | Game practice, boids licenses | [keijiro/Boids (GitHub API)](https://api.github.com/repos/keijiro/Boids); [digest](digests/fallback-practice-r1-1.md) | 2014 / various | 2026-10-05 | medium |
| 8 | OpenGameArt and Gobkit candidates | [OpenGameArt](https://opengameart.org/content/low-poly-3d-pigeon-model-rigged-animated-untextured); [digest](digests/cc0-libraries-r1-1.md) | 2024 / various | 2026-10-05 | medium |
| 9 | glTFast morph-target playback limits | [Unity](https://docs.unity3d.com/Packages/com.unity.cloud.gltfast@7.0/manual/features.html) | v7.0 docs | 2026-10-05 | high |
| V | Lead spot-checks (license, download, Blender CLI, vendor scan) | [verify digest](digests/verify-r1-1.md) | 2026-10-05 | 2026-10-05 | high |

## Staleness map

- **Asset licenses and downloads [1][3][4]:** re-check before any public release. The Robin was published recently and its terms could change.
- **Poly Pizza and Sketchfab availability [2][5]:** re-check by **2027-04-05** (6-month ecosystem window).
- **The earliest re-check is 2027-04-05.** Licenses are stable unless the authors change them.

## Post-run verification (2026-10-05, both files downloaded and opened in Blender 5.2.2 headless)

- **Pigeon:** clips **Flapping, Gliding, Takeoff, Landing, Standing Idle**. The mesh has 394 faces, so the 710 from Sketchfab is corrected. The texture is 512 px and packed into the file. The rig is a Rigify rig with 348 bones, 76 of which deform the mesh. **Export with "only deform bones"** and leave out the `metarig` and `WGT-*` helper objects.
- **Robin:** 22 clips: Call ×3, Flutter, Fly ×2, Hop ×3, Idle ×3, PeckGround ×3, Preen ×3, ScratchGround ×3, Walk. 738 faces, a 53-bone armature, a texture PNG, and an FBX included.
- **The Robin's license bans redistributing the files themselves.** Shipping the bird inside a game is fine. **Don't push the Robin's source files to a public git repo.** Keep `vendor/` and its imported asset folder out of a public repo, or keep the repo private.
