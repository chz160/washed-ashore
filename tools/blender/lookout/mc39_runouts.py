"""Linked level run-outs for the landings where the built-in straight run does not fit. Pure Python (no bpy).

Rule (tower-td-note, lane-end ruling as written by t-td on 2026-10-11; provisional until issued). At each lane end
on a landing the player is carried `built_in_run` straight on. Where that point or line is not `capsule_reach` clear
of every rail-plane wall and stair mouth edge, the end gets a linked run-out P0 (the lane end, on the lip), P1 (the
bend), P2 (the hand-over point):
  1 segment 1, P0-P1, is collinear with the flight, at least `run_out_first_segment_min` long: no turn at the lip
  2 P2 at least capsule_reach from every wall and stair-mouth edge; every point of both segments that far from every
    wall; the landing's two hand-over points that far apart, the line between them holding it; run-outs do not cross
  3 bands per side at wall_reach (see bend_bands): segment 1's band at P0 must equal the flight's lip width, and the
    carry across the bend must hold in both directions on both sides
  4 the bend is carried (at most `carry_bend_max_toward` toward the next flight or `carry_bend_max_away` away) or a
    stop (at least `stop_bend_min`, toward); nothing between. Each landing's behaviour is frozen in the table
    (`frozen_behaviour`). If a carried landing has no solution, the ends named in `stop_fallback_allowed` are searched
    again as a stop turning toward the next flight (at least `stop_bend_min_lever`); that is reported as a fallback,
    a finding and not a result. An end not named there (flight 8 foot, whose bend turns away) has no fallback
  5 among the solutions: the largest smallest-margin; then the longest first segment; then the smaller bend. For the
    ends named in `shortest_first_segment` (flight 8 foot) the shortest first segment that passes comes first instead
Points are picked from a 5 mm grid (first segment lengths every 5 mm) and refined by hill-climbing to 0.01 mm on the
margin, the refined result being kept only if rules 1-4 still hold. Nothing here moves drawn geometry.
"""
import math
import time

import mc39_poly as poly
import mc39_walls as W

G = 0.005
SEEDS = 12          # grid pairs refined per search
FIRST_SEGMENT_SEARCH_LIMIT = 0.6          # metres: the longest first segment the tool tries
# Reach is found to sub-millimetre precision by default. Off (build_mc39.py --whole-millimetre-reach) it is the old
# whole-millimetre figure: a comparison run only, never a candidate for the copy; the manifest says which it is.
REACH_BISECT = True


def _crosses(a, b, c, d):
    def o(p, q, r):
        return (q[0] - p[0]) * (r[1] - p[1]) - (q[1] - p[1]) * (r[0] - p[0])
    return o(a, b, c) * o(a, b, d) < 0 and o(c, d, a) * o(c, d, b) < 0


