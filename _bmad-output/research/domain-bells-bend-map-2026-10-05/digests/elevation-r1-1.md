# Digest: Bells Bend topography and real elevation data (elevation-r1-1)

Researcher dimension: topography and real elevation data. Accessed 2026-10-05.
Raw evidence is from live USGS API calls made this run: EPQS, TNM Access, and the 3DEP ImageServer. I downloaded a 560 x 520 float32 GeoTIFF clip covering lon -86.99 to -86.85 and lat 36.13 to 36.26 (about 22 m x 28 m pixels) and analysed it with numpy.

## Claims

| # | Claim | Source | Publisher | pub_date | accessed | confidence | class |
|---|---|---|---|---|---|---|---|
| 1 | The Bells Bend peninsula proper is a south-pointing loop of the Cumberland River. It spans about lon -86.945 to -86.888 (about 5 km E-W) and lat 36.137 (southern tip) to about 36.21 (northern neck), about 8 km N-S. The river runs along the west arm at about -86.94, rounds the south tip at about 36.137, and runs up the east arm at about -86.89 to -86.90. | Derived from the 3DEP ImageServer clip (cells at or below 119 m traced as river) and EPQS spot checks | USGS | live service (copyright stamp 2026-10-01) | 2026-10-05 | medium (my own raster trace, not a published map) | derived-measurement |
| 2 | Scottsboro lies to the north and Bells Bend to the south. Bells Bend is a "peninsula looped by the Cumberland River", on fertile floodplain. Scottsboro-Bells Bend together cover about 13,000 acres. | nashville.gov / tclf.org / nashvillelifestyles.com (search-result summary) | Metro Nashville Parks; TCLF | n.d. | 2026-10-05 | medium (search summary) | secondary |
| 3 | Bells Bend Park covers about 808 acres and sits along the river on the west side of the peninsula. The Outdoor Center is at 4187 Old Hickory Blvd. Park coordinates are 36.157778, -86.938889. | nashville.gov (search summary); https://nashvillesites.org/records/bells-bend-park | Metro Nashville; Nashville Sites | n.d. | 2026-10-05 | high | secondary |
| 4 | The river level is the Cheatham Lake pool: normal pool is 385 ft MSL (117.3 m) and minimum pool is 382 ft (116.4 m). Cheatham Lake runs from Cheatham Dam (mile 148.7) to Old Hickory Dam (mile 216.2), so it covers the Nashville reach. | USACE Cheatham Lake Master Plan (via search summary) | USACE Nashville District | 2018-05 | 2026-10-05 | high | primary (via summary) |
| 5 | In the DEM, the water surface around the bend is flat at about 117-119 m (histogram peak in the 118-119 m bin). This matches the 385 ft pool. | ImageServer clip, my histogram | USGS | live | 2026-10-05 | high | derived-measurement |
| 6 | The high ground is a central N-S spine, not only the north half. Spot heights: 239.2 m (36.166, -86.906), 235.2 m (36.182, -86.910), 230.6 m (36.194, -86.922). The row maxima across the peninsula are 206-238 m from lat 36.162 to 36.202. South of about 36.155 the row maxima fall to 131-159 m (low terrace and floodplain). | EPQS + clip row profile | USGS | live | 2026-10-05 | high | primary-measurement |
| 7 | Peninsula relief is about 122 m: from about 117 m (pool) to about 239 m (spine tops). Floodplain bottoms along both arms and the southern tip sit at about 120-127 m, so only 3-10 m above pool. | EPQS + clip | USGS | live | 2026-10-05 | high | primary-measurement |
| 8 | The neck at about 36.21 dips to about 135-170 m (neck_north = 168.9 m; row median 136 m at 36.210). North of the neck, the Scottsboro ridges rise higher: the clip maximum is 276.5 m at 36.256, -86.901, and EPQS gives 277.0 m there. | EPQS + clip | USGS | live | 2026-10-05 | high | primary-measurement |
| 9 | Slope character over the whole clip (22-28 m cells): median slope is 7.9°, 90th percentile 22.4°, 99th percentile 29.4°. This describes flat bottoms with steep, dissected hollow sides on the spine. Slopes will read steeper at 1-10 m resolution. | Gradient of the clip | USGS data, my computation | live | 2026-10-05 | medium (coarse cells, and the clip includes off-peninsula hills) | derived-measurement |
| 10 | The TNM Access API returns 1/3 arc-second (about 10 m) tile n37w087 as GeoTIFF. The current version is 20240923 (480,753,958 bytes); earlier versions are 20230407, 20230130 and 20120201. | TNM Access API JSON | USGS | 2024-09-23 (product) | 2026-10-05 | high | primary |
| 11 | 1 m DEM coverage exists from project TN_DavidsonCounty_D22 (published 2023-01-30): six 10 km UTM 16N tiles, x50-51 / y400-402, each 226-374 MB GeoTIFF. Older 1 m projects TN_Middle_B1_2018, TN_Middle_B2_2018 and TN_Eastern_2_16_B16_Del1_2016 are also listed. | TNM Access API JSON | USGS | 2023-01-30 | 2026-10-05 | high | primary |
| 12 | Lidar point clouds (LAZ) exist for TN_DAVIDSONCO_2007 and TN_DavidsonCounty_D22 (2022). The query returned 414 LAZ tiles in the bbox. | TNM Access API JSON | USGS | 2023-01-27 | 2026-10-05 | high | primary |
| 13 | 3DEP products are available "free of charge and without use restrictions". The 1 m DEM uses the UTM projection. | https://www.usgs.gov/3d-elevation-program/about-3dep-products-services | USGS | n.d. | 2026-10-05 | high | primary |
| 14 | The 3DEP ImageServer (3DEPElevation) is a dynamic multi-resolution bare-earth mosaic. Its native pixel size is about 1 m, maxImageWidth/Height is 8000, pixelType is F32, and its native SR is 3857. exportImage worked with bboxSR=4326 and format=tiff and returned a tiled, uncompressed float32 TIFF. | ImageServer `?f=json` + my exportImage call | USGS | live | 2026-10-05 | high | primary |
| 15 | Unity TerrainData.heightmapResolution is clamped to 33, 65, 129, 257, 513, 1025, 2049 or 4097 (2^n+1). | https://docs.unity3d.com/ScriptReference/TerrainData-heightmapResolution.html | Unity | current | 2026-10-05 | high | primary |
| 16 | Unity Import Raw supports 8- or 16-bit depth and a platform-dependent byte order option. | https://docs.unity3d.com/Manual/terrain-Heightmaps.html | Unity | current | 2026-10-05 | high | primary |
| 17 | Unverified belief (not evidenced this run): 3DEP DEM vertical datum is NAVD88 metres, and water bodies in lidar DEMs are hydro-flattened. Claim 5 is consistent with hydro-flattening. | none retrieved | - | - | - | low | unverified |

