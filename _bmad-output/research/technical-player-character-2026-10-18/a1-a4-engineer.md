# Engineer section: first-person body and camera, capsule and headroom, stance interactions, bindings (A.1, A.3, A.4)

Author: engineer (gameplay-engineer). Date 2026-10-18, branch `player-character`. This replaces an earlier draft of this file that assumed no body mesh (written before the pod brief); its still-valid findings are kept below (tower save/restore, trap-door writer, climb areas).

Verified by engineer-2 against the working tree: the old draft's claims (TrapDoor.cs:190-191 calls OverrideEyeHeight and ClearViewOverride, which resets to the standing pivot; TowerWalkZones saves and restores body.height; C and LeftCtrl unbound in code; R3 `rightStickPress` unused in Assets/Scripts) all hold. Its no-body camera proposal contradicts spec H5 and is superseded by section 2; td must rule.

Tags: **[code]** read in the repo, **[scene]** read in World.unity, **[derived]** arithmetic on tagged values, **[mine]** my choice, **[designer]** value owned by `_bmad-output/design/player-stances.md` (pending; I use no number of my own for it). No outside sources fetched; nothing here is tagged as sourced that I did not read.

## 1. Facts read from the repo

- Capsule: height 1.8, radius 0.4, center y 0.9, step 0.4, slope 45, skin 0.05 [scene World.unity:11872-11878]. Camera pivot local y 1.65 [scene]; near plane 0.1, FOV 70 [scene World.unity:83664].
- `TowerWalkZones` slims the capsule via `PlayerController.SetWalkCapsule(0.25, 1.6)` inside the footprint, saving `body.height` on entry and restoring it on exit [code TowerWalkZones.cs:61-65]. A crouched capsule on entry would be saved and restored as "normal". Not mine to edit.
- `SetWalkCapsule` keeps the feet fixed and re-bases `authoredRadius` [code PlayerController.cs:121-129].
- Camera seam is `PlayerController.View.cs`: `OverrideEyeHeight` (hatch duck, TrapDoor), `OverrideView` (firefinder), `ClearViewOverride` which restores the **authored** pivot position (1.65) [code View.cs]. Nothing else may write the pivot.
- Firefinder calls `SuspendControl()` for its whole focus [code Firefinder.cs:115]; it starts only if `IsGrounded && !OnLane && !ViewOverridden` [code Firefinder.cs:102].
- Water: `EyeHeight` is read from the pivot; `SwimTuning.Validate` needs floatDepth + 0.2 <= eye height [code SwimTuning.cs:75]. Camera never underwater (fish N1) is the director's condition.
- Climb areas lock jump (`ClimbJumpLocked`) and shrink only the radius [code Climb/PlayerController.Climb.cs].

## 2. First-person view (A.1): recommendation [mine]

**Full-body true first person; camera is NOT parented to a bone.**

