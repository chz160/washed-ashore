# Bush slowdown (issue #21, task #13)

Status: draft for ACK by `gameplay-engineer` and `qa-lead`. Owner: `systems-designer`.

Every multiplier below is **assumed (not playtested)**. Nobody has walked through a bush at these speeds yet. The numbers come from the reasoning given in each row and are tuning data, not fixed rules.

## 1. Player problem and loop

- **Problem:** in Bells Bend you can sprint through a 3 m kudzu mound as fast as across a mown field. Cover costs nothing, so the land reads as flat.
- **Loop it serves:** moving across the map (scout, gather, reach shelter). Thickets become a trade: the short line through the scrub is slower, and the open line around it is faster but exposed. This serves "scarcity and risk" (time and exposure are costs) and "grounded world" (brush slows you, as it does in life).
- **Not in scope:** wildlife hearing you in brush, stamina, clothing snag or damage, and any HUD.

## 2. Motion model

- The player stays the kinematic `CharacterController` (`docs/physics-guidelines.md`, Player rules). No new colliders, Rigidbodies or physics queries.
- The slowdown is a **data lookup** of the terrain detail layers under the feet, run inside the player's fixed sim tick (`PlayerTickClock.TickDt` = 1/60 s, `Assets/Scripts/Gameplay/PlayerTickClock.cs:10`). It only scales the move target speed. Detail layers can't carry colliders, so bushes can't block or snag the player (B3).
- The lookup reads baked terrain data and the player's position only, so it replays the same on any machine (one-sim rule, decision 0017/0018 drafts).

## 3. Inputs from the code (facts)

| Fact | Source |
|---|---|
| Walk 5, sprint 8 m/s | `Assets/Scripts/Gameplay/PlayerController.cs:23-24` |
| Acceleration 20, deceleration 25 m/s² | `PlayerController.cs:25-26` |
| Move target = input × (sprint ? 8 : 5), then clamped by `WalkSpeedCap`, then by `StanceSpeedLimit` | `PlayerController.cs:209-211`, `Water/PlayerController.Water.cs:72-75` |
| Crouch speed 1.75 (proven range 1.25–2.50) | `Stance/StanceTuning.cs:22,69`; `_bmad-output/design/player-stances.md` §3a |
| Tower caps: stairs 1.15, landing 1.5 (through `WalkSpeedCap`) | `Assets/Scripts/Lookout/TowerWalkZones.cs:27,29,93` |
| Detail cell = 2 m | `Assets/Editor/Art/BellsBendGround.cs:17` |
| Shrub layers are painted **0 or 1** per cell (Briar, ShrubThicket, KudzuMound); KudzuLeaf and the grasses get counts of 0–3 | `Assets/Editor/Art/BellsBendVegetation.cs:140-173` |
| Per-instance height is not stored. Unity draws each instance at a random size inside the layer's min/max | `BellsBendVegetation.cs:115-121` (prototype min/max only) |

The layer definitions (`DetailSpecs`, `BellsBendVegetation.cs:28-37`) are **not changed** by this spec. #15 and #16 use them.

## 4. Per-layer table

`M_walk` and `M_sprint` are the multipliers at **full cover** (cover `c = 1`, section 5). The resulting speeds are at full cover.

| Layer (index) | Height (m) | Shrub | M_walk | M_sprint | Walk → m/s | Sprint → m/s | Reason |
|---|---|---|---|---|---|---|---|
| Broomsedge (0) | 0.7–1.1 | no | 1.00 | 1.00 | 5.00 | 8.00 | Bunchgrass you wade through. It covers whole field gaps, so any slowdown would tax most of the open map. |
| Briar (1) | 0.9–1.6 | yes | 0.70 | 0.50 | 3.50 | 4.00 | Waist-to-chest blackberry and sumac. Thorns let you push through at a careful walk; you can't run through them. |
| ShrubThicket (2) | 1.5–2.6 | yes | 0.55 | 0.40 | 2.75 | 3.20 | Over-the-head honeysuckle and privet. You part branches with your arms. |
| KudzuMound (3) | 1.6–3.2 | yes | 0.50 | 0.35 | 2.50 | 2.80 | Vine-smothered scrub, the tallest and most tangled cover. The strongest slowdown. |
| KudzuLeaf (4) | 0.5–0.9 | yes | 0.90 | 0.80 | 4.50 | 6.40 | Knee-high vine mat on road verges. It trips your stride more than it blocks you. |
| Fern (5) | 0.5–0.9 | no | 1.00 | 1.00 | 5.00 | 8.00 | Soft forest-floor fern. Slowing it would make every Ridge and Hollow slow. |
| CrackGrass (6) | 0.25–0.5 | no | 1.00 | 1.00 | 5.00 | 8.00 | Grass in road cracks. Roads must stay the fast way through. |
| BankGrass (7) | 0.6–1.1 | no | 1.00 | 1.00 | 5.00 | 8.00 | Riverbank grass. Grass doesn't slow, same as Broomsedge. |

