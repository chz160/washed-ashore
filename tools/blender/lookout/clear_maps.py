"""Clear-region maps for landings: where a body of radius 0.30 m can stand. Not part of the build.

Run with the system Python from the repo root:  python -B tools/blender/lookout/clear_maps.py [landing numbers]
Writes TestResults/tower-art/landing-clear-maps.txt. Everything comes from the parameter table through the same
layout code the build uses. Tower-local metres, X east, Z north (the design frame's x, y). Grid 5 mm.
"""
import json
import math
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import mc39_poly as poly                    # noqa: E402
from mc39_layout import Layout              # noqa: E402
from mc39_tower import Tower                # noqa: E402

G = 0.005
R_STAND, R_BODY = 0.30, 0.25
RUN = 0.34          # built-in straight run length
STOP_BEND = 45.0    # a junction that stops the player bends at least this much (carry-through lever 35 + 10)


def f3(p):
    return f"({p[0]:.3f}, {p[1]:.3f})"


def main():
    with open(os.path.join(HERE, "mc39_params.json"), encoding="utf-8") as fh:
        P = json.load(fh)
    L = Layout(P)
    T = Tower(P, L)
    T.frame(0); T.stairs(0); T.landings(0)          # fills T.obstacles (member centre lines with radii)
    ks = [int(a) for a in sys.argv[1:]] or [5, 6, 7]
    out = [f"Landing clear-region maps. Tower-local metres, X east, Z north. Grid 5 mm. From mc39_params.json via mc39_layout / mc39_tower.",
           "A 'wall' is a CLIP wall on a drawn guard-rail plane. A 'mouth' is a stretch of deck edge with no rail because a flight stands there.",
           f"Standing circle {R_STAND} m; body on the move {R_BODY} m.",
           "Distances at a named point and along a named segment are analytic (point to segment, segment to segment). Hand-over points are picked from the grid"
           " (5 mm; 10 mm where a region has more than 900 points), so a better point may exist within one grid step; the distances quoted for the picked point are exact.",
           "NOTE: the HAND-OVER / SELECT / STOP-AND-TURN lines come from this script's own earlier search, which samples regions of more than 900 nodes on a 10 mm grid (landings 1-5) and does not refine one-segment picks. The set is the BUILD MODULE OUTPUT section at the end.",
           "CLIP walls are generated on the rail planes from the same numbers; the manifest rounds coordinates to 0.01 mm.", ""]
    for k in ks:
        outline = L.landing_polygon(k)
        walls = T.guard_runs(k, under_stair=True)
        opens = T.edge_openings(k)
        mouths = []
        for (i, a, b, s0, s1, why) in opens:
            ln = math.hypot(b[0] - a[0], b[1] - a[1])
            d = ((b[0] - a[0]) / ln, (b[1] - a[1]) / ln)
            mouths.append(((a[0] + d[0] * s0, a[1] + d[1] * s0), (a[0] + d[0] * s1, a[1] + d[1] * s1), why))
        ends = [(f"flight {f['index']} {end}", q) for f, end, q in T.landing_ends(k)]
        out.append(f"LANDING {k}  (deck Y = {L.levels[k]:.4f}; {'drawn frame (interpretation)' if L.is_frame_landing(k) else 'platform cut by the faces'})")
        out.append("  deck outline: " + " ".join(f3(p) for p in outline))
        for i, (a, b) in enumerate(walls, 1):
            out.append(f"  wall {i} (CLIP_BOX_Landing{k:02d}_{i:02d}, on a rail plane): {f3(a)} to {f3(b)}  length {math.hypot(b[0]-a[0], b[1]-a[1]):.3f}")
        for a, b, why in mouths:
            out.append(f"  mouth ({why}): {f3(a)} to {f3(b)}")
        for nm, q in ends:
            out.append(f"  lane end, {nm}: {f3(q)}; nearest wall {min(poly.seg_dist(q, a, b) for a, b in walls):.3f}")
        x0, y0, x1, y1 = poly.bbox(outline)

        def wall_d(q):
            return min(poly.seg_dist(q, a, b) for a, b in walls)

        def mouth_d(q):
            return min(poly.seg_dist(q, a, b) for a, b, _ in mouths) if mouths else 9.9
        pts = []
        for i in range(int((x1 - x0) / G) + 1):
            for j in range(int((y1 - y0) / G) + 1):
                q = (x0 + i * G, y0 + j * G)
                if poly.inside(outline, q) and wall_d(q) >= R_STAND:
                    pts.append(q)
        strict = [q for q in pts if mouth_d(q) >= R_STAND]
        for label, S in ((f"A. centres {R_STAND} m from every wall (stair mouths not counted as drops)", pts),
                         (f"B. centres {R_STAND} m from every wall AND {R_STAND} m from every mouth edge", strict)):
            if not S:
                out.append(f"  {label}: NONE")
                continue
            bx = (min(p[0] for p in S), max(p[0] for p in S), min(p[1] for p in S), max(p[1] for p in S))
            out.append(f"  {label}: {len(S) * G * G:.4f} m2 of centre positions; X {bx[0]:.3f}..{bx[1]:.3f}, Z {bx[2]:.3f}..{bx[3]:.3f}")
            near = []
            for nm, q in ends:
                c = min(S, key=lambda p: math.hypot(p[0] - q[0], p[1] - q[1]))
                dist = math.hypot(c[0] - q[0], c[1] - q[1])
                brg = (math.degrees(math.atan2(c[0] - q[0], c[1] - q[1])) + 360.0) % 360.0
                n = max(1, int(dist / G))
                worst = min(wall_d((q[0] + (c[0] - q[0]) * t / n, q[1] + (c[1] - q[1]) * t / n)) for t in range(n + 1))
                on_deck = all(poly.inside(outline, (q[0] + (c[0] - q[0]) * t / n, q[1] + (c[1] - q[1]) * t / n), 1e-6) for t in range(n + 1))
                out.append(f"     from {nm}: nearest centre {f3(c)}, {dist:.3f} m at bearing {brg:.0f} deg (0 = north, 90 = east); "
                           f"wall dist at that centre {wall_d(c):.3f}, mouth dist {mouth_d(c):.3f}; "
                           f"straight line: smallest wall distance {worst:.3f} ({'clear' if worst >= R_BODY else 'NOT clear'} for a {R_BODY} m body), "
                           f"{'on the deck throughout' if on_deck else 'leaves the deck'}")
                near.append(c)
            if len(near) == 2:
                a, b = near
                dist = math.hypot(b[0] - a[0], b[1] - a[1])
                n = max(1, int(dist / G))
                worst = min(wall_d((a[0] + (b[0] - a[0]) * t / n, a[1] + (b[1] - a[1]) * t / n)) for t in range(n + 1))
                out.append(f"     between those two centres: {dist:.3f} m straight; smallest wall distance on the way {worst:.3f} "
                           f"({'clear' if worst >= R_BODY else 'NOT clear'} for a {R_BODY} m body)")
        # Exact margins: distances to walls and mouth edges are analytic (point to segment); the search for the best
        # point is refined from the 5 mm grid down to 0.01 mm, so the figures below do not depend on the grid.
        def margin(q):
            return min(wall_d(q), mouth_d(q)) if poly.inside(outline, q, 1e-9) else -1.0
        if strict:
            c = max(strict, key=margin)
            step = G
            while step > 1e-5:
                step /= 2.0
                c = max(((c[0] + dx * step, c[1] + dy * step) for dx in (-1, 0, 1) for dy in (-1, 0, 1)), key=margin)
            out.append(f"  EXACT: the roomiest point of the deck is {f3(c)}: {wall_d(c):.4f} m to the nearest wall, {mouth_d(c):.4f} m to the nearest mouth edge; "
                       f"so a circle of up to {margin(c):.4f} m fits clear of both (search refined to 0.01 mm)")
            for nm, q in ends:
                f = next(fl for fl, end, qq in T.landing_ends(k) if qq is q)
                head = nm.endswith("head")
                travel = (math.degrees(math.atan2(f["dir"][0], f["dir"][1])) + (0.0 if head else 180.0)) % 360.0
                cB = min(strict, key=lambda p_: math.hypot(p_[0] - q[0], p_[1] - q[1]))
                brg = math.degrees(math.atan2(cB[0] - q[0], cB[1] - q[1])) % 360.0
                other = next(qq for _, _, qq in T.landing_ends(k) if qq is not q)
                ob = math.degrees(math.atan2(other[0] - q[0], other[1] - q[1])) % 360.0
                rel = (brg - travel + 180.0) % 360.0 - 180.0
                relo = (ob - travel + 180.0) % 360.0 - 180.0
                out.append(f"  TURN at {nm}: travelling {travel:.0f} deg on arrival; B centre is {rel:+.0f} deg from straight ahead (exact wall {wall_d(cB):.4f}, mouth {mouth_d(cB):.4f}); "
                           f"the other lane end is {relo:+.0f} deg; {'same way' if rel * relo >= 0 else 'OPPOSITE way'}; {'within' if abs(rel) <= 30 else 'beyond'} 30 deg")
        # HAND-OVER POINTS, chosen for margin (t-td): one per lane end, inside region B, the straight line from the lane
        # end holding R_STAND to every wall along its whole length, the two points at least R_STAND apart with the line
        # between them also holding R_STAND; among those, the pair whose smaller margin is largest.
        def crosses(a, b, c, d):
            def o(p_, q_, r_):
                return (q_[0] - p_[0]) * (r_[1] - p_[1]) - (q_[1] - p_[1]) * (r_[0] - p_[0])
            return o(a, b, c) * o(a, b, d) < 0 and o(c, d, a) * o(c, d, b) < 0

        def line_wall(a, b):
            """Exact smallest distance from segment a-b to any wall segment (analytic, no sampling)."""
            best_ = 9.9
            for wa, wb in walls:
                if crosses(a, b, wa, wb):
                    return 0.0
                best_ = min(best_, poly.seg_dist(a, wa, wb), poly.seg_dist(b, wa, wb), poly.seg_dist(wa, a, b), poly.seg_dist(wb, a, b))
            return best_
        sub_ = [q for q in strict if (round((q[0] - x0) / G) % 2 == 0 and round((q[1] - y0) / G) % 2 == 0)] if len(strict) > 900 else strict
        cands = []
        for nm, q in ends:
            cands.append([c for c in sub_ if line_wall(q, c) >= R_STAND])
        out.append(f"  HAND-OVER: candidates with a line holding {R_STAND} m to every wall: {len(cands[0])} for {ends[0][0]}, {len(cands[1])} for {ends[1][0]} (of {len(sub_)} region-B grid points)")
        best = None
        if cands[0] and cands[1]:
            c1s = sorted(cands[1], key=margin, reverse=True)
            for a in sorted(cands[0], key=margin, reverse=True):
                ma = margin(a)
                if best and ma <= best[0]:
                    break
                for b in c1s:
                    m_ = min(ma, margin(b))
                    if best and m_ <= best[0]:
                        break
                    if math.hypot(a[0] - b[0], a[1] - b[1]) >= R_STAND and line_wall(a, b) >= R_STAND:
                        best = (m_, a, b)
                        break
        if not best:
            out.append(f"  HAND-OVER: NO pair of points meets all the conditions on this landing")
        else:
            _, a, b = best
            for (nm, q), c in zip(ends, (a, b)):
                f = next(fl for fl, end, qq in T.landing_ends(k) if qq is q)
                travel = (math.degrees(math.atan2(f["dir"][0], f["dir"][1])) + (0.0 if nm.endswith("head") else 180.0)) % 360.0
                other = next(qq for _, _, qq in T.landing_ends(k) if qq is not q)
                rel = (math.degrees(math.atan2(c[0] - q[0], c[1] - q[1])) - travel + 180.0) % 360.0 - 180.0
                relo = (math.degrees(math.atan2(other[0] - q[0], other[1] - q[1])) - travel + 180.0) % 360.0 - 180.0
                out.append(f"  HAND-OVER {nm}: point {f3(c)}; exact wall {wall_d(c):.4f}, mouth {mouth_d(c):.4f}, margin over {R_STAND}: {margin(c) - R_STAND:+.4f}; "
                           f"run-out {math.hypot(c[0]-q[0], c[1]-q[1]):.3f} m, smallest wall distance along it {line_wall(q, c):.4f}; "
                           f"turn {rel:+.0f} deg from the direction of travel ({travel:.0f}), next flight at {relo:+.0f} deg: "
                           f"{'same way' if rel * relo >= 0 or abs(rel) < 2 else 'OPPOSITE way'}, {'within' if abs(rel) <= 30 else 'beyond'} 30 deg")
            ncr = max(1, int(math.hypot(a[0] - b[0], a[1] - b[1]) / 0.001))
            mcr = min(mouth_d((a[0] + (b[0] - a[0]) * t / ncr, a[1] + (b[1] - a[1]) * t / ncr)) for t in range(ncr + 1))
            out.append(f"  HAND-OVER crossing between the two points: {math.hypot(a[0]-b[0], a[1]-b[1]):.3f} m; smallest wall distance along it {line_wall(a, b):.4f}; smallest distance to a stair mouth edge along it {mcr:.4f}")
        # Trade of point margin against crossing length (asked for landing 7): best smaller-margin for a required crossing.
        if best and cands[0] and cands[1]:
            for need in (0.31, 0.32, 0.35):
                bb = None
                c1s = sorted(cands[1], key=margin, reverse=True)
                for a in sorted(cands[0], key=margin, reverse=True):
                    if bb and margin(a) <= bb[0]:
                        break
                    for b in c1s:
                        m_ = min(margin(a), margin(b))
                        if bb and m_ <= bb[0]:
                            break
                        if math.hypot(a[0] - b[0], a[1] - b[1]) >= need and line_wall(a, b) >= R_STAND:
                            bb = (m_, a, b)
                            break
                out.append(f"  CROSSING >= {need:.2f} m: " + (f"points {f3(bb[1])} and {f3(bb[2])}, crossing {math.hypot(bb[1][0]-bb[2][0], bb[1][1]-bb[2][1]):.3f} m, smaller point margin {bb[0] - R_STAND:+.4f}" if bb else "no pair"))
        # Two-segment run-out (t-td, amended R3), computed where the one-segment search found no pair: first segment
        # straight on from the lane end, then one bend to a region-B point; every sample on both holds R_STAND.
        if not best and strict:
            def lw2(a, b):
                n = max(1, int(math.hypot(b[0] - a[0], b[1] - a[1]) / 0.002))
                return min(wall_d((a[0] + (b[0] - a[0]) * t / n, a[1] + (b[1] - a[1]) * t / n)) for t in range(n + 1))
            feas = []
            for nm, q in ends:
                f = next(fl for fl, end, qq in T.landing_ends(k) if qq is q)
                sg = 1.0 if nm.endswith("head") else -1.0
                d = (f["dir"][0] * sg, f["dir"][1] * sg)
                other = next(qq for _, _, qq in T.landing_ends(k) if qq is not q)
                side_next = d[0] * (other[1] - q[1]) - d[1] * (other[0] - q[0])
                opts = []
                sv = 0.0
                while sv <= 0.6:
                    bend_pt = (q[0] + d[0] * sv, q[1] + d[1] * sv)
                    if not poly.inside(outline, bend_pt, 1e-6) or lw2(q, bend_pt) < R_STAND:
                        break
                    for c in strict:
                        v = (c[0] - bend_pt[0], c[1] - bend_pt[1])
                        ln = math.hypot(*v)
                        if ln < 0.05:
                            continue
                        ang = math.degrees(math.atan2(d[0] * v[1] - d[1] * v[0], d[0] * v[0] + d[1] * v[1]))
                        if lw2(bend_pt, c) >= R_STAND:
                            opts.append((abs(ang), sv, bend_pt, c, ang, ang * side_next >= 0, ln))
                    sv += 0.01
                feas.append(sorted(opts, key=lambda o_: o_[0]))
                out.append(f"  TWO-SEGMENT {nm}: {len(opts)} feasible (first length, point) combinations; straight-on reach before the first wall limit {sv - 0.01:.2f} m")
            pick = None
            if feas[0] and feas[1]:
                for a in feas[0][:400]:
                    for b in feas[1][:400]:
                        worst = max(a[0], b[0])
                        if pick and worst >= pick[0]:
                            break
                        if math.hypot(a[3][0] - b[3][0], a[3][1] - b[3][1]) >= R_STAND and lw2(a[3], b[3]) >= R_STAND:
                            pick = (worst, a, b)
                            break
            if not pick:
                out.append("  TWO-SEGMENT: NO pair of two-segment run-outs meets the conditions on this landing")
            else:
                for (nm, q), o_ in zip(ends, pick[1:]):
                    _, sv, bp, c, ang, same, ln = o_
                    out.append(f"  TWO-SEGMENT {nm}: first segment {sv:.3f} m straight on to {f3(bp)} (smallest wall distance {lw2(q, bp):.4f}); bend {ang:+.1f} deg "
                               f"({'same way as' if same else 'OPPOSITE to'} the turn to the next flight; {'under' if abs(ang) <= 30 else 'over'} 30); second segment {ln:.3f} m to {f3(c)} "
                               f"(smallest wall distance {lw2(bp, c):.4f}); point: wall {wall_d(c):.4f}, mouth {mouth_d(c):.4f}, margin {margin(c) - R_STAND:+.4f}")
                a, b = pick[1][3], pick[2][3]
                out.append(f"  TWO-SEGMENT crossing between the two points: {math.hypot(a[0]-b[0], a[1]-b[1]):.3f} m; smallest wall distance along it {lw2(a, b):.4f}")
        # STRAIGHT: the built-in run, RUN m straight on from each lane end (t-designer's laneRunOutLength)
        sp = []
        for nm, q in ends:
            f = next(fl for fl, end, qq in T.landing_ends(k) if qq is q)
            sg = 1.0 if nm.endswith("head") else -1.0
            c = (q[0] + f["dir"][0] * sg * RUN, q[1] + f["dir"][1] * sg * RUN)
            on = poly.inside(outline, c, 1e-6)
            sp.append(c)
            out.append(f"  STRAIGHT {nm}: point {RUN} m straight on {f3(c)}: {'on the deck' if on else 'OFF the deck'}; wall {wall_d(c):.4f}, mouth edge {mouth_d(c):.4f}; "
                       f"smallest wall distance along the run {line_wall(q, c):.4f}; {'holds' if on and min(wall_d(c), mouth_d(c), line_wall(q, c)) >= R_STAND else 'does NOT hold'} {R_STAND} m")
        ncs = max(1, int(math.hypot(sp[0][0] - sp[1][0], sp[0][1] - sp[1][1]) / 0.001))
        cs = [(sp[0][0] + (sp[1][0] - sp[0][0]) * t / ncs, sp[0][1] + (sp[1][1] - sp[0][1]) * t / ncs) for t in range(ncs + 1)]
        out.append(f"  STRAIGHT crossing between the two points: {math.hypot(sp[0][0]-sp[1][0], sp[0][1]-sp[1][1]):.3f} m; smallest wall distance along it {min(wall_d(c) for c in cs):.4f}; smallest mouth-edge distance along it {min(mouth_d(c) for c in cs):.4f}")
        # SELECT: t-td's selection rule for the points the ruling will fix.
        #  1 hard limits: every clearance >= R_STAND (point to walls and mouth edges, every sample of each segment, the
        #    crossing between the two points) and crossing length >= R_STAND
        #  2 margin target TARGET on each of those  3 one straight segment if rule 1 allows, else two (first straight on)
        #  4 a turn or bend is 'carried' up to 35 deg toward the next flight or 30 deg away; a larger one is a stop-and-turn
        #    and must be toward the next flight  5 the pair with the largest smallest-margin; ties to the smaller turn
        TARGET = 0.010
        if strict:
            pts_ = [q for q in strict if (round((q[0] - x0) / G) % 2 == 0 and round((q[1] - y0) / G) % 2 == 0)] if len(strict) > 900 else strict
            opts_by_end = []
            for nm, q in ends:
                f = next(fl for fl, end, qq in T.landing_ends(k) if qq is q)
                sg = 1.0 if nm.endswith("head") else -1.0
                d = (f["dir"][0] * sg, f["dir"][1] * sg)
                other = next(qq for _, _, qq in T.landing_ends(k) if qq is not q)
                side_next = d[0] * (other[1] - q[1]) - d[1] * (other[0] - q[0])

                def turn_of(a_, c_):
                    v = (c_[0] - a_[0], c_[1] - a_[1])
                    ang = math.degrees(math.atan2(d[0] * v[1] - d[1] * v[0], d[0] * v[0] + d[1] * v[1]))
                    toward = ang * side_next >= 0 or abs(ang) < 0.5
                    return ang, toward
                opts = []
                for c in pts_:
                    lw = line_wall(q, c)
                    if lw < R_STAND:
                        continue
                    ang, toward = turn_of(q, c)
                    if not toward and abs(ang) > 30:
                        continue
                    opts.append({"m": min(margin(c), lw) - R_STAND, "pt": c, "segs": [(q, c, lw)], "ang": ang, "toward": toward})
                if not opts:
                    sv = 0.01
                    while sv <= 0.6:
                        bp = (q[0] + d[0] * sv, q[1] + d[1] * sv)
                        l1 = line_wall(q, bp)
                        if not poly.inside(outline, bp, 1e-6) or l1 < R_STAND:
                            break
                        for c in pts_:
                            if math.hypot(c[0] - bp[0], c[1] - bp[1]) < 0.05:
                                continue
                            l2 = line_wall(bp, c)
                            if l2 < R_STAND:
                                continue
                            ang, toward = turn_of(bp, c)
                            if not toward:
                                continue
                            opts.append({"m": min(margin(c), l1, l2) - R_STAND, "pt": c, "segs": [(q, bp, l1), (bp, c, l2)], "ang": ang, "toward": toward})
                        sv += 0.01
                opts.sort(key=lambda o_: (-round(o_["m"], 4), abs(o_["ang"])))
                opts_by_end.append(opts)
            def pair_search(A, Bo):
                pick_ = None
                for a_ in A:
                    if pick_ and a_["m"] <= pick_[0]:
                        break
                    for b_ in Bo:
                        m_ = min(a_["m"], b_["m"])
                        if pick_ and m_ <= pick_[0]:
                            break
                        cl_ = math.hypot(a_["pt"][0] - b_["pt"][0], a_["pt"][1] - b_["pt"][1])
                        if cl_ < R_STAND:
                            continue
                        # run-outs on one landing must not cross each other (t-td)
                        if any(crosses(sa[0], sa[1], sb[0], sb[1]) for sa in a_["segs"] for sb in b_["segs"]):
                            continue
                        cw_ = line_wall(a_["pt"], b_["pt"])
                        if cw_ < R_STAND:
                            continue
                        m2_ = min(m_, cw_ - R_STAND, cl_ - R_STAND)
                        if not pick_ or m2_ > pick_[0]:
                            pick_ = (m2_, a_, b_, cl_, cw_)
                return pick_
            A, Bo = opts_by_end
            pick = pair_search(A, Bo)
            if pick and any(len(o_["segs"]) == 2 for o_ in pick[1:3]):
                out.append("  BEND CURVE (two-segment run-outs): best smallest-margin against the largest bend allowed")
                for cap in list(range(38, 51)):
                    pk = pair_search([o_ for o_ in A if abs(o_["ang"]) <= cap], [o_ for o_ in Bo if abs(o_["ang"]) <= cap])
                    out.append(f"    bend <= {cap} deg: " + (f"margin {pk[0]:+.4f} ({'target met' if pk[0] >= TARGET - 5e-5 else 'target NOT met'}); bend {abs(pk[1]['ang']):.1f}; "
                               f"first segment {math.hypot(pk[1]['segs'][0][1][0]-pk[1]['segs'][0][0][0], pk[1]['segs'][0][1][1]-pk[1]['segs'][0][0][1]):.3f} m (margin {pk[1]['segs'][0][2]-R_STAND:+.4f}), "
                               f"second {math.hypot(pk[1]['segs'][1][1][0]-pk[1]['segs'][1][0][0], pk[1]['segs'][1][1][1]-pk[1]['segs'][1][0][1]):.3f} m (margin {pk[1]['segs'][1][2]-R_STAND:+.4f}) to {f3(pk[1]['pt'])}; "
                               f"point wall {wall_d(pk[1]['pt'])-R_STAND:+.4f}, mouth {mouth_d(pk[1]['pt'])-R_STAND:+.4f}; crossing {pk[3]:.3f} m (wall margin {pk[4]-R_STAND:+.4f})" if pk else "no solution"))
                def seg_len(sg_):
                    return math.hypot(sg_[1][0] - sg_[0][0], sg_[1][1] - sg_[0][1])
                # the deliberate stop (t-td): bend >= STOP_BEND; largest smallest-margin, then the longest first
                # segment, then the smaller bend
                key_ = lambda o_: (-round(o_["m"], 4), -round(seg_len(o_["segs"][0]), 3), abs(o_["ang"]))
                pk = pair_search(sorted([o_ for o_ in A if abs(o_["ang"]) >= STOP_BEND], key=key_), sorted([o_ for o_ in Bo if abs(o_["ang"]) >= STOP_BEND], key=key_))
                # the landing is symmetric about the tower's diagonal: prefer the best pair that is a mirror image
                As = sorted([o_ for o_ in A if abs(o_["ang"]) >= STOP_BEND], key=key_)
                Bm = {(round(o_["pt"][1], 3), round(o_["pt"][0], 3), round(seg_len(o_["segs"][0]), 3)): o_ for o_ in Bo if abs(o_["ang"]) >= STOP_BEND}
                for a_ in As:
                    b_ = Bm.get((round(a_["pt"][0], 3), round(a_["pt"][1], 3), round(seg_len(a_["segs"][0]), 3)))
                    if not b_:
                        continue
                    cl_ = math.hypot(a_["pt"][0] - b_["pt"][0], a_["pt"][1] - b_["pt"][1])
                    cw_ = line_wall(a_["pt"], b_["pt"])
                    if cl_ >= R_STAND and cw_ >= R_STAND and not any(crosses(sa[0], sa[1], sb[0], sb[1]) for sa in a_["segs"] for sb in b_["segs"]):
                        mm = min(a_["m"], b_["m"], cw_ - R_STAND, cl_ - R_STAND)
                        if pk and mm >= pk[0] - 5e-5:
                            pk = (mm, a_, b_, cl_, cw_)
                            out.append("  STOP-AND-TURN: a mirror-image pair with the same smallest margin exists and is the one printed")
                        break
                if not pk:
                    out.append(f"  STOP-AND-TURN (bend >= {STOP_BEND} deg): NO solution holds {R_STAND} m")
                else:
                    for (nm, q), o_ in zip(ends, pk[1:3]):
                        out.append(f"  STOP-AND-TURN (bend >= {STOP_BEND} deg) {nm}: first segment {f3(o_['segs'][0][0])} to {f3(o_['segs'][0][1])}, {seg_len(o_['segs'][0]):.3f} m, wall margin {o_['segs'][0][2]-R_STAND:+.4f}; "
                                   f"bend {abs(o_['ang']):.1f} deg toward the next flight; second segment to {f3(o_['pt'])}, {seg_len(o_['segs'][1]):.3f} m, wall margin {o_['segs'][1][2]-R_STAND:+.4f}; "
                                   f"point: wall margin {wall_d(o_['pt'])-R_STAND:+.4f}, mouth margin {mouth_d(o_['pt'])-R_STAND:+.4f}")
                    out.append(f"  STOP-AND-TURN crossing: {pk[3]:.3f} m, wall margin along it {pk[4]-R_STAND:+.4f}; smallest margin of the landing {pk[0]:+.4f}; 10 mm target {'met' if pk[0] >= TARGET else 'NOT met'}")
                # MIN-BEND CURVE, refined below the grid. The landing is symmetric about the tower's diagonal, so the pair
                # is a point and its mirror image; variables are the first segment's length and the point. Hill-climb from
                # the best grid candidates with steps from 2 mm down to 0.01 mm; all distances analytic.
                nm0, q0_ = ends[0]
                f0 = next(fl for fl, end, qq in T.landing_ends(k) if qq is q0_)
                sg0 = 1.0 if nm0.endswith("head") else -1.0
                d0 = (f0["dir"][0] * sg0, f0["dir"][1] * sg0)

                def ev(sv, c):
                    bp = (q0_[0] + d0[0] * sv, q0_[1] + d0[1] * sv)
                    if sv < 0 or not poly.inside(outline, bp, 1e-9) or not poly.inside(outline, c, 1e-9):
                        return -1.0, 0.0, None
                    v = (c[0] - bp[0], c[1] - bp[1])
                    ang = abs(math.degrees(math.atan2(d0[0] * v[1] - d0[1] * v[0], d0[0] * v[0] + d0[1] * v[1])))
                    cm = (c[1], c[0])
                    parts = {"first segment": line_wall(q0_, bp) - R_STAND, "second segment": line_wall(bp, c) - R_STAND,
                             "point wall": wall_d(c) - R_STAND, "point mouth": mouth_d(c) - R_STAND,
                             "crossing wall": line_wall(c, cm) - R_STAND, "crossing length": math.hypot(c[0] - cm[0], c[1] - cm[1]) - R_STAND}
                    return min(parts.values()), ang, (bp, parts)
                out.append("  MIN-BEND CURVE (mirror pair, refined to 0.01 mm): best smallest-margin subject to bend >= B")
                for Bmin in (45.0, 45.5, 46.0, 47.0, 48.0, 50.0):
                    seeds = sorted([o_ for o_ in A if abs(o_["ang"]) >= Bmin], key=lambda o_: -o_["m"])[:40]
                    bestr = None
                    for o_ in seeds:
                        sv = seg_len(o_["segs"][0]); c = o_["pt"]
                        m_, ang_, info = ev(sv, c)
                        if info is None or ang_ < Bmin:
                            continue
                        step = 0.002
                        while step >= 1e-5:
                            moved = False
                            for dsv, dx, dy in ((step, 0, 0), (-step, 0, 0), (0, step, 0), (0, -step, 0), (0, 0, step), (0, 0, -step),
                                                (step, step, 0), (step, -step, 0), (-step, step, 0), (-step, -step, 0), (0, step, step), (0, step, -step), (0, -step, step), (0, -step, -step)):
                                m2_, a2_, i2 = ev(sv + dsv, (c[0] + dx, c[1] + dy))
                                if i2 is not None and a2_ >= Bmin and m2_ > m_ + 1e-9:
                                    sv, c, m_, ang_, info, moved = sv + dsv, (c[0] + dx, c[1] + dy), m2_, a2_, i2, True
                                    break
                            if not moved:
                                step /= 2.0
                        if not bestr or m_ > bestr[0]:
                            bestr = (m_, ang_, sv, c, info)
                    if not bestr:
                        out.append(f"    bend >= {Bmin:.1f}: no solution")
                    else:
                        m_, ang_, sv, c, (bp, parts) = bestr
                        out.append(f"    bend >= {Bmin:.1f}: smallest margin {m_:+.4f}; bend {ang_:.1f}; first segment {sv:.4f} m to ({bp[0]:.4f}, {bp[1]:.4f}); second {math.hypot(c[0]-bp[0], c[1]-bp[1]):.4f} m to ({c[0]:.4f}, {c[1]:.4f}); "
                                   + ", ".join(f"{k_} {v_:+.4f}" for k_, v_ in parts.items()))
                pk = pair_search([o_ for o_ in A if abs(o_["ang"]) >= 50], [o_ for o_ in Bo if abs(o_["ang"]) >= 50])
                out.append("    bend >= 50 deg: " + (f"margin {pk[0]:+.4f} ({'target met' if pk[0] >= TARGET - 5e-5 else 'target NOT met'}); bend {abs(pk[1]['ang']):.1f}; "
                           f"first segment {math.hypot(pk[1]['segs'][0][1][0]-pk[1]['segs'][0][0][0], pk[1]['segs'][0][1][1]-pk[1]['segs'][0][0][1]):.3f} m, second "
                           f"{math.hypot(pk[1]['segs'][1][1][0]-pk[1]['segs'][1][0][0], pk[1]['segs'][1][1][1]-pk[1]['segs'][1][0][1]):.3f} m to {f3(pk[1]['pt'])}; crossing {pk[3]:.3f} m" if pk else "no solution"))
            if not pick:
                out.append("  SELECT: NO pair meets the hard limits on this landing (named fail)")
            else:
                m2, a, b, cl, cw = pick
                for (nm, q), o_ in zip(ends, (a, b)):
                    carried = abs(o_["ang"]) <= (35 if o_["toward"] else 30)
                    segs = "; ".join(f"segment {i + 1} {f3(sa)} to {f3(sb)}, {math.hypot(sb[0]-sa[0], sb[1]-sa[1]):.3f} m, wall margin {lw_ - R_STAND:+.4f}" for i, (sa, sb, lw_) in enumerate(o_["segs"]))
                    c = o_["pt"]
                    out.append(f"  SELECT {nm}: {len(o_['segs'])}-segment; {segs}; point {f3(c)}: wall margin {wall_d(c) - R_STAND:+.4f}, mouth margin {mouth_d(c) - R_STAND:+.4f}; "
                               f"{'bend' if len(o_['segs']) == 2 else 'turn'} {o_['ang']:+.1f} deg, {'toward' if o_['toward'] else 'away from'} the next flight, "
                               f"{'carried' if carried else 'stop-and-turn'}; smallest margin on this end {o_['m']:+.4f}, 10 mm target {'met' if o_['m'] >= TARGET else 'NOT met'}")
                z_ = L.levels[k]
                near_ = [o for o in T.obstacles if min(o["p0"][2], o["p1"][2]) - o["r"] < z_ + 2.7 and max(o["p0"][2], o["p1"][2]) + o["r"] > z_ + 0.6]

                def seg3(q_, a3, b3):
                    ab = (b3[0] - a3[0], b3[1] - a3[1], b3[2] - a3[2])
                    t_ = min(max(sum((q_[i] - a3[i]) * ab[i] for i in range(3)) / max(sum(v * v for v in ab), 1e-12), 0.0), 1.0)
                    return math.sqrt(sum((q_[i] - a3[i] - ab[i] * t_) ** 2 for i in range(3)))
                for (nm, q), o_ in zip(ends, (a, b)):
                    best_e = (9.9, "")
                    for sa, sb, _ in o_["segs"]:
                        n_ = max(1, int(math.hypot(sb[0] - sa[0], sb[1] - sa[1]) / 0.005))
                        for t in range(n_ + 1):
                            e_ = (sa[0] + (sb[0] - sa[0]) * t / n_, sa[1] + (sb[1] - sa[1]) * t / n_, z_ + 1.65)
                            for ob in near_:
                                dd = seg3(e_, ob["p0"], ob["p1"]) - ob["r"]
                                if dd < best_e[0]:
                                    best_e = (dd, ob["name"])
                    out.append(f"  SELECT {nm}: eye (1.65 m, on the line, no slack) comes within {best_e[0]:.3f} m of {best_e[1]} along this run-out")
                out.append(f"  SELECT crossing: {cl:.3f} m (over the 0.30 minimum by {cl - R_STAND:+.4f}), wall margin along it {cw - R_STAND:+.4f}; "
                           f"smallest margin of the whole landing {m2:+.4f}, 10 mm target {'met' if m2 >= TARGET else 'NOT met'}")
        out.append("")
    # THE SET: printed from the build module (mc39_runouts), which is what the manifest and FBX carry. Where a line
    # above (HAND-OVER, SELECT, STOP-AND-TURN, curves) disagrees with this section, this section is the source.
    import mc39_runouts
    out.append("BUILD MODULE OUTPUT (mc39_runouts; the source for the manifest). Margins are over the reach, analytic.")
    for (fi, end), r in sorted(mc39_runouts.built_in(P, L, T).items()):
        out.append(f"  MODULE flight {fi} {end}: landing {r['landing']}, built-in straight run {r['run']} m to {f3(r['point'])}; wall {r['point_wall_margin']:+.4f}, mouth {r['point_mouth_margin']:+.4f}, "
                   f"run {r['segment_wall_margins'][0]:+.4f}; crossing {r['crossing_length']:.4f} m (wall {r['crossing_wall_margin']:+.4f}, mouth {r['crossing_mouth_margin']:+.4f})")
    for (fi, end), r in sorted(mc39_runouts.select(P, L, T).items()):
        if "fail" in r:
            out.append(f"  MODULE flight {fi} {end}: landing {r['landing']}: FAIL, {r['fail']}")
            continue
        out.append(f"  MODULE flight {fi} {end}: landing {r['landing']}, {r['shape']}: " + " -> ".join(f"({p_[0]:.4f}, {p_[1]:.4f})" for p_ in r["path"]) +
                   f"; segments {r['segment_wall_margins']}; point wall {r['point_wall_margin']:+.4f}, mouth {r['point_mouth_margin']:+.4f}; {r['turn_deg']:.2f} deg {r['turn']}, {r['arrival']}; "
                   f"crossing {r['crossing_length']:.4f} m (wall {r['crossing_wall_margin']:+.4f}); landing margin {r['landing_margin']:+.4f}; 10 mm target {'met' if r['target_met'] else 'NOT met'}")
    dst = os.path.join(HERE, "..", "..", "..", "TestResults", "tower-art")
    os.makedirs(dst, exist_ok=True)
    path = os.path.abspath(os.path.join(dst, "landing-clear-maps.txt"))
    with open(path, "w", encoding="utf-8", newline="\n") as fh:
        fh.write("\n".join(out) + "\n")
    print("\n".join(out))
    print("WROTE " + path)


if __name__ == "__main__":
    main()
