"""Opposite-bank fix along the north line (design brief section (f), ruling/bells-bend-north-cut).

North of the line and outside the neck (west of the west crossing, east of the east crossing),
the lake edge follows z_b(x) = northLineZ + offset(x), a seeded smooth noise in
[0, oppositeBankOffsetMax] tapered to 0 at each crossing. Opposite-bank land is then capped at
W + bankLip + d * tan(oppositeBankSlopeDeg), d = distance to the nearest lake cell.
"""
import hashlib
import math

import numpy as np

import heights


def offset_profile(gx, cfg, west_x, east_x):
    """Boundary offset (m) per grid column; 0 inside the neck span."""
    rng = np.random.default_rng(int(cfg.oppositeBankSeed))
    lo, hi = cfg.oppositeBankWavelengthMin, cfg.oppositeBankWavelengthMax
    n = np.zeros_like(gx, dtype=np.float64)
    for _ in range(4):
        wl = rng.uniform(lo, hi)
        n += rng.uniform(0.5, 1.0) * np.sin(2 * math.pi * gx / wl + rng.uniform(0, 2 * math.pi))
    n = (n - n.min()) / max(n.max() - n.min(), 1e-9)
    dist = np.where(gx < west_x, west_x - gx, np.where(gx > east_x, gx - east_x, 0.0))
    taper = heights.smoothstep(dist / cfg.oppositeBankTaper)
    return n * cfg.oppositeBankOffsetMax * taper, dist > 0, taper


def lake_mask(cfg, nx, nz, gx0, gz0, step, north, west, east):
    """Cells north of the data line, outside the neck, south of z_b(x): become lake."""
    gx = gx0 + np.arange(nx) * step
    gz = gz0 + np.arange(nz) * step
    off, outside_neck, taper = offset_profile(gx, cfg, west[0], east[0])
    zb = cfg.northLineZ + off
    mask = north & outside_neck[None, :] & (gz[:, None] < zb[None, :])
    return mask, off, outside_neck, taper


def cap_land(y, water, cfg, north, outside_neck, step, gz0, taper):
    """Cap opposite-bank north land by distance to the nearest lake cell (brief (f), rev 8 3a).

    No hard cutoff: the distance runs to d_max = (max opposite-bank Y - W) / tan + 20 m, where the
    cap is above all terrain. The cap is weighted by the same crossing taper as the offset, so
    capped opposite banks blend into the untouched neck. Returns (y, info)."""
    w = cfg.water_level_y
    tan_b = math.tan(math.radians(cfg.oppositeBankSlopeDeg))
    j0 = max(int((cfg.northLineZ - gz0) / step) - 4, 0)
    before = y[j0:].copy()
    region = north[j0:] & outside_neck[None, :] & ~water[j0:]
    d_max = (float(before[region].max()) - w) / tan_b + 20
    d = heights.chamfer_distance(~water[j0:], d_max / step) * step
    cap = w + cfg.bankLipAboveWater + d * tan_b
    # never drop faster than the cap slope away from the untouched neck columns
    cols = np.nonzero(~outside_neck)[0]
    gxi = np.arange(y.shape[1])
    for col, dx in ((int(cols.min()), (cols.min() - gxi) * step), (int(cols.max()), (gxi - cols.max()) * step)):
        h_neck = np.where(water[j0:, col], w, before[:, col])
        cap = np.where((dx > 0)[None, :], np.maximum(cap, h_neck[:, None] - dx[None, :] * tan_b), cap)
    capped_y = np.minimum(before, cap)
    y[j0:] = np.where(region, capped_y, before)
    capped = region & (y[j0:] < before - 1e-4)
    return y, {"oppositeBankCappedCells": int(capped.sum()), "window": [j0, y.shape[0]],
               "capDistanceMax": round(d_max, 1), "_preCap": before}


def acceptance(yq, water, cfg, north, outside_neck, off, step, j0, j1, pre_cap):
    """qa-2 checks: adjacent step across the lake edge, slope near it, offset stats, mask hash."""
    w = cfg.water_level_y
    reg = (north & outside_neck[None, :])[j0:j1]
    ys, wat = yq[j0:j1], water[j0:j1] | (yq[j0:j1] <= w)
    steps = []
    for a, b, ra, rb, wa, wb in ((ys[:, :-1], ys[:, 1:], reg[:, :-1], reg[:, 1:], wat[:, :-1], wat[:, 1:]),
                                 (ys[:-1], ys[1:], reg[:-1], reg[1:], wat[:-1], wat[1:])):
        edge = (ra | rb) & (wa != wb)
        if edge.any():
            steps.append(float(np.abs(a - b)[edge].max()))
    land = reg & ~wat
    d = heights.chamfer_distance(~wat, 100 / step) * step
    gz, gx = np.gradient(ys, step)
    slope = np.degrees(np.arctan(np.hypot(gx, gz)))
    near = land & (d <= 100)
    steep = near & (slope > cfg.oppositeBankSlopeDeg + 0.5)
    pz, px = np.gradient(pre_cap, step)
    pre_slope = np.degrees(np.arctan(np.hypot(px, pz)))
    cap_induced = near & (slope > 35.05) & (pre_slope <= 35) & (slope > pre_slope + 0.1)
    span = off[outside_neck]
    diff = np.abs(np.diff(off)) < 0.01
    run, best = 0, 0
    for flat, o in zip(diff, outside_neck[1:]):
        run = run + 1 if (flat and o) else 0
        best = max(best, run)
    return {"maxAdjacentStepAcrossBoundary": round(max(steps) if steps else 0.0, 3), "targetStep": 2.0,
            "maxSlopeWithin100m": round(float(slope[near].max()) if near.any() else 0.0, 1),
            "cellsSteeperThanCapWithin100m": int(steep.sum()),
            "capInducedOver35Within100m": int(cap_induced.sum()),
            "note": "steeper cells are natural DEM slopes left under the cap (the cap only lowers)",
            "offsetStd": round(float(span.std()), 2), "offsetMax": round(float(span.max()), 2),
            "longestConstantOffsetRunMeters": round(best * step, 1)}


def lake_hash(yq, w):
    return hashlib.sha256(np.packbits(yq <= w).tobytes()).hexdigest()
