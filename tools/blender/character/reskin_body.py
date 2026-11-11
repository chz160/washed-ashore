"""The P4 body fit for reskin_survivor.py and skeleton_stills.py (pod 49): his mesh onto Skeleton_Human by rotation only, fingers reposed (F7),
weights onto the game bone names, a wider blend band at the knees and elbows (DK1), rigid watch weights (Addendum 3). Returns arrays; exports nothing."""
import json

import numpy as np

import reskin_map as M
import reskin_io as R
from reskin_fingers import straighten
from reskin_fit import repose, unit, watch_vertices
from reskin_split import Fail
from reskin_tex import luma_at

import os  # noqa: E402
BAND_SHIFT = float(os.environ.get("RESKIN_BAND_SHIFT", "0.0"))
# per limb transition-centre shifts found by trial (pod49 out/shift_*): the right knee's pinch sits 2 cm below the joint, so its blend moves 1 cm up
SHIFTS = {("LowerLeg", "R"): -0.01}  # m, moves the blend centre along the limb (trial parameter; positive = toward the lower segment)
BAND = 0.06          # m, half width of the knee and elbow blend band (td-49 DK1)
BAND_RADIUS = 0.12   # m, distance from the limb axis inside which vertices belong to the limb
L2 = {"LowerArm": 0.02, "Wrist": 0.02, "LowerLeg": 0.03}  # director L2: how far a skeleton joint may sit from his mesh's joint (m)
MITTEN = os.environ.get("RESKIN_FINGERS", "mitten") == "mitten"   # 49 ships mitten hands (ruling 2); RESKIN_FINGERS=chain tries the F7'/F7'' weights
WEB_BAND, WEB_ITERS = 0.02, 8   # DK3: half width (m) of the thumb/palm band and the number of diffusion passes [my choice]
WATCH_BAND = 0.03   # m, the hand-side blend band after the watch (td ruling 3)
EDGE_P99, EDGE_MAX = 1.5, 3.0  # td R6: hand edge-length ratio limits after the finger repose


def smooth(t):
    t = np.clip(t, 0, 1)
    return t * t * (3 - 2 * t)


def load_skeleton(path):
    sk = json.load(open(path))
    return {n: {"p": d["p"], "h": np.array(d["h"]), "t": np.array(d["t"]), "m": np.array(d["m"]), "R": np.array(d["m"])[:3, :3]} for n, d in sk.items()}


def widen_band(G, gi, V3, skel, side, upper, lower, hip_bone, report):
    """Blend the weight of two neighbouring bones along the limb axis over +-BAND around the joint (replaces Meshy's weights in that band only)."""
    a = skel[hip_bone + "." + side]["h"]          # start of the upper segment (hip or shoulder)
    j = skel[lower + "." + side]["h"]             # the joint
    axis = unit(j - a)
    iu, il = gi[upper + "." + side], gi[lower + "." + side]
    rel = V3 - j
    d = rel @ axis
    perp = np.linalg.norm(rel - np.outer(d, axis), axis=1)
    sgn = 1.0 if side == "L" else -1.0
    S = G[:, iu] + G[:, il]
    sel = (np.abs(d) <= BAND) & (perp < BAND_RADIUS) & (V3[:, 0] * sgn > 0) & (S >= 0.5)
    t = smooth((d[sel] - BAND_SHIFT - SHIFTS.get((lower, side), 0.0) + BAND) / (2 * BAND))
    G[sel, il] = S[sel] * t
    G[sel, iu] = S[sel] * (1 - t)
    report["%s.%s" % (lower, side)] = int(sel.sum())


def smooth_web(G, gi, V3, hand_w, weld, edges, boundary_y, side, report):
    """DK3: soften the web between the thumb and the palm. Over a 4 cm lateral band around the thumb/index boundary the weights over Wrist, Thumb1-3 and
    Index1-4 are diffused along the mesh (8 passes), then made equal across position-welded duplicates so the UV seams cannot open."""
    cols = [gi[n + "." + side] for n in ("Wrist", "Thumb1", "Thumb2", "Thumb3", "Index1", "Index2", "Index3", "Index4")]
    region = (hand_w >= 0.5) & (np.abs(V3[:, 1] - boundary_y) < WEB_BAND)
    ea, eb = edges[:, 0], edges[:, 1]
    both = region[ea] & region[eb]
    rows = np.where(region)[0]
    for _ in range(WEB_ITERS):
        acc = np.zeros((len(G), len(cols)))
        cnt = np.zeros(len(G))
        np.add.at(acc, ea[both], G[np.ix_(eb[both], cols)])
        np.add.at(acc, eb[both], G[np.ix_(ea[both], cols)])
        np.add.at(cnt, ea[both], 1)
        np.add.at(cnt, eb[both], 1)
        r = rows[cnt[rows] > 0]
        G[np.ix_(r, cols)] = 0.5 * G[np.ix_(r, cols)] + 0.5 * acc[r] / cnt[r, None]
        for w in np.unique(weld[rows]):
            grp = rows[weld[rows] == w]
            if len(grp) > 1:
                G[np.ix_(grp, cols)] = G[np.ix_(grp, cols)].mean(0)
    report["web.%s" % side] = int(region.sum())


