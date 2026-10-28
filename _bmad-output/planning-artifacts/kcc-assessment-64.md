# KCC assessment for #64 (K3)

Spike #64, branch `controller-spike-64`. Written by engineer-64r, 2026-10-27, for td review.

**Method and limits.** Public documentation only: the Asset Store listing, the author's published docs and
the Unity manual. **Nothing was downloaded and no KCC code was read.** This session had no web access, so every
KCC statement below is from what I know of those public docs and is marked **[verify]** where td or Noah should
confirm it on the Asset Store page before relying on it. The numbers about our own controller come from the
probe logs and td's records (`studio:engineering adr/64-probe-review` part17-31).

## 1. What the spike found (our side)

| Probe | Result |
|---|---|
| K1 (`Probe64_Tower`, main) | Up with a 10/20/30 degree yaw offset: 45 of 48 rows stop (30/32 at 10 and 20 degrees). Down: 1 of 48. Straight-on references: 0 of 16. Repeat 4/4. PASS 39, FAIL 74, INVALID 3. |
| F5tDiag | The #41 ramp-plus-rail crease stop is triggered by `TowerWalkZones.upperStepOffset = 0.05` (`TowerWalkZones.cs:23`). Our ground-snap `Move` is not the cause. With step 0.25 PhysX slides the crease at the ideal speed. |
| TowerStep | Step 0.25 on the ramps removes all 42 rail stalls. POP 0, CLIMB 0, OFF RAMP 0. td: strong candidate, not a clean fix (3 rows at exactly the 0.03 overlap bar, 9 vs 8 end stops). |
| Option A (K2) | PASS / FAIL / INVALID of 116 on the tower probe: K1 39 / 74 / 3; A1 42 / 71 / 3 (crease unchanged, 45 of 48 up rows still stop); A2 52 / 61 / 3; **record run (A2 + 2 landing boxes) 56 / 57 / 3**, 0 of 48 up rows stop. Collider swap attempted: 0 of 16 flight walls are exact boxes, so none were swapped; 2 of 7 landings were swapped. Rounded rail ends not applied (no box wall to round). Fixture (51 rows): K1 44 / 6 / 1; A1 40 / 10 / 1; A2 42 / 8 / 1. A1 and A2 break the F3 doorway rows (kept_path 1.00 down to 0.60 and 0.17). Record run: T1 end-stop rows 31 (K1 8, A2 code-only 36); one repeat (row 116) nondeterministic in end stops, so the probe is marked invalid; F8 hatch-rim rows 100-102 pop up 0.14 m and row 102 climbs. |

