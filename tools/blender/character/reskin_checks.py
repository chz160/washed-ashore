"""Numeric checks for the P4 re-skin (pod 49): posed linear-blend skinning in numpy, cross-section volume at the knees and elbows (DK1),
the watch at the wrist-flex extreme (S3) and shoulder mirror symmetry (S1). Nothing here changes the mesh."""
import numpy as np

import os as _os
S_MAX = float(_os.environ.get("RESKIN_SMAX", "2.0"))   # cap on the radial scale (trial parameter)
CREASE_STRENGTH = float(_os.environ.get("RESKIN_CREASE", "0.0"))
CROSS_FLOOR = 0.85   # td-49 DK1: the script's fail-closed floor; the stills are the look pass


def rot_about(p, axis, deg):
    a = np.array(axis, dtype=float)
    a /= np.linalg.norm(a)
    t = np.radians(deg)
    K = np.array([[0, -a[2], a[1]], [a[2], 0, -a[0]], [-a[1], a[0], 0]])
    Rm = np.eye(3) + np.sin(t) * K + (1 - np.cos(t)) * K @ K
    M = np.eye(4)
    M[:3, :3] = Rm
    M[:3, 3] = p - Rm @ p
    return M


def depth(skel, n):
    d = 0
    while skel[n]["p"]:
        n, d = skel[n]["p"], d + 1
    return d


def deltas(skel, rots):
    """World delta matrix of every bone for the given {bone: (axis, degrees)} rotations about the bone's own head (rotations pass on to children)."""
    W = {}
    for n in sorted(skel, key=lambda k: depth(skel, k)):
        Wp = W[skel[n]["p"]] if skel[n]["p"] else np.eye(4)
        if n in rots:
            p = (Wp @ np.append(skel[n]["h"], 1.0))[:3]
            W[n] = rot_about(p, *rots[n]) @ Wp
        else:
            W[n] = Wp
    return W


def pose(V, G, gnames, skel, rots, follow=()):
    W = deltas(skel, rots)
    for sd in follow:   # the IK foot is not a child of the shin: put it on the shin's end, as the clips' IK would
        a0 = skel["Foot." + sd]["h"]
        a1 = (W["LowerLeg." + sd] @ np.append(a0, 1.0))[:3]
        Tm = np.eye(4)
        Tm[:3, 3] = a1 - a0
        W["Foot." + sd] = Tm
    out = np.zeros_like(V)
    Vh = np.c_[V, np.ones(len(V))]
    for gi, n in enumerate(gnames):
        w = G[:, gi]
        m = w > 1e-6
        if m.any():
            out[m] += w[m, None] * (Vh[m] @ W[n].T)[:, :3]
    return out, W


def hull_area(P):
    pts = sorted(map(tuple, np.round(P, 6)))
    if len(pts) < 3:
        return 0.0
    def cross(o, a, b):
        return (a[0] - o[0]) * (b[1] - o[1]) - (a[1] - o[1]) * (b[0] - o[0])
    lo, up = [], []
    for p in pts:
        while len(lo) >= 2 and cross(lo[-2], lo[-1], p) <= 0:
            lo.pop()
        lo.append(p)
    for p in reversed(pts):
        while len(up) >= 2 and cross(up[-2], up[-1], p) <= 0:
            up.pop()
        up.append(p)
    h = lo[:-1] + up[:-1]
    return 0.5 * abs(sum(h[i][0] * h[(i + 1) % len(h)][1] - h[(i + 1) % len(h)][0] * h[i][1] for i in range(len(h))))


def section(P, centre, axis):
    """Convex-hull area of the points projected on the plane through `centre` perpendicular to `axis`."""
    a = axis / np.linalg.norm(axis)
    u = np.cross(a, [0.0, 0.0, 1.0] if abs(a[2]) < 0.9 else [1.0, 0.0, 0.0])
    u /= np.linalg.norm(u)
    v = np.cross(a, u)
    q = P - centre
    return hull_area(np.c_[q @ u, q @ v])