class Landing:
    def __init__(self, P, L, T, k, proxies):
        """proxies: [(proxy name, plan footprint)], every collision proxy a body standing on this deck can touch
        (mc39_walls; Collision.deck_walls). They are the walls for reach, bands, the hand-over points and the crossing."""
        self.k = k
        self.proxies = proxies
        self.ck = P["checks"]
        self.reach = P["checks"]["capsule_reach"]
        self.outline = [(p[0], p[1]) for p in L.landing_polygon(k)]
        self.walls = [((a[0], a[1]), (b[0], b[1])) for a, b in T.guard_runs(k, under_stair=True)]
        # Fall edges: every stretch of the deck's outline with no rail-plane wall on it (the stair mouths, and any
        # short stretch or edge that carries no rail).
        self.fall_edges = []
        outline3 = L.landing_polygon(k)
        opens = T.edge_openings(k)
        for i in range(len(outline3)):
            a, b = outline3[i], outline3[(i + 1) % len(outline3)]
            ln = math.hypot(b[0] - a[0], b[1] - a[1])
            if ln < 1e-9:
                continue
            d = ((b[0] - a[0]) / ln, (b[1] - a[1]) / ln)

            def at(sv, a=a, d=d):
                return (a[0] + d[0] * sv, a[1] + d[1] * sv)
            if ln < 0.10:
                self.fall_edges.append((at(0.0), at(ln)))
                continue
            blocked = sorted((s0, s1) for (ei, _, _, s0, s1, why) in opens if ei == i and why == "mouth")
            cur = 0.0
            for s0, s1 in blocked + [(ln, ln)]:
                if cur < s0 <= cur + 0.10:
                    self.fall_edges.append((at(cur), at(s0)))          # too short for a rail: left open
                if s1 > s0:
                    self.fall_edges.append((at(s0), at(s1)))
                cur = max(cur, s1)
        self.mouths = []
        for (_, a, b, s0, s1, _) in T.edge_openings(k):
            ln = math.hypot(b[0] - a[0], b[1] - a[1])
            d = ((b[0] - a[0]) / ln, (b[1] - a[1]) / ln)
            self.mouths.append(((a[0] + d[0] * s0, a[1] + d[1] * s0), (a[0] + d[0] * s1, a[1] + d[1] * s1)))
        self.ends = []
        for f, end, q in T.landing_ends(k):
            sg = 1.0 if end == "head" else -1.0
            self.ends.append({"flight": f["index"], "end": end, "q": (q[0], q[1]), "d": (f["dir"][0] * sg, f["dir"][1] * sg)})
        for e, o in ((self.ends[0], self.ends[1]), (self.ends[1], self.ends[0])):
            e["side_next"] = e["d"][0] * (o["q"][1] - e["q"][1]) - e["d"][1] * (o["q"][0] - e["q"][0])
        self.named_walls = [(nm, (lambda q, fp=fp: W.dist(fp, q))) for nm, fp in self.proxies]
        self.on_deck = lambda q: poly.inside(self.outline, q, 1e-9)
        self._bands = {}
        self._cands = {}
        self.deadline = None          # wall-clock limit for this landing's search, set by select()
        self._lips = {}
        self._cross = {}
        self._r1 = {}
        self.last_counts = [0, 0]
        self.quick_rejects = 0          # candidates turned down by the cheap necessary test for rule 4 (not in rule_3_rejections)
        self.rejects = {}

    def wall_d(self, q):
        return min(W.dist(fp, q) for _, fp in self.proxies)

    def fall_d(self, q):
        """Distance to the nearest fall edge of the deck (a stair mouth or an unrailed stretch of its outline)."""
        return min(poly.seg_dist(q, a, b) for a, b in self.fall_edges) if self.fall_edges else 9.9

    def line_fall(self, a, b):
        """Smallest distance from the segment a-b to any fall edge (0 if it crosses one)."""
        best = 9.9
        for fa, fb in self.fall_edges:
            if _crosses(a, b, fa, fb):
                return 0.0
            best = min(best, poly.seg_dist(a, fa, fb), poly.seg_dist(b, fa, fb), poly.seg_dist(fa, a, b), poly.seg_dist(fb, a, b))
        return best

    def mouth_d(self, q):
        return min(poly.seg_dist(q, a, b) for a, b in self.mouths) if self.mouths else 9.9

    def margin(self, q):
        return min(self.wall_d(q), self.fall_d(q))

    def line_wall(self, a, b):
        """(smallest distance from the segment a-b to any proxy of the wall set, that proxy's name); 0 if they meet.
        Rule B2 (2026-10-11): a line the body is carried along must hold wall_reach from every proxy; a line or point
        where the ordinary controller is live (the crossing, a hand-over point) must hold capsule_reach."""
        return min((W.seg_dist(fp, a, b), nm) for nm, fp in self.proxies)

    def carried_ok(self, a, b):
        """Is the carried line a-b clear of every proxy at wall_reach (no intersection at wall_reach - band_tolerance)?"""
        return self.line_wall(a, b)[0] >= self.ck["wall_reach"] - self.ck["band_tolerance"]

    def turn(self, e, a, c):
        v = (c[0] - a[0], c[1] - a[1])
        ang = math.degrees(math.atan2(e["d"][0] * v[1] - e["d"][1] * v[0], e["d"][0] * v[0] + e["d"][1] * v[1]))
        return ang, (ang * e["side_next"] >= 0 or abs(ang) < 0.5)

    def straight_fits(self, run):
        for e in self.ends:
            c = (e["q"][0] + e["d"][0] * run, e["q"][1] + e["d"][1] * run)
            if not poly.inside(self.outline, c, 1e-6) or self.margin(c) < self.reach or not self.carried_ok(e["q"], c):
                return False
        return True

    def region(self):
        x0, y0, x1, y1 = poly.bbox(self.outline)
        pts = []
        for i in range(int((x1 - x0) / G) + 1):
            for j in range(int((y1 - y0) / G) + 1):
                q = (x0 + i * G, y0 + j * G)
                if poly.inside(self.outline, q) and self.margin(q) >= self.reach:
                    pts.append(q)
        return pts

    def bend_class(self, ang, toward, behaviour):
        """Is a bend of `ang` degrees allowed for the behaviour asked for? 'carried', 'stop' (the frozen stop) or
        'stop-fallback' (the lever's own stop angle)."""
        ck = self.ck
        a = abs(ang)
        g = ck["bend_limit_guard_deg"]          # the game decides with the exact lever; the table's angle is a rounding of it
        if behaviour == "carried":
            return a <= ((ck["carry_bend_max_toward"] - g) if toward else ck["carry_bend_max_away"])
        return toward and a >= (ck["stop_bend_min"] if behaviour == "stop" else ck["stop_bend_min_lever"] + g)

    def candidate(self, e, d0, c):
        """One end's run-out with first segment d0 and hand-over point c, or None where a hard limit fails."""
        R = self.reach
        q, d = e["q"], e["d"]
        bp = (q[0] + d[0] * d0, q[1] + d[1] * d0)
        if d0 < self.ck["run_out_first_segment_min"] - 1e-9 or not poly.inside(self.outline, bp, 1e-9) or not poly.inside(self.outline, c, 1e-9):
            return None
        if math.hypot(c[0] - bp[0], c[1] - bp[1]) < self.ck["run_out_final_segment_min"]:
            return None
        wr = self.ck["wall_reach"]
        (l1, n1), (l2, n2) = self.line_wall(q, bp), self.line_wall(bp, c)
        mc = self.margin(c)
        if mc < R or min(l1, l2) < wr - self.ck["band_tolerance"]:
            return None
        ang, toward = self.turn(e, bp, c)
        # the margin the selection sees: the hand-over point's over capsule_reach, each carried line's over wall_reach
        return {"m": min(l1 - wr, l2 - wr, mc - R), "pt": c, "d0": d0, "path": [q, bp, c], "ang": ang, "toward": toward,
                "seg_m": [l1 - wr, l2 - wr], "seg_clear": [l1, l2], "seg_proxy": [n1, n2]}

    def bands_record(self, o):
        """Rule 3 worked out in full for one candidate (not cached: the records are large)."""
        ck = self.ck
        return bend_bands(o["path"][0], o["path"][1], o["path"][2], self.named_walls, self.on_deck, ck["wall_reach"],
                          ck["join_mouth_default"] / 2.0, math.tan(math.radians(ck["mouth_slope_deg"])), ck["band_tolerance"], at1=self.reach_on_first)

    def reach_on_first(self, c, left):
        """reach_at for a point on a first segment, kept: a first segment is collinear with its flight, so every
        candidate at one lane end samples the same line and the same points come up again and again."""
        key = (round(c[0], 7), round(c[1], 7), round(left[0], 7), round(left[1], 7))
        if key not in self._r1:
            ck = self.ck
            self._r1[key] = reach_at(c, left, self.named_walls, self.on_deck, ck["wall_reach"], ck["join_mouth_default"] / 2.0)
        return self._r1[key]

    def bands(self, o):
        """Does rule 3 hold for this candidate (the band at the lane end equal to the lip, the carry across the bend)?
        Only the yes or no is kept, per candidate; a rejection is counted once, with its nearest miss."""
        key = (round(o["path"][0][0], 6), round(o["path"][0][1], 6), round(o["d0"], 6), round(o["pt"][0], 6), round(o["pt"][1], 6))
        if key in self._bands:
            return self._bands[key]
        if self.deadline is not None and time.time() > self.deadline:
            raise RuntimeError(f"run-out search on landing {self.k} passed its time limit (build_constants.run_out_search_time_limit_s): no result")
        # A cheap necessary test first: rule 4 cannot hold unless the reach across segment 1 at the bend (the most the
        # bend can accept) plus the slope over the first segment's length reaches the flight's lip on both sides.
        ck = self.ck
        cap, slope = ck["join_mouth_default"] / 2.0, math.tan(math.radians(ck["mouth_slope_deg"]))
        P0, P1 = o["path"][0], o["path"][1]
        v1 = ((P0[0] - P1[0]) / o["d0"], (P0[1] - P1[1]) / o["d0"])
        for side in (1.0, -1.0):
            left = (-v1[1] * side, v1[0] * side)
            lk = (round(P0[0], 6), round(P0[1], 6), side)
            if lk not in self._lips:
                self._lips[lk] = self.reach_on_first(P0, left)[0]
            top = self.reach_on_first(P1, left)[0]
            if self._lips[lk] - (top + slope * o["d0"]) > ck["band_tolerance"]:
                self._bands[key] = False
                self.quick_rejects += 1
                return False
        b = self.bands_record(o)
        self._bands[key] = b["holds"]
        if not b["holds"]:
            # keep, per lane end and reason, how many candidates rule 3 turned down and the nearest miss (millimetres per side)
            end = (round(o["path"][0][0], 4), round(o["path"][0][1], 4))
            lip = {sd: round(b["segment_1"][sd]["lip_shortfall"] * 1000.0, 2) for sd in ("left", "right")}
            rev = {sd: round(max(b["segment_2"][sd]["carried_into_segment_1"]["across"] - b["segment_2"][sd]["carried_into_segment_1"]["accept_there"], 0.0) * 1000.0, 2)
                   for sd in ("left", "right")}
            for reason, mm in (("band at the lane end short of the flight's lip", lip), ("segment 2's band at the bend not inside what segment 1 accepts", rev)):
                if max(mm.values()) > self.ck["band_tolerance"] * 1000.0:
                    r_ = self.rejects.setdefault((end, reason), {"count": 0, "nearest_miss_mm": None})
                    r_["count"] += 1
                    if r_["nearest_miss_mm"] is None or max(mm.values()) < max(r_["nearest_miss_mm"].values()):
                        r_["nearest_miss_mm"] = mm
                        r_["nearest_miss_at"] = {"first_segment_length": round(o["d0"], 4), "bend_deg": round(abs(o["ang"]), 2), "margin": round(o["m"], 4)}
        return b["holds"]

    def reject_summary(self):
        """Rule 3's rejections since the last call, per lane end and reason; then cleared. A cached candidate is counted once."""
        out = []
        for (end, reason), r_ in sorted(self.rejects.items()):
            e = next(e_ for e_ in self.ends if (round(e_["q"][0], 4), round(e_["q"][1], 4)) == end)
            out.append(dict(r_, end=f"flight {e['flight']} {e['end']}", reason=reason))
        self.rejects = {}
        return out

    def first_ok(self, e, d0):
        bp = (e["q"][0] + e["d"][0] * d0, e["q"][1] + e["d"][1] * d0)
        return poly.inside(self.outline, bp, 1e-6) and self.carried_ok(e["q"], bp)

    def candidates(self, i, pts, behaviour, fixed):
        """End i's candidates for one behaviour, ordered margin, longer first segment, smaller bend, coordinates.
        `fixed` pins the first segment's length."""
        key = (i, behaviour, fixed)
        if key not in self._cands:
            e = self.ends[i]
            lst = []
            d0 = self.ck["run_out_first_segment_min"] if fixed is None else fixed
            while d0 <= 0.6 and self.first_ok(e, d0):
                for c in pts:
                    o = self.candidate(e, d0, c)
                    if o and self.bend_class(o["ang"], o["toward"], behaviour):
                        lst.append(o)
                if fixed is not None:
                    break
                d0 = round(d0 + G, 6)
            lst.sort(key=lambda o: (-round(o["m"], 4), -round(o["d0"], 4), round(abs(o["ang"]), 3), o["pt"]))
            self._cands[key] = lst
        return self._cands[key]

    def pair(self, a, b):
        """The two ends' candidates together: (smallest margin, crossing length, crossing wall distance, crossing
        fall-edge distance) or None. The crossing between the two hand-over points must be capsule_reach long and hold
        capsule_reach from every wall and every fall edge along its whole length."""
        R = self.reach
        cl = math.hypot(a["pt"][0] - b["pt"][0], a["pt"][1] - b["pt"][1])
        if cl < R:
            return None
        for s0, s1 in zip(a["path"], a["path"][1:]):
            for t0, t1 in zip(b["path"], b["path"][1:]):
                if _crosses(s0, s1, t0, t1):
                    return None
        # run-outs that come closer to each other than band_tolerance count as crossing
        sep = min(W._seg_seg(s0, s1, t0, t1) for s0, s1 in zip(a["path"], a["path"][1:]) for t0, t1 in zip(b["path"], b["path"][1:]))
        if sep < self.ck["band_tolerance"]:
            return None
        cw = self.line_wall(a["pt"], b["pt"])[0]
        if cw < R:
            return None
        cf = self.line_fall(a["pt"], b["pt"])
        if cf < R:
            return None
        self.last_separation = sep
        return (min(a["m"], b["m"], cw - R, cl - R, cf - R), cl, cw, cf)

    def crossing(self, pa, pb):
        """The line between two hand-over points, worked out once per pair of points: (length, wall distance, fall-edge
        distance), or None where it is shorter than capsule_reach or comes within capsule_reach of a proxy or fall edge."""
        key = (pa, pb)
        if key not in self._cross:
            R = self.reach
            cl = math.hypot(pa[0] - pb[0], pa[1] - pb[1])
            r = None
            if cl >= R:
                cw = self.line_wall(pa, pb)[0]
                if cw >= R:
                    cf = self.line_fall(pa, pb)
                    if cf >= R:
                        r = (cl, cw, cf)
            self._cross[key] = r
        return self._cross[key]

    def bent(self, pts, behaviours, fixed=(None, None), need_bands=True):
        """Rules 1-5 for the landing's two ends with the behaviour asked of each. `fixed` pins an end's first-segment
        length; need_bands=False leaves rule 3 out (for reporting what fails).

        Exact on its grid: no candidate is dropped by a cap. Pairs of hand-over points are taken best-first by the most
        their pair could score (a pair's smallest margin cannot exceed either candidate's own margin nor the
        crossing's length margin); the crossing is tested once per pair of points; rule 3, the costly test, is tried
        last and only on candidates that could still win. The search stops when nothing left can beat the SEEDS best
        pairs found; each of those is refined and the best refined pair is taken (margin, then the longer first
        segments, then the smaller bends). Returns (margin, a, b, crossing length, crossing wall distance, crossing
        fall-edge distance), or None when no pair holds the rule on this grid."""
        R = self.reach
        opts = [self.candidates(i, pts, behaviours[i], fixed[i]) for i in (0, 1)]
        self.last_counts = [len(o) for o in opts]
        if not opts[0] or not opts[1]:
            return None
        by_pt = []
        for o in opts:
            d = {}
            for c in o:                      # already ordered: margin, longer first segment, smaller bend
                d.setdefault(c["pt"], []).append(c)
            by_pt.append(d)
        pairs = []
        for pa, la_ in by_pt[0].items():
            for pb, lb_ in by_pt[1].items():
                cl = math.hypot(pa[0] - pb[0], pa[1] - pb[1])
                if cl >= R:
                    pairs.append((min(la_[0]["m"], lb_[0]["m"], cl - R), pa, pb))
        pairs.sort(key=lambda t: (-round(t[0], 6), t[1], t[2]))
        seeds = []          # best first; at most SEEDS

        def floor():
            return seeds[-1][0] if len(seeds) == SEEDS else -1.0
        for ub, pa, pb in pairs:
            if ub <= floor():
                break
            if self.deadline is not None and time.time() > self.deadline:
                raise RuntimeError(f"run-out search on landing {self.k} passed its time limit (build_constants.run_out_search_time_limit_s): no result")
            cx = self.crossing(pa, pb)
            if cx is None:
                continue
            cl, cw, cf = cx
            mx = min(cl - R, cw - R, cf - R)
            if mx <= floor():
                continue
            for a in by_pt[0][pa]:
                if min(a["m"], mx) <= floor():
                    break
                if need_bands and not self.bands(a):
                    continue
                for b in by_pt[1][pb]:
                    m = min(a["m"], b["m"], mx)
                    if m <= floor():
                        break
                    # run-outs that cross, or come closer to each other than band_tolerance, are out
                    sep = min(W._seg_seg(s0, s1, t0, t1) for s0, s1 in zip(a["path"], a["path"][1:]) for t0, t1 in zip(b["path"], b["path"][1:]))
                    if sep < self.ck["band_tolerance"]:
                        continue
                    if need_bands and not self.bands(b):
                        continue
                    seeds.append((m, a, b, cl, cw, cf))
                    seeds.sort(key=lambda t: (-round(t[0], 5), -round(t[1]["d0"] + t[2]["d0"], 4), round(abs(t[1]["ang"]) + abs(t[2]["ang"]), 3), t[1]["pt"], t[2]["pt"]))
                    del seeds[SEEDS:]
        best = None
        for sd in seeds:
            r = self.refine(sd, behaviours, fixed, need_bands)
            key = (round(r[0], 5), round(r[1]["d0"] + r[2]["d0"], 4), -round(abs(r[1]["ang"]) + abs(r[2]["ang"]), 3))
            if best is None or key > best[0]:
                best = (key, r)
        return best[1] if best else None

    def refine(self, pick, behaviours, fixed=(None, None), need_bands=True):
        """Hill-climb both ends' first-segment lengths and hand-over points below the grid (2 mm down to 0.01 mm) on the
        margin, moving one or two of the six numbers at a time in a fixed order; a pinned first-segment length stays
        pinned. The refined pair is kept only if every rule still holds for it; otherwise the grid pair stands."""
        ea, eb = self.ends

        def ev(x):
            a, b = self.candidate(ea, x[0], (x[1], x[2])), self.candidate(eb, x[3], (x[4], x[5]))
            if not a or not b or not self.bend_class(a["ang"], a["toward"], behaviours[0]) or not self.bend_class(b["ang"], b["toward"], behaviours[1]):
                return None
            r = self.pair(a, b)
            return (r[0], a, b, r[1], r[2], r[3]) if r else None
        x = [pick[1]["d0"], pick[1]["pt"][0], pick[1]["pt"][1], pick[2]["d0"], pick[2]["pt"][0], pick[2]["pt"][1]]
        free = [i for i in range(6) if not ((i == 0 and fixed[0] is not None) or (i == 3 and fixed[1] is not None))]
        moves = [((i, sg),) for i in free for sg in (1.0, -1.0)]
        moves += [((i, si), (j, sj)) for n_, i in enumerate(free) for j in free[n_ + 1:] for si in (1.0, -1.0) for sj in (1.0, -1.0)]
        r = ev(x)
        step = 0.002
        while r and step >= 1e-5:
            moved = False
            for mv in moves:
                y = list(x)
                for i, sg in mv:
                    y[i] += sg * step
                r2 = ev(y)
                if r2 and r2[0] > r[0] + 1e-9:
                    x, r, moved = y, r2, True
                    break
            if not moved:
                step /= 2.0
        if r and r[0] > pick[0] and (not need_bands or (self.bands(r[1]) and self.bands(r[2]))):
            return r
        return pick


