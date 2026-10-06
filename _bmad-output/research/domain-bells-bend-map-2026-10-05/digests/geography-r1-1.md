# Geography r1-1: extents and shape of Bells Bend

Scope: the peninsula inside the Cumberland River meander, south of Noah's red north line. All coordinates are WGS84 (lat, lon). Accessed 2026-10-05.

Method: The bank geometry comes from OpenStreetMap via the Overpass API (OSM base timestamp 2026-10-06T04:37Z). The west channel's inner bank is OSM way 154005077 (outer ring of relation 2690112, "Cheatham Lake"). The east channel's inner bank is OSM way 379808068 (outer ring of relation 5656572, `water=river`). The two ways share the node at 36.1377767, -86.9147814 at the south tip. Areas and extents are computed with an equirectangular projection at 36.17°N (1° lon = 89.9 km, 1° lat = 110.95 km), so expect about ±1% error. Spot elevations come from the USGS 3DEP Elevation Point Query Service (EPQS).

## Claims

| # | Claim | Source URL | Publisher | Pub date | Accessed | Confidence | Class |
|---|---|---|---|---|---|---|---|
| 1 | Inner bank of the west channel: from the south tip at 36.1378, -86.9148, the bank runs NW, then due north along lon ≈ -86.9404 to -86.9412 from lat 36.159 to 36.194, then NW to the neck (36.2046, -86.9473) and west along the north shore. | https://overpass-api.de/api/interpreter (way 154005077) | OpenStreetMap contributors (ODbL) | live data, base 2026-10-06 | 2026-10-05 | High (mapping); medium (exact bank line) | Primary data |
| 2 | Inner bank of the east channel: from the south tip, the bank runs east to about -86.889 (lat 36.159), then NNW to the river apex. The apex of the bank polygon is at lat 36.2080 (max lat of way 379808068). | https://overpass-api.de/api/interpreter (way 379808068, relation 5656572) | OpenStreetMap contributors | live | 2026-10-05 | High | Primary data |
| 3 | The river centerline (way 40806685, "Cumberland River") reaches its northern apex between Bells Bend and Cockrill Bend at 36.2071, -86.8977. Its southern tip is at 36.1354, -86.9075. Its west channel runs at lon ≈ -86.9415 (lat 36.17–36.19). | same, way 40806685 | OSM | live | 2026-10-05 | High | Primary data |
| 4 | Ashland City Hwy (TN 12) runs about 36.210–36.212 between -86.921 and -86.876 (way 344347060), so it lies about 0.5–0.7 km north of the river apex. | same, way 344347060 | OSM | live | 2026-10-05 | High | Primary data |
| 5 | River width, bank to bank: the east channel is about 160 m at 36.19, 235 m at 36.17 and 180 m at 36.15. The west channel is about 190 m at 36.17 and 205 m at 36.19. | derived from OSM ways 379808068/154004959 and 154005077 | OSM (derived) | live | 2026-10-05 | Medium-high | Derived |
| 6 | Cheatham Lake normal pool is 385 ft MSL. Minimum pool is 382 ft and winter pool 384 ft. Pool area is 7,450 acres, with 320 shoreline miles. The lake runs from Cheatham L&D (river mile 148.7) to Old Hickory L&D (river mile 216.2). | https://upload.wikimedia.org/wikipedia/commons/a/af/Cheatham_Lake_master_plan_-_USACE-p16021coll7-12386.pdf (seen via search-result extract; I did not open the full PDF) | USACE Nashville District | May 2018 | 2026-10-05 | Medium (snippet only) | Primary (indirect) |
| 7 | The west channel bank polygon is tagged as part of "Cheatham Lake" (`water=reservoir`), so Bells Bend lies within the Cheatham pool. | Overpass, relation 2690112 | OSM | live | 2026-10-05 | High | Primary data |
| 8 | Ground elevations: about 118–124 m near the east bank (36.17, -86.8945 → 118 m; 36.20, -86.91 → 119 m), about 123–124 m on the south-tip bottoms (36.142, -86.905/-86.920), and about 122 m at the west bank (36.17, -86.938). An interior ridge reaches 199 m (36.17, -86.910) and 189 m (36.20, -86.925). Normal pool is 117.3 m (385 ft). | https://epqs.nationalmap.gov/v1/json | USGS 3DEP | live | 2026-10-05 | High (point values) | Primary data |
| 9 | Bank character, interpreted from claim 8: the east bank and south tip are low bottomland only about 1–7 m above pool. Along the west bank the ground rises steeply inland (140 m at 0.9 km in, 161 m at 1.8 km in). So the west side sits closer to a bluff/upland; the east and south sides are floodplain. | derived from EPQS | USGS (derived) | live | 2026-10-05 | Medium | Derived |
| 10 | "In the floods of 1926 the Cumberland covered thousands of acres, drowning hundreds of cows". Looting on Bells Bend "escalated in 2010 following catastrophic flooding". Scottsboro is "the small community at the crossroads", with "rocky hills of northern Scottsboro". | https://bellsbend.squarespace.com/history | Bells Bend Conservation Corridor | n.d. | 2026-10-05 | Medium | Secondary |
| 11 | May 2010: more than 13.5 in of rain fell in 36 hours. The Nashville crest was 51.86 ft, the highest since the dam system was built. | https://www.weather.gov/ohx/may2010flood (via search extract) | NWS Nashville | 2010+ | 2026-10-05 | Medium | Primary (indirect) |
| 12 | Bells Bend Park covers 808 acres, opened in 2007, and has about 7.4 mi of trails. | https://www.tclf.org/bells-bend-park (via search extract) | The Cultural Landscape Foundation | n.d. | 2026-10-05 | Medium | Secondary |
| 13 | Bells Bend has river terraces and bottomland soils. The Quaternary alluvium is gravel, sand, silt and clay. The bend is surrounded by Western Highland Rim slopes. | https://digital.tnconservationist.org/publication/?i=717358&article_id=4091300 ; https://pubs.usgs.gov/of/1980/0202/report.pdf (search extracts) | TN Conservationist (2021); USGS OFR 80-202 | 2021; 1980 | 2026-10-05 | Low-medium | Secondary |