KNEE_ABOVE, KNEE_BELOW = 0.06, 0.02   # td Update 2: the knee blend runs from 6 cm above the joint to only 2 cm below; LowerLeg is 1.0 below that
BOOT_COLLAR_ABOVE_ANKLE = 0.06        # m, boot collar height above his ankle joint [my choice; checked on the stills]
ANKLE_BAND = 0.03                     # m, LowerLeg -> Foot blend half width around the boot collar; Foot is 1.0 below it


def knee_and_ankle(G, gi, V3, skel, side, report):
    """td Update 2. (1) Asymmetric knee band so the shin stays straight below the knee. (2) Ankle band: LowerLeg to Foot only within +-3 cm of the boot
    collar, Foot at 1.0 below it (Meshy's Foot weights reach up the shin, and the IK foot lags the shin's end). Returns the boot collar height."""
    a, j = skel["UpperLeg." + side]["h"], skel["LowerLeg." + side]["h"]
    ax = unit(j - a)
    rel = V3 - j
    dd = rel @ ax
    perp = np.linalg.norm(rel - np.outer(dd, ax), axis=1)
    sgn = 1.0 if side == "L" else -1.0
    leg = (perp < BAND_RADIUS) & (V3[:, 0] * sgn > 0)
    iu, il, ifoot = gi["UpperLeg." + side], gi["LowerLeg." + side], gi["Foot." + side]
    # knee: above -> blend -> below
    S = G[:, iu] + G[:, il]
    kb = leg & (dd >= -KNEE_ABOVE) & (dd <= KNEE_BELOW) & (S >= 0.5)
    t = smooth((dd[kb] + KNEE_ABOVE) / (KNEE_ABOVE + KNEE_BELOW))
    G[kb, il] = S[kb] * t
    G[kb, iu] = S[kb] * (1 - t)
    below = leg & (dd > KNEE_BELOW) & (S >= 0.5)
    G[below, il] = S[below]
    G[below, iu] = 0.0
    # ankle: collar height from his ankle joint
    ankle_z = skel["Foot." + side]["h"][2]
    collar = ankle_z + BOOT_COLLAR_ABOVE_ANKLE
    T = G[:, il] + G[:, ifoot]
    ab = leg & (V3[:, 2] <= collar + ANKLE_BAND) & (V3[:, 2] > 0.0) & (dd > KNEE_BELOW) & (T >= 0.5)
    tt = smooth((V3[ab, 2] - (collar - ANKLE_BAND)) / (2 * ANKLE_BAND))
    G[ab, il] = T[ab] * tt
    G[ab, ifoot] = T[ab] * (1 - tt)
    # Meshy's Foot weights reach up the shin: above the ankle band every leg vertex follows the shin (LowerLeg takes the Foot and UpperLeg share)
    up = leg & (dd > KNEE_BELOW) & (V3[:, 2] > collar + ANKLE_BAND)
    T2 = G[:, il] + G[:, ifoot] + G[:, iu]
    up &= (T2 >= 0.5)
    G[up, il] = T2[up]
    G[up, ifoot] = 0.0
    G[up, iu] = 0.0
    report["knee." + side] = int(kb.sum() + below.sum())
    report["ankle." + side] = int(ab.sum())
    return collar