def bend_bands(P0, P1, P2, walls, ok, wall_reach, cap, slope, tol, spacing=0.01, at1=None):
    """Bands of a two-segment run-out P0 (lane end) - P1 (bend) - P2 (free end), per side, at wall_reach.

    Sides are left and right as seen looking along each segment toward the stair. Segment 2: s from the free end;
    band2(s) = min(cap, reach2(s), lip2 + slope x distance to P1) as a running minimum from the free end, lip2 = reach2
    at P1. At the bend, accept per side is the largest width, no more than reach1 at P1, such that a body that far
    across segment 1 lies inside segment 2's band where it stands (see band2_at); found by bisection, not rounded. Segment 1: s from the lane end (0) to the bend;
    band1(s) = min(cap, reach1(s), accept + slope x distance to P1), as a running minimum from the lane end. Holds if
    band1 at the lane end is the flight's lip width there (reach1 at P0, capped) on both sides, and segment 2's band at
    P1, carried into segment 1's frame, lies inside accept on the side it falls."""
    d0 = math.hypot(P1[0] - P0[0], P1[1] - P0[1])
    ln2 = math.hypot(P2[0] - P1[0], P2[1] - P1[1])
    v1 = ((P0[0] - P1[0]) / d0, (P0[1] - P1[1]) / d0)          # toward the stair along segment 1
    v2 = ((P1[0] - P2[0]) / ln2, (P1[1] - P2[1]) / ln2)        # toward the stair along segment 2
    l1, l2 = (-v1[1], v1[0]), (-v2[1], v2[0])                  # each segment's left
    out = {"first_segment_length": round(d0, 4), "second_segment_length": round(ln2, 4), "holds": True}
    seg2, seg1 = {}, {}
    for nm, side in (("left", 1.0), ("right", -1.0)):
        ss, rs, bs = reach_profile(P1, P2, side, walls, ok, wall_reach, cap, spacing)
        lip2 = rs[-1]
        band, run, rmin, rr = [], 9.9, 9.9, []
        for s_, r_ in zip(ss, rs):
            run = min(run, cap, r_, lip2 + slope * (ln2 - s_))
            band.append(round(max(run, 0.0), 6))
            rmin = min(rmin, r_)
            rr.append(rmin)
        seg2[nm] = {"s": ss, "reach": rs, "reach_bound_by": bs, "band": band, "lip_width": lip2, "_reach_run_min": rr}

    def band2_at(nm, t):
        """Segment 2's band on one side at distance t along it from P1 (toward the free end). Behind the bend (t <= 0)
        it is the bend's own sample. Ahead of it, the same three-term rule with the reach term taken as the smaller
        of the two neighbouring samples (never wider than computed) and the slope term exact; this is the form the
        game's build check uses, so the two accept figures can be compared."""
        ss, bd, rr = seg2[nm]["s"], seg2[nm]["band"], seg2[nm]["_reach_run_min"]
        if t <= 0.0:
            return bd[-1]
        s_ = min(max(ln2 - t, 0.0), ln2)
        for i in range(len(ss) - 1):
            if ss[i] - 1e-9 <= s_ <= ss[i + 1] + 1e-9:
                return max(min(cap, rr[i], rr[i + 1], seg2[nm]["lip_width"] + slope * (ln2 - s_)), 0.0)
        return bd[-1]

    def lands(side, w):
        X = (l1[0] * side * w, l1[1] * side * w)                 # the body, from P1
        t = -(X[0] * v2[0] + X[1] * v2[1])                       # along segment 2 from P1, toward the free end
        a = X[0] * l2[0] + X[1] * l2[1]
        nm2 = "left" if a >= 0 else "right"
        return nm2, t, abs(a), abs(a) <= band2_at(nm2, t) + 1e-9
    for nm, side in (("left", 1.0), ("right", -1.0)):
        # s from the lane end (0) to the bend (d0): the profile is taken looking away from the stair, so the side flips
        ss, rs, bs = reach_profile(P1, P0, -side, walls, ok, wall_reach, cap, spacing, at=at1)
        top = rs[-1]
        if top <= 0.0 or lands(side, top)[3]:
            accept = max(top, 0.0)
        else:
            lo, hi = 0.0, top
            while hi - lo > 1e-7:
                mid = (lo + hi) / 2.0
                if lands(side, mid)[3]:
                    lo = mid
                else:
                    hi = mid
            accept = lo
        landed = lands(side, accept)
        band, run = [], 9.9
        for s_, r_ in zip(ss, rs):                               # running minimum from the lane end
            run = min(run, cap, r_, accept + slope * (d0 - s_))
            band.append(round(max(run, 0.0), 6))
        lip = min(cap, rs[0])
        seg1[nm] = {"s": ss, "reach": rs, "reach_bound_by": bs, "band": band, "accept_at_bend": round(accept, 6), "reach_at_bend": top,
                    "accept_lands": {"side_of_segment_2": landed[0], "along_from_bend": round(landed[1], 6), "across": round(landed[2], 6),
                                     "band_there": round(band2_at(landed[0], landed[1]), 6)},
                    "band_at_lane_end": band[0], "flight_lip": lip, "lip_shortfall": round(max(lip - (accept + slope * d0), 0.0), 6),
                    "band_at_lane_end_equals_flight_lip": lip - min(cap, rs[0], accept + slope * d0) <= tol}
        out["holds"] = out["holds"] and seg1[nm]["band_at_lane_end_equals_flight_lip"]
    for nm, side in (("left", 1.0), ("right", -1.0)):
        w = seg2[nm]["band"][-1]                                     # segment 2's band at P1, carried into segment 1's frame
        X = (l2[0] * side * w, l2[1] * side * w)
        a = X[0] * l1[0] + X[1] * l1[1]
        on = "left" if a >= 0 else "right"
        fits = abs(a) <= seg1[on]["accept_at_bend"] + tol
        seg2[nm]["carried_into_segment_1"] = {"width_at_bend": w, "side_of_segment_1": on, "across": round(abs(a), 6),
                                              "accept_there": seg1[on]["accept_at_bend"], "fits": fits}
        out["holds"] = out["holds"] and fits
    out["bend_deg"] = round(math.degrees(math.acos(max(-1.0, min(1.0, v1[0] * v2[0] + v1[1] * v2[1])))), 2)
    out["segment_1"], out["segment_2"] = seg1, seg2
    out["segment_1_left"], out["segment_2_left"] = l1, l2
    return out


