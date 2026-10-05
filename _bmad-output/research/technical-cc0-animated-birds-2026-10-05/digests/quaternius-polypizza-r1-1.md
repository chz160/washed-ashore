# Digest: quaternius-polypizza

Scope: Quaternius (quaternius.com, quaternius.itch.io, Poly Pizza mirrors) and Poly Pizza bird search.
Method: fetched pack pages; scraped 13 Poly Pizza searches (bird, crow, raven, seagull, eagle, hawk, owl, duck, sparrow, pigeon, chicken, flamingo, parrot) producing 169 unique model IDs; read each model page's embedded `__SERVER_APP_STATE__` JSON (Title, Creator, Licence, Animated, Type, Tris); downloaded the GLBs of bird-relevant animated hits from `static.poly.pizza/<ResourceID>.glb` and parsed the glTF JSON chunk for animation clip names and skin joint counts.

## Claims
- Quaternius "Ultimate Animated Animal Pack" is CC0, 12 animals, FBX/OBJ/Blend/glTF; Poly Pizza bundle lists Cow, Donkey, Deer, Alpaca, Bull, Fox, Shiba Inu, Stag, Husky, Wolf, White Horse, Horse: no birds | https://quaternius.com/packs/ultimateanimatedanimals.html ; https://poly.pizza/bundle/Animated-Animal-Pack-ILAPXeUYiS | Quaternius / Poly Pizza | n/a | accessed: 2026-10-05 | high | animation
- Quaternius "Farm Animal Pack" is CC0, 7 animals (Llama, Pig, Pug, Sheep, Horse, Cow, Zebra): no chicken or duck | https://quaternius.com/packs/farmanimal.html ; https://poly.pizza/bundle/Farm-Animal-Pack-1kUvRTPLzT | Quaternius / Poly Pizza | n/a | accessed: 2026-10-05 | high | other
- Quaternius itch.io "LowPoly Animated Animals" is CC0, name-your-own-price, FBX/OBJ/Blend, 6 farm animals (tags cow/horse/llama/pig/pug), no birds; download without login | https://quaternius.itch.io/lowpoly-animated-animals | Quaternius (itch.io) | n/a | accessed: 2026-10-05 | medium (6th species not named on page) | other
- No dedicated Quaternius "birds" pack was found by search or on the fetched pages | WebSearch "Quaternius animated birds pack CC0" + pages above | n/a | n/a | accessed: 2026-10-05 | medium (absence claim) | other
- Poly Pizza "Chicken" (Z3RCoCYss4) and "Chick" (LH96IMq0rE) by Quaternius: CC0 1.0, Animated=true, skinned (7 joints), GLB clips Attack, Death, Idle, Idle_Peck, Run. No fly/flap/glide clip. Chicken uploaded 2023-11-15 | https://poly.pizza/m/Z3RCoCYss4 ; https://poly.pizza/m/LH96IMq0rE (+ GLB parsed) | Poly Pizza | 2023-11-15 | accessed: 2026-10-05 | high | animation
- Poly Pizza "Pigeon" (9NGlBTpDEr), "Birb" (gZ2ExU9OAB), "Chicken" (ineV9pU5VL) by Quaternius: CC0 1.0, Animated=true, uploaded 2022-10-23, skinned with only 4 joints, clips Bite_Front, Dance, Death, HitRecieve, Idle, Jump, No, Walk, Yes. Chibi/creature style set, no flight clips | https://poly.pizza/m/9NGlBTpDEr (+ GLB parsed) | Poly Pizza | 2022-10-23 | accessed: 2026-10-05 | high | animation
- Poly Pizza "Hawk Lp Rigged" (RkN6MEbP6g) by Sherkiz: CC-BY 3.0 (attribution required), Animated=true, description "Rigged and Animated Hawk.", 9,956 tris, 58-joint skin, single clip "metarig|Fly", uploaded 2025-06-28 | https://poly.pizza/m/RkN6MEbP6g (+ GLB parsed) | Poly Pizza | 2025-06-28 | accessed: 2026-10-05 | high | license
- All other bird hits from the 13 searches (e.g. Crow 1MIvWQ5Q3R9 "Poly by Google" CC-BY 3.0 OBJ; Flying gull, Sparrow, Eagle, Parrot, Duck, Hummingbird, Western bluebird, Low Poly Bird: Raven) have Animated=false in page JSON | https://poly.pizza/search/bird and per-model pages | Poly Pizza | n/a | accessed: 2026-10-05 | high | animation
- Model pages say "Download ... for free. No login"; static GLB `https://static.poly.pizza/<ResourceID>.glb` returned HTTP 200 to anonymous curl; ResourceID is in the model page's embedded JSON, so a script can download without an API key | https://poly.pizza/m/1MIvWQ5Q3R9 | Poly Pizza | n/a | accessed: 2026-10-05 | high | import
- Model pages offer FBX and glTF downloads for Quaternius animated models (WebFetch of Pigeon page) | https://poly.pizza/m/9NGlBTpDEr | Poly Pizza | n/a | accessed: 2026-10-05 | medium | import
- Poly Pizza API (`api.poly.pizza/v1.1`) returns HTTP 401 without a key; secondary sources state a free account key from poly.pizza/settings/api, sent as `X-Auth-Token` header | curl 401 ; https://github.com/jasonkneen/tiny-world-builder/blob/main/.agents/skills/poly-pizza-api/SKILL.md | Poly Pizza / third party | n/a | accessed: 2026-10-05 | medium (official docs page is JS-rendered; key details from third-party) | other

