"""Bells Bend terrain pipeline: one command, rerunnable from scratch.

    /c/Python313/python.exe tools/terrain/build_terrain.py   # Bash; `py ...` in PowerShell. Fetch (or reuse cache) + build + report
    /c/Python313/python.exe tools/terrain/build_terrain.py --refetch  # ignore the DEM cache

Reads Assets/World/MapConfig.asset; writes Data/terrain/build/ (RAW tiles,
manifest.json, map_vectors.json, l2_samples.json, report.json, overlay.png).
"""
import argparse
import hashlib
import json
import math
import sys
import time
from pathlib import Path

import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parent))
import fetch_dem  # noqa: E402
import geo  # noqa: E402
import heights  # noqa: E402
import mapconfig  # noqa: E402
import northcut  # noqa: E402
import report  # noqa: E402
import vectors  # noqa: E402

ROOT = Path(__file__).resolve().parents[2]
CACHE = ROOT / "Data/terrain/cache"
RIVER_CARVE_M = 1.5  # north of the line, DEM below pool + this is treated as river (hydro-flattened)
FETCH_PAD_M = 4
LINE_MOVE_TOLERANCE_M = 1.0  # config vs data north line; the data line itself tilts under 2 m


def log(msg):
    print(f"[{time.strftime('%H:%M:%S')}] {msg}", flush=True)