def built_in(P, L, T, landing):
    """{(flight, end): record} for the lane ends on landings that use the built-in straight run: the hand-over point
    `built_in_run` straight on, with its claimed margins over `capsule_reach`."""
    ck = P["checks"]
    out = {}
    for k in range(1, L.n_landings + 1):
        la = landing(k)
        if not la.straight_fits(ck["built_in_run"]):
            continue
        pts = []
        for e in la.ends:
            c = (e["q"][0] + e["d"][0] * ck["built_in_run"], e["q"][1] + e["d"][1] * ck["built_in_run"])
            pts.append(c)
        cl, cw = math.hypot(pts[0][0] - pts[1][0], pts[0][1] - pts[1][1]), la.line_wall(pts[0], pts[1])[0]
        cf = la.line_fall(pts[0], pts[1])
        n = max(1, int(cl / 0.001))
        cm = min(la.mouth_d((pts[0][0] + (pts[1][0] - pts[0][0]) * t / n, pts[0][1] + (pts[1][1] - pts[0][1]) * t / n)) for t in range(n + 1))
        for e, c in zip(la.ends, pts):
            out[(e["flight"], e["end"])] = {
                "landing": k, "kind": "built-in straight run", "run": ck["built_in_run"], "point": (c[0], c[1], L.levels[k]),
                "point_wall_margin": round(la.wall_d(c) - la.reach, 4), "point_mouth_margin": round(la.mouth_d(c) - la.reach, 4),
                "segment_line_clear": [round(la.line_wall(e["q"], c)[0], 4)], "segment_line_proxy": [la.line_wall(e["q"], c)[1]],
                "segment_line_margins": [round(la.line_wall(e["q"], c)[0] - ck["wall_reach"], 4)],
                "crossing_length": round(cl, 4), "crossing_wall_margin": round(cw - la.reach, 4),
                "crossing_mouth_min": round(cm, 4), "crossing_mouth_margin": round(cm - la.reach, 4),
                "crossing_fall_edge_min": round(cf, 4), "crossing_fall_edge_margin": round(cf - la.reach, 4),
                "point_fall_edge_margin": round(la.fall_d(c) - la.reach, 4)}
    return out


