# Artist section: animation sources, rig type, retargeting (A.2, with A.1 mesh notes)

Author: artist (technical-artist). Date 2026-10-18. Tags: [inspected] = read from the vendor zips with Blender 5.2 headless (scratch copies, nothing unzipped into the repo); [decision] = my recommendation; [unverified] = needs the Unity import to confirm.

## 1. What the packs contain [inspected]

- Both packs are CC0 1.0 (Quaternius). `License.txt` sits at each pack root and says so. `How To Use.txt` is identical in both.
- `Separate Skeletal Meshes and Animations/`: per outfit `<Outfit>_Head|Body|Legs|Feet.fbx`, plus `Animations.fbx` (armature and clips, no mesh).
  - Men: 11 outfits incl. Worker, Farmer, Casual, Casual_2, Adventurer, Punk, Swat, Suit, Beach, King, SpaceSuit. Women: Worker, Casual, Adventurer, Soldier, Punk, Suit, Formal, SciFi, Medieval, Witch.
  - Every part FBX carries the **full `CharacterArmature` (Root, Body, Hips, ... Head, fingers, legs, PT.L/R toes)** and one skinned mesh with an Armature modifier (Worker_Body: 54 vertex groups, 4 materials, 1511 verts men / 1315 women). Bone names are identical across all parts of one gender and across Animations.fbx. This is what makes name-based rebinding in the assembler work.
  - Men's rig: 79 bones (extra `*_end` leaf bones, a 4th joint per finger). Women's rig: 62 bones. **The two rigs differ in bone set and proportions**, so a men's clip does not drive a women's body one-to-one by bone path (the common names would, but the extra men's bones would be dead curves and rest poses differ). Clips are per gender.
- Pack clips (24, same names in both): Death, Gun_Shoot, HitRecieve, HitRecieve_2, Idle, Idle_Gun, Idle_Gun_Pointing, Idle_Gun_Shoot, Idle_Neutral, Idle_Sword, Interact, Kick_Left, Kick_Right, Punch_Left, Punch_Right, Roll, Run, Run_Back, Run_Left, Run_Right, Run_Shoot, Sword_Slash, Walk, Wave. Frame ranges: Idle 1-51, Walk 1-41, Run 1-25, Run_Back 26, Run_Left/Right 25 (frame rate to read at import).
- **Not in the packs:** Jump (start/air/land), Crouch idle, Crouch walk, Squat, Sit, Sleep. No Walk_Back/Walk_Left/Walk_Right either (strafe exists only at Run speed).
- A "Humanoid Rig" variant of each pack exists (no animations); it is for Mixamo-style retargeting.

## 2. Humanoid or Generic [decision: Generic]

Generic, with one authored/packed clip set per gender on the pack's own rig.

Why:
1. No clip source needs retargeting. Pack clips and Blender-authored clips both live on `CharacterArmature`. Humanoid retargeting only pays off when clips come from another skeleton (Mixamo is excluded by the brief: Adobe login; no downloads).
2. The modular system rebinds by bone name. Seven-plus FBX parts each with their own armature are simplest as Generic: the assembler instantiates a skeleton once and maps each part's `SkinnedMeshRenderer.bones` by name. With Humanoid every part would also need its own valid avatar and a copy-avatar setup.
3. The repo's facing rule works for Generic. `ModelFacingPostprocessor` skips Humanoid rigs (CLAUDE.md), so a Humanoid import needs its own facing handling and nothing guarantees Unity orients it. Generic goes through the existing flip path; the +Z test (H1) then guards it. [unverified] The postprocessor's head/hips detection must be run on a mesh-less Animations.fbx and on the part FBXs, and all must decide the same way. If any part reports `ambiguous`, treat it as a bug (CLAUDE.md), not a hand rotation.
4. Spec H1 allows Generic "if the research chooses it".

Costs accepted: no Humanoid IK goals (foot IK is a later issue anyway; Generic can use Animation Rigging or manual two-bone IK), and a future shared animation library from another skeleton would need a Humanoid avatar added then. Because Quaternius also ships the Humanoid Rig variant, that door stays open: do not delete the `Humanoid Rig` folders from the vendor zips.

Avatar validation for H1 under Generic: `ModelImporter.animationType == Generic`, avatar `isValid`, root motion off, `Hips` as root-motion node unused; test asserts the required bone list (Root, Hips, Abdomen, Torso, Chest, Neck, Head, UpperArm/LowerArm/Wrist L+R, UpperLeg/LowerLeg/Foot L+R) exists in every part and that all parts' bone name sets match their gender's Animations.fbx.

## 3. Clip plan

| Stance clip | Source | Notes |
|---|---|---|
| Idle, Walk, Run, Run_Back, Run_Left, Run_Right | pack (CC0) | strafe at walk speed: blend Run_Left/Right time-scaled, or author Walk_Left/Right/Back (see below) |
| Jump_Start, Jump_Air, Jump_Land | **authored** | Roll is not a jump |
| Crouch_Idle, Crouch_Walk | **authored** | built from Idle/Walk keys with hips lowered, spine forward, knees bent |
| Squat_Enter, Squat_Hold, Squat_Exit | **authored** | |
| Sit_Enter, Sit_Loop, Sit_Exit | **authored** | ground sit |
| Sleep_Down, Sleep_Loop, Sleep_Wake | **authored** | lying on the side or back; |
| Walk_Back, Walk_Left, Walk_Right | **authored if time** | otherwise reuse Run strafe clips slowed; I will report footslide against the H4 10 cm limit |

Authoring: one Blender script per gender-agnostic pose library in `tools/blender/character/`, run headless, reading the pack's `Animations.fbx` for the rest pose and writing an FBX of new actions with the same armature and bone names (Y-up/Z-up export flags matching the pack so the postprocessor treats them identically). Poses are keyed as pose-bone rotation offsets on named bones, interpolated with Bezier; hips height set from the pack's measured Hips z (0.865-0.894 m men, 0.983-1.002 m women, rest). Each clip is checked by a render-free script: foot bone world z against the floor at keys, no inverted knees. License for authored clips: ours (project), recorded per clip in `_bmad-output/poc/character-clip-manifest.md`; pack clips recorded as CC0 with the zip name.

Alternative sources (research only, none used): other Quaternius CC0 animation sets (Universal Animation Library and similar) would need a download, so they go through td to team-lead per the brief. Mixamo: Adobe login, terms permit use in games but not redistribution of the raw clips; excluded. Blender authoring is the default and does not need approval.

## 4. Retargeting
None needed with Generic. If the Humanoid door is ever opened: pack's Humanoid Rig FBX as the avatar source, `Animation Type = Humanoid`, copy avatar to the clip files, and the facing postprocessor must be extended first.

## 5. Open risks
- [unverified] Crouch/squat/sit/sleep authored by script will look like a placeholder; acceptable for this pod (placeholder character), flagged for H9.
- [unverified] Women's `Foot.L` tail direction in the animation armature looks odd (-0.64 y); will confirm the rest orientation before authoring leg poses.
- Part meshes ship their own copy of the skeleton, so import must drop the extra armature per part (assembler uses one skeleton; parts are mesh sources only).
