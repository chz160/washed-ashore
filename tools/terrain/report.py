"""Build-time evidence: L1 shoreline deviation, L2 sample table, L3 outside scan,
L4 slope histogram preview, and a top-down overlay PNG."""
import struct
import zlib

import numpy as np

import geo
import heights


def contour_points(y, level, gx0, gz0, step, keep):
    """Zero crossings of (y - level) along grid edges, as (x, z) arrays, filtered by keep[row, col]."""
    s = y - level
    pts = []
    h = (s[:, :-1] > 0) != (s[:, 1:] > 0)
    j, i = np.nonzero(h & keep[:, :-1])
    t = s[j, i] / (s[j, i] - s[j, i + 1])
    pts.append((gx0 + (i + t) * step, gz0 + j * step))
    v = (s[:-1] > 0) != (s[1:] > 0)
    j, i = np.nonzero(v & keep[:-1])
    t = s[j, i] / (s[j, i] - s[j + 1, i])
    pts.append((gx0 + i * step, gz0 + (j + t) * step))
    return np.concatenate([p[0] for p in pts]), np.concatenate([p[1] for p in pts])


def _nearest(ax, az, bx, bz, chunk=512):
    out = np.empty(len(ax))
    for k in range(0, len(ax), chunk):
        dx = ax[k:k + chunk, None] - bx[None, :]
        dz = az[k:k + chunk, None] - bz[None, :]
        out[k:k + chunk] = np.sqrt((dx * dx + dz * dz).min(axis=1))
    return out


def densify(x, z, spacing):
    px, pz = [x[0]], [z[0]]
    for k in range(len(x) - 1):
        n = max(int(np.hypot(x[k + 1] - x[k], z[k + 1] - z[k]) / spacing), 1)
        t = np.arange(1, n + 1) / n
        px.extend(x[k] + (x[k + 1] - x[k]) * t)
        pz.extend(z[k] + (z[k + 1] - z[k]) * t)
    return np.array(px), np.array(pz)


def l1_shoreline(y, cfg, gx0, gz0, step, north, bank_x, bank_z):
    """Mean distance between the built WaterLevelY contour (south of the line) and the OSM bank."""
    keep = ~north
    cx, cz = contour_points(y, cfg.water_level_y, gx0, gz0, step, keep)
    bx, bz = densify(bank_x, bank_z, 2.0)
    ci = np.clip(np.round((bx - gx0) / step).astype(int), 0, keep.shape[1] - 1)
    cj = np.clip(np.round((bz - gz0) / step).astype(int), 0, keep.shape[0] - 1)
    sel = keep[cj, ci]  # bank samples in the line band have no contour to match
    bx, bz = bx[sel], bz[sel]
    bank_to_terrain = _nearest(bx, bz, cx, cz)
    terrain_to_bank = _nearest(cx, cz, bx, bz)
    return {"meanDeviation": round(float(bank_to_terrain.mean()), 3),
            "p95Deviation": round(float(np.percentile(bank_to_terrain, 95)), 3),
            "maxDeviation": round(float(bank_to_terrain.max()), 3),
            "terrainToBankMean": round(float(terrain_to_bank.mean()), 3),
            "terrainToBankMax": round(float(terrain_to_bank.max()), 3),
            "bankSamples": int(len(bx)), "contourPoints": int(len(cx)), "passThreshold": 15.0}


def l3_outside(y, cfg, inside, north, shore_dist, land_dist):
    w = cfg.water_level_y
    out = ~inside & ~north
    near = out & (shore_dist <= 150)
    thresh = w - 8
    above = near & (y > thresh)
    return {"WaterLevelY": round(w, 3), "threshold": round(thresh, 3),
            "maxHeightOutside": round(float(y[out].max()), 3),
            "maxHeightOutsideWithin150m": round(float(y[near].max()), 3),
            "maxHeightOutsideBeyondRamp": round(float(y[out & (land_dist >= cfg.shoreRampMeters)].max()), 3),
            "maxLandDistanceAboveThreshold": round(float(land_dist[out & (y > thresh)].max()) if (out & (y > thresh)).any() else 0.0, 2),
            "fractionWithin150mAtOrBelowThreshold": round(1 - float(above.sum()) / max(int(near.sum()), 1), 5),
            "maxShoreDistanceAboveThreshold": round(float(shore_dist[above].max()) if above.any() else 0.0, 2),
            "anyOutsideAboveWaterLevel": bool((y[out] > w).any())}


def slope_degrees(y, step):
    gz, gx = np.gradient(y, step)
    return np.degrees(np.arctan(np.hypot(gx, gz)))