def search_landing(la, ck, k):
    """The layout TOOL's search for one landing (never run inside a build): the frozen behaviour first, then the
    fallback rule 8 allows. Returns {"frozen", "behaviour", "fallback", "attempts", "pick" (a, b, crossing) or None,
    "curve", "exhaustive"}. Exact on its grid: no cap; a time-out raises and yields no result."""
    frozen = ck["frozen_behaviour"].get(str(k))
    if frozen not in ("carried", "stop"):
        raise ValueError(f"landing {k} needs a linked run-out and has no frozen behaviour in the table")
    names = [f"flight {e['flight']} {e['end']}" for e in la.ends]
    la.deadline = time.time() + ck["run_out_search_time_limit_s"]
    short = [i for i in (0, 1) if names[i] in ck["shortest_first_segment"]]
    pts = la.region()
    curve = []

    def search(beh):
        """The selection order; for an end named in shortest_first_segment, the shortest first segment that passes:
        lengths are tried in ascending order and nothing is searched past the first that yields a pair. The lengths
        that failed before it are the curve."""
        if not short:
            return la.bent(pts, beh)
        i = short[0]
        d0 = ck["run_out_first_segment_min"]
        while d0 <= FIRST_SEGMENT_SEARCH_LIMIT and la.first_ok(la.ends[i], d0):
            r = la.bent(pts, beh, fixed=(d0 if i == 0 else None, d0 if i == 1 else None))
            curve.append({"behaviour": list(beh), "first_segment_length": d0, "passes_rules_1_to_5": bool(r),
                          "candidates": list(la.last_counts), "smallest_margin": round(r[0], 4) if r else None})
            if r:
                return r
            d0 = round(d0 + G, 6)
        return None
    attempts = []
    beh = (frozen, frozen)
    pick = search(beh) if pts else None
    attempts.append({"behaviour": list(beh), "found": bool(pick), "margin": round(pick[0], 4) if pick else None, "exhaustive": True,
                     "candidates": list(la.last_counts), "quick_rejects": la.quick_rejects, "rule_3_rejections": la.reject_summary()})
    fallback = False
    if not pick and pts and frozen == "carried":
        # rule 8a: only the ends named in the table may fall back to a stop (turning toward the next flight); this is
        # a finding, not a result to copy. An end not named (flight 8 foot) keeps "carried": rule 8b, no fallback.
        fb = tuple("stop-fallback" if names[i] in ck["stop_fallback_allowed"] else frozen for i in (0, 1))
        if fb != beh:
            beh = fb
            pick = search(beh)
            attempts.append({"behaviour": list(beh), "found": bool(pick), "margin": round(pick[0], 4) if pick else None, "exhaustive": True,
                             "candidates": list(la.last_counts), "quick_rejects": la.quick_rejects, "rule_3_rejections": la.reject_summary()})
            fallback = bool(pick)
    return {"frozen": frozen, "behaviour": beh, "fallback": fallback, "attempts": attempts, "pick": pick, "curve": curve,
            "exhaustive": True, "region_points": len(pts)}