Rules the table keeps, so tuning can't break them:
- **Only layers with `shrub = true` slow.** Grass and fern don't. That is the same flag #15 and #16 read, so "cover" means one thing in the game.
- **Sprint is cut harder than walk** (`M_sprint < M_walk`), but **sprint is never slower than walk** in the same cover: `8 × M_sprint ≥ 5 × M_walk` for every layer. Holding sprint in a thicket still helps a little; it never hurts.
- **Taller layers slow more.** Ordered by mid-height (KudzuLeaf 0.70, Briar 1.25, ShrubThicket 2.05, KudzuMound 2.40 m), `M_walk` and `M_sprint` both fall strictly. This is how bush **height** scales the effect (see 5.3).
- No full-cover walk speed is under 2.5 m/s, so crouch (1.75) is below every bush speed (section 6).

## 5. Scaling rule: density and height

### 5.1 Cover per layer

For each slowing layer `L`, cover `c_L` is in [0, 1]:

1. Find the terrain tile under the player (`TerrainQuery.TileAt`).
2. Turn the feet position into detail-grid space: `u = (p.x − tile.x) / size.x × res − 0.5`, `v = (p.z − tile.z) / size.z × res − 0.5`. Cell centres sit on whole numbers.
3. Read the four cells around `(u, v)`: `(x0, y0)`, `(x0+1, y0)`, `(x0, y0+1)`, `(x0+1, y0+1)` with `x0 = floor(u)`, `y0 = floor(v)`. Clamp indices to the grid.
4. Each cell's fill is `min(count, N_L) / N_L`. `N_L` = 1 for Briar, ShrubThicket and KudzuMound (they're painted 0 or 1), and 2 for KudzuLeaf (its counts run 0–3; 2 or more counts as full mat).
5. `c_L` = bilinear blend of the four fills with weights from `fx = u − x0`, `fy = v − y0`.

Why bilinear: a 2 m cell is wider than the player. Without blending, speed would step at cell edges. With it, cover fades in over about 2 m as you walk in, and a lone bush slows you less than a block of them. That is the **density** scaling.

**Height gate:** a layer counts only while the feet are less than that layer's max height (`DetailSpecs.maxH`) above the terrain surface (`TerrainQuery.Height`). On a tower deck or a roof you're above the bushes; in a jump you're still in them (jump apex 1.2 m is below Briar's 1.6 m max).

### 5.2 Multiplier

Per layer: `m_L = 1 − (1 − M_L) × c_L`, using `M_walk` or `M_sprint` from the table.

Overall: **`m = min over slowing layers of m_L`.** Overlapping layers don't multiply together; the worst cover under you wins. So the speed can never fall below the table's full-cover value.

`m` is exactly `1.0f` when every `c_L` is 0, because `1 − x × 0 = 1` in floats. Don't add an epsilon or smoothing that could leave 0.9999.

Which multiplier: use `M_sprint` when the sprint button is held and the stance is Stand; otherwise `M_walk`.

### 5.3 Height

Per-instance height can't be read at runtime (section 3). Height scales the effect **between layers**: the table falls strictly with layer mid-height (section 4). Inside one layer, every instance counts the same.

### 5.4 Worked examples

**A. Light: sprinting past the edge of one briar.** One Briar cell; the player is 1.5 m from its centre along x and level with it in z, so `fx = 0.75` away from it, `fy = 0`. Fill of the briar cell = 1, the others 0.
- `c_Briar = (1 − 0.75) × 1 = 0.25`
- Sprint: `m = 1 − (1 − 0.50) × 0.25 = 0.875` → 8 × 0.875 = **7.00 m/s**
- Walk: `m = 1 − (1 − 0.70) × 0.25 = 0.925` → 5 × 0.925 = **4.625 m/s**

**B. Dense: walking along a kudzu road verge.** KudzuMound cells around the player give `c_KudzuMound = 0.6`. The KudzuLeaf counts are 3, 2, 2, 3, so every fill is 1 and `c_KudzuLeaf = 1`.
- `m_KudzuMound = 1 − 0.50 × 0.6 = 0.70`; `m_KudzuLeaf = 1 − 0.10 × 1 = 0.90`
- Walk: `m = min(0.70, 0.90) = 0.70` → **3.50 m/s**
- Sprint: `m_KudzuMound = 1 − 0.65 × 0.6 = 0.61`, `m_KudzuLeaf = 0.80` → `min = 0.61` → 8 × 0.61 = **4.88 m/s**