## Key numbers table

| Quantity | Value | Basis |
|---|---|---|
| Bounding box of the playable peninsula (inner bank, south of the north line) | S 36.1363, N 36.2055, W -86.9502, E -86.8888 | OSM banks + north line |
| North–south extent | ≈ 7.68 km | derived |
| East–west extent (max) | ≈ 5.51 km | derived |
| Land area south of the line, inside the banks | ≈ 25.7 km² ≈ 6,350 acres | shoelace on 84-pt bank + north line |
| Width along the north line (36.2055) | ≈ 4.0 km (-86.9502 to -86.9054) | derived |
| Narrowest waist | ≈ 2.4 km at lat 36.19 (-86.9405 to -86.9138) | derived |
| Widest part | ≈ 4.5 km at lat 36.16 (-86.9395 to -86.8890) | derived |
| Width at 36.20 / 36.18 / 36.17 / 36.15 / 36.14 | 2.9 / 3.0 / 4.0 / 3.9 / 2.5 km | derived |
| Shoreline length (W crossing → tip → E crossing) | ≈ 19.3 km | derived |
| River width | ≈ 160–235 m (typically about 200 m) | OSM banks |
| Normal pool (Cheatham Lake) | 385 ft = 117.3 m MSL (min 382 ft = 116.4 m; winter 384 ft = 117.0 m) | USACE master plan (snippet) |
| Low bank ground | ≈ 118–124 m (1–7 m above pool) | USGS EPQS |
| Interior ridge high point sampled | ≈ 199 m (36.170, -86.910), about 82 m above pool | USGS EPQS |
| 2010 Nashville crest | 51.86 ft stage | NWS (snippet) |

The extent is shaped like a pear. A waist about 2.4 km wide sits at 36.19, just south of the north line. It widens to about 4 km at the line itself, because the river turns west on the west side and east on the east side. The broad southern lobe is about 4.5 km wide at 36.16 and rounds off to the tip near 36.136, -86.905.

## North line estimate

I calibrated the sketch against OSM. The river apex sits at sketch y ≈ 145 px (lat 36.2071), and the west-channel centerline at x ≈ 320 px (lon -86.9415). The scale is about 13.5 m/px, which matches the sketch's south tip (x ≈ 560 → -86.9055 against OSM -86.9075). As a check, the TN-12 shield at y ≈ 108 maps to 36.2116; OSM puts TN-12 at 36.210–36.212.

