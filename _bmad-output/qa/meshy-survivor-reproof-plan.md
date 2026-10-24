# Re-proof test plan: Meshy "Weary Survivor" replacing the Farmer body

Author: qa (swap pod). Date 2026-10-22. Status: DRAFT test plan, nothing run (no Unity opened, no tests run, per the brief). A plan, not an import.

Source tags used below: [QA] = `_bmad-output/qa/player-character-qa-report.md`; [GATE] = studio:decisions `gate/meshy-survivor-eval` (director, 2026-10-22); [A5B] = look-down bar of record, QA report line 420; [A8G] = amend8g, QA report lines 518-527; [H9] = `TestResults/player-character/h9/noah-session-20261020-ratings.md`; [G43] = `gate/physics-layers-43` and `-amend1-l3`; [I35] = `issues/future-character-14-locomotion-clip-quality.md`; [I36] = `issues/future-character-15-womens-rig-first-person.md`; [SPEC] = poc-spec-washed-ashore-player-character.md; [CODE] = test or source file read. A number with no tag is my choice and says so.

## 0. What "the swap works" means

All of this, with no waiver beyond the ones already of record:

1. Every bar the Farmer passed in section 2 passes again on the Meshy body, on the same build path, graded by exactly the same tests (not re-tuned thresholds).
2. The first-person view is unchanged in kind: the camera is never moved by animation; the view body has a hideable head; the shadow body casts the full silhouette, headwear included [GATE].
3. Nothing the Farmer pod recorded as a known item resets: #35 and #36 are carried forward, not cleared by the swap [GATE].
4. The provenance of every Meshy asset (source, licence, date, tool terms) is recorded in the clip/asset manifest before it enters the project (automatic FAIL otherwise) [GATE][SPEC]. A download needs Noah's approval with file name, URL, size and licence [I35].
5. Noah rates the feel check on the new body and says "right" (H9 cells) [SPEC].

Waivers that carry over unchanged, and nothing more: amend5b cells (Stand +50..+55 torso clause, Crouch +40..+75 torso clause) and amend8g cells (bar 2 only: Stand Sprint 8 +60..+85, Jog 5 +60/+65). Bar 1 is absolute at every pitch, every gait, every stance [A5B][A8G]. A waiver was granted for the Farmer view body and the director's basis was six stills of that body [A8G (3)]; whether it extends to a different body is a director question (see risk R-W below). I will not assume it does.

## 1. Pre-conditions that decide the plan's shape (unknowns today)

