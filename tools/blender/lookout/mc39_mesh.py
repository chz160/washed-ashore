"""Mesh buffers and primitives for the MC-39 build. Pure Python (no bpy), so the geometry can be checked outside Blender.

Every primitive writes positions, faces, per-corner UVs (atlas space) and a material index into a MeshBuf.
Atlas regions come from mc39_atlas.REGIONS; a region has a physical size, so UVs are laid out in metres.
"""
import math

MAT_OPAQUE, MAT_CUTOUT, MAT_GLASS, MAT_IMPOSTOR = 0, 1, 2, 3
MATERIAL_NAMES = ["MC39_Opaque", "MC39_Cutout", "MC39_Glass", "MC39_Impostor"]


def add(a, b): return (a[0] + b[0], a[1] + b[1], a[2] + b[2])
def sub(a, b): return (a[0] - b[0], a[1] - b[1], a[2] - b[2])
def mul(a, s): return (a[0] * s, a[1] * s, a[2] * s)
def dot(a, b): return a[0] * b[0] + a[1] * b[1] + a[2] * b[2]
def cross(a, b): return (a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0])
def length(a): return math.sqrt(dot(a, a))
def lerp(a, b, t): return (a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t, a[2] + (b[2] - a[2]) * t)


def norm(a):
    n = length(a)
    if n < 1e-12:
        raise ValueError("zero-length vector")
    return (a[0] / n, a[1] / n, a[2] / n)


def perp_to(v, axis):
    """v with its component along axis removed, normalised."""
    return norm(sub(v, mul(axis, dot(v, axis))))


class Region:
    """A rectangle of the atlas with a physical size: `length` metres along u, `width` metres along v."""

    def __init__(self, name, px, atlas_px, length, width, inset_px=2.0):
        self.name = name
        x, y, w, h = px
        self.px = px
        self.length, self.width = length, width
        self.u0 = (x + inset_px) / atlas_px
        self.u1 = (x + w - inset_px) / atlas_px
        # Image row 0 is the top of the atlas; UV v = 0 is the bottom.
        self.v0 = 1.0 - (y + h - inset_px) / atlas_px
        self.v1 = 1.0 - (y + inset_px) / atlas_px

    def uv(self, su, sv):
        """su, sv in metres from the region's origin; clamped to the region."""
        fu = min(max(su / self.length, 0.0), 1.0)
        fv = min(max(sv / self.width, 0.0), 1.0)
        return (self.u0 + (self.u1 - self.u0) * fu, self.v0 + (self.v1 - self.v0) * fv)

    def uv01(self, fu, fv):
        return (self.u0 + (self.u1 - self.u0) * fu, self.v0 + (self.v1 - self.v0) * fv)


class MeshBuf:
    def __init__(self, name):
        self.name = name
        self.verts = []
        self.faces = []      # tuples of vertex indices (3 or 4)
        self.uvs = []        # per face: tuple of (u, v) per corner
        self.mats = []       # per face material index
        self.normals = None  # optional per-vertex custom normal (impostor)
        self.smooth = False  # collider meshes: shared vertices, so a box stays 8 vertices after import

    def tri_count(self):
        return sum(len(f) - 2 for f in self.faces)

    def face(self, pts, uvs, mat=MAT_OPAQUE):
        base = len(self.verts)
        self.verts.extend(pts)
        self.faces.append(tuple(range(base, base + len(pts))))
        self.uvs.append(tuple(uvs))
        self.mats.append(mat)

    def quad_region(self, p0, p1, p2, p3, region, mat=MAT_OPAQUE, f=(0.0, 0.0, 1.0, 1.0)):
        """Quad mapped to the fraction rectangle f = (fu0, fv0, fu1, fv1) of a region; p0 = (fu0, fv0), counter-clockwise."""
        self.face([p0, p1, p2, p3], [region.uv01(f[0], f[1]), region.uv01(f[2], f[1]), region.uv01(f[2], f[3]), region.uv01(f[0], f[3])], mat)

    def tri_region(self, p0, p1, p2, region, mat=MAT_OPAQUE, f=((0.0, 0.0), (1.0, 0.0), (0.5, 1.0))):
        self.face([p0, p1, p2], [region.uv01(*f[0]), region.uv01(*f[1]), region.uv01(*f[2])], mat)

    def extend(self, other):
        base = len(self.verts)
        self.verts.extend(other.verts)
        self.faces.extend(tuple(i + base for i in f) for f in other.faces)
        self.uvs.extend(other.uvs)
        self.mats.extend(other.mats)

    def bounds(self):
        xs = [v[0] for v in self.verts]; ys = [v[1] for v in self.verts]; zs = [v[2] for v in self.verts]
        return (min(xs), min(ys), min(zs)), (max(xs), max(ys), max(zs))