def check_step_offset(cfg, scene=ROOT / "Assets/Scenes/World.unity"):
    """Brief rev 14: bankLipAboveWater + 0.01 (outside clamp) must stay below the player's
    CharacterController stepOffset, or the lip stops being a step. Warns; never changes output."""
    if not scene.exists():
        log(f"WARNING: {scene} not found; lip/stepOffset constraint not checked")
        return
    import re
    offsets = [float(v) for v in re.findall(r"m_StepOffset: ([0-9.]+)", scene.read_text(encoding="utf-8"))]
    lip = cfg.bankLipAboveWater + heights.WATER_EPS
    if not offsets:
        log("WARNING: no CharacterController stepOffset in World.unity; lip constraint not checked")
    elif lip >= min(offsets):
        log(f"WARNING: lip step {lip:.2f} m >= CharacterController stepOffset {min(offsets)} m (brief rev 14)")
    else:
        log(f"lip step {lip:.2f} m < stepOffset {min(offsets)} m (brief rev 14 constraint OK)")


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--refetch", action="store_true", help="redownload DEM chunks even if cached")
    ap.add_argument("--config", default=str(mapconfig.DEFAULT_ASSET), help="MapConfig asset path")
    ap.add_argument("--tag", default="dev", help="build tag written to manifest.json and report.json")
    args = ap.parse_args()
    t_start = time.time()

    cfg = mapconfig.load(ROOT, args.config)
    check_step_offset(cfg)
    out_dir = ROOT / Path(cfg.manifestPath).parent
    out_dir.mkdir(parents=True, exist_ok=True)
    log(f"MapConfig {cfg.path}: hs={cfg.horizontalScale} vs={cfg.verticalScale} datum={cfg.datumMeters} "
        f"WaterLevelY={cfg.water_level_y:.3f} northLineZ={cfg.northLineZ}")

    shore = vectors.load_shoreline(ROOT)
    frame, area_real = vectors.make_frame(shore, cfg.horizontalScale)
    ring_x, ring_z = frame.to_game(shore["ring_lonlat"][:, 1], shore["ring_lonlat"][:, 0])
    bank_x, bank_z = frame.to_game(shore["bank_lonlat"][:, 1], shore["bank_lonlat"][:, 0])
    west, east = (bank_x[0], bank_z[0]), (bank_x[-1], bank_z[-1])
    north_z_data = (west[1] + east[1]) / 2
    log(f"origin UTM16N E={frame.e_c} N={frame.n_c}; polygon {area_real / 1e6:.2f} km2 real; "
        f"data north line z={north_z_data:.2f}")
    line_moved = bool(abs(cfg.northLineZ - north_z_data) > LINE_MOVE_TOLERANCE_M)
    if line_moved:
        log(f"WARNING: MapConfig.northLineZ {cfg.northLineZ} differs from the data north line {north_z_data:.2f} "
            f"by {cfg.northLineZ - north_z_data:+.1f} m. KNOWN LIMITATION (ruling/bells-bend-line-move-terrain): "
            "the neck crossings are NOT re-derived for the moved line, so the far-bank lake can cut beside the "
            "neck (up to ~22.7 m at +100 m). Follow-up required before shipping a moved line.")

    # --- game grid: tiles covering polygon + margin, and >= northVista beyond the config line
    step = cfg.tileSizeMeters / (cfg.heightmapResolution - 1)
    xmin, xmax = ring_x.min() - cfg.marginMeters, ring_x.max() + cfg.marginMeters
    zmin = ring_z.min() - cfg.marginMeters
    zmax = max(ring_z.max() + cfg.marginMeters, cfg.northLineZ + cfg.northVistaMinMeters,
               north_z_data + cfg.northVistaMinMeters)
    T = cfg.tileSizeMeters
    tiles_x, tiles_z = math.ceil((xmax - xmin) / T), math.ceil((zmax - zmin) / T)
    gx0 = math.floor(xmin - (tiles_x * T - (xmax - xmin)) / 2)
    gz0 = math.floor(zmin - (tiles_z * T - (zmax - zmin)) / 2)
    nx, nz = tiles_x * (cfg.heightmapResolution - 1) + 1, tiles_z * (cfg.heightmapResolution - 1) + 1
    log(f"grid {tiles_x}x{tiles_z} tiles of {T} m, origin ({gx0}, {gz0}), {nx}x{nz} samples at {step} m")

    # --- DEM window in UTM (1 m pixels on whole metres)
    e_min = int(math.floor(frame.e_c + gx0 / frame.hs)) - FETCH_PAD_M
    n_min = int(math.floor(frame.n_c + gz0 / frame.hs)) - FETCH_PAD_M
    width = int(math.ceil((nx - 1) * step / frame.hs)) + 2 * FETCH_PAD_M + 1
    height = int(math.ceil((nz - 1) * step / frame.hs)) + 2 * FETCH_PAD_M + 1
    dem, fetch_stats = fetch_dem.load_mosaic(e_min, n_min, width, height, CACHE, log, args.refetch)
    holes = heights.fill_nans(dem)
    log(f"DEM range {np.nanmin(dem):.1f}..{np.nanmax(dem):.1f} m, {holes} nodata px filled")
    if 1 / frame.hs >= 1.5:
        dem_f = heights.tent_prefilter(dem)
    else:
        dem_f = dem
    dem_game = heights.resample_to_grid(dem_f, e_min, n_min, frame, gx0, gz0, nx, nz, step)
    del dem_f

    # --- masks and shaping
    inside = geo.rasterize_polygon(ring_x, ring_z, gx0, gz0, nx, nz, step)
    north = heights.north_mask(nx, nz, gx0, gz0, step, west, east)
    inside &= ~north
    shore_dist = geo.distance_to_polyline(bank_x, bank_z, gx0, gz0, nx, nz, 400.0, step)
    gxs, gzs = gx0 + np.arange(nx) * step, gz0 + np.arange(nz) * step
    zline = west[1] + (gxs - west[0]) * (east[1] - west[1]) / (east[0] - west[0])
    line_dist = np.maximum(zline[None, :] - gzs[:, None], 0).astype(np.float32)  # metres south of the data line
    opp_lake = off = outside_neck = taper = None
    if cfg.oppositeBankEnabled:
        opp_lake, off, outside_neck, taper = northcut.lake_mask(cfg, nx, nz, gx0, gz0, step, north, west, east)
    vec = vectors.export(ROOT, out_dir, frame, shore, cfg, north_z_data)
    landmark_xz = {lm["id"]: (lm["x"], lm["z"]) for lm in vec["landmarks"]}
    bluffs = {k: landmark_xz[k] for k in ("BuzzardBluff", "McCordBluff")}
    y, water, land_dist, pre_bank, shape_info = heights.shape(dem_game, cfg, inside, north, RIVER_CARVE_M, step,
                                                              shore_dist, opp_lake, bluffs.values(), (gx0, gz0),
                                                              line_dist)
    if cfg.oppositeBankEnabled:
        y, cap_info = northcut.cap_land(y, water, cfg, north, outside_neck, step, gz0, taper)
        pre_cap = cap_info.pop("_preCap")
        shape_info.update(cap_info, oppositeBankLakeCells=int(opp_lake.sum()))
    log(f"shaped: {shape_info}")

    # --- quantize: one shared base/height for every tile (seamless)
    base = math.floor((cfg.water_level_y - cfg.lakeDepthBelowWater - 1) * 10) / 10
    top = math.ceil((float(y.max()) + 1) * 10) / 10
    size_y = top - base
    q = np.clip(np.round((y - base) / size_y * 65535), 0, 65535).astype("<u2")
    yq = (q.astype(np.float32) / 65535 * size_y + base)  # what Unity will see

    res = cfg.heightmapResolution
    tiles = []
    for tj in range(tiles_z):
        for ti in range(tiles_x):
            block = q[tj * (res - 1):tj * (res - 1) + res, ti * (res - 1):ti * (res - 1) + res]
            name = f"tile_x{ti}_z{tj}.raw"
            np.ascontiguousarray(block).tofile(out_dir / name)
            tiles.append({"file": name, "ix": ti, "iz": tj, "position": [gx0 + ti * T, base, gz0 + tj * T]})
    dem_cm = np.clip(np.round(dem_game * 100), 0, 65535).astype("<u2")
    for t in tiles:
        ti, tj = t["ix"], t["iz"]
        t["demFile"] = f"dem_x{ti}_z{tj}.raw"
        np.ascontiguousarray(dem_cm[tj * (res - 1):tj * (res - 1) + res, ti * (res - 1):ti * (res - 1) + res]
                             ).tofile(out_dir / t["demFile"])
    del dem_cm
    log(f"wrote {len(tiles)} RAW tiles ({res}x{res} u16 LE) to {out_dir}")

    # --- evidence
    l1_keep_line = (west[0], west[1] - 20), (east[0], east[1] - 20)
    north_l1 = heights.north_mask(nx, nz, gx0, gz0, step, *l1_keep_line)
    l1 = report.l1_shoreline(yq, cfg, gx0, gz0, step, north_l1, bank_x, bank_z)
    l3 = report.l3_outside(yq, cfg, inside, north, shore_dist, land_dist)
    l4 = report.l4_slopes(yq, inside, step, cfg, bluffs, gx0, gz0)
    cell = lambda x, z: (int(round((z - gz0) / step)), int(round((x - gx0) / step)))  # noqa: E731
    l2 = report.l2_table(vectors.sample_points(), frame, cfg, yq, gx0, gz0, step,
                         lambda x, z: bool(inside[cell(x, z)]), lambda x, z: bool(north[cell(x, z)]),
                         dem, e_min, n_min, (ring_x, ring_z))
    (out_dir / "l2_samples.json").write_text(json.dumps({
        "rule": json.loads((Path(__file__).parent / "l2_sample_points.json").read_text())["rule"]
                + " Compute expectedY from the live MapConfig, not previewExpectedY.",
        "previewConfig": {"datumMeters": cfg.datumMeters, "verticalScale": cfg.verticalScale},
        "points": l2}, indent=1), encoding="utf-8")
    report.overlay_png(out_dir / "overlay.png", yq, cfg, step, gx0, gz0, ring_x, ring_z, cfg.northLineZ)

    cfg_hash = hashlib.sha256(Path(cfg.path).read_bytes()).hexdigest()[:16]
    manifest = {
        "generator": "tools/terrain/build_terrain.py", "buildTag": args.tag, "builtUtc": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
        "mapConfig": cfg.as_dict(), "mapConfigSha256_16": cfg_hash,
        "frame": {"utmEpsg": 26916, "originE": frame.e_c, "originN": frame.n_c, "origin": "playable polygon centroid",
                  "axes": "+X east, +Z north"},
        "raw": {"format": "uint16 little-endian", "rowOrder": "row 0 = south (min Z), col 0 = west (min X)",
                "demFiles": "dem_x{i}_z{j}.raw: unshaped real DEM (before clamp/lake), uint16 LE centimetres, same layout",
                "unityImport": "TerrainData.SetHeights(0, 0, float[row, col] = u16 / 65535f): "
                               "SetHeights' first index is Z, so row 0 lands at the tile's min Z"},
        "tileSize": T, "heightmapResolution": res, "sampleSpacing": step,
        "tilesX": tiles_x, "tilesZ": tiles_z, "gridOrigin": [gx0, gz0],
        "terrainBaseY": base, "terrainHeight": round(size_y, 3), "heightRangeY": [round(float(yq.min()), 3),
                                                                                   round(float(yq.max()), 3)],
        "WaterLevelY": round(cfg.water_level_y, 4), "northLineZData": round(north_z_data, 2),
        "tiles": tiles, "dem": {"source": "USGS 3DEP ImageServer exportImage, 1 m, EPSG:26916, bilinear",
                                "window": [e_min, n_min, width, height], **fetch_stats, "nodataFilled": holes},
        "shaping": {**shape_info, "shoreRampMeters": cfg.shoreRampMeters},
    }
    (out_dir / "manifest.json").write_text(json.dumps(manifest, indent=1), encoding="utf-8")
    banks, stations = report.bank_stations(yq, pre_bank, cfg, gx0, gz0, step, inside, bank_x, bank_z,
                                           north_z_data, list(bluffs.values()))
    banks.update(loweredCells=shape_info["bankLoweredCells"], loweredMaxShoreDist=shape_info["bankLoweredMaxShoreDist"],
                 loweredOnPreSlopeOver40=shape_info["bankLoweredOnPreSlopeOver40"],
                 loweredWithinBluffExclusion=shape_info["bankLoweredWithinBluffExclusion"])
    (out_dir / "bank_stations.json").write_text(json.dumps(stations), encoding="utf-8")
    cond3 = report.condition3(yq, cfg, inside, shore_dist, gx0, gz0, step)
    north_cut = {"enabled": bool(cfg.oppositeBankEnabled), "seed": cfg.oppositeBankSeed}
    if cfg.oppositeBankEnabled:
        north_cut.update(northcut.acceptance(yq, water, cfg, north, outside_neck, off, step, *cap_info["window"],
                                             pre_cap))
    lake_sha = northcut.lake_hash(yq, cfg.water_level_y)
    rep = {"northLineMoved": {"moved": line_moved, "configZ": cfg.northLineZ, "dataZ": round(north_z_data, 2),
                              "limitation": "neck crossings not re-derived for a moved line; far-bank cut beside the "
                                            "neck up to ~22.7 m at +100 m; follow-up required before shipping "
                                            "(ruling/bells-bend-line-move-terrain)" if line_moved else None},
           "buildTag": args.tag, "lakeMaskSha256": lake_sha, "northCut": north_cut, "L1": l1, "L3": l3, "L4preview": l4, "banks": banks, "condition3": cond3,
           "floor": {k: shape_info[k] for k in ("insideCellsRaisedToMinLand", "insideFractionRaisedToMinLand")},
           "L2": l2, "seconds": round(time.time() - t_start, 1)}
    (out_dir / "report.json").write_text(json.dumps(rep, indent=1), encoding="utf-8")
    log(f"L1 mean shoreline deviation {l1['meanDeviation']} m (p95 {l1['p95Deviation']}, max {l1['maxDeviation']})")
    log(f"L3 max outside height {l3['maxHeightOutside']} (WaterLevelY {l3['WaterLevelY']}), "
        f"max within 150 m {l3['maxHeightOutsideWithin150m']}, beyond ramp {l3['maxHeightOutsideBeyondRamp']} "
        f"(threshold {l3['threshold']})")
    log(f"L4 preview: {l4['fractionAtOrBelow40'] * 100:.1f}% of land <= 40 deg; bluffs {l4['bluffs']}")
    log(f"banks: {banks}")
    log(f"north cut: {north_cut}; lake mask sha256 {lake_sha[:16]}")
    log(f"condition 3: {cond3}")
    log(f"done in {rep['seconds']} s -> {out_dir}")


if __name__ == "__main__":
    main()
