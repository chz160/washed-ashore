"""Tower frame, footings, stairs, landings and antennas for the MC-39 build. Pure Python (no bpy).

Design frame: x = east, y = north, z = up. Every function takes a lod (0, 1 or 2) and writes into a MeshBuf.
"""
import math
import random

import mc39_poly as poly

from mc39_atlas import N_STEEL, N_WOOD, region
from mc39_layout import D, IN, SQ2, U, diag
from mc39_mesh import (MAT_CUTOUT, MAT_OPAQUE, MeshBuf, add, angle, bar, box, cross, cylinder, dot, length, lerp,
                       mul, norm, perp_to, prism, sub)

UP = (0.0, 0.0, 1.0)
# name, corner signs at each end (the face runs from c0 to c1), inward horizontal normal
FACES = [
    ("N", (-1, 1), (1, 1), (0.0, -1.0, 0.0)),
    ("E", (1, 1), (1, -1), (-1.0, 0.0, 0.0)),
    ("S", (1, -1), (-1, -1), (0.0, 1.0, 0.0)),
    ("W", (-1, -1), (-1, 1), (1.0, 0.0, 0.0)),
]
CORNERS = [(1, 1), (1, -1), (-1, -1), (-1, 1)]


LEG_IN = [3, 3, 3.5, 4, 4, 4, 5, 5, 5]          # leg angle size by panel, counted from the top (inches)


def sized(from_top, table):
    return table[min(from_top, len(table) - 1)] * IN