## Candidates table
| asset | author | species | license | formats | animations (verified from GLB) | rigged | download path | login? | style | notes |
|---|---|---|---|---|---|---|---|---|---|---|
| Chicken (Z3RCoCYss4) | Quaternius | chicken | CC0 1.0 | FBX, glTF/GLB | Attack, Death, Idle, Idle_Peck, Run | yes, 7 joints | static.poly.pizza/9ba093a4-6525-4f3b-927d-98dca72e1e40.glb | no | Quaternius low-poly, 2,140 tris | Ground bird only; no flight. Best style match. |
| Chick (LH96IMq0rE) | Quaternius | chick | CC0 1.0 | FBX, glTF/GLB | same 5 as Chicken | yes, 7 joints | static.poly.pizza/6589c0bb-26b3-413f-9a82-770f99511980.glb | no | Quaternius, 1,924 tris | Same rig as Chicken. |
| Pigeon (9NGlBTpDEr) | Quaternius | pigeon | CC0 1.0 | FBX, glTF/GLB | Bite_Front, Dance, Death, HitRecieve, Idle, Jump, No, Walk, Yes | yes, 4 joints | static.poly.pizza/2ad33f9e-e0d5-4a11-821b-7aafe8d13dd3.glb | no | chibi/creature, 2,024 tris | No flight clip; cartoon "pet" set. |
| Birb (gZ2ExU9OAB) | Quaternius | generic bird | CC0 1.0 | FBX, glTF/GLB | same 9 as Pigeon | yes, 4 joints | static.poly.pizza/05dac745-bdd0-4169-9e64-f497ca21f69a.glb | no | chibi, 2,668 tris | No flight clip. |
| Chicken (ineV9pU5VL) | Quaternius | chicken | CC0 1.0 | FBX, glTF/GLB | same 9 as Pigeon | yes, 4 joints | static.poly.pizza/a0001762-9352-48c3-9abd-be91e42db114.glb | no | chibi, 2,648 tris | Same set as Pigeon. |
| Hawk Lp Rigged (RkN6MEbP6g) | Sherkiz | hawk | **CC-BY 3.0** (fails no-attribution gate) | FBX, GLB | Fly (single clip) | yes, 58 joints | static.poly.pizza/2a7aca61-a8f6-45f6-950e-003d4bfcab45.glb | no | low-poly, 9,956 tris (heavier than Quaternius) | Only verified flying bird found; attribution required. |
| Chicken Guy (b2hbNsaTN0) | J-Toastie | chicken character | CC-BY 3.0 | FBX | not inspected | Animated=true | poly.pizza/m/b2hbNsaTN0 | no | humanoid-ish | Animated per page; clips not confirmed. Probably not a usable wild bird. |

Bottom line: no CC0 Quaternius or Poly Pizza bird with a fly/flap/glide clip was found. The CC0 options are ground birds (Chicken with Idle_Peck) or chibi birds with walk/jump only. The one flying candidate (Sherkiz hawk) is CC-BY 3.0.

## Leads
- Quaternius "Ultimate Monsters" / creature packs may be the source of the 2022-10-23 Pigeon/Birb/Chicken set (4-joint, Bite_Front/Dance/Yes/No clips). Unverified; pack page not fetched.
- Quaternius Patreon "Source" files might contain extra clips. Unverified.
- Sherkiz hawk could be used with CC-BY credit if the attribution gate is relaxed. Check its FBX for additional clips (GLB has only Fly).
- Poly Pizza bat (Quaternius, hNO9XvjlKa, CC0, animated) might stand in as a flying silhouette. Clips not inspected.

## Looked for but could not find
- Any Quaternius pack named for birds, or any Quaternius bird with fly/flap/glide/takeoff/land clips.
- Animated (Animated=true) crow, raven, seagull, eagle, owl, duck, sparrow, flamingo or parrot on Poly Pizza: all hits were static.
- The official Poly Pizza API docs text (the page is JS-rendered); key requirement confirmed only by a 401 response plus third-party docs.
