# Credits

## Map data

- Elevation: USGS 3DEP (public domain)
- © OpenStreetMap contributors (ODbL)

## Textures and plants

- Terrain textures: ambientCG (https://ambientcg.com), CC0 1.0. Ground037, Ground023, Asphalt026C, Ground078, Ground054, Rock051, Grass001 (see `Assets/ThirdParty/ambientCG/SOURCE.md`)
- Trees and plants: Stylized Nature MegaKit by Quaternius (https://quaternius.com), CC0 1.0

## Characters

- Player character ("Weary Survivor"): generated with Meshy (https://www.meshy.ai) from a concept image, re-skinned and split into head and body by script onto the project skeleton; base colour from the Meshy export; metallic and roughness packed by script into `Survivor_MetallicGloss.png` (`tools/blender/character/reskin_tex.py`); files in `Assets/Art/Character/Survivor/`; raw Meshy export and texture maps in `tools/blender/character/source/`. Licence: generated with Meshy (meshy.ai) on a paid Pro plan, October and November 2026; per Meshy's terms the output is owned by the account holder and needs no attribution.
- Character animation: the pack clips Idle, Idle_Neutral, Walk, Run, Run_Back, Run_Left, Run_Right and Interact of the Ultimate Modular Men pack by Quaternius (https://quaternius.com), CC0 1.0, retargeted to the project skeleton by `tools/blender/character/retarget_pack.py` from `tools/blender/character/source/Quaternius_Men_Animations.fbx` (`Assets/Art/Character/Clips/Pack_Retargeted.fbx`); the other clips are authored by script (`tools/blender/character/author_clips.py`). Licence text: `Assets/ThirdParty/Quaternius/LICENSE_CC0.txt`.
