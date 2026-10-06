---
title: 'Domain research: Bells Bend as the Washed Ashore map'
type: 'domain'
topic: 'Bells Bend (Nashville, TN) peninsula mapping for the game map'
decision: 'How to model the land portion of the map on Bells Bend and where/how to stop players going north'
source: 'native run (5 subagents) + Noah reference images'
status: complete
validation: 'normal (single-source figures flagged in digests)'
created: '2026-10-05'
---

# Domain research: Bells Bend as the Washed Ashore map

**References:** `noah-boundary-sketch.png` (the red line is the north limit; the blue is future water) and `satellite-reference.jpg`.
**Digests:** `digests/geography-r1-1.md`, `elevation-r1-1.md`, `landcover-landmarks-r1-1.md`, `adaptation-r1-1.md`, `boundary-r1-1.md`.
**Data:** `digests/shoreline.geojson` (51- and 84-point inner bank, playable polygon, north line) and `digests/roads.geojson` (355 OSM segments).

## Key facts

| Fact | Value | Source |
|---|---|---|
| North line (red) | **36.2055 N** ±0.0015. West bank 36.2055, -86.9502; east bank 36.2055, -86.9054 | geography (sketch matched to OSM) |
| Peninsula south of the line | about **7.7 km N–S × 5.5 km E–W**, **25.7 km²**, shoreline 19.3 km. Pear-shaped: 4.0 km wide at the line, 2.4 km waist at 36.19, 4.5 km widest at 36.16 | geography (OSM banks) |
| River | about 200 m wide; Cheatham Lake normal pool **385 ft / 117.3 m** | geography, elevation (USACE 2018 master plan) |
| Relief | Bottoms 120–127 m (3–10 m above the pool). **Central N–S ridge** at 230–239 m (peak 239.2 m at 36.166, -86.906). Median slope 7.9°, 90th percentile 22° | elevation (live USGS EPQS and 3DEP samples) |
| Elevation data | USGS 3DEP, public domain. The ImageServer `exportImage` serves a clipped float32 GeoTIFF at about 1 m native, up to 8000 px. A 2022 1 m Davidson County DEM also exists | elevation |
| Land cover | Forest on the central ridge and hollows. Fields along the west bottom strip, in the southern bowl and on the east lobe. Ponds inside the west bank at about 36.18–36.19. **No measured percentages** (satellite reading plus Metro 2006 constraint figures) | landcover |
| Roads | **Old Hickory Blvd is the only road in** (formerly Bells Bend Rd; no bridge). Pecan Valley Rd, Tidwell Hollow Rd, Cleeces Ferry Rd (to the old ferry landing). TN-12 and Scottsboro lie **north of the line** | landcover (OSM) |
| Landmarks | Bells Bend Park (808 ac): Outdoor Center, 1842 Buchanan House, campsite, viewpoint, boat slipway (east bank), wastewater plant. Buzzard Bluff and McCord Bluff, Potato Hill, Robertson Island. Cleeces Ferry landing (ran 1880s–1990) | landcover |
| 25 years of decay | Fields turn to patchy eastern red cedar woodland 5.5–8 m tall, with blackberry, sumac and broomsedge. Kudzu on rights-of-way; privet and bush honeysuckle thickets on edges and floodplain. Roads cracked and cratered; house walls sagging | adaptation (USDA FEIS, VT extension, TN Forestry, Weisman) |
| Scale | Recommended **1:2 horizontal** (about 3.9 × 2.8 km; a 5 m/s run crosses in 7–10 min). Keep vertical near true. Origin at the map centre, so no floating-origin issues | adaptation (design recommendation) |
| Boundary | Layers: a visible in-world barrier with a closed gate that can open later, a hidden collider backstop, an authoritative position clamp, and an optional in-world warning. Invisible walls alone are criticised. The line is stored as data | boundary |

## Implications for the spec

1. **Horizontal 1:2 doubles the slopes.** True vertical would push about 10% of the land past 40°. Make vertical exaggeration a parameter (default 0.7), with a walkability check.
2. **Plan for the water now.** Everything outside the playable polygon, including the opposite banks the sketch turns to water, must sit below the water level later, so the terrain must extend past the shore with a falloff below the pool line.
3. **The north line sits at the neck**, just south of TN-12, so the barrier crosses the narrowest high ground (about 135–170 m elevation) between the two river arms.

## Licenses

- **USGS 3DEP:** public domain (credit as a courtesy).
- **OpenStreetMap shoreline and roads:** ODbL. Ship the credit **"© OpenStreetMap contributors"**. Any redistributed derived *data* stays ODbL.

## Open questions

- The vertical datum (NAVD88 assumed).
- The status of the May Town Center land in 2023–26.
- A house count; cemeteries and barns.
- The identity of the ponds inside the west bank.
- Whether the 2010 flood reached the bottoms.
