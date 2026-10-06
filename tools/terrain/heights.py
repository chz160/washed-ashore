"""Resample the real DEM onto the game grid and shape it for land / lake / north vista."""
import math

import numpy as np

import geo


def fill_nans(a, max_passes=64):
    """Fill NaN holes from valid 4-neighbours (simple iterative diffusion)."""
    holes = int(np.isnan(a).sum())
    passes = 0
    while np.isnan(a).any() and passes < max_passes:
        p = np.pad(a, 1, mode="edge")
        nb = np.stack([p[:-2, 1:-1], p[2:, 1:-1], p[1:-1, :-2], p[1:-1, 2:]])
        with np.errstate(all="ignore"):
            mean = np.nanmean(nb, axis=0)
        m = np.isnan(a) & ~np.isnan(mean)
        a[m] = mean[m]
        passes += 1
    if np.isnan(a).any():
        raise RuntimeError("DEM still has NaN after filling; widen the fetch window")
    return holes


def tent_prefilter(a):
    """[1,2,1]/4 separable low-pass before 2x decimation (anti-alias)."""
    b = a.copy()
    b[:, 1:-1] = (a[:, :-2] + 2 * a[:, 1:-1] + a[:, 2:]) * 0.25
    c = b.copy()
    c[1:-1] = (b[:-2] + 2 * b[1:-1] + b[2:]) * 0.25
    return c


def sample_bilinear(dem, e_min, n_min, e, n):
    """Bilinear DEM value at UTM (e, n); dem pixel (r, c) is at (e_min + c, n_min + r)."""
    fx, fy = np.asarray(e, float) - e_min, np.asarray(n, float) - n_min
    c0 = np.clip(np.floor(fx).astype(np.int64), 0, dem.shape[1] - 2)
    r0 = np.clip(np.floor(fy).astype(np.int64), 0, dem.shape[0] - 2)
    tx, ty = fx - c0, fy - r0
    v00, v01 = dem[r0, c0], dem[r0, c0 + 1]
    v10, v11 = dem[r0 + 1, c0], dem[r0 + 1, c0 + 1]
    return (v00 * (1 - tx) + v01 * tx) * (1 - ty) + (v10 * (1 - tx) + v11 * tx) * ty


def resample_to_grid(dem, e_min, n_min, frame, gx0, gz0, nx, nz, step, band=256):
    """DEM metres on the game grid (row 0 = south)."""
    out = np.empty((nz, nx), dtype=np.float32)
    gx = gx0 + np.arange(nx) * step
    e = frame.e_c + gx / frame.hs
    for j0 in range(0, nz, band):
        j1 = min(j0 + band, nz)
        gz = gz0 + np.arange(j0, j1) * step
        n = frame.n_c + gz / frame.hs
        ee, nn = np.meshgrid(e, n)
        out[j0:j1] = sample_bilinear(dem, e_min, n_min, ee, nn)
    return out


def chamfer_distance(water, max_dist):
    """Approx Euclidean distance (grid cells) from each water cell to the nearest land cell."""
    big = np.float32(max_dist + 2)
    d = np.where(water, big, np.float32(0)).astype(np.float32)
    s2 = np.float32(math.sqrt(2))
    for _ in range(int(math.ceil(max_dist)) + 1):
        p = np.pad(d, 1, mode="edge")
        cand = np.minimum.reduce([
            p[:-2, 1:-1] + 1, p[2:, 1:-1] + 1, p[1:-1, :-2] + 1, p[1:-1, 2:] + 1,
            p[:-2, :-2] + s2, p[:-2, 2:] + s2, p[2:, :-2] + s2, p[2:, 2:] + s2])
        nd = np.where(water, np.minimum(d, cand), 0)
        if np.array_equal(nd, d):
            break
        d = nd
    return d


def sliding_max(a, r):
    """Square-window max filter of radius r (separable, numpy only)."""
    out = a.copy()
    for axis in (0, 1):
        src = out.copy()
        for k in range(1, r + 1):
            for sgn in (1, -1):
                sh = np.roll(src, sgn * k, axis=axis)
                np.maximum(out, sh, out=out)
    return out


def smoothstep(t):
    t = np.clip(t, 0, 1)
    return t * t * (3 - 2 * t)


def north_mask(nx, nz, gx0, gz0, step, west, east):
    """True north of the straight line through the polygon's two north crossings."""
    gx = gx0 + np.arange(nx) * step
    slope = (east[1] - west[1]) / (east[0] - west[0])
    zline = west[1] + (gx - west[0]) * slope
    gz = gz0 + np.arange(nz) * step
    return gz[:, None] > zline[None, :]