def cross_ratio(V, G, gnames, skel, upper_bone, lower_bone, side, deg, axis, rest=None):
    """Cross-section area (posed at `deg` about `axis` at the joint) over rest, in slabs 5 cm above, at, and 5 cm below the joint."""
    a = skel[upper_bone + "." + side]["h"]
    j = skel[lower_bone + "." + side]["h"]
    ax = (j - a) / np.linalg.norm(j - a)
    Vr = V if rest is None else rest   # rest geometry (without a corrective shape) for the reference sections
    rel = Vr - j
    d = rel @ ax
    perp = np.linalg.norm(rel - np.outer(d, ax), axis=1)
    sgn = 1.0 if side == "L" else -1.0
    base = (perp < 0.13) & (Vr[:, 0] * sgn > 0)
    Vp, W = pose(V, G, gnames, skel, {lower_bone + "." + side: (axis, deg)})
    jp = (W[skel[lower_bone + "." + side]["p"]] @ np.append(j, 1.0))[:3]
    low_dir = (W[lower_bone + "." + side] @ np.append(j + ax, 1.0))[:3] - (W[lower_bone + "." + side] @ np.append(j, 1.0))[:3]
    out = {}
    for name, off in (("above5cm", -0.05), ("joint", 0.0), ("below5cm", 0.05)):
        m = base & (np.abs(d - off) < 0.01)
        if m.sum() < 8:
            out[name] = None
            continue
        r0 = section(Vr[m], j + ax * off, ax)
        dirp = ax if off < 0 else (low_dir if off > 0 else (ax + low_dir) / np.linalg.norm(ax + low_dir))
        cp = jp + (dirp / np.linalg.norm(dirp)) * off
        r1 = section(Vp[m], cp, dirp)
        out[name] = round(float(r1 / r0), 3) if r0 > 0 else None
    # every 2 cm from -6 to +6: the pinch is at the joint plane; under linear blending it is bounded by cos(angle / 2) there (0.707 at 90 degrees)
    planes, at = [], []
    for off in np.arange(-0.06, 0.0601, 0.02):
        m = base & (np.abs(d - off) < 0.01)
        if m.sum() < 8:
            continue
        r0 = section(Vr[m], j + ax * off, ax)
        dirp = ax if off < -0.005 else (low_dir if off > 0.005 else (ax + low_dir) / np.linalg.norm(ax + low_dir))
        cp = jp + (dirp / np.linalg.norm(dirp)) * off
        planes.append(float(section(Vp[m], cp, dirp) / r0))
        at.append(round(float(off), 3))
    out["planes"] = {str(o): round(v, 3) for o, v in zip(at, planes)}
    out["planes_min"] = round(min(planes), 3) if planes else None
    out["planes_min_at_m"] = at[int(np.argmin(planes))] if planes else None
    out["planes_mean"] = round(float(np.mean(planes)), 3) if planes else None
    return out


def extents(P):
    Q = P - P.mean(0)
    axes = np.linalg.svd(Q, full_matrices=False)[2]
    proj = Q @ axes.T
    return proj.max(0) - proj.min(0)


def watch_flex(V, G, gnames, skel, wid, edges, deg=50, band=None):
    """Watch size along its own axes at the wrist-flex extremes against rest (limit 2 mm), and the largest edge-length change where the watch meets the arm."""
    res = {}
    e0 = extents(V[wid])
    inw = np.zeros(len(V), bool)
    inw[wid] = True
    cross_e = edges[inw[edges[:, 0]] ^ inw[edges[:, 1]]]
    L0 = np.linalg.norm(V[cross_e[:, 0]] - V[cross_e[:, 1]], axis=1)
    for sign in (+1, -1):
        Vp, _ = pose(V, G, gnames, skel, {"Wrist.R": ((0, 1, 0), sign * deg)})
        res[sign * deg] = {"size_change_mm": round(1000 * float(np.abs(extents(Vp[wid]) - e0).max()), 3),
                           "step_edge_change_mm": round(1000 * float(np.abs(np.linalg.norm(Vp[cross_e[:, 0]] - Vp[cross_e[:, 1]], axis=1) - L0).max()), 3)}
    if band is not None and len(band):
        bm = np.zeros(len(V), bool)
        bm[band] = True
        be = edges[bm[edges[:, 0]] | bm[edges[:, 1]]]
        L1 = np.linalg.norm(V[be[:, 0]] - V[be[:, 1]], axis=1)
        for sign in (+1, -1):
            Vp, _ = pose(V, G, gnames, skel, {"Wrist.R": ((0, 1, 0), sign * deg)})
            r = np.linalg.norm(Vp[be[:, 0]] - Vp[be[:, 1]], axis=1) / np.maximum(L1, 1e-9)
            res[sign * deg]["band_edge_p99"] = round(float(np.percentile(r, 99)), 3)
            res[sign * deg]["band_edge_max"] = round(float(r.max()), 3)
    return res


