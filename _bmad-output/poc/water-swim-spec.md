---
title: 'Player in water: wading and surface swimming (W6, W8 inputs)'
owner: systems-designer (pod name "w-designer")
created: '2026-10-06'
status: r3, swim numbers approved (ruling/water-swim-numbers, studio:decisions); built only after PHASE 1 OPEN
spec: _bmad-output/planning-artifacts/poc-spec-washed-ashore-water.md (Phase 0 items 4, 5, 7; W6, W8)
inputs: bells-bend-design-brief.md (c) bank rule; bells-bend-td-note.md §3 checklist; greenlight/water (studio:decisions)
consumers: w-engineer (build), w-qa (W6/W8 plan), w-director (approval), w-td (research.md items 4/7)
---

# Player in water

**Player problem.** The river surrounds the bend on every side, and the player has never been in it. Water must read as a
heavy, slow hazard you can step into and get out of again, never a faster road and never a way round the north line.

**Loop it serves.** Explore → reach the bank → (optionally) wade or swim a short way → climb out on a low bank. Water edges
the play space; it is not a traversal shortcut (greenlight/water note 3).

**Not in scope (automatic FAIL if built):** drowning, stamina, breath, oxygen, death, respawn, health. Name nothing in code
`breath`, `oxygen`, `stamina` or `drown`. Duck-under and current push are deferred too (r2).
The drowning-as-boundary design for later is in the research section (research.md item 4b), not here.

## 1. Inputs and terms

| Term | Definition |
|---|---|
| `S(x,z,t)` | Water surface height: the W9 deterministic surface function, based on `MapConfig.WaterLevelY`. **No literal 1.61** anywhere |
| `G(x,z)` | Ground height under the player: `TerrainQuery.TryGroundHeight` (the lake bed over water) |
| `depth` | `S(x,z,t) − G(x,z)` at the player's XZ. ≤ 0 means dry |
| Live controller (World.unity, read at test time) | walk 5, sprint 8 m/s, accel 20, decel 25, jump 1.2 m, gravity −20; CC height 1.8, radius 0.4, eye (cameraPivot) 1.65, stepOffset 0.4, slopeLimit 45 |
| Bank (brief c, rev 14) | Lip W + 0.3 meets outside cells clamped ≤ W − 0.01 (0.31 m riser); 18° shelf out to bed W − 2 (~7.1 m out), then smoothstep to W − 10 by 20 m |

State is decided **analytically from `depth`**, never from trigger volumes or collisions with the water.

## 2. States

| State | Enter | Leave | Movement |
|---|---|---|---|
| Dry | depth ≤ `wadeMinDepth` | → Wade when depth > `wadeMinDepth` | Unchanged PlayerController |
| Wade | `wadeMinDepth` < depth < `swimEnterDepth` | → Dry at depth ≤ `wadeMinDepth`; → Swim at depth ≥ `swimEnterDepth` | Grounded walking on the bed, gravity as on land, speed × `wadeSpeedByDepth(depth)` |
| Swim | depth ≥ `swimEnterDepth` (also on spawn/teleport/clamp snap into deep water) | → Wade at depth ≤ `swimExitDepth` | No gravity. Feet held at `S − floatDepth` (never below `G` + skin). Horizontal swim speeds. `isGrounded` is ignored |
| Duck | **Not built in the POC (r2).** The director allows it but doesn't require it | n/a | Numbers parked in §3 for later |

Hysteresis: Swim enters at 1.35 m and exits at 1.15 m, so the shelf never flickers between states.
Evaluate the state every frame **after** `WorldBoundsClamp` and respawn moves too (they teleport the player; the next frame's
depth decides the state, no stale state).

## 3. Numbers (tunables; APPROVED, `ruling/water-swim-numbers`)

