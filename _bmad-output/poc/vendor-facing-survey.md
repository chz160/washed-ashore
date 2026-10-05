# Vendor pack facing survey (Unity space)

**Date:** 2026-10-05 · **Owner:** technical-artist · **Scope:** read-only. I sampled the vendor zips with Blender 5.2.2 in headless mode. I did not touch the Unity project or the Editor.

## Method

- I imported each sample with Blender's FBX importer (GLB through the glTF importer), using a rest pose and default settings, then mapped Blender world coordinates to Unity with `(x, y, z)_blender -> (x, z, y)_unity`.
- **Calibration control:** Quaternius `Deer.fbx`. Under this mapping the head lands at Unity **-Z**, which matches what we saw in the Editor. The opposite reflection, `(-x, z, -y)`, put the head at +Z, so I discarded it.
- **Chirality cross-check:** in every rigged sample, the bones on the character's left (`*.L`) sit at Unity **+X**. In Unity's left-handed frame, that only happens when the model faces **-Z**. So this check agrees with the head and foot measurements.
- I took the facing from bone vectors (Head minus Body/Hips, Foot head to tail, Beak relative to Head). I took gun barrels from mesh profiles (the thin end along the long axis). I confirmed each with orthographic Workbench renders (side, top and front).

## Results

| Pack | Sample(s) | Rigged | Animated | Front in Unity | Needs fix | Confidence |
|---|---|---|---|---|---|---|
| Ultimate Animated Animals | Deer (control), Stag, Fox, Cow | Yes (Generic, `AnimalArmature`, root bone `Body`) | Yes (12–13 clips) | **-Z** (head 1.9–4.0 m ahead of `Body` along -Z) | **Yes, rotate 180° Y** | High |
| Ultimate Modular Men | Swat, Suit (plain FBX), Swat (Humanoid Rig FBX) | Yes (`CharacterArmature`, `Root`/`Hips`) | Plain FBX: 24 clips. Humanoid Rig FBX: 0 | **-Z** (Foot bones point -Z; left at +X; render shows face and toes at -Z) | **Yes, rotate 180° Y** (see the Humanoid caveat) | High |
| Ultimate Modular Women | Soldier, Witch (plain FBX), Soldier (Humanoid Rigs FBX) | Yes (same rig layout as Men) | Plain FBX: 24 clips. Humanoid Rigs FBX: 0 | **-Z** (same evidence as Men) | **Yes, rotate 180° Y** (see the Humanoid caveat) | High |
| LookToTheBirds American Robin | `American_Robin_Mesh.fbx` | Yes (`Armature`, root bone `Root`, 53 bones) | Yes (40 actions in FBX) | **-Z** (Beak, Toe and Foot bones all point -Z) | **Yes, rotate 180° Y** | High |
| LookToTheBirds American Robin | `American_Robin_Mesh.glb` | Yes | Yes (20 actions) | Faces glTF +Z (the spec forward). In Unity this is **+Z under glTFast or UnityGLTF (X-mirror)**, so no fix needed. | No, if imported as GLB. Don't import both. | Medium (depends on the importer) |
| Ultimate Gun Pack | AssaultRifle_4, Pistol_1, SniperRifle_3, Shotgun_1 | No | No | **Barrel +X**, top +Y, grip -Y | Yes for a forward-is-+Z convention: **rotate -90° Y** (`Euler(0,-90,0)`), not 180° | High |
| Survival Pack (weapons) | Shotgun_1 (same mesh as Gun Pack), Revolver_1, FlareGun | No | No | **Barrel +X**, top +Y | Same as Gun Pack: -90° Y, handled through the socket | High |
| Survival Pack (tools/props) | Axe, Tent | No | No | Axe: handle along Y, blade to -X. Tent: symmetric along Z. | No (a hand socket defines the axe's pose) | Medium |
| Downtown City MegaKit (`FBX (Unity)` folder) | Building_Large_2, Brick_Inset_Window, Door_1 | No | No | Building facade (entrance) faces **-Z**, with the pivot at the facade corner. Wall module: the sill sticks out to -Z, so the exterior faces -Z. Door: pivot on the hinge edge, and it's double-sided. | No import fix. Level snapping handles it; it only matters for scripted "street-facing" placement. | Medium (the "front" of a modular piece is a convention, not a feature) |
| Stylized Nature MegaKit | CommonTree_1 (`FBX` and `FBX (Unity)` folders) | No | No | Not defined (radial) | No | High |
| Nature Crops | Wheat_1, Carrot_1 | No | No | Not defined (radial) | No | High |

The plain `FBX` and `FBX (Unity)` variants of CommonTree_1 import identically in Blender. The `(Unity)` variant probably differs only in its axis and scale header. That should make Unity's root transform cleaner, but it won't change the world-space facing.

## Recommendation

1. **The import fix applies to rigged creatures and characters.** Every rigged sample faces -Z: the Quaternius animals, the Modular Men and Women, and the Robin FBX. This is a consistent artifact of the Blender-to-FBX export, not a per-asset accident, so expect future Quaternius and Blender-authored packs to arrive the same way. Rotating 180° about Y at the model root in the AssetPostprocessor is correct for all of them.
2. **Use socket offsets for weapons, not the postprocessor.** Guns are 90° off (barrel +X), not 180°. A head/front-based 180° flip would be wrong for them. Author an equip socket or grip transform per weapon family with `Euler(0,-90,0)`, plus a muzzle transform. Both Quaternius gun sources share this orientation, so one offset works for every weapon tested. The gun meshes also look about 5–6 times real size (an AK measures about 5.5 units), so check scale in the Editor before tuning the socket.
3. **Facing doesn't matter for** trees, crops, foliage, and most survival props. For modular city kit pieces, grid snapping and rotation handle it. They consistently put the exterior or facade at -Z, so if scripted city placement ever needs a "street-facing" side, treat -Z as the convention instead of rotating on import.
4. **The detection rule "head bone points -Z in root space ⇒ flip 180°" is NOT safe as written:**
   - For quadrupeds it is safe and robust: the head sits 1.9–4.0 units ahead of `Body`.
   - **For bipeds it gives the wrong answer.** Measured from Hips, the Modular Men and Women heads sit 0.017–0.035 units toward **+Z**. That's posture lean, but it's on the opposite side from the true facing (-Z). The rule would skip the flip, and these characters would stay backwards. If you measure the Head bone's own axis instead, it points +Y on bipeds and gives no signal at all.
   - On the Robin it is correct but marginal: 0.021 units, compared with a body height of 0.155.
   - **Use this rule instead.** Facing is -Z if any of the following holds, checked in this order of preference:
     - (a) The mean X of the `*.L` bones is greater than 0. This is the left-side test. It held on every rigged sample, works for any Blender-named rig, and doesn't depend on posture.
     - (b) The Foot/Toe bone's head-to-tail vector, or the Beak/Nose vector, points -Z.
     - (c) For quadrupeds, Head minus Body points -Z by more than 25% of body length.
   - If none of these give a clear answer (no `.L`/`.R` naming, no feet, ambiguous), log a warning and don't flip. Guard it to rigged models only, so weapons and props never get flipped.
5. **Humanoid caveat (verify in the Editor):** the Men and Women packs ship separate "Humanoid Rig(s)" FBXs that have no clips. If they're imported with Animation Type = Humanoid, Mecanim derives the body orientation from the avatar. Retargeted clips with "Root Transform Rotation: Based Upon Body Orientation" can then turn the character to face the GameObject's +Z even when the bind pose faces -Z, and a 180° postprocessor flip on top of that could double-correct. So have one Play-mode test on a Humanoid import, both with and without the flip, before applying the postprocessor to Humanoid-type imports.
6. **GLB note:** the Robin is the only sample that ships both formats, and the two disagree in Unity (FBX faces -Z, GLB faces +Z). Pick one format per asset. Key the postprocessor rule on FBX only.