def mirror_p95(V, zlo=1.3, zhi=1.6, xmax=0.4):
    """p95 of the distance from each shoulder-region vertex's mirror image to the nearest vertex on the other side (a symmetric mesh gives about 0)."""
    sel = V[(V[:, 2] > zlo) & (V[:, 2] < zhi) & (np.abs(V[:, 0]) < xmax)]
    sel = sel[::3]
    mir = sel * np.array([-1.0, 1.0, 1.0])
    d = np.empty(len(sel))
    for k in range(0, len(sel), 400):
        d[k:k + 400] = np.sqrt(((mir[k:k + 400, None, :] - sel[None, :, :]) ** 2).sum(2)).min(1)
    return float(np.percentile(d, 95))


def thumb_web(V, G, gnames, skel, edges, side, degs=(40, 45, 50)):
    """DK3: edge-length ratio (posed over rest) on the edges that touch the thumb/palm blend, with the thumb chain bent toward the palm (palm down, so -Z)."""
    rots = {}
    W = None
    # the bone axes are taken from the T bind; each bend is about the bone's own head, applied cumulatively down the chain
    for i, deg in enumerate(degs):
        n = "Thumb%d.%s" % (i + 1, side)
        d = skel[n]["t"] - skel[n]["h"]
        axis = np.cross(d / np.linalg.norm(d), [0.0, 0.0, -1.0])
        rots[n] = (axis / np.linalg.norm(axis), deg)
    Vp, _ = pose(V, G, gnames, skel, rots)
    gi = {n: k for k, n in enumerate(gnames)}
    t1 = G[:, gi["Thumb1." + side]]
    blend = (t1 > 0.01) & (t1 < 0.99)
    sel = blend[edges[:, 0]] | blend[edges[:, 1]]
    e = edges[sel]
    r = np.linalg.norm(Vp[e[:, 0]] - Vp[e[:, 1]], axis=1) / np.maximum(np.linalg.norm(V[e[:, 0]] - V[e[:, 1]], axis=1), 1e-9)
    return {"edges": int(len(e)), "p99": round(float(np.percentile(r, 99)), 3), "max": round(float(r.max()), 3)}