All values live in a **`SwimTuning` ScriptableObject** (`WashedAshore.Gameplay`, one asset referenced by PlayerController,
`[Range]`/`[Tooltip]` on every field, path of w-engineer's choosing under `Assets/`). No literals in code. The water height
comes only from MapConfig via `S(x,z,t)`.

| Field | Value | Note |
|---|---|---|
| `wadeMinDepth` | 0.05 m | Ignores surface ripple at the lip |
| `wadeSpeedByDepth` | AnimationCurve, linear keys: (0.05, 0.9) (0.3, 0.75) (0.6, 0.6) (1.0, 0.5) (1.35, 0.4) | Multiplier on walk speed: 4.5 → 2.0 m/s |
| `wadeSprintMaxDepth` | 0.3 m | Sprint (× curve) allowed shallower; ignored deeper |
| `wadeJumpMaxDepth` | 0.6 m | Jump disabled deeper and while swimming |
| `waterAcceleration` / `waterDeceleration` | 8 / 10 m/s² | Wade and land-to-water entry (land is 20/25) |
| `swimEnterDepth` | **1.35 m** | Chest depth; eye 0.30 m above water |
| `swimExitDepth` | **1.15 m** | Must be < `swimEnterDepth` |
| `floatDepth` | **1.30 m** | Feet below the surface while swimming; eye 0.35 m above water |
| `swimSpeed` | **1.8 m/s** | 36% of walk; below the deepest wade (2.0). With normal input (no sprint) and on stroke means, speed only falls as depth rises. Sprint-stroke (2.4) beats chest-deep wading (2.0, sprint off); the director accepted this (note a) because it's still under half of walk |
| `swimSprintSpeed` | **2.4 m/s** | Sprint key while swimming; still < half of walk |
| `swimBackStrafeMultiplier` | 0.6 | Backward/sideways input |
| `strokeSurgeAmplitude` / `strokeSurgeHz` | 0.2 / 0.9 | Speed pulses ±20% per stroke: the laboured feel without a HUD. 0 disables |
| `swimAcceleration` / `swimDeceleration` | 3 / 2 m/s² | Glide when input stops |
| `swimOverspeedDeceleration` | 6 m/s² | Used when speed > the swim target (sprint-jump into water: 8 → 2.4 m/s in ~1 s, ~5 m) |
| `surfaceFollowRate` | 4 m/s | Max vertical speed when tracking `S − floatDepth` (bob follows the surface) |
| Current push on the swimmer | **none** (r2, `ruling/water-swim-numbers-r2`) | The current is visual only, but it must still visibly drift downstream (greenlight note 2), ideally following the bends rather than one global direction. That's for w-td/w-artist's motion model. A push, if added later, needs a flow field and a W8 re-sweep |

**Duck-under: deferred, not built in the POC (r2).** For when it is built: hold C / LeftCtrl / gamepad buttonEast; sink 1.5 m/s,
rise 2.0 m/s, at most 1.2 m below float, at most **2.0 s** under, 1.5 s cooldown, horizontal ×0.6. It's a fixed allowance, not breath.
Binding check (director note c): PlayerController has no crouch action today (only Move, Look, StickLook, Sprint and Jump), so C and LeftCtrl
are free. If a crouch is added before duck, duck uses that crouch action in water instead of taking a second binding.

**Input.** No new actions. Movement stays yaw-only (camera pitch never steers a swimmer up or down).

**Validation (OnValidate + an EditMode test):** `swimExitDepth < swimEnterDepth`; `floatDepth ≤ swimEnterDepth`;
`floatDepth + 0.2 ≤ eye height (1.65)` so the camera stays above water; `swimSprintSpeed < walkSpeed`;
`wadeSpeedByDepth(swimEnterDepth) × walkSpeed ≥ swimSpeed` (with normal input, the stroke-mean speed never jumps up when you
start swimming; the ±20% surge peaks and the sprint-stroke may exceed it).

## 4. Collision rules

1. **The CharacterController must never ground on water.** Water quads carry **no non-trigger collider** (they are on layer 4
   Water; queries are analytic via `S`). If a collider is ever added it must be a trigger, or the Physics matrix must exclude the
   player's layer from Water (w-tools owns ProjectSettings). EditMode test: every water object has no Collider or only triggers.
2. In Swim, the controller moves vertically toward `S − floatDepth` at ≤ `surfaceFollowRate`; terrain collision still applies, so the
   capsule slides up the 18° shelf (< slopeLimit 45) as the bed rises, and the state flips to Wade at 1.15 m.
3. `GuardFallThrough` is unchanged (the bed is terrain).
4. `WorldBoundsClamp.SnapController` puts a snapped player at `max(y, ground + 0.5)`; over deep water that keeps the swim height.
   No clamp change is needed.

## 5. Exits on low banks

No special climb is built. On a low bank the path out is: swim → (depth 1.15 m, ~4.5 m out) wade up the 18° shelf → 0.31 m lip
riser (< stepOffset 0.4) → bank ≤ 35°. If W6 finds any snag, fix in this order and tell w-designer: (1) the swim vertical target
near the shelf, (2) a ledge assist capped at 0.45 m (never above `stepOffset + 0.05`, so water never climbs what land can't).
Bluff faces and protected cut banks are not exits, by ruling.

**Fix 1 as applied (r4, Phase 1, w-engineer).** W6.8 found 6 frames grounded at depth 1.35-1.48 (the uphill side of the 0.4 m capsule
touches the 18° shelf). The "never below G + skin" clamp now uses the highest ground under the capsule footprint (the feet point plus 4
points at ±radius), **counting only samples whose ground is below the surface (G < S)**. Dry samples are ignored, so a swimmer pressing
into a bluff face or high bank is never lifted onto its top. No numbers changed. Added test: swim into each bluff and high-bank station at
0° and 30° for 10 s; the feet never rise above S + 0.05, and the player never ends on ground above W + stepOffset.

**Bluff-neighbour stations (W6.5 reading).** No low-bank station sits next to either bluff (the stations on either side, 1599/1623 and
1827/1850, are high banks). They and the first bluff station of each run are graded by the no-trap check (swim away freely, no climb out).
Nearest low bank to both bluffs: **1549**; 309.5 m from McCord, 1338.5 m from Buzzard; the east bank from McCord to the north line
has no low bank, by ruling. (Straight-line XZ from the map_vectors.json markers. Verified by w-qa against `Data/terrain/build/bank_stations.json`,
the rc4 table of record: 1890 stations, 732 lowBank, 45 bluff-excluded, 19 seam-excluded, lake mask 8f8a637cd4657d82. The 766 in the TD note
was qa-2's independent 5 m sampling, not this table.) In the exit test, 1549 stands in for both bluff-neighbour stations.

On the shelf the geometry is depth = 0.325·s − 0.3 (s = metres out from the polygon edge): wade from the lip to ~5.1 m out, swim beyond.

## 6. W6 reading for w-qa (important)

The spec's W6 says "shallower than the brief's wading depth, wade; deeper, swim". The brief's wading depth is the **bed** at
W − `wadeDepth` (2 m), the end of the shelf. A 1.8 m player standing in 2 m of water has eyes 0.35 m **under** water, so the swim
switch cannot sit at 2 m. Reading of record (APPROVED by w-director, `ruling/water-swim-numbers` item 5):
- Wading happens on the shelf, between W and W − `swimEnterDepth`; swimming starts at chest depth, which is inside the shelf
  (~5.1 m of the ~7.1 m shelf). Deeper than the brief's wading depth the player is **always** swimming.
- Dry speed above W (depth ≤ 0.05), wade speed = walk × `wadeSpeedByDepth(depth)`, swim speed 1.8 (2.4 sprint) at depth ≥ 1.35.
- w-qa reads all thresholds from the `SwimTuning` asset at test time (as with PlayerController values), not from this document.

## 7. Acceptance criteria (for w-qa; extend the W6/W8 rows in water-qa-plan.md)

**How swim speed is measured (r3).** Swim speed carries a ±20% stroke surge, so every swim-speed bar is graded on the **stroke
mean**: horizontal speed averaged over whole stroke periods (1 / `strokeSurgeHz`, read from SwimTuning) at steady state.
Frame peaks are reported, not graded. The expected peak is target × (1 + `strokeSurgeAmplitude`): 2.16 for swimming, 2.88 for sprint-stroke.
Wading has no surge and is graded per frame at steady state.

**W6**
1. Speed vs depth (walk input, no sprint, flat-bearing probe from land into water): dry 5.0 ± 0.1; at depth 0.3 → 3.75 ± 0.2; at 1.0 → 2.5 ± 0.2;
   swimming stroke mean 1.8 ± 0.1 (sprint-stroke mean 2.4 ± 0.1). With normal input: swim stroke mean < every wade speed < walk.
   Sprint-stroke is exempt from "< wade" (director note a). Its stroke mean only has to stay under walk / 2.
2. Swim 50 m in open water (depth ≥ 3): eye stays 0.35 ± 0.1 m above `S` every frame, `isGrounded` false, no state flips, time 27-30 s.
3. State hysteresis: walk out along the shelf and back: exactly one Wade→Swim and one Swim→Wade transition each way.
4. While swimming, the camera never goes under `S` (duck-under is parked, `ruling/water-swim-numbers-r2`; it gets no bar until it's built).
5. Exits: per w-qa's plan (10 seeded low-bank stations + west neck corner + 2 per bluff side): swim straight at shore from 20 m out,
   reach ground ≥ W + 0.3 within 15 s (20 m at 1.8 + wading), no stall (horizontal speed < 0.2 m/s with input held) > 1 s.
   Bluff/protected stations are expected to fail to exit; they record "not an exit" and must not trap the player (swim away freely).
6. Sprint-jump into water, **sprint held throughout** (the realistic case): from 8 m/s, the stroke mean over the first full stroke that
   starts ≥ 1.5 s after entering Swim is ≤ 2.5 m/s (target 2.4). Frame peaks up to 2.88 + 0.1 are reported, not graded.
   Second case, sprint released on entry: the stroke mean on the same window is ≤ 1.9 m/s.
7. No HUD bars or meters added; no fields or types named breath/oxygen/stamina/drown/health (AF5 grep).
8. Water objects have no non-trigger colliders; `isGrounded` never true while `S − G ≥ swimEnterDepth`.

**W8 swim-around expectations**
- Every attempt type in w-qa's plan runs with the real controller in water: direct, bank-hugging (≤ 5 m off the bank, wading where
  shallow), mid-channel, diagonal 30°/60°, sprint-then-swim (sprint-jump off the bank). Duck-under
  attempts only if duck is ever built. ≥ 20 per end, 0 crossings, player Z ≤ line Z every frame.
- Expected stopper: the full-width backstop first; the clamp only if a body gets past it. Report which one stopped each run.
- At 1.8 m/s, a 45 m swim approach takes ~25 s; budget test timeouts at ≥ 60 s per run so slow swims are not mis-scored as skips.
- No reliance on drowning (none exists). A slow current, if the motion model adds one later, must be re-swept.

## 8. Feel check (Noah, W6 in a build, needs team-lead's OK to launch)

Prompt: wade into a low bank to chest depth, swim 30 m out and back, climb out. Ask: (a) wading heavy and clearly slower?
(b) swimming slow and laboured, worse than walking? (c) climbing out reliable? Tuning in Phase 1 only inside a baton slot:
first `wadeSpeedByDepth` and `swimSpeed`, then `strokeSurgeAmplitude`. Allowed range without going back to the director:
swim 1.5-2.2 m/s, sprint-stroke ≤ 2.8 m/s, chest-depth wade multiplier 0.35-0.5. These are `SwimTuning` target values (stroke means),
not frame peaks. The §3 validation rule also bounds the band (director note b): chest-depth wade speed (walk × multiplier) must be
≥ `swimSpeed`. Any combination that breaks it (e.g. multiplier 0.35 with swim > 1.75) is out of band. The §7 bar "sprint-stroke < walk / 2"
also binds, so with walk 5 the usable sprint-stroke cap is < 2.5 (the director's 2.8 applies only if walk is ever raised). The swim enter/exit
depths (1.35/1.15) are approved fixed values, not in the tuning band.

## 9. Answers to w-engineer (r2)

1. **Thresholds:** `swimEnterDepth` 1.35, `swimExitDepth` 1.15, eye **0.35 m** above `S` while swimming (float depth 1.30 with the
   pivot at 1.65; deriving float depth from the camera pivot is fine as long as the 0.35 eye clearance is the tuned value). Not 1.5: at
   1.5 the wading eye is 0.15 m above water, and the ripple would clip the near plane.
2. **Wade curve:** use the §3 curve, not a 1.0 → min lerp. It starts at 0.9 just past the lip, so the first step in reads as heavier.
   Sprint applies only below 0.3 m (× curve). Deeper, sprint is ignored while wading. While swimming, sprint gives the 2.4 sprint-stroke.
   Momentum bleeds with the water accel/decel and the overspeed decel.
3. **Swim:** 1.8 m/s; accel/decel 3 / 2; overspeed decel 6. Jump only below 0.6 m depth. Never while swimming.
4. **Duck-under:** agreed, not built for the POC. The director allows it but doesn't require it, and dropping it cuts W8 surface area.
5. **Current:** agreed, visual only, no push.
6. **Where numbers live:** in the `SwimTuning` ScriptableObject, not as loose fields on PlayerController. team-lead's rule is a
   tuning ScriptableObject or MapConfig-style config, and an asset lets me tune in a baton slot without editing World.unity.
   PlayerController holds one serialized reference to it (with a clear error if it's missing).

## Change log
- 2026-10-06 r1: first draft (w-designer), Phase 0.
- 2026-10-06 r2: duck-under deferred, current push none, §9 answers to w-engineer.
- 2026-10-06 r3: numbers approved (`ruling/water-swim-numbers`, studio:decisions; its item 5 makes `swimEnterDepth` 1.35, not
  W − wadeDepth, the W6 swim switch). Bars fixed, numbers unchanged: swim bars graded on stroke means, with peaks reported (§7);
  sprint-jump bar defined with sprint held (plus a released case); duck bar dropped (parked, r2 ruling); monotonic-speed claim reworded (note a); tuning band
  bounded by the validation rule (note b); crouch binding check (note c).
- 2026-10-06 r4 (Phase 1): §5 fix 1 as applied (footprint clamp counting only underwater samples, plus a bluff no-climb test);
  bluff-neighbour exit reading (1549); §8 sprint cap < walk/2.