def landing_records(la, L, ck, k, pick, beh, frozen, fallback, attempts, curve):
    """The two lane ends' manifest records for a chosen pair, every figure derived from the pair. One function for a
    pair the tool has just found and a pair the build has just verified from the stored layout."""
    out = {}
    names = [f"flight {e['flight']} {e['end']}" for e in la.ends]
    short = [i for i in (0, 1) if names[i] in ck["shortest_first_segment"]]
    m, a, b, cl, cw, cf = pick
    z = L.levels[k]
    # smallest distance to a stair-mouth edge along the crossing between the two hand-over points (every 1 mm)
    n_ = max(1, int(cl / 0.001))
    cm = min(la.mouth_d((a["pt"][0] + (b["pt"][0] - a["pt"][0]) * t / n_, a["pt"][1] + (b["pt"][1] - a["pt"][1]) * t / n_)) for t in range(n_ + 1))
    frame = ck["tower_walk_speed"] / 30.0
    for i, (e, o) in enumerate(zip(la.ends, (a, b))):
        stop = beh[i] != "carried"
        ang = abs(o["ang"])
        if stop:
            limit, limit_name = (ck["stop_bend_min"] if beh[i] == "stop" else ck["stop_bend_min_lever"] + ck["bend_limit_guard_deg"]), "stop floor"
        else:
            limit, limit_name = ((ck["carry_bend_max_toward"] - ck["bend_limit_guard_deg"]) if o["toward"] else ck["carry_bend_max_away"]), ("carry limit toward (the lever's angle less the guard)" if o["toward"] else "carry limit away")
        rec = {
            "landing": k, "shape": "two segments", "path": [(p[0], p[1], z) for p in o["path"]],
            "first_segment_length": round(o["d0"], 4),
            "one_frame_travel": round(frame, 4),
            "first_segment_note": "one_frame_travel is tower_walk_speed / 30, for information: a first segment shorter than that is allowed (the runtime resolves several junctions in one frame)",
            "segment_line_clear": [round(v, 4) for v in o["seg_clear"]], "segment_line_proxy": o["seg_proxy"],
            "segment_line_margins": [round(v, 4) for v in o["seg_m"]],
            "run_out_separation": round(min(W._seg_seg(s0, s1, t0, t1) for s0, s1 in zip(a["path"], a["path"][1:]) for t0, t1 in zip(b["path"], b["path"][1:])), 4),
            "point_wall_margin": round(la.wall_d(o["pt"]) - la.reach, 4), "point_mouth_margin": round(la.mouth_d(o["pt"]) - la.reach, 4),
            "turn_deg": round(ang, 2), "bend_deg": round(ang, 2),
            "bend_limits": {"guard_deg": ck["bend_limit_guard_deg"], "lever_angle": ck["carry_bend_max_toward"], "carry_away_max": ck["carry_bend_max_away"], "stop_floor": ck["stop_bend_min_lever"],
                            "frozen_stop_floor": ck["stop_bend_min"], "applies": limit_name, "limit": limit,
                            "room": round((ang - limit) if stop else (limit - ang), 2),
                            "above_lever_stop_floor": round(ang - ck["stop_bend_min_lever"], 2) if stop else None},
            "segment_turns_deg": [0.0, round(ang, 2)], "turn": "toward the next flight" if o["toward"] else "away from the next flight",
            "arrival": "stop-and-turn" if stop else "carried",
            "frozen_behaviour": frozen, "fallback": fallback, "attempts": attempts,
            "selection": "shortest first segment that passes, then margin" if i in short else "largest smallest-margin, then longest first segment, then smaller bend",
            "crossing_length": round(cl, 4), "crossing_wall_margin": round(cw - la.reach, 4),
            "crossing_mouth_min": round(cm, 4), "crossing_mouth_margin": round(cm - la.reach, 4),
            "crossing_fall_edge_min": round(cf, 4), "crossing_fall_edge_margin": round(cf - la.reach, 4),
            "point_fall_edge_margin": round(la.fall_d(o["pt"]) - la.reach, 4),
            "margin": round(min(o["m"], cw - la.reach, cl - la.reach, cf - la.reach), 4), "landing_margin": round(m, 4),
            "target_met": m >= ck["run_out_margin_target"] - 5e-5,
            "first_segment_search_limit_reached": False,
            "_bands": la.bands_record(o),
        }
        if i in short:
            rec["first_segment_curve"] = curve
            rec["first_segment_curve_note"] = "the first-segment lengths tried in ascending order by the layout tool; the first that passes is taken and nothing longer is searched"
        out[(e["flight"], e["end"])] = rec
    return out