**What this means for the choice.** PhysX `CharacterController` already does collide-and-slide. The worst snag
Noah named (#41) comes from one of our own tuning values, not from a limit that needs another controller. That
weakens the case for B on feel grounds. B has to be justified by #46's needs, below.

## 2. #46 needs against KCC

| Need | Our `CharacterController` (CC) | KCC (public docs) |
|---|---|---|
| **Fixed tick we step ourselves** | Works today: call `controller.Move(dt-based delta)` from our own tick. CC has no internal simulation loop. Needs #46's move out of `Update`, which is needed either way. Measured (DtSweep, td part21): the crease stop does not depend on dt from 1/30 to 1/144. At 1/30, F1 up theta 0 kept_path is 0.82 against 1.00 at 1/60 and 1/144, so about 18% of the climb is lost. For #46: avoid a 30 Hz tick, or re-verify P5 at whatever tick is chosen. | The motor is stepped by a `KinematicCharacterSystem` in `FixedUpdate` by default, with an auto-simulation flag. The docs describe turning auto-simulation off and stepping motors yourself with a chosen `dt` **[verify the exact API on the installed version]**. |
| **Replay within 1 mm** | Same input and start state give the same result on one machine and build. CC keeps a little hidden state (last contact flags, grounded flag). The repo already restores a pose with `TestSpawn.Reposition` (move, then `Physics.SyncTransforms`). Position plus velocity plus our own flags is the state to save. Measured: the "repeat" rows of every Tower run (4 of 4) differ by 0.000 mm, same machine and build (td part17, part31). Cross-machine float equality is not guaranteed. | The motor is managed C# on top of `Physics.CapsuleCast` and overlap queries. It exposes a state struct (position, rotation, base velocity, grounding flags) that can be read and applied, built for prediction and rollback **[verify]**. All logic is readable, so a replay failure can be debugged in source. Same cross-machine float limit, since it uses the same physics queries. |
| **Many players** | Each player is a CC; no static state in CC. The single-player assumptions are in our scripts (#46 P4), not the controller. | One motor per character, registered with a shared system singleton. Many characters are supported. Per-character cost is higher than CC (several casts and overlaps in C#). No numbers measured here. |
| **No Rigidbody-driven player** | Yes. No Rigidbody on the player. | The player is not driven by a dynamic Rigidbody, but the motor component requires a `CapsuleCollider` and, as I know it, adds a **kinematic** `Rigidbody` **[verify]**. td ruling (`studio:engineering adr/64-probe-review/part34`, item 2): a kinematic Rigidbody that only the motor's code moves does not trigger the "Rigidbody-driven player" Automatic FAIL. If B is ever chosen, `docs/physics-guidelines.md` rule 1 (:28) and the player row (:72) need an ADR amendment first. It would also change the layer and contact matrix (the CC is the only contact maker today). |

## 3. Other points the issue asks for

**Licence.** Distributed through the Asset Store under the standard Asset Store EULA, free of charge **[verify]**.
Use in a shipped game is allowed. Redistribution of the asset source is not. Our repo is on GitHub, so if it is
public, checking KCC source into `Assets/` is a redistribution problem. Noah decides; it also needs Noah's OK to
download at all (issue rule).

**Maintenance.** As I know it, the last substantial releases are from around 2019-2021 and the author has said
little since **[verify the date and the Unity versions listed]**. We run Unity 6000.6. Risk: we would own the code
afterwards. Mitigated by the source being plain C# we can read and patch. Compatibility with 6000.6 is unproven
until a prototype compiles and runs.

**Ramps and step offset.** KCC has its own settings in place of CC's `slopeLimit`, `stepOffset` and `skinWidth`:
maximum stable slope angle, step handling (off, standard, extra) with a maximum step height and a minimum step
depth, ledge and denivelation handling, and a collision offset **[verify names]**. It also resolves a move into
two walls by projecting along the crease with a limited number of iterations, which is the case #41 shows
for PhysX. But:
- `TowerWalkZones` changes `stepOffset` (0.05 on ramps and decks, 0.25 elsewhere on the tower), `slopeLimit` in
  the flight zone, and the capsule size. Each would need a mapped KCC equivalent. Settings are not one-to-one, so
  the tower would need re-tuning and a new feel check (#8, #41).
- The 0.05 step that triggers #41 on PhysX has a reason (rail-end pop-up, `TowerWalkZones.cs:23`). KCC's step logic
  is different code, so it may or may not pop at rail ends. Unknown without a prototype.

**Moving platforms.** KCC has its own mover component for platforms **[verify]**. Our kinematic movers (#45) would
need to be checked against it, or they stay on the Unity physics path and the motor treats them as colliders.

**Migration size (measured by grep, not estimated from KCC).**
- Basis: `git grep CharacterController` on `main` (HEAD), `*.cs`, so without option A's `PlayerController.Slide.cs` and without the probe. 33 tracked files and 66 lines; 31 files and 64 lines under `Assets/` (the other 2 are `_bmad-output/poc/pipeline-scripts`). Under `Assets/`: 10 runtime files (20 lines), 19 test files (42 lines), 2 editor files.
- Runtime: `PlayerController` (+ Stance, Water, View partials, 298 + 355 + 117 + 89 lines on main), `TowerWalkZones`, `TrapDoor`, `WindowSash`, `WorldBoundsClamp`, `FishF8Spawn`, and
  3 perf-probe scripts.
- Tests and test support: 19 files, among them `TestSpawn`, the Lookout fixture and movers, TowerWalk,
  WorldWalk, water, wildlife and world-bounds kits.
- The move call is one site (`PlayerController.cs:268` on main, plus the ground snap at :272). The cost is not that call.
  It is in the resize paths (stances, tower, swim), the `controller.excludeLayers` use for Water, the overlap
  code that uses `skinWidth`, and every test that moves or places the player.
- Tuned speeds (walk 5, sprint 8, jump 1.2, tower 1.15/1.5) stay as our velocity inputs. What shifts is how contact
  changes the result. #46 P5 (TowerWalk up time 26.6 s, 10% band; the 26.6 s is from `_bmad-output/planning-artifacts/issues/08-tower-stair-traversal.md:10`, #8 S5) would need re-proving.

## 4. Assessment

- **Fixed tick and replay.** Both options can meet them. CC needs no new dependency and the repo already has the
  reposition pattern. KCC's ready state struct is a small plus for rollback (#13), not a need for #46.
- **Wall sliding.** The measured crease problem is our own step value. Option A, or the step change from TowerStep,
  addresses it on CC. KCC's crease handling is a possible advantage, but unproven on our tower.
- **Cost of B.** A third-party licence question, an unverified Unity 6000.6 compatibility, a kinematic Rigidbody
  that touches the physics rules, a re-tuned tower, and a change touching 31 files under `Assets/` with feel re-checks.
- **Recommendation for td (not a ruling).** Keep CC for #46. Treat B as the fallback if the follow-up on step 0.25
  and the hull-end overlaps fails Noah's feel check. A B prototype needs Noah's OK for the download first.

## 5. Open items to settle by checking the Asset Store page (no download)

1. Current version, release date and listed Unity versions.
2. Licence text and whether a public repo can hold the source.
3. The API for manual stepping and for saving and applying motor state.
4. Whether the motor adds a kinematic Rigidbody (td has ruled it is allowed, part34; this only confirms the fact).