def prism(buf, ring, axis_vec, region, u_dir, u_off=0.0, mat=MAT_OPAQUE, caps=True, v_scale=None):
    """Extrude a planar polygon `ring` (list of points, counter-clockwise seen from +axis_vec) by axis_vec.

    UVs: u = distance along u_dir (metres, plus u_off); v runs around the section, scaled to fill the region's width.
    The far cap (ring + axis_vec) and the near cap are mapped flat with the same u.
    """
    n = len(ring)
    top = [add(p, axis_vec) for p in ring]
    ud = norm(u_dir)
    o = ring[0]
    axn = norm(axis_vec)
    axis_is_u = abs(dot(axn, ud)) > 0.7

    def su(p):
        return dot(sub(p, o), ud) + u_off

    # v for caps: the in-plane direction perpendicular to u (or any in-plane direction when the axis is u)
    if axis_is_u:
        vd = perp_to(sub(ring[1], ring[0]), axn)
    else:
        vd = norm(cross(axn, ud))
    svs = [dot(sub(p, o), vd) for p in ring]
    vmin, vmax = min(svs), max(svs)
    span = (vmax - vmin) if (vmax - vmin) > 1e-9 else 1.0
    scale = v_scale if v_scale is not None else region.width / span

    def cap_uv(p):
        return region.uv(su(p), (dot(sub(p, o), vd) - vmin) * scale)

    if caps:
        buf.face(list(top), [cap_uv(p) for p in ring] if not axis_is_u else [region.uv(u_off + length(axis_vec), (dot(sub(p, o), vd) - vmin) * scale) for p in ring], mat)
        buf.face(list(reversed(ring)), [cap_uv(p) if not axis_is_u else region.uv(u_off, (dot(sub(p, o), vd) - vmin) * scale) for p in reversed(ring)], mat)
    # sides
    perim = [0.0]
    for i in range(n):
        perim.append(perim[-1] + length(sub(ring[(i + 1) % n], ring[i])))
    pscale = region.width / perim[-1] if perim[-1] > 1e-9 else 1.0
    for i in range(n):
        a, b = ring[i], ring[(i + 1) % n]
        a2, b2 = top[i], top[(i + 1) % n]
        if axis_is_u:
            va, vb = perim[i] * pscale, perim[i + 1] * pscale
            uvs = [region.uv(u_off, va), region.uv(u_off, vb), region.uv(u_off + length(axis_vec), vb), region.uv(u_off + length(axis_vec), va)]
        else:
            t = length(axis_vec) * scale
            uvs = [region.uv(su(a), 0.0), region.uv(su(b), 0.0), region.uv(su(b), t), region.uv(su(a), t)]
        buf.face([a, b, b2, a2], uvs, mat)


def box(buf, c, ex, ey, ez, region, u_off=0.0, mat=MAT_OPAQUE, caps=True):
    """Box centred on c with half-extent vectors ex (the long, u axis), ey, ez."""
    ring = [add(add(c, mul(ex, -1)), add(mul(ey, sy), mul(ez, sz))) for sy, sz in ((-1, -1), (1, -1), (1, 1), (-1, 1))]
    if dot(cross(sub(ring[1], ring[0]), sub(ring[2], ring[1])), ex) < 0:
        ring.reverse()
    prism(buf, ring, mul(ex, 2.0), region, ex, u_off, mat, caps)


def bar(buf, p0, p1, side, w, h, region, u_off=0.0, mat=MAT_OPAQUE, caps=True):
    """Rectangular bar from p0 to p1; `side` hints the direction of its w dimension, h is across the other."""
    ax = norm(sub(p1, p0))
    s = perp_to(side, ax)
    t = norm(cross(ax, s))
    c = lerp(p0, p1, 0.5)
    box(buf, c, mul(ax, length(sub(p1, p0)) / 2.0), mul(s, w / 2.0), mul(t, h / 2.0), region, u_off, mat, caps)


