# Designer section: stance numbers and rules (A.3, A.4)

Written by designer (systems-designer), 2026-10-18. Tags: **[sheet]** = an existing number of record (file named), **[derived]** = computed from sheet numbers (formula given), **[mine]** = my choice, with the reason. Every **[mine]** value is listed in section 7 for td and qa. Mechanics (how the capsule and eye are applied) are the engineer's (`a1-a4-engineer.md` §3); this file gives the numbers and the rules they serve.

## 0. Inputs read

- Capsule 1.8 / r 0.4 / skin 0.05 / step 0.4 / slope 45, centre y 0.9 [sheet: `Assets/Scenes/World.unity:11872-11878`, `_bmad-output/poc/controller-tuning.md`].
- Eye (CameraPivot local y) 1.65 [sheet: World.unity, CameraPivot transform near line 83619]. Camera near 0.10, far 1000, vertical FOV 70 [sheet: World.unity:83664-83666].
- Walk 5, sprint 8, jump 1.2, gravity -20, accel 20, decel 25 [sheet: `PlayerController.cs`]. Tower: capsule r 0.25 / h 1.6, stairs 1.15, landings 1.5 [sheet: `TowerWalkZones.cs`, issue #8]. None of these change.
- Tower eye rules: R8, eye 0.20 m from every tower surface, "never lowered to pass a check" [sheet: `LookoutTuning.EyeClearanceR8`]; 0.15 m on the ordinary walking line, 0.08 m absolute floor, near plane 0.10 everywhere outside the climb area [sheet: `studio:decisions` `ruling/lookout-tower-near-plane-eye-rule-amend3` C1, C3]. Director greenlight C1-C5 [sheet: `greenlight/player-character`].
- Water: swim float depth 1.30, eye 1.65, rule floatDepth + 0.2 <= eye; wade from 0.05 m depth [sheet: `SwimTuning.cs`]. Parked duck-under: C / LeftCtrl / East, "duck uses that crouch action in water instead of taking a second binding" [sheet: `_bmad-output/poc/water-swim-spec.md:73-76`].

## 1. The eye-headroom rule (applies to every stance)

The standing pair already obeys one rule: **eye = capsule top + skin - 0.20** (1.8 + 0.05 - 0.20 = 1.65) [derived]. A ceiling can come no closer than capsule top + skin, so the eye keeps R8's 0.20 m to anything overhead. I keep this for every stance: **capsule height = eye + 0.15** [derived], and it must hold **every frame, including during a transition**: the eye may never be higher than (current capsule top + 0.05 - 0.20). If the capsule drops at once on crouch while the eye eases down (engineer §3), a player walking crouched under an overhang during the 0.25 s ease can push the eye into it; the engineer either shrinks the capsule with the eye or proves the ease ends first. qa checks it (section 6, S-A3).

Near-plane check [derived, near 0.10, FOV 70]: the near rectangle's corners sit 0.174 m from the eye at 16:9 and 0.204 m at 21:9; its highest point above the eye at any pitch (no roll) is sqrt(0.10² + 0.07²) = 0.122 m. So 0.20 overhead is enough at any pitch. Note for td: at 21:9 the corner reach (0.204) is 4 mm past the 0.20 sphere sideways; that is today's standing rule, unchanged by stances, flagged only.

Lying is the one exception: a CharacterController cannot be shorter than 2 × radius, so the sleep capsule is 0.80 [derived: 2 × 0.4], taller than eye + 0.15. That is harmless (more headroom, not less).

## 2. Stance table (A.3)

| Stance | Eye (m) | Capsule h (m) | Move speed (m/s) | Eye transition | Source of eye |
|---|---|---|---|---|---|
| Stand | 1.65 [sheet] | 1.80 [sheet] | walk 5 / sprint 8 [sheet] | n/a | scene |
| Crouch | 1.20 [mine] | 1.35 [derived] | 1.75 [mine] | 0.25 s each way [mine] | crouch-walk: hips ~0.65, torso ~40° forward, seat-to-eye 0.79 → 0.65 + 0.79·cos40 ≈ 1.26; set lower to read as a crouch |
| Squat | 1.00 [mine] | 1.15 [derived] | 0, stationary [mine] | enter 0.4 s, exit 0.4 s [mine] | deep squat: buttocks ~0.30, torso ~30° forward → 0.30 + 0.79·cos30 ≈ 0.98 |
| Sit (ground) | 0.80 [mine] | 0.95 [derived] | 0 | enter 1.0 s, exit 0.8 s [mine] | seated eye height ~0.79 m above the seat, 50th-percentile adult male (standard anthropometric tables, e.g. Pheasant, *Bodyspace*) |
| Sleep (lying on back) | 0.20 [mine] | 0.80 [derived, CC floor] | 0 | lie down 1.5 s, wake 1.5 s [mine] | eye ~0.17 on bare ground; set to 0.20 so the lying eye keeps R8's 0.20 to flat ground (a folded jacket under the head) |

- Radius never changes with stance (0.4 off the tower) [engineer §3, agreed].
- Speeds: crouch 1.75 = 0.35 × walk [mine]. Reference: Source engine crouch is about 1/3 of run; Unreal's default crouched speed is 1/2 of walk. 1/3 was chosen because walk 5 is already a jog. Tuning range 1.25-2.5. Back and strafe same as forward (walk has no back multiplier) [sheet]. Accel/decel 20/25 unchanged [sheet]. Crouch combines with any speed cap by `min` (engineer §4).
- **H5 tolerance:** eye heights are matched ±3 cm by the clips' hidden head, not the reverse: the camera uses the numbers above, never a bone [engineer §2; required by greenlight C3: no bob or sway].

## 3. Jump, landing and stride matching (A.3)

**Jump physics (unchanged):** take-off speed sqrt(2 · 20 · 1.2) = 6.93 m/s, apex at 0.346 s, 0.693 s in the air on flat ground [derived].

**Feel rules:**
- **No wind-up delay.** The body leaves the ground on the press frame, as today. JumpStart is a take-off pose of ≤ 0.15 s laid over the first part of the rise, not an anticipation that holds the player down [mine: any delay changes the tuned jump feel].
- JumpAir loops from the end of JumpStart until the controller is grounded. Walking off an edge plays JumpAir only after **0.20 s** airborne [mine], so ramp snaps and kerbs don't flicker it.
- JumpLand plays on touchdown only after **≥ 0.25 s** airborne [mine] (shorter = a step, no land clip). Length 0.2-0.3 s; it blends out into locomotion in 0.1 s if moving. **No movement penalty and no camera dip** [mine; a dip is head bob, which C3 forbids on the tower and I don't add elsewhere].
- Crouch, squat, sit and sleep cannot be entered in the air. Walking off an edge while crouched stays crouched in the air.

**Stride matching (H4: ≤ 10 cm foot slide per stride):**
- One locomotion blend on actual horizontal speed and local direction (2D: forward, back, left, right). Its thresholds are **the clips' own measured speeds** (stride length × cycles per second), measured by the artist and recorded in the clip manifest, never guessed [mine: method]. Above the fastest clip, playback rate = actual speed / clip speed.
- Walk 5 m/s is a jog in human terms (real walking tops out near 2 m/s; 5 m/s at a 2.8 steps/s cadence is a 1.8 m step) [derived]. So **the Run clip carries 5 m/s and sprint 8**; the Walk clip carries the tower speeds (1.15, 1.5) and the speed-up/slow-down phases; CrouchWalk carries 1.75.
- **Risk for the artist:** if Run's measured speed needs a playback rate above 1.4 to reach 8 m/s, the legs will look like they spin. Then a scripted longer-stride sprint variant is needed (a Blender script, H3). The slide criterion is met by rate-matching either way; the look is Noah's H9 call.
- qa's slide measure: during each foot plant (foot bone within 3 cm of its lowest height), the foot's world displacement; ≤ 0.10 m every plant at steady walk 5 and sprint 8 on flat ground [mine: measuring convention].

## 4. Foot IK: later, with one thing now (A.3)

**Foot IK goes to a future issue** (it is on R1's list). Why: H4 is graded on flat ground; the body's feet are seen only when looking down; the ground near spawn is ≤ 10° [sheet: controller-tuning spawn flat zone].

Numbers that show what is visible without it [derived: a capsule on a slope θ rests with its foot point r/cosθ - r above the ground, plus the skin]:

| Slope | Off-tower (r 0.4) float incl. 0.05 skin | Tower (r 0.25) |
|---|---|---|
| 10° | 0.057 m | 0.055 m |
| 20° | 0.079 m | 0.069 m |
| 30° | 0.120 m | 0.096 m |
| 45° (terrain limit) | 0.236 m | 0.174 m |
| 52-56° (tower ramps) | n/a | 0.24-0.29 m |

**Now:** the body mesh sits **0.05 m (the skin width) below the capsule's foot point** [derived], so feet touch flat ground. Nothing else. **Later (future issue):** foot IK on slopes and tower treads. **For td/director:** on the tower ramps the feet float 0.24-0.29 m above the ramp line when looking down. That is new and visible. It goes to director under greenlight C3 if the chosen view shows it.

## 5. Rules between stances (A.4)

### 5.1 Bindings (agreeing with engineer §5)

| Stance | Keyboard | Gamepad | Mode |
|---|---|---|---|
| Crouch | Left Ctrl | East | toggle [mine; engineer offered toggle] |
| Squat | C | D-pad Down | toggle |
| Sit | X | D-pad Left | toggle |
| Sleep | Z | D-pad Right | toggle |

Duck-under reservation (C5): water-swim-spec says duck "uses that crouch action in water". So when duck-under is built, it is **Left Ctrl / East in water**; C (squat) does nothing in water. That releases C from the old claim [derived from the swim spec's own sentence; td to confirm in the audit]. No stance uses South; South stays jump + interact as today.

### 5.2 Enter matrix (rows: from; what each key does)

| From | Ctrl / East | C / D-Down | X / D-Left | Z / D-Right | Move | Jump | Sprint held |
|---|---|---|---|---|---|---|---|
| Stand | → Crouch | → Squat | → Sit (if still) | → Sleep (if still) | walk | jump | sprint |
| Crouch | → Stand (headroom) | → Squat | → Sit (if still) | → Sleep (if still) | crouch-walk 1.75 | → Stand only, no jump [engineer §4] | → Stand, then sprint (headroom) |
| Squat | → Crouch | → back to the stance it came from (headroom) | → Sit | → Sleep | → back to the stance it came from | → Stand (headroom) | → Stand |
| Sit | → Crouch (headroom) | → Squat | → Stand | → Sleep (lie back) | → Stand | → Stand | ignored |
| Sleep | wake → Stand | wake → Stand | wake → Stand | wake → Stand | wake → Stand | wake → Stand | ignored |

- "Still" = horizontal speed ≤ 0.10 m/s [mine]. Pressing sit or sleep while moving is ignored (no queue).
- Squat entered while moving: the player slows to 0 at the normal 25 m/s² deceleration (8 → 0 in 0.32 s) during the 0.4 s enter [derived].
- **Look and interact never stand you up or wake you** [engineer §4; mine for sleep: a bumped mouse must not wake].
- **No headroom when standing:** the request is remembered and the player stands as soon as there is room [mine; engineer keeps it 0.25 s, I ask for "until another stance key cancels it": a player who pressed Ctrl under a beam and crawls out expects to stand]. If neither stand nor crouch fits (rare, sitting under a low ledge), move input does nothing; no slide, no message.
- Wake and exits go to Stand. If Stand has no headroom, they go to Crouch, then Squat, then stay put [mine].

### 5.3 Blocked contexts (all four lowered stances unless said)

| Context | Rule | Source |
|---|---|---|
| Airborne | entry blocked | spec H6 |
| Any wading or swimming (`WaterMode != Dry`) | blocked; if lowered when water arrives, stand at once | engineer §4; [mine] adopted: SwimTuning assumes the standing eye, and the camera never goes under (C4) |
| Tower: anywhere the capsule is restricted (footprint), on ramps or decks (`WalkSpeedCap > 0`), climb areas | blocked; a lowered player walking in stands up as soon as the tower capsule fits | [mine], closes C3 by blocking, not measuring: no lowered eye exists on the tower, so the 0.15 / 0.20 / 0.08 m figures and the 0.10 near plane hold as measured today for standing |
| Hatch duck, firefinder, control suspended, view overridden | stance input ignored; the frame control resumes is skipped | engineer §4 |
| Sit: ground slope over 25° under the capsule | sit blocked | [mine] |
| Sleep: slope over 15°, or ground height across the lie box differing by more than 0.5 m | sleep blocked | [mine] (0.5 m over 1.9 m ≈ 15°) |
| Sit: sit box (0.9 m ahead of the capsule axis, 0.6 m wide, 0.15-0.6 m above ground) hits a collider | sit blocked | [mine]: legs go forward |
| Sleep: lie box (1.9 m ahead of the axis, 0.7 m wide, 0.15-0.5 m above ground) hits a collider, or any water surface above the ground anywhere in it, or anything within 0.20 m of the lying eye point | sleep blocked | [mine]; the 0.20 is R8 at the lying eye [derived] |
| Respawn or `Teleported` | Stand at once, fade cleared | engineer §4 |

Blocked presses do nothing visible or audible. No HUD (C1).

## 6. Camera when sitting and sleeping (A.4)

- **The camera follows the stance numbers only, never a bone.** It moves only vertically (and in sleep, in pitch), eased with smoothstep over the transition time in §2. No bob, sway, roll or FOV change in any stance [C3; mine off the tower].
- **Sit:** eye 1.65 → 0.80 over 1.0 s. Look stays free (yaw turns the seated body, as when standing; pitch -85..+85). The interaction prompt is hidden while sitting [mine: the only interactables are on the tower, where sitting is blocked anyway].
- **Sleep:** from Stand: eye to 0.80 by 0.6 s, then to 0.20 by 1.5 s, while pitch eases to **-60°** (looking up at the sky or canopy) [mine]. Look input is then locked. The screen **dims to 15% brightness over 2.0 s** after the lie-down ends [mine], by exposure or a full-screen fade, never text or icons (C1). No time skip (sleep tied to needs is a future issue).
- **Wake:** fade up over 0.5 s, starting at once; eye and pitch reverse over the 1.5 s wake (pitch returns to 0); control returns when the wake ends.
- **Never underwater (C4):** sit and sleep need dry ground; the sleep lie box needs no water above the ground; lowered stances stand up at the first wade. The lowest lying eye (0.20) is never over water.
- **Inside the body (H5):** with the eye fixed by stance and the body pushed back (engineer §2), the qa pitch sweep runs at each of the five eye heights.

## 7. My-choice values (for td and qa before build)

Crouch eye 1.20; squat eye 1.00; sit eye 0.80; lying eye 0.20; crouch speed 1.75 (range 1.25-2.5); squat stationary; crouch ease 0.25 s; squat 0.4 / 0.4 s; sit 1.0 / 0.8 s; sleep 1.5 / 1.5 s; sleep pitch -60°; dim to 15% over 2.0 s; fade-up 0.5 s; JumpStart ≤ 0.15 s with no delay; JumpAir after 0.20 s of falling; JumpLand after ≥ 0.25 s airborne; no land penalty or camera dip; "still" ≤ 0.10 m/s; sit slope 25°; sleep slope 15° / 0.5 m; sit and lie box sizes; stand request held until cancelled; exit fallback Stand → Crouch → Squat; toggle for all four; Run carries 5 m/s; foot-plant measuring convention; lowered stances blocked in all water and the whole tower.

## 8. Testable criteria for qa (proposed)

- **S-A1:** each stance's pivot height equals §2 ±0.01 m once settled; capsule height equals §2 ±0.01 m.
- **S-A2:** crouch speed 1.75 ±0.05 m/s; walk 5, sprint 8, jump 1.2 and tower speeds unchanged.
- **S-A3:** every frame of every transition, eye ≤ capsule top + 0.05 - 0.20 (+0.005 tolerance), including crouch-walking under a 1.45 m bar placed 0.3 m ahead at the crouch press.
- **S-A4:** stand under a 1.5 m ceiling → stays crouched; walk out → stands within 0.1 s of clearing it.
- **S-A5:** the §5.2 matrix and every §5.3 block, one PlayMode case each.
- **S-A6:** look-only input for 10 s during sleep does not wake.
- **S-A7:** foot slide per §3.

## 9. Open for td

1. Is §1's every-frame rule acceptable as written, or do you rule the engineer's capsule-first, eye-eases approach with the ease proven shorter than the overhang case?
2. Feet floating 0.24-0.29 m on the tower ramps (§4): send to director now, or wait for the chosen view?
3. Release of C from the duck-under claim (§5.1).
