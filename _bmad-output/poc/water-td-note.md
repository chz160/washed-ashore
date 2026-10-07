# Water: TD note (boats, arrival-by-wreck spawn, server-side physics)

Author: w-td (technical-director), 2026-10-06. Spec: planning-artifacts/poc-spec-washed-ashore-water.md, hand-back item 3.
Decision of record: studio:decisions water/tech-pick; ADR studio:engineering adr/water-1-custom-urp-water; research: research/technical-water-2026-10-06/research.md.

## 1. What the water is (as landed)

- **Surface.** 20 flat 1x1 quads, one per 1024 m tile, at MapConfig.WaterLevelY. They're on layer 4 with **no collider** and no shadows. The shader is hand-written URP HLSL (Assets/World/BellsBend/Water/BellsBendWater.shader + WaterSurface.hlsl).
- **Motion.** One pure function, WaterMotion.Offset(waves, x, z, t) (Assets/Scripts/World/WaterMotion.cs):
  - Up to 4 vertical sines, with a shipped total amplitude of 0.028 m.
  - The per-wave phase is computed in double in C# and pushed by WaterBody, so the shader never sees t.
  - Time comes through one seam, WaterClock.Now.
- **Look data.** One baked RG8 WaterMaps texture (shore distance + bed depth), behind the W3 guard. The current is visual only: flow phase x the bank-tangent field, clockwise around the bend (OSM / USGS sourced).
- **Player.** Wade and swim are analytic: depth = f - TerrainQuery ground. Nothing collides with the water.
- **Evidence:**
  - W3: _bmad-output/poc/water-w1w3-editmode.xml (all W3 cases pass).
  - W9: _bmad-output/poc/water-w6w8w9/editmode-w9-record.xml (94/94) and pm-w9-record-run1/run2.xml (5/5 twice).
  - GPU vs C#: 0.00-0.02 mm at the four world corners and at t + 3 days.
  - Crate: within 0.3 mm of the surface from its spawn frame.
  - W10 web size: water ships about 0.5 MB of the 57.1 MiB Web.data (0.8%; WaterMaps about 0.2 MB, the ripple normal map about 0.3 MB), measured by block-level attribution of the Brotli-q11-over-LZ4HC data (TestResults/water-w4/builds/web-size-compressed.txt; complete build size 64.1 -> 64.5 MB agrees). The growth since the web baseline (33.8 -> 57.1 MiB) is Bells Bend land: terrain tiles about 23 MB shipped, plus the kit textures, not water.
  - W10 FPS, Windows shore walk (solo, rebuilt player, RTX 4060 Ti, 1080p windowed): whole route median 5.49 ms (182 FPS), p99 9.01 ms, 100% of frames at 60 or more; shore leg median 5.51 ms, p99 7.81 ms (TestResults/water-w4/w10-windows/shore/bells-bend-fps-shore.json). The earlier 29.6 FPS run is void (two concurrent players). R2 like-for-like budget check: median 6.91 ms vs 6.68 pre-water = **+0.23 ms** against the +1.5 ms budget, p99 9.83 ms (under 16.6), max 17.42 ms, 99.99% of frames at 60 or more, wildlife and birds on (TestResults/water-w4/w10-windows/r2/, copied from the player's bells-bend-fps.json of 22:04). **W10 Windows budget: PASS.**
  - W10 web FPS (30 or more): **PASS on the run of record, with a recorded gap.**
    - Run: TestResults/water-w4/w10-web/w10-web-fps.json. Chrome 154, ANGLE D3D11, RTX 4060 Ti, canvas 1904x1015, local serve, Web.data hash matches the record.
    - Result: median 16.7 ms (59.9 FPS, at the 60 Hz vsync cap), mean 43.1 FPS, p95 33.4 ms, p99 33.6 ms, max 100.1 ms, 79.75% of frames at 33.33 ms or less. 1,294 samples over 30 s.
    - **Gap: the water wasn't in view.** The walk was 30 s through the park from spawn, so the web run didn't exercise the water's own cost.
    - Accepted for this pod. The spec's shore requirement is for Windows only; the web bar is 30 FPS or more. On Windows the same shader cost +0.23 ms (R2) with the shore leg at 5.51 ms. On web it adds no extra passes (no depth or opaque copy), and the ripple and foam work fades out past 150-160 m, so far water is cheap.
    - Watch item: p95/p99 at about 33 ms (one frame in five drops to the 30 FPS vsync step) shows web is already near its edge **without** water in view. That's CPU/wasm, not water.
    - Follow-up (non-blocking): a 60 s web walk along the shore leg, water filling the view, the next time Noah is at the PC. Bar: median 30 FPS or more.

## 2. Boats

**Ready now.** Any body can float on the exact visual surface by sampling f at hull points. f is analytic, so a boat needs no GPU readback and no water collider. Use multi-point buoyancy: 4-8 probes per hull, each sampling WaterBody/WaterMotion at WaterClock.Now. That's the WaterFloat pattern generalised. It costs a few sin() calls per probe per physics step.

**What a boat needs before it ships:**
1. **A C# current field.** The current is visual only today (ruling water-swim-numbers-r2).
   - A drifting boat or debris needs CurrentAt(x, z): the same bank-tangent rule the shader uses (from the LevelMaps shore-distance gradient, with the north-of-line channel rule and the open-water fade), ported to C# and read from the same data.
   - Rule: **one flow definition, two consumers**, as with f. Visual drift and physical drift must match within a tolerance test, as W9 does for height.
2. **A barrier re-sweep (W8) with boats.** A boat is the swim-around threat at 3-5 times the speed.
   - WorldBoundsClamp already snaps Rigidbodies (crate snap tests pass both ends).
   - Boats must be Rigidbodies moved **through the body** (MovePosition / forces), never by transform alone. A transform-only move is re-floated and clamped only at the next physics step (w-engineer's recorded limit).
   - Rerun the W8 sweep with a boat at each barrier end.
3. **Water inside the hull.** The surface is an opaque-ish transparent quad, so it will draw inside an open hull below the waterline. Fix it with a stencil "dry volume" pass on the hull, around 0.05 ms. Not with a depth texture: web has none, by design (ADR water-1).
4. **Visible swell.** A 3 cm, 1.5-6 m ripple barely moves a boat. If a boat needs to visibly heave, add a gridded water mesh near the camera with displacement wavelengths of 32 m or more (8 m grid, interpolation error about 0.31 A). Keep f the single source and extend the W9 readback to the displaced vertices. Don't add FFT or a dynamic simulation; they aren't network-deterministic (research D3).
5. **Wakes and splashes** are visual only (decals or particles), never part of f.

## 3. Arrival-by-wreck spawn

The brainstorm idea: the player leaves a wrecked boat and lands on shore. What the water choice implies:

- **Ground the wreck; don't float it.** A wreck resting on the shelf is a static prop: no buoyancy, no network state, and it can't drift past the line.
  - If a floating wreck is ever wanted, it's a boat (section 2) and carries all of section 2's requirements.
- **Placement rules** (derive everything; no literals):
  - Choose from the existing low-bank stations (Data/terrain/build/bank_stations.json, 732 usable) south of the line.
  - Put the wreck where bed depth is under swimExitDepth (1.15 m) and the player's start point is under the wade threshold, so arrival starts wading, not swimming. That gives a guaranteed exit up the 18 deg shelf and the 0.31 m lip, which W6 already proves at random stations.
  - Heights come from MapConfig.WaterLevelY + WaterMotion, and depths from TerrainQuery.
- **The spawn-frame lesson from W9.** Anything placed relative to the water must be valid on its first frame (WaterFloat now floats in OnEnable). A spawn that waits for a physics step shows a wrong frame, or reports wrong state to a server.
- **Camera.** The camera never goes underwater (ruling r2). The arrival camera has to keep the near plane out of a +/-0.08 m band around f. The only underside protection is the flat murk back face.
- **Shared service.** Drowning's later "wash ashore" respawn uses the same station set and rules (research D6 §4b). Build one SpawnStations service used by both. The arrival spawn is then also the drowning respawn, and a server can validate both deterministically.
- **Seeded.** If the arrival station is random, pick it from a logged seed. QA can then reproduce it, and a server can agree on it.

## 4. Server-side physics

**Ready:**
- **Surface height.** f is pure, and the per-wave phase is computed in double.
  - The contract between machines is "same function within 1 mm", not bit-identical: Math.Sin can differ in the last ulp across OS and CPU.
  - On the server, call WaterClock.Use(() => serverTime). Clients use server time plus an offset (Crest's TimeProviderNetworked pattern, research [35]).
- **Swim and wade state.** It's analytic from f and TerrainQuery, with no triggers or colliders, so the server can recompute any player's water state from position and time.
- **The boundary.** WorldBoundsClamp is authoritative for players and Rigidbodies, and drowning's pull P (later) depends only on position and time.

**Gaps to close before the server is authoritative over water:**
1. **TerrainQuery.** It must return an explicit "not loaded" instead of the nearest-edge fallback (bells-bend-td-note §2, follow-up 4). Under streaming, a wrong ground height means a wrong depth, so the wrong swim/wade state, which also opens a swim-around path. This was a recommendation; it's now a **blocker** for server water authority.
2. **WaterClock.**
   - Its default is timeSinceLevelLoad, which resets on every level load. Sessions must use network time from the start.
   - Add a [RuntimeInitializeOnLoadMethod(SubsystemRegistration)] reset, so the static source can't carry over between Play-mode runs with domain reload off.
3. **Unity types in WaterMotion.** It uses Mathf and Vector2. That's fine for a Unity headless server. A non-Unity .NET server would need a roughly 60-line port to System.Math, checked against the same EditMode tolerance tests.
4. **Flow.** There's no physical current yet (section 2.1). Until there is, the server treats water as still.
5. **Render-only data stays client-side.** WaterMaps, the normal maps and the shader aren't needed on a server. Drowning's P will read LevelMaps shore distance (1 m quantised), which is good enough for 40/70 m bands.

## 5. Follow-ups (ranked)

1. **TerrainQuery "not loaded" result** (gameplay-engineer). Now required for server water authority and for streaming.
2. **C# current field + flow parity test** (gameplay-engineer + technical-artist). Before any boat or drifting debris.
3. **WaterClock SubsystemRegistration reset and a network-time provider** (gameplay-engineer). Small; do it with the first netcode work.
4. **SpawnStations service** shared by the arrival wreck and the wash-ashore respawn (level-designer + gameplay-engineer).
5. **Web size levers are the land tiles and kit textures, not the water** (platform-tools-engineer + technical-artist). Measured shipped shares: terrain about 23 MB (land tiles 0.6-1.8 MB each); other textures about 21.6 MB, where 1K normal maps barely compress (about 1.2 MB each). The all-water tiles compress to about 3 KB each, so the bells-bend-td-note §1 alphamap override on them is a memory and disk win only, not a download win.
6. **Web shore-view FPS check** (qa-lead, with Noah at the PC): close the W10 web gap above. If the median drops below 30 FPS, cut the second ripple layer, then drift lines, then render scale.
7. **BuildAll swallows non-water failures with exit 0** (build-engineer). Pre-existing; found in the W3 review. The water path now throws; the rest of the level build doesn't.

Not changed by this pod: the web target stays WebGL2 (adr/web-target-webgl2). Nothing in the water needs compute, so WebGPU stays optional. Revisit the reflection choice if URP ships SSR (Unity 6.7).