**C. Full thicket.** Centre of a ShrubThicket block (all four fills 1): walk **2.75**, sprint **3.20 m/s**.

## 6. Where it sits in the move, and crouch stacking

The engineer applies `m` to the **move target, before the existing clamps**:

```
target = MoveTarget(input, sprint) × m        // new: m from section 5
if (WalkSpeedCap > 0) clamp to WalkSpeedCap   // existing, unchanged
if (StanceSpeedLimit >= 0) clamp to it        // existing, unchanged
```

- **Crouch stacks by `min`, not by multiplying.** Crouch speed is `min(1.75, bush walk speed)`. Every full-cover bush walk speed is ≥ 2.50, so crouch is **1.75 in or out of bushes**. This matches the existing rule that crouch "combines with any speed cap by `min`" (`player-stances.md` §4, line 44).
  - Why: crouching is already the pace you'd pick through brush at. Multiplying would put crouch at 0.875 m/s in a thicket (1.75 × 0.50), a CrouchWalk playback rate of about 0.49. That is outside the proven 0.70–1.40 foot-slide band (`player-stances.md` §3a), so it would reopen the stance pod's proof.
  - If `crouchSpeed` is ever tuned above a bush walk speed (the range allows 2.50; KudzuMound walk is 2.50), `min` still holds and crouch is never faster than walking in that bush.
- **Tower caps win.** While `WalkSpeedCap > 0` (tower stairs and landing), `m = 1`. Tower speeds stay 1.15 and 1.5 everywhere.
- **Water:** apply `m` only in `WaterMode.Dry`. Wading and swimming keep their own speeds, so the water tests' baseline doesn't move.
- **Outside bushes nothing changes:** `m = 1.0f` exactly, so walk 5, sprint 8 and jump 1.2 are untouched (jump uses no horizontal speed at all, `PlayerController.cs:217-218`).
- Acceleration is unchanged (20 m/s²). Entering a full thicket at walk, 5 → 2.75 takes about 0.11 s. That reads as a quick drag, not a wall.

## 7. Tuning data