## Sampled elevations table

The labelled points are from USGS EPQS (`units=Meters`, wkid 4326).

| Label | Lat | Lon | Elev (m) |
|---|---|---|---|
| central_ridge_S (spine top) | 36.1660 | -86.9060 | 239.2 |
| central_ridge_mid | 36.1820 | -86.9104 | 235.2 |
| central_ridge_N | 36.1940 | -86.9220 | 230.6 |
| neck_north | 36.2100 | -86.9150 | 168.9 |
| park_floodplain_W (Bells Bend Park) | 36.1578 | -86.9389 | 122.3 |
| west_bottom | 36.1800 | -86.9360 | 119.8 |
| south_fields (low terrace) | 36.1480 | -86.9150 | 150.6 |
| south_tip bottom | 36.1410 | -86.9200 | 120.6 |
| east_bottom | 36.1800 | -86.8970 | 127.1 |
| east_lobe_bottom | 36.2000 | -86.9000 | 121.8 |
| river_W_arm (bank pixel; not open water) | 36.1700 | -86.9430 | 122.2 |
| river_S (bank pixel; not open water) | 36.1370 | -86.9250 | 122.6 |
| scottsboro_ridge_N (outside peninsula) | 36.2561 | -86.9006 | 277.0 |
| check point E of east arm | 36.1664 | -86.8780 | 132.7 |

The coarse 6 x 6 EPQS grid below runs over lon -86.98 to -86.88 (0.02° step, west to east) and lat 36.14 to 36.24. Columns 1-2 and the bottom-left corner are off-peninsula (across the river).

```
36.24: 207.4 204.9 161.2 193.5 206.7 172.9
36.22: 126.5 180.4 210.2 185.7 190.9 152.1
36.20: 138.4 132.7 156.4 153.9 121.8 118.3
36.18: 150.0 165.2 123.3 157.5 120.0 139.9
36.16: 177.5 175.3 118.0 139.8 121.3 138.5
36.14: 200.5 139.3 149.2 119.8 121.9 134.9
```

Clip-wide stats (whole bbox, not only the peninsula): min is 40.8 m (a single low patch near 36.166, -86.878, outside the peninsula). EPQS gives 132.7 m at that point, so the patch is either a quarry pit or a resampling artifact; mask it if the tile ever includes it. Other stats: p5 120.2, p50 157.4, p95 225.7, max 276.5 m.