The director did not open the FBX meshes, so these are UNKNOWN [GATE] and the first test step is to measure them (read-only, in a scratch project or EditMode, with Noah's approval for the asset to be in a scratch location):

| Unknown | Why it decides tests |
|---|---|
| Head separate from body? (one fused mesh per [GATE]) | If fused, the view-body head hide and the shadow-body full silhouette both need a new method (mask, split, or vertex-group). All of H5 and view/shadow sync re-proves. |
| Bone names, count, finger bones | The tests hide the head and measure Neck/Head/Shoulder triangles by dominant bone (bar 1: "dominant bone is Neck/Head/shoulder top" [QA line 192]). Different names break the bar-1 detector until remapped. |
| Rig: Humanoid or Generic | The postprocessor skips Humanoid rigs and a separate facing test applies [SPEC H1]; the pod's rig is Generic with the Animator on `CharacterArmature` [QA line 179]. Retargeting 64 clips is a large step. |
| Polycount, material count, texture size (4096 PNG seen [GATE]) | FPS bar and memory; skinned vertex cost doubles because two bodies are skinned [QA lines 428-430]. |
| Facing (+Z) and scale/eye point | Eye match and `s = 1.030` scale rule [QA line 226] are body-specific; a new body needs its own scale and clip eye offsets. |
| Headwear | Shadow hat must exist as a separate renderer or be authored; the director wants "no pristine hat" [GATE]. |
| Licence and terms of the Meshy output | Must be recorded before import (see 0.4). Not assessed here. |

## 2. Re-proof matrix

Column "Exists" = a test or harness already in the repo that can be reused as is (names from [CODE], read only, not run). "New" = to be written. Every row names the evidence file path convention `TestResults/meshy-survivor/<criterion>/`.

### 2.1 Import and assembly (H1, H2, D7)

| ID | Bar | Test or evidence | Exists / New |
|---|---|---|---|
| M1 | Clean scripted import, no hand steps, one entry method, licence file copied in | `CharacterImportTests.LicenseIsCopiedIn`, `ModelImportsAsGenericWithValidAvatar` (or Humanoid variant if rig differs), import log with a single entry method | Exists (needs Meshy fixture); Humanoid avatar validity check New if Humanoid |
| M2 | Facing +Z, same decision on every rigged file | `FacingDecisionIsTheSameOnEveryRiggedFile`, `AssembledBodyFacesPlusZ`; `TestResults/model-facing-report.json` 0 ambiguous | Exists |
| M3 | Required bones present, matches shared skeleton, or a bone map exists and is tested | `PartHasRequiredBonesAndMatchesTheSkeleton` | Exists; bone-map test New |
| M4 | Assembles with 0 errors/warnings; runtime part swap keeps pose and seams; missing/wrong-type part refused | `OutfitAssemblesWithNoErrors`, `LegsSwapAtRuntimeKeepsThePoseAndTheSeams`, `MissingPartFailsWith...`, `PartFromTheOtherBodyTypeIsRefused...` | Exists. Runtime Legs swap and part-swap checks are N/A by the director's ruling (via producer, 2026-10-22): H2 customisation dropped as a named scope change, the Survivor is the single default look, customisation returns as a future item; Noah's word wins if he wants it kept |
| M5 | View copy follows shadow pose, has no head, Animator or collider; rebuilds on swap | `ViewCopyFollowsTheShadowPoseHasNoHeadAnimatorOrColliderAndRebuildsOnSwap`; `ViewCopyCarriesTheShadowTints...` | Exists; "no head" step New for a fused mesh |
| M6 | Appearance hashes: all nine existing appearance assets unchanged after the swap work (the swap must not touch the Farmer assets) | qa rehash against `appearance-hashes-after-22b-7.txt` [QA line 461] | Exists (procedure) |
| M7 | D7: default look is a believable survivor, style fits low-poly world | Screenshot set at the D7 positions, art-director sign-off; style is the director's reason for NOT GREENLIT [GATE] so this is a gate, not only a test | Exists (D7 stills); art-director judgement New |

### 2.2 Look-down and first-person view (H5, amend2, amend5b, amend8g)

| ID | Bar | Test or evidence | Exists / New |
|---|---|---|---|
| M8 | Look-down at amend5b: FULL bar at Stand +60..+85 and Crouch +80..+85; bar 2 waived only at Stand +50..+55 and Crouch +40..+75 [A5B]; bar 1 absolute everywhere; amend8g cells waived for bar 2 only | `LookDownTests.StandAndCrouch_LookDown_BackOffsetSweepAndConfiguredTables` (asserts bar 2 outside the waiver, prints WAIVED inside) plus `-clipsweep` rows Walk 1.15/1.5, Jog 5, Sprint 8 | Exists. Tables (`LookDownOffset`) were tuned to the Farmer; a new body almost certainly needs new tables (the women's rig did [I36]), so `LookDownOffsetTests` EditMode table tests need new points |
| M8a | Waiver re-grant stills (was R-W). Director ruling (via producer): the amend5b ranges (Stand +50..+55, Crouch +40..+75) and amend8g cells (Stand Sprint 8 +60..+85, Jog 5 +60/+65) are the MOST the Survivor can be waived for; each cell needs a fresh director re-grant on Survivor stills, 16:9 and 21:9, at the fewest-torso cycle point; the full bar applies everywhere else; bar 1 absolute | For every cell asked for: measured row from the sweep (bar 1 count, bar 2 torso count) printed WAIVED-REQUESTED with its figure, stills at 16:9 and 21:9 at the fewest-torso cycle point (found by scanning the gait cycle), summary file like `waiver-stills-summary.txt` (frames settled, bar 1, arm-in-body). Director opens the stills. Until a cell is re-granted it is graded at the full bar: a miss is a FAIL, not a waiver | Method Exists (`WaiverStills_Sprint8_Jog5_GitHub37`, `ConfiguredStills_ForTheWaiver`); stills and the re-grant are New |
| M9 | Bar 1 detector must find the new body's neck/shoulder triangles (dominant-bone method) | Positive control: push a known arm/neck vertex set into view and see non-zero (as `ArmInsideBody_PositiveControl_ArmPushedIntoTorso_ReadsNonzero` does for arms) | New for the new bone names; control exists for arms |
| M10 | Forced-failure proof: each re-targeted assertion turns red on a deliberately bad body (wrong offset, wrong stance, no settle frames) | `CheckRow_FailsForAWrongStance_AndAWrongOffset`, `WrongStance_MustTurnRed`, `Gait_NoReplace_Sprint_MustTurnRed`, `Settled_MidCrouchEase_MustTurnRed` | Exists; rerun on the new body is mandatory ("force the failure before claiming" rule, my memory) |
| M11 | Stills: Stand and Crouch +60 and +85, still and 4 walk-cycle points; chest or belly at bottom, legs beyond; no neck stump, open edge, shoulder cap | `ConfiguredStills_ForTheWaiver`, `WaiverStills_Sprint8_Jog5_GitHub37`, 16:9 and 21:9 | Exists |
| M12 | View body with the head hidden: 0 visible triangles whose dominant bone is Neck/Head/shoulder top, 0 back faces over a full-frame ray grid | Same ray-grid method [QA line 192] | Exists (method); needs a head-hide implementation for a fused mesh, New |
| M13 | Shadow body: full silhouette with Head, Body, Legs, Feet and the headwear; shadow twin-diff fix on vs off 0 changed pixels at 16:9 and 21:9 | Shadow twin-diff [QA line 192]; hat shadow renderer table printed (the director held "the hat question" open until the renderer table was seen [QA line 186]) | Exists (method); renderer table New for the new body |
| M13a | Head split approved as the plan (producer, creative OK): head as its own mesh on the same armature, weights kept; view body hides it, shadow body keeps it. Cut seam: no neck stump, open edge or visible seam on the view body at every look-down pitch (bar 1) | LookDown sweep and stills as M8/M11, plus a boundary-edge count of the cut (open edges at the neck must be 0 on the body mesh, or capped under the collar) | New (cut-edge count); sweep Exists |
| M13b | Seam in the shadow: the shadow body is head + body together; no gap, light leak or crack line at the neck in the shadow render | Shadow STILL at 16:9 and 21:9 with the scene's own sun rotation, twin-diff of split mesh vs the unsplit export (0 changed px outside the cut region, any change inside it listed) | New |
| M13c | Split keeps weights: vertex weights at the cut identical on both sides (no pop at the neck during Sprint, Jump, Sleep); view and shadow copies animate the neck bones identically | EditMode weight compare across the seam; M25 sync test with neck/head bones in the bone list | New |
| M13d | No-cut fallback (collapse the head bone on the view body) instead of the split: same bars M12 and M8, plus no stray triangles at the neck and no other bone moves (shoulders, spine) | Triangle dominance count with head bone scale 0; bone world-position diff before/after collapse = 0 for non-head bones | New. One option per build; not both graded together |
| M14 | Pitch sweep -85..+85, 5 stances, 0 clipping, on the view body | `PitchSweepTests.EveryStance_PitchSweep_NoClipping` | Exists |
| M15 | Range-end eye/pitch proofs: 0 back faces at squat, sit, sleep, sleep pitch -45/-85 | `RangeEndTests.EyeEnds_LowAndHigh`, `SleepPitchEnds` | Exists. HIGH RISK: these failed on the women's rig (1 to 107 back faces) [I36], so a new body shape is likely to hit the same class |
| M16 | Hand glimpse: Walk and Run hands at pitch +30 at arm-swing peak, under about a tenth of frame [QA line 416]; capped arms keep hands out of frustum | `CappedClipsOnTheViewBodyKeepEveryHandOutOfTheFrustum`; hand-ray counts 0 at Jog and Sprint | Exists |
| M17 | Landing 6 body-in-steel overlap < 2.5 cm (D4) | `QaLanding6StillsTests` | Exists |
| M18 | Twin-diff every render measurement: gate stills on changed pixels versus a sign-free twin | Rule from my memory `twin-diff-every-render-measurement` | Exists (method) |

### 2.3 Eye match, stance heights (H5, H4)

| ID | Bar | Test or evidence | Exists / New |
|---|---|---|---|
| M19 | Eye match: camera world y minus shadow-body head eye point y within +-0.03 m, feet planted, one frame, all five stances, both y values printed; Stand feet within +-0.01 m of floor; scale over 1.06 or floating feet go to the director [QA line 220] | `StanceTests.SettledHeights_MatchTheTable`, `EveryTransitionFrame_KeepsTheEyeUnderTheCapsuleTop`, eye-match lines of `RangeEndTests` | Exists. A new body needs its own scale (Farmer: s 1.030 from pack Idle 1.70/1.651 [QA line 226]) and lowered-stance clip eye targets (stance eye + 0.05)/s |
| M20 | Eye heights at the table values: stand 1.65, crouch 1.20, squat 1.00, sit 0.80, sleep 0.20 [H9 startup line] and range ends crouch 1.10/1.30, squat 0.90/1.10, sit 0.75/0.90, sleep 0.20/0.25 [QA line 440] | `RangeEndTests`, `StanceRangeArgumentTests` | Exists |
| M21 | Enter/hold/exit/lie/wake transitions do not jump at any range end | `EnterHoldExitBoundariesAtEveryRangeEndDoNotJump` | Exists |
| M22 | Every clip's key set: only bones present; every stance clip exists | `EveryStanceClipExistsAndKeysOnlyBones`, `EveryStateIsReachableAndEveryRequiredClipIsUsed` | Exists; the clips must be retargeted or re-authored for a different skeleton: New work |

### 2.4 Animation, foot slide, sync (H3, H4, amend7)

| ID | Bar | Test or evidence | Exists / New |
|---|---|---|---|
| M23 | RETARGETED OR AUTHORED clips, no reuse of Farmer results: foot slide <= 0.10 m on every plant (foot within 3 cm of its lowest height, largest horizontal distance, sampled every frame in game) at forward Jog 5 and Sprint 8; back asserted; strafe and diagonals report-only (diagonals 0.19-0.20 m known) [QA line 263][I35] | `FootSlideTests.Gaits_Jog5_Sprint8`, `Sprint8_AllDirections_BothRigs`, `Sprint8_HipAndFoot_BothRigs`, calibration and forced-bad tests | Exists. The clip's stride length must match 5 and 8 m/s with playback rate <= 1.4 [I35 Q2]; Meshy's single clip "Jump and Grab Wall" does not [GATE], so every locomotion clip is retargeted/authored: New |
| M24 | Crouch speed range ends (1.25 / 1.75 / 2.50) slide <= 0.10 m | `CrouchSpeeds_RangeEnds` | Exists |
| M25 | View/shadow sync: non-arm bones within 1 mm over a 60 s mixed run, 0 resyncs; Parameters equal on both Animators same frame; shadow-only write turns red | `NonArmBones_ViewMatchesShadow_Within1mm_OverA60sMixedRun_NoResyncs`; `ViewAnimatorParameterTests.*`; `PoseBounds_ShadowHandsInsideRendererBounds` (wrists included, per td [QA line 272]); `SyncGuardResyncsADriftedViewAnimator...` | Exists (Farmer result: 0.061 mm, 0 resyncs [QA line 453]); the arm-cap override maps (8 clips to _Full on shadow, none on view) must be rebuilt for the new clip set, New |
| M26 | Shadow override maps exactly the eight capped clips to _Full; view maps none; every range-variant controller too | `ShadowOverrideMapsExactlyTheEightCappedClips...`, `EveryControllerTheShadowCanReceive...`, `RangeControllersAlsoSwapTheEnterAndExitClips` | Exists; new controllers New |
| M27 | Pose bounds inside renderer bounds in every stance clip | `PosedMeshStaysInsideTheRendererBoundsForEveryStanceClip` | Exists |
| M28 | Debug-cam side stills (sprint mid-stance, mid-flight), the arms and the guard look | `DebugCamera_Sprint_SideStills_...`, `DebugCamera_FullSwing_Stills` | Exists (these are the #35 evidence) |
| M29 | Manifest: every clip has source, licence, measured ground speed, rate <= 1.4 [I35 Q2, Q3]. Noah's direction (via producer): Meshy Walking, Running and Walk_Backward_inplace are reference only; all locomotion (walk, jog, sprint, every direction) and the stance set come from the Unity side, either a Humanoid retarget of the current clips or clips authored in Unity (swap-engineer says which per clip). Meshy manifest rows only for an optional Meshy clip actually taken in (swim, sleep, lie down, wake, idles, falls are optional) | Manifest `_bmad-output/poc/character-clip-manifest.md` | Exists (file); Meshy rows New |

### 2.5 Performance (H8)

| ID | Bar | Test or evidence | Exists / New |
|---|---|---|---|
| M30 | FPS with body on within 5% of off, both Animators counted. 8-arm protocol, release build: alternating ON/OFF x 4 runs, all through the real probe path (`-bbFpsWalk`, `-bbBodyOff`), median frame time ON vs OFF, both pairs agreeing; JSON fields animator_count = 2 and SMR counts recorded | `BellsBendFpsWalk.cs` (the `-bbBodyOff` arm and a real-animator feed exist [CODE lines 26, 57, 78]); Farmer result +0.99% in `TestResults/player-character/fps/noah-session-20261020/` [H9] | Exists (procedure and probe). Run by team-lead with Noah present, never by qa launching the player; same quiet-machine and `query user` rules [QA line 121]. The risk is higher than for the Farmer: a 4096 texture and an unknown vertex count on two skinned copies |
| M31 | Release build contains no debug camera and ignores `-bbStance` (byte search of the built player: StanceRangeArgument absent, debug camera absent, -bbBodyOff present) | Byte search of `Managed/Assembly-CSharp.dll` [QA line 119]; `[Stance] eyes ... range args: none` at first release launch | Exists (procedure) |
| M32 | Build succeeds on Windows (Mono); Linux/macOS reported as "waiting on Noah's word" unless he asks | `BellsBendWindowsBuild.BuildBatch` log | Exists |
| M33 | Texture memory and import: texture size, compression and mipmaps recorded; no 4096 base map left at full size without a td ruling | Import settings dump | New (my choice of check; no number given by a source, td to set the budget) |

### 2.6 Stances, tower, water (H6, D3, D5)

| ID | Bar | Test or evidence | Exists / New |
|---|---|---|---|
| M34 | Stance matrix: 43 cells (stance x context), sit/sleep only standing still on ground, blocked on tower, water, air; bindings no conflicts; sleep dim with no Canvas | `StanceMatrixTests` (43), `StanceTests` (`Sleep_RulesAndDim`, `Airborne_BlocksEntry`, `SitAndSleepRefuseBadGround`...), `StanceBindings_DoNotShareControlsWithOtherActions`, `StanceRangeArgumentTests` | Exists |
| M35 | Tower: lowered stances blocked on the whole tower; entering crouched never leaks the crouch capsule; TowerWalk up time 26.6 s +-10%; camera roll 0, FOV constant, eye-to-steel 0.15/0.20 m minimums in every stance allowed on tower | `StanceTowerTests.EnteringTheTowerCrouched_NeverLeaksTheCrouchCapsule`, TowerWalkTests (Farmer 26.4 s [QA line 560]); manual tower look-down watch item (PASS in [H9]) | Exists. The capsule must not change with the body (the body has no collider [CODE]); a test that the capsule radius/height values equal the Farmer's is New. Note: the railing snag (#41) and un-wired tower interactions (#40) are not body-dependent; they stay open and are not part of "the swap works" |
| M36 | Water D5: camera y above the water surface in every stance at the water edge, wade and swim; lowered stances blocked in any water | `WaterSwim` tests; Noah wade-in check (PASS, [H9]); no separate underwater-camera test was read in the Farmer pod [QA line 566] | Exists partly; a dedicated camera-y vs water-surface test per stance is New (and would also close a Farmer-pod gap). Swim/wade pose is the placeholder, logged |
| M37 | Body fits 0.62-0.72 m tower flights without snagging | Manual climb by Noah (PARTIAL for the Farmer: #41 [H9]) | Exists as manual; compare to Farmer, not to a clean pass |

### 2.7 Physics-layer rule (#43)

| ID | Bar | Test or evidence | Exists / New |
|---|---|---|---|
| M38 | At runtime both bodies (shadow and view), all their child renderers and any collider, and the controller are on the Player layer. The assembler creates the bodies at runtime, so the layer must be set at the creation point, not in a vendor asset; vendor/ThirdParty assets (ModularCharacters etc.) are not re-layered in the asset [G43 C5][CODE physics-01 L1] | PlayMode: after assembly, walk the hierarchy and assert `layer == WorldLayers.Player` for every transform of both bodies; after a part swap and after respawn; after `RebuildWith` | New (layer assertion); the Meshy asset must not be modified to carry a layer |
| M39 | Every camera culling mask and the sun's culling/shadow settings include Player; view body visible; shadow body still casts (hat included); qa twin still at Stand +60 vs the pre-change image [G43 C1] | Twin-diff still; `LookDownTests` rerun | Exists (method) |
| M40 | Robins still treat the player as an obstacle (RobinFlightPlanner.ObstacleMask includes Player); queries keep ignoring the player's own colliders (stance, climb fit, Interactor) and still see props/vegetation as before (#38 refusals unchanged) [G43 C2, C4] | Bird tests, `StanceTests.SitAndSleepRefuseBadGround` | Exists |
| M41 | No `~0` or `~excludeLayers` in Assets/Scripts or Editor code introduced by the swap; the five named test-only exemptions stand and no new one is added [G43 amend1] | Grep | New run of an existing rule |
| M42 | No new collider on the body (the pod's view copy has none); if Meshy import adds a collider or ragdoll, it is stripped or on the reserved Ragdoll layer | Hierarchy dump | New |

### 2.8 Carried-forward items

| ID | Item | Re-proof | Exists / New |
|---|---|---|---|
| M43 | #35 clip quality: Q10 natural swing (sprint/jog arms and legs "over exaggerated" [H9 cell 9]), fingers (splayed, pack-wide), knees (mid-flight about 80 degrees), diagonals (0.19-0.20 m), boxer's-guard arms [I35]. The Meshy body must be at least as good as the Farmer on each, measured the same way; none is "reset to clean" by the swap and none may be hidden by a different pose that merely avoids the measurement | Q6 finger-spread measure per clip, Q7 side sheet, Q8 `Sprint8_AllDirections_BothRigs` diagonals, Q9 side stills | Exists as criteria in [I35]; most measures are New. Q10 (natural swing): source of record is amend8j (6), QA report line 635 (director, via producer); also H9 log row 9 ("arms and leg movement don't look very realistic and are over exaggerated", deferred by Noah). Q10 criterion of record (amend8j (6)): jog and sprint arm and leg swing amplitude reads natural, not exaggerated, from the debug camera; first-person arm cap (hand count 0) kept; Noah rates it right. No numeric bar, so the re-proof is Noah's rating plus the debug-cam side stills; a number would be my choice and needs td |
| M44 | #36 women's rig first person: the Meshy figure is male; the swap must not break the women's path (the women's rig stays as it is, its 5 open failures W1-W5 remain open, men's results unchanged W5) [I36] | The women's `PC_RIG=women` run before and after (13 of 18 as the baseline [I36]) | Exists (procedure); new run |
| M45 | Appearance/customisation: N/A by director ruling (via producer, 2026-10-22): dropping H2 is an accepted named scope change; Noah's word wins if he wants it kept. QA still checks that the default appearance asset and the nine Farmer appearance assets are untouched (M6) and that no code path leaves a dead swap hook that the release build strips differently | Ruling cited; M6 hashes | N/A (M6 Exists) |
| M46 | Known open Farmer-pod items must not be blamed on or credited to the swap: #38 (sit/sleep refusal feedback), #39 (jump), #40 (tower interactions not wired), #41 (railing snag). Run these checks on both bodies to prove they are body-independent | Same repro, Farmer vs Meshy | New (comparison) |

### 2.9 Feel check and sweeps

| ID | Bar | Test or evidence | Exists / New |
|---|---|---|---|
| M47 | Noah's H9 feel check, from first person and the debug camera: walk, run/sprint, jump, crouch, squat, sit, sleep, look straight down in Stand and Crouch, tower stairs, plus the watch items (the leg that seems to vanish under bracing, sprint legs, sprint arms). Rated "right" or "not right" with a reason, rating log as in [H9]; at most 2 retunes, ends only [QA line 132]. The Farmer cells 3 (jump), 6 (sit), 7 (sleep) were NOT RIGHT; the Meshy rating must be read against that, not "no worse" [H9] | H9 sheet | Exists (sheet); new session, team-lead with Noah, `query user` check, `-monitor 1`, heads-up first |
| M48 | Full EditMode + PlayMode run by test name against the baseline: EditMode 21 failing (baseline), PlayMode baseline 43 plus the one known `LookDown...ConfiguredTables` case that has since been fixed; no new failure by name; no baseline failure silently changing cause [QA lines 9, 560] | Test XML vs `TestResults/player-character/baseline/*-failed.txt` | Exists. Caveat from the Farmer pod: the last full-suite rerun did not follow the #26 test edits [QA line 560]; this plan requires a full rerun after the last code change |
| M49 | Automatic-FAIL sweep: no HUD, no player-facing third person, speeds unchanged (walk 5, sprint 8, jump 1.2, tower 1.15 and 1.5), no unlicensed asset, no hand steps, no unapproved download, no GitHub issues created early, not the same blocker 3 times, not more than 3 assists | Sweep at every verdict [QA line 94] | Exists |
| M50 | Speeds and capsule unchanged | `StanceTests.Speeds_CrouchWalkSprint`, `SettledHeights_MatchTheTable`; git diff of those constants | Exists |

## 3. Risk ranking for the swap (my assessment, no run behind it)

| Risk | Why | Rating |
|---|---|---|
| R-W: waivers do not carry over | Director ruled they do not; each cell needs a re-grant (row M8a). Any cell not re-granted is graded at the full bar | High |
| R-H: head split | Approved as the plan; cut seam, shadow seam and weights across the cut are new work (M13a-M13d). Dropping H2 customisation is accepted by director ruling (M45 N/A) | High until M13 passes |
| R-C: clip set | 64 clips retargeted to a different rig; the foot-slide rates, the arm-cap pairs and the eye offsets all depend on it. The Farmer pod needed many baton slots on exactly this | High |
| R-F: FPS | 4096 PBR on two skinned copies, topology unreduced; the Farmer margin was +0.99% against a 5% bar [H9], but vertex count and PBR cost are unmeasured. Keep M30 (8-run A-B-A, quiet machine, release build) prominent and early in the order | High |
| R-S: style | The director's first reason; art-director gate, cannot be tested | High (non-test) |
| R-L: layer | Easy to set at runtime, easy to forget on a rebuilt copy (part swap, respawn, `RebuildWith`) | Low-Medium |

## 4. Effort (rough, my judgement, not measured, not sourced)

The Farmer pod ran from 2026-10-18 (spec, baseline) to 2026-10-21 (wade-in check) [QA baseline section; H9 log], with baton slots numbered past #25 [QA line 438]; I have not counted slots or hours. A Meshy swap with a fused body and a new rig repeats the import, retarget, look-down tuning and re-grade steps. I would not plan it as less than the Farmer pod. If the body arrives as separate parts on the same Quaternius rig, it is far smaller. That choice, not the test list, drives the effort.

## 5. Order of tests (cheap and decisive first)

1. M1-M3 and section 1 unknowns (EditMode, no player).
2. M38 layer assertion, M5/M12 head-hide and the head split rows M13a-M13d (decide whether the design stands). Run an early M30 timing-only FPS pass of the real path here too, since FPS is the likeliest budget problem.
3. M8-M15 look-down and pitch sweep (the long poles; they decide tables).
4. M19-M27 eye match, clips, slide, sync.
5. M34-M37 stances, tower, water.
6. M48 full run, then M30-M32 release build, FPS, strip checks.
7. M47 Noah's feel check last (never before the builds above pass).

## 6. Not in scope and flagged

- No Unity opened, no test run, nothing downloaded, nothing committed, per the brief. Every "Exists" is from reading the file names and test names in `Assets/Tests`; none was run and a test's name does not prove what it asserts.
- Q10: source confirmed by team-lead (GitHub #35 body, H9 row 9); the local issue file still lists Q1-Q9 only.
- The Linux/macOS builds follow the Farmer rule (waiting on Noah's word).

## 7. Method, pass number, source and new baseline per bar (producer's format)

Method key: UTF = Unity Test Framework (EditMode or PlayMode); STILL = twin-diffed still; PERF = release-build A-B-A on a quiet machine; MANUAL = Noah. "New baseline" = the number or file must be re-recorded on the Meshy body; the Farmer value is not reused.

| Bar | Rows | Method | Pass number | Source | New baseline? |
|---|---|---|---|---|---|
| Look-down | M8-M13 | UTF + STILL | bar 1 = 0 everywhere; bar 2 only where the full bar applies (Stand +60..+85, Crouch +80..+85); WAIVED printed in amend5b/8g cells | amend5b (QA line 420), amend8g (lines 518-527) | Yes: LookDownOffset tables, sweep CSV, stills |
| View body, head hidden | M12 | UTF ray grid + STILL | 0 neck/head/shoulder-top triangles, 0 back faces | QA line 192 | Yes: new bone names and head-hide method |
| Shadow body | M13 | STILL twin-diff + renderer table | 0 changed px fix on vs off at 16:9 and 21:9; hat in the table | QA lines 186, 192 | Yes |
| Pitch sweep and range ends | M14-M15 | UTF | 0 clipping, 5 stances; 0 back faces at squat, sit, sleep | QA line 560; women's rig failed this [I36] | Yes |
| Eye match | M19-M21 | UTF | within +-0.03 m, all 5 stances; feet within +-0.01 m; scale <= 1.06 | QA line 220 | Yes: new scale and clip eye offsets |
| Foot slide | M23-M24 | UTF | <= 0.10 m every plant, forward Jog 5 and Sprint 8, back asserted | QA line 263, I35 | Yes |
| View/shadow sync | M25-M26 | UTF | non-arm bones within 1 mm over 60 s, 0 resyncs; params equal both Animators | QA line 453 | Yes |
| FPS | M30 | PERF 8 alternating runs | ON median frame time within 5% of OFF; animator_count 2 | spec H8; Farmer +0.99% in fps/noah-session-20261020 | Yes (OFF arm can reuse the same build with -bbBodyOff) |
| Stances, tower, water | M34-M37 | UTF + MANUAL | 43 matrix cells; TowerWalk 26.6 s +-10%; camera above water in every stance | QA lines 560, 566 | Tower time and matrix: re-run, same number. D5 per-stance test: new |
| Feel | M47 | MANUAL | Noah "right" per cell, 2 retunes | spec H9; H9 log | Yes: new log |
| #43 layer | M38-M42 | UTF + grep | both bodies on Player at runtime; no ~0 in runtime code | gate/physics-layers-43, -amend1-l3 | New test |
| #35 / #36 | M43-M44 | UTF + STILL | Farmer-measured values as the floor, not reset | I35, I36 | Yes |

## 8. Effect of the art direction (amend1, Noah via team-lead, 2026-10-22)

Source: studio:decisions `gate/meshy-survivor-eval-amend1-art-direction`. The low-poly kit is being retired; the "style clash" reason no longer counts; the swap plans for the target style and does not flatten the Survivor. Option B (palette-snap, vertex colours, reduced mesh, no textures) is REJECTED and kept here only as a note. swap-art's revised section is pending; this section holds until it arrives.

What changes in the re-proof, under "keep as is or a light treatment" (the mesh and textures stay close to the Meshy export):
- M33 texture budget stays and gets weight: 4K textures are a named risk in the amend. Test: import settings dump plus the FPS bar (M30). The budget number (texture size, polycount per asset class) is an open Noah item (3); I will not invent one. Until td sets it, M30's 5% is the only hard number.
- Two skinned copies each carry the textured material: the FPS run must keep both Animators and both renderers on in the ON arm (animator_count 2), and record the SMR/vertex counts, since a realistic body may cost more than the Farmer's +0.99%.
- M7 style: the check is no longer "fits the low-poly world". It becomes "meets the art-director's written target style guide" (open item 2), which does not exist yet. QA cannot grade realism level or texel-density match; it only checks the guide is cited and the stills are taken with the scene's sun, FOV and eye height.
- D7 "default look" meaning the Survivor is open item 4; the D7 stills are re-taken after Noah answers.
- Re-look flag, not a QA test: look rulings tuned to the low-poly world (water look and haze 2026-10-06, D7 stills) will need re-checking when the world changes; the look-down and eye-height numbers do not depend on world style.
- Photo-real texture on a view body that sits close to the camera: M11 stills and M8 bar 1 are unchanged, but texture shimmer and the shadow body's hat must be judged on the real texture; the twin-diff method (M18) still applies.
- Licence: CLOSED. Noah confirmed (via team-lead) that amend2 is his word: Meshy is a stand-in and style benchmark, not shipped. If any Meshy file is ever imported, its licence and source go into CREDITS and the manifest first.
- swap-art rev 3: lead with E (keep the model as is), C (light treatment) as fallback decided by stills. Triggers for C: fold shading fights the sun, trousers/boots blend into the ground, plaid shimmer, clean pink hand skin, colour bleed at distance. Stills = twin-diff with the scene's sun, FOV, eye height. Any C texture fix reruns M30 and M33 and the M7/M11 stills, not the look-down sweep.
- No decimation, vertex bake or polycount cap: the Meshy topology stays unless swap-ta's FPS reasoning sets a budget. The skin-weight bar-1 check therefore applies only to the head split (M13a-M13d), not the whole mesh.
- swap-art rev 2 checks map to rows: 1 twin stills = M7/M11/M13b; 2 shimmer, 3 trousers vs ground, 4 baked folds vs sun, 7 shadow reads as a person, 8 weary look = art-director judgement (QA only confirms stills used the scene's sun, FOV, eye height); 5 bar 1 = M8-M13a; 6 head/hat shadow-only = M12, M13. A failure on check 3 or 4 triggers swap-art's step 2 (base-colour repaint plus normal-map bake): textures only, so it reruns M30 and M33, not the look-down rows.

- Blender edits to the model (head split, headwear, repaint) are approved by Noah, only in layers-producer's window. Any edited FBX is a new asset: M1-M3 (import, facing, bones) and the manifest rows rerun on the edited file, and the Blender steps must be a rerunnable script under `tools/blender/` (spec: hand-made Blender steps are an automatic FAIL).

## 9. Facts from swap-ta (measured by their FBX parser, nothing run) and what each does to the rows

Source: swap-ta message to qa, 2026-10-22; their part-2 plan is the record. Tag [TA].

| Fact [TA] | Effect on the plan |
|---|---|
| 31,114 tris vs Farmer 5,476 (Body 3,358, Legs 918, Head 816, Feet 384); 4096 base colour, one material, 1,655 UV islands | About 5.7x the triangles (my arithmetic from their two figures), skinned twice (view and shadow). M30 FPS is the likeliest failing bar; M33 now records triangles, material count and texture size. The 5% bar is unchanged; any budget is td's |
| One fused mesh including hair and a large backpack (z -24..-31 cm), no hat | Head split (M13a-d) decides where hair goes (head mesh, hidden in the view). Backpack removed by director ruling (amend3), checked in M13e. Bare-headed: the shadow hat checks (M13 renderer table) become 'no hat, full head and hair in the shadow' |
| 180 cm tall. CORRECTION from swap-ta: the earlier x0.944 is withdrawn; the Farmer's 1.650 is the stand-clip eye height, not body height. Survivor standing eye about 165-166 cm (zoomed render, within about 1.5 cm, not a mesh measurement), so fit scale is about 1.00-1.04 (estimate). Eye point about 14 cm further forward of the Head bone than the Farmer's convention (Head + 0.1094 up + 0.1011 forward [repo]) | New eye-match scale and eye point (M19): measure, do not assume. The 1.06 scale limit stands as written; the estimate sits under it, and the x0.944 question to td is withdrawn. The 14 cm forward eye changes the camera-to-neck geometry, so the look-down offset tables (M8) and the head-hide/clipping sweeps (M12, M14) are re-measured from scratch, not adjusted. Lowered-stance clip targets are recomputed |
| 22 humanoid bones, no fingers, no eyes | Bar-1 detector (dominant bone Neck/Head/shoulder top) needs the new bone names (M9). #35 fingers (Q6): "no fingers" does not clear the item; the Survivor has no hand pose to grade, which is a loss, not a fix. Record it as carried forward. Hand-ray and hand-frustum tests (M16) still apply to the arm and wrist geometry |
| Weights up to 10 per vertex in the rigged file, 4 in Merged | Unity skins at most 4 bones per vertex by default (QualitySettings setting, project value unread by me). Weights beyond the cap change the pose near the neck and shoulders. M13c compares weights across the cut on the file that actually ships, and the bar-1 sweep runs on the 4-weight result, not the 10-weight file |
| A-pose with bent knees at rest | Stand eye and feet planted (M19: feet within +-0.01 m) measured on the retargeted Idle, not the rest pose; bent-knee rest can change Stand height by clip |
| 20 clips at 60 fps; walk/run/back-walk/idle/swim-idle loop in place; Swim_Forward and 3 death/fall clips have root motion; four lying clips 1.3 m below the floor | Meshy clips are reference only (Noah). If an optional Meshy clip is taken in: root motion must be stripped (M22 test that clips key only bones, no root translation) and lying clips need the floor offset checked by the sleep eye (M19/M20 Sleep 0.20 m) |
| No jump, crouch, squat, sit clips | Consistent with the Unity-side plan; M22/M23 cover the retargeted or authored set |
| Walking stance-foot speed about 1.4 m/s, Running 4.3-5.1 m/s | Confirms foot-slide needs play-rate scaling (M23, rate <= 1.4 [I35 Q2]); reference only, since the locomotion source is the Unity side |

New row:

| ID | Bar | Test or evidence | Exists / New |
|---|---|---|---|
| M13e | Director ruling `gate/meshy-survivor-eval-amend3-backpack-hat`: the backpack is cut off as its own unused mesh and is on neither body; the Survivor is bare-headed, no hat; the shadow keeps the full head and hair, the view body shows no hair. Checks: (1) no backpack triangles or renderer on the view body or the shadow body at runtime (renderer table printed, backpack mesh absent from the assembled prefab and the build); (2) the backpack cut is closed: no hole, open edge or missing back faces visible in the shadow render or in a third-person debug-cam still from behind; (3) hair in the shadow only (view renderer list has no hair); (4) the shadow silhouette has no hat and keeps the head and hair outline, compared with the Farmer's shadow; (5) pitch sweep -85..+85 clips nothing at any stance on the back of the body | Renderer table; boundary-edge count of the cut at the back; shadow STILL twin-diff 16:9 and 21:9; debug-cam still from behind (dev build only); `PitchSweepTests` | New |

## 10. Reuse labels (Noah via producer: the Survivor is a placeholder; the future is naked base humans plus clothing and gear layers on one shared skeleton; H2 customisation returns that way). No new tests to write now.

R = reusable for future base bodies (run against any body, same bar). S = Survivor-only. L = would need a version for each clothing layer later.

| Rows | Label | Note |
|---|---|---|
| M1-M3 import, facing, bones | R | Bone-map test (M3) is per skeleton; the shared skeleton makes it one test |
| M4, M45 customisation | N/A now | Returns with base body plus layers (future item); the old Legs-swap tests are the starting point |
| M5 view copy, M6 hashes | R | Hashes are per asset set |
| M7 style, D7 | S (the look); R (the method) | The target style guide is open |
| M8, M8a, M9-M11 look-down, waiver re-grant, stills | R; L | Each layer set that changes the torso, neck or shoulders (coat, collar, scarf) needs its own look-down sweep and waiver re-grant; waivers never transfer |
| M12, M13, M13a-d head hide, shadow, head split | R; L | Head split is per body; hats, hoods and hair layers need head-hide and shadow checks (hat renderer table) |
| M13b seam in the shadow | R; L | Seams at every layer boundary (collar, cuff, hem) |
| M13c weights across the cut | R; L | Weights at each layer seam, on the weights that ship (4 per vertex cap) |
| M13e backpack cut, bare head | S | Backpack and hole check are Survivor-only; a gear layer (backpack) needs its own M12-style view hide |
| M14-M15 pitch sweep, range ends | R; L | Layers with bulk (coat, pack) change the clipping |
| M16 hand glimpse, M17 landing 6 | R; L | Sleeves and gloves change the hand shape |
| M18 twin-diff method | R | |
| M19-M21 eye match, heights, transitions | R | Per-body scale; layers should not move the eye |
| M22-M29 clips, foot slide, sync, override maps | R | Clips on a shared skeleton are shared; sync and override maps are per controller |
| M30 FPS | R; L | Must be re-run with the layer set that is on the body (more renderers, more skinned vertices) |
| M31, M32 strip and build | R | |
| M33 texture and triangle budget | R; L | Budget per layer set |
| M34-M37 stances, tower, water | R | Body-independent (capsule); run to prove it |
| M38-M42 layer rule (#43) | R; L | Every layer mesh must also be on the Player layer at runtime; add layers to the hierarchy walk |
| M43-M44 carried-forward #35/#36 | R | |
| M46 body-independence comparison | R | |
| M47 feel check | R | New body or layer set that changes silhouette and weight: new rating |
| M48-M50 sweeps | R | |

## 11. Placeholder status and layered bodies (director amend4, via producer; swap-art rev5)

Source: studio:decisions `gate/meshy-survivor-eval-amend4-placeholder-base-bodies` (relayed by producer; I have not read the record text itself) and studio:art `style/meshy-survivor-bridge-rev5-placeholder` (relayed by swap-art).

Applicability:
- Survivor: the must-keeps and the M8a waiver re-grants apply only where the Survivor reaches a build Noah plays. A Survivor in a scratch scene or an art-only still set is not graded against section 2 (QA still runs the rows that decide the head split, M12-M13e, early, since they decide whether the design stands).
- Future layered bodies: graded on every shipped base + clothing + gear combination, not on the base alone. Per-layer variants are the L rows in section 10, with these specifics: bar 1 includes clothing poking through the body and clothing poking into the camera (M8-M12); the view hides the head and every head-worn layer (hat, hood, hair layer) (M12); the shadow casts the full outfit and gear (M13); FPS at the heaviest shipped outfit counts every layer's renderers and both Animators (M30); eye match per base body (M19); #36 becomes a second base body (M44, W1-W5 of I36 become that body's re-proof).
- Row tags: S = M7 (look) and M13e (backpack cut, bare head); all other rows R, with the L variants named in section 10. Nothing else is Survivor-only.

Art rev5 (placeholder): the pack-cut hole, plaid shimmer, trousers blending into the ground, bar 1, and head/hair casting shadows only still trigger a C-fix. Painted-in shading vs the sun, pink skin, and colour bleed at distance are logged "deferred placeholder polish", not a FAIL unless colour bleed shows in first person. QA reading: only the pack-cut hole and bar 1 are QA-gradable (M13e, M8-M13a); the rest stay art-director judgement and are not in QA's pass/fail. The hat check is dropped (bare-headed).

### 11a. Corrections after reading amend4 itself (read via CLI, field `content`, 2026-10-22; replaces my relay-based wording where it differs)

Cited: `gate/meshy-survivor-eval-amend4-placeholder-base-bodies` (director, Noah's direction via team-lead). The relay matched on every point except these, which are now the rows of record:
- Bar 1 for future bodies also covers clothing layers poking through the body OR EACH OTHER, and back faces seen in look-down (M8-M12 variant, L rows).
- The view hides the head AND every head-worn layer: hair, hats, hoods and collars that reach the camera (M12, M13a variants). Collars are in; my relay wording listed only hat, hood, hair.
- FPS counts every layer's SkinnedMeshRenderers AND draw calls at the heaviest shipped outfit (M30: record SMR count, draw calls and both Animators; the draw-call figure is new).
- The backpack is the first gear layer, not waste: M13e is Survivor-only for the hole check, but the gear-layer variant (hide in view, cast in shadow, closed cut) becomes the template for later gear.
- Bare-headed is the default; a hat returns as a headwear layer (hat renderer table comes back for that layer).
- The drop of H2 customisation is temporary (M4, M45 N/A for now only).
- Cost rule: "keep the Survivor's own cost low; no asset-specific polish unless a must-keep fails". QA reading: run the must-keep rows (bar 1, head hide, shadow, FPS, eye match, #35/#36) first; the style and art-polish rows stay art-director's. Reusable steps the amend asks to invest in: a rerunnable Blender script (re-skin, scale, head/gear split), import settings, a material standard, and the re-proof tests. QA's addition: the script's output hash is recorded so a re-run that changes the body fails M6-style.
- Waiver re-grants (M8a): needed only where the Survivor reaches a build Noah plays.