def angle(buf, p0, p1, fa, fb, size, t, region, lod=0, joint0=True, joint1=True, min_w=0.0, mat=MAT_OPAQUE):
    """Angle-iron member with its heel on the line p0-p1 and flanges along fa and fb.

    lod 0: solid L section in two halves, so each end maps to the 'joint' end (u = 0) of a steel strip.
    lod 1: the two flange faces only. lod 2: one flat strip along fa, at least min_w wide.
    """
    ax = norm(sub(p1, p0))
    a = perp_to(fa, ax)
    b = perp_to(fb, ax)
    L = length(sub(p1, p0))
    if lod >= 2:
        w = max(size, min_w)
        o = mul(a, -(w - size) / 2.0)
        q0, q1 = add(p0, o), add(p1, o)
        buf.face([q0, q1, add(q1, mul(a, w)), add(q0, mul(a, w))],
                 [region.uv(0.3, 0.0), region.uv(min(0.3 + L, region.length), 0.0), region.uv(min(0.3 + L, region.length), region.width), region.uv(0.3, region.width)], mat)
        return
    if lod == 1:
        for d, v0, v1 in ((a, 0.0, 0.5), (b, 0.5, 1.0)):
            m = lerp(p0, p1, 0.5)
            for s, e in ((p0, m), (p1, m)):
                h = L / 2.0
                buf.face([s, e, add(e, mul(d, size)), add(s, mul(d, size))],
                         [region.uv(0.0, v0 * region.width), region.uv(h, v0 * region.width), region.uv(h, v1 * region.width), region.uv(0.0, v1 * region.width)], mat)
        return
    sec = [(0.0, 0.0), (size, 0.0), (size, t), (t, t), (t, size), (0.0, size)]
    if dot(cross(a, b), ax) < 0:
        sec.reverse()
    m = lerp(p0, p1, 0.5)
    for start, end, cap in ((p0, m, joint0), (p1, m, joint1)):
        # Each half runs from its joint to the middle; u = 0 at the joint.
        d = sub(end, start)
        ring = [add(start, add(mul(a, x), mul(b, y))) for x, y in sec]
        if dot(d, ax) < 0:
            ring.reverse()
        prism_half(buf, ring, d, region, cap, mat)


def prism_half(buf, ring, axis_vec, region, cap_start, mat):
    n = len(ring)
    top = [add(p, axis_vec) for p in ring]
    h = length(axis_vec)
    perim = [0.0]
    for i in range(n):
        perim.append(perim[-1] + length(sub(ring[(i + 1) % n], ring[i])))
    ps = region.width / perim[-1]
    for i in range(n):
        a, b = ring[i], ring[(i + 1) % n]
        va, vb = perim[i] * ps, perim[i + 1] * ps
        buf.face([a, b, top[(i + 1) % n], top[i]], [region.uv(0.0, va), region.uv(0.0, vb), region.uv(h, vb), region.uv(h, va)], mat)
    if cap_start:
        if n == 6:
            # the L-shaped end as two rectangles (one per flange) rather than one concave six-sided face
            rr = list(reversed(ring))
            ix = list(reversed(range(n)))
            # find the reflex corner: the vertex whose two neighbours' far points make both flanges rectangles
            best = None
            for st in range(6):
                q1 = [(st + j) % 6 for j in (0, 1, 2, 3)]
                q2 = [(st + j) % 6 for j in (0, 3, 4, 5)]

                def rect(q):
                    a, b, c, d = (rr[i] for i in q)
                    return abs(dot(sub(b, a), sub(c, b))) < 1e-9 and abs(dot(sub(c, b), sub(d, c))) < 1e-9 and abs(dot(sub(d, c), sub(a, d))) < 1e-9
                if rect(q1) and rect(q2):
                    best = (q1, q2)
                    break
            if best:
                for q in best:
                    buf.face([rr[i] for i in q], [region.uv(0.0, perim[ix[i]] * ps) for i in q], mat)
                return
        buf.face(list(reversed(ring)), [region.uv(0.0, perim[i] * ps) for i in reversed(range(n))], mat)


def cylinder(buf, p0, p1, r0, r1, sides, region, mat=MAT_OPAQUE, caps=True, ref=(1.0, 0.0, 0.0)):
    ax = norm(sub(p1, p0))
    if abs(dot(ax, ref)) > 0.9:
        ref = (0.0, 1.0, 0.0)
    a = perp_to(ref, ax)
    b = cross(ax, a)
    L = length(sub(p1, p0))
    r0s, r1s = [], []
    for i in range(sides):
        th = 2.0 * math.pi * i / sides
        d = add(mul(a, math.cos(th)), mul(b, math.sin(th)))
        r0s.append(add(p0, mul(d, r0)))
        r1s.append(add(p1, mul(d, r1)))
    for i in range(sides):
        j = (i + 1) % sides
        v0, v1 = region.width * i / sides, region.width * (i + 1) / sides
        if r1 < 1e-6:
            buf.face([r0s[i], r0s[j], p1], [region.uv(0.0, v0), region.uv(0.0, v1), region.uv(L, (v0 + v1) / 2)], mat)
        else:
            buf.face([r0s[i], r0s[j], r1s[j], r1s[i]], [region.uv(0.0, v0), region.uv(0.0, v1), region.uv(L, v1), region.uv(L, v0)], mat)
    if caps:
        if r1 > 1e-6:
            buf.face(list(r1s), [region.uv(L, region.width * i / sides) for i in range(sides)], mat)
        buf.face(list(reversed(r0s)), [region.uv(0.0, region.width * i / sides) for i in reversed(range(sides))], mat)