## DEM products found

| Title | Res | Size (bytes) | URL |
|---|---|---|---|
| USGS 1/3 Arc Second n37w087 20240923 | about 10 m | 480,753,958 | https://prd-tnm.s3.amazonaws.com/StagedProducts/Elevation/13/TIFF/historical/n37w087/USGS_13_n37w087_20240923.tif |
| USGS 1/3 Arc Second n37w087 20230407 | about 10 m | 479,546,608 | https://prd-tnm.s3.amazonaws.com/StagedProducts/Elevation/13/TIFF/historical/n37w087/USGS_13_n37w087_20230407.tif |
| USGS 1 Meter 16 x50y400 TN_DavidsonCounty_D22 | 1 m | 364,995,505 | https://prd-tnm.s3.amazonaws.com/StagedProducts/Elevation/1m/Projects/TN_DavidsonCounty_D22/TIFF/USGS_1M_16_x50y400_TN_DavidsonCounty_D22.tif |
| USGS 1 Meter 16 x50y401 TN_DavidsonCounty_D22 | 1 m | 324,624,349 | https://prd-tnm.s3.amazonaws.com/StagedProducts/Elevation/1m/Projects/TN_DavidsonCounty_D22/TIFF/USGS_1M_16_x50y401_TN_DavidsonCounty_D22.tif |
| USGS 1 Meter 16 x50y402 TN_DavidsonCounty_D22 | 1 m | 226,141,850 | https://prd-tnm.s3.amazonaws.com/StagedProducts/Elevation/1m/Projects/TN_DavidsonCounty_D22/TIFF/USGS_1M_16_x50y402_TN_DavidsonCounty_D22.tif |
| USGS 1 Meter 16 x51y400 TN_DavidsonCounty_D22 | 1 m | 349,838,345 | https://prd-tnm.s3.amazonaws.com/StagedProducts/Elevation/1m/Projects/TN_DavidsonCounty_D22/TIFF/USGS_1M_16_x51y400_TN_DavidsonCounty_D22.tif |
| USGS 1 Meter 16 x51y401 TN_DavidsonCounty_D22 | 1 m | 337,323,745 | https://prd-tnm.s3.amazonaws.com/StagedProducts/Elevation/1m/Projects/TN_DavidsonCounty_D22/TIFF/USGS_1M_16_x51y401_TN_DavidsonCounty_D22.tif |
| USGS 1 Meter 16 x51y402 TN_DavidsonCounty_D22 | 1 m | 374,106,490 | https://prd-tnm.s3.amazonaws.com/StagedProducts/Elevation/1m/Projects/TN_DavidsonCounty_D22/TIFF/USGS_1M_16_x51y402_TN_DavidsonCounty_D22.tif |
| USGS one meter x50y401 TN Middle B1 2018 (older) | 1 m | 130,631,598 | https://prd-tnm.s3.amazonaws.com/StagedProducts/Elevation/1m/Projects/TN_Middle_B1_2018/TIFF/USGS_one_meter_x50y401_TN_Middle_B1_2018.tif |
| Lidar Point Cloud TN_DavidsonCounty_D22 (for example tile 046100) | LAZ | 80,967,868 | https://rockyweb.usgs.gov/vdelivery/Datasets/Staged/Elevation/LPC/Projects/TN_DavidsonCounty_D22/TN_DavidsonCo_1_2022/LAZ/USGS_LPC_TN_DavidsonCounty_D22_046100.laz |
| 3DEP ImageServer exportImage (clipped GeoTIFF on demand) | about 1 m native, any output size up to 8000 px | about 1.6 MB for 560x520 F32 | https://elevation.nationalmap.gov/arcgis/rest/services/3DEPElevation/ImageServer/exportImage |

These are the TNM queries used. The bbox is `-86.98,36.13,-86.86,36.25`, with `&outputFormat=JSON&max=50`.

- `https://tnmaccess.nationalmap.gov/api/v1/products?datasets=National%20Elevation%20Dataset%20(NED)%201/3%20arc-second&bbox=...`
- `datasets=Digital%20Elevation%20Model%20(DEM)%201%20meter`
- `datasets=Lidar%20Point%20Cloud%20(LPC)`

Tile naming: x50y401 means UTM zone 16, 10 km tile. Unverified belief: x/y give the NW corner in 10 km units. From my estimate (E about 507 km, N about 4006 km), the peninsula falls mostly in x50y401. Confirm by reading the GeoTIFF bounds before downloading the others.

## Recommended pipeline