def corrective(V, G, gnames, skel, upper_bone, lower_bone, side, deg, axis, planes):
    """td DK1 fallback: a blend shape that restores the cross-section at `deg` (weight 1.0 = the full bend). Radial scale about the posed limb axis,
    1 / sqrt(area ratio) taken from the measured planes, tapered to 1 at 10 cm from the joint; converted to a bind-space delta with the inverse of the
    vertex's blended linear skinning matrix. Returns the delta array (zero outside the band)."""
    a = skel[upper_bone + "." + side]["h"]
    j = skel[lower_bone + "." + side]["h"]
    ax = (j - a) / np.linalg.norm(j - a)
    rel = V - j
    d = rel @ ax
    perp = np.linalg.norm(rel - np.outer(d, ax), axis=1)
    sgn = 1.0 if side == "L" else -1.0
    sel = (np.abs(d) <= 0.10) & (perp < 0.13) & (V[:, 0] * sgn > 0)
    Vp, W = pose(V, G, gnames, skel, {lower_bone + "." + side: (axis, deg)})
    nm = lower_bone + "." + side
    jp = (W[skel[nm]["p"]] @ np.append(j, 1.0))[:3]
    low_dir = (W[nm] @ np.append(j + ax, 1.0))[:3] - (W[nm] @ np.append(j, 1.0))[:3]
    offs = np.array(sorted(float(k) for k in planes))
    rat = np.array([planes[k] for k in sorted(planes, key=float)])
    idx = np.where(sel)[0]
    dd = d[idx]
    r = np.clip(np.interp(dd, offs, rat), 0.5, 1.0)
    s = np.minimum(1.0 / np.sqrt(r), S_MAX)
    fade = np.clip((0.10 - np.abs(dd)) / 0.03, 0, 1)
    s = 1.0 + (s - 1.0) * (fade * fade * (3 - 2 * fade))
    ap = np.where((dd < 0)[:, None], jp + np.outer(dd, ax), jp + np.outer(dd, low_dir))
    rad = Vp[idx] - ap
    rl = np.linalg.norm(rad, axis=1)
    e = low_dir / np.linalg.norm(low_dir) - ax
    e /= np.linalg.norm(e)    # toward the inside of the bend (the crease): no expansion there, so the shape adds no self-intersection
    cr = (rad / np.maximum(rl, 1e-9)[:, None]) @ e
    t = np.clip((cr - 0.2) / 0.6, 0, 1)
    if CREASE_STRENGTH > 0:   # 0 = full radial expansion; 1 = none on the crease side (the back of the knee)
        s = 1.0 + (s - 1.0) * (1.0 - CREASE_STRENGTH * t * t * (3 - 2 * t))
    Vd = ap + s[:, None] * rad
    Lm = {n: W[n][:3, :3] for n in gnames}
    gi = {n: k for k, n in enumerate(gnames)}
    delta = np.zeros_like(V)
    for t, v in enumerate(idx):
        A = np.zeros((3, 3))
        for k in np.nonzero(G[v] > 1e-6)[0]:
            A += G[v, k] * Lm[gnames[k]]
        delta[v] = np.linalg.solve(A, Vd[t] - Vp[v])
    return delta


def self_intersections(V, polys, weld, sel_verts):
    """Count triangle pairs that overlap and share no (position-welded) vertex, among the polygons whose vertices all lie in `sel_verts` (a bool mask)."""
    from mathutils import Vector
    from mathutils.bvhtree import BVHTree
    tris, ids = [], []
    for p in polys:
        if all(sel_verts[v] for v in p):
            for k in range(1, len(p) - 1):
                tris.append((p[0], p[k], p[k + 1]))
    used = sorted({v for t in tris for v in t})
    remap = {v: i for i, v in enumerate(used)}
    bvh = BVHTree.FromPolygons([Vector(V[v]) for v in used], [tuple(remap[v] for v in t) for t in tris], epsilon=1e-6)
    wt = [frozenset(int(weld[v]) for v in t) for t in tris]
    n = 0
    for a, b in bvh.overlap(bvh):
        if a < b and not (wt[a] & wt[b]):
            n += 1
    return n


def shin_checks(V, G, gnames, skel, side, deg, collar):
    """td Update 2 checks at a bend of `deg` with the IK foot on the shin end: (a) the shin centreline from 5 cm below the knee to the ankle within 0.01 m of
    a straight line; (b) the boot (below collar - 3 cm) rigid to Foot, RMS error in metres."""
    gi = {n: k for k, n in enumerate(gnames)}
    a, j = skel["UpperLeg." + side]["h"], skel["LowerLeg." + side]["h"]
    ax = (j - a) / np.linalg.norm(j - a)
    rel = V - j
    dd = rel @ ax
    perp = np.linalg.norm(rel - np.outer(dd, ax), axis=1)
    sgn = 1.0 if side == "L" else -1.0
    leg = (perp < 0.12) & (V[:, 0] * sgn > 0)
    Vp, W = pose(V, G, gnames, skel, {"LowerLeg." + side: ((1, 0, 0), deg)}, follow=(side,))
    ankle_d = float((skel["Foot." + side]["h"] - j) @ ax)
    # Straightness = how far each slab's posed centroid is from where a RIGID shin would put it (the LowerLeg bone's transform of the rest centroid).
    # Raw centroids are no use: pockets and patches pull a slab's centroid around even at rest. 0 means the shin does not curve at all.
    # Slabs run from 5 cm below the knee to the top of the ankle band (collar is 6 cm above the ankle, plus 3 cm), where the shin meets the boot.
    devs = []
    Wl = W["LowerLeg." + side]
    for off in np.arange(0.05, ankle_d - 0.09, 0.02):
        m = leg & (np.abs(dd - off) < 0.01)
        if m.sum() >= 6:
            rig = (Wl @ np.append(V[m].mean(0), 1.0))[:3]
            devs.append(float(np.linalg.norm(Vp[m].mean(0) - rig)))
    dev = max(devs) if devs else 0.0
    pts = devs
    boot = leg & (V[:, 2] < collar - 0.03) & (V[:, 2] > 0.0)
    Wf = W["Foot." + side]
    rigid = (np.c_[V[boot], np.ones(int(boot.sum()))] @ Wf.T)[:, :3]
    rms = float(np.sqrt(((Vp[boot] - rigid) ** 2).sum(1).mean())) if boot.any() else 0.0
    return {"shin_centreline_max_dev_m": round(dev, 4), "boot_rms_m": round(rms, 5), "slabs": int(len(devs)), "boot_vertices": int(boot.sum())}


