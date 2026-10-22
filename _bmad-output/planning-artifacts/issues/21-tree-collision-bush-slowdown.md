**Bug:** the player walks straight through every tree in Bells Bend. Trees used to block you.

**Second part (new):** walking through bushes should slow you down, and the bigger or denser the bush, the more it slows you.

## Bug: trees have no collision

**Cause (found by reading the code and assets; not yet confirmed in Play mode):**
- The MegaKit tree prefabs each carry a `CapsuleCollider`. For example, `Assets/ThirdParty/Quaternius/NatureMegaKit/Prefabs/Trees/CommonTree_1.prefab` has radius 0.35 m and height 7.26 m.
- Since f56289b (2026-10-06, the Bells Bend land build), the terrain doesn't use those prefabs. `BellsBendVegetation` calls `BellsBendTreeLods.Ensure`, which builds a new prefab per tree in `Assets/World/BellsBend/Art/TreeLods/`.
- That new prefab has a LOD0 mesh, an impostor card and a `LODGroup`, but `Ensure` never copies the collider over.
- All 11 prefabs in `TreeLods/` have 0 colliders.
- The terrains still have `m_EnableTreeColliders: 1`, so Unity would use a collider if the prefab had one.

**Fix direction:**
- Have `BellsBendTreeLods.Ensure` copy the source prefab's collider (type, center, radius, height) onto the LOD prefab root. It must be in the same position relative to the trunk.
- Rebuild the prefabs with the script. No hand edits.
- Dead and twisted trees may need their own collider size if the kit's capsule doesn't fit their trunk.
- Field cedars share the Pine prototypes at a different scale. Check that the collider scales with the tree instance.

## Feature: bushes slow you down

**What exists:** bushes and ground cover are terrain **detail** layers, not trees. The layers are defined in `BellsBendVegetation.DetailSpecs`:

| Layer | Height (m) | Counts as shrub |
|---|---|---|
| Broomsedge | 0.7–1.1 | no |
| Briar | 0.9–1.6 | yes |
| ShrubThicket | 1.5–2.6 | yes |
| KudzuMound | 1.6–3.2 | yes |
| KudzuLeaf | 0.5–0.9 | yes |
| Fern | 0.5–0.9 | no |
| CrackGrass | 0.25–0.5 | no |
| BankGrass | 0.6–1.1 | no |

Detail layers can't have colliders in Unity, so the slowdown needs its own system. For example, sample each detail layer's density under the player and pick a speed multiplier per layer.

**To decide (designer):**
- the speed multiplier per layer, and whether sprint is reduced more than walk
- whether grass and fern slow you at all
- whether density or bush height scales the effect
- whether being in a thicket should give feedback (rustle audio, camera brushing). **No HUD.**

## Pass criteria

| # | Criterion | Evidence |
|---|---|---|
| T1 | The player can't walk through any tree type: common, pine, field cedar, dead or twisted, at walk and sprint, from any side | A PlayMode test that walks into one instance of each prototype |
| T2 | Tree colliders roughly match the trunk (not the canopy). You can walk between trees that are visibly apart | Collider vs trunk size per prototype; a squeeze test between two close trees |
| T3 | The fix is in `BellsBendTreeLods`, and the prefabs are rebuilt by script. Nothing is placed by hand | Code diff and rebuild log |
| B1 | Each shrub layer slows the player by its designed multiplier, and bigger or denser cover slows more | A PlayMode speed table, one row per layer |
| B2 | Leaving the bush restores walk 5 and sprint 8 exactly | PlayMode |
| B3 | Bushes don't block or snag the player. They only slow | PlayMode walk through a dense thicket |
| B4 | No regressions: tower walk, water, wildlife and spawn tests at their current baseline, and Windows FPS within 5% | Test XML vs a baseline run; FPS JSON |

## Automatic FAIL
- Changing walk 5, sprint 8, jump 1.2, or the tower speeds 1.15 and 1.5, outside bushes.
- HUD elements.
- Hand-placed colliders or other hand steps.

## Overlaps
- **Player-character pod** (branch `player-character`): it is changing `PlayerController` (stances, capsule, crouch speed). Wait for it to land, or agree with it how crouching in bushes stacks with the bush slowdown.
- **#15 and #16** (animal and bird population) use the shrub cover maps. Keep the same layer definitions.

## Pod
level-designer or technical-artist (tree colliders), systems-designer (slowdown numbers), gameplay-engineer (slowdown system), qa-lead.
