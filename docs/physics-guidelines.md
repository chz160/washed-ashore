# Physics guidelines

How Washed Ashore decides **when to use physics and when not to**. Every new system follows these rules. Washed Ashore is a server-authoritative multiplayer open-world game that is planned but not built yet (#13). It uses Unity 6000.6 with built-in PhysX, is based on GameObjects, and will use Netcode for GameObjects (NGO).

Written 2026-10-20 from industry and Unity research plus an audit of the codebase. Sources are listed at the end.

## The core idea

The physics engine is mainly a **collision query service**: raycasts, sphere casts, capsule casts, overlaps, and the CharacterController's sweeps. **Dynamic rigidbodies are the exception.** Use one only when something has to tumble, settle or react physically, and even then simulate it **on the server only**, let it go to sleep, and freeze it once it has settled.

## Pick the motion model

| If the thing… | Use | Examples |
|---|---|---|
| is controlled by a player, or must be predicted | **Kinematic controller** (CharacterController, sweep and slide, manual gravity) | Player, later vehicles, if they're arcade-style |
| follows a known path or a rule | **Scripted or analytic motion**, with transform writes or pure functions and no colliders unless something has to stand on it | Doors, hatches, gates, birds, fish, waves, bullets (hitscan or stepped ballistics) |
| moves over the ground under AI control | **NavMesh, steering or kinematic motion**, snapped to the terrain heightmap, simulated on the server, interpolated on clients | Land animals, later NPCs |
| has to tumble and settle, and **everyone must see the same result** | **Dynamic Rigidbody, on the server only.** Clients are kinematic and interpolated, and the body is frozen and made dormant once it sleeps | Dropped loot, crates, thrown items, grenades |
| is only there to look good | **Client-only physics, never networked** (each client may get a different result) | Death ragdolls, debris, shell casings, cloth |
| sways or ripples | **A shader or wind function, not physics** | Foliage, grass, water surface |
| has to be detected or sensed | **A query with an explicit layer mask**, not a trigger collider, for one-off checks | Interaction, line of sight, headroom, ground checks, AI sensing, hit validation |

If a system needs more than one model (for example, an animal that ragdolls on death), the **default model runs normally and physics switches on only at that event**.

## Rules

### Player
1. **The player is a kinematic CharacterController, never a dynamic Rigidbody.** Input-driven moves can be replayed cheaply for prediction and reconciliation. Every misprediction on a rigidbody player means re-running the whole physics step.
2. **For multiplayer, player movement must run on a fixed tick from an input command** (not `Update` with a variable `deltaTime`), so the server and the owning client can simulate the same steps. NGO has **no built-in prediction for CharacterController**. We write prediction and reconciliation ourselves (#13).
3. **The player body has no colliders.** Combat hitboxes, when they arrive, are **kinematic primitive colliders on bones** on a `Hitbox` layer. They're hit only by server-side, lag-compensated queries.
4. **CharacterController doesn't push Rigidbodies.** If the player needs to shove something, apply the force in `OnControllerColliderHit`, on the server.
5. **A single owner changes the controller's shape.** Stances, tower zones and climbing all resize the capsule today. Add new resize requests through the existing hooks; don't add another script that writes to it.

### AI creatures and ambient life
6. **Animals, birds and fish don't use dynamic physics.** Use NavMesh, steering, splines, boids or sampled math, plus the occasional query.
7. **Ambient life has no colliders unless gameplay needs it to block something or be hit.** When gameplay arrives (hunting, fishing), add **kinematic colliders on a dedicated layer** to the individuals that need them. Don't give the whole population colliders.
8. **Ragdolls start out kinematic and switch on at death.** To switch: turn off the Animator and the controller, then set `isKinematic = false`. The ragdoll is cosmetic and runs on each client. The server keeps the corpse's authoritative position, for example for looting.

### Props and world objects
9. **Doors, hatches, gates and sashes are a replicated state** (an enum, or an angle and elapsed time) with scripted motion. Their moving colliders need a **kinematic Rigidbody moved with `MovePosition`/`MoveRotation`**, or they're switched off while they move (the `TrapDoor` pattern).
10. **Never move a static collider (a collider with no Rigidbody) at runtime.** Every move forces PhysX to rebuild its spatial structure. Configure static colliders at build time only.
11. **Floating objects use the shared water function** (`WaterMotion`, sampled the same way on the server and clients). Make them a Rigidbody only when something must collide with them.
12. **Dynamic props are simulated on the server only.** Use `NetworkRigidbody` with `NetworkTransform`; NGO makes clients kinematic. **Collision events fire only on the server**, so send the gameplay results to clients by RPC.
13. **Settle the body, then freeze it.** Once a prop sleeps, make it kinematic or static and network-dormant. Keep a budget of awake bodies for each server and each world cell, and despawn or merge stale loot.

### Projectiles
14. **Fast bullets are hitscan or stepped analytic ballistics:** gravity and drag each tick, and a raycast along each segment. **Never rigidbody bullets,** which pass through thin geometry and cost solver time.
15. **The server checks every hit** with line of sight, distance, fire rate and lag compensation (rewinding hitboxes by the shooter's latency). Slow thrown objects, such as grenades, may be server rigidbodies.

### Queries
16. **Every query passes an explicit layer mask, a finite distance, and `QueryTriggerInteraction.Ignore`** (or a deliberate `Collide`). Use the named masks in `WorldLayers`. Never use `~0` or masks of nearly every layer.
17. **Use the NonAlloc query variants with preallocated buffers** (`RaycastNonAlloc`, `OverlapSphereNonAlloc` and so on). Size each buffer for the worst case; hits beyond it are silently dropped. **Don't use `RaycastAll`, `SphereCastAll`, or `FindObjectsByType` in per-frame code.**
18. **Batch large sets of queries** (server line of sight, AI sensing) with `RaycastCommand`/`SpherecastCommand`.
19. **`Physics.autoSyncTransforms` stays off.** If you move a transform and query it in the same frame, call `Physics.SyncTransforms()` first.

### Colliders and layers
20. **Collider cost, cheapest first:** sphere, capsule, box, several primitives combined, convex mesh (up to 255 triangles), non-convex mesh. Non-convex meshes go on static geometry only, and Rigidbodies that aren't kinematic need convex meshes.
21. **Bake MeshColliders ahead of time.** Keep Prebake Collision Meshes on, or call `Physics.BakeMesh` at load or on a worker thread. Never cook a mesh on the main thread during play.
22. **Every category of object gets its own layer, and the Layer Collision Matrix turns off pairs that never interact.** Review the matrix whenever a layer is added.
23. **Watch the collider count.** Rust hit Unity's limit of about 64k colliders through player building and had to merge tree colliders. Use terrain tree colliders and merged or compound colliders for dense static content.

### Simulation, animation and cost
24. **Physics runs on a fixed step:** `FixedUpdate`, 0.02 s (50 Hz). Never feed `Time.deltaTime` into physics. Set the maximum allowed timestep so slow frames can't pile up catch-up steps.
25. **Let bodies sleep.** Don't call `WakeUp()` without a reason. Check the Physics Profiler's active body counts.
26. **Simulate and replicate only near players.** Use interest management by world cell (#12, #13). Cells with no player in them have no awake bodies.
27. **Animators:** set `AnimatePhysics` only where animated bodies push or touch Rigidbodies; otherwise use `Normal`. Off-screen life uses Cull Update Transforms or Cull Completely. **The headless server doesn't animate bodies it doesn't query.**

## Decisions by system (2026-10-20 audit)

| System | How it moves | Physics | Decision |
|---|---|---|---|
| **Player** | `CharacterController.Move` with manual gravity | Kinematic controller, no Rigidbody | ✅ Right model. Before #13, move it from `Update`/`deltaTime` to a fixed input tick (rule 2) |
| **Player character body** (stances, first-person body) | Animated under the Player. Stances resize the controller after a headroom check | **No colliders, no Rigidbodies, no ragdoll** on the body. Stance checks use NonAlloc overlaps with explicit masks and `Ignore` | ✅ Right for now. Hitboxes (rule 3) and a death ragdoll (rule 8) come with combat or death. Animators are `AlwaysAnimate`; revisit for remote players and the server |
| **Wade and swim** | Analytic: depth = water surface minus terrain height | No water colliders or triggers | ✅ Correct (rule 11) |
| **Fish** | Simulated virtually, not as GameObjects, with analytic steering; drawn with instancing and baked vertex animation | **Deliberately none.** Rule F10 ("fish never collide"), enforced in `FishSceneSetup` and `FishLiveSetTests` | ✅ Correct (rules 6 and 7). Fish mustn't bump the swimmer or block queries. **For fishing:** the bobber and line use queries plus the water function, and only a **hooked** fish becomes a real object, simulated on the server and kinematic (or a scripted fight). The school never gets colliders |
| **Land animals** | NavMesh agent moved by script, snapped to the heightmap | No colliders or Rigidbodies | ✅ Correct (rule 6). Kinematic colliders on a layer when hunting arrives |
| **Birds** | Scripted flocks and robin flight paths | No colliders. Robins use sphere and capsule checks when flushed or landing | ✅ Model correct. ⚠️ `RobinFlightPlanner` uses `SphereCastAll`, which allocates (rule 17) |
| **Lookout tower** | Invisible static ramp colliders; `TowerWalkZones` tunes the controller | Static box and convex mesh colliders | ✅ Correct. Ramp speeds are final (#8) |
| **Hatch, sash, firefinder** (`Hinge`, not in `World.unity` yet) | Scripted hinge state | `TrapDoor` turns its collider off while swinging. `WindowSash` rotates a collider by transform | ⚠️ The sash must follow rule 9 before it ships |
| **Boundary gate** | Gate leaves rotated by transform | Static `BoxCollider`s rotated when configured | ⚠️ Fine while it only changes at configuration time. It needs rule 9 if the gate opens during play |
| **Interaction** (`Interactor`) | Camera raycast | `SightMask`, `Ignore`, no allocations | ✅ Correct (rule 16) |
| **Floating objects** (`WaterFloat`) | Water function, kinematic Rigidbody | Not used in any scene yet | ✅ Ready for server use |
| **World bounds** | `WorldBoundsClamp` snaps players and bodies back | Static boxes on the `WorldBounds` layer | ⚠️ `FindObjectsByType<Rigidbody>()` runs every `LateUpdate` (rule 17). Make bodies register themselves before props exist |
| **Terrain and trees** | Terrain tile colliders, tree colliders on | **The tree LOD prefabs have no colliders, so players walk through trees** | ⚠️ Tracked in issue 21 |

## Known gaps (fix as these areas are touched)

1. **The Layer Collision Matrix is all on, and the layers are too few.** The player is on `Default`. There's no `Player`, `Wildlife`, `Interactable`, `Hitbox`, `Prop` or `Ragdoll` layer. Add the layers, then switch off the unused pairs (rule 22).
2. **Allocations in per-frame or burst code:** `WorldBoundsClamp.ClampBodies` and `RobinFlightPlanner`'s `SphereCastAll` (rule 17).
3. **Colliders moved by transform with no kinematic Rigidbody:** `Hinge` leaves, `WindowSash`, `BoundaryGate` (rules 9 and 10).
4. **The climb fit test** (`PlayerController.Climb.cs`) masks `~excludeLayers`, which includes `WorldBounds` and `Ignore Raycast`. Use a named `WorldLayers` mask.
5. **Single-player assumptions** that need removing before #13: player lookups with `FindGameObjectWithTag("Player")` (`TowerWalkZones`, `TrapDoor`, `WildlifeAgent`, `WorldBoundsClamp`), and player state that `TowerWalkZones` keeps (it caches a single player's controller).
6. **No explicit cooking options** on the tower's convex MeshColliders. The defaults work, but set them explicitly when the builder is next edited (rule 21).

## Checklist for any new system or pod

- [ ] Which row of **Pick the motion model** is it? Write that in the spec.
- [ ] Rigidbodies: why are they needed, who simulates them (server or client), and when do they sleep and freeze?
- [ ] Colliders: which layer, which primitive, and is the collision matrix updated?
- [ ] Queries: a named mask, a finite distance, `QueryTriggerInteraction` set, NonAlloc, no per-frame allocations.
- [ ] Is anything moved by transform that has a collider and no kinematic Rigidbody? It must have one.
- [ ] Multiplayer: what runs on the server, what's interpolated on clients, and what's client-only cosmetic?

## Sources

- Unity CharacterController: https://docs.unity3d.com/6000.0/Documentation/ScriptReference/CharacterController.html and https://docs.unity3d.com/6000.0/Documentation/Manual/class-CharacterController.html
- Unity physics optimisation (collider types, static colliders, sleeping, transform sync, queries, layers, timestep): https://docs.unity3d.com/6000.0/Documentation/Manual/physics-optimization.html
- Unity MeshCollider and cooking: https://docs.unity3d.com/6000.0/Documentation/Manual/class-MeshCollider.html and https://docs.unity3d.com/6000.0/Documentation/Manual/physics-optimization-cpu-mesh-cooking-options.html
- Unity ragdoll stability: https://docs.unity3d.com/6000.0/Documentation/Manual/RagdollStability.html
- NGO physics (NetworkRigidbody, server authority, collisions only on the server): https://docs.unity3d.com/Packages/com.unity.netcode.gameobjects@2.4/manual/advanced-topics/physics.html
- Unreal CharacterMovementComponent (kinematic capsule with prediction): https://dev.epicgames.com/documentation/unreal-engine/API/Runtime/Engine/UCharacterMovementComponent
- Physics-driven animation and hit reactions: https://dev.epicgames.com/documentation/unreal-engine/physics-driven-animation-in-unreal-engine
- Network dormancy: https://dev.epicgames.com/documentation/unreal-engine/actor-network-dormancy-in-unreal-engine
- Gaffer On Games, networked physics, state sync, fixed timestep: https://gafferongames.com/post/introduction_to_networked_physics/ , https://gafferongames.com/post/state_synchronization/ , https://gafferongames.com/post/fix_your_timestep/
- Fish-Net prediction (non-physics option for controllers): https://fish-networking.gitbook.io/docs/guides/features/prediction
- Valve lag compensation: https://developer.valvesoftware.com/wiki/Lag_Compensation
- Rust: server hit validation (https://rust.facepunch.com/news/devblog-114), collider limit and tree collider merging (https://rust.facepunch.com/blog/friday-devblog-16), animal navigation (https://rust.facepunch.com/blog/devblog-173)

**Not documented by the sources (practitioner opinion):** physics-free birds and fish, buoyancy math instead of rigidbodies, shader-only foliage, and any specific body budget. How Rust, DayZ, Tarkov and PUBG move their players internally hasn't been published.