- The camera stays on the existing `CameraPivot` on the CharacterController root. The body is a child of the player root, faces +Z (artist's facing test), animated with root motion off. A head-bone camera would import clip bob and sway into the eye, which breaks the tower rule (no bob, sway, roll or FOV change) and the existing eye-height tests.
- The Head slot renderer(s) are set to `ShadowCastingMode.ShadowsOnly` by a `FirstPersonBody` component (head casts a shadow, is never drawn to the camera). Other slots draw normally and cast full shadows. The assembler (artist) exposes renderers by slot name.
- The body is pushed back from the pivot by `bodyBackOffset`, a per-stance table starting at 0.10 m [mine], tuned only by the pitch sweep, so the neck opening and chest top sit behind the near plane.
- The body does not pitch with the camera in this pod. Looking down shows torso, hands and legs as the clips pose them. Spine lean and look-aim go with foot IK as a later issue.
- Not built: arms-only viewmodel, second rig, remote-player view (multiplayer would show the head; logged for the future issues).

### Pitch-sweep clipping check (H5)

Per stance, pitch -85 to +85 in 5 degree steps (35 poses), on the clip pose held for that stance. With the real camera pose and `Mesh.Bake` snapshots of every drawn body renderer:
- Take the near-plane centre and 4 corners (distance 0.1, camera FOV and aspect).
- From each point cast a ray along camera forward through the baked triangles. If the nearest triangle met is back-facing, the point is inside a mesh. Also fail if any baked vertex lies between the camera and the near plane inside the frustum.
- Output: `TestResults/player-character/pitch-sweep-<stance>.csv` (stance, pitch, worst point), a PlayMode test asserting zero failures, and screenshots at -85, 0, +85 per stance.

## 3. Capsule, headroom and eye height (A.3)

All heights, eye heights, speeds and transition times come from `player-stances.md` [designer]. I add none. Mechanics [mine]:

- A `PlayerController.Stance.cs` partial owns the stance state and applies capsule and eye height; pure rules live in a static `StanceRules` class (testable without a scene). Feet stay fixed (as `SetWalkCapsule`). Radius is unchanged by stance, so the tower's slim radius is not fought.
- `SetWalkCapsule(r, h)` additionally records `standingHeight = h`. Lowered-stance heights are the designer's absolute numbers, so in the tower, where stances are blocked (section 4), the capsule is the tower's 1.6 and nothing else.
- **Headroom to stand up:** `Physics.OverlapCapsuleNonAlloc` from the current top (radius minus skin) up to the target height; mask = everything except the Water layer, triggers ignored, own collider ignored. A hit means stay lowered. The stand request is remembered for 0.25 s so stepping out from under a ceiling stands up without a second press.
- Entering a lower stance sets the capsule at once; the eye eases with `SmoothDamp` over the designer's time. Standing up grows the capsule only after headroom passes, then the eye eases up.
- **Seam change (my file):** `ClearViewOverride` restores the pivot to the **current stance eye height**; `AuthoredEyeHeight` stays 1.65 (Firefinder and tests read it). While `eyeHeightOverridden` or `viewPoseOverridden`, stance code only updates its target and never writes the pivot (the trap-door duck keeps its writer role).
- No head bob, sway, roll or FOV change in any stance; the eye moves vertically only during a stance change.

## 4. Stance rules against tower, water, interaction (A.4)

| Context | Rule | Why |
|---|---|---|
| Capsule restricted by a zone (a `SetWalkCapsule` call left height below `standingHeight`'s authored 1.8): the tower footprint. Detected inside Gameplay with no reference to the Lookout assembly | Crouch, squat, sit, sleep all blocked. If lowered on entry, stand as soon as the tower-height check passes. | Eye-to-steel minimums (0.15 / 0.20 m) and the near-plane eye rule were measured for the standing eye only. Blocking is the director's allowed alternative and needs no new measurement. Also gives H6 (no sit/sleep on stairs). |
| `WalkSpeedCap > 0` (ramp, landing deck) | Same block. | Same. |
| Climb area / lane | Same block as jump (`ClimbJumpLocked`). | Capsule radius is shrunk there. |
| Hatch duck (`OverrideEyeHeight`) | Stance changes ignored. | Duck owns the eye. |
| Firefinder focus (`ControlSuspended`, `ViewOverridden`) | All stance input ignored; the frame control resumes is also skipped (a leave press must not also crouch). Leaving restores the pivot to the current stance eye. | East is Firefinder leave. |
| Wade or Swim (`WaterMode != Dry`) | Lowered stances blocked; if lowered when water arrives, stand at once (open water, no headroom test). | Camera never underwater (N1); SwimTuning assumes the standing eye. |
| Airborne | Entry blocked. A jump press from crouch or squat is a stand-up request (jump stays 1.2 m, standing only). Sit/sleep: any move or jump input stands/wakes. | H6. |
| Interactor | Unchanged. Its ray starts at the pivot, so it follows the lowered eye. South is taken by the Interactor first (order -100) and `JumpPressed` already honours it. Interact does not wake or stand; only move, jump or a stance key does. | Spec H6. |
| Sprint held while lowered | Stands up (if headroom), then sprints 8; no crouch-sprint. | Keeps 8 m/s unchanged. |
| Respawn / `Teleported` | Reset to Stand, standing capsule, no headroom check. | No stale state, as Water does. |
| Debug camera (H7) | Own camera, `#if UNITY_EDITOR || DEVELOPMENT_BUILD`; reads stance and body, never writes the pivot. Stripped from release by the define; build check proves it. | H7 and the director's condition. |

Walk 5, sprint 8, jump 1.2, tower 1.15 / 1.5 untouched. Designer's crouch/squat speeds apply to `MoveTarget` and combine with caps by `min`.

## 5. Binding audit

### Bound today [code]

| Action | Keyboard / mouse | Gamepad |
|---|---|---|
| Move [PlayerController.cs:69-75] | W A S D | left stick |
| Look | mouse delta | right stick |
| Sprint | Left Shift | left stick press (L3) |
| Jump | Space | **South** |
| Interact (Interactor, runtime action) [Interactor.cs:25-26] | E | **South** |
| Firefinder (focus only) [Firefinder.cs:65-69] | Q turn left, Esc leave | right stick look, **East** leave |
| Template `Assets/InputSystem_Actions.inputactions` (no code reads it) | Attack LMB/Enter, Next 2, Previous 1, Interact E, Crouch **C**, Jump Space, Sprint LShift | Attack West, Next/Previous Dpad right/left, **Interact North**, **Crouch East**, Jump South, Sprint L3 |

Findings:
1. **South is shared by Jump and Interact and works**: the Interactor runs first, takes the press while a prompt shows (`TookPressThisFrame`), `JumpPressed` rejects a gamepad South press that frame [PlayerController.cs:204-209]. No stance may add a third South use.
2. **The template says Interact = North; the code says South.** Inert, but misleading. I propose aligning the template with the code (Interact South, stance bindings as below). This file is on my ownership list; I will tell producer before touching it.
3. **C and LeftCtrl:** C exists only as a Crouch binding in the inert template (the parked duck-under claim). **LeftCtrl is bound nowhere.** Both are free at runtime. Risk: Ctrl+P / Ctrl+S Editor shortcuts when the Game view loses focus (dev only). No Ctrl-modified action exists.
4. **East** is Firefinder leave; the focus suspends control, so East can also mean crouch otherwise. The resume-frame guard (section 4) stops a double meaning.
5. Dpad left/right, West, R3 are unused by code.

### Proposed bindings (designer's rules of record decide; spec suggests Ctrl crouch, C squat, X sit, Z sleep)

| Stance | Keyboard | Gamepad | Mode | Conflict check |
|---|---|---|---|---|
| Crouch | Left Ctrl | East | toggle (designer may choose hold) | LeftCtrl free; East shared only with Firefinder leave (focus-scoped, guarded) |
| Squat | C | Dpad Down | toggle | both free |
| Sit | X | Dpad Left | toggle | both free in code |
| Sleep | Z | Dpad Right | toggle | both free in code |

All paths live in one static `StanceBindings` table. An audit test enumerates every `InputAction` binding path from PlayerController, Interactor, Firefinder and StanceController and fails on any shared path outside the allowed list `{South: Jump+Interact, East: Crouch+FirefinderLeave}`.

## 6. Open questions

1. td: block all lowered stances where the capsule is restricted (my recommendation) or measure crouch against 0.15 / 0.20 m?
2. designer: keys for crouch vs squat; toggle or hold; jump-from-crouch as stand-up.
3. Behaviour of `SetWalkCapsule` and `ClearViewOverride` changes only when the stance is not Stand, so existing callers and tests are unchanged.
4. producer: OK to edit `Assets/InputSystem_Actions.inputactions` to match the code.