def l4_slopes(y, inside, step, cfg, bluffs, gx0, gz0, radius=40.0):
    s = slope_degrees(y, step)
    land = inside & (y > cfg.water_level_y)
    sl = s[land]
    land_disc = land
    edges = [0, 10, 20, 30, 35, 40, 45, 50, 60, 90]
    hist, _ = np.histogram(sl, bins=edges)
    res = {"landCells": int(land.sum()), "fractionAtOrBelow40": round(float((sl <= 40).mean()), 4),
           "median": round(float(np.median(sl)), 2), "p90": round(float(np.percentile(sl, 90)), 2),
           "p99": round(float(np.percentile(sl, 99)), 2),
           "histogram": {f"{a}-{b}": round(float(c) / len(sl), 4) for a, b, c in zip(edges, edges[1:], hist)},
           "bluffs": {}}
    for name, (bx, bz) in bluffs.items():
        i0, j0 = int(round((bx - gx0) / step)), int(round((bz - gz0) / step))
        r = int(radius / step)
        win = s[max(j0 - r, 0):j0 + r + 1, max(i0 - r, 0):i0 + r + 1]
        r60 = int(60 / step)
        jj, ii = np.mgrid[-r60:r60 + 1, -r60:r60 + 1]
        disc = (jj * jj + ii * ii) * step * step <= 60 * 60
        sub = s[j0 - r60:j0 + r60 + 1, i0 - r60:i0 + r60 + 1]
        lsub = land_disc[j0 - r60:j0 + r60 + 1, i0 - r60:i0 + r60 + 1]
        res["bluffs"][name] = {"maxSlope": round(float(win.max()), 1),
                               "fractionOver40": round(float((win > 40).mean()), 3), "radius": radius,
                               "areaOver40m2Within60m": round(float(((sub > 40) & disc).sum()) * step * step, 1),
                               "landAreaOver40m2Within60m": round(float(((sub > 40) & disc & lsub).sum()) * step * step,
                                                                  1)}
    return res


def l2_table(points, frame, cfg, y, gx0, gz0, step, inside_fn, north_fn, dem_mosaic, e_min, n_min, ring):
    rows = []
    nz, nx = y.shape
    rx, rz = np.append(ring[0], ring[0][0]), np.append(ring[1], ring[1][0])
    floor = cfg.water_level_y + cfg.landMinAboveWater
    for p in points:
        x, z = (float(v) for v in frame.to_game(p["lat"], p["lon"]))
        e, n = frame.game_to_utm(x, z)
        fx, fz = (x - gx0) / step, (z - gz0) / step
        in_grid = 0 <= fx < nx - 1 and 0 <= fz < nz - 1
        row = {"label": p["label"], "grade": p.get("grade", "L2"), "lat": p["lat"], "lon": p["lon"],
               "x": round(x, 2), "z": round(z, 2), "demMeters": p["demMeters"],
               "distToPolygonEdge": round(geo.point_segment_distance(x, z, rx, rz), 1)}
        if not in_grid:
            row.update(region="outside_terrain", note="beyond the built footprint; not testable")
            rows.append(row)
            continue
        region = "north_vista" if north_fn(x, z) else ("land" if inside_fn(x, z) else "lake")
        actual = float(heights.sample_bilinear(y, 0, 0, fx, fz))
        mosaic = float(heights.sample_bilinear(dem_mosaic, e_min, n_min, e, n))
        exp = cfg.dem_to_unity_y(p["demMeters"])
        if region == "land":
            exp = max(exp, floor)
        row.update(region=region, pipelineY=round(actual, 2), fetchedDemMeters=round(mosaic, 2),
                   demMinusEpqs=round(mosaic - p["demMeters"], 2))
        if region == "lake":
            row.update(l3Threshold=round(cfg.water_level_y - 8, 2), l3Pass=actual <= cfg.water_level_y - 8)
        else:
            row.update(previewExpectedY=round(exp, 2), previewDelta=round(actual - exp, 2),
                       previewPass=abs(actual - exp) <= 3)
        rows.append(row)
    return rows


