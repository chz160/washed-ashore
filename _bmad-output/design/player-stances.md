---
title: 'Player stances: numbers and rules of record'
owner: designer-2 (systems-designer)
consumers: engineer-2, qa-2, artist, td
created: '2026-10-18'
status: 'OF RECORD for the values in §1-§8. Phase 0 gate passed 2026-10-18 (td rulings; director studio:decisions gate/player-character-phase0 rulings 3-5). §3a H9 ranges (gate/player-character-phase0-amend1-h9-ranges): times and dim approved; eye ranges proven (eng-c12a) and sleep pitch proven (eng-b9c); crouch speed 1.25-2.50 proven with fail-closed check (qa-run3, eng-d14f). All §3a ranges are approved or proven. All other §3a ranges are approved or proven.'
basis: _bmad-output/research/technical-player-character-2026-10-18/a3-a4-designer.md (reasoning and anthropometric sources)
---

# Player stances: numbers and rules of record

Tags: **[sheet]** = an existing number of record (file named) · **[derived]** = computed from sheet numbers (formula given) · **[mine]** = designer-2's choice · **[td]** = td ruling, 2026-10-18. Every [mine] value is listed in §8. Reasoning is in the research section named above.

## 1. Fixed inputs (do not change)

| Value | Number | Tag |
|---|---|---|
| Standing capsule h / r / skin / step / slope | 1.80 / 0.40 / 0.05 / 0.40 / 45° | [sheet] World.unity:11872-11878 |
| Standing eye (CameraPivot local y) | 1.65 | [sheet] World.unity CameraPivot |
| Camera near / far / vertical FOV | 0.10 / 1000 / 70 | [sheet] World.unity:83664-83666 |
| Walk / sprint / jump / gravity / accel / decel | 5 / 8 / 1.2 / -20 / 20 / 25 | [sheet] PlayerController.cs |
| Tower capsule r / h; stairs / landings speed | 0.25 / 1.6; 1.15 / 1.5 | [sheet] TowerWalkZones.cs, issue #8 |
| Tower eye rules | R8 0.20 m to any tower surface; 0.15 m ordinary walking line; 0.08 m absolute floor | [sheet] LookoutTuning.EyeClearanceR8; studio:decisions ruling/lookout-tower-near-plane-eye-rule-amend3 C3 |

## 2. Eye-headroom rule (every stance, every frame)

- **Eye ≤ capsule top + skin − 0.20** (= capsule top − 0.15) on every frame, transitions included [derived from 1.80 + 0.05 − 0.20 = 1.65; td ruling 1].
- Settled capsule height = eye + 0.15 [derived]. Exception: sleep capsule 0.80, the CharacterController floor (2 × r) [derived].
- **Going down:** the capsule follows the eye every frame (h = eye + 0.15) [td].
- **Going up:** headroom check for the target capsule first; the capsule grows to the target; then the eye eases up [td].
- Radius never changes with stance [td, engineer §3].
- The camera follows these numbers only, never a bone. No bob, sway, roll or FOV change in any stance [greenlight C3; mine off the tower].

## 3. Stance table

| Stance | Eye (m) | Capsule h (m) | Speed (m/s) | Enter / exit time (s) |
|---|---|---|---|---|
| Stand | 1.65 [sheet] | 1.80 [sheet] | 5 walk / 8 sprint [sheet] | n/a |
| Crouch | 1.20 [mine] | 1.35 [derived] | 1.75 [mine], tuning range 1.25-2.5 | 0.25 / 0.25 [mine] |
| Squat | 1.00 [mine] | 1.15 [derived] | 0, stationary [mine] | 0.4 / 0.4 [mine] |
| Sit (ground) | 0.80 [mine] | 0.95 [derived] | 0 | 1.0 / 0.8 [mine] |
| Sleep (on back) | 0.20 [mine] | 0.80 [derived] | 0 | 1.5 lie down / 1.5 wake [mine] |

