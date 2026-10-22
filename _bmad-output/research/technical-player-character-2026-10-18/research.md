---
title: 'Technical research: placeholder player character (first-person body, stances, animation) + customization and third-person research'
type: 'technical'
topic: 'player character'
decision: 'How the first-person body, stances and clips are built now (A); designs for later customization (B) and an optional over-the-shoulder view (C)'
source: 'native run (bmad-deep-recon does not exist; WebSearch/WebFetch read-only for B and C)'
status: complete
created: '2026-10-18'
updated: '2026-10-18'
sections: 'a1-a4-artist.md (artist), a3-a4-designer.md (designer-2), a1-a4-engineer.md (engineer-2; rewrite of an earlier no-body draft)'
---

# Technical research: placeholder player character

**Decisions this research serves:** what the first-person player sees, where the missing clips come from, the stance numbers and rules (A.1-A.4, built now); and the designs behind the future issues for customization (B) and an optional over-the-shoulder view (C), not built in this pod.

Tags used below: **[code]/[scene]** read in the repo, **[inspected]** read from the vendor zips, **[derived]** arithmetic on tagged values, **[mine]** an author's choice (named), **[n]** a cited source in the appendix. The section files hold the full working; this file is the merged record and td's rulings.

## Executive summary

- **First person (A.1): full-body true first person.** One camera on the existing `CameraPivot`, never on a bone; the Head slot draws shadows only; the body is pushed back per stance until a pitch sweep shows no clipping. No arms-only rig. **td: approved.**
- **Animation (A.2): Generic rig, pack clips plus clips authored by Blender script.** Pack gives Idle, Walk, Run and the Run strafes; Jump, Crouch, Squat, Sit and Sleep are authored in `tools/blender/character/`. No downloads, no Mixamo, no retargeting. **td: approved.**
- **Stances (A.3): Stand 1.65 / Crouch 1.20 / Squat 1.00 / Sit 0.80 / Sleep 0.20 m eye**, capsule = eye + 0.15 (sleep 0.80, the controller's floor). Crouch speed 1.75 m/s; squat, sit and sleep are stationary. Walk 5, sprint 8, jump 1.2, tower 1.15 / 1.5 unchanged. **td: approved.**
- **Rules (A.4): every lowered stance is blocked on the whole tower and in any water,** so the tower eye minimums and "camera never underwater" hold with no new measurement. Sit and sleep need standing still on dry, flat ground. **td: approved, with two fixes named below** (capsule easing order; the tower capsule save).
- **Two items go to director now,** because the chosen view makes the body visible: feet float 0.24-0.29 m over the tower ramps (no foot IK), and the accepted <2.5 cm landing-6 shoulder overlap may now be seen.
- **Default look proposed: men's Worker** (alternates: men's Farmer, women's Worker). Pillar-risk outfits (Suit, Swat, Soldier, Punk, and also King, SpaceSuit, SciFi, Witch, Medieval) are out.
- **B and C** produce 12 + 2 future issues (list at the end). The core design calls: appearance is a small versioned **descriptor of IDs**, never meshes; one skeleton, slot meshes, body-region hide flags, tint masks; a third-person view, if ever added, is a **server rule first** (first-person-only worlds by default), with the camera ray as the aim ray and forced first person on the tower and in tight interiors.

## A.1 What the player sees in first person [td: APPROVED]

**Options weighed.**
| Approach | Who uses it | For | Against |
|---|---|---|---|
| Full-body true first person | Arma, DayZ, Escape from Tarkov, Mirror's Edge [C29][C33] | One rig, one animation set; real shadows; body seen looking down; same body other players see later | Head and neck clip the near plane; clip bob reaches the camera if the camera rides a bone |
| Arms-only view model + shadow-only body | most shooters [C30][C31] | Clean framing, no clipping | Two rigs and two clip sets; nothing useful in this game to hold yet; feet not visible (spec H5 wants the body) |
| Hybrid (full body, head hidden, camera on the root) | **chosen** | Keeps the tower's no-bob rule; one rig | Needs the pitch sweep to prove no clipping |

**Ruling.** Hybrid true first person, from `a1-a4-engineer.md` §2:
- The camera stays on `CameraPivot` on the CharacterController root [code `PlayerController.View.cs`]. A head-bone camera would carry clip bob and sway into the eye, which greenlight C3 forbids on the tower.
- A `FirstPersonBody` component sets the Head slot renderers to `ShadowCastingMode.ShadowsOnly`; all other slots draw and cast full shadows (H5).
- `bodyBackOffset` per stance, starting at 0.10 m [mine, engineer-2], set only by the pitch sweep: −85° to +85° in 5° steps, near-plane centre and 4 corners ray-cast against `Mesh.Bake` snapshots, at **all five stance eye heights, sleep included** even though sleep locks look (td: the spec's H5 says every stance and pitch). Output `TestResults/player-character/pitch-sweep-<stance>.csv`.
- The body does not pitch or lean with the camera in this pod (later, with foot IK).
- What other players will see (the head, aim lean) is logged to the future issues; nothing networked is built.

**Director items raised by this view (go with the gate):**
1. **Feet over the tower ramps** float 0.24-0.29 m above the ramp line [derived, designer-2 §4: capsule on a 52-56° ramp, r 0.25]. Looking down on the stairs shows it. Options: accept as placeholder and log it under foot IK (td's recommendation), or add a cheap ground-probe body drop now (an extra system on the tower, more risk to C3).
2. **Landing 6 body-in-steel overlap (<2.5 cm, shoulder height)** was accepted as unseen with no body. Whether it shows now is unmeasured; td asks qa-2 for a still from the landing-6 eye at pitch 0 / ±45° / ±85° in Phase 1. Visible = back to director (C3).

**Phase 1 look-down amendment (director amend2-amend4, td method, 2026-10-18).** The Stand and Crouch look-down showed the stylised torso cap and the cut neck, so the local player now has two bodies: a fixed SHADOW body (all 4 slots, shadows only, the one Animator) and a VIEW body (Body/Legs/Feet, shadows off, pose copied each LateUpdate) offset by a per-stance pitch table f(stance, pitch) (monotone cubic; the camera never moves). Measured windows (`TestResults/player-character/lookdown/lookdown-sweep.csv`): Stand +50..+85 and Crouch +75..+85 meet the full bar. Crouch +40..+70 cannot show the torso without the neck, so director accepted td's waiver (amend4): "Crouch +40..+70: thighs and knees visible from the bottom edge, no neck/cap/edge; bar (2) torso clause waived", on condition of no neck in any crouch-walk frame at +60, f continuous across the range edges, and bar 1 at every pitch in every stance. The men's skeleton is scaled 1.030 (controller hover 0.05 m; see A.3). **Final look-down tables** (`Assets/Scripts/Gameplay/Stance/LookDownOffset.cs`, pitch ° → back m, monotone cubic): Stand (20,0) (40,0) (45,0.02) (50,0.04) (55,0.06) (60,0.07) (65,0.10) (70,0.16) (75,0.21) (80,0.23) (85,0.30), max slope 0.0156 m/° at 83.5°; Crouch (20,0) (60,0) (65,0.05) (70,0.06) (75,0.11) (80,0.125) (85,0.175), max slope 0.0140 m/° at 63° (eng-ld15b; td cap 0.02 m/°). Bar 1 holds at every pitch for idle and every gait; director amend5 (`gate/player-character-phase0-amend5-waiver-extension`) ACCEPTED the Stand +50..+55 bar-2 waiver and HELD Crouch +75: the stills harness shows neck/shoulder rays (3-6) at every CrouchWalk phase at +75 (`TestResults/player-character/lookdown-stills/stills-summary.txt` lines 27-34) against neck 0 in the gait sweep; reconciled in eng-ld17: a real-game-path arbiter (crouch key, move input, one placement, settled) shows neck 0 in 8/8 phases, so the gait sweep stands; the stills harness placed the player twice mid-walk, and its 34 hits were Shoulder.L/R arm-root triangles at the bottom frame edge. Crouch +75 bar 2 goes back to director as a waiver after retaken stills. Watch item: at Sprint 8 a pumping hand enters the frame at pitch 0 and fills a third of it at +30 (ld17 watch stills); sent to director with a recommendation for a scripted arm-swing fix. **Arm swing (director amend6, amend7):** the Jog/Sprint arm swing is capped by re-authored clips; per amend7 (`gate/player-character-phase0-amend7-view-only-cap`) the cap applies to the first-person VIEW body only: the shadow body runs an AnimatorOverrideController mapping the 8 capped clips to held `_Full` clips (full swing for the shadow, the debug camera and later other players), and the view body runs its own Animator on the capped controller, mirroring the shadow's parameters and copying every bone except the arm chains (sync guard counted). Legs, speeds, stride and plant are identical in both clip sets. **H9 watch item:** the sprint legs run deeply bent with a strong forward lean (a low sneak-run look, from the legs, not the arm fix); if Noah rates the sprint "not right", it goes to director with his rating before any leg re-author. **Sprint re-author (director amend8-8e):** the authored Sprint legs were re-authored (mid-stance hip 0.812 m unscaled = the pack Run's, stance knee 48°, swing knee 113°, the hip highest in flight, rate 1.379 at 8 m/s), and the shadow's _Full arms were re-authored to td's hand-path bar (wrist 0.16 m behind to 0.27 m in front of the shoulder, elbow 82-99°, shoulder swing 125°); bar (b) was amended in amend8e to allow a chin-height ceiling of ~0.10 m above the shoulder on the forward swing after td disclosed that his two arm instructions contradicted each other. Relaxed hands and the mid-flight knee (~80°) are logged in #35.

## A.2 Animation set and sources [td: APPROVED]

From `a1-a4-artist.md`:
- **Packs:** both CC0 1.0 (Quaternius), `License.txt` at each pack root [inspected]. The *Separate Skeletal Meshes and Animations* folder has `<Outfit>_Head|Body|Legs|Feet.fbx` per outfit and a mesh-less `Animations.fbx`. Every part carries the full `CharacterArmature` with identical bone names per gender: men 79 bones, women 62, with different proportions, so **clips are per gender** [inspected].
- **Pack clips (24):** usable here are Idle, Idle_Neutral, Walk, Run, Run_Back, Run_Left, Run_Right, Interact. **Missing:** Jump (start/air/land), Crouch idle and walk, Squat, Sit, Sleep, and walk-speed strafes [inspected].
- **Sources weighed:** other Quaternius CC0 animation sets would mean a download, which goes to Noah via team-lead (not requested: not needed). **Mixamo is out** (Adobe login; the brief bans it). **Blender authoring by script is the default** and needs no approval: `tools/blender/character/`, headless, reading the pack's rest pose, writing FBX on the same armature and bone names.
- **Rig: Generic, not Humanoid.** No clip comes from another skeleton, so there's nothing to retarget. The assembler rebinds part meshes by bone name, which is simplest on Generic. And Generic stays inside `ModelFacingPostprocessor`, which skips Humanoid (repo `CLAUDE.md`). Cost accepted: no Humanoid IK goals (foot IK is later; Animation Rigging two-bone IK works on Generic). The vendor zips' Humanoid Rig folders stay, so the door stays open.

**td conditions:**
1. The facing report (`TestResults/model-facing-report.json`) shows the same decision on every part FBX, on `Animations.fbx` and on every authored FBX. `ambiguous` = bug, never a hand rotation. Plus the separate +Z EditMode test (H1).
2. The clip manifest records each clip's measured ground speed (stride × cycles/s); the locomotion blend thresholds use it (A.3).
3. If Run needs a playback rate over 1.4 to reach 8 m/s, a scripted longer-stride sprint variant is authored (designer-2 §3).
4. Licence per clip in `_bmad-output/poc/character-clip-manifest.md`: pack clips CC0 + zip name; authored clips "project-authored".

**Departure recorded (artist, 2026-10-18):** the pack locomotion clips are too slow for the game's speeds. Measured foot speed: Walk ≈ 1.34 m/s, Run ≈ 3.0 m/s (in place, both genders) [inspected, artist]. That gives rates of 2.67 for Run at 8 m/s and 1.67 for Run at 5 m/s [derived], over the 1.4 cap. **td ruling:** a longer-stride jog (~5 m/s) and sprint (~8 m/s) are authored by script per gender, each at a rate ≤ 1.4. The pack Walk and Run cover the slow range, tower speeds and crouch. Any strafe or back limit that remains is named in the clip manifest.

## A.3 Stance numbers [td: APPROVED]

From `a3-a4-designer.md` §1-§4; sheet values from `World.unity:11872-11878` (capsule 1.8 / r 0.4 / centre 0.9 / skin 0.05), CameraPivot y 1.65, near 0.10, FOV 70 (`World.unity:83664`).

**The eye-headroom rule:** eye = capsule top + skin − 0.20 (standing: 1.8 + 0.05 − 0.20 = 1.65) [derived], so a ceiling is never nearer than R8's 0.20 m to the eye. Kept for every stance: **capsule = eye + 0.15**, every frame.

| Stance | Eye (m) | Capsule (m) | Speed (m/s) | Transition |
|---|---|---|---|---|
| Stand | 1.65 [scene] | 1.80 [scene] | walk 5 / sprint 8 [code] | — |
| Crouch | 1.20 [mine, designer-2] | 1.35 [derived] | 1.75 [mine; 0.35 × walk; range 1.25-2.5] | 0.25 s each way [mine] |
| Squat | 1.00 [mine] | 1.15 [derived] | 0 [mine] | 0.4 / 0.4 s [mine] |
| Sit (ground) | 0.80 [mine; seated eye ≈ 0.79 m above seat, Pheasant *Bodyspace*] | 0.95 [derived] | 0 | 1.0 / 0.8 s [mine] |
| Sleep (on back) | 0.20 [mine] | 0.80 [derived: CharacterController floor 2 × r] | 0 | 1.5 / 1.5 s [mine] |

- Radius never changes with stance [engineer-2 §3].
- **Jump:** take-off 6.93 m/s, apex 0.346 s, 0.693 s airtime [derived from jump 1.2, gravity −20]. No wind-up delay; JumpStart ≤ 0.15 s laid over the rise; JumpAir after 0.20 s airborne; JumpLand only after ≥ 0.25 s airborne; no landing penalty, **no camera dip** (C3) [mine, designer-2].
- **Stride matching (H4 ≤ 10 cm):** one 2D blend on real horizontal speed, thresholds = the clips' measured speeds; above the fastest clip, playback rate = speed / clip speed. Walk 5 is a jog in human terms [derived], so **the authored jog carries 5 and the authored sprint 8 (see the A.2 departure); pack Walk and Run carry the tower speeds 1.15 / 1.5 and the ramp-up**; Crouch_Walk carries 1.75.
- **Foot IK: later** (future issue). The mesh sits 0.05 m (skin) below the capsule foot point so feet touch flat ground [derived]. Off-tower float on slopes: 0.057 m at 10°, 0.120 m at 30°, 0.236 m at the 45° limit [derived, designer-2 §4].

**td ruling on the transition order (designer-2 §9 Q1):** the every-frame rule wins. Going **down**, the capsule follows the eye every frame (h = eye + 0.15). Going **up**, the capsule grows to the target first, after the headroom check, then the eye eases. engineer-2's "capsule drops at once, eye eases" is superseded. Test: designer-2 S-A3 (crouch-walk under a 1.45 m bar 0.3 m ahead at the press).

**Measured hover and body scale (td ruling, 2026-10-18, Phase 1).** The controller hovers h = 0.0500 m (= skinWidth; `TestResults/player-character/sweep/sweep-summary.txt`, run 2026-10-18T12:23:22). The eye heights above are **pivot-local**; the eye above ground is pivot + 0.05 (1.70 standing). The body drop is h, so the feet touch the ground. To match the hidden head to the camera (H5 ±3 cm), the men's skeleton gets a uniform scale s = (1.65 + h) / stand clip eye at the eye point measured from the Men_Farmer_Head mesh (indicative 1.028; final value in `_bmad-output/poc/character-clip-manifest.md`). Lowered clips are re-authored to clip eye = stance eye + h. Capsule, radius, eye heights and speeds are unchanged; the women's rig is not scaled. Default look is now men's Farmer (Worker's hi-vis read as modern).

**Headroom:** `Physics.OverlapCapsuleNonAlloc` from the current top to the target height, everything except Water, no triggers, own collider ignored [mine, engineer-2]. A blocked stand stays requested until another stance key cancels it (designer-2's rule, over engineer-2's 0.25 s). Exits fall back Stand → Crouch → Squat → stay.

## A.4 Rules between stances [td: APPROVED with two fixes]

Full matrix: `a3-a4-designer.md` §5.2; blocked contexts §5.3; engineer mechanics `a1-a4-engineer.md` §4.

- **Enter:** Stand ↔ Crouch ↔ Squat freely (headroom to go up). Sit and Sleep only when still (≤ 0.10 m/s [mine]) and grounded. Any move or jump input stands you up or wakes you; **look and interact never do**. Jump from crouch or squat = stand only, no jump (jump stays 1.2 from standing). Sprint while lowered = stand (headroom), then sprint 8; no crouch-sprint.
- **Blocked (all four lowered stances):** airborne; any wading or swimming (lowered on water arrival = stand at once); **the whole tower** (footprint, ramps and decks with `WalkSpeedCap > 0`, climb areas); hatch duck, firefinder focus, suspended control, overridden view (and the frame control resumes, so East-to-leave the firefinder doesn't also crouch). Sit: slope > 25°, or the 0.9 × 0.6 m leg box hits something. Sleep: slope > 15°, height change > 0.5 m across the lie box, any water in the 1.9 × 0.7 m lie box, or anything within 0.20 m of the lying eye [mine, designer-2].
- **Why blocking on the tower is right (C3):** the eye minimums (0.15 / 0.20 m, near plane 0.10) were measured for the standing eye only. Blocking is the director's allowed alternative and needs no new measurement. It also gives H6's "no sit or sleep on the stairs".
- **Camera when sitting and sleeping:** the camera follows stance numbers only, vertically, eased (smoothstep); no bob, sway, roll or FOV change. Sit keeps free look. Sleep eases pitch to −60° (looking up), locks look, and **dims to 15% over 2.0 s** [mine]; wake fades up over 0.5 s. **td: dim with a URP Volume (post-exposure), not a Canvas**, so no UI element exists (C1). The interaction prompt is hidden while sitting.
- **Never underwater (C4):** sit and sleep need dry ground, the lie box rejects water, and every lowered stance stands up at the first wade. The lowest eye (0.20) is never over water.

**td fix 1, transition order:** see A.3.

**td fix 2, the tower capsule save.** `TowerWalkZones.cs:61` saves `body.height` on entering the footprint, and `:62` restores it on exit [code]. A player who walks in crouched has 1.35 saved, is stood up by the block, and leaves the tower standing on a 1.35 m capsule. Ruling: `SetWalkCapsule` called while lowered forces Stand (no headroom test; the tower capsule is already ruled). The preferred fix is a one-line change at `:61` to save a new `PlayerController.StandingHeight` (needs producer-2's OK: not engineer-2's file). TowerWalkTests stay unchanged. Plus a new PlayMode case: enter crouched, exit, capsule 1.80 ±0.01 and Stand.

**Camera seam:** `ClearViewOverride` restores the **current stance** eye height; `AuthoredEyeHeight` stays 1.65 for Firefinder and the tests. While the eye or view is overridden, stance code only updates its target [engineer-2 §3].

### Binding audit (spec H6, greenlight C5) [td: APPROVED]

Today [code, engineer-2 §5]: Move WASD / left stick; Sprint LShift / L3; **Jump Space / South**; **Interact E / South** (`Interactor.cs:25-26`); Firefinder focus Q, Esc / East leave. The inert template `Assets/InputSystem_Actions.inputactions` (read by no code) has Crouch C / East and Interact North.

1. **Gamepad South is shared by Jump and Interact and works:** the Interactor runs first (order −100); `JumpPressed` rejects a gamepad South press the Interactor took that frame (`InteractionRules.JumpBlocked`, `PlayerController.cs:234-235`). No stance takes South.
2. **C and LeftCtrl** are bound nowhere at runtime. **The duck-under reservation (C5) is honoured:** water-swim-spec:73-76 says duck "uses that crouch action in water", so duck-under will be LeftCtrl / East in water. **td confirms C is released** to squat, and squat is inert in water.
3. The template's Interact = North disagrees with the code (South). Align the template to the code once producer-2 approves.

| Stance | Keyboard | Gamepad | Mode |
|---|---|---|---|
| Crouch | Left Ctrl | East | toggle |
| Squat | C | D-pad Down | toggle |
| Sit | X | D-pad Left | toggle |
| Sleep | Z | D-pad Right | toggle |

An audit test enumerates every runtime binding path (PlayerController, Interactor, Firefinder, stance) and fails on any shared path outside `{South: Jump + Interact, East: Crouch + Firefinder leave}`.

## Default look (for director)

td proposes **men's Worker** as the default: work clothes read as a person living off the land in 1987-collapse Bells Bend (greenlight C6). **Alternates:** men's Farmer, then women's Worker. In first person only the torso, hands, legs and feet are seen, and the head only as a shadow. **Out for the default (pillar risk):** Suit, Swat, Soldier, Punk (C6), plus King, SpaceSuit, SciFi, Medieval and Witch [inspected outfit list, artist §1]. H2's 2 + 2 assembly test can use Worker, Farmer, women's Worker and women's Casual or Adventurer.

**Default look changed to men's Farmer (Phase 1).** Under director's gate ruling 2 (`gate/player-character-phase0` in `studio:decisions`: "if anything reads modern or out of place for 1987+25 (for example hi-vis), switch to Farmer without coming back to me"), artist switched the default from Worker to **men's Farmer** (`Assets/Art/Character/Appearances/Men_Farmer.asset`): the Worker's hi-vis vest and hard hat read modern for 1987+25. Director notes the Farmer (hat and overalls) fits well. H5 is graded on the Farmer.

## Stance list (for director)

Stand, Crouch, Squat, Sit (on the ground), Sleep (lying on the back). Sit on objects or beds, swim and wade poses, and sleep tied to needs are future issues.

## B. Customization: design for later (research only, nothing built)

Sources are read-only web research for td (B-numbers in the appendix). "Fetched" sources were read; others are search snippets and marked lower confidence.

### B.1 Modular slots on a shared skeleton
- **Unity:** each part is its own `SkinnedMeshRenderer`; equipping a part means pointing its `bones[]` at the shared armature's Transforms **by bone name**, setting `rootBone`, and keeping the part's own bind poses [B4][B3]. Clothing whose bone array differs from the body's breaks a naive bones-array copy, so remap per renderer [B43].
- **Unreal's three methods** [B1] map the trade-off: Leader Pose (cheap to set up, one draw per part, keeps morphs), Copy Pose (per-part graphs and physics, costliest), Skeletal Mesh Merge (one draw, costly one-time merge, **drops morph targets**, needs a planned atlas).
- **For us:** the Quaternius parts already share `CharacterArmature` names per gender [inspected, artist], so H2's `CharacterAssembler` is the Leader-Pose equivalent: one skeleton instance, parts as mesh sources. Merging is a later optimisation (B.6), not the base design.

### B.2 Body types and blend shapes
- Unity blend shapes run 0-100 through `SetBlendShapeWeight` and can be keyed [B5][B6]. **Every outfit part must carry the same body blend shapes as the body**, so one slider set drives both [B25].
- Blend shapes change shape, not proportions; height and limb length need **bone scaling** (UMA's "DNA") [B16][B25]. Rumbleverse fitted accessories across 6 very different body types (GDC 2023, overview only) [B22].
- **Our constraint:** men's (79 bones) and women's (62) rigs differ [inspected], so "body type" starts as a choice of rig, and sliders within a rig come later. The packs ship no blend shapes [unverified; artist to confirm on import].

### B.3 Face, hair and skin
- Survival games mostly use **preset heads**: Rust derives skin colour, head mesh and head material from the SteamID (2×2 face combinations at launch, 2016) [B38]. 7 Days to Die 1.0: 4 faces per race and sex, 20 hairstyles, 10 hair colours, beards, 30+ eye colours, with players criticising the lack of body types [B39].
- Real-time hair is alpha cards in layers (scalp, volume, flyaways) [B26]. Skin tone is a tint on a greyscale or flat base (B.4).
- **For us:** the pack's `*_Head` parts are the head presets; hair and beard as optional sub-slots of Head.

### B.4 Tint masks vs texture swaps
- RGB channel masks: each channel marks a region and the shader multiplies a chosen colour over a greyscale base, so 3-4 regions per mask [B40]. A palette or key-colour swap is cheaper and suits flat-colour art [B40]. Destiny 2's move to single-use shaders shows players read dye as an owned item [B45].
- **For us (td inference):** Quaternius parts use flat-colour materials (4 materials on Worker_Body [inspected]). The cheapest tint is per-material `_BaseColor` through a `MaterialPropertyBlock` (no material copies). An atlas plus a **palette texture** (indices into a shared palette) is the upgrade that also allows merging (B.6). Pick the palette route before merging is built.

### B.5 Layered clothing and hiding the body underneath
- **Bethesda:** armour declares biped slots (30 head, 32 body, 33 hands, 37 feet...), the body mesh is split into matching partitions, and covered partitions hide; a mismatched slot makes the item invisible [B18][B19].
- **Mutable:** *Remove Mesh Blocks* deletes geometry in chosen UV blocks "to avoid clipping problems with hidden geometry"; clip-with-mesh and UV-mask clipping are the alternatives [B11][B14]. Cyberpunk 2077's garment support shrinks inner layers under outer ones with painted weights (modding wiki) [B20].
- **For us:** start with a `hidesSlots` flag set per item (a coat hides Body; boots hide Feet) on the existing four slots. Add body **regions** (submeshes or UV blocks: forearms, shins, neck) only when partial cover arrives. One under-layer rule per item, checked by an EditMode test.

### B.6 Merging skinned meshes and atlasing
- Unity's `CombineMeshes` does not handle skinning; community combiners concatenate geometry, offset bone indices and rebuild bones and bind poses, and mistakes duplicate bones or break weights. There is no official guide [B7][B8].
- UMA atlases one texture per avatar (one material, one draw; up to 4096², 2048² suggested for few avatars) [B16]. Mutable repacks textures at runtime and Epic calls it costly, worth it only to cut draws or memory [B12]. Merging drops morph targets in Unreal [B1], which conflicts with body sliders.
- **For us:** 4 parts × ~4 materials ≈ 16 draws per character [derived from artist's Worker_Body count]. Fine for one local player; it matters with many remote players. Merge plus atlas becomes a future issue gated on a measured draw-call budget in a multiplayer scene.

### B.7 LOD
- Unity 6.2+ **Mesh LOD** on skinned meshes: the generator ignores skin weights and blend shapes, skinning always deforms LOD0 (no skinning saving), and pairing it with LOD Group is "not recommended" [B28].
- The working route is a **LOD Group with one SkinnedMeshRenderer per level on the shared bones** [B27][B41]; Simplygon remaps generated LODs to the original skeleton by bone name [B30]. Fewer bones per vertex (1/2/4) cuts cost at distance [B4]. URP cross-fade needs the URP Asset setting plus the group's fade mode [B27].
- **For us:** the first-person player never needs LOD. It is needed for remote players (#11 LOD and #13 multiplayer).

### B.8 Appearance as data: save and network sync
- Mutable: "the simplest way to replicate a customization is to send the set of parameter values" (`SaveDescriptor` / `LoadDescriptor`) [B10]. UMA ships packing code and warns it may need shrinking; users report long JSON recipes [B16][B17]. Rust stores nothing and derives the look from the SteamID [B38].
- **td design:** `CharacterAppearance` (H2) is already the data. Its saved and networked form is a **versioned descriptor of IDs**: schema version, rig (body type), head/hair preset IDs, quantised slider bytes, palette indices. That's tens of bytes [inference]. **Equipped clothing is not in the descriptor:** it comes from inventory item IDs, mapped to meshes by content tables. Meshes, materials and paths never travel. Unknown IDs fall back to the default look, and a version bump has a migration test.

### B.9 How a character-creator screen usually works
- Camera framing presets: MetaHuman Creator has Face, Body, Far and Auto (follows the active tool) [B31]; Hogwarts Legacy cycles Face, Full Body, Upper, Lower, Shoe on one button [B32].
- The common order is base and sex → body → face → hair and colour → name. SCUM's body-type triangle changes both looks and stats [B37][B44]. The Sims 4 replaced sliders with click-and-drag on the body [B44]. No authoritative source was found on randomise buttons or preset galleries.
- **For us:** a creator is a **future issue only** (automatic FAIL here). It needs a director ruling, because it is the first UI beyond the single prompt line (tower N11, greenlight C1). It also needs a permadeath rule: a new character each life, or the same look.

### B.10 Equipment linked to inventory: damage, wetness, dirt
- **DayZ** (fan wiki) [B34]: 11 slots (Vest, Top, Belt, Legs, Back, Head, Face, Eyes, Gloves, Feet, Armband). Condition steps Pristine 100% insulation → Worn 90 → Damaged 80 → Badly Damaged 50 → Ruined 0, visual damage from Damaged on, Ruined not wearable. Wetness Damp → Drenched raises weight ×1.0-2.0 and drops insulation 90% → −15%.
- **The Long Dark** (official, 2016) [B33]: several torso layers, condition % scales warmth and waterproofing, only the outer layer gets wet, and a fully wet item freezes. Items carry windproof and waterproof stats.
- **Project Zomboid:** holes, dirt and patching; Build 41 added visibly ripped clothing [B35][B36]. DayZ shows damage by swapping materials (`hiddenSelections`, modding forums, unverified) [B46].
- **td design:** an item instance holds condition, wetness and dirt as 0-1 values. These drive a shader (darken, roughness, dirt mask) continuously, with mesh or material swaps only at thresholds. The look follows inventory state and is never stored separately. Wetness ties to the water pod (wading and swimming already exist).

### B.11 Survival games compared
| Game | Creator | Clothing slots | Condition / wetness | Source |
|---|---|---|---|---|
| DayZ | preset survivors (creator depth unverified) | 11 | condition + wetness steps, insulation per slot (Top and Legs 23% each, Feet 12%) | [B34] low |
| Rust | none: look from SteamID | wearables | not covered | [B38] medium |
| SCUM | stats + body-type triangle (fat/muscle affect metabolism, noise) | layered | soaked clothes weigh more | [B37] medium |
| The Long Dark | fixed protagonists | multi-layer torso, accessories | condition scales warmth; outer layer wets and freezes | [B33] high |
| Project Zomboid | full 2D creator | many | holes, dirt, patching | [B35][B36] low-medium |
| 7 Days to Die 1.0 | presets: faces, hair, beards | outfit sets with set bonuses | not covered | [B39] medium |

### B.12 Off-the-shelf options
- **UMA:** MIT licence (repo checked), free, community-kept, bone-scale DNA, per-avatar atlas, recipe serialisation [B15][B16]. It would mean converting the Quaternius parts into UMA's pipeline, and its wardrobe and mesh-hide APIs are unconfirmed. **td: a candidate for the merge/atlas issue only.** It would be a package install, so Noah decides. Not for this pod.
- **Unreal Mutable:** Unreal-only; a design reference [B9][B13].

## C. Optional over-the-shoulder third person: design for later (research only)

Not built in this pod; the H7 debug camera is the only third-person camera, dev builds only (greenlight C2). Three Bohemia wiki pages returned 403, so the Arma/DayZ settings rest on snippets and hosting guides.

### C.1 How games offer the switch, and how players took it
| Game | Model | Notes | Source |
|---|---|---|---|
| DayZ | server rule `disable3rdPerson` (0 default = 3PP allowed); browser shows and filters it | official servers keep a separate character per view; players split: 1PP praised for less corner-peeking, some 1PP servers near empty | [C1][C2][C3] medium/low |
| Arma 3 | difficulty profile `thirdPersonView` 0 / 1 / 2 = off / on / vehicles only | presets below Veteran allow 3PP; key spelling varies by source | [C4][C5][C6] medium |
| SCUM | `scum.AllowFirstPerson`, `scum.AllowThirdPerson` in ServerSettings.ini | keys change often | [C7] medium |
| Rust | **first person only**; 3PP and debug camera admin-only | a 2015 reply: 3PP withheld until the camera "can't see through walls"; plugins could force 3PP but not cleanly back | [C8][C9][C10] low/medium |
| PUBG | launched 3PP, **1PP queues added by player demand** (Jul 2017 → all regions Jan 2018) | a queue opened only where population filled it (≥ 500 per mode); 1PP seen as more competitive; early 1PP issues: windows too high, sound harder to place | [C11]-[C15] medium |
| 7 Days to Die | 3PP only as the `switchview` debug toggle | unfinished, disables achievements | [C16] low/medium |
| RDR2 | 3PP default, full 1PP toggle on one button | praised at launch | [C17][C18] medium |
| The Division | 3PP cover shooter with shoulder swap | preferred over automatic side switching (forum) | [C19] low |

**Reading:** in multiplayer survival the view is a **server rule**, not just a player setting. Players who want fair fights move to first-person servers, and the community splits along that line.

### C.2 The camera
- **Cinemachine 3 Third Person Follow:** shoulder offset, vertical arm, camera side 0-1 (so a shoulder swap can blend), distance. Example rig: shoulder (0.7, 0.3, −0.5), arm 0.5, distance 2. Built-in collision has a layer filter, an ignore tag (the player) and a camera radius, with separate damping into and out of collision [C20].
- **Deoccluder:** *Transparent Layers* never block (foliage goes there), and *Minimum Occlusion Time* ignores brief blockers such as a branch or a rail post. Raycast-based, so blockers need colliders [C21]. Unreal's spring arm sweeps a sphere and retracts on a hit, as a cross-check [C22].
- **Practice:** foliage on its own transparent layer; sphere-cast at about the near-plane radius so thin walls don't slip through; snap in at once and ease out [C23][C24]. **For us:** tower rails and stringers block; leaves don't.
- **Aiming:** Cinemachine Third Person Aim casts from the camera and keeps that point at screen centre, and exposes the real hit point when something blocks the shot but not the view [C25][C26]. Monday Night Combat used three traces (hand → muzzle, camera forward, muzzle → camera hit, capped near 7 m) because a camera-only trace let players **shoot through cover**, "clearly unacceptable" [C27]. Fire from the muzzle toward the camera hit, and ignore the player's own head [C28].

### C.3 One rig or two
- Two common approaches [C29]: separate arms on the camera plus a world body (two clip sets that must stay in sync), or **true first person** with one body (camera on the head, aim-offset poses, and watch tick order, double head rotation and bob-induced sickness).
- The common shooter pattern is first-person arms plus a third-person body for shadows and for other players [C30]. Unreal's first-person rendering draws arms in their own FOV and depth range, while the owner-hidden world body casts the shadow [C31]. Unity's equivalent is `ShadowCastingMode.ShadowsOnly` [C32]. Arma and DayZ are widely cited as true first person, but no Bohemia technical source was found [C33].
- **For us:** A.1's one-body choice makes a later third-person view **close to free in animation**, because the body is already animated full-body. An arms-only rig would have needed a whole third-person set (stairs, sash, hatch, wade, swim) [inference from C29][C30]. Third-person does expose the placeholder gaps: no foot IK, the swim pose, interaction reach. Those issues gate it.

### C.4 Fairness in a multiplayer survival game
- **The problem:** a third-person camera sees over and around cover that hides the body. In first person, "neither sees a thing until someone physically leans their head past the wall" [C15][C34]. Forcing first person "changes combat balance significantly" [C1].
- **Counter 1, separate pools:** first-person-only servers or queues (PUBG, DayZ, SCUM), or Arma's vehicles-only value [C1][C4][C7][C12]. The cost is a split population: PUBG wouldn't open a queue it couldn't fill [C13]. In a persistent permadeath world a split means separate worlds or characters, as on DayZ official [C2].
- **Counter 2, the server sends only what each player may see:** Valorant's Fog of War runs line-of-sight per client and per entity, and sends nothing for hidden actors. It grows bounds by velocity × a look-ahead longer than ping to avoid pop-in, and precomputed cell visibility cut its cost from ~50% to < 2% of server frame time [C35]. Netcode for GameObjects has per-client `CheckObjectVisibility`: hidden objects despawn on that client and get no traffic, but you re-check yourself, and the host sees everything [C36]. **Caveat [inference]:** the check must start from the view each player is allowed. With third person on, that is the camera, which is exactly the peek, so server culling limits wallhacks but **does not remove third-person peeking**.
- **Counter 3, first person for precise acts:** aiming and shooting in first person, with the muzzle-trace rule from C.2 [C27]. DayZ and PUBG aim-down-sights in first person [unverified].

### C.5 How the choice is offered, and the tower, interactions, water
- **Models:** server rule (DayZ, Arma, SCUM) [C1][C4][C7]; separate queues (PUBG) [C12]; a player toggle in single player (RDR2) [C17]; debug or admin only (Rust, 7DtD) [C8][C16]. **Whichever model is used, the server must be able to force the view both ways** [C10].
- **Per-situation forcing:** Arma's vehicles-only mode [C4]; a Rust plugin that switches on vehicle mount and back [C10]; Skyrim and Starfield mods that force first person indoors and reset at doors [C37]; camera tools that blend to first person when the boom collapses [C38]. A Valheim developer notes that supporting both views affects architecture, sound, AI and combat [C37].
- **The lookout tower:** 0.62-0.72 m flights and a small cab leave no room for a shoulder camera [C38, inference]. **td design: force first person inside the tower footprint** (the same volume that already blocks lowered stances) and collapse to first person whenever collision shortens the boom below a threshold. Tower feel rules (C3) then hold unchanged.
- **Interactions** (hatch, sash, firefinder): the ray starts at the **eye, never the third-person camera**, or players reach through walls. This is the aim-parallax problem again [C25][C27, inference]. The firefinder already overrides the view, so it stays first person.
- **Swimming and wading:** no source found. **td design:** water is out of the camera collision filter, and a waterline rule keeps the third-person camera above the surface. The camera never goes underwater (greenlight C4, fish N1), in any view.
- **td recommendation for the future issue:** worlds default to **first person only**. Third person is a per-world server rule (off by default), forced off in the tower, cab and interiors, with first-person aiming and eye-origin interaction rays. Server-side visibility culling is built with multiplayer (#13) either way, as anti-wallhack, not as the third-person fix.

## Future issue list (B and C) for Noah

Spec R1's twelve, plus two td adds. Drafts are written after the gate as `_bmad-output/planning-artifacts/issues/future-character-*.md`.

1. **Character creator UI:** a creator screen design. It needs a director ruling on the first UI beyond the prompt line, and a permadeath rule (new look each life?).
2. **Body types and blend shapes:** men's/women's rig choice first, then sliders, with every outfit part carrying matching blend shapes and bone-scale proportions.
3. **Face, hair and skin:** head presets from the packs, hair and beard sub-slots, a skin tint palette.
4. **Outfit and equipment system linked to inventory:** worn items are inventory items, 0-1 condition/wetness/dirt drive the look, slots modelled on DayZ / The Long Dark.
5. **Clothing layering and body masking:** `hidesSlots` per item now; body regions (UV blocks or submeshes) when partial cover arrives.
6. **Appearance save and network sync:** a versioned ID descriptor (tens of bytes), clothing from inventory IDs, migration test.
7. **Mesh merging and atlas performance:** merge plus palette atlas, gated on a measured draw-call budget with N remote players; UMA (MIT) as a candidate install.
8. **Swim and wade animations:** replace the placeholder pose; the camera stays above water (C4).
9. **Sitting on objects and beds:** authored sit points found by the Interactor ray, eye = seat + 0.79; the cab stool and **sitting on the tower top deck to take in the view** (director gate item 4) each need an R8 / 0.15 / 0.08 sweep at the seated eye.
10. **Sleep tied to survival needs:** fatigue, time skip (a multiplayer rule for Noah), threat wake, no HUD.
11. **Foot IK:** terrain and tower treads, pelvis drop; includes the 0.24-0.29 m ramp float, accepted as placeholder; the ground-probe body drop was not approved (director gate item 5, C3 risk).
12. **Optional over-the-shoulder third-person view:** first-person-only worlds by default, a per-world server rule, forced first person on the tower and in interiors, eye-origin interaction and aim rays, waterline rule.
13. **(td add) Remote-player body and look-aim:** what other players see: head visible, spine pitch and aim lean, LOD for remote characters (#11, #13).
14. **(td add) Locomotion clip quality:** walk-speed strafes and back, the authored jog/sprint polish, replacing script-authored placeholder clips.

## Source appendix

All accessed 2026-10-18. Confidence: **high** = official or developer source read directly; **medium** = secondary or official snippet; **low** = fan wiki or forum. A-section facts come from the repo and the vendor zips (tags in the text), not the web.

### B sources
| # | Supports | Source | Date | Conf. |
|---|---|---|---|---|
| B1 | Leader Pose / Copy Pose / Merge | [Epic, Working with modular characters](https://dev.epicgames.com/documentation/en-us/unreal-engine/working-with-modular-characters-in-unreal-engine) | current | high |
| B3 | bone remap by name | [Unity Discussions 797976](https://discussions.unity.com/t/how-to-move-a-skinned-mesh-renderer-over-to-another-model/797976) | ~2020 | low |
| B4 | SkinnedMeshRenderer bones, quality | [Unity Manual](https://docs.unity3d.com/Manual/class-SkinnedMeshRenderer.html) | current | high |
| B5 | blend shapes 0-100 | [Unity Manual 6000.6](https://docs.unity3d.com/Manual/BlendShapes.html) | current | high |
| B6 | SetBlendShapeWeight | [Unity API](https://docs.unity3d.com/ScriptReference/SkinnedMeshRenderer.SetBlendShapeWeight.html) | current | high |
| B7 | bindposes | [Unity API](https://docs.unity3d.com/kr/6000.0/ScriptReference/Mesh-bindposes.html) | 6000.0 | medium |
| B8 | skinned combine pitfalls | [Unity Discussions 244559](https://discussions.unity.com/t/combine-skinned-meshes/244559) | — | low |
| B9 | Mutable overview | [Mutable wiki](https://github.com/anticto/Mutable-Documentation/wiki/) | current | high |
| B10 | replicate parameters | [Mutable wiki, Replication](https://github.com/anticto/Mutable-Documentation/wiki/Replication) | current | high |
| B11 | Remove Mesh Blocks | [Mutable wiki](https://github.com/anticto/Mutable-Documentation/wiki/Remove-Mesh-Blocks) | current | high |
| B12 | texture packing cost | [Mutable wiki](https://github.com/anticto/Mutable-Documentation/wiki/Texture-Packing) | current | high |
| B13 | Mutable status | [Epic, What is Mutable](https://dev.epicgames.com/documentation/unreal-engine/mutable-overview-in-unreal-engine?lang=en-US) | current | medium |
| B14 | clip with mesh | [UE Python API](https://dev.epicgames.com/documentation/en-us/unreal-engine/python-api/class/CustomizableObjectNodeModifierClipWithMesh) | current | medium |
| B15 | UMA MIT licence | [UMA GitHub](https://github.com/umasteeringgroup/UMA) | current | high |
| B16 | UMA DNA, atlas, packing | [UMA wiki](https://github.com/umasteeringgroup/UMA/wiki) | current | high |
| B17 | UMA recipe size | [Photon forum 21631](https://forum.photonengine.com/discussion/21631/uma-2-photon-fusion-avatar-sync) | — | low |
| B18-19 | Bethesda biped slots | [UESP Biped Object](https://ck.uesp.net/wiki/Biped_Object); [Slot Masks](https://skyrimck.uesp.net/wiki/Slot_Masks_-_Armor) | — | low-medium |
| B20 | garment support | [RED Modding wiki](https://wiki.redmodding.org/cyberpunk-2077-modding/modding-know-how/3d-modelling/garment-support-how-does-it-work) | — | low |
| B22 | Rumbleverse 6 body types | [GDC Vault 1028733](https://gdcvault.com/play/1028733/Building-Champions-Character-Art-Pipeline) | 2023 | high (overview) |
| B25 | outfit blend shapes match body | [Blender Artists](https://blenderartists.org/t/modular-characters-for-game-project/1651881) | — | low |
| B26 | hair cards | [80.lv](https://80.lv/articles/creating-hair-for-real-time-game-characters/) | — | medium |
| B27 | LOD Group cross-fade | [Unity Manual](https://docs.unity3d.com/Manual/lod/lod-transitions-lod-group.html) | 6.4 | medium |
| B28 | Mesh LOD skinned caveats | [Unity Manual](https://docs.unity3d.com/Manual/lod/mesh-lod-introduction.html) | current | high |
| B30 | LOD skeleton reuse | [Simplygon](https://simplygon.com/posts/45ff2ece-1d74-484c-840c-47b12fea76fa) | — | medium |
| B31 | creator camera presets | [Epic, MetaHuman Creator](https://dev.epicgames.com/documentation/metahuman/navigating-metahuman-creator-in-unreal-engine) | current | medium |
| B32 | Hogwarts Legacy focus | [CurseForge support](https://support.curseforge.com/support/solutions/articles/9000263556-hogwarts-legacy-creators-basic-instructions) | — | low |
| B33 | Long Dark layers, wet, condition | [Hinterland, Resolute Outfitter](https://www.thelongdark.com/?p=1191) | 2016-12-19 | high |
| B34 | DayZ slots, condition, wetness | [DayZ wiki.gg, Clothing](https://dayz.wiki.gg/wiki/Clothing) | — | low (fan wiki) |
| B35-36 | Zomboid holes, Build 41 | [Steam comment](https://steamcommunity.com/sharedfiles/filedetails/comments/3156717285); [GamingOnLinux](https://gamingonlinux.com/2021/12/well-thats-a-surprise-project-zomboids-massive-overhaul-is-out-now) | 2024 / 2021-12 | low / medium |
| B37 | SCUM body and stats | [GamesBeat](https://gamesbeat.com/scum-the-survival-sandbox-that-simulates-the-whole-human-body/); [Game Informer](https://www.gameinformer.com/games/scum/b/pc/archive/2018/04/18/your-body-is-a-temple-in-this-openworld-survival-title.aspx) | 2018-04-18 | medium |
| B38 | Rust look from SteamID | [PC Gamer](https://www.pcgamer.com/uk/rust-update-assigns-skin-color-and-face-to-players) | 2016-03 | medium |
| B39 | 7DtD 1.0 presets | [HostHavoc](https://hosthavoc.com/blog/7-days-to-die-version-1.0-update-overview) | 2024-07-26 | medium |
| B40 | RGB masks, palettes | [REDengine shader docs](https://wiki.redmodding.org/redengine3-research/assets/shaders/shader-docs); [Godot Forums](https://godotforums.org/d/19848-rgb-tint-texture-mask) | — | low |
| B41 | mesh/bone LOD swap | [Unity Discussions 492688](https://discussions.unity.com/t/mecanim-mesh-and-bone-lod/492688) | — | low |
| B43 | clothes bone arrays differ | [Reallusion forum](https://forum.reallusion.com/429523/Exporting-bodyparts-and-clothes-to-Unity-and-issue-using-same-Animation-Controller) | — | low |
| B44 | Sims 4 creator | [SimsCommunity (EA blog repost)](https://simscommunity.info/2014/05/21/up-close-and-personal-with-the-new-create-a-sim-in-the-sims-4/) | 2014-05-21 | medium |
| B45 | Destiny 2 shaders | [BGR](https://www.bgr.com/entertainment/destiny-2-shaders-single-use/) | 2017 | medium |
| B46 | DayZ hiddenSelections | [Bohemia forums](https://forums.bohemia.net/forums/topic/221642-damaged-car-rendering/) | — | low |

### C sources
| # | Supports | Source | Date | Conf. |
|---|---|---|---|---|
| C1 | DayZ `disable3rdPerson` | [Host Havoc](https://hosthavoc.com/wiki/game-servers/dayz/perspective-mode) | — | medium |
| C2 | DayZ servers, separate characters | [DayZ wiki.gg, Servers](https://dayz.wiki.gg/wiki/Servers) | — | low |
| C3 | DayZ player reception | [Steam 3040481812840105547](https://steamcommunity.com/app/221100/discussions/0/3040481812840105547); [Steam 591762230004947902](https://steamcommunity.com/app/221100/discussions/0/591762230004947902) | 2021 | low |
| C4 | Arma `thirdPersonView` | [Bohemia server.armaprofile](https://community.bistudio.com/wiki/server.armaprofile); [Difficulty Menu](https://community.bistudio.com/wiki/Arma_3_Difficulty_Menu) | — | medium (403, snippet) |
| C5-6 | Arma presets, custom difficulty | [Prima Games](https://primagames.com/tips/how-to-go-third-person-in-arma-3); [Freakhosting](https://help.freakhosting.com/games/arma-3/configuring-custom-difficulty-and-ai-skill) | — | medium |
| C7 | SCUM perspective keys | [Kinetic Hosting](https://www.kinetichosting.com/articles/scum/basic-s/how-to-enabledisable-third-and-first-person-for-your-scum-server); [GameServerKings](https://www.gameserverkings.com/knowledge-base/scum/scum-server-settings-ini/) | 2025-08 | medium |
| C8 | Rust 3PP withheld | [Steam 610573009256321864](https://steamcommunity.com/app/252490/discussions/0/610573009256321864) | 2015-02 | low |
| C9 | Rust debug camera | [Facepunch wiki](https://wiki.facepunch.com/rust/Debug_Camera) | — | medium |
| C10 | force view both ways | [uMod 12624](https://umod.org/community/rust/12624-player-flag-not-working-to-go-back-to-normal-first-person-view); [uMod 29678](https://umod.org/community/plugin-requests/29678-third-person-toggle-for-players-rust) | — | low |
| C11 | PUBG FPP servers announced | [Shacknews](https://www.shacknews.com/article/100592/pubg-adding-first-person-servers-in-its-next-update) | 2017-07-13 | medium |
| C12-14 | PUBG FPP rollout, population | [COGconnected](https://cogconnected.com/2017/08/pubg-first-person-servers-game-modes-regions/); [TweakTown](https://www.tweaktown.com/news/59042/pubg-react-backlash-releasing-oceania-fpp-servers/index.html); [PC Gamer](https://www.pcgamer.com/au/pubg-will-open-squad-fpp-oce-servers-after-mid-october); [MP1st](https://mp1st.com/news/pubg-hotfix-xbox-one-now-live-adds-first-person-perspective) | 2017-08 to 2018-01 | medium |
| C15 | FPP more competitive | [Red Bull](https://redbull.com/se-en/pubg-whats-more-competitive-fpp-or-tpp) | — | medium |
| C16 | 7DtD switchview | [GameServerKings](https://www.gameserverkings.com/knowledge-base/7-days-to-die/7-days-to-die-console-commands/); [Sportskeeda](https://www.sportskeeda.com/esports/how-enable-third-person-view-7-days-die) | 2026-07 | medium/low |
| C17-18 | RDR2 first-person mode | [GameRevolution](https://www.gamerevolution.com/news/434623-red-dead-redemption-2-has-a-full-first-person-mode); [GameSpot](https://www.gamespot.com/articles/red-dead-redemption-2-first-person-mode-revealed/1100-6461906/) | 2018 | medium |
| C19 | shoulder swap reception | [Giant Bomb wiki](https://giantbomb.com/wiki/Concepts/Shoulder_Swapping_Aim) | — | low |
| C20 | Third Person Follow | [Cinemachine 3.1](https://docs.unity3d.com/Packages/com.unity.cinemachine@3.1/manual/CinemachineThirdPersonFollow.html) | 3.1 | high |
| C21 | Deoccluder | [Cinemachine 3.0](https://docs.unity3d.com/Packages/com.unity.cinemachine@3.0/manual/CinemachineDeoccluder.html) | 3.0 | high |
| C22 | spring arm | [Epic USpringArmComponent](https://dev.epicgames.com/documentation/en-us/unreal-engine/API/Runtime/Engine/USpringArmComponent) | current | high (snippet) |
| C23-24 | thin walls, leaves | [Bugnet](https://bugnet.io/blog/how-to-fix-unity-third-person-camera-tunneling-through-thin-walls); [GameDev.tv](https://community.gamedev.tv/t/cinemachine-camera-blocked-by-leaves/219632) | — | low |
| C25-26 | Third Person Aim | [Cinemachine 3.1 Aim](https://docs.unity3d.com/Packages/com.unity.cinemachine@3.1/manual/CinemachineThirdPersonAim.html); [Third Person Cameras](https://docs.unity3d.com/Packages/com.unity.cinemachine@3.1/manual/ThirdPersonCameras.html) | 3.1 | high |
| C27 | three-trace aiming | [Forrest Smith, Game Developer](https://gamedeveloper.com/design/opinion-adventures-with-guns-in-a-third-person-shooter) | 2011-05-17 | high |
| C28 | muzzle toward camera hit | [Plague Wardens devlog](https://boardgamers2409.itch.io/plague-wardens/devlog/802384/accurate-shooting-keith-babcock) | — | medium |
| C29 | true first person vs arms | [Fabrice Piquet, Game Developer](https://gamedeveloper.com/programming/true-first-person-camera-in-unreal-engine-4) | 2018-06-21 | high |
| C30 | arms + body common | [NaturalPoint forum](https://forums.naturalpoint.com/viewtopic.php?p=29492); [Godot forum](https://forum.godotengine.org/t/character-mesh-structure-in-fps/129048) | — | low |
| C31 | UE first-person rendering | [Epic, First Person Rendering](https://dev.epicgames.com/documentation/en-us/unreal-engine/first-person-rendering) | 5.6+ | high |
| C32 | ShadowsOnly | [Unity API](https://docs.unity.cn/ScriptReference/Rendering.ShadowCastingMode.ShadowsOnly.html) | 2022.3 | high (snippet) |
| C33 | Arma/DayZ true FP cited | [OpenMW #2666](https://gitlab.com/OpenMW/openmw/-/issues/2666) | — | low |
| C34 | TPP vs FPP peeking | [Inven Global](https://invenglobal.com/articles/19247/tpp-vs-fpp-in-pubg-which-is-better) | — | low-medium |
| C35 | Valorant Fog of War | [Riot, Demolishing Wallhacks](https://www.riotgames.com/en/news/demolishing-wallhacks-valorants-fog-war) | 2020-04-14 | high |
| C36 | NGO object visibility | [Unity NGO docs](https://mp-docs.dl.it.unity3d.com/netcode/2.3.2/basics/object-visibility/) | 2.3.2 | high (snippet) |
| C37 | forced FP indoors; Valheim note | [Nexus Starfield 16043](https://www.nexusmods.com/starfield/mods/16043); [Nexus Skyrim 69500](https://www.nexusmods.com/skyrimspecialedition/mods/69500); [Steam Valheim](https://steamcommunity.com/app/892970/discussions/0/4521136683852786643/?ctp=3) | — | low |
| C38 | tight interiors, boom collapse | [Kingdom Come forum](https://forum.kingdomcomerpg.com/t/so-third-person/35433?page=2); [Highrise ThirdPersonCamera](https://create.highrise.game/learn/studio-api/scripts/ThirdPersonCamera) | — | low |

## Open questions and gaps
- **B:** no blend shapes confirmed in the packs (artist checks on import); UMA's wardrobe and hide APIs unverified; DayZ creator depth unverified; the full GDC talks were read as overviews only.
- **C:** no Bohemia technical source for one body serving both views; no population data by view mode; no source on water cameras in third person; The Division's first-person handling unknown.
- **A:** the landing-6 overlap visibility and the tower ramp foot float await director (above); the facing decision on mesh-less `Animations.fbx` is confirmed only at import.

## Staleness map
- **Server-config keys (C1, C4, C7, C16):** they change with game patches; re-check when the third-person issue is opened.
- **Unity docs (B4-B7, B27-B28, C20-C26, C36):** re-check on the next Unity or Cinemachine/NGO upgrade (the 6000.7 question in #13).
- **The earliest re-check is 2027-04-18** (a 6-month window), or when issue 12 or #13 opens, whichever comes first.
