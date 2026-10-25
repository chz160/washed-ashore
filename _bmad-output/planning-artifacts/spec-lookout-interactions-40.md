# Spec: lookout tower interactions on the walkable tower (#40)

Owner: designer-40 (systems-designer). Branch `lookout-interactions-40` (cut from `main` f98d029).
Status: **r10, 2026-10-25 04:15 CDT (clock read); criteria fixed before any result.** Every question is ruled
(section 7). Nothing is pending on design; the stop depth's value is confirmed by the first build's log and the engineer's print.

Gates this spec serves: director `studio:decisions gate/lookout-interactions-40` (L1-L9); td `studio:engineering
adr/lookout-interactions-40` (R1-R5). Older text of record: `_bmad-output/poc/tower-design-spec.md` (called "TDS" below).

## 0. How values are tagged

Every value carries its source in brackets:
- `[file:line]`: read from that file on f98d029.
- `[ruling key]`: a record in `studio:decisions` or `studio:engineering`.
- `[manifest key]`: `Assets/World/Lookout/MC39_Lookout.manifest.json` (schema 6), as landed.
- `[derived]`: arithmetic from the tagged inputs beside it.
- `[my choice]`: designer-40's; listed again in section 7 for ruling.

## 1. Player problem and loop

The player climbs the lookout to watch the land. Today the cab's three things do nothing: no prompt, E is dead, and the
hatch stands open for good (issue #40). The loop served is the observation loop: climb, close the hatch behind you, open
windows, take bearings on the firefinder, climb down. Nothing here adds gameplay; it wires parts already designed under
TDS §3-§6 and the rulings in section 2, onto the walkable tower (no guided path).

## 2. Rulings this spec applies (cited, not restated)

| Key | What it fixes here |
|---|---|
| `gate/lookout-interactions-40` | L1-L9 (feel, floor, look, no guard, prompts, firefinder, sashes, look elsewhere, Noah's hand check) |
| `gate/lookout-interactions-40-amend1-td-l2-l7-l8` | headroom stop accepted, conditions (a) head meets the leaf, eye to leaf ≥ 0.15 m (never under 0.12), measured eye to leaf, and (b) Open shows and works from the stop; layers accepted |
| `gate/lookout-interactions-40-amend2-spec-q1-q7` | Q1 91.1518°; Q2 1.22 / 0.185 m from `cab.floor_y`; Q3 (no duck) WITHDRAWN by amend3; Q4 neither body nor its shadow during the view; Q5 and Q6 to td and qa; Q7 visible wall |
| `gate/lookout-interactions-40-amend3-duck-stays` | THE DUCK STAYS: lane-free, position-driven from the tower's flight data, `HatchEyeHeight` unchanged, all six o1-o2 conditions, eye never below 1.0 m; T7.3 runs with the duck; removing it later needs measured numbers brought to the director |
| `gate/lookout-interactions-40-amend4-stop-depth` | stop depth DERIVED so the eye stops ≥ 0.15 m below the closed leaf, measured eye to leaf, from the game's camera height and capsule top (about 0.20 m expected); above 0.25 m back to the director; no duck at the stop; amend1 conditions stand; qa reports the eye-to-leaf figure |
| `adr/lookout-interactions-40` parts 1-17 (the highest part wins) | R1 leaf collider; part4 (A) lane-free duck source, (B) headroom stop, (C) 2 mm bump bound; part5 L7 sweep and retirements; part7 stop form and the height check; part9 test rewrites and retirements; part10 K1 CabBand on WorldBounds, K3 derived stop depth, K4 collider check withdrawn; part11 Q1 mechanism, Q2 datum, Q5, Q6, Q7; part12 Q4 mechanism, TrapDoor 8fbfb652 accepted; part13 near plane vs the LEAF only; part14 open-pose test at the wall's outer face, `ClimbRulesTests` case retired; part15 duck confirmed; part16 `StopDepth()` final rule; part17 level-40 stage of record (LookoutGameplaySetup.cs 916cba7a) |
| `greenlight/lookout-tower` N13 (as quoted in `gate/kinematic-movers-45`) | about a second or more; nothing auto-opens or closes; closing refused while the player is in the opening |
| `ruling/lookout-tower-open-hatch-no-guard` | nothing drawn or invisible round the open hatch; walking in steps down onto the top flight |
| `ruling/lookout-tower-hatch-leaf-open-position` + amend1, amend2, amend3 | leaf (v) built: pin set back 0.045 m, open leaf rests against the east wall, touching accepted; no flicker; nothing of it shows through the wall from outside |
| `ruling/lookout-tower-o1-o2` + amend1, amend2 | the duck rules, if a duck exists (section 5.1) |
| `ruling/lookout-tower-near-plane-eye-rule` + amend1, amend2 | 0.12 m floor nothing goes under without the director; camera never inside anything (NP4) |
| `ruling/lookout-tower-firefinder-rim-and-sight-view` (+ amendments) | eye at the rear sight; no screen-space element; view restored exactly |
| `gate/kinematic-movers-45` K1-K4, amend3, amend3a | leaf yields, never the player; colliders off while moving; #40 must re-run the hatch cases with no stand-ins |
| `greenlight/player-character` C1, C3 | no HUD beyond the prompt line; no new bob, sway, roll or FOV change on the tower |

## 3. Values

### 3.1 Interaction (all unchanged)

| Item | Value | Source |
|---|---|---|
| Interact input | E (`<Keyboard>/e`), gamepad South | [Interactor.cs:20-21] |
| Aim ray | from the camera, along camera forward, once per frame | [Interactor.cs:110, 129-142] |
| Range | 2.5 m; hard cap 2.5 m | [InteractionSettings.asset:15], [InteractionSettings.cs:13] |
| Mask | `WorldLayers.InteractMask` = Default + Prop + Interactable; triggers ignored; nearest hit only, so solids occlude | [Interactor.cs:133], [WorldLayers.cs:41,50] |
| Prompt format | `{key} – {Verb} {noun}`: space, en dash (U+2013), space | [InteractionSettings.asset:19], TDS §3.2 |
| Key text | keyboard `E`; Xbox `A`; PlayStation `Cross`; any other pad `South` | TDS §3.2, [InteractionSettings.asset:20-21] |
| Prompt place and size | baseline 30 % of screen height up; cap height 2.4 % of screen height | [InteractionSettings.asset:16-17] |
| Refusal | no prompt and E does nothing; no refusal text (L5) | TDS §3.1, `gate/lookout-interactions-40` L5 |

### 3.2 Prompt line per state (keyboard shown)

| Target and state | Line | Source |
|---|---|---|
| Hatch closed, player clear of the swing volume | `E – Open hatch` | [LookoutTuning.asset:63,67], TDS §3.2 |
| Hatch open, player clear | `E – Close hatch` | [LookoutTuning.asset:64,67] |
| Hatch moving, or player's capsule in the swing volume | none | [TrapDoor.cs:114], TDS §4 rule 1 |
| Hinged sash closed / open | `E – Open window` / `E – Close window` | [LookoutTuning.asset:63-64,68] |
| Sash moving; any fixed sash; any pane | none | TDS §3.2, [WindowSash.cs:51] |
| Firefinder idle, player grounded | `E – Use firefinder` | [LookoutTuning.asset:65,69], [Firefinder.cs:103-108] |
| Firefinder in use, first 4 s of that use | `E – Leave firefinder` | [LookoutTuning.asset:66,62], `ruling/lookout-tower-o1-o2` O2 |
| Firefinder in use after 4 s; anything else | none | as above |

### 3.3 Hatch

| Item | Value | Source |
|---|---|---|
| Start state | closed (leaf flush with the cab floor, ± 0.01 m) | issue T3; TDS §4; td R1(a) |
| Hinge | east long edge, pin at tower-local (1.0236, 26.3398, -0.0882), axis (0, 0, -1), set back 0.045 m east of the opening | [manifest hatch.pivot, hinge_axis, pin_setback] |
| Opening | x 0.2674-0.9786, z -0.8382-0.6618 at floor top y 26.3398 (0.711 x 1.50 m) | [manifest hatch.opening_min/max] |
| Leaf | 0.7112 m hinge to free edge, 0.045 m thick | [manifest hatch.leaf_width, thickness] |
| Open angle | **91.1518°**, the manifest's value, written into `LookoutTuning.hatchOpenAngle` (the one value TrapDoor reads); an EditMode check holds it equal to the manifest within 0.001°. The asset's 92.0 is retired | [manifest hatch.open_angle_deg]; `gate/lookout-interactions-40-amend2-spec-q1-q7` Q1; td part11 |
| Swing time | 1.4 s each way (range 1.0-2.5) | [LookoutTuning.asset:15], [LookoutTuning.cs:17], L1 |
| Easing | ease in and out, keys (0,0) and (1,1), flat tangents; monotonic; shared with the sashes | [LookoutTuning.asset:17-40] |
| Peak angular speed | at most 150°/s (about 98°/s at 1.4 s and 91.15°) | TDS §4; [derived: 1.5 x angle / time, the curve's peak slope] |
| Swing volume (refusal) | the opening's plan, from 0.05 m below the floor's underside up to one leaf width above the floor top | [TrapDoor.cs:176-182], [LookoutTuning.asset:42] |
| Swing blocker | `CLIP_BOX_HatchSwing_01`: on only while the leaf moves; layer WorldBounds | [manifest colliders], td R2 |
| Leaf collider | `COL_BOX_Hatch_01`, adopted under the leaf; on only at rest; layer Interactable | td R1(a), R2; [TrapDoor.cs:139-153] |
| Closed hatch stops the player | from above, by its leaf collider; from below, by the **headroom stop** `HATCH_HeadroomStop`: one box, a child of the tower root, found by name on a rebuild; the opening's x/z footprint; its TOP at the leaf's underside and its LOWER face (where the capsule top stops) at underside - `StopDepth()`; layer WorldBounds; saved disabled, on in play only while Closed (TrapDoor.Settle from Awake) | td part4 (B), part14 (1), part16, part17; director amend1; level-40 22:3x |
| Headroom stop depth | `LookoutGameplaySetup.StopDepth()`, the one function the build and every check use: max(`hatchStopHeadroom` 0.15, 0.15 + max(0, eye at the stop - tower capsule height)), the standing eye read from World.unity's camera pivot (read-only), never typed. Expected about **0.20 m** (pivot 1.65 - capsule 1.60 = 0.05; no duck at the stop); the build THROWS above **0.25 m** (director's cap) | director amend4; td part14 (1), part15 (3), part16, part17; [LookoutTuning.asset:41]; [TowerWalkZones.cs:25] |
| Floor underside at the opening | opening y minus `cab.floor_thickness` (26.3398 - 0.0508 = 26.289) | [manifest hatch.opening_min, cab.floor_thickness]; [TrapDoor.cs:170] |
| Duck under the floor | **ON**: lane-free, driven by the player's position on the top flight from manifest `flights[last]` foot, head and width (passed by the build to TrapDoor.Configure), `LookoutRules.HatchEyeHeight` unchanged, never a ClimbArea; all six `ruling/lookout-tower-o1-o2` conditions bind; eye never below 1.0 m. It does not react to the hatch being open or closed | director amend3, amend4; td part4 (A), part15 (1), part17 |

### 3.4 Sashes

| Item | Value | Source |
|---|---|---|
| Which | the four `*_Hinged` sashes (N, E, S, W); the four `*_Fixed` never prompt | [manifest sashes[].hinged] |
| Hinge | top edge; the bottom swings outward | [manifest sash_note], TDS §6 |
| Travel | 0° to 35° outward; the bottom rail moves about 0.67 m out and 0.21 m up | [LookoutTuning.asset:48], [manifest sashes[].open_angle_deg]; [derived from height 1.1722 m] |
| Swing time | 0.9 s each way (range 0.6-2.0), same curve as the hatch | [LookoutTuning.asset:47], L1 |
| Refusal | no prompt while the player's capsule overlaps the sash's swept volume | [WindowSash.cs:51, 61-73] |
| Collider | `COL_BOX_Sash*Hinged_01` under its sash; off while it moves; layer Interactable | [WindowSash.cs:76-83], td R2 |
| Panes | mesh, materials and missing panes identical open and closed | TDS §6 |
| Window band walls | `CLIP_BOX_CabBand_01`-`04` on WorldBounds, set by the build by name: they block the player (L7), and the aim passes through them to the sash. Other `CLIP_` proxies stay Default | td part10 (K1), part12 (1) |

### 3.5 Firefinder

| Item | Value | Source |
|---|---|---|
| Enter | `E – Use firefinder` with a clear ray; player grounded | [Firefinder.cs:103-108] |
| Blend in and out | 0.4 s each, smoothstep | [LookoutTuning.asset:52], [Firefinder.cs:191] |
| Sight FOV | 45° vertical, from the player's 70° | [LookoutTuning.asset:49], [World.unity:83736] |
| Eye at the sight | **1.22 m** up, **0.185 m** back from the pedestal axis; ranges 1.21-1.26 and 0.15-0.195 (replacing the asset's 1.30 / 0.33 and the class's 1.25-1.40 / 0.30-0.40) | TDS §5, §8 (A3, A10); `gate/lookout-interactions-40-amend2-spec-q1-q7` Q2; [LookoutTuning.cs:45-47] (to change) |
| Floor datum of the eye | the cab's walking surface, `cab.floor_y` 26.3398, not `targets.cab_floor_y`; an EditMode check asserts the eye = `floor_y` + eye height | [manifest cab.floor_note]; td part11 (Q2); [LookoutGameplaySetup.cs:95] (to change) |
| Sight still | one or more sight stills, with the scene's own sun and FOV, go to the director **before qa grades T5**; they also show that no body shadow strays into view | amend2 Q2, Q4 |
| Start bearing | 135° the first time; kept between uses | [LookoutTuning.asset:61], [Firefinder.cs:77-80] |
| Mouse | 0.06° per count: X turns the bearing, Y pitches | [LookoutTuning.asset:53], [Firefinder.cs:157-160] |
| Right stick | 60°/s at full tilt: X turns, Y pitches | [LookoutTuning.asset:54], [Firefinder.cs:158] |
| Q / E held | after 0.20 s: 12°/s for 0.5 s, then 45°/s; Q turns anticlockwise, E clockwise (gamepad South held turns like E) | [LookoutTuning.asset:55-58], [LookoutRules.cs:69-73] |
| Pitch limits | -25° to +5° | [LookoutTuning.asset:59-60] |
| Leave | Esc or gamepad East; or a tap of E / South (released under 0.20 s). The press that entered never counts; a hold that has started turning never leaves | [Firefinder.cs:73-74, 146-170], TDS §5 |
| After leaving | yaw = the bearing, pitch 0; FOV 70; eye back at the standing eye; stance Stand; control back | [Firefinder.cs:136-141, 211-224], [PlayerController.View.cs:59-66], L6 |
| While in use | move, sprint, jump, normal look and the aim ray are off; gravity and collision stay on | [PlayerController.View.cs:68-73], TDS §5 |
| Player body during the view | from entry to the end of the blend out, **neither the view body nor the shadow body is seen or casts a shadow**. On entry the Firefinder records each body renderer's `enabled` and `shadowCastingMode` and disables it; on exit it restores the recorded values exactly | amend2 Q4; td part12 (2) |

### 3.6 Player values the criteria read (unchanged, read from the game at test time)

Standing eye 1.70 m above the walking surface (pivot 1.65 m + 0.05 m) [TDS §9 T-I2e, A13; World.unity camera pivot
localPosition y 1.65]; camera near plane 0.1 m [World.unity:83734]; tower capsule radius 0.25 m, height 1.6 m
[TowerWalkZones.cs:25]; lowered stances blocked on the tower [`spec/player-stances`].

## 4. Acceptance criteria

Each row says what passes. "Real tower" means `LookoutTower.prefab` as built by the editor scripts into `World.unity`,
with no stand-ins (`gate/kinematic-movers-45-amend3`). Proof time steps are `LookoutTargets.ProofDeltaTimes` (1/30 to
1/240) where a row names them. **(N)** marks rows that also need Noah's hand check (section 6).

### T1 Builder wiring (EditMode and build diff)

| ID | Pass |
|---|---|
| T1.1 | After the build, the prefab holds exactly one `TrapDoor` (id `lookout.hatch`), four `WindowSash` (ids `lookout.sash.MC39_Sash_{N,E,S,W}_Hinged`), one `Firefinder` (id `lookout.firefinder`), and no `ClimbArea` anywhere in the prefab or `World.unity`. Every serialized reference on them is non-null (scene and prefab YAML: no `fileID: 0` on those fields) |
| T1.2 | Every name, position, axis and size they use comes from the model's nodes or the manifest; the test reads the manifest itself and finds each value equal (positions within 0.001 m, axes within 0.1°). No value is typed in the scene |
| T1.3 | `COL_BOX_Hatch_01` and `CLIP_BOX_HatchSwing_01` exist; the leaf collider is a child of `MC39_Hatch`; the blocker is not, and is disabled at rest. Exactly one `HATCH_HeadroomStop`, a child of the tower root, not under the leaf: x/z equal to the opening's (± 0.001 m); **stop height == `StopDepth()` (same inputs) and stop top == the leaf's underside, each within 0.001 m**, never a literal 0.15; `StopDepth()` ≤ 0.25; layer WorldBounds; saved disabled; a rebuild reuses it by name (td part15 (2), part16; amend4) |
| T1.3a | `TrapDoor` holds the top flight's foot, head and width, equal to manifest `flights[last]` (± 0.001 m): the duck is on (width is never 0). No ClimbArea (amend3; td part15, part17) |
| T1.3b | EditMode equalities: `LookoutTuning.hatchOpenAngle` = manifest `hatch.open_angle_deg` ± 0.001°; the firefinder eye = `cab.floor_y` + `firefinderEyeHeight` ± 0.001 m (td part11) |
| T1.4 | The leaf is authored closed in the prefab (rotation equals the FBX's) |
| T1.5 | Layers set by the build, read back by the test: the leaf, hinged-sash and firefinder proxies on Interactable; the swing blocker, the headroom stop and `CLIP_BOX_CabBand_01`-`04` on WorldBounds; every other proxy on Default (td R2, part10 K1). The #38 stance refusals in the cab are unchanged |
| T1.6 | Running the full build twice from a clean checkout: the second run leaves `git status` empty for the prefab, `World.unity` and every `.asset` (`.meta` and ProjectSettings by td's D8 rule) |
| T1.7 | Each hinged sash and the open hatch can be aimed at from the cab: with every part at rest, a ray from a standing eye point 1.0 m in from the middle of each sash, aimed at the sash's centre, gets that sash as target (the first hit is the sash proxy, through the band); and likewise for the hatch (td part10 K1) |

### T2 Player has the Interactor (EditMode)

| ID | Pass |
|---|---|
| T2.1 | `World.unity` YAML has one `Interactor` and one `InteractionPrompt` on the Player's camera (`CameraPivot`), by script GUID; both references in `Interactor` are non-null and point at `Assets/Settings/InteractionSettings.asset` and that prompt |
| T2.2 | Running the setup again changes nothing (`git status` empty) |
| T2.3 | Exactly one UI Text exists while a prompt shows (TDS T-I1c) |

### T3 Hatch (PlayMode on the real tower) (N)

| ID | Pass |
|---|---|
| T3.1 | On load the hatch is closed: angle 0 ± 0.01°, leaf collider on, blocker off. It never moves in 60 s idle in each rest state |
| T3.2 | From the top flight below, aiming at the closed leaf from where the headroom stop holds the player (W held 2 s, then released): the interactor's own aim hits the leaf first (the stop ignored, because WorldBounds is not in `InteractMask`; shown by the test, not assumed), `CanInteract` is true, the line reads `E – Open hatch`, and E opens it. **The player does not have to step back first** (L2; director amend1 (b); td part9 (3)). The headroom stop is off from the first frame of the swing and while open; on again only once Closed |
| T3.3 | Opening: angle rises monotonically from 0 to the open angle (Q1) ± 0.5° in 1.4 ± 0.05 s; peak ≤ 150°/s; no prompt and E ignored while moving |
| T3.4 | Walking up into the cab through the open hatch with W only works at every proof time step |
| T3.5 | From the cab floor, clear of the swing volume, aiming at the open leaf: `E – Close hatch`; E closes it to 0 ± 0.5° in 1.4 ± 0.05 s. The test lists every point of a 0.10 m grid on the cab floor where the line shows; the list is not empty |
| T3.6 | Closed hatch from above: the player walks across the leaf both ways along and across the opening and stands on it 5 s. Feet within 0.02 m of the floor top throughout; no step, bump or slide (vertical feet speed under 0.1 m/s; "grounded" = a ground probe hits within 0.02 m under the feet every frame, raw `isGrounded` frames reported beside it, as qa measures); drift under 0.01 m while standing; feet height constant within 0.002 m while walking across the leaf (part4 C) (L2) |
| T3.7 | Closed hatch from below: the player walks up the top flight into it holding W for 5 s. The player stops at the headroom stop, and it reads as the head meeting the closed leaf: stopped with the leaf visibly just overhead, never short of it on the stair; no slide back down the flight, push, jitter or loss of look (director amend1 (a)). The eye is measured to the leaf in T7.3. The player never passes, never ends inside the stop or the leaf (`Physics.ComputePenetration` 0 within 0.001 m every frame); no jitter (feet move under 0.01 m in the last 4 s); no launch (upward feet speed under 2 m/s); releasing W leaves them standing (drift under 0.01 m in 5 s) |
| T3.8 | Refusals (silent): standing on the closed leaf, no prompt; standing in the opening on the top flight with the hatch open, no prompt; pressing E during a swing does nothing. In all three the leaf does not move |
| T3.9 | Always a way through: from every refused position in T3.8, stepping off the leaf or out of the opening brings the prompt back within 0.5 m of travel |
| T3.10 | Walking or backing into the open hatch from the cab floor puts the player on the top flight: no launch, no frame with feet more than 1.3 m below the cab floor top, no loss of control, no camera clip (L4; `ruling/lookout-tower-open-hatch-no-guard`) |
| T3.11 | `TowerWalkZones` still restores the capsule after a hatch trip (radius and height equal to before the tower, once off the footprint) |

### T4 Sashes (PlayMode on the real tower) (N)

| ID | Pass |
|---|---|
| T4.1 | Each of the four hinged sashes: `E – Open window`, opens to 35 ± 0.5° outward in 0.9 ± 0.05 s, monotonic; then `E – Close window`, back to 0 ± 0.5°. No prompt while moving. The four fixed sashes never show a prompt |
| T4.2 | Mesh, material and pane mask of every sash hash the same open and closed; missing-pane count over all 8 is unchanged |
| T4.3 | Pressed against each hinged sash's sill (W held 2 s toward it), with the sash closed, open and at every 5° step of its swing (sampled at those steps, not every frame). Graded for the view body and the shadow body separately, vertices baked at each pose (`gate/lookout-interactions-40-amend7-t43-visible-clip`, measured per `gate/lookout-interactions-40-amend7a-t43-measure`; td part79): **(A)** wherever a sash stands in the window (every fixed sash; a hinged sash when closed): no vertex beyond that sash's cab-side face (the inner face of manifest `COL_BOX_Sash*_01`); count, plus max depth in m along the face's outward normal. **(B)** at an open hinged sash's opening: no vertex beyond the wall's visible outer face by more than 0.001 m; count. Leaning out is fine; poking out past the wall is not. **(C)** no vertex inside the rendered window frame, mullion, sash or glass meshes (amend7 (2); td part79), by an exact point-in-mesh test (temporary MeshColliders from those renderers on a spare layer, ray-crossing parity with backface hits on and restored, colliders destroyed after, mesh names logged); count. Also: no vertex inside the sash leaf's oriented box (td part77 (2a), recorded after the provisional probe), and the camera's near rectangle enters none of these. Reported, not graded: the depth into the invisible window-band box. Non-vacuity: a row counts only if the capsule reached within 0.30 m of the sash face or opening plane, with feet start, end and moved distance logged; otherwise the row is marked VACUOUS. qa reports, per side and pose, the count and depth for A, B and C, and the worst pose per clause. Any non-zero A, B or C goes to the director with a still of the worst pose (`player.ViewCamera`, 960x540). A closed-sash depth over 0 is a visible clip, which the director rules on with the still. The bar does not move after the figures |
| T4.4 | Walking and jumping at each open sash for 10 s (yaw sweep, sprint, jump spam): the player stays in the cab; no new way out (L7). Also a capsule sweep (tower capsule) outward through each open sash's window opening is blocked by the `CLIP_BOX_CabBand_0N` wall (part5 D). Layout note, not a new criterion: the east hinged sash stands directly over the hatch opening, which is hard against the east wall. With the hatch open, a sprint or jump at that window drops the player about 0.21 m through the opening onto the top flight's head, still inside the tower: the designed drop-in (`ruling/lookout-tower-open-hatch-no-guard`), not a way out (L7); L4 applies to this approach too, covered by the existing drop-in checks (T3.10) (`gate/lookout-interactions-40-note-east-window-over-hatch`). So T4.4 runs with the hatch closed by the real path first (td part59 (1)) |
| T4.5 | Shutter clearance as TDS T-I4a (sash never within 0.02 m of its side's shutter assembly through 0-35°) |

### T5 Firefinder (PlayMode on the real tower) (N)

| ID | Pass |
|---|---|
| T5.1 | From the cab, grounded, aiming at the pedestal or ring: `E – Use firefinder`; E enters. In use within 0.5 s; FOV 45 ± 0.5° in use |
| T5.2 | Eye at the sight is 1.22 m above `cab.floor_y` and 0.185 m behind the pedestal axis along the bearing, ± 0.002 m, at bearings 0, 90, 180, 270. Graded only after the director has seen the sight still (3.5) |
| T5.3 | Mouse: 1000 counts of X turn the bearing 60 ± 0.5°; Y pitches and stops at -25° and +5°. The firefinder multiplies the raw mouse delta by `firefinderMouseDegPerCount` with no look-sensitivity term [Firefinder.cs:157]; qa prints the expected and measured values |
| T5.4 | Right stick at full tilt X for 1.0 s turns 60 ± 2°; at half tilt, half that |
| T5.5 | E held: no turn for the first 0.20 s; 12°/s for the next 0.5 s; 45°/s after (each ± 1°/s); Q the same, anticlockwise. The pointer's world azimuth equals `BearingDegrees` ± 0.5° |
| T5.6 | `E – Leave firefinder` shows for 4.0 ± 0.1 s from entry, then never again in that use |
| T5.7 | Leave by a tap of E, by Esc, by gamepad East, and by a tap of South (four runs). Each: the press that entered never counts; a hold that turned never leaves on release; camera back in 0.4 s |
| T5.8 | After leaving (each way in T5.7, and after a respawn mid-use): eye back at the standing stance eye ± 0.001 m, stance Stand, capsule as before entry, FOV 70 ± 0.01°, roll 0, yaw = the bearing ± 0.5°, pitch 0; the first frame after the blend accepts move and look; the leaving press neither jumps nor changes stance; capsule moved under 0.01 m during the whole use (L6) |
| T5.9 | Body (amend2 Q4; td part12 (2)): from entry to the end of the blend out, no view-body or shadow-body renderer is enabled and none casts a shadow, and the sight still shows no body and no stray body shadow (twin, bodies shown against hidden, inside the frame: zero changed pixels). After leaving (every way in T5.7, and a respawn), each body renderer's `enabled` and `shadowCastingMode` equal the values recorded before entry |
| T5.10 | Through the bearing sweep at the sight no part of the instrument crosses the near plane's rectangle (TDS T-C3b, A3) |

### T6 Lookout suites (EditMode and PlayMode)

| ID | Pass |
|---|---|
| T6.1 | Retired by name as guided-path, each with a one-line reason in qa's report: `LookoutClimbTests`, `LookoutStressTests`, `LookoutTwinTests` (td part9 (8)); `LookoutBuiltTowerTests`, `LookoutEyeClearanceTests` (part5 H); `ClimbRulesTests.LevelSpeed_IsWalkSpeed_RampedToStairSpeedWhereTheLevelMeetsTheIncline`, with its pre-existing figure recorded (part14 (3)). `LookoutHatchTests` retires only case by case, each case mapped by name to its lane-free successor (the duck test, HatchWalk B and C) (part9 (8)) |
| T6.2 | Every other test in the 24 Lookout PlayMode fixtures and the Lookout EditMode suite passes on the real tower, or goes to td by name with why it is guided-path |
| T6.3 | `LookoutMovers` hatch cases (K1, K2, K3) run on the real tower with no stand-ins S1-S5, and pass; `RealTowerWiring` retired or reduced to a helper (td R4) |
| T6.4 | Old targets that move with this spec: T-I2b's open angle reads 91.1518° from the tuning; T-I2e's duck case is carried on lane-free as T7.4; `LookoutHatchWalkTests` B and C are rewritten to T3.2, T3.7 and T7.3 (td part9 (3)) |

### T7 Tower walking and the #45 follow-ups

| ID | Pass |
|---|---|
| T7.1 | `TowerWalkTests` passes; up time 26.6 s ± 10 % (`gate/kinematic-movers-45` K6). The test opens the hatch through `TrapDoor.SetState(Open, 0)` before its timer starts, so BEFORE (hatch posed open) and AFTER compare like for like, and names that step in its source and qa's report; qa prints the timer start and the hatch state (td part11 Q5; amend2 Q5). The real open-from-below flow is covered by T3.2 |
| T7.2 | Walk-out of the swing area: from every refused standing point (on the leaf; in the opening on the top flight), W in each of 8 headings for 1 s: the player is never pushed, lifted or trapped; the prompt returns once clear (T3.9) |
| T7.3 | With the duck running (amend3). Eye to leaf: stopped at the headroom stop below the closed leaf, the eye is ≥ 0.15 m below the leaf's underside, measured eye to leaf (amend1 (a), amend4); qa reports the figure. Under 0.15 m goes to the director by name; under 0.12 m fails. Near plane: at every frame of T3.2, T3.4, T3.7 and T3.10, all four corners of the near-plane rectangle (near 0.1) lie outside the leaf collider and the leaf's rendered mesh, and the eye is ≥ 0.15 m from the leaf and the floor edge on the ordinary walking line (`ruling/lookout-tower-near-plane-eye-rule` amend2 P2). Corners inside the headroom stop or the swing blocker (invisible) are logged, not failed (td part13 (3)). A fail goes to the director with the numbers; never a ClimbArea, never a collider change (td R1(e)) |
| T7.4 | The lane-free duck (amend3; td part4 A), per `ruling/lookout-tower-o1-o2` conditions and TDS T-I2e's datum (camera height above the surface): eye height on the top flight is a function of position only, the same ± 0.005 m going up, going down and stopped 5 s, at 1/30 and 1/240, and the same with the hatch open and closed; never under 1.0 m above the flight's line; smooth (no step over 0.005 m between frames at 1/240); standing eye once the feet are on the cab floor; FOV 70 and roll 0 throughout; look input still turns the camera. If the built opening gives no lowering, the eye's height above the line is constant to 0.001 m, and the qa report says so with the opening length and pitch (removing the duck stays the director's, amend3) |
| T7.5 | Swing blocker mid-swing: at each 5° of an opening and a closing swing, the blocker is enabled, the leaf collider and every collider under the leaf are disabled, the leaf mesh's part the capsule can reach lies inside the blocker, and the player's capsule never overlaps the leaf mesh. With the player pressed against the blocker (W toward the opening) during the swing: no push, no lift, no embed (K2) |
| T7.6 | Open pose against the east wall (amend2 Q7; td part10 K4 withdraws the collider check; td part14 (2), part18 set the test). Wall selection: the `MC39_LOD0_Cab` triangles with centroid in x 0.9786-1.0668, y 26.3408-27.3558, z -1.0668-1.0668 (tower-local; the lower east wall from the floor top to the window band) [manifest objects, cab, hatch; my choice, accepted by td part18 and the director provided it covers the whole face]. Pass: (a) show-through: no vertex of `MC39_Hatch` at the open pose lies beyond the selection's OUTER face (its max x) by more than 0.001 m; (b) whole face covered: the open leaf's max y is below 27.3558 (the window band's bottom); if it is not, the window glass is checked too and it goes to td and the director; (c) flicker: no leaf triangle coplanar with a selected triangle (normals parallel within 1° and plane distance under 0.0005 m; qa's method). Touching is accepted (`ruling/lookout-tower-hatch-leaf-open-position-amend3`) |
| T7.7 | Flicker and show-through stills, twin-diffed: from the cab's far (west) wall at the open leaf, and from outside the cab east of the tower at the leaf's height, both at 16:9 with the scene's own sun, each against a twin with the leaf hidden. Outside: zero changed pixels from the leaf. Inside: no pixel alternating between leaf and wall over 60 frames with a still camera. Counted only inside the leaf's projected bounds, with birds, wildlife and the body frozen (qa's mask; whole-frame diffs are noisy). Stills go to the director (L3) and Noah **(N)** |

## 5. Design notes

### 5.1 Duck and stop from below (ruled)

- **Duck:** it stays (director amend3, withdrawing amend2 Q3; td part4 A, part15). It is lane-free, from the top
  flight's data, position-only, and does not react to the hatch. My r1 "no duck" rested on a harness figure not of
  record. Removing it later needs measured numbers brought to the director.
- **Stop from below:** `HATCH_HeadroomStop`, with its depth from `StopDepth()` (director amend4; td parts 14-17): derived
  so the eye, not the capsule top, stops ≥ 0.15 m under the leaf. About 0.20 m today, and the build throws above 0.25 m.
  This closes K2 and K3. The first build's log and the engineer's print confirm the value; qa's eye-to-leaf figure
  decides (T7.3).
- A ClimbArea is never a remedy (issue FAIL).

### 5.2 Risks found while reading, and where each went

| # | Risk (r1) | Outcome | Record |
|---|---|---|---|
| K1 | Window-band `CLIP_` boxes on Default occlude the sashes | **Ruled:** `CLIP_BOX_CabBand_01`-`04` on WorldBounds, set by the build; T1.5, T1.7, T4.4 | td part10 K1, part12 (1) |
| K2 | Open refused where the leaf stops the player | **Closed** by the headroom stop; T3.2 | td part4 B, part10 K2; director amend1 (b) |
| K3 | Eye above the tower capsule's top, too near the leaf from below | **Closed** by the derived stop depth, measured eye to leaf; T1.3, T7.3 | td part10 K3; director amend1 (a) |
| K4 | R1(f) collider wall check fails by construction | **Ruled:** collider check withdrawn; visible meshes; T7.6 | td part10 K4; amend2 Q7 |
| K5 | Firefinder eye placed from the steel | **Ruled:** from `cab.floor_y`, with an EditMode check; T1.3b | td part11 Q2 |
| K6 | Firefinder eye values predate the spec of record | **Ruled:** 1.22 / 0.185 m with the A10 ranges; T5.2 | amend2 Q2 |
| K7 | The player's own body in the sight view | **Ruled:** neither body seen nor casting a shadow; T5.9 | amend2 Q4; td part12 (2) |
| K8 | `TowerWalkTests` meets a closed hatch | **Ruled:** `SetState(Open)` before the timer; T7.1 | td part11 Q5; amend2 Q5 |
| K9 | `TrapDoor` reads lane data unguarded | **Closed:** lane-free TrapDoor 8fbfb652 accepted | td part9, part11, part12 (4) |

## 6. Noah's hand check (closes H8; from L9)

qa writes this list into its report; Noah's ratings stand as given.
1. Climb to the closed hatch; see `E – Open hatch`; E; climb into the cab.
2. From the cab: `E – Close hatch`; E; walk across the closed hatch; `E – Open hatch`; walk down through it.
3. Open and close every hinged sash; look down at the body by each open window.
4. Firefinder: `E – Use firefinder`; turn by mouse, by Q/E, by stick; leave with a tap of E and with Esc (or pad East);
   check you stand at normal height with your body back.
5. Look at the open hatch from the cab and from the top flight: any show-through of the east wall, any flicker.
Rows needing this check: T3, T4, T5, T7.7.

## 7. Rulings on the spec's questions

| Q | Question | Ruling | Record |
|---|---|---|---|
| Q1 | Hatch open angle | **91.1518°** from the manifest; `LookoutTuning.hatchOpenAngle` set to it with an EditMode equality check; 92.0 retired; the L3 look check stands | amend2 Q1; td part11, part12 (2) |
| Q2 | Firefinder eye and datum | **1.22 m up, 0.185 m back, from `cab.floor_y`**, ranges 1.21-1.26 / 0.15-0.195; the rim-and-sight-view Proposal 2 conditions stand; a sight still goes to the director before T5 is graded | amend2 Q2; td part11 |
| Q3 | Duck | **Stays** (amend2 Q3 withdrawn) | amend3; td part15 (1) |
| Q4 | Body during the view | **Neither body seen nor casting a shadow** from entry to the end of the blend out; exact restoration | amend2 Q4; td part12 (2) |
| Q5 | `TowerWalkTests` opens the hatch first | **Accepted** as test setup; T3 covers the real flow | td part11; amend2 Q5 |
| Q6 | Tolerances | **Stand as written** unless a result shows one is unachievable; that goes to td by name with the measured value. qa's measuring methods (ground probe, 5° sampling, printed mouse figure, masked pixel counts) are written into T3.6, T4.3, T5.3 and T7.7 | td part11 Q6; amend2 Q6; layers-qa 22:28 |
| Q7 | Wall check | **Against the visible wall**: the outer-face show-through test on my wall selection, the max-y bound and the coplanar check (T7.6) | amend2 Q7; td part10 K4, part14 (2), part18; director 22:37 |
| Q8 | Camera vs capsule top at the stop | **Ruled:** the depth is derived by `StopDepth()` from the eye, about 0.20 m, capped at 0.25 m; confirmed by the first build's log and the engineer's print | amend4; td part13 (2), part16, part17 |
| Q9 | T4.3 band clause and its measure | **Ruled:** the band clause is replaced (amend7); measured as A / B / C (amend7a), with td part79's method; any non-zero count goes to the director with a still | `gate/lookout-interactions-40-amend7-t43-visible-clip`, `-amend7a-t43-measure`; td part77, part79 |

One my-choice value remains, now accepted: the T7.6 wall selection (td part18; director, provided it covers the whole face, which T7.6 (b) asserts).

## 8. Change log

- r1 2026-10-24 ~22:24 CDT: first issue. (Its "22:30" stamp was written without reading the clock.)
- r2 2026-10-24 ~22:29 CDT: td parts 4-6. (Its "22:35" stamp was also not read from the clock.)
- r3 2026-10-24 ~22:33 CDT: director amend1 and amend2, and td parts 7-12. Q1, Q2 and Q4-Q8 ruled; K1-K9 closed
  (5.2). The duck is PENDING the director's amendment: r2's "stays" is withdrawn and both cases are written (3.3,
  T1.3a, T7.4). Also new: the derived stop depth (3.3, T1.3); T1.3b equality checks; CabBand on WorldBounds (3.4, T1.5,
  T1.7); the firefinder eye, datum and sight still (3.5, T5.2); the body and shadow rule (3.5, T5.9); retirements by
  name (T6.1, T6.4); qa's measuring methods (T1.6, T3.6, T4.3, T5.3, T7.7); and the T7.6 wall mesh named.
- r4 2026-10-24 22:36 CDT: director amend3 (duck stays) and amend4 (derived stop depth); td parts 13-17. Duck ON (3.3, T1.3a, T7.4, 5.1; Q3); stop `HATCH_HeadroomStop`, depth `StopDepth()`, cap 0.25 (3.3, T1.3; Q8); T7.3 near plane vs the leaf only, eye to leaf at the stop; T7.6 outer-face show-through test; `ClimbRulesTests` case retired (T6.1).
- r5 2026-10-24 22:38 CDT: td part18. The stop name `HATCH_HeadroomStop` is final (`CLIP_BOX_HatchHeadroomStop` void). T7.6 restores the r3 wall selection (accepted by td part18 and the director) under the outer-face test, and adds (b): the open leaf's max y is below 27.3558. Q7 and the my-choice line are updated.

- r6 2026-10-25 02:18 CDT: T4.4 gets td part59's layout note (the east sash is over the open hatch; dropping in is the designed way down, not an escape; the test closes the hatch first). No criterion changed.
- r7 2026-10-25 02:22 CDT: T4.4 cites the director's `gate/lookout-interactions-40-note-east-window-over-hatch` (no change; L4 applies to this approach). No criterion changed.
- r8 2026-10-25 04:12 CDT: T4.3's leaf-bounds wording is read as the oriented leaf box (td part77 (2a)); this was written after the provisional probe, and the director may revert it. The band clause is marked as under review, with Q9 added; the clause itself is unchanged.
- r9 2026-10-25 04:14 CDT: T4.3 rewritten per director amend7 (`gate/lookout-interactions-40-amend7-t43-visible-clip`): the band clause is dropped and replaced by (b) OUTER FACE and (c) VISIBLE FRAME; the oriented leaf box (td part77) stays; Q9 is ruled.
- r10 2026-10-25 04:15 CDT: T4.3 is now the text of record for amend7a A / B / C, with td part79's measuring convention (depth along the outward normal, 1 mm on B, exact point-in-mesh for C, vacuous rows, worst-pose still). It supersedes r9's (a)-(d) wording; the rule is unchanged.
