**Feature:** try a photo-real player body in place of the low-poly Farmer, and set up the pipeline for future base bodies with clothing and gear layers. The work started as a planning pod on 2026-10-22 and was put on hold so #43 could finish. Nothing has been imported.

## Noah's direction (2026-10-22)

- "the assets we have in place right now will be replaced by assets that are similar to this soon". The low-poly kit is temporary.
- "This is just a placeholder for later but I wanted to try a photo-real type model."
- "in the future we'll make naked human models anyway because we'll need different clothes and gear".
- Walk and run in every direction come from the Unity side. Meshy's forward-only walk and run clips are reference only.
- Editing the model in Blender is fine, for example splitting off the head.
- Generating the base body and its clothing with the Meshy API is an option. Each generation needs Noah's OK, because it spends credits.

## Source model (placeholder)

Meshy AI "Weary Survivor", A-pose biped. Noah's exports are in his Downloads folder. Use `Meshy_AI_Weary_Survivor_A_Pose_biped_rigged.zip` (the 18:19 re-export).

- **`Meshy_Merged_Animations.fbx`** is enough: it has the same mesh and rest pose as `Character_output.fbx`, plus all 20 clips. The rigged file isn't needed, because the skeleton gets replaced by ours either way.
- **Mesh:** one mesh with one material, 31,114 triangles (5.7 times the Farmer's 5,476). There's no separate head and no hat. It has hair, and a large backpack fused to the back.
- **Skeleton:** 22 Mixamo-named bones with no fingers. Our clips use the shared 79-bone skeleton, which has fingers.
- **Textures:** 4096 base colour, metallic and roughness. Metallic is nearly empty, and there's no normal map.
- **Size:** 1.80 m tall, with eyes at about 1.65 m. The eye-match scale is about 1.00-1.04 (an estimate). The eye sits about 14 cm further forward of the head bone than the Farmer's.
- **Clips (20):** in-place walk, run and walk-backward; 7 idles; swim (with root motion); sleep and lie-down (1.3 m below the floor); wake up, falls, dying. There's no jump, crouch, squat or sit.

## Rulings so far (`studio:decisions`)

- `gate/meshy-survivor-eval`: the director's NOT GREENLIT, then Noah's override (`-override`): plan the swap.
- `-amend1-art-direction`: style clash no longer counts, and the swap plans for the target style.
- `-amend2-no-meshy-licence`: Meshy is a stand-in, not shipped. If Meshy output is ever imported, its licence goes into CREDITS and the manifest first.
- `-amend3-backpack-hat`: the backpack comes off, kept as its own piece, which makes it the first gear layer. He stays bare-headed.
- `-amend4-placeholder-base-bodies`: keep the Survivor cheap, and invest in reusable steps. The must-keeps carry over to every shipped combination of base body, clothing and gear.
- On the swap itself: look-down waivers must be re-granted on Survivor stills, and dropping H2 customisation for now is accepted.

## Plan outline

1. **Reusable Blender script** in `tools/blender/` (hand-made steps are an automatic FAIL). It re-skins the mesh onto our shared skeleton, scales it, splits off the head and hair (view body hides them, shadow keeps them), cuts the backpack off as a gear piece, and tags each piece as head, head-worn, body or gear. The script's output hash is recorded.
2. **Import:** the colour and roughness maps; metallic dropped.
3. **Clips:** our current clip set drives the new body. Meshy clips are optional extras (swim, sleep, idles).
4. **Re-prove** against the must-keeps (below).
5. **Optional Meshy trial:** one base body in plain fitted shorts plus one clothing piece, about 86 credits (nano-banana-pro images, meshy-7.1 at 4K with PBR, rig). Each step needs Noah's OK. The clothes get split off in Blender and bound to the base skeleton.

## Pass criteria

The re-proof plan, `_bmad-output/qa/meshy-survivor-reproof-plan.md`, has rows M1-M50 plus sections 7-11a. The key ones:

- **First-person view:** unchanged, and animation never moves the camera.
- **Look-down:** the amend5b bar, with waived cells re-granted on Survivor stills. Bar 1 is absolute: no neck stump, shoulder cap, open edges or back faces.
- **Head and shadow:** the view body hides the head and every head-worn layer. The shadow casts the full head, hair, outfit and gear.
- **Eye match:** at every stance. Foot slide and view/shadow sync on new baselines.
- **FPS:** body on within 5% of off, using the 8-arm release protocol, at the heaviest outfit. This is the highest risk: about 31k triangles, skinned twice.
- **Player layer:** the body is on the Player layer at runtime (#43).
- **Carried forward, not reset:** #35 (including Q10 and fingers, which this model can't meet) and #36.

## Open items

- The technical artist's part 2 (re-skin or Humanoid retarget, budgets, steps) and the engineer's section (body-system fit, clip coverage map) weren't finished.
- The art-director's layered-clothing standard (`studio:art style/layered-clothing-standard`) is a proposal, waiting for the director and technical artist.
- The director's questions for Noah: how realistic the target is, whether to write a style guide first, and per-asset budgets.

## Pod

**technical-artist** (lead), **gameplay-engineer**, **art-director**, **qa-lead**, **executive-producer**; **game-director** as the creative gate. Blender and Unity runs wait until no other pod is measuring.

Related: #35, #36, #43; the player-character spec and QA report.
