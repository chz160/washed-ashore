# Digest: fallback-practice

Scope: how shipped games handle ambient birds; cheap techniques that avoid a rigged, animated bird; whether script-rotated wings are an acceptable approach. Research run 2026-10-05, 17 tool calls, 8 sources cited.

## Claims

- keijiro/Boids has **no license**. The GitHub API returns license = none, and the repo root holds only `.gitignore`, `Assets`, `Blender` and `ProjectSettings`, with no LICENSE file. It was created 2014-02-19 and last pushed 2014-02-25 (8 commits). Without a license the code is all-rights-reserved by default, so we should not copy it. Note that a web-search summary claimed it was MIT; the API does not support that. | https://api.github.com/repos/keijiro/Boids (and /contents/) | GitHub / Keijiro Takahashi | 2014-02-25 (last push) | accessed: 2026-10-05 | high | license
- keijiro/Boids is a C# "Flocking behavior simulation (Unity)" from 2014. Its compatibility with Unity 6 is unverified. | https://github.com/keijiro/Boids | GitHub | 2014 | accessed: 2026-10-05 | medium | technique
- NVJOB Simple Boids is **MIT**-licensed and needs Unity 2019.1.8 or later. "Animation of birds, fish and butterflies implemented using shaders": it ships 2 custom shaders, one for birds and butterflies and one for fish. One script drives many flock members. The author says "tens of thousands of birds" would degrade performance. The README does not name a render pipeline. I believe the shaders target the Built-in pipeline and would need porting for URP, but I did not verify this. | https://github.com/nvjob/nvjob-boids | NVJOB (GitHub) | n.d. (32 commits) | accessed: 2026-10-05 | high (license, shader-animated); low (URP) | license / technique
- Vertex-displacement wing flap: build a mask from abs(object-space X) so both wings move the same way, then offset Z (up) by a sine of time. For a bird, add the mask into the sine's phase so the wing bends. Equivalent code: `float mask = abs(vertex.x) - 0.2; float s = sin(_Time.y * 10.0 + mask);`. The tutorial presents this for Shader Graph and as code, and does not mention URP-specific limits. | https://www.cyanilux.com/tutorials/vertex-displacement/ | Cyanilux | 2019-06-14 (updated 2023-01-31) | accessed: 2026-10-05 | high | technique
- A hobbyist made a Unity Shader Graph shader that flaps only the wing geometry, with exposed range (amplitude) and speed properties. The body and wings use separate textures so the shader can target the wings. The author recommends shaders for instanced repeated assets "to increase your games performance". | http://www.hoonaya.com/blog-bird-shader.html | Hoonaya (blog) | 2020-04-29 | accessed: 2026-10-05 | medium | technique
- RDR2 birds vary by habitat and behaviour, as observed by a birder journalist (no developer quotes): "Hawks perch on exposed branches and ducks flush from riverbanks"; vultures descend onto carcasses; eagles and condors soar over peaks. The game has about 200 interactive animal species. | https://www.audubon.org/news/birding-its-1899-inside-blockbuster-american-west-video-game | National Audubon Society (Nicholas Lund) | 2019-01-02 | accessed: 2026-10-05 | medium | practice
- In The Long Dark, crows circle over carcasses in calm weather to draw the player to them, and drop crow feathers below. A group of crows sometimes flies in "V" formation straight across the map. Crows disappear in high wind or snow and leave when the carcass is gone. (Source is a fan wiki, seen only as a search snippet.) | https://thelongdark.fandom.com/wiki/Crow | The Long Dark Fandom wiki | n.d. | accessed: 2026-10-05 | low | practice
- An indie Unity devlog added flocks that "fly around in the sky and also randomly sit on different areas like on trees", noting "a little bit of performance impact". It gives no detail on how they are animated. | https://livingworldproject.itch.io/living-world-project/devlog/1186965/update-0150-devlog-9-bird-flocks | Living World Project (itch.io) | ~2025-12 (page says "286 days ago") | accessed: 2026-10-05 | low | practice
- Rotating a wing object by a sine of time (for example `l_wing01.rotateZ = sin(time*10)*40`) is an established DCC expression idiom, from a Maya forum. This shows that sine-driven rigid wing rotation is common practice, but it is not evidence from a Unity game. | https://simplymaya.com/forum/showthread.php?t=29950 | SimplyMaya forum | n.d. | accessed: 2026-10-05 | low | technique

## Options table