1. **Fetch a clipped DEM. No 300-480 MB tile is needed.**
   Call the ImageServer `exportImage` in UTM so pixels come out square in metres:
   ```
   https://elevation.nationalmap.gov/arcgis/rest/services/3DEPElevation/ImageServer/exportImage?
     bbox=<xmin,ymin,xmax,ymax in EPSG:26916>&bboxSR=26916&imageSR=26916
     &size=2049,2049&format=tiff&pixelType=F32&noData=-9999
     &interpolation=RSP_BilinearInterpolation&f=image
   ```
   I verified this call with bboxSR/imageSR=4326. The UTM variant is a standard ArcGIS parameter but I did not test it this run. A 2049² output over an 8.2 km square gives about 4 m per sample, and 4097² gives about 2 m. Both are under the 8000 px cap.
   For reproducibility, download the 1 m D22 tile(s) from the S3 URLs instead and clip them with `gdalwarp -t_srs EPSG:26916 -te <bounds> -ts 2049 2049 -r bilinear`.
2. **Suggested footprint.** About 8.2 x 8.2 km, from lat about 36.130 to 36.215 and lon -86.955 to -86.875. This covers the whole loop, both river arms and the neck. Extending north to 36.26 adds the 277 m Scottsboro ridges, but it doubles the area. For that case, tile into 2-4 terrains (neighbours / Terrain Groups) at matching edge samples.
3. **Read the TIFF.** GDAL and rasterio are not installed on this machine; only numpy is. The returned TIFF is tiled (TIFF tags 322-325) and uncompressed. A 30-line numpy tile reader worked, and `pip install tifffile` is the simpler route. Mask nodata (-9999) and outlier pits such as the 40.8 m patch.
4. **Normalize.** Set a fixed base of 115 m (just below the 116.4 m minimum pool), so `h01 = (z - 115) / H` with H = Terrain size.y.
   - True scale: use H = 130 m for the peninsula (max about 239 m) or 165 m including Scottsboro (277 m). Real relief is only about 122 m over 8 km, so with true scale (1 Unity unit = 1 m) the floodplain is very flat and the spine is a gentle ridge.
   - Optional vertical exaggeration of 1.2-1.5x: apply it by scaling H, not the data. Keep 1 unit = 1 m horizontally so player speed, weapons and nav stay real.
5. **Orientation.** Unverified belief, check in the editor: GeoTIFF row 0 is north, while Unity `SetHeights(float[y,x])` has y along +Z, so row 0 is south. Apply `np.flipud` before SetHeights or RAW export. Also confirm east maps to +X.
6. **Output.**
   - (a) Raw path: write `(h01*65535).astype('<u2').tofile('bellsbend.raw')`, then use Import Raw with Depth 16-bit, Byte Order Windows (little-endian), and resolution 2049.
   - (b) Float path: write float32 and call `TerrainData.SetHeights(0,0,float[,])` from an editor script, with `heightmapResolution` = 2049 (or 4097, per claim 15).
7. **Water.** The DEM water surface is flat at about 117-118 m. Place a water plane at world Y = 117.3 - 115 = 2.3 m above terrain base (times any exaggeration), which is the 385 ft pool. Floodplain fields are only 3-10 m above it, so shoreline blending and z-fighting need care.

## Leads

- USACE Cheatham Lake Master Plan 2018 PDF (https://upload.wikimedia.org/wikipedia/commons/a/af/Cheatham_Lake_master_plan_-_USACE-p16021coll7-12386.pdf) has pool/flood stages. 2010 flood levels at Bells Bend would help set a "flooded" game state.
- TN_DavidsonCounty_D22 project metadata (lidar quality level, collection date, vertical accuracy). Look for the XML beside the TIFF under `.../Projects/TN_DavidsonCounty_D22/metadata/`.
- ImageServer `renderingRule` (Hillshade, Slope Map) can produce reference textures and slope masks directly for splat-map painting.
- USGS NHD flowlines for the exact river centreline. My river trace uses a 119 m threshold and is approximate.
- The Tennessee Conservationist Sept/Oct 2021 article "The Cultural Significance of Bells Bend" may describe the land forms.

## Could not find

- A published USGS or official figure for Bells Bend's min/max elevation. The figures above are my own measurements from USGS services.
- Vertical datum and hydro-flattening confirmation for this DEM (claim 17 is unverified).
- Lidar quality level and accuracy of the D22 project.
- The exact UTM bounds of each 1 m tile. I did not download them.
- Confirmation of the Unity heightmap row/axis orientation (step 5 is a belief).
- EPQS samples in open water. Both "river" points landed on bank pixels at about 122 m, so open-water values come only from the clip histogram.
