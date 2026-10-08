# Brief: fish for Washed Ashore (Bells Bend, Cheatham pool of the Cumberland River)

Decision: which fish live in the water around Bells Bend, where they sit by shore distance and depth, how a player standing above murky water would see them, and the measurable targets (density, sightings, surface events, visibility, scatter, sim radius) the fish pod builds and QA tests against. Also the director gate items G1, G2, G5 and G6 (G3 shared with f-artist, G4 is f-artist's stills).
Type: domain. Shape: describe + quantify. Preset: standard (6 dimensions, 1-2 rounds). Validation: normal; two-source on the carp ruling (G2) and the Secchi number (G5). Red team: off (headless; logged as an assumption).
Lead: f-designer (systems-designer). Contributors: f-artist (items 3 and 5, look half), f-td (item 5 technique, WebGL2 cost), f-level (structure anchors). Headless plan-and-proceed: scope set by team-lead's tasking and the spec.

## Requirements frame (from the project, not from research)
- Spec: _bmad-output/planning-artifacts/poc-spec-washed-ashore-fish.md (items 1-7, F1-F10).
- Greenlight notes N1-N7 (studio:decisions greenlight/fish): murk decides what you see; not an aquarium; 25 years with no fishing and no dam operation; carp only if the lore allows; Fish1/Fish2 only in a muted palette; slow, economical behaviour; the avoid list.
- Lore: the collapse began in 1987; the game is set about 25 years later (README.md). "No dam operation" means no lockage, no generation schedule; the pool level is settled (N3).
- Water as landed (water-td-note.md): surface = WaterLevelY + WaterMotion.Offset; WaterMaps RG8 (R shore distance, G bed depth clamped at 4 m); shader extinction RGB 2.2/2.4/3.2 per m; camera never underwater.
- Bed profile (water-swim-spec.md): 0.31 m lip, 18 deg shelf with depth = 0.325 s - 0.3 to W-2 at about 7.1 m out, smoothstep ramp to W-10 by 20 m out, flat W-10 beyond. Bluffs meet the water as sheer faces.
- Map scale: 1:2 horizontal squeeze, but player-scale things (and the bank profile) are in game metres and are not halved (bells-bend-design-brief.md). Fish densities are therefore applied per game square metre of water, unhalved.

## Dimensions
D1 Species of the Cumberland River / Cheatham pool; adult lengths; habitat and group behaviour (f-designer)
D2 Silver and bighead carp: history, passage, lore ruling input for G2 (f-designer; two-source)
D3 25 years with no fishing, no stocking and no dam operation: population change (f-designer)
D4 Visibility: Secchi range for Cheatham pool, reconciliation with the shader (f-designer numbers; f-artist look; two-source)
D5 What games do (f-designer) + technique and WebGL2 cost (f-td) + look/stand-in mapping (f-artist)
D6 Reaction to the player: flight distances and settle times (f-designer)
