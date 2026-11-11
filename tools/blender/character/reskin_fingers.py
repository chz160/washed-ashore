"""td-49 F7: repose the Meshy fingers instead of weighting a curled hand to straight game bones (pod 49).

Per hand: the five finger groups (lateral k-means of the vertices past the palm), ordered thumb to pinky by position; for each, a temporary chain
fitted to its centreline (PCA axis, joints at equal parts of its length), weighted along the finger, rotated onto the Farmer bind finger directions
(chain bone i = game bone i), then named as the game finger bones. Fingers are never scaled. If the five blobs are not found the caller falls back
to mitten weights (all hand vertices on Wrist) and reports it.
"""
import numpy as np

from reskin_fit import swing, unit

RAMP = 0.30  # half width of the weight ramp across a finger joint, as a fraction of a segment [my choice, DK3]
REPOSE = False  # True = the td F7 straightening (kept for reference; fails the R6 edge-length limits on this mesh)
THUMB_FROM = 0.10  # fraction of the hand length where the thumb band starts [my choice, checked on the thumb stills]
ORDER = ["Thumb", "Index", "Middle", "Ring", "Pinky"]
BONES = {"Thumb": 3, "Index": 4, "Middle": 4, "Ring": 4, "Pinky": 4}


def components(cand, edges, weld, V3):
    """The five finger groups. The fingers touch each other, so the welded mesh joins them into one blob: group by lateral position instead
    (1-D k-means with k = 5 on Y, the width of the T-pose hand; thumb at -Y). Groups must hold 30+ vertices and sit 8 mm or more apart."""
    idx = np.where(cand)[0]
    if len(idx) < 150:
        return []
    y = V3[idx, 1]
    cen = np.linspace(y.min(), y.max(), 5)
    for _ in range(40):
        lab = np.argmin(np.abs(y[:, None] - cen[None, :]), axis=1)
        new = np.array([y[lab == k].mean() if (lab == k).any() else cen[k] for k in range(5)])
        if np.allclose(new, cen, atol=1e-6):
            break
        cen = new
    lab = np.argmin(np.abs(y[:, None] - cen[None, :]), axis=1)
    if np.diff(np.sort(cen)).min() < 0.008:
        return []
    out = [idx[lab == k] for k in range(5)]
    return [o for o in out if len(o) >= 30] if all(len(o) >= 30 for o in out) else []


def straighten(V3, N3, W_hand, hand_head, game, edges, side, palm_fraction, weld):
    """Returns (V3', N3', weights dict {game bone: per-vertex weight array}, info) or None when the fingers cannot be found cleanly."""
    wr = game["Wrist." + side]["h"]
    axis = unit(game["Middle1." + side]["h"] - wr)
    region = W_hand >= 0.5
    al = (V3 - hand_head) @ axis
    ext = float(al[region].max())
    cand = region & (al > palm_fraction * ext)
    comps = components(cand, edges, weld, V3)
    info = {"components": len(comps), "sizes": [int(len(c)) for c in comps]}
    if len(comps) != 5:
        return None, info
    comps.sort(key=lambda c: float(V3[c, 1].mean()))  # thumb is the most forward (-Y) in the T bind, pinky the most back
    V = V3.copy()
    Nn = N3.copy()
    wts = {}
    base = V3[region & (al <= palm_fraction * ext + 0.01) & (al > palm_fraction * ext - 0.02)]
    base = base.mean(0) if len(base) else hand_head
    worst = 0.0
    # the thumb starts near the wrist (its base is the thenar): take the whole lateral band from 10% of the hand, not from the palm line
    y_t, y_i = float(V3[comps[0], 1].mean()), float(V3[comps[1], 1].mean())
    boundary = (y_t + y_i) / 2
    comps[0] = np.where(region & (al > THUMB_FROM * ext) & (V3[:, 1] <= boundary))[0]
    for name, comp in zip(ORDER, comps):
        pts = V3[comp]
        q = pts - pts.mean(0)
        pa = np.linalg.svd(q, full_matrices=False)[2][0]
        if (pts.mean(0) - base) @ pa < 0:
            pa = -pa
        u = (pts - base) @ pa
        lo, hi = float(u.min()), float(u.max())
        n = BONES[name]
        edges_u = [lo + (hi - lo) * k / n for k in range(n + 1)]
        joints = []
        for k in range(n + 1):
            w = 0.08 * (hi - lo)
            sel = np.abs(u - edges_u[k]) < w
            joints.append(pts[sel].mean(0) if sel.any() else base + pa * edges_u[k])
        gb = [game["%s%d.%s" % (name, i + 1, side)] for i in range(n)]
        info.setdefault("chains", {})[name] = [p.copy() for p in joints]  # filled below with the straightened heads
        Rm, head = [], [joints[0]]
        for i in range(n):
            d = unit(joints[i + 1] - joints[i])
            D = unit(gb[i]["t"] - gb[i]["h"])
            Rm.append(swing(d, D))
            head.append(head[i] + Rm[i] @ (joints[i + 1] - joints[i]))
        if not REPOSE:  # F7': joints ON his curled fingers, geometry untouched (straightening stretched the skin between touching fingers: edge ratio p99 6.8)
            head = [j.copy() for j in joints]
            Rm = [np.eye(3) for _ in range(n)]
        info["chains"][name] = [h.copy() for h in head]
        delta = RAMP * (hi - lo) / n
        w = np.zeros((len(comp), n))
        for i in range(n):
            up = np.ones(len(comp)) if i == 0 else np.clip((u - (edges_u[i] - delta)) / (2 * delta), 0, 1)
            dn = np.ones(len(comp)) if i == n - 1 else 1 - np.clip((u - (edges_u[i + 1] - delta)) / (2 * delta), 0, 1)
            w[:, i] = up * dn
        w /= np.maximum(w.sum(1), 1e-9)[:, None]
        newv = np.zeros_like(pts)
        newn = np.zeros_like(pts)
        for i in range(n):
            newv += w[:, i, None] * (head[i] + (pts - joints[i]) @ Rm[i].T)
            newn += w[:, i, None] * (N3[comp] @ Rm[i].T)
        V[comp] = newv
        Nn[comp] = newn / np.maximum(np.linalg.norm(newn, axis=1), 1e-12)[:, None]
        worst = max(worst, float(np.linalg.norm(newv - pts, axis=1).max()))
        for i in range(n):
            full = np.zeros(len(V3))
            full[comp] = w[:, i]
            wts["%s%d.%s" % (name, i + 1, side)] = full
    info["max_vertex_move_m"] = round(worst, 4)
    info["thumb_boundary_y"] = float(boundary)
    return (V, Nn, wts), info
