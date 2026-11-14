---
type: decision
status: proposed
tech: [multiplayer-ready, physics]
area: [player, lookout-tower]
issues: [13]
prs: []
source: "review pod 2026-11-11: task #8 (technical-director verdict and 7 points), #9 (skeptic H1-H10), #10 (qa-lead list F1-F9, D1-D2, U1-U3; code read, nothing run)"
decided_by: "proposed by technical-director (#8), verified by qa-lead (#10); not accepted"
created: 2026-11-11
updated: 2026-11-11
supersedes: ""
superseded_by: ""
---
# 0022 Hinge replication, corrected (would supersede 0019)

_Proposed, not accepted. If accepted, it would supersede [[0019-blocking-world-state-on-sim-tick]]; until then 0019 stays proposed and unchanged, and `supersedes` stays empty. Direction is unchanged (hinges on the sim tick, replicated as state), but 0019's mechanism and scope were wrong in places._

## Context
qa-lead checked 0019 against the code (#10). Reading only; no test was run. Scenarios below are reasoned, not reproduced.

## Decision (technical-director's seven points, #8)
1. **Replicate state plus elapsed ticks, not pose** (F1). The leaf angle never reaches player collision: leaf colliders are off while moving (`TrapDoor.cs:180-183`, `Hinge.cs:56-61`, `Hinge.cs:154-159`; sash `WindowSash.cs:75-76`). Mid-swing the player meets only the static swingBlocker (`TrapDoor.cs:181`). The real risk is which collider set is active on the transition tick; transitions land on a frame boundary (`Hinge.cs:140-142`). This replaces 0019's "wrong hatch pose" claim.
2. **Name a world-tick driver** (F3). The tick is per player (`PlayerController.Tick.cs:18-19`, `tick++` at `:94`); the hatch is shared. qa-lead found no world tick in the files read (not an exhaustive search). Also fix an update order between hinge and player (F6): no `DefaultExecutionOrder` on `TrapDoor`, `WindowSash` or `PlayerController` (grep, Assets/Scripts).
3. **Anti-trap checks on the replication path** (F2, F9). They run only in `CanInteract` (`TrapDoor.cs:142,152-155`; `WindowSash.cs:49,82-85`). `SetState` is Apply plus Settle with no player check (`TrapDoor.cs:159-163`; `WindowSash.cs:89-94`). Settle turns on the blocker (`:181`) and headroom stop (`:187`) around whoever is there. Say what a client does when replicated state conflicts with its predicted player. 0019's "collider rules still run" is true for colliders, false for anti-trap. Existing coverage: `LookoutMovers.cs:340-360`.
4. **SyncTransforms on every rest-pose change** (D1, downgraded). Pose re-enables colliders (`Hinge.cs:155-159`); Settle syncs only if `collisionMoving` (`TrapDoor.cs:189`; `WindowSash.cs:78`); AutoSyncTransforms is 0 (`DynamicsManager.asset:22`). Mechanism verified; the effect (stale same-frame query) is **assumed, needs a test**.
5. **Drop "gates"** (F7). `BoundaryGate` logs an error and refuses to move in play (`BoundaryGate.cs:25-36`); name #45 as a precondition if gates are wanted later.
6. **Add WaterFloat-style kinematic bodies to scope** (F8). `WaterFloat.cs:35` moves a kinematic Rigidbody in `FixedUpdate` on `WaterClock.Now` (`:44`). Latent: no .unity, .prefab or .asset references it (grep). Cross-reference only: `WorldBoundsClamp.cs:109` is a static-bounds clamp, not hatch-class state (D2, downgraded).
7. **Link [[0020-replay-is-side-effect-free]] for rewind** (F4). `PlayerSimState` excludes hinges (`PlayerSimState.cs:8-9`), so replay cannot see the hatch at tick N. The ADR must choose rewind, accept-error, or a pure state-at-tick query.

Scope note (F5, skeptic H4, MEDIUM): Interact is not in the command stream (`Interactor.cs:95-116` on frame time; `PlayerCommand.cs:6` has Sprint and Jump only); Firefinder suspends control (`Firefinder.cs:125`, `controlSuspended` in `PlayerSimState`). Include both in scope.

## Not verified
- U1: whether an open sash is reachable and blocks the player (layer, collision matrix, cab walls). Not a fact; do not rely on it.
- U2: `WaterClock` being frame time (`WaterClock.cs:13`); not read by qa-lead.
- U3: whether 0020 forbids mid-replay `Physics.SyncTransforms` (qa-lead did not read 0020).
- Severity ranks (HIGH on F2-F4) are skeptic's; qa-lead agrees on code evidence only.

## Related
- [[0019-blocking-world-state-on-sim-tick]], [[0018-one-sim-clock-60hz]], [[0020-replay-is-side-effect-free]], [[lookout-tower-traversal-and-interactions]], [[player-controller]]
- Issue: https://github.com/chz160/washed-ashore/issues/13
