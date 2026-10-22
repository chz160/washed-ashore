Remove the physics-related allocations from per-frame and burst code. Rule 17 in [`docs/physics-guidelines.md`](https://github.com/chz160/washed-ashore/blob/main/docs/physics-guidelines.md).

## What we know (audit 2026-10-20)

- **`WorldBoundsClamp.ClampBodies`** (`Assets/Scripts/Gameplay/WorldBounds/WorldBoundsClamp.cs:73-77`) calls `FindObjectsByType<Rigidbody>()` **every `LateUpdate`**. That allocates an array each frame and walks the whole scene. There are 0 bodies today, but the cost grows with props, loot and boats.
- **`RobinFlightPlanner`** (around lines 20–34 and 144) uses `Physics.SphereCastAll`, which allocates per path segment and heading: up to 8 headings × 3 distances × 5 segments for each flush. Line 57 also allocates a `new[] {…}`.
- Every other query in the codebase already uses NonAlloc with preallocated buffers, for example the stance code and `WildlifePlacer`.

## Pass criteria

| # | Criterion | Evidence |
|---|---|---|
| A1 | **Rigidbodies register themselves for the bounds clamp**, through a component or a registry with OnEnable and OnDisable. No `FindObjectsBy*` call in any Update or LateUpdate | Diff; grep |
| A2 | **The robin planner uses `SphereCastNonAlloc`** with a static buffer sized for the worst case, plus a test that a full buffer is detected, not silently dropped | Diff; EditMode test |
| A3 | **0 B GC alloc** from `WorldBoundsClamp` and the robin planner in a Profiler capture while robins flush | Profiler capture |
| A4 | **The bounds clamp still works:** the B5 sweep test passes, and a test Rigidbody spawned north of the line is snapped back | Test XML |
| A5 | **Robin behaviour is unchanged:** the bird tests pass | Test XML |

## Automatic FAIL

- Changing the bounds rule or the robin flight rules.

## Pod

**gameplay-engineer** only: 1 agent, and qa-lead checks the result.
