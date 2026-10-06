# ambientCG textures (CC0)

Source: https://ambientcg.com, downloaded 2026-10-06 as the 1K-JPG zips
(`https://ambientcg.com/get?file=<ID>_1K-JPG.zip`). Only the Color and NormalGL maps are kept.

Licence: Creative Commons CC0 1.0 Universal (public domain). Attribution is not required;
it is given in `CREDITS.md` anyway.

| ID | Used as (Bells Bend terrain layer) |
|---|---|
| Ground037 | OldFieldGrass (splat layer 0, read as grass by habitats): moss, broomsedge stubble and twigs in the overgrown fields |
| Ground023 | ForestFloor: leaf litter on the ridge, in the hollows and on the bank |
| Asphalt026C | CrackedAsphalt: Old Hickory Blvd, Cleeces Ferry Rd, Pecan Valley Rd |
| Ground078 | OvergrownGravel: Tidwell Hollow Rd and the Cleeces Ferry track end |
| Ground054 | RiverBank: silt and mud along the Cumberland |
| Rock051 | BluffRock: cliffs (game slope over 40°) and the river bluffs |
| Grass001 | KudzuCover: dense broadleaf vine cover on the road verges |

Re-fetch and rebuild with `unity command eval "return AmbientCgLayers.Build();"`
(`Assets/Editor/Art/AmbientCgLayers.cs`).
