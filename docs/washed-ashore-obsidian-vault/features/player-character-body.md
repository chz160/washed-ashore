---
type: feature
status: in-progress
tech: [character-art, animation, rendering, probe-tests, perf]
area: [player-body, player]
issues: [49, 35, 36, 95, 96]
prs: []
source: "pod49 close-out; td-49 B36-B40"
created: 2026-11-10
updated: 2026-11-11
---
# Player character body

_The player's body is the Meshy Survivor, seen as hands, forearms, legs and boots in first person and as a full shadow. Pod 49 finished it on 2026-11-10 with QA and director sign-offs (each with conditions). It is not committed yet: status stays in-progress until the commit lands and #49 closes._

## Shipped
On branch `player-body-49`, not yet committed or merged (2026-11-10).
- **The body:** `Survivor_Body.fbx` `459fa2f6` and `Survivor_Head.fbx` `5677ec81`, the `Men_Survivor` appearance, on the new `Skeleton_Human` ([[0009-survivor-is-the-player-body]], [[0010-realistic-human-skeleton-low-poly-humans-removed]]).
- **Scale:** `skeletonScale` 1.0266, about 1.85 m, so the eye matches the camera ([[0011-survivor-scaled-to-1-85-m]]).
- **Knee correctives:** two blend shapes (`Knee_L_90`, `Knee_R_90`) driven by `KneeCorrectiveDriver.cs`.
- **Clips:** clips20 (`Authored_Survivor.fbx`) is the clip set of record and look-final; the Quaternius pack is retargeted (`Pack_Retargeted.fbx`).
- **Sockets:** named attach points on the view and shadow bodies (`CharacterSockets`, `CharacterEquipment`). Anything head-worn is shadow only. The backpack itself is #95.
- **Look-down table:** `LookDownOffset` `936e724c` and `World.unity` `1b3971b7`. Bar 2 waivers in [[0012-look-down-bar-2-waivers]].
- **Gates passed:**
  - Look-down gate ×3: 381 cells each, 0 Bar 1 defects. The tightest cell is the left knee in CrouchWalk at +75: 34.2 mm under the 35 mm leg limit. The director's knee exception was never used.
  - Baton 23: StandAndCrouch PASSED (2,988 s), `Bar2_HandWorked` PASSED, 17 WAIVED lines.
  - M30 frame time: R = 1.0169, worst pairing 1.0303, accepted by Noah 2026-11-08 ([[0016-frame-time-noise-rule-a1]]). M31: debug-only code absent from the release build.
  - S6 look gate: hands and forearms show, the watch is on the right wrist, the flannel front shows at Stand +85, no head or hair in first person, the shadow is whole. Eye heights stand 1.65, crouch 1.20, squat 1.00, sit 0.80, sleep 0.20; pitch ±85.
- **Accepted defects:** 39 fold-silhouette gaps (66 px over 385 frames), waiver W-49-5 ([[0015-fold-gaps-accepted-w-49-5]]).

## Open
- **The commit:** only when Noah asks. It includes `KneeCorrectiveDriver.cs` and the test-only `HairlineGapSizing.Hull.cs` and `.KClass.cs` with their `.meta` files; no BackingHull file. Which of ta's Blender tools go in is Noah's call.
- **Item 6, remove the low-poly humans:** after the pod-49 commit, in a separate worktree, as its own change set. Before-stills stay in `_bmad-output/poc/character-stills/`.
- **Record items:**
  - td hasn't recorded his review of `LookDownTests` `2f55d70e` (qa condition C2).
  - Bar 2 option 1 is team-lead's reading of Noah's words and stands unless he objects (C3).
  - No still exists at Stand idle +65; W-49-2 holds on data.
- **Deferred tidy-ups:** the crop file name `Safe()` change (`d94becf8`, held back so W-49-5 cites the classifier of record); B34a's unresolved-class path; stale doc comments in `Bar2GaitStills` and `StandIdleStill`.
- **Motion notes (S6 C2, not blocking):** the walk reads vigorous rather than weary, CrouchWalk reads as a lunge, the crouch hips look like an "invisible chair", and the jolt needs polish. The director's record sends these to #96; clip quality is #35. Open palms in Crouch_Idle and Squat_Hold are a #35/#96 clip note.
- **Fabric reads flat up close:** no normal map, by Noah's call (amend 8 note 1).
- **#36:** the women's base on the same bone set.

## Rulings
[[0009-survivor-is-the-player-body]], [[0010-realistic-human-skeleton-low-poly-humans-removed]], [[0011-survivor-scaled-to-1-85-m]], [[0012-look-down-bar-2-waivers]], [[0013-hairline-gap-rule]], [[0014-s2-gap-classes]], [[0015-fold-gaps-accepted-w-49-5]], [[0016-frame-time-noise-rule-a1]]

## Patterns
[[see-through-gap-checks-on-a-skinned-body]], [[fold-silhouette-gaps-in-layered-cloth]], [[reading-a-non-readable-mesh-in-tests]], [[probe-and-playmode-test-gotchas]]