All values go in one ScriptableObject (suggested name `BushSlowdownTuning`, engineer's choice), per the engine note's rule that designer numbers are data:

| Field | Start value | Range allowed by the table rules |
|---|---|---|
| `M_walk[8]`, `M_sprint[8]` | section 4 | 0.30–1.00, keeping the four rules in section 4 |
| `N_L[8]` (full-cover count) | 1 for Briar, ShrubThicket, KudzuMound; 2 for KudzuLeaf; unused for others | 1–3 |
| Layer indices | `BellsBendVegetation` constants (0–7) | read from the constants, never hard-coded twice |

Validate the section 4 rules when the asset loads (`OnValidate`), and log an error if one fails.

Performance: the lookup is up to 4 layers × 4 cells per tick. It must not allocate per tick (`TerrainData.GetDetailLayer` allocates a new array, so cache the shrub layers or the cells near the player). Budget: inside B4's "Windows FPS within 5%".

## 8. Feedback recommendation (not built in this pod)

No HUD. Feedback is what you hear and see in the world.

1. **Rustle audio (recommended first).** A brush-movement loop on the player whose volume follows `(1 − m) × horizontal speed / 8`. Silent in the open, loud when you sprint into kudzu, quiet when you creep. One sound set per family: dry briar crackle, leafy thicket swish, kudzu vine rustle. Owner when built: `audio-designer`.
2. **Camera brushing (second).** A small view sway, at most 0.5° roll and 0.5° pitch, scaled by the same value, plus leaf cards that brush past the near plane in ShrubThicket and KudzuMound only. No screen shake. Turn it off with any reduce-motion setting. Owner when built: `technical-artist` with `gameplay-engineer`.
3. **Not recommended:** a speed or cover icon, vignette or any HUD element (automatic FAIL on #21).

**For `team-lead` (not decided here):** rustle that wildlife or other players can hear would make brush noise a real risk, which touches the "scarcity and risk" pillar and #15/#16. That is a scope and pillar question.

## 9. Acceptance criteria

Test setup for PlayMode rows: a flat test terrain with 2 m detail cells (same as Bells Bend), the player standing, `WaterMode.Dry`, no tower zone. A **full patch** is a 15 × 15-cell (30 m) block of one layer at count `N_L` in every cell, so `c = 1` within 14 m of its centre. The player starts at rest at the patch centre, holds full forward input (walk or sprint) for 1.5 s, and the measured speed is the horizontal distance moved over the **last 1.0 s** (60 ticks) divided by 1.0 s.

### B1: each shrub layer slows by its multiplier, and bigger or denser cover slows more

| # | Check | Expected | Tolerance |
|---|---|---|---|
| B1.1 | Walk in a full patch of each layer (8 rows) | 5 × `M_walk`: Broomsedge 5.00, Briar 3.50, ShrubThicket 2.75, KudzuMound 2.50, KudzuLeaf 4.50, Fern 5.00, CrackGrass 5.00, BankGrass 5.00 | ±0.05 m/s |
| B1.2 | Sprint in a full patch of each layer (8 rows) | 8 × `M_sprint`: 8.00, 4.00, 3.20, 2.80, 6.40, 8.00, 8.00, 8.00 (same order) | ±0.05 m/s |
| B1.3 | Bigger slows more: full-patch walk and sprint speeds | KudzuLeaf > Briar > ShrubThicket > KudzuMound, each step at least 0.2 m/s apart | strict |
| B1.4 | Denser slows more: for Briar, ShrubThicket and KudzuMound, the multiplier at `c = 0.25`, `0.5`, `1.0` (EditMode test on the pure function) | equals `1 − (1 − M) × c` for walk and sprint; strictly falls as `c` rises | ±0.001 |
| B1.5 | Worked examples A and B (EditMode, from the given cell fills and fractions) | A: 7.00 sprint, 4.625 walk. B: 3.50 walk, 4.88 sprint | ±0.001 m/s |
| B1.6 | Overlapping layers use the minimum (EditMode): a cell with KudzuMound `c = 1` and KudzuLeaf `c = 1` | walk 2.50, sprint 2.80 (not 2.25 / 2.24) | ±0.001 |
| B1.7 | Crouch in a full ShrubThicket and a full KudzuMound patch (PlayMode) | 1.75 m/s, same as crouch on open ground | ±0.05 m/s |

### B2: leaving the bush restores walk 5 and sprint 8 exactly

| # | Check | Expected | Tolerance |
|---|---|---|---|
| B2.1 | The multiplier where all four cells have no slowing cover (EditMode) | exactly `1.0f` (`==`), walk and sprint | none |
| B2.2 | Start at the centre of a full KudzuMound patch, walk forward out of it; measure 1.0 s after the feet are 2 m past the last cell centre (cover is 0 there) | 5.00 m/s; the move target magnitude is exactly 5.0f | ±0.01 m/s measured; target exact |
| B2.3 | Same as B2.2 at sprint | 8.00 m/s; target exactly 8.0f | ±0.01 m/s measured; target exact |
| B2.4 | Full patches of Broomsedge, Fern, CrackGrass and BankGrass at walk and sprint | 5.00 and 8.00; target exactly 5.0f / 8.0f | ±0.01 m/s measured; target exact |
| B2.5 | Tower stairs and landing (existing tower tests) | 1.15 and 1.5 unchanged | baseline from #12 |
| B2.6 | Jump height on open ground | 1.2 m unchanged | baseline from #12 |

"Target exactly 5.0f / 8.0f" in B2.2–B2.4 is read through a read-only `LastMoveTargetMagnitude` property on `PlayerController` (the target after the bush multiplier, before the speed caps), which `gameplay-engineer` adds for tests.

### B3: bushes don't block or snag

The system adds no colliders and no queries (section 2), so B3 checks that motion stays smooth.

| # | Check | Expected | Tolerance |
|---|---|---|---|
| B3.1 | Start at rest at the centre of a full ShrubThicket patch and walk forward for 1.5 s. Do the same for KudzuMound | After the first 0.5 s, every tick's horizontal displacement is > 0 and within 5% of `5 × M_walk × dt` (dt = 1/60 s). No tick has zero displacement | 5% per tick |
| B3.2 | Walk from open ground straight through a full ShrubThicket patch and out the far side | The player exits the far side. No tick after the first 0.5 s has zero horizontal displacement | none |

## 10. Open items

- All multipliers are **assumed (not playtested)**. A playtest pass should check whether thicket walk 2.75 feels like pushing through brush or like a penalty. Retune inside the section 4 rules; the B1 tests read the asset, so they follow.
- Bilinear blending is clamped at tile edges. Where two terrain tiles meet, cover may step by up to one cell. Assumed acceptable; `qa-lead` may note it.
- Wildlife-audible rustle: a `team-lead` question (section 8).