def select(P, L, T, landing, only=None):
    """The layout TOOL: ({(flight index, 'head'|'foot'): record}, layout choices) for every lane end that needs a
    linked run-out. `only` limits it to the named landings (a debugging aid). Never called by a build."""
    ck = P["checks"]
    out, layout = {}, {}
    for k in range(1, L.n_landings + 1):
        if only is not None and k not in only:
            continue
        la = landing(k)
        if la.straight_fits(ck["built_in_run"]):
            continue
        r = search_landing(la, ck, k)
        names = [f"flight {e['flight']} {e['end']}" for e in la.ends]
        info = {"frozen_behaviour": r["frozen"], "behaviour": list(r["behaviour"]), "fallback": r["fallback"], "exhaustive": r["exhaustive"],
                "attempts": r["attempts"], "first_segment_curve": r["curve"], "region_points": r["region_points"]}
        if not r["pick"]:
            for e in la.ends:
                out[(e["flight"], e["end"])] = {"landing": k, "fail": "no run-out holds the rule on this landing", "attempts": r["attempts"]}
            layout[str(k)] = dict(info, ends=None)
            continue
        out.update(landing_records(la, L, ck, k, r["pick"], r["behaviour"], r["frozen"], r["fallback"], r["attempts"], r["curve"]))
        _, a, b = r["pick"][:3]
        layout[str(k)] = dict(info, ends={nm: {"first_segment_length": repr(float(o["d0"])), "hand_over": [repr(float(o["pt"][0])), repr(float(o["pt"][1]))]}
                                         for nm, o in zip(names, (a, b))})
    return out, layout


def from_layout(P, L, T, landing, layout):
    """The BUILD's path: {(flight index, end): record} from the stored run_out_layout, each stored pair checked against
    every rule the search enforces, through the same functions. A stored layout that breaks a rule stops the build,
    naming the landing, the end and the rule. `layout` is the block's "landings" table."""
    ck = P["checks"]
    out = {}
    for k in range(1, L.n_landings + 1):
        la = landing(k)
        if la.straight_fits(ck["built_in_run"]):
            if str(k) in layout:
                raise ValueError(f"run_out_layout names landing {k}, where the built-in straight run fits: rerun the layout tool")
            continue
        st = layout.get(str(k))
        if st is None:
            raise ValueError(f"run_out_layout has no entry for landing {k}, which needs a linked run-out: rerun the layout tool")
        if not st.get("ends"):
            for e in la.ends:
                out[(e["flight"], e["end"])] = {"landing": k, "fail": "the layout tool found no run-out that holds the rule on this landing", "attempts": st.get("attempts", [])}
            continue
        beh = tuple(st["behaviour"])
        cands = []
        for i, e in enumerate(la.ends):
            nm = f"flight {e['flight']} {e['end']}"
            se = st["ends"][nm]
            d0, c = float(se["first_segment_length"]), (float(se["hand_over"][0]), float(se["hand_over"][1]))
            o = la.candidate(e, d0, c)
            if o is None:
                raise ValueError(f"stored run-out for {nm} breaks a hard limit (B1 lengths, R1a hand-over point 0.30 m, or R1c carried line at wall_reach): rerun the layout tool")
            if not la.bend_class(o["ang"], o["toward"], beh[i]):
                raise ValueError(f"stored run-out for {nm} has a bend of {abs(o['ang']):.2f} deg, outside what '{beh[i]}' allows (B5): rerun the layout tool")
            if not la.bands(o):
                raise ValueError(f"stored run-out for {nm} fails rule 3 / B4 (band at the lane end equal to the lip, carry across the bend): rerun the layout tool")
            cands.append(o)
        r = la.pair(cands[0], cands[1])
        if r is None:
            raise ValueError(f"stored run-outs on landing {k} break the crossing rule (R5) or cross or touch each other: rerun the layout tool")
        frozen = ck["frozen_behaviour"].get(str(k))
        want = (frozen, frozen)
        fallback = bool(st.get("fallback"))
        if not fallback and beh != want:
            raise ValueError(f"stored behaviour {list(beh)} on landing {k} is not the frozen '{frozen}' and is not marked a fallback")
        out.update(landing_records(la, L, ck, k, (r[0], cands[0], cands[1], r[1], r[2], r[3]), beh, frozen, fallback,
                                   st.get("attempts", []), st.get("first_segment_curve", [])))
    return out


def mouth_side(a, e, side, walls, mouths, ok, reach, half_default, use_mouths):
    """Largest half-width of the join mouth on one side of the level stretch from a (lip or junction) to e (free end),
    and what stops it.

    Sides are named looking along the lane from the free end toward the stair: side = +1 is left, -1 is right.
    walls and mouths are lists of (name, distance function). On that side the allowed offset is a funnel:
    half-width x (1 - s / length), s from the free end. A half-width fits if every funnel point is on the floor (`ok`)
    and is no nearer any wall than `reach`, or than the centre line is at the same s where the centre line itself is
    nearer (the exit lane's foot overlaps one named wall). With use_mouths, it must also be `reach` from every stair
    mouth edge wherever the centre line is. Returns (half-width, margin at it, name of the binding constraint or '').
    Searched to 1 mm; sampled every 5 mm of length at five offsets."""
    ln = math.hypot(e[0] - a[0], e[1] - a[1])
    v = ((a[0] - e[0]) / ln, (a[1] - e[1]) / ln)
    left = (-v[1] * side, v[0] * side)
    n = max(2, int(ln / 0.005))
    cons = [(nm, fn, False) for nm, fn in walls] + ([(nm, fn, True) for nm, fn in mouths] if use_mouths else [])

    def worst(hw):
        m, who = 9.9, ""
        for i in range(n + 1):
            s_ = ln * i / n
            c = (e[0] + v[0] * s_, e[1] + v[1] * s_)
            h = hw * (1.0 - s_ / ln)
            for fr in (0.0, 0.25, 0.5, 0.75, 1.0):
                q = (c[0] + left[0] * h * fr, c[1] + left[1] * h * fr)
                if not ok(q):
                    return -1.0, "the edge of the deck"
                for nm, fn, is_mouth in cons:
                    cc = fn(c)
                    if is_mouth:
                        if cc < reach:
                            continue
                        base = reach
                    else:
                        base = min(reach, cc)
                    d_ = fn(q) - base
                    if d_ < m:
                        m, who = d_, nm
        return m, who
    m, who = worst(half_default)
    if m >= -1e-9:
        return half_default, round(m, 4), ""
    lo, hi = 0.0, half_default
    while hi - lo > 0.001:
        mid = (lo + hi) / 2.0
        if worst(mid)[0] >= -1e-9:
            lo = mid
        else:
            hi = mid
    return round(lo, 3), round(worst(lo)[0], 4), worst(hi)[1]