def bed_profile(d, cfg):
    """Underwater bed at outward distance d: lip slope down to wading depth, then smoothstep
    to full lake depth by shoreRampMeters. Never above WaterLevelY (L3a)."""
    w = cfg.water_level_y
    tan_b = math.tan(math.radians(cfg.bankSlopeDeg))
    d_wade = (cfg.bankLipAboveWater + cfg.wadeDepth) / tan_b
    shelf = w + cfg.bankLipAboveWater - d * tan_b
    deep = w - cfg.wadeDepth - (cfg.lakeDepthBelowWater - cfg.wadeDepth) * smoothstep(
        (d - d_wade) / max(cfg.shoreRampMeters - d_wade, 1e-3))
    return np.minimum(np.where(d < d_wade, shelf, deep), w - WATER_EPS)


WATER_EPS = 0.01  # outside cells stay at least this far below WaterLevelY


def shape(dem_m, cfg, inside, north, river_carve_m, step, shore_dist, extra_water=None, bluffs=(), origin=(0, 0),
          line_dist=None):
    """Return (unity Y, water mask, outward distance for water cells, pre-bank Y, info). See README.

    shore_dist: distance to the polygon's shoreline segments (not the north line)."""
    w = cfg.water_level_y
    y = ((dem_m - cfg.datumMeters) * cfg.verticalScale).astype(np.float32)
    h = y.copy()  # pre-bank height
    north_river = north & (dem_m < cfg.poolElevationMeters + river_carve_m)
    water = (~inside & ~north) | north_river
    if extra_water is not None:
        water |= extra_water
    ramp = cfg.shoreRampMeters / step
    d = np.minimum(chamfer_distance(water, ramp) * step, shore_dist)
    floor = w + cfg.landMinAboveWater
    raised = inside & (y < floor)
    y_unc = np.where(inside, np.maximum(y, floor), y)
    # shoreline bank rule (brief rev 3 (c)): inland cap in the band, blended out on high banks
    band = inside & (shore_dist <= cfg.bankBandMeters)
    cap = w + cfg.bankLipAboveWater + shore_dist * math.tan(math.radians(cfg.bankSlopeDeg))
    y_cap = np.minimum(y_unc, cap)
    weight = 1 - smoothstep((h - (w + cfg.bankHighStart)) / (cfg.bankHighEnd - cfg.bankHighStart))
    # rev 7: steep faces (pre-bank game slope) and the bluff zones are exempt from the cap
    gz, gx = np.gradient(y_unc, step)
    pre_slope = np.degrees(np.arctan(np.hypot(gx, gz))).astype(np.float32)
    del gz, gx
    steep_w = 1 - smoothstep((pre_slope - cfg.bankSteepFadeStartDeg)
                             / (cfg.bankSteepExemptDeg - cfg.bankSteepFadeStartDeg))
    if cfg.bankSteepExemptHighBanksOnly:
        # only faces of banks taller than bankHighStart within the band radius count as faces
        tall = sliding_max(np.where(inside, h, -np.inf), int(round(cfg.bankBandMeters / step)))             > w + cfg.bankHighEnd
        steep_w = np.where(tall, steep_w, 1)
    weight = weight * steep_w
    bluff_zone = np.zeros_like(band)
    nz_, nx_ = y.shape
    for bx, bz in bluffs:
        gxs = origin[0] + np.arange(nx_) * step
        gzs = origin[1] + np.arange(nz_) * step
        dist = np.hypot(gxs[None, :] - bx, gzs[:, None] - bz)
        weight = weight * smoothstep((dist - cfg.bankBluffExclusionMeters) / cfg.bankBluffFadeMeters)
        bluff_zone |= dist <= cfg.bankBluffExclusionMeters
    if line_dist is not None:
        # fade the cap out toward the north line so the polygon edge meets the untouched neck smoothly
        weight = weight * smoothstep((line_dist - 2) / cfg.bankBandMeters)
    y = np.where(band, y_unc + (y_cap - y_unc) * weight, y_unc).astype(np.float32)
    lowered = band & (y < y_unc - 1e-4)
    raised &= ~lowered
    y = np.where(water, np.minimum(y, bed_profile(d, cfg)), y)
    info = {"bankLoweredCells": int(lowered.sum()),
            "bankLoweredOnPreSlopeOver40": int((lowered & (pre_slope > 40)).sum()),
            "bankLoweredWithinBluffExclusion": int((lowered & bluff_zone).sum()),
            "bankLoweredMaxShoreDist": round(float(shore_dist[lowered].max()) if lowered.any() else 0.0, 2),
            "insideCellsRaisedToMinLand": int(raised.sum()),
            "insideFractionRaisedToMinLand": round(float(raised.sum()) / max(int(inside.sum()), 1), 4),
            "insideDemMinMeters": round(float(dem_m[inside].min()), 2),
            "northDemMinMeters": round(float(dem_m[north].min()), 2),
            "northRiverCarvedCells": int(north_river.sum()),
            "northRiverCarveBelowDemMeters": cfg.poolElevationMeters + river_carve_m}
    return y, water, d, y_unc, info
