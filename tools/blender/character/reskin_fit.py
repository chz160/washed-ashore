"""Repose, per-segment fit, spine redistribution, rigid watch and normals for reskin_survivor.py (pod 49)."""
import numpy as np

from reskin_split import Fail


def unit(v):
    n = np.linalg.norm(v)
    return v / n if n > 1e-12 else v


def swing(a, b):
    """Minimal rotation taking unit a to unit b (Rodrigues)."""
    a, b = unit(a), unit(b)
    c = float(a @ b)
    if c > 0.999999:
        return np.eye(3)
    if c < -0.999999:
        raise Fail("swing of 180 degrees")
    v = np.cross(a, b)
    k = np.array([[0, -v[2], v[1]], [v[2], 0, -v[0]], [-v[1], v[0], 0]])
    return np.eye(3) + k + k @ k / (1 + c)


def depth(b, n):
    d = 0
    while b[n]["p"]:
        n, d = b[n]["p"], d + 1
    return d


def target_point(spec, game, ankle_z):
    """Game-frame landing point of a fitted joint. ('joint', bone) = that bone's head; ('line', bone, z) = point on that bone's line at height z."""
    if spec[0] == "joint":
        return game[spec[1]]["h"]
    h, t = game[spec[1]]["h"], game[spec[1]]["t"]
    return h + (t - h) * ((h[2] - ankle_z) / (h[2] - t[2]))


def repose(V, N, W, names, mb, game, fit, tf, ankle_z, limb_target, loopN=None, loopV=None):
    """Rotate limb bones onto game directions (fit == {}: diagnostic, rotation only). With a fit table, each fitted bone is aimed at its landing
    joint and scaled ALONG ITS OWN AXIS by the distance ratio, so the joint lands on the game joint. tf = (s, c, g): game = s * (meshy - c) + g.
    Returns posed V, posed N, posed joint heads, per-bone (R, A, K)."""
    s, c, g = tf
    order = sorted(mb, key=lambda n: depth(mb, n))
    R, K, head2, A = {}, {}, {}, {}
    for n in order:
        b = mb[n]
        p = b["p"]
        head2[n] = b["h"] if p is None else head2[p] + R[p] @ ((b["h"] - mb[p]["h"]) * K[p])
        d_rest = unit(b["t"] - b["h"])
        length = float(np.linalg.norm(b["t"] - b["h"]))
        K[n] = 1.0
        if n in fit:
            tgt = (target_point(fit[n], game, ankle_z) - g) / s + c
            R[n] = swing(d_rest, unit(tgt - head2[n]))
            K[n] = float(np.linalg.norm(tgt - head2[n]) / length)
        elif n in limb_target:
            g0, g1 = limb_target[n]
            d_t = unit(game[g1]["h"] - game[g0]["h"]) if g1 != "_tail" else unit(game[g0]["t"] - game[g0]["h"])
            R[n] = swing(d_rest, d_t)
        else:
            R[n] = np.eye(3)
        A[n] = np.eye(3) + (K[n] - 1.0) * np.outer(d_rest, d_rest)
    out = np.zeros_like(V)
    nout = np.zeros_like(V)
    lout = None if loopN is None else np.zeros_like(loopN)
    for i, n in enumerate(names):
        w = W[:, i]
        m = w > 1e-6
        if not m.any():
            continue
        L = R[n] @ A[n]
        out[m] += w[m, None] * (head2[n] + (V[m] - mb[n]["h"]) @ L.T)
        nout[m] += w[m, None] * (N[m] @ np.linalg.inv(L))  # inverse transpose applied to row vectors
        if lout is not None:
            wl = w[loopV]
            ml = wl > 1e-6
            lout[ml] += wl[ml, None] * (loopN[ml] @ np.linalg.inv(L))
    nl = np.linalg.norm(nout, axis=1)
    nout /= np.maximum(nl, 1e-12)[:, None]
    if lout is not None:
        lout /= np.maximum(np.linalg.norm(lout, axis=1), 1e-12)[:, None]
    repose.loop_normals = lout
    return out, nout, head2, {n: (R[n], A[n], K[n]) for n in order}


def watch_vertices(V, uv_lum, hand_head, radius=0.09, grow=0.012):
    """Watch = near-black texels within `radius` of the wrist plus everything within `grow` of them (case, strap, glass)."""
    seeds = np.where((np.linalg.norm(V - hand_head, axis=1) < radius) & (uv_lum < 0.10))[0]
    if len(seeds) == 0:
        return seeds
    near = np.zeros(len(V), bool)
    for k in range(0, len(seeds), 32):
        d = np.linalg.norm(V[:, None, :] - V[seeds[k:k + 32]][None, :, :], axis=2).min(1)
        near |= d < grow
    near &= np.linalg.norm(V - hand_head, axis=1) < radius + 0.03
    return np.where(near)[0]


def rigid_watch(V, V2, ids, bone, mb, head2, RAK, W, names):
    """Re-place the watch with the forearm's rotation and NO axial scale, keeping its centroid on the fitted arm. Returns new V2 rows and its size before/after."""
    R, A, K = RAK[bone]
    h = mb[bone]["h"]
    i = names.index(bone)
    c0 = V[ids].mean(0)
    # centroid carried by the fitted bone only (scale included), the shape by the rotation alone
    cfit = head2[bone] + (c0 - h) @ (R @ A).T
    new = cfit + (V[ids] - c0) @ R.T
    def extents(P):  # rotation-invariant size: extent along the principal axes
        Q = P - P.mean(0)
        axes = np.linalg.svd(Q, full_matrices=False)[2]
        proj = Q @ axes.T
        return proj.max(0) - proj.min(0)
    return new, extents(V[ids]), extents(new)


def spine_weights(G, gi, W, names, V3, game, spine_names=("mixamorig:Hips", "mixamorig:Spine", "mixamorig:Spine1", "mixamorig:Spine2"),
                  targets=("Hips", "Abdomen", "Torso", "Chest")):
    """F6: redistribute only the Hips+Spine+Spine1+Spine2 weight sum over Hips/Abdomen/Torso/Chest by vertex height at the GAME joint heights."""
    S = sum(W[:, names.index(n)] for n in spine_names)
    for t in targets:
        G[:, gi[t]] = 0.0
    hts = np.array([game[t]["h"][2] for t in targets])
    z = V3[:, 2]
    for k in range(len(targets)):
        if k == 0:
            w = np.where(z <= hts[0], 1.0, np.clip((hts[1] - z) / (hts[1] - hts[0]), 0, 1))
        elif k == len(targets) - 1:
            w = np.where(z >= hts[-1], 1.0, np.clip((z - hts[-2]) / (hts[-1] - hts[-2]), 0, 1))
        else:
            up = np.clip((z - hts[k - 1]) / (hts[k] - hts[k - 1]), 0, 1)
            dn = np.clip((hts[k + 1] - z) / (hts[k + 1] - hts[k]), 0, 1)
            w = np.minimum(up, dn)
        G[:, gi[targets[k]]] = S * w
    return {t: round(float(h), 3) for t, h in zip(targets, hts)}