- Crouch back and strafe = forward [sheet: walk has no back multiplier]. Accel / decel 20 / 25 [sheet]. Combined with any speed cap by `min` [engineer §4].
- Eye moves use smoothstep over the times above [mine].
- H5 ±3 cm: the clips' hidden head matches these eye heights; the camera never follows the head [engineer §2; director gate ruling 1].
- Eyes, capsules and the sleep dim are approved [director gate ruling 3].
- **H5 measuring convention** [td combined ruling 2026-10-18]:
  - **Eye point** = Head bone + **0.1094 m up, 0.1011 m forward** (mean of the Head mesh's Eye-material vertices at rest, Men Farmer_Head) [sheet: `_bmad-output/poc/character-clip-manifest.md` Eye point table, line 29; rule td 2026-10-18]. H5 is graded on the men's Farmer, the default (`Assets/Art/Character/Appearances/Men_Farmer.asset`). The artist switched to it from Worker (hi-vis vest, hard hat) under director's pre-approval [studio:decisions gate/player-character-phase0 ruling 2; artist's switch]. The sweep's current Head + 0.103 up + 0.08 forward is a placeholder with no recorded source, not of record.
  - It is read on the **SHADOW body** and measured from the **player origin** (capsule foot point), with the drop h (§4) applied.
  - **Clip eye target = stance eye + h** (in clip space, above the clip floor). With h = 0.05 (§4): **stance eye + 0.05, ±0.01** [td h-ruling 2026-10-18, received directly].
- **Standing eye stays 1.65; no tolerance exception** [sheet: R8, the walking-line sweeps and SwimTuning are measured at 1.65].
- **Pivot-local eyes** [td h-ruling, received directly]: the hover h = skinWidth 0.05 [td].
  - Every eye in §3 is the CameraPivot's local y above the player origin. The origin hovers h = 0.05 above the ground, so the camera eye above the ground = pivot + 0.05: 1.70 / 1.25 / 1.05 / 0.85 / 0.25 for Stand / Crouch / Squat / Sit / Sleep [td]. Nothing in the table changes.
  - The §2 headroom rule is relative to the capsule, so it is unchanged.
  - The lying eye keeps R8's 0.20 to flat ground with 0.05 to spare [derived].
