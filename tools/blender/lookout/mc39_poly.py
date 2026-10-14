"""Plan-view polygon helpers for the MC-39 build (landing outlines, clear circles, run room). Pure Python.

Points are (x, y) or (x, y, z) tuples in the design frame; only x and y are used.
"""
import math


def clip_half_plane(poly, point, normal):
    """Keep the part of a convex polygon on the side of the line through `point` that `normal` points to."""
    out = []
    n = len(poly)

    def side(p):
        return (p[0] - point[0]) * normal[0] + (p[1] - point[1]) * normal[1]
    for i in range(n):
        a, b = poly[i], poly[(i + 1) % n]
        sa, sb = side(a), side(b)
        if sa >= 0:
            out.append(a)
        if (sa >= 0) != (sb >= 0):
            t = sa / (sa - sb)
            out.append((a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t))
    return out


def inside(poly, p, eps=1e-9):
    """True if p is inside or on a convex polygon (either winding)."""
    sign = 0
    n = len(poly)
    for i in range(n):
        a, b = poly[i], poly[(i + 1) % n]
        c = (b[0] - a[0]) * (p[1] - a[1]) - (b[1] - a[1]) * (p[0] - a[0])
        if abs(c) <= eps:
            continue
        s = 1 if c > 0 else -1
        if sign == 0:
            sign = s
        elif s != sign:
            return False
    return True


def interval(poly, origin, along, across, s):
    """Where the line {origin + along*u + across*s} crosses a convex polygon: (u_min, u_max), or None."""
    us = []
    n = len(poly)
    for i in range(n):
        a, b = poly[i], poly[(i + 1) % n]
        sa = (a[0] - origin[0]) * across[0] + (a[1] - origin[1]) * across[1] - s
        sb = (b[0] - origin[0]) * across[0] + (b[1] - origin[1]) * across[1] - s
        if abs(sa - sb) < 1e-12:
            if abs(sa) < 1e-9:
                us += [(p[0] - origin[0]) * along[0] + (p[1] - origin[1]) * along[1] for p in (a, b)]
            continue
        t = sa / (sa - sb)
        if -1e-9 <= t <= 1 + 1e-9:
            x, y = a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t
            us.append((x - origin[0]) * along[0] + (y - origin[1]) * along[1])
    if len(us) < 2:
        return None
    return min(us), max(us)


def seg_dist(q, a, b):
    vx, vy = b[0] - a[0], b[1] - a[1]
    t = min(max(((q[0] - a[0]) * vx + (q[1] - a[1]) * vy) / max(vx * vx + vy * vy, 1e-12), 0.0), 1.0)
    return math.hypot(q[0] - a[0] - vx * t, q[1] - a[1] - vy * t)


def bbox(poly):
    xs = [p[0] for p in poly]; ys = [p[1] for p in poly]
    return min(xs), min(ys), max(xs), max(ys)


def centroid(poly):
    return (sum(p[0] for p in poly) / len(poly), sum(p[1] for p in poly) / len(poly))