def segment_hits_box(p0, p1, bmin, bmax):
    """True if the segment p0-p1 passes through the axis-aligned box (slab test)."""
    t0, t1 = 0.0, 1.0
    for i in range(3):
        d = p1[i] - p0[i]
        if abs(d) < 1e-12:
            if p0[i] < bmin[i] or p0[i] > bmax[i]:
                return False
            continue
        a, b = (bmin[i] - p0[i]) / d, (bmax[i] - p0[i]) / d
        if a > b:
            a, b = b, a
        t0, t1 = max(t0, a), min(t1, b)
        if t0 > t1:
            return False
    return True


def hull_box(name, c, ex, ey, ez):
    """A box as a collider mesh: exactly 8 shared vertices and 12 triangles, outward winding."""
    if dot(cross(ex, ey), ez) < 0:
        ez = mul(ez, -1.0)
    buf = MeshBuf(name)
    buf.smooth = True
    for sz in (-1, 1):
        for sy in (-1, 1):
            for sx in (-1, 1):
                buf.verts.append(add(c, add(mul(ex, sx), add(mul(ey, sy), mul(ez, sz)))))
    quads = [(0, 2, 3, 1), (4, 5, 7, 6), (0, 1, 5, 4), (2, 6, 7, 3), (0, 4, 6, 2), (1, 3, 7, 5)]
    for a, b, c2, d in quads:
        buf.faces.append((a, b, c2)); buf.faces.append((a, c2, d))
    buf.uvs = [((0.0, 0.0),) * 3] * len(buf.faces)
    buf.mats = [MAT_OPAQUE] * len(buf.faces)
    return buf


def hull_bar(name, p0, p1, side, w, h):
    ax = norm(sub(p1, p0))
    sd = perp_to(side, ax)
    t = norm(cross(ax, sd))
    return hull_box(name, lerp(p0, p1, 0.5), mul(ax, length(sub(p1, p0)) / 2.0), mul(sd, w / 2.0), mul(t, h / 2.0))


def clip_below(ring, z_max):
    """A planar polygon (3D points in order) cut off above the height z_max."""
    out = []
    n = len(ring)
    for i in range(n):
        a, b = ring[i], ring[(i + 1) % n]
        ia, ib = a[2] <= z_max + 1e-12, b[2] <= z_max + 1e-12
        if ia:
            out.append(a)
        if ia != ib:
            t = (z_max - a[2]) / (b[2] - a[2])
            out.append((a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t, z_max))
    return out


def hull_frustum(name, ring0, ring1):
    """A convex hull between two planar rings of the same count (a prism that tapers and leans), as a collider mesh
    with shared vertices. ring0 is the lower ring; both are given in the same order."""
    n = len(ring0)
    axis_vec = sub(ring1[0], ring0[0])
    if dot(cross(sub(ring0[1], ring0[0]), sub(ring0[2], ring0[1])), axis_vec) < 0:
        ring0, ring1 = list(reversed(ring0)), list(reversed(ring1))
    buf = MeshBuf(name)
    buf.smooth = True
    buf.verts = list(ring0) + list(ring1)
    for i in range(1, n - 1):
        buf.faces.append((0, i + 1, i))                    # bottom
        buf.faces.append((n, n + i, n + i + 1))            # top
    for i in range(n):
        j = (i + 1) % n
        buf.faces.append((i, j, n + j)); buf.faces.append((i, n + j, n + i))
    buf.uvs = [((0.0, 0.0),) * 3] * len(buf.faces)
    buf.mats = [MAT_OPAQUE] * len(buf.faces)
    return buf


def hull_prism(name, ring, axis_vec):
    """A convex prism as a collider mesh with shared vertices; ring is counter-clockwise seen from +axis_vec."""
    n = len(ring)
    if dot(cross(sub(ring[1], ring[0]), sub(ring[2], ring[1])), axis_vec) < 0:
        ring = list(reversed(ring))
    buf = MeshBuf(name)
    buf.smooth = True
    buf.verts = list(ring) + [add(q, axis_vec) for q in ring]
    for i in range(1, n - 1):
        buf.faces.append((0, i + 1, i))                    # bottom
        buf.faces.append((n, n + i, n + i + 1))            # top
    for i in range(n):
        j = (i + 1) % n
        buf.faces.append((i, j, n + j)); buf.faces.append((i, n + j, n + i))
    buf.uvs = [((0.0, 0.0),) * 3] * len(buf.faces)
    buf.mats = [MAT_OPAQUE] * len(buf.faces)
    return buf