class Tower:
    def __init__(self, P, L):
        self.P, self.L = P, L
        self.seed = P["seed"]
        self.decay = P["decay"]["amount"]
        self.min_w = P["lod"]["lod2_min_member_width"]
        self.obstacles = []      # dicts: name, p0, p1, r (half section), kind, owner; for the corridor sweep
        self._names = {}
        self.rail_inventory = []

    def rng(self, part):
        return random.Random(f"{self.seed}:{part}")

    def steel(self, r, rusty=0.0):
        """A steel strip; higher-numbered strips carry more rust. `rusty` biases the pick (0..1)."""
        x = min(max(r.random() * 0.75 + (self.decay - 0.5) * 0.3 + rusty * 0.4, 0.0), 0.999)
        return region(f"STEEL{int(x * N_STEEL)}")

    def member(self, buf, name, p0, p1, fa, fb, size, r, lod, rusty=0.0, t=0.009):
        angle(buf, p0, p1, fa, fb, size, t, self.steel(r, rusty), lod, min_w=self.min_w)
        if lod == 0:
            self.obs(name, p0, p1, size / 2.0, "frame")

    def obs(self, name, p0, p1, r, kind, owner=None, unique=False):
        """Record a member for the corridor sweep. Repeated names get a running number so each is named singly."""
        if not unique:
            n = self._names.get(name, 0) + 1
            self._names[name] = n
            name = f"{name}_{n}"
        self.obstacles.append({"name": name, "p0": p0, "p1": p1, "r": r, "kind": kind, "owner": owner})
        return name

    def leg_sizes(self):
        """[(z0, z1, flange size in metres)] of the visible leg angle, panel by panel from the bed plate up."""
        n = self.L.n_flights
        return [(self.L.steel[k], self.L.steel[k + 1], sized(n - 1 - k, LEG_IN)) for k in range(n)]

    def corner(self, c, z, inset=0.0):
        a = self.L.half(z) - inset
        return (c[0] * a, c[1] * a, z)

    # ---- frame --------------------------------------------------------------------------------------------------
    def frame(self, lod):
        L = self.L
        buf = MeshBuf(f"Frame_LOD{lod}")
        r = self.rng("frame")
        n = L.n_flights
        zs = L.steel          # the frame stands on the drawn steel levels; the decks lie on top
        for k in range(n):
            ft = n - 1 - k
            leg = sized(ft, LEG_IN)
            for c in CORNERS:
                p0, p1 = self.corner(c, zs[k]), self.corner(c, zs[k + 1])
                if lod == 2:
                    angle(buf, p0, p1, (-c[0], 0, 0), (0, -c[1], 0), max(leg, self.min_w), 0.01, self.steel(r), 1)
                else:
                    self.member(buf, f"leg{k}", p0, p1, (-c[0], 0, 0), (0, -c[1], 0), leg, r, lod, rusty=0.2, t=0.011)
        # legs carry on as the cab corner posts
        for c in CORNERS:
            p0, p1 = self.corner(c, L.z_steel), self.corner(c, L.z_eave)
            if lod == 2:
                angle(buf, p0, p1, (-c[0], 0, 0), (0, -c[1], 0), max(3 * IN, self.min_w), 0.01, self.steel(r), 1)
            else:
                self.member(buf, "cabpost", p0, p1, (-c[0], 0, 0), (0, -c[1], 0), 3 * IN, r, lod, rusty=0.5)

        for fname, c0, c1, nin in FACES:
            for k in range(n):
                ft = n - 1 - k
                dg = sized(ft, [2, 2, 2, 2, 2.5, 2.5, 2.5, 3, 3])
                gt = sized(ft, [3, 2, 2, 2.5, 2.5, 3, 3, 4, 4])
                ins = 0.012
                b0, b1 = self.corner(c0, zs[k], ins), self.corner(c1, zs[k], ins)
                t0, t1 = self.corner(c0, zs[k + 1], ins), self.corner(c1, zs[k + 1], ins)
                h = norm(sub(b1, b0))
                tag = f"{fname}{k}"
                # girt at the top of the panel (the top one is the cab sill frame)
                # girts stop at the legs' flanges: two faces' girts would otherwise share the corner, in one plane
                lt = sized(ft, LEG_IN) * 0.8
                self.member(buf, "girt" + tag, add(t0, mul(h, lt)), add(t1, mul(h, -lt)), (0, 0, -1), nin, gt, r, lod, rusty=0.3)
                # X diagonals, back to back with a filler between them where they cross (T-1007)
                o2 = mul(nin, 0.012)
                o1 = mul(nin, 0.001)          # a millimetre off the girts' plane
                self.member(buf, "diag" + tag, add(b1, o1), add(t0, o1), perp_to(UP, norm(sub(t0, b1))), nin, dg, r, lod)
                self.member(buf, "diag" + tag, add(b0, o2), add(t1, o2), perp_to(UP, norm(sub(t1, b0))), nin, dg, r, lod)
                # the diagonals cross at wb / (wb + wt) of the panel height
                wb, wt = length(sub(b1, b0)), length(sub(t1, t0))
                fx = wb / (wb + wt)
                xc = lerp(lerp(b0, b1, 0.5), lerp(t0, t1, 0.5), fx)
                if lod < 2:
                    m0, m1 = lerp(b0, t0, fx), lerp(b1, t1, fx)
                    o3 = mul(nin, 0.002)          # in the first diagonal's layer: nothing stands proud into the stair
                    self.member(buf, "mid" + tag, add(add(m0, o3), mul(h, lt)), add(add(m1, o3), mul(h, -lt)), (0, 0, -1), nin, 2 * IN, r, lod)
                    if zs[k + 1] - zs[k] > 3.0:
                        self.member(buf, "hang" + tag, add(lerp(t0, t1, 0.5), o3), add(xc, o3), h, nin, 2 * IN, r, lod)
                if lod == 0:
                    up = norm(sub(t0, b0))
                    g = 0.13
                    for pt, hd in ((t0, h), (t1, mul(h, -1))):
                        c = add(add(pt, mul(hd, g + 0.02)), mul(nin, -0.006))          # 2 mm clear of the flanges it sits on: no two faces in one plane
                        box(buf, c, mul(hd, g), mul(up, g), mul(nin, 0.004), self.steel(r, 0.6), u_off=r.random() * 0.3)
                    box(buf, add(xc, mul(nin, dg * 0.5)), mul(h, 0.1), mul(up, 0.1), mul(nin, 0.004), self.steel(r, 0.6), u_off=r.random() * 0.3)

        # plan frame turned 45 degrees under each landing
        if lod < 2:
            for k in range(1, L.n_landings + 1):
                z = zs[k] - 0.004          # a hair under the girts' flanges, which share its level
                a = L.half(z) - 0.03
                pts = [(a, 0, z), (0, a, z), (-a, 0, z), (0, -a, z)]
                own = 0 if k % 2 == 1 else 2          # the edge on this landing's corner side (NE first)
                for i in range(4):
                    p0, p1 = pts[i], pts[(i + 1) % 4]
                    # Left out where it would cross a stair: on the landing's own side when the flight reaches past it
                    # (the landing's edge angle carries the planks there), and opposite it over a short panel.
                    if i == own and a / SQ2 < L.edge[k] + 0.05:
                        continue
                    if i == (own + 2) % 4 and zs[k] - zs[k - 1] < 2.2:
                        continue
                    inward = norm(mul(lerp(p0, p1, 0.5), -1.0))
                    # each side stops a flange-width short of the diamond's corner, so two sides never share it in one plane
                    ax_ = norm(sub(p1, p0))
                    self.member(buf, f"plan{k}", add(p0, mul(ax_, 0.07)), add(p1, mul(ax_, -0.07)), (0, 0, -1), (inward[0], inward[1], 0.0), 2.5 * IN, r, lod)
        return buf

    # ---- footings -----------------------------------------------------------------------------------------------
    def pad_boxes(self):
        """(centre, half size) of the four leg pads (tops at z = 0) and the stair-foot pad."""
        p = self.P["pads"]
        hs, dp = p["size"] / 2.0, p["depth"]
        a = self.L.half(0.0) - 0.06
        out = [((c[0] * a, c[1] * a, -dp / 2.0), (hs, hs, dp / 2.0)) for c in CORNERS]
        f = self.L.flights[0]
        x, y, _ = f["first_riser"]
        # the stair-foot landing's top is the bottom flight's walking surface (pads._stair_foot in the table)
        out.append(((x - f["dir"][0] * 0.1, y - f["dir"][1] * 0.1, self.L.levels[0] - dp / 2.0), (0.55, 0.55, dp / 2.0)))
        return out

    def pads(self, lod):
        buf = MeshBuf(f"Pads_LOD{lod}")
        r = self.rng("pads")
        con = region("CONCRETE")
        for i, (c, hsz) in enumerate(self.pad_boxes()):
            ring = [(c[0] - hsz[0], c[1] - hsz[1], c[2] - hsz[2]), (c[0] + hsz[0], c[1] - hsz[1], c[2] - hsz[2]),
                    (c[0] + hsz[0], c[1] + hsz[1], c[2] - hsz[2]), (c[0] - hsz[0], c[1] + hsz[1], c[2] - hsz[2])]
            prism(buf, ring, (0, 0, 2 * hsz[2]), con, (1, 0, 0), u_off=r.random() * 0.05, v_scale=1.0)
            if lod == 0 and i < 4:
                # bed plate and the two anchor bolts
                cc = CORNERS[i]
                a = self.L.half(0.0)
                pc = (cc[0] * (a - 0.1), cc[1] * (a - 0.1), 0.01)
                box(buf, pc, (0.16, 0, 0), (0, 0.16, 0), (0, 0, 0.01), self.steel(r, 0.9), u_off=r.random())
                for sx, sy in ((1, -1), (-1, 1)):
                    q = (pc[0] + sx * 0.11, pc[1] + sy * 0.11, 0.02)
                    cylinder(buf, q, add(q, (0, 0, 0.06)), 0.014, 0.014, 6, self.steel(r, 1.0))
        return buf

    # ---- stairs -------------------------------------------------------------------------------------------------
    def stairs(self, lod):
        L, st = self.L, self.P["stairs"]
        buf = MeshBuf(f"Stairs_LOD{lod}")
        r = self.rng("stairs")
        hw = L.stair_w / 2.0
        td0, tt = st["tread_depth"], st["tread_thickness"]
        nd = L.n_flights - 1
        for f in L.flights:
            dx, dy = f["dir"]
            d3 = (dx, dy, 0.0)
            lat = (f["lat"][0], f["lat"][1], 0.0)
            n = f["risers"]
            td = min(td0, f["going"] - 0.011)
            foot, head = f["foot"], f["head"]
            slope = norm(sub(head, foot))
            if lod == 2:
                # one pale slab per flight
                w = hw + 0.05
                a0, a1 = add(foot, mul(lat, -w)), add(foot, mul(lat, w))
                b0, b1 = add(head, mul(lat, -w)), add(head, mul(lat, w))
                buf.quad_region(a0, a1, b1, b0, region("STEEL1"), f=(0.3, 0.0, 0.9, 1.0))
                continue
            # treads: perforated plate, nosing at the downhill edge
            for i in range(1, n):
                ns = L.nosing(f, i)
                tr = region(f"TREAD{r.randrange(2)}")
                c = add(ns, add(mul(d3, td / 2.0), (0, 0, -tt / 2.0)))
                if lod == 0:
                    top = [add(c, add(mul(lat, sl * hw), add(mul(d3, sd * td / 2.0), (0, 0, tt / 2.0)))) for sl, sd in ((-1, -1), (1, -1), (1, 1), (-1, 1))]
                    bot = [add(p, (0, 0, -tt)) for p in top]
                    buf.quad_region(top[0], top[1], top[2], top[3], tr, MAT_CUTOUT)
                    buf.quad_region(bot[1], bot[0], bot[3], bot[2], tr, MAT_CUTOUT)
                    # solid nosing and back edges
                    for a, b in ((0, 1), (2, 3)):
                        buf.quad_region(bot[a], bot[b], top[b], top[a], self.steel(r), f=(0.4, 0.0, 0.55, 0.12))
                else:
                    top = [add(c, add(mul(lat, sl * hw), add(mul(d3, sd * td / 2.0), (0, 0, tt / 2.0)))) for sl, sd in ((-1, -1), (1, -1), (1, 1), (-1, 1))]
                    buf.quad_region(top[0], top[1], top[2], top[3], tr, MAT_CUTOUT)
            # stringers
            sd_, sth = st["stringer_depth"], st["stringer_thickness"]
            nrm = norm(cross(slope, lat))
            if nrm[2] < 0:
                nrm = mul(nrm, -1.0)
            s0 = add(lerp(foot, head, 0.02), mul(nrm, -(sd_ / 2.0 + 0.01)))
            s1 = add(head, mul(nrm, -(sd_ / 2.0 + 0.01)))
            for sl in (-1, 1):
                o = mul(lat, sl * (hw + sth / 2.0))
                p0, p1 = add(s0, o), add(s1, o)
                if lod == 0:
                    bar(buf, p0, p1, nrm, sd_, sth, self.steel(r), u_off=r.random() * 0.5)
                    self.obs(f"Stringer_F{f['index']:02d}_{'L' if sl < 0 else 'R'}", p0, p1, sth / 2, "stringer", ("flight", f["index"]), True)
                else:
                    q0, q1 = add(p0, mul(nrm, -sd_ / 2)), add(p1, mul(nrm, -sd_ / 2))
                    buf.quad_region(q0, q1, add(q1, mul(nrm, sd_)), add(q0, mul(nrm, sd_)), self.steel(r), f=(0.05, 0.0, 0.95, 0.5))
            # handrails: top and mid rail, posts. Rails and posts stand on the stringers (T-1006).
            rh = st["rail_height"]
            tan_p = L.riser / f["going"]
            for sl in (-1, 1):
                side = 'L' if sl < 0 else 'R'
                o = mul(lat, sl * (hw + sth * 0.5))
                run = f["run"] + f["going"]
                trim = 0.0
                if f["index"] == L.n_flights:
                    # OM1 (approved by t-director by name): the top flight's rails stop where their top comes within
                    # `top_flight_rail_floor_gap` of the cab floor's underside; carried on they would pass through the
                    # floor and the closed trap door.
                    trim = (rh + 0.0225 + self.P["cab"]["floor_thickness"] + st["top_flight_rail_floor_gap"]) / tan_p
                t1 = 1.0 - trim / run
                r0 = add(lerp(foot, head, f["going"] / run), o)
                r1 = add(lerp(foot, head, t1), o)
                rails = ((rh, 0.045, "Top"), (rh * 0.5, 0.035, "Mid")) if lod == 0 else ((rh, 0.045, "Top"),)
                for hgt, w, tag in rails:
                    p0, p1 = add(r0, (0, 0, hgt)), add(r1, (0, 0, hgt))
                    if lod == 0:
                        bar(buf, p0, p1, UP, w, w, self.steel(r), u_off=r.random() * 0.5)
                        nm = self.obs(f"HandRail_F{f['index']:02d}_{side}_{tag}", p0, p1, w / 2, "rail", ("flight", f["index"]), True)
                        if tag == "Top":
                            self.rail_inventory.append({"name": nm, "type": "handrail", "flight": f["index"], "side": side, "p0": p0, "p1": p1,
                                                        "height_above_nosing": rh, "stops_short_of_head_run": trim, "sheet": "USFS T-1006-A"})
                    else:
                        buf.quad_region(add(p0, (0, 0, -w / 2)), add(p1, (0, 0, -w / 2)), add(p1, (0, 0, w / 2)), add(p0, (0, 0, w / 2)), self.steel(r), f=(0.05, 0.0, 0.95, 0.3))
                step = st["rail_post_every_treads"]
                last_i = max(1, int((n - 1) * t1)) if trim > 0 else n - 1
                posts = [L.nosing(f, i) for i in sorted(set(list(range(1, last_i + 1, step)) + [last_i]))]
                if trim > 0:
                    posts.append(lerp(foot, head, t1))        # a made end: the rail finishes on a post
                for j, ns0 in enumerate(posts, 1):
                    ns = add(ns0, o)
                    base = add(ns, (0, 0, -0.12))
                    topp = add(ns, (0, 0, rh))          # ends inside the rail, so no cap shows
                    if lod == 0:
                        bar(buf, base, topp, d3, 0.04, 0.04, self.steel(r), u_off=r.random() * 0.5)
                        self.obs(f"HandPost_F{f['index']:02d}_{side}_{j}", base, topp, 0.02, "rail", ("flight", f["index"]), True)
                    else:
                        buf.quad_region(add(base, mul(d3, -0.02)), add(base, mul(d3, 0.02)), add(topp, mul(d3, 0.02)), add(topp, mul(d3, -0.02)), self.steel(r), f=(0.05, 0.0, 0.4, 0.3))
        return buf

    # ---- landings -----------------------------------------------------------------------------------------------
    def landing_ends(self, k):
        """Flight ends that stand on landing k: (flight, 'head' or 'foot', point)."""
        L = self.L
        return [(L.flights[k - 1], "head", L.flights[k - 1]["head"]), (L.flights[k], "foot", L.flights[k]["foot"])]

    def mouth_half(self):
        """Half the overall width of a flight over its stringers (2'-4" / 2)."""
        return self.L.stair_w / 2.0 + self.P["stairs"]["stringer_thickness"]

    def landings(self, lod):
        """Plank decks on a light angle frame. T-1004-A: 5'-2" x 2'-6" platforms, cut by the tower faces where the
        corner is smaller; the top landing fills its drawn frame (see landings.top_landing in the table)."""
        L, lp = self.L, self.P["landings"]
        buf = MeshBuf(f"Landings_LOD{lod}")
        pt = lp["plank_thickness"]
        for k in range(1, L.n_landings + 1):
            r = self.rng(f"landing{k}")
            z = L.levels[k]
            outline = L.landing_polygon(k)
            if lod == 2:
                ring = self._ccw_up(outline)
                wood = region("WOOD0")
                for i in range(1, len(ring) - 1):
                    buf.tri_region(ring[0], ring[i], ring[i + 1], wood, f=((0.1, 0.0), (0.6, 0.0), (0.35, 1.0)))
                continue
            o, al, ac = L.plank_frame(k)
            ss = [(q[0] - o[0]) * ac[0] + (q[1] - o[1]) * ac[1] for q in outline]
            s_end = max(ss)

            def at(u, sv, zz):
                return (o[0] + al[0] * u + ac[0] * sv, o[1] + al[1] * u + ac[1] * sv, zz)
            t = 0.0
            while t < s_end - 0.03:
                w = lp["plank_width"] * (1.0 + (r.random() - 0.5) * 0.12)
                w = min(w, s_end - t)
                gap = lp["plank_gap_min"] + (lp["plank_gap_max"] - lp["plank_gap_min"]) * (r.random() ** 2) * self.decay
                pieces = [(t, t + w)]
                if lod == 0 and r.random() < self.P["decay"]["plank_split_fraction"] * self.decay and w > 0.1:
                    cut = w * (0.35 + 0.3 * r.random())
                    crack = 0.006 + 0.012 * r.random()
                    pieces = [(t, t + cut - crack / 2), (t + cut + crack / 2, t + w)]
                for a, b in pieces:
                    sag = -0.006 * r.random() * self.decay
                    ia, ib = poly.interval(outline, o, al, ac, a + 1e-6), poly.interval(outline, o, al, ac, b - 1e-6)
                    sa, sb = (r.random() * lp["plank_end_stagger"] for _ in range(2))
                    if ia is None or ib is None:
                        continue
                    l0a, l0b, l1a, l1b = ia[0] + sa, ia[1] - sb, ib[0] + sa, ib[1] - sb
                    if l0b - l0a < 0.1 or l1b - l1a < 0.1:
                        continue
                    wood = region(f"WOOD{r.randrange(N_WOOD)}")
                    span = max(l0b - l0a, l1b - l1a)
                    uo = r.random() * max(wood.length - span - 0.05, 0.0) - min(l0a, l1a)
                    if lod == 0:
                        ring = self._ccw_up([at(l0a, a, z - pt + sag), at(l0b, a, z - pt + sag), at(l1b, b, z - pt + sag), at(l1a, b, z - pt + sag)])
                        prism(buf, ring, (0, 0, pt), wood, (al[0], al[1], 0.0), u_off=uo + (ring[0][0] - o[0]) * al[0] + (ring[0][1] - o[1]) * al[1])
                    else:
                        q = self._ccw_up([at(l0a, a, z), at(l0b, a, z), at(l1b, b, z), at(l1a, b, z)])
                        buf.face(q, [wood.uv(uo + (p_[0] - o[0]) * al[0] + (p_[1] - o[1]) * al[1],
                                             wood.width * (1.0 if abs((p_[0] - o[0]) * ac[0] + (p_[1] - o[1]) * ac[1] - b) < 1e-6 else 0.0)) for p_ in q])
                t += w + gap

            sr = self.rng(f"landingsteel{k}")
            zf = z - pt - 0.002
            lim = L.half(z) - 0.03
            # two bearers along the planks' ends of the deck, run out to the tower faces, and three joists across them
            for sv in (0.04, s_end - 0.04):
                # an ordinary platform's bearers run out to the tower faces; the top landing's stay under its own deck,
                # because run on they would cross the flight that arrives beside it
                box_ = outline if L.is_frame_landing(k) else [(-lim, -lim), (lim, -lim), (lim, lim), (-lim, lim)]
                iv = poly.interval(box_, o, al, ac, sv)
                if iv:
                    self.member(buf, f"landbearer{k}", at(iv[0], sv, zf), at(iv[1], sv, zf), (0, 0, -1), (ac[0], ac[1], 0.0), 2.5 * IN, sr, lod)
            us = [(q[0] - o[0]) * al[0] + (q[1] - o[1]) * al[1] for q in outline]
            u0, u1 = min(us), max(us)
            for fr in (0.12, 0.5, 0.88):
                u = u0 + (u1 - u0) * fr
                iv = poly.interval(outline, o, ac, al, u)
                if iv and iv[1] - iv[0] > 0.2:
                    self.member(buf, f"landjoist{k}", at(u, iv[0] + 0.04, zf), at(u, iv[1] - 0.04, zf), (0, 0, -1), (al[0], al[1], 0.0), 2 * IN, sr, lod)

            gh = lp["guard_height"]
            placed = []
            for i, (p0, p1) in enumerate(self.guard_runs(k), 1):
                self._guard(buf, k, i, p0, p1, gh, sr, lod, placed)
        return buf

    def edge_openings(self, k):
        """Per deck edge of landing k: the stretches left open, as (edge index, a, b, s0, s1, why).
        'mouth' = a flight's foot or head stands there; 'under top flight' = the top flight passes over the edge lower
        than a guard rail could stand (the stair closes it)."""
        L, lp = self.L, self.P["landings"]
        outline = L.landing_polygon(k)
        n = len(outline)
        w = self.mouth_half()
        tf = L.flights[-1]
        gh = lp["guard_height"]
        out = []
        for i in range(n):
            a, b = outline[i], outline[(i + 1) % n]
            ln = math.hypot(b[0] - a[0], b[1] - a[1])
            if ln < 1e-6:
                continue
            d = ((b[0] - a[0]) / ln, (b[1] - a[1]) / ln)
            for f, end, q in self.landing_ends(k):
                sv = (q[0] - a[0]) * d[0] + (q[1] - a[1]) * d[1]
                off = abs((q[0] - a[0]) * -d[1] + (q[1] - a[1]) * d[0])
                if off < 0.03 and -w < sv < ln + w:
                    out.append((i, a, b, max(sv - w, 0.0), min(sv + w, ln), "mouth"))
            # under the top flight
            if k == L.n_landings:
                foot, head = tf["foot"], tf["head"]
                run = math.hypot(head[0] - foot[0], head[1] - foot[1])
                slope = (head[2] - foot[2]) / run
                start = None
                m = max(1, int(ln / 0.01))
                for j in range(m + 1):
                    sv = ln * j / m
                    q = (a[0] + d[0] * sv, a[1] + d[1] * sv)
                    al_ = (q[0] - foot[0]) * tf["dir"][0] + (q[1] - foot[1]) * tf["dir"][1]
                    la_ = (q[0] - foot[0]) * tf["lat"][0] + (q[1] - foot[1]) * tf["lat"][1]
                    under = abs(la_) <= w and 0.02 < al_ <= run and (al_ * slope - self.P["stairs"]["stringer_depth"] - 0.03) < gh + 0.05
                    if under and start is None:
                        start = sv
                    if (not under or j == m) and start is not None:
                        out.append((i, a, b, start, sv, "under top flight"))
                        start = None
        return out

    def guard_runs(self, k, under_stair=False):
        """Guard rail runs of landing k as (p0, p1) at deck height: every deck edge, less the flight mouths and less the
        stretches the top flight closes (T-1006-A typical landing: rail on the landing's own edge). With under_stair
        the stretches under the top flight are kept, for the invisible wall there."""
        L = self.L
        outline = L.landing_polygon(k)
        n = len(outline)
        opens = self.edge_openings(k)
        runs = []
        for i in range(n):
            a, b = outline[i], outline[(i + 1) % n]
            ln = math.hypot(b[0] - a[0], b[1] - a[1])
            if ln < 0.10:
                continue
            d = ((b[0] - a[0]) / ln, (b[1] - a[1]) / ln)
            blocked = sorted((s0, s1) for (ei, _, _, s0, s1, why) in opens if ei == i and (why == "mouth" or not under_stair))
            cur = 0.0
            for s0, s1 in blocked + [(ln, ln)]:
                if s0 - cur > 0.10:
                    runs.append(((a[0] + d[0] * cur, a[1] + d[1] * cur, a[2]), (a[0] + d[0] * s0, a[1] + d[1] * s0, a[2])))
                cur = max(cur, s1)
        return runs

    @staticmethod
    def _ccw_up(ring):
        ax = cross(sub(ring[1], ring[0]), sub(ring[2], ring[1]))
        return ring if ax[2] > 0 else list(reversed(ring))

    def _guard(self, buf, k, i, p0, p1, gh, r, lod, placed):
        """One guard rail run: top and mid rail between posts. Rails butt against the posts' faces and a post shared by
        two runs is built once (`placed`), so no two faces lie in the same plane on top of each other."""
        if lod == 2:
            return
        d = sub(p1, p0)
        hd = norm(d)
        ln = length(d)
        butt = min(0.02, ln / 4.0)
        heights = ((gh, 0.045, "Top"), (gh * 0.5, 0.035, "Mid")) if lod == 0 else ((gh, 0.045, "Top"),)
        for hgt, w, tag in heights:
            a, b = add(p0, (0, 0, hgt)), add(p1, (0, 0, hgt))
            if lod == 0:
                bar(buf, add(a, mul(hd, butt)), add(b, mul(hd, -butt)), UP, w, w, self.steel(r), u_off=r.random() * 0.5)
                nm = self.obs(f"GuardRail_L{k:02d}_{i:02d}_{tag}", a, b, w / 2, "guard", ("landing", k), True)
                if tag == "Top":
                    # the inventory states the rail as built: it stops at the faces of its end posts
                    self.rail_inventory.append({"name": nm, "type": "guardrail", "landing": k, "p0": add(a, mul(hd, butt)), "p1": add(b, mul(hd, -butt)),
                                                "height_above_deck": gh, "butts_into_end_posts": round(butt, 4), "sheet": "USFS T-1006-A"})
            else:
                buf.quad_region(add(a, (0, 0, -w / 2)), add(b, (0, 0, -w / 2)), add(b, (0, 0, w / 2)), add(a, (0, 0, w / 2)), self.steel(r), f=(0.05, 0.0, 0.95, 0.3))
        n = max(1, int(math.ceil(ln / 1.3)))
        for j in range(n + 1):
            q = lerp(p0, p1, j / n)
            if any(length(sub(q, q2)) < 0.001 for q2 in placed):
                continue
            placed.append(q)
            # end posts stand to the top of the rail (the rail butts into them); a post in mid-run stops under the rail
            top = gh + 0.0225 if j in (0, n) else gh - 0.0225
            if lod == 0:
                bar(buf, q, add(q, (0, 0, top)), hd, 0.04, 0.04, self.steel(r), u_off=r.random() * 0.5)
                self.obs(f"GuardPost_L{k:02d}_{i:02d}_{j + 1}", q, add(q, (0, 0, gh)), 0.02, "guard", ("landing", k), True)
            else:
                buf.quad_region(add(q, mul(hd, -0.02)), add(q, mul(hd, 0.02)), add(add(q, mul(hd, 0.02)), (0, 0, gh)), add(add(q, mul(hd, -0.02)), (0, 0, gh)), self.steel(r), f=(0.05, 0.0, 0.4, 0.3))

    # ---- antennas (no post-1987 hardware: roof mast and one Yagi, both bent) ------------------------------------
    def antenna(self, lod):
        L, ap = self.L, self.P["antenna"]
        buf = MeshBuf(f"Antenna_LOD{lod}")
        galv = region("GALV")
        sides = 6 if lod == 0 else 4
        # mast: clamped outside the north-east cab corner, clear of the eave, kinked above the roof
        off = L.roof_half + 0.13          # stood off on two brackets, clear of the eave corner whichever roof is built
        base = (off, off, L.z_sill + 0.15)
        kink = (off, off, L.z_eave + ap["mast_height"] * 0.45)
        b = math.radians(ap["mast_bend_deg"])
        rest = ap["mast_height"] * 0.55
        tip = add(kink, (-math.sin(b) * rest * 0.8, math.sin(b) * rest * 0.6, math.cos(b) * rest))
        rm = 0.022 if lod < 2 else self.min_w / 2
        cylinder(buf, base, kink, rm, rm, sides, galv)
        cylinder(buf, kink, tip, rm, rm * 0.8, sides, galv)
        if lod < 2:
            for z in (L.z_sill + 0.3, L.z_eave - 0.25):      # stand-off brackets to the corner post
                bar(buf, (L.cab_half, L.cab_half, z), (off, off, z), UP, 0.03, 0.03, galv)
            # crossed dipole at the tip, one arm drooping
            ax = norm(sub(tip, kink))
            e1 = perp_to((1, 0, 0), ax)
            e2 = cross(ax, e1)
            top = add(kink, mul(ax, rest * 0.93))
            cylinder(buf, add(top, mul(e1, -1.6)), add(top, mul(e1, 1.6)), 0.009, 0.009, 4, galv)
            cylinder(buf, add(top, mul(e2, -1.6)), top, 0.009, 0.009, 4, galv)
            cylinder(buf, top, add(add(top, mul(e2, 1.35)), (0, 0, -0.7)), 0.009, 0.009, 4, galv)
            # Yagi on a stub off the south-east leg, below the cab; boom drooping
            zy = L.z_steel - 1.3
            a = L.half(zy)
            root = (a - 0.12, -a + 0.02, zy)
            elbow = (a - 0.12, -a - 0.4, zy)
            yb = math.radians(ap["yagi_bend_deg"])
            bd = norm((0.18, -math.cos(yb), -math.sin(yb)))
            end = add(elbow, mul(bd, 1.25))
            cylinder(buf, root, elbow, 0.018, 0.018, sides, galv)
            cylinder(buf, elbow, end, 0.014, 0.014, sides, galv)
            ed = perp_to((0.0, 0.15, 1.0), bd)
            for i, (t, ln) in enumerate(((0.12, 0.52), (0.45, 0.47), (0.75, 0.43), (1.05, 0.40))):
                c = add(elbow, mul(bd, t))
                tilt = perp_to(add(ed, mul(bd, 0.12 * (i - 1.5))), bd)
                cylinder(buf, add(c, mul(tilt, -ln / 2)), add(c, mul(tilt, ln / 2)), 0.006, 0.006, 4, galv)
        return buf