def fit_body(o, game, skel):
    """game = template bones (names, hierarchy, finger directions); skel = Skeleton_Human from skeleton_human.json."""
    mesh, V, N, W, names, mb = R.load_meshy(o["src"])
    me = mesh.data
    uv_v, lu = R.mesh_uvs(me)
    lum = luma_at(o["tex"], uv_v)
    mapping = dict(M.MESHY_TO_GAME)
    if o.get("force_fail") == "swap_sides":
        mapping = {n: (g[:-2] + (".R" if g.endswith(".L") else ".L") if g[-2:] in (".L", ".R") else g) for n, g in mapping.items()}
    R.side_checks(mb, game, mapping)
    floor = float(V[:, 2].min())
    hip = (mb[M.P + "LeftUpLeg"]["h"][:2] + mb[M.P + "RightUpLeg"]["h"][:2]) / 2
    ghip = (game["UpperLeg.L"]["h"][:2] + game["UpperLeg.R"]["h"][:2]) / 2
    c = np.array([0.0, hip[1], floor])
    g = np.array([0.0, ghip[1], 0.0])
    LN, LI = R.LAST_LOOPS
    V2, N2, head2, RAK = repose(V, N, W, names, mb, game, {}, (1.0, c, g), 0.0, M.LIMB_TARGET_P4, LN, LI)
    LN3 = repose.loop_normals
    c[2] = float(V2[:, 2].min())
    V3 = V2 - c + g
    head3 = {k: v - c + g for k, v in head2.items()}
    # D1/L2, fail closed: the skeleton joint (averaged left and right) against his own joint on the same side
    off = {}
    for gb, mbn in (("LowerArm", "ForeArm"), ("Wrist", "Hand"), ("LowerLeg", "Leg")):
        for sd, long in (("L", "Left"), ("R", "Right")):
            off["%s.%s" % (gb, sd)] = float(np.linalg.norm(skel["%s.%s" % (gb, sd)]["h"] - head3[M.P + long + mbn]))
    bad = {k: round(v, 4) for k, v in off.items() if v > L2[k.split(".")[0]]}
    if bad:
        raise Fail("a skeleton joint is off his mesh beyond L2: %s" % bad)
    weld = np.unique(np.round(V * 1e5).astype(np.int64), axis=0, return_inverse=True)[1].ravel()
    edges = np.array([[e.vertices[0], e.vertices[1]] for e in me.edges])
    G, gi, gnames = R.build_weights(W, names, game, mapping)
    N3 = N2
    fingers, edge_stats = {}, {}
    for side, hn in (("L", "mixamorig:LeftHand"), ("R", "mixamorig:RightHand")):
        hh = head3[hn]
        res, info = straighten(V3, N3, W[:, names.index(hn)], hh, game, edges, side, M.PALM_FRACTION, weld)
        fingers[side] = {k: v for k, v in info.items() if k != "chains"}
        if MITTEN:
            fingers[side]["fallback"] = "mitten shipped (director and td ruling 2); F7'' needs item 3's finger extremes"
            print("FINGERS %s: MITTEN weights (all hand vertices on Wrist), #35 Q10 still fails" % side)
            continue
        if res is None:
            print("FINGERS %s: FALLBACK to mitten weights (finger groups not found: %s); #35 Q10 still fails" % (side, fingers[side]))
            continue
        Vn, Nn, wts = res
        moved = np.where(np.linalg.norm(Vn - V3, axis=1) > 1e-9)[0]
        sel = np.zeros(len(V3), bool)
        sel[moved] = True
        ok = sel[edges[:, 0]] & sel[edges[:, 1]]
        if len(moved) == 0:   # F7': the geometry is untouched, so every edge ratio is exactly 1
            p99, mx = 1.0, 1.0
        else:
            ratio = np.linalg.norm(Vn[edges[ok, 0]] - Vn[edges[ok, 1]], axis=1) / np.maximum(np.linalg.norm(V3[edges[ok, 0]] - V3[edges[ok, 1]], axis=1), 1e-9)
            p99, mx = float(np.percentile(ratio, 99)), float(ratio.max())
        edge_stats[side] = {"edges": int(ok.sum()), "p99": round(p99, 3), "max": round(mx, 3)}
        print("FINGERS %s: reposed (F7), %s; hand edge-length ratio p99 %.3f (limit %.1f), max %.3f (limit %.1f)" % (side, fingers[side], p99, EDGE_P99, mx, EDGE_MAX))
        if p99 > EDGE_P99 or mx > EDGE_MAX:
            print("FINGERS %s: FALLBACK to mitten weights (edge-length limits exceeded); #35 Q10 still fails" % side)
            fingers[side]["fallback"] = "edge ratio"
            continue
        V3, N3 = Vn, Nn
        ws = G[:, gi["Wrist." + side]].copy()
        mv = np.zeros(len(V3))
        for bn, wv in wts.items():
            G[:, gi[bn]] += ws * wv
            mv += wv
        G[:, gi["Wrist." + side]] = ws * (1 - np.clip(mv, 0, 1))
    web = {}
    for side, hn in (("L", "mixamorig:LeftHand"), ("R", "mixamorig:RightHand")):
        if fingers[side].get("thumb_boundary_y") is not None and not fingers[side].get("fallback"):
            smooth_web(G, gi, V3, W[:, names.index(hn)], weld, edges, fingers[side]["thumb_boundary_y"], side, web)
    print("THUMB WEB smoothing, vertices in the band:", web)
    band = {}
    collars = {}
    for side in ("L", "R"):
        collars[side] = knee_and_ankle(G, gi, V3, skel, side, band)
        widen_band(G, gi, V3, skel, side, "UpperArm", "LowerArm", "UpperArm", band)
    print("BLEND BAND (+-%.2f m around the knees and elbows), vertices re-weighted:" % BAND, band)
    G = R.clean(G)
    wid = watch_vertices(V, lum, mb["mixamorig:RightHand"]["h"])
    lid = watch_vertices(V, lum, mb["mixamorig:LeftHand"]["h"])
    print("WATCH vertices: right wrist %d, left wrist %d" % (len(wid), len(lid)))
    if len(wid) < 30 or len(lid) * 4 > len(wid):
        raise Fail("watch check: expected on the RIGHT wrist (right %d, left %d vertices)" % (len(wid), len(lid)))
    cen = V3[wid].mean(0)
    # Rigid by construction: ONE bone at weight 1.0. A weight set shared by two bones that move differently (Wrist.R and LowerArm.R at the
    # wrist-flex extreme) is a blend of two transforms, not a rigid one: measured 8 mm of size change. The watch rides the RIGHT forearm.
    row = np.zeros(G.shape[1])
    row[gi["LowerArm.R"]] = 1.0
    G[wid] = row
    wdiff = float(np.abs(G[wid] - row).max())
    top = [(gnames[k], round(float(row[k]), 3)) for k in np.argsort(row)[::-1] if row[k] > 0]
    print("WATCH weights (identical on all %d vertices): %s, max per-vertex difference %.1f" % (len(wid), top, wdiff))
    if not all(n.endswith(".R") for n, _ in top):
        raise Fail("watch weights are not on the RIGHT arm: %s" % top)
    # td ruling 3: a blend band on the hand side of the watch only (watch edge to 3 cm toward the hand), LowerArm.R to Wrist.R, smoothed
    ax = unit(skel["Wrist.R"]["h"] - skel["LowerArm.R"]["h"])
    p_edge = float((V3[wid] @ ax).max())
    rel = V3 - skel["LowerArm.R"]["h"]
    perp = np.linalg.norm(rel - np.outer(rel @ ax, ax), axis=1)
    proj = V3 @ ax
    inw = np.zeros(len(V3), bool)
    inw[wid] = True
    iu, il = gi["LowerArm.R"], gi["Wrist.R"]
    S = G[:, iu] + G[:, il]
    bsel = (~inw) & (proj > p_edge) & (proj < p_edge + WATCH_BAND) & (perp < 0.08) & (V3[:, 0] < 0) & (S >= 0.5)
    t = smooth((proj[bsel] - p_edge) / WATCH_BAND)
    G[bsel, il] = S[bsel] * t
    G[bsel, iu] = S[bsel] * (1 - t)
    print("WATCH hand-side blend band: %d vertices over %.0f mm from the watch edge toward the hand" % (int(bsel.sum()), 1000 * WATCH_BAND))
    zero = int((G.sum(1) < 0.999).sum())
    if zero:
        raise Fail("%d vertices have no weight (R2)" % zero)
    counts = {b: int((G[:, gi[b]] > 0.01).sum()) for b in gnames}
    fz = [b for b in gnames if b.split(".")[0] in M.FINGER_BONES and not b.endswith("_end") and counts[b] == 0
          and not fingers[b[-1]].get("fallback") and fingers[b[-1]].get("components") == 5]
    if fz:
        raise Fail("finger bones with 0 vertices: %s" % fz)
    polys = [tuple(p.vertices) for p in me.polygons]
    return {"LN0": LN, "LN": LN3, "V0": V - c + g, "V": V3, "N": N3, "G": G, "gnames": gnames, "gi": gi, "polys": polys, "loop_uv": lu, "uv_v": uv_v, "watch": wid, "W": W, "names": names,
            "mesh": mesh, "weld": weld, "joint_offsets": off, "fingers": fingers, "edge_stats": edge_stats, "band": band, "counts": counts,
            "watch_diff": wdiff, "collars": collars, "watch_band": np.where(bsel)[0], "edges": edges, "web": web, "watch_weights": top, "floor_shift": float(V2[:, 2].min() - floor), "lum": lum}
