"""Geodesy and 2D geometry helpers (numpy only, no GDAL/pyproj).

UTM forward projection uses the Krueger series on the GRS80 ellipsoid
(EPSG:26916, NAD83 / UTM 16N); error is well under 1 mm in the zone.
OSM coordinates are WGS84; the NAD83/WGS84 offset here is about 1 m,
which is far inside the 15 m (L1) and 10 m (L6) tolerances.
"""
import math

import numpy as np

_A = 6378137.0
_F = 1 / 298.257222101
_K0 = 0.9996
_E0 = 500000.0


def _utm_consts():
    n = _F / (2 - _F)
    a_rect = _A / (1 + n) * (1 + n * n / 4 + n ** 4 / 64)
    alpha = (n / 2 - 2 * n * n / 3 + 5 * n ** 3 / 16,
             13 * n * n / 48 - 3 * n ** 3 / 5,
             61 * n ** 3 / 240)
    return n, a_rect, alpha


_N, _ARECT, _ALPHA = _utm_consts()


def utm_zone_meridian(zone):
    return math.radians(-183 + 6 * zone)


def latlon_to_utm(lat, lon, zone=16):
    """lat/lon in degrees (scalars or arrays) -> (easting, northing) in metres."""
    lat = np.radians(np.asarray(lat, dtype=np.float64))
    dlon = np.radians(np.asarray(lon, dtype=np.float64)) - utm_zone_meridian(zone)
    c = 2 * math.sqrt(_N) / (1 + _N)
    sin_lat = np.sin(lat)
    t = np.sinh(np.arctanh(sin_lat) - c * np.arctanh(c * sin_lat))
    xi = np.arctan2(t, np.cos(dlon))
    eta = np.arctanh(np.sin(dlon) / np.sqrt(1 + t * t))
    e_sum, n_sum = eta.copy(), xi.copy()
    for j, a in enumerate(_ALPHA, start=1):
        e_sum = e_sum + a * np.cos(2 * j * xi) * np.sinh(2 * j * eta)
        n_sum = n_sum + a * np.sin(2 * j * xi) * np.cosh(2 * j * eta)
    return _E0 + _K0 * _ARECT * e_sum, _K0 * _ARECT * n_sum


def polygon_area_centroid(xs, ys):
    """Shoelace area-weighted centroid of a closed or open ring."""
    x, y = np.asarray(xs, float), np.asarray(ys, float)
    x1, y1 = np.roll(x, -1), np.roll(y, -1)
    cross = x * y1 - x1 * y
    area = cross.sum() / 2
    cx = ((x + x1) * cross).sum() / (6 * area)
    cy = ((y + y1) * cross).sum() / (6 * area)
    return abs(area), cx, cy


def rasterize_polygon(px, pz, x0, z0, nx, nz, step=1.0):
    """Boolean mask[nz, nx] of grid points (x0 + i*step, z0 + j*step) inside the ring.

    Even-odd scanline fill; row 0 is the southmost row (z0).
    """
    px, pz = np.asarray(px, float), np.asarray(pz, float)
    ax, az = px, pz
    bx, bz = np.roll(px, -1), np.roll(pz, -1)
    mask = np.zeros((nz, nx), dtype=bool)
    gx = x0 + np.arange(nx) * step
    for j in range(nz):
        z = z0 + j * step
        hit = (az <= z) != (bz <= z)
        if not hit.any():
            continue
        xs = ax[hit] + (z - az[hit]) * (bx[hit] - ax[hit]) / (bz[hit] - az[hit])
        xs.sort()
        for k in range(0, len(xs) - 1, 2):
            mask[j] |= (gx >= xs[k]) & (gx < xs[k + 1])
    return mask


def distance_to_polyline(lx, lz, x0, z0, nx, nz, max_dist, step=1.0):
    """Distance[nz, nx] from grid points to an open polyline, capped at max_dist."""
    dist = np.full((nz, nx), np.float32(max_dist))
    for k in range(len(lx) - 1):
        ax, az, bx, bz = lx[k], lz[k], lx[k + 1], lz[k + 1]
        i0 = max(int(math.floor((min(ax, bx) - max_dist - x0) / step)), 0)
        i1 = min(int(math.ceil((max(ax, bx) + max_dist - x0) / step)) + 1, nx)
        j0 = max(int(math.floor((min(az, bz) - max_dist - z0) / step)), 0)
        j1 = min(int(math.ceil((max(az, bz) + max_dist - z0) / step)) + 1, nz)
        if i0 >= i1 or j0 >= j1:
            continue
        gx = (x0 + np.arange(i0, i1) * step)[None, :]
        gz = (z0 + np.arange(j0, j1) * step)[:, None]
        dx, dz = bx - ax, bz - az
        ll = dx * dx + dz * dz
        t = np.clip(((gx - ax) * dx + (gz - az) * dz) / ll, 0, 1) if ll > 0 else 0
        d = np.hypot(gx - (ax + t * dx), gz - (az + t * dz)).astype(np.float32)
        np.minimum(dist[j0:j1, i0:i1], d, out=dist[j0:j1, i0:i1])
    return dist


def point_segment_distance(x, z, lx, lz):
    """Min distance from one point to an open polyline."""
    best = math.inf
    for k in range(len(lx) - 1):
        ax, az, bx, bz = lx[k], lz[k], lx[k + 1], lz[k + 1]
        dx, dz = bx - ax, bz - az
        ll = dx * dx + dz * dz
        t = 0.0 if ll == 0 else min(max(((x - ax) * dx + (z - az) * dz) / ll, 0.0), 1.0)
        best = min(best, math.hypot(x - ax - t * dx, z - az - t * dz))
    return best


def point_in_ring(x, z, px, pz):
    inside = False
    n = len(px)
    for k in range(n):
        ax, az, bx, bz = px[k], pz[k], px[(k + 1) % n], pz[(k + 1) % n]
        if (az <= z) != (bz <= z):
            if x < ax + (z - az) * (bx - ax) / (bz - az):
                inside = not inside
    return inside
