---
title: 'POC Spec: Placeholder player character (body, stances, animation) + customization research'
status: ready-for-pod
owner: John (PM)
created: '2026-10-18'
inputs:
  - vendor/Ultimate Modular Men- Feb 2022-*.zip
  - vendor/Ultimate Modular Women - April 2022-*.zip
  - Assets/Scripts/Gameplay/PlayerController.cs
  - CLAUDE.md   # model-facing rule (Humanoid rigs are skipped by the postprocessor)
---

# POC Spec: Placeholder player character

**The question this answers:** can Ruflo's agents give the first-person player a **real body**, a placeholder survivor who can walk, run, jump, crouch, squat, sit and sleep, **built so it can be customised later** without a rewrite? And can a research pass turn "character designer plus changeable outfits" into a set of well-formed GitHub issues for later?

## What exists today (checked 2026-10-18)

- **The player has no body.** `PlayerController` is a CharacterController with a camera. It has `walkSpeed` 5, `sprintSpeed` 8 (left Shift or the left stick press) and `jumpHeight` 1.2 (Space or gamepad South), plus wade and swim from the water pod, the lookout tower ramps through `TowerWalkZones` (**speeds 1.15 and 1.5 are final, see #8**), and an interaction system (E).
- **The vendor packs are both CC0, by Quaternius.**
  - **Ultimate Modular Men** (Feb 2022): 11 outfits, including Worker, Farmer, Casual, Casual 2, Adventurer, Punk, Swat and Suit.
  - **Ultimate Modular Women** (Apr 2022): 10 outfits, including Worker, Casual, Adventurer, Soldier, Punk and Formal.
- **Four ways to use each pack:**
  - *All together*
  - *Individual Characters* (FBX, glTF, .blend)
  - **Humanoid Rig** versions, for Unity retargeting
  - **Separate Skeletal Meshes and Animations:** `*_Head`, `*_Body`, `*_Legs` and `*_Feet` per outfit, plus a shared `Animations.fbx`. **This is a ready-made modular slot system.**
- **The rig:** `CharacterArmature`, with 79 bones (men) or 62 (women), including fingers.
- **The pack's 24 clips** include Idle, Idle_Neutral, Walk, Run, Run_Back, Run_Left, Run_Right, Roll, Interact, Death and HitRecieve.
- **Clips the pack doesn't have:** **Jump, Crouch, Squat, Sit and Sleep (lie down).**
- **Possible input conflict:** gamepad South is bound to Jump. Check whether the interaction system also uses South.

## Phase 0: research (gate; it shapes Phase 1 and produces the future issues)

**technical-director** leads, with **technical-artist**, **systems-designer** and **gameplay-engineer**. They run `bmad-deep-recon` at the **deep** preset and write `_bmad-output/research/technical-player-character-<date>/research.md`, citing sources (GDC talks, engine docs, postmortems).

**A. Needed for the build now:**
1. **What the player sees in first person:** full-body "true first person" (Arma, DayZ, Mirror's Edge, Escape from Tarkov), arms only plus a separate body for shadows (most FPS games), or a hybrid. Cover camera attachment and stabilisation, hiding the head, shadow-only meshes, and what multiplayer players see of each other.
2. **Animation set and sources:** where to get **CC0 or compatible** clips for jump, crouch, squat, sit and sleep. Look at Quaternius' other CC0 animation sets, Mixamo (check its license, and that it needs an Adobe login, so it's a manual download), and authoring in Blender. Cover retargeting through Unity Humanoid against keeping Generic.
3. **Stance numbers:** capsule height and eye height for standing, crouching, squatting, sitting and lying; crouch speed; how a jump-start-air-land sequence should feel; stride matching to stop foot sliding. Is foot IK on slopes needed now or later?
4. **Rules between stances:** which stances you can enter from which, when you're blocked (moving, airborne, in water, on tower stairs, no headroom to stand up), and how the camera behaves when sitting and sleeping.

**B. Customization, research only: a design for later, not built in this pod.** Cover:
- modular mesh slots on a shared skeleton
- body types and blend shapes
- face, hair and skin
- tint and colour masks against texture swaps
- how layered clothing hides the body underneath (body masks or hidden-under parts)
- merging skinned meshes and atlasing for performance
- LOD
- how to describe appearance in data so it saves and syncs over the network (an appearance ID or descriptor rather than meshes)
- how a character-creator screen usually works
- how equipment links to the inventory, including damage, wetness and dirt on clothing
- how survival games handle this: DayZ, Rust, SCUM, The Long Dark, Project Zomboid, 7 Days to Die

**C. Optional over-the-shoulder third-person view, research only: a design for later, not built in this pod** (the H7 debug camera is the only third-person camera built here). Cover:
- how survival and shooter games let players switch between first and third person (DayZ, Rust, SCUM, PUBG, Arma, 7 Days to Die, RDR2, The Division), and how players receive each approach
- the camera itself: shoulder offset and swapping shoulders, collision with walls and foliage, and aiming and crosshair alignment
- first-person and third-person animation sets, and whether one rig or two is needed (for example first-person arms against the full body)
- **fairness in a multiplayer survival game:** third-person peeking around corners and over cover, and the usual counters, such as first-person-only servers or server-side line-of-sight checks
- how the choice is offered (player setting, server rule, per-situation forcing) and how it interacts with interactions, the lookout tower stairs and the cab, and with swimming

**Gate:**
- technical-director approves A.1–A.4.
- game-director approves the first-person approach, the **default placeholder look** (suggested: the Worker or Farmer outfit) and the stance list.
- Noah approves the **future issue list** from B and C.

## Phase 1: pass criteria

| # | Criterion | Evidence qa-lead checks |
|---|---|---|
| H1 | **A clean, scripted import.** Both packs import through scripts in `Assets/Editor/` or `tools/` (no hand steps), and their licenses are copied in. Humanoid avatars are valid with no missing required bones (or Generic, if the research chooses it). **The postprocessor skips Humanoid rigs,** so a separate EditMode test checks the body faces +Z | Import log; avatar validation; facing test |
| H2 | **Built for customisation now.** A `CharacterAppearance` data asset (body type, the Head, Body, Legs and Feet slot meshes, and tint slots) is turned into a character at runtime by a `CharacterAssembler`. **At least 2 men's and 2 women's outfits** assemble from the separate meshes with 0 errors. Swapping one slot at runtime (for example Legs) works with no T-pose or seams. The player uses one default appearance, picked by game-director | EditMode and PlayMode tests; screenshots of 4 assemblies |
| H3 | **Every stance has animation.** Idle, Walk, Run (with strafe and back), Jump (start, air, land), Crouch (idle and walk), Squat (enter, hold, exit), Sit on the ground (enter, loop, exit) and Sleep (lie down, loop, wake). Each clip is from the pack, from a CC0 source the research chose, or **authored in Blender by a script in `tools/blender/`**. The license is recorded per clip | Clip manifest with sources and licenses; a PlayMode check that every state reaches its clip |
| H4 | **Movement unchanged, stances added.** Walk 5, sprint 8 and jump 1.2 stay as they are, along with **tower stairs 1.15 and 1.5**. Crouch and squat use the research's speeds and capsule heights. You can't stand up from crouch or squat under a low ceiling. Foot sliding is **≤ 10 cm per stride** at walk and run | PlayMode: the speed table, a headroom test, and foot-plant drift measured |
| H5 | **First-person view per research.** Looking down shows the body (or arms) as decided. The body casts full shadows. The camera never shows the inside of the head or body at any stance or pitch from −85° to +85°. Stance eye heights match the research ±3 cm | PlayMode: a pitch sweep with a clipping check per stance; screenshots |
| H6 | **Stance rules.** Sit and sleep are allowed only when standing still on the ground. They're blocked on tower stairs, in water and in mid-air. Any move input wakes you or makes you stand up. Sleeping shows a dim or fade only (**no HUD**). Bindings come from the research (suggested: Ctrl crouch, C squat, X sit, Z sleep, with gamepad equivalents) and **have no conflicts** | A PlayMode matrix of every stance against every blocked context; a binding audit |
| H7 | **A third-person debug camera** (development builds only) for QA and Noah to check animations. It's stripped from release builds | Build check |
| H8 | **No regressions.** TowerWalkTests passes unchanged (up time 26.6 s ±10%). The body fits the 0.62–0.72 m flights without snagging. Hatch, sash and firefinder still work. Wading and swimming work with the body (a placeholder pose is OK, logged for later). WorldBounds, wildlife, birds and fish tests all pass. Windows FPS is within 5% of the bodiless build. **Windows, Linux and macOS (Mono) builds succeed** | Full test XML; FPS JSON; 3 build logs |
| H9 | **Noah's feel check:** walk, run, jump, crouch, squat, sit and sleep, each rated "right" from first person and from the debug camera. 2 retunes are allowed | Ratings in the QA report |
| R1 | **The future issues.** The B and C research becomes **issue drafts in the same format as #8–#16** (question, what we know, gate, pass criteria, automatic FAIL, pod), saved to `_bmad-output/planning-artifacts/issues/future-character-*.md`. At least these: character creator UI; body types and blend shapes; face, hair and skin; outfit and equipment system linked to inventory; clothing layering and body masking; appearance save and network sync; mesh merging and atlas performance; swim and wade animations; sitting on objects and beds; sleep tied to survival needs; foot IK; **optional over-the-shoulder third-person view** (from C, including the multiplayer fairness rules). **Created on GitHub only after Noah approves the list** | The draft files, then the issue links |

## Automatic FAIL

- Building any character-creator UI, outfit gameplay or a player-facing third-person view. Those are research and issues only.
- Changing the tuned walk, sprint, jump or tower speeds.
- Any clip or asset without a recorded, game-compatible license.
- Hand-made Blender or Unity steps.
- Creating GitHub issues without Noah's approval.
- HUD elements.
- The same blocker 3 times in a row, or more than 3 assists.

## Pod

Run `/studio-pod` from `C:\Tools\ruflo\studio`, with the project path set to `E:\GitHub\washed-ashore`.

| Role | Owns |
|---|---|
| technical-director | Phase 0 lead, first-person and assembler architecture, future-issue quality (R1) |
| technical-artist | Imports, avatars, assembly, Blender clip authoring and retargeting (H1–H3) |
| gameplay-engineer | Stance state machine, capsule and headroom, camera, bindings, debug camera (H4–H7) |
| systems-designer | Stance numbers and rules; sleep and sit design notes for the future issues |
| game-director | Approves the default look and the first-person approach (gate) |
| qa-lead | Verdict, Noah's feel check, regression (H8, H9) |
| executive-producer | Schedule, assist count, final report, and filing the approved issues |