def reach_span(e, a, side, walls, ok, reach):
    """How far from the free end e, perpendicular to the lane on one side, a body can stand: on the deck and `reach`
    from every wall. Stepped at 1 mm, capped at 1 m. Also what ends it."""
    ln = math.hypot(e[0] - a[0], e[1] - a[1])
    v = ((a[0] - e[0]) / ln, (a[1] - e[1]) / ln)
    left = (-v[1] * side, v[0] * side)
    t = 0.0
    while t < 1.0:
        q = (e[0] + left[0] * (t + 0.001), e[1] + left[1] * (t + 0.001))
        if not ok(q):
            return round(t, 3), "the edge of the deck"
        nm, d_ = min(((nm_, fn(q)) for nm_, fn in walls), key=lambda x: x[1]) if walls else ("", 9.9)
        if d_ < reach:
            return round(t, 3), nm
        t += 0.001
    return 1.0, ""


def catch_side(a, e, side, band_hw, walls, ok, wall_reach, half_default):
    """Catch region on one side of the final level stretch: where a join from the floor may start.

    A position is reachable if it is on the floor and at least wall_reach from every wall. The catch half-width is the
    widest (cap half_default) such that from every reachable position in it, at any distance along the stretch, the
    straight sideways pull-in to the band (half-width band_hw x (1 - s / length)) passes only through reachable
    positions. Unreachable positions are simply not in the set. Returns (half-width, binding wall or '').
    Stretch sampled every 5 mm, offsets every 1 mm."""
    ln = math.hypot(e[0] - a[0], e[1] - a[1])
    v = ((a[0] - e[0]) / ln, (a[1] - e[1]) / ln)
    left = (-v[1] * side, v[0] * side)
    n = max(2, int(ln / 0.005))
    best, who = half_default, ""
    for i in range(n + 1):
        s_ = ln * i / n
        c = (e[0] + v[0] * s_, e[1] + v[1] * s_)
        if walls and min(fn(c) for _, fn in walls) < wall_reach - 1e-9:
            continue        # the lane itself carries the body nearer a wall here (the exit lane's foot); no join from the floor at this s
        o = band_hw * (1.0 - s_ / ln)
        blocked = None
        while o <= best + 1e-9:
            q = (c[0] + left[0] * o, c[1] + left[1] * o)
            nm, d_ = min(((nm_, fn(q)) for nm_, fn in walls), key=lambda x: x[1]) if walls else ("", 9.9)
            reachable = ok(q) and d_ >= wall_reach - 1e-9
            if not reachable and blocked is None:
                blocked = nm if ok(q) else "the edge of the deck"
            elif reachable and blocked is not None:
                # a reachable position beyond an unreachable one: its pull-in would cross the blocked stretch
                if o < best:
                    best, who = round(o - 0.001, 3), blocked
                break
            o += 0.001
    return max(best, 0.0), who


def reach_at(c, left, walls, ok, wall_reach, cap):
    """How far sideways from c, along the unit vector `left`, a body wall_reach from every wall can stand on the deck;
    capped. Zero where c itself is nearer a wall than wall_reach. The deck's edge is found by stepping 1 mm; the first
    wall by stepping no further than the clearance in hand (a wall's distance cannot fall faster than the body moves),
    never less than 1 mm; the last millimetre is then bisected to 1e-7 (REACH_BISECT). Returns (reach to six
    decimals, what bounds it)."""
    def wall(o):
        q = (c[0] + left[0] * o, c[1] + left[1] * o)
        return min(((fn(q), nm_) for nm_, fn in walls), key=lambda x: x[0]) if walls else (9.9, "")

    def bad(o):
        """What stops a body at offset o, or None."""
        if not ok((c[0] + left[0] * o, c[1] + left[1] * o)):
            return "the edge of the deck"
        d_, nm = wall(o)
        return nm if d_ < wall_reach - 1e-9 else None
    who = bad(0.0)
    if who is not None:
        return 0.0, who
    # the deck's edge, on the 1 mm grid
    edge, o = cap, 0.0
    while o < cap - 1e-9:
        o = min(o + 0.001, cap)
        if not ok((c[0] + left[0] * o, c[1] + left[1] * o)):
            edge = o
            break
    # the first wall before that
    o, hit = 0.0, None
    while o < edge - 1e-9:
        d_, nm = wall(o)
        if d_ < wall_reach - 1e-9:
            hit = (o, nm)
            break
        o = min(o + max(d_ - wall_reach, 0.001), edge)
    if hit is None:
        d_, nm = wall(edge)
        if d_ < wall_reach - 1e-9:
            hit = (edge, nm)
    if hit is None and edge >= cap - 1e-9 and ok((c[0] + left[0] * cap, c[1] + left[1] * cap)):
        return round(cap, 6), "the cap"
    hi, who = (hit[0], hit[1]) if hit else (edge, "the edge of the deck")
    lo = max(hi - 0.001, 0.0)
    while bad(lo) is not None and lo > 0.0:          # a long step can land past the limit: walk back to a clear offset
        hi, lo = lo, max(lo - 0.001, 0.0)
    while hi - lo > 1e-7:
        mid = (lo + hi) / 2.0
        if bad(mid) is None:
            lo = mid
        else:
            hi = mid
    if not REACH_BISECT:
        return round(math.floor(lo * 1000.0 + 1e-4) / 1000.0, 6), who          # the comparison run: whole millimetres
    return round(lo, 6), who


def reach_profile(a, e, side, walls, ok, wall_reach, cap, spacing=0.01, at=None):
    """reach(s) along the level stretch from the free end e (s = 0) to the lip or junction a (s = length), on one side:
    how far sideways, perpendicular to the lane, a body wall_reach from every wall can stand on the deck; capped.
    Zero where the lane's own centre line is nearer a wall than wall_reach. Sampled every `spacing`, plus the far end
    where the length is not a whole number of spacings (s is strictly increasing); reach to six decimals (see reach_at).
    Returns (list of s, list of reach, list of what bounds it)."""
    ln = math.hypot(e[0] - a[0], e[1] - a[1])
    v = ((a[0] - e[0]) / ln, (a[1] - e[1]) / ln)
    left = (-v[1] * side, v[0] * side)
    ss = [round(i * spacing, 4) for i in range(int(ln / spacing + 1e-9) + 1)]
    if round(ln, 4) > ss[-1]:
        ss.append(round(ln, 4))
    rs, bs = [], []
    for s_ in ss:
        s_ = min(s_, ln)          # the last sample stands on the lip itself, not a rounding step past it
        c_ = (e[0] + v[0] * s_, e[1] + v[1] * s_)
        r_, who = at(c_, left) if at else reach_at(c_, left, walls, ok, wall_reach, cap)
        rs.append(r_)
        bs.append(who)
    return ss, rs, bs