- **Red line latitude ≈ 36.2055 (±0.0015, about ±170 m).** It sits about 0.6 km south of TN-12 and about 0.2 km south of the river's northern apex.
- **West crossing (inner bank): 36.2055, -86.9502.** Here the river turns west toward Ashland City, and the inner bank becomes the river's north shore. The opposite (south/Whites Bend) bank is at about 36.2055, -86.9583.
- **East crossing (inner bank): 36.2055, -86.9054.** This is on the Bells Bend bank of the channel between Bells Bend and Cockrill Bend. North of this the bank keeps rising to the apex at 36.2080. The line is nearly tangent to the river apex, so a small shift of the line moves this crossing a lot along the bank.
- Scottsboro / Old Hickory Blvd / TN-12 all lie north of the line, about 36.209–36.212, so they are outside the playable area.
- The red line also runs on east across Cockrill Bend and the river, out to TN-155. That stretch lies outside the peninsula and doesn't matter here.

## Shoreline points (50 segments, 51 pts, about 385 m spacing)

The order runs from the W crossing, south down the west channel, round the south tip, then north up the east channel to the E crossing. With the north line closing it, the ring is counterclockwise in map terms. The full-resolution bank (84 pts), the 51-pt resample, the closed polygon and the north line are in `digests/shoreline.geojson`, with the ODbL attribution embedded.

```
36.205500,-86.950186  36.203770,-86.946631  36.201157,-86.943986  36.198536,-86.941182
36.195235,-86.940239  36.191768,-86.940284  36.188334,-86.940858  36.184875,-86.941179
36.181400,-86.941225  36.177986,-86.940532  36.174512,-86.940433  36.171039,-86.940530
36.167566,-86.940629  36.164095,-86.940424  36.160676,-86.939666  36.157227,-86.939279
36.153873,-86.938421  36.151333,-86.935506  36.148683,-86.932739  36.145812,-86.930321
36.142940,-86.927906  36.140399,-86.925014  36.138761,-86.921318  36.138067,-86.917131
36.137309,-86.912957  36.136495,-86.908795  36.136536,-86.904541  36.137439,-86.900408
36.139481,-86.896979  36.142042,-86.894097  36.145167,-86.892279  36.148459,-86.890910
36.151872,-86.890110  36.155297,-86.889383  36.158738,-86.888853  36.162142,-86.889597
36.165284,-86.891391  36.168103,-86.893887  36.170793,-86.896561  36.173177,-86.899680
36.175322,-86.903054  36.178015,-86.905693  36.181081,-86.907707  36.183828,-86.910305
36.186768,-86.912570  36.190042,-86.913842  36.193499,-86.913686  36.196796,-86.912378
36.199868,-86.910376  36.202830,-86.908139  36.205500,-86.905449
```

**Licence:** © OpenStreetMap contributors, ODbL 1.0 (https://www.openstreetmap.org/copyright). The game credits must carry this attribution. A derived database (the GeoJSON) is share-alike under ODbL.

## Leads

- OSM has several `water=reservoir` polygons just inside the west bank at about 36.178–36.195, -86.935 to -86.940 (ways 1238492819–1238492831), plus ponds at 36.180–36.185, -86.917 to -86.925. They could be quarry, sediment or farm ponds and are worth identifying for level design.
- The full USACE Cheatham Lake Master Plan (2018) PDF should give the flowage easement and flood-control elevations. It may also give the 2010 pool stage at Bells Bend river mile (about RM 180–186?), but that RM is unverified.
- The NWS assessment (https://www.weather.gov/media/publications/assessments/Tenn_Flooding.pdf) and USGS May 2010 flood inundation mapping could give the 2010 extent over the Bells Bend bottoms.
- A Metro Nashville GIS / Davidson County 2 ft contour or LiDAR DEM would give a terrain heightmap. The EPQS spot checks show about 80 m of relief.
- Metro Parks and Bells Bend Park boundary: 808 acres, covering about 13% of the playable area.

## Could not find

- I found no source that directly states Bells Bend bottoms were inundated in May 2010. One source states only that flooding happened and that looting followed. The 1926 inundation is documented.
- I could not verify the pool elevation on a page I opened. The 385 ft figure comes from a search extract of the USACE 2018 master plan; the TWRA page refused the connection.
- I found no authoritative statement on bank character (bluff versus floodplain). Claim 9 is my interpretation of USGS spot elevations.
- I found no river-mile designation for Bells Bend.