def bank_stations(yq, pre, cfg, gx0, gz0, step, inside, bank_x, bank_z, north_z, bluffs, spacing=5.0, dt=0.25):
    """Brief bank acceptance: stations every 5 m, profile along the inward normal from 10 m outside
    to bankBandMeters inside at 0.25 m steps; max slope over segments whose midpoint height is in
    [W - wadeDepth, W + landMinAboveWater]. Stations at the north-line seam or in a bluff zone are excluded."""
    w = cfg.water_level_y
    sx, sz = densify(bank_x, bank_z, spacing)
    tx, tz = np.gradient(sx), np.gradient(sz)
    norm = np.hypot(tx, tz) + 1e-9
    nx_, nz_ = -tz / norm, tx / norm
    to_grid = lambda x, z: ((x - gx0) / step, (z - gz0) / step)  # noqa: E731
    gi, gj = to_grid(sx + 3 * nx_, sz + 3 * nz_)
    gi = np.clip(np.round(gi).astype(int), 0, inside.shape[1] - 1)
    gj = np.clip(np.round(gj).astype(int), 0, inside.shape[0] - 1)
    flip = ~inside[gj, gi]
    nx_[flip], nz_[flip] = -nx_[flip], -nz_[flip]
    t = np.arange(-10.0, cfg.bankBandMeters + 1e-6, dt)
    px, pz = sx[:, None] + t[None, :] * nx_[:, None], sz[:, None] + t[None, :] * nz_[:, None]
    fi, fj = to_grid(px, pz)
    prof = heights.sample_bilinear(yq, 0, 0, fi, fj)
    pre_prof = heights.sample_bilinear(pre, 0, 0, fi, fj)
    pre_max = np.where(t[None, :] >= 0, pre_prof, -np.inf).max(axis=1)
    a, b = prof[:, :-1], prof[:, 1:]
    mid = (a + b) / 2
    in_rng = (mid <= w + cfg.landMinAboveWater) & (mid >= w - cfg.wadeDepth)
    seg = np.degrees(np.arctan(np.abs(b - a) / dt))
    max_slope = np.where(in_rng, seg, 0).max(axis=1)
    reason = np.full(len(sx), "", dtype=object)
    reason[sz >= north_z - cfg.bankBandMeters - 10] = "excluded: north-line seam"
    for bx, bz in bluffs:
        reason[(np.hypot(sx - bx, sz - bz) <= cfg.bankBluffExclusionMeters) & (reason == "")] = "excluded: bluff"
    scope = reason == ""
    low = (pre_max <= w + cfg.bankHighStart) & scope
    climb = max_slope <= 35
    table = [{"i": int(k), "x": round(float(sx[k]), 1), "z": round(float(sz[k]), 1),
              "preBankMaxY": round(float(pre_max[k]), 2), "maxSlope": round(float(max_slope[k]), 1),
              "lowBank": bool(low[k]), "excluded": reason[k] or None} for k in range(len(sx))]
    over = [r for r in table if r["lowBank"] and r["maxSlope"] > 35]
    return {"stations": int(len(sx)), "spacing": spacing, "profileStep": dt, "lowBankStations": int(low.sum()),
            "lowBankAtOrBelow35Pct": round(100 * float(climb[low].mean()) if low.any() else 100.0, 2),
            "lowBankOver35": over,
            "allStationsClimbablePct": round(100 * float(climb[scope].mean()), 2),
            "excludedNorthLineSeam": int((reason == "excluded: north-line seam").sum()),
            "excludedBluff": int((reason == "excluded: bluff").sum()),
            "climbableRule": "max slope <= 35 deg over 0.25 m segments whose midpoint height is within "
                             "[W - wadeDepth, W + landMinAboveWater]",
            "lowBankMaxSlope": round(float(max_slope[low].max()) if low.any() else 0.0, 1)}, table


def condition3(yq, cfg, inside, shore_dist, gx0, gz0, step, inland=20.0):
    """Greenlight condition 3: in-polygon samples more than 20 m inland are >= W + 1.5."""
    m = inside & (shore_dist > inland)
    vals = np.where(m, yq, np.inf)
    j, i = np.unravel_index(int(np.argmin(vals)), vals.shape)
    floor = cfg.water_level_y + cfg.landMinAboveWater
    return {"minY": round(float(vals[j, i]), 3), "at": [float(gx0 + i * step), float(gz0 + j * step)], "floor": round(floor, 3),
            "pass": bool(vals[j, i] >= floor - 0.005), "inlandMeters": inland}


def _png(path, rgb):
    h, w, _ = rgb.shape
    raw = b"".join(b"\x00" + rgb[r].tobytes() for r in range(h))
    def chunk(tag, data):
        c = struct.pack(">I", len(data)) + tag + data
        return c + struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF)
    with open(path, "wb") as fh:
        fh.write(b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 2, 0, 0, 0))
                 + chunk(b"IDAT", zlib.compress(raw, 6)) + chunk(b"IEND", b""))


def overlay_png(path, y, cfg, step, gx0, gz0, ring_x, ring_z, cfg_line_z, every=4):
    """Top-down preview (north up): hillshaded land, blue water, red OSM polygon, yellow config line."""
    ys = y[::every, ::every]
    gz, gx = np.gradient(ys, step * every)
    shade = np.clip(0.55 + 0.45 * (-gx * 0.7 + gz * 0.7) / np.sqrt(1 + gx * gx + gz * gz), 0, 1)
    lo, hi = cfg.water_level_y, float(ys.max())
    t = np.clip((ys - lo) / max(hi - lo, 1), 0, 1)
    rgb = np.stack([(90 + 120 * t) * shade, (120 + 80 * t) * shade, (70 + 60 * t) * shade], -1)
    water = ys <= cfg.water_level_y
    rgb[water] = [40, 80, 150]
    rgb = rgb.astype(np.uint8)
    h, w = ys.shape
    px, pz = densify(np.append(ring_x, ring_x[0]), np.append(ring_z, ring_z[0]), step * every / 2)
    ci = np.clip(((px - gx0) / (step * every)).round().astype(int), 0, w - 1)
    cj = np.clip(((pz - gz0) / (step * every)).round().astype(int), 0, h - 1)
    rgb[cj, ci] = [230, 30, 30]
    lj = int(round((cfg_line_z - gz0) / (step * every)))
    if 0 <= lj < h:
        rgb[lj, :] = [250, 220, 40]
    _png(path, np.ascontiguousarray(rgb[::-1]))