- **Skeleton scale (men's only, uniform): YES** [td h-ruling 2026-10-18, received directly; director pre-acceptance: studio:decisions gate/player-character-phase0-amend3-two-body-and-scale ruling 2]. It reverses the earlier withdrawal (td combined ruling), because h = 0.05 > 0.03.
  - **s = (1.65 + h) / stand clip eye** at the Farmer eye point, unscaled clip space [td]. Measured stand clip eye at the manifest point, unscaled, feet planted: **1.651** (pack Idle 1.649-1.654; authored stand 1.650) [sheet: artist report 2026-10-18]. This is below 1.67, so a scale is needed. **s = 1.030, CONFIRMED by td** (men's only) [derived: (1.65 + h 0.0500) / 1.651, the pack Idle stand eye at the manifest eye point; rounded to 0.001; td FINAL 2026-10-18. The authored stand at 1.650 gives the same 1.030]. **Lowered clip targets, unscaled clip space** = (stance eye + 0.05) / 1.030 [derived; td]: crouch **1.214**, squat **1.019**, sit **0.825**, sleep **0.243**, each ±0.01. **Landed** [sheet: manifest, artist 2026-10-18; build and tests 110/110]. Scaled eye above the floor: Idle 1.698-1.704, Crouch_Idle 1.249-1.250, Squat_Hold 1.049-1.051, Sit_Loop 0.850-0.851, Sleep_Loop 0.240-0.261. All are within ±0.01 of stance eye + 0.05 except Sleep_Loop's top, 0.261, which is 1 mm over; it is inside H5's ±3 cm. **Accepted deviation, no re-author** [td ruling 2026-10-18: ±0.01 is td's authoring tolerance; H5's ±3 cm governs]. Stand is fixed by the scale, not by a re-author [td].
  - **No scale if the stand clip eye ≥ 1.67** [td]. At 1.67 the Stand mismatch is −0.03, the edge of ±3 cm [derived].
  - Lowered clips and §3a range ends are re-authored by script to stance eye + 0.05, ±0.01, **on the final (scaled) skeleton** [td].
  - Director amend3 conditions stand: capsule, radius, eye and speeds unchanged; clip ground speeds re-measured (H4); S-A9 and the H5 sweep re-run on the scaled body; landing-6 stills and the H8 fit with the scaled view body; growth of the <2.5 cm overlap goes back to director.
  - **Detail from td's verbatim text** (received directly, 2026-10-18):
    - The scale goes on the skeleton root of both bodies, same value; the feet stay at the origin.
    - The final s is rounded to 0.001.
    - The lowered-clip script takes s as input and targets (stance eye + 0.05) / s in unscaled clip space. The artist's held +0.05 set lands as is only if there is no scale.
    - **Order:** artist measures the Farmer eye point → sends the stand clip eye to td → td confirms s → artist scales, re-authors lowered clips, re-measures clip speeds and pose bounds (manifest) → engineer-2 re-runs the sweep → look-down work continues.
    - **Effects at s = 1.028** [td, derived]: playback rates are jog 1.12, sprint 1.11 and crouch 0.95, all ≤ 1.4 and inside the §3a band. The body is 2.8% wider, but visual only (0 colliders), so the flight fit is unchanged. Landing-6 stills are taken after the scale.
    - **Director limits:** s > 1.06 or floating feet go back to director [td, director check].
  - Women's: no scale [td]. The women's eye is not the player in this pod; it goes to future-character-02 (T3).
  - The eye match is read on the SHADOW body: **every stance within ±3 cm** [td].
- **Two bodies** [td (c), (d); director amend3 ruling 1]:
  - The **VIEW body** (Body, Legs, Feet; shadows Off) is offset back by f(stance, pitch) for Stand and Crouch only; f = 0 in Squat, Sit and Sleep. f eases across stance changes.
  - The **SHADOW body** (all 4 slots, shadows only) stays at base placement.
  - The camera and stance eyes never move.
  - Neither body has a Collider. The capsule comes only from §3 and the tower, so the flight fit is unchanged.
  - Landing-6 stills use the view body.
- CrouchWalk measured ground speed 1.786 m/s on the scaled body (the clip is authored at 1.786 / 1.030) [sheet: artist's clip manifest]. Crouch-speed range 1.25-2.50 gives playback rates 0.70-1.40; the §3a band is inclusive at both ends [derived].

### 3a. H9 retune ranges: all approved or proven [ ranges mine; rules director gate/player-character-phase0-amend1-h9-ranges]

A range claims both its ends work. Noah's H9 may retune inside a range without returning to director only once its status reads **approved** or **proven**.

| Value | Of record | Range | Status | Proof needed at BOTH ends (amend1 (b)) |
|---|---|---|---|---|
| Enter/exit times in §3 | as listed | 0.5× to 1.5× | **approved** (amend1 (a)) | none |
| Sleep dim | 15% | 10-30% | **approved** (amend1 (a)); Volume only, never black | none |
| Crouch eye | 1.20 | 1.10-1.30 | **proven** (eng-c12a.xml RangeEndTests.EyeEnds_LowAndHigh: eye error −0.010 to +0.011 m against ±0.030; sweep −85..+85 near-slab 0, back faces 0; crouch ends refused under a 1.65 m ceiling) | H5 pitch sweep −85..+85 with no clipping; H5 hidden head within 3 cm; S-A4 headroom |
| Squat eye | 1.00 | 0.90-1.10 | **proven** (eng-c12a.xml RangeEndTests.EyeEnds_LowAndHigh: eye error −0.010 to +0.011 m against ±0.030; sweep −85..+85 near-slab 0, back faces 0; crouch ends refused under a 1.65 m ceiling) | same as crouch eye |
| Sit eye | 0.80 | 0.75-0.90 | **proven** (eng-c12a.xml RangeEndTests.EyeEnds_LowAndHigh: eye error −0.010 to +0.011 m against ±0.030; sweep −85..+85 near-slab 0, back faces 0; crouch ends refused under a 1.65 m ceiling) | same as crouch eye |
| Sleep eye | 0.20 | 0.20-0.25 | **proven** (eng-c12a.xml RangeEndTests.EyeEnds_LowAndHigh: eye error −0.010 to +0.011 m against ±0.030; sweep −85..+85 near-slab 0, back faces 0; crouch ends refused under a 1.65 m ceiling) | at 0.25: H5 sweep, hidden head within 3 cm |
| Sleep pitch | −60° | −45° to −85° | **proven** (range-sleep-pitch.txt, sleep-pitch-45/-85.png, eng-b9c) | one still each at −45° and −85°, with the sleep sweep |
| Crouch speed | 1.75 | **1.25-2.50** | **proven** [sheet: `TestResults/player-character/qa-run3/character-playmode.xml` (qa-2, independent) and `eng-c12a.xml`; 60 fps clips; 2026-10-19] | FootSlideTests.CrouchSpeeds_RangeEnds passed. Slides: 1.25 → 0.027 m at rate 0.705 (eng-c12a); 1.50-2.50 → 0.000 m; 2.50 at rate 1.400 passes the inclusive edge after #14's 0.0001 tolerance (qa-run3). **Fail-closed proven:** ForcedBad_RateStuckAtOne_MustTurnRed printed "EXPECTED RED: got RED: slide 0.268 m, rate 1.00" (qa-run3 and `eng-d14f.xml`). The earlier shrink to 1.75-2.50 is void: it rested on the faulty eng-b9c reading. Low end rate check: 1.786 is on-body, so 1.25 → 0.700 (artist; td code check CharacterClipSet.cs:45-67) |

- Hidden-head match at a range end is made by a script-authored clip variant or a measured pose, never a hand step (amend1 (b)).
- **An end that fails** shrinks its range to the last value that passes. producer-2 records it with no return to director. A range is never widened without director (amend1 (c)).
- Every retune keeps capsule = eye + 0.15 and the §2 every-frame rule. Ordering checks for any retune: crouch − squat ≥ 0.10 and squat − sit ≥ 0.10.

### 3b. H9 range-end settings (dev build launch argument) [td 2026-10-19; values from §3a]

H9 retunes use **only the recorded range ends**, chosen by a dev-build launch argument (for example `-bbStance crouch=low`) [td]. engineer-2 codes against this table. Eyes are pivot-local (camera above the ground = eye + 0.05). Capsule = eye + 0.15 [derived, §2], except sleep, which stays at the 0.80 floor [derived].

| Key | Setting | Eye (m) | Capsule h (m) | Speed (m/s) | Clips (manifest) | Status |
|---|---|---|---|---|---|---|
| crouch | low | 1.10 [§3a] | 1.25 [derived] | crouchspeed setting | Crouch_Idle_Low, CrouchWalk*_Low | proven (eng-c12a) |
| crouch | record | 1.20 [mine] | 1.35 [derived] | crouchspeed setting | Crouch_Idle, CrouchWalk* | of record |
| crouch | high | 1.30 [§3a] | 1.45 [derived] | crouchspeed setting | Crouch_Idle_High, CrouchWalk*_High | proven (eng-c12a) |
| squat | low | 0.90 [§3a] | 1.05 [derived] | 0 | Squat_Hold_Low; Squat_Enter_Low / Squat_Exit_Low **pending artist** | proven (eng-c12a) |
| squat | record | 1.00 [mine] | 1.15 [derived] | 0 | Squat_Hold | of record |
| squat | high | 1.10 [§3a] | 1.25 [derived] | 0 | Squat_Hold_High; Squat_Enter_High / Squat_Exit_High **pending artist** | proven (eng-c12a) |
| sit | low | 0.75 [§3a] | 0.90 [derived] | 0 | Sit_Loop_Low; Sit_Enter_Low / Sit_Exit_Low **pending artist** | proven (eng-c12a) |
| sit | record | 0.80 [mine] | 0.95 [derived] | 0 | Sit_Loop | of record |
| sit | high | 0.90 [§3a] | 1.05 [derived] | 0 | Sit_Loop_High; Sit_Enter_High / Sit_Exit_High **pending artist** | proven (eng-c12a) |
| sleep | low = record | 0.20 [mine] | 0.80 [derived, CC floor] | 0 | Sleep_Loop | of record (the range's low end is the record value) |
| sleep | high | 0.25 [§3a] | 0.80 [derived, CC floor] | 0 | Sleep_Loop_High; Sleep_Down_High / Sleep_Wake_High **pending artist** | proven (eng-c12a) |
| crouchspeed | low | n/a | n/a | 1.25 [§3a] | CrouchWalk*, rate 0.70 | proven (eng-c12a, qa-run3; fail-closed proven) |
| crouchspeed | record | n/a | n/a | 1.75 [mine] | rate 0.98 | of record |
| crouchspeed | high | n/a | n/a | 2.50 [§3a] | rate 1.40 | proven (qa-run3: slide 0.000, rate 1.400) |

- **Independent keys** [mine]: crouch eye and crouch speed are separate, so `crouch=` and `crouchspeed=` combine freely. Eye ends pick clips; speed only changes the playback rate.
- **Ordering check, fail-closed** [td 2026-10-19]: after the overrides, each stance's eye must be ≥ 0.10 above the next lower stance's (crouch − squat, squat − sit). The comparison uses an epsilon, diff ≥ 0.10 − 0.0001, so exact 0.10 gaps pass despite float rounding [qa-2; mine: epsilon value, matching #14's]. If any pair breaks it, the **whole** argument is refused, every stance stays at record, and one error is logged naming the pair. No partial apply, nothing on screen (C1).
  - **Refused (6 of the 27 crouch × squat × sit combinations)** [derived; qa-2]: crouch=low with squat=high (1.10 against 1.10), with any sit (3 combinations); squat=low with sit=high (0.90 against 0.90), with any crouch (3 combinations). My earlier text named only the first pair; qa-2 found the second.
  - **Accepted:** the other 21, including the exact-0.10 gaps: crouch=low with squat=record, crouch=record with squat=high, squat=low with sit=record, squat=record with sit=high.
  - Tests: engineer-2 tests both refused pairs (refused, all at record, error logged). qa-2 runs all 27 when #23 lands, and qa-2's H9 sheet lists only accepted combinations.
- **Between the ends:** an eye value between the ends needs a rebuild with script-re-authored clips (clip eye = (eye + 0.05) / 1.030), and then the H5 proof [td]. A crouch speed between the ends needs no new clips (rate only), but this build's launch argument offers only low / record / high [td].
- **Transition variants required** [td 2026-10-19]: record-height transitions are not acceptable at range ends. The body would pop by the eye difference (for example 0.10 m) at the enter→hold and hold→exit hand-overs, which shows on the debug camera and in the shadow. The artist authors the _Low/_High transitions by the same script, with the eye target as a parameter: Squat_Enter/Exit and Sit_Enter/Exit at both ends, Sleep_Down/Wake at high only (low = record). Crouch has no enter clip (eye ease only). **Test:** at every transition→hold and hold→exit boundary, at each end, hips and head move ≤ 0.02 m between consecutive frames. The squat, sit and sleep end settings are not ready for H9 until those clips land and pass.

## 4. Jump, landing, stride, feet

- Jump: take-off 6.93 m/s, apex 0.346 s, 0.693 s airborne on flat ground [derived: sqrt(2 · 20 · 1.2)].
- The body leaves the ground on the press frame. JumpStart ≤ 0.15 s, overlaid on the rise, no wind-up delay [mine].
- JumpAir: after JumpStart; after walking off an edge, only once 0.20 s airborne [mine].
- JumpLand: only after ≥ 0.25 s airborne, length 0.2-0.3 s, blends to locomotion in 0.1 s. No speed penalty, no camera dip [mine].
- Locomotion blend on actual horizontal speed and local direction. Thresholds = the clips' measured speeds (stride × cycles/s) from the artist's clip manifest [mine: method]. Above the fastest clip, playback rate = speed / clip speed.
- **Clip set and measured speeds, scaled men's body** [sheet: `_bmad-output/poc/character-clip-manifest.md` clip table, generated by make_manifest.py, artist 2026-10-18]:
  - Walk 1.377 m/s carries 1.15 / 1.5 (rates 0.84 / 1.09).
  - Run (pack) 3.126 m/s covers the accel phase.
  - **Jog** (authored) 4.478 m/s carries walk 5 (rate 1.12).
  - **Sprint** (authored) 7.21 m/s carries 8 (rate 1.11).
  - CrouchWalk 1.786 m/s carries 1.75 (rate 0.98; 0.70 at 1.25, 1.40 at 2.50).
  - Drift per plant ≤ 0.035 m on every locomotion clip, under the 0.10 bar.
  - This replaces my earlier "Run carries 5 and 8" [derived], which assumed the pack Run alone.
- The sprint-clip rule (a scripted longer-stride sprint when Run would need a rate > 1.4) is **met** by the authored Jog and Sprint clips [mine: threshold].
- **Foot slide measure** [td ruling 2026-10-18, tightening my convention]:
  - A plant = the foot bone within 3 cm of its lowest height.
  - Slide per plant = the **largest horizontal distance between any two samples** of the foot during that plant.
  - Sampling: every frame in-game, or at 4× the key rate in Blender. This is the same measure as the manifest's Drift column.
  - **Pass: ≤ 0.10 m every plant** at steady forward Jog 5 and Sprint 8 m/s on flat ground [sheet: spec H4 bar; td].
  - **Scope: forward only**, at walk 5 (Jog) and sprint 8 (Sprint): **narrowed by Noah** [Noah via producer-2, 2026-10-18]. Strafe and back slide are information only, tracked in #35.
  - The same measure applies to the §3a crouch-speed range ends and S-A7.
- **Body drop = h, the measured controller hover**, applied as a constant to **both bodies** so the feet touch the ground [td ruling (b), no 0.02 cap; td combined ruling (a), 2026-10-18]. No ground probe, raycast or per-frame body drop [director gate ruling 5]. **h = 0.0500 m** [sheet: `TestResults/player-character/eng-hover1.xml`, World scene run 2026-10-18T12:23:22, "capsule bottom minus floor top"]. h > 0.03 went to td under td's stop rule. td ruled: scale YES, and re-author to stance eye + 0.05 (§3). If h ≤ 0.03 m, H5 and the feet both pass. If h > 0.03 m, engineer-2 stops and brings the number to td. The pivot and stance eyes never move, and the tolerance does not change [td]. The earlier 0.05 m (skin) figure is superseded. **Foot IK: future issue** [mine].
- Ramp foot float 0.24-0.29 m on the tower (r 0.25 at 52-56°, incl. skin) [derived: r/cosθ − r + 0.05/cosθ, a 0.05 hover with **no** body drop subtracted]. With the §4 drop h applied, float = (r + h)(1/cosθ − 1) [derived]: h 0 → 0.156-0.197; h 0.03 → 0.175-0.221; h 0.05 → 0.187-0.236 m. Every h gives less float than the accepted figure. **At h = 0.05: 0.187 m (52°) to 0.237 m (56°), and 0.241 m on the top flight at 56.3°** [derived; h from eng-hover1.xml; flight pitch 52-56° from TowerWalkZones.cs, top flight 56.3° from tower-restart-rulings.md §3]. Sent to producer-2 for issue #32. **Accepted as placeholder** and logged to the foot-IK future issue with these figures [director gate ruling 5]. If Noah's H9 rates the stairs "not right" because of it, that goes back to director.

## 5. Rules between stances

### 5.1 Bindings (all toggles) [mine; engineer §5; td ruling 3]

| Stance | Keyboard | Gamepad |
|---|---|---|
| Crouch | Left Ctrl | East |
| Squat | C | D-pad Down |
| Sit | X | D-pad Left |
| Sleep | Z | D-pad Right |

- South stays Jump + Interact. East is shared only with Firefinder leave (focus-scoped; the resume frame is skipped).
- Duck-under, when built, uses the Crouch action (Left Ctrl / East) in water. C is inert in water [td 3; water-swim-spec.md:73-76].

### 5.2 Matrix (row = current stance, column = input)

| From | Ctrl/East | C/D-Down | X/D-Left | Z/D-Right | Move | Jump | Sprint held |
|---|---|---|---|---|---|---|---|
| Stand | Crouch | Squat | Sit if still | Sleep if still | walk | jump | sprint |
| Crouch | Stand* | Squat | Sit if still | Sleep if still | 1.75 | Stand*, no jump | Stand*, then sprint |
| Squat | Crouch | previous stance* | Sit | Sleep | previous stance* | Stand* | Stand* |
| Sit | Crouch* | Squat | Stand* | Sleep | Stand* | Stand* | ignored |
| Sleep | wake → Stand* | wake → Stand* | wake → Stand* | wake → Stand* | wake → Stand* | wake → Stand* | ignored |

\* needs headroom. Rules for the matrix:
- **No headroom:** the stand request is held until another stance key cancels it, then the player stands as soon as there is room [mine; td].
- **Exit fallback:** Stand → Crouch → Squat → stay put [mine].
- "Still" = horizontal speed ≤ 0.10 m/s. Sit or sleep pressed while moving is ignored [mine].
- Squat entered while moving: the player decelerates at 25 m/s² during the enter [derived].
- Look and interact never stand you up or wake you [mine; engineer §4].

### 5.3 Blocked contexts

| Context | Rule | Tag |
|---|---|---|
| Airborne | entry blocked; a crouch held off an edge stays crouched | spec H6 |
| Any wading or swimming | all lowered stances blocked; lowered on entry → stand at once | [mine]; approved, director gate ruling 4 (beyond H6) |
| Tower: footprint (capsule restricted), ramps/decks (`WalkSpeedCap > 0`), climb areas | all lowered stances blocked; a lowered player stands as soon as the tower capsule fits | [mine]; approved, director gate ruling 4 (beyond H6); closes greenlight C3 by blocking. Sitting on the top deck is a future issue |
| Hatch duck, firefinder, control suspended, view overridden | stance input ignored; resume frame skipped | engineer §4 |
| Sit | slope > 25°, or sit box hits a collider (0.9 m ahead of the axis, 0.6 m wide, 0.15-0.6 m above ground) | [mine] |
| Sleep | slope > 15°; or ground across the lie box varies > 0.5 m; or the lie box hits a collider (1.9 m ahead, 0.7 m wide, 0.15-0.5 m above ground); or any water surface above the ground in the box; or anything within 0.20 m of the lying eye | [mine]; 0.20 = R8 at the lying eye [derived] |
| Respawn / `Teleported` | Stand at once, standing capsule, fade cleared | engineer §4 |

Blocked presses do nothing visible or audible (greenlight C1: no HUD).

### 5.4 Clarifications (engineer-2's readings, confirmed by designer-2, 2026-10-18)

1. "Sprint held" in §5.2 means sprint held **with** move input. Sprint alone while still in Crouch or Squat does nothing.
2. Every upward capsule change gets the headroom check, including Squat → Crouch and Squat → previous stance.
3. While in Sit or Sleep (enter, loop and exit), the Interactor is disabled, which hides the prompt. It is re-enabled **on every way out**: stand, crouch or squat from Sit, wake, respawn or `Teleported`. Not only on respawn.
4. Boxes are measured from the capsule axis forward. Sit: 0.6 m wide, 0.9 m long, band 0.15-0.6 m above the feet. Sleep: 0.7 m wide, 1.9 m long, band 0.15-0.5 m above the feet. The sleep ground check samples 5 points along the length: max − min ≤ 0.5 m. The slope ≤ 15° under the capsule, the no-water-above-ground check and the 0.20 m clear sphere at the lying eye still apply.
5. During the 1.5 s lie-down and the 1.5 s wake, all stance keys, move and look are ignored [mine; an H9 feel item]. After the lie-down ends, any §5.2 sleep-row input wakes.
6. Gamepad South keeps Interact-first. A South press taken by a prompt is not a jump, so it never stands a crouched or squatting player. A South press with no prompt is a jump press: stand per §5.2.

## 6. Camera: sit and sleep

- **Sit:** eye 1.65 → 0.80 over 1.0 s. Look free (yaw turns the seated body; pitch −85..+85) [mine]. Interaction prompt hidden [mine].
- **Sleep:** eye 1.65 → 0.80 by 0.6 s → 0.20 by 1.5 s, while pitch eases to −60° (looking up) [mine]. Look then locked.
- **Dim:** to 15% brightness over 2.0 s after the lie-down ends, by a **URP Volume post-exposure** change (≈ −2.7 EV), never a Canvas, text or icon [mine for the numbers; td for the method; greenlight C1]. No time skip.
- **Wake:** exposure back over 0.5 s, starting at once; eye and pitch reverse over 1.5 s (pitch to 0); control returns at the end [mine].
- **Never underwater (C4):** see §5.3 water rows.
- **Pitch sweep (H5):** runs at all five eye heights. In sleep it covers the full −85..+85 even though look is locked [td].

## 7. Acceptance criteria for qa-2

| # | Criterion |
|---|---|
| S-A1 | Settled pivot height per §3 ±0.01 m; capsule height per §3 ±0.01 m |
| S-A2 | Crouch speed 1.75 ±0.05 m/s; walk 5, sprint 8, jump 1.2, tower 1.15 / 1.5 unchanged |
| S-A3 | Every frame of every transition: eye ≤ capsule top − 0.15 (+0.005). Includes crouch-walking under a bar at (crouch capsule + 0.10) m, placed 0.3 m ahead at the crouch press (1.45 m at record values) |
| S-A4 | Pressing stand under a 1.65 m ceiling → stays crouched; walking out → stands within 0.1 s of clearing it. Another stance key in between cancels the stand. 1.65 sits above the highest crouch capsule + skin (1.50) and below standing capsule + skin (1.85), so it holds across the §3a range |
| S-A5 | One PlayMode case per cell of the §5.2 matrix and per row of §5.3 |
| S-A6 | Look-only input for 10 s while asleep does not wake |
| S-A7 | Foot slide per §4: graded forward only at 5 and 8 m/s (narrowed by Noah); strafe/back reported as information (#35) |
| S-A8 | Sleep dim is a Volume exposure change; no new Canvas or UI element exists in any stance |
| S-A9 | §3a range ends, per its proof column: crouch eye 1.10/1.30, squat 0.90/1.10, sit 0.75/0.90 and sleep eye 0.25 pass the H5 sweep, the hidden-head 3 cm match (SHADOW body, §3 eye point, drop h) and S-A4. Sleep pitch −45°/−85° has one still each with the sleep sweep. Crouch speed 1.25/2.50 passes S-A7 with CrouchWalk. Printed per end, pass or fail; a failed end shrinks the range (§3a) |
| S-A10 | World-space eye match (director check; zero confirmed by td FINAL 2026-10-18): camera world y − SHADOW-body head eye point world y (manifest eye point) = 0 ±0.03 m (head eye on the camera; both are stance eye + 0.05 above the ground after the scale and re-author), read in one frame with feet planted, in all five stances. Feet within ±0.01 m of the floor in Stand idle. s > 1.06 or floating feet → director |
| S-A11 | Moving clipping sweep [td ruling 2026-10-18]: the look-down bars "still and walking" and the −85..+85 clipping sweep while moving use the gaits actually played. Those are Jog at 5 m/s, Sprint at 8, Walk at 1.15 and 1.5 (tower speeds) and CrouchWalk at 1.75, each at 4 cycle points. No camera inside the body or showing the neck/shoulder opening. S-A10 stays feet-planted. Why: in-game walking at 5 m/s plays Jog, whose head eye sits 6-15 cm under the camera while moving (manifest) |

## 8. My-choice values (designer-2)

All §3a ranges; crouch eye 1.20; squat eye 1.00; sit eye 0.80; lying eye 0.20; crouch speed 1.75 (range 1.25-2.5); squat stationary; all enter/exit times in §3; smoothstep eases; sleep pitch −60°; dim to 15% over 2.0 s; wake fade 0.5 s; the JumpStart / JumpAir / JumpLand timings; no land penalty or dip; sprint-clip threshold 1.4; foot-plant convention; "still" ≤ 0.10 m/s; sit slope 25°; sleep slope 15° / 0.5 m; sit and lie box sizes; exit fallback order; toggles; prompt hidden while sitting; lowered stances blocked in all water and on the whole tower.
