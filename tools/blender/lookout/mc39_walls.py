"""Which collision proxies a standing body can touch on a deck, as plan footprints. Pure Python (no bpy).

Rule (tower-td-note, lane-end ruling, R1b as widened 2026-10-11): the wall set for reach, lips, bands, accept figures,
hand-over clearances and the no-penetration sweep on a deck is EVERY collision proxy a body standing on that deck can
touch, found from the proxy list by a geometric test, not from a list of proxy families. A proxy is a convex hull; it
is cut by the slab of heights the body occupies there and the plan outline of what is left is its footprint.
"""
import math

import mc39_poly as poly


def _hull2d(pts):
    """Convex hull of plan points, counter-clockwise (monotone chain); collinear points dropped."""
    ps = sorted(set((round(p[0], 9), round(p[1], 9)) for p in pts))
    if len(ps) <= 2:
        return ps

    def cross(o, a, b):
        return (a[0] - o[0]) * (b[1] - o[1]) - (a[1] - o[1]) * (b[0] - o[0])
    lower, upper = [], []
    for p in ps:
        while len(lower) >= 2 and cross(lower[-2], lower[-1], p) <= 1e-12:
            lower.pop()
        lower.append(p)
    for p in reversed(ps):
        while len(upper) >= 2 and cross(upper[-2], upper[-1], p) <= 1e-12:
            upper.pop()
        upper.append(p)
    return lower[:-1] + upper[:-1]


def footprint(verts, z0, z1):
    """Plan outline of the part of a convex hull (its vertices) lying between heights z0 and z1; [] if none.
    Every pair of vertices is cut by the two planes: pairs that are not edges only add points inside the outline."""
    pts = [(v[0], v[1]) for v in verts if z0 <= v[2] <= z1]
    n = len(verts)
    for i in range(n):
        a = verts[i]
        for j in range(i + 1, n):
            b = verts[j]
            for zc in (z0, z1):
                if (a[2] - zc) * (b[2] - zc) < 0.0:
                    t = (zc - a[2]) / (b[2] - a[2])
                    pts.append((a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t))
    return _hull2d(pts) if pts else []


def dist(fp, q):
    """Plan distance from q to a footprint (0 inside it)."""
    n = len(fp)
    if n == 1:
        return math.hypot(q[0] - fp[0][0], q[1] - fp[0][1])
    if n == 2:
        return poly.seg_dist(q, fp[0], fp[1])
    if poly.inside(fp, q, 0.0):
        return 0.0
    return min(poly.seg_dist(q, fp[i], fp[(i + 1) % n]) for i in range(n))


def _seg_seg(a, b, c, d):
    def o(p, q, r):
        return (q[0] - p[0]) * (r[1] - p[1]) - (q[1] - p[1]) * (r[0] - p[0])
    if o(a, b, c) * o(a, b, d) < 0 and o(c, d, a) * o(c, d, b) < 0:
        return 0.0
    return min(poly.seg_dist(a, c, d), poly.seg_dist(b, c, d), poly.seg_dist(c, a, b), poly.seg_dist(d, a, b))


def seg_dist(fp, a, b):
    """Plan distance from the segment a-b to a footprint (0 if they meet)."""
    n = len(fp)
    if n == 1:
        return poly.seg_dist(fp[0], a, b)
    if n == 2:
        return _seg_seg(a, b, fp[0], fp[1])
    if poly.inside(fp, a, 0.0) or poly.inside(fp, b, 0.0):
        return 0.0
    return min(_seg_seg(a, b, fp[i], fp[(i + 1) % n]) for i in range(n))


def bbox_gap(fp, box):
    """Plan gap between a footprint's bounding box and a box (x0, y0, x1, y1); 0 if they overlap."""
    xs = [p[0] for p in fp]
    ys = [p[1] for p in fp]
    dx = max(box[0] - max(xs), min(xs) - box[2], 0.0)
    dy = max(box[1] - max(ys), min(ys) - box[3], 0.0)
    return math.hypot(dx, dy)