| Technique | Needs animated asset? | Agent difficulty (script-only) | URP OK? | Performance | Code license | Notes |
|---|---|---|---|---|---|---|
| A. Static low-poly mesh with wings as separate child transforms, rotated in C# (`localRotation = Euler(0,0,±sin(t*f)*amp)`) | No. Needs a static mesh split into body plus 2 wings, which can be built from primitives or procedurally in C#. | **Easiest.** Plain C#, no shader, works with stock URP/Lit materials. | Yes (no custom shader) | One transform update per wing per frame; fine for tens to low hundreds of birds. A C# job or a single manager helps at higher counts. No GPU instancing benefit, though the SRP Batcher still applies. | Our own code | The idiom is well known (sine rotation, see SimplyMaya). I found **no shipped-game evidence** that this is used for distant birds, but at distance a rigid flap reads the same as a skinned one. That last point is my belief, not something I verified. |
| B. Vertex-shader flap on a static mesh (mask from abs(x) or vertex colour, sine on Z) | No | Medium. The vertex function must be written in HLSL. Writing a URP `.shader` file by hand avoids authoring Shader Graph from scripts, but the agent has to write correct URP HLSL (lighting includes, SRP Batcher CBUFFER). | Yes if written for URP. Cyanilux gives the maths, and Unity's URP docs show the URP shader structure. | Best at scale: GPU-only and instancing-friendly. Shipped boids assets (NVJOB) use this approach. | Maths from tutorial (no code licence issue); NVJOB shaders MIT | The Cyanilux snippet is short, so porting it into a minimal URP unlit or simple-lit shader is feasible. |
| C. Billboard or two-quad sprite birds (alternating frames, or a V-shaped pair of quads rotated) | No (2 to 4 sprite frames or a flat texture) | Easy to medium. A sprite-sheet flipbook needs a texture; a quad "V" can be rotated in C# as in A. | Yes (URP Unlit or Particles material) | Very cheap. Could use the Particle System with mesh or billboard particles. | n/a | I found no source on this in this run. Suited to **far** flocks only. |
| D. Boids flocking (steering) | Independent of how the bird is rendered | Easy to medium. Can be written from scratch in C#. | Yes (logic only) | Fine up to hundreds with GameObjects; NVJOB warns about tens of thousands. | NVJOB: **MIT** (usable). keijiro/Boids: **no licence, do not copy**. | For a proof of concept, simple circling or waypoint paths may be enough (The Long Dark circling crows, V formation). |
| E. Rigged and animated asset (the baseline) | Yes | Import is easy; finding a CC0 one is the open question. | Yes | Skinned meshes cost more per bird | Depends on the asset | Close birds (perch, then take off) benefit most from it. |

## How games do ambient birds (summary)

The evidence is thin; no developer talk was found. What the sources show:

- **Mostly distant and tied to a purpose.** Crows in The Long Dark circle carcasses as a gameplay signal, fly V formations across the map, and depend on weather. RDR2 birds are habitat-specific and behaviour-driven: they perch on branches, flush from riverbanks, and descend onto carcasses.
- **Perch and flee is the expected close-range behaviour.** Seen in RDR2 (ducks flush) and in an indie Unity devlog (flocks land in trees).
- **Counts.** No source gave per-flock or on-screen counts.

My unverified belief: AAA games use LOD'd skinned birds and swap to cheaper representations at distance. This run found no evidence for or against it.

For a low-poly proof of concept, the fallback answer is: **a rigged asset is not required for distant birds.** Options A (C#-rotated wing children) or B (hand-written URP vertex flap) together with simple circling or boids cover far and mid-range flocks. A rig, or a convincing take-off, matters mainly for close perch-and-flee moments.

## Leads

- NVJOB Simple Boids (MIT): check its bird shader source and whether it is a surface shader (Built-in) or already ported to URP. A Unity 6 URP fork may exist.
- Unity URP docs, "writing-shaders-urp-basic-unlit-structure" (packages @14.0): a template for a hand-written URP vertex-flap shader.
- Shinao/Unity-GPU-Boids and hecomi/UnityECSBoidsSimulation: check their licences if GPU or ECS scale is ever needed.
- GDC Vault search, for example "Horizon Zero Dawn ambient wildlife", "Far Cry ambient birds" or "Ghost of Tsushima birds": not searched for lack of budget.

## Looked for but could not find

- Any dev talk, postmortem or blog post on ambient birds from The Long Dark, RDR2, Firewatch, Far Cry or Valheim. I found only journalism and fan wikis.
- Bird counts per flock or per scene in shipped games.
- Shipped-game or devlog evidence that **script-rotated rigid wings** are used for distant birds. I found only a generic sine-rotation idiom (Maya forum) and Houdini vertex experiments.
- The render pipeline of NVJOB's shaders, and the Unity 6 compatibility of keijiro/Boids.
- Sources on billboard or two-quad bird sprites.