def corrective_refined(V, G, gnames, skel, upper_bone, lower_bone, side, deg, axis, planes, passes=4):
    """corrective() re-aimed up to `passes` times at the planes still short of 1 (the radial push about the axis point is not exactly area-preserving)."""
    target = dict(planes)
    dl, after = None, None
    for _ in range(passes):
        dl = corrective(V, G, gnames, skel, upper_bone, lower_bone, side, deg, axis, target)
        after = cross_ratio(V + dl, G, gnames, skel, upper_bone, lower_bone, side, deg, axis, rest=V)
        if after["planes_min"] is not None and after["planes_min"] >= 0.95:
            break
        target = {k: target[k] * min(after["planes"].get(k, 1.0), 1.0) for k in target}
    return dl, after


classify_report = {}


def self_intersections_classified(Vp, Vrest, polys, weld, sel_verts, knee, ax, band=0.06):
    """td ruling: (total, crease, front_or_side). A pair is in the CREASE when both triangles lie on the flexion side (the back of the knee, +Y in the rest pose,
    relative to the leg axis); everything else is FRONT/SIDE (visible)."""
    from mathutils import Vector
    from mathutils.bvhtree import BVHTree
    tris = []
    for p in polys:
        if all(sel_verts[v] for v in p):
            for k in range(1, len(p) - 1):
                tris.append((p[0], p[k], p[k + 1]))
    used = sorted({v for t in tris for v in t})
    remap = {v: i for i, v in enumerate(used)}
    bvh = BVHTree.FromPolygons([Vector(Vp[v]) for v in used], [tuple(remap[v] for v in t) for t in tris], epsilon=1e-6)
    wt = [frozenset(int(weld[v]) for v in t) for t in tris]
    cen = np.array([Vrest[list(t)].mean(0) for t in tris]) - knee
    along = cen @ ax
    rad = cen - np.outer(along, ax)
    in_crease = rad[:, 1] > 0   # td refined rule: the crease is the back of the knee at any axial position (the fold reaches beyond 6 cm)
    total = crease = 0
    out_along, out_back, not_back = [], [], [0]
    classify_report["pairs"] = []
    for a, b in bvh.overlap(bvh):
        if a < b and not (wt[a] & wt[b]):
            total += 1
            if in_crease[a] and in_crease[b]:
                crease += 1
            else:
                classify_report.setdefault("pairs", []).append((tris[a], tris[b]))
                out_along.append(0.5 * (along[a] + along[b]))
                if not (rad[a, 1] > 0 and rad[b, 1] > 0):
                    not_back[0] += 1
                out_back.append(0.5 * (rad[a, 1] + rad[b, 1]))
    classify_report["last"] = {"outside_along_m": [round(float(np.percentile(out_along, p)), 3) for p in (0, 50, 100)] if out_along else [],
                               "outside_on_back_fraction": round(float(np.mean(np.array(out_back) > 0)), 2) if out_back else None, "outside_not_both_on_back": not_back[0]}
    return total, crease, total - crease
