"""Collision proxies, lanes and the manifest for the MC-39 build. Pure Python (no bpy).

Naming follows tower-td-note.md: COL_BOX_/COL_CVX_ solid, CLIP_BOX_/CLIP_CVX_ for the player-only WorldBounds layer.
Manifest coordinates are Unity's: X = east, Y = up, Z = north (design x, z, y).
"""
import hashlib
import json
import math

import mc39_poly as poly
import mc39_runouts
import mc39_walls

from mc39_atlas import region
from mc39_layout import D, SQ2, U, diag
from mc39_mesh import add, clip_below, cross, dot, hull_bar, hull_box, hull_frustum, hull_prism, length, lerp, mul, norm, sub
from mc39_tower import CORNERS, FACES, UP

WB_HEIGHT = 2.2
SCHEMA = 5          # 5: check_values, inner_mouths, <end>_junction, under_opening, anchors, steel_levels_y (2026-10-11)


def unity(p):
    return [round(p[0], 5), round(p[2], 5), round(p[1], 5)]


def unity_dir(d):
    return [round(d[0], 6), round(d[2] if len(d) > 2 else 0.0, 6), round(d[1], 6)]


class Collision:
    """Walk-envelope proxies, one object per collider (tower-td-note.md sections 2 and 6).

    COL_BOX_ / COL_CVX_ are solid (Default layer); CLIP_BOX_ / CLIP_CVX_ are invisible containment (WorldBounds).
    A BOX is exactly 8 vertices and 12 triangles; a CVX is a convex hull of at most 32 vertices.
    """

    def __init__(self, P, L, tower, cab):
        self.P, self.L, self.T, self.C = P, L, tower, cab

    def colliders(self):
        """List of (name, MeshBuf, pivot). Movers' proxies carry their part's pivot."""
        if getattr(self, "_colliders", None) is not None:
            return self._colliders
        L, T, C = self.L, self.T, self.C
        out = []
        Z = (0.0, 0.0, 0.0)

        def put(buf, pivot=Z):
            out.append((buf.name, buf, pivot))

        for i, (c, hs) in enumerate(T.pad_boxes(), 1):
            put(hull_box(f"COL_BOX_Pad_{i:02d}", c, (hs[0], 0, 0), (0, hs[1], 0), (0, 0, hs[2])))
        # Legs: the convex hull of the visible angle (its two flanges and the triangle between their tips), one hull per
        # leg, tapering in a straight line from the bed plate to the top of the steel and never thinner than the steel
        # at any level (the angle's size steps down panel by panel; the line is the smallest that covers every step).
        s0, s1 = self.leg_hull_sizes()
        for i, c in enumerate(CORNERS, 1):
            rings = []
            for z, sz in ((0.0, s0), (L.z_steel, s1)):
                h = T.corner(c, z)
                rings.append([h, (h[0] - c[0] * sz, h[1], z), (h[0], h[1] - c[1] * sz, z)])
            put(hull_frustum(f"COL_CVX_Leg_{i:02d}", rings[0], rings[1]))
        # the bottom panel's bracing can be reached from the ground
        n = 0
        for fname, c0, c1, nin in FACES:
            ins = 0.05
            z0, z1 = L.steel[0], L.steel[1]
            b0, b1 = T.corner(c0, z0, ins), T.corner(c1, z0, ins)
            t0, t1 = T.corner(c0, z1, ins), T.corner(c1, z1, ins)
            wb, wt = length(sub(b1, b0)), length(sub(t1, t0))
            fx = wb / (wb + wt)
            for p0, p1, wd in ((b0, t1, 0.12), (b1, t0, 0.12), (lerp(b0, t0, fx), lerp(b1, t1, fx), 0.10), (t0, t1, 0.12)):
                n += 1
                put(hull_bar(f"COL_BOX_Brace_{n:02d}", p0, p1, nin, 0.10, wd))

        pt = self.P["landings"]["plank_thickness"]
        for k in range(1, L.n_landings + 1):
            poly = [(q[0], q[1], q[2] - pt) for q in L.landing_polygon(k)]
            put(hull_prism(f"COL_CVX_Landing_{k:02d}", poly, (0, 0, pt)))
            deck_c = (sum(q[0] for q in poly) / len(poly), sum(q[1] for q in poly) / len(poly))
            for i, (p0, p1) in enumerate(T.guard_runs(k, under_stair=True), 1):
                # invisible wall on the rail plane, from the deck to 2.2 m, standing outside the rail
                d = norm(sub(p1, p0))
                mid = lerp(p0, p1, 0.5)
                # outside = away from the deck: on the deck's inner edge (between the stair mouths) that is toward the
                # tower's centre, over the stair, not onto the planks
                outward = (-d[1], d[0], 0.0)
                if dot(outward, (mid[0] - deck_c[0], mid[1] - deck_c[1], 0.0)) < 0:
                    outward = mul(outward, -1.0)
                # thin: its face is on the rail plane toward the deck, its thickness (a table value) behind it
                ht = self.P["checks"]["rail_wall_thickness"] / 2.0
                c = add(add(mid, mul(outward, ht)), (0, 0, WB_HEIGHT / 2))
                put(hull_box(f"CLIP_BOX_Landing{k:02d}_{i:02d}", c, mul(d, length(sub(p1, p0)) / 2), mul(outward, ht), (0, 0, WB_HEIGHT / 2)))

        st = self.P["stairs"]
        for f in L.flights:
            i = f["index"]
            lat = (f["lat"][0], f["lat"][1], 0.0)
            foot, head = f["foot"], f["head"]
            slope = norm(sub(head, foot))
            nrm = norm(cross(slope, lat))
            if nrm[2] < 0:
                nrm = mul(nrm, -1.0)
            # solid wedge: a slab just under the nosing line, so a flight can't be walked through from below or beside
            hw = L.stair_w / 2 + st["stringer_thickness"]
            # it starts and stops stairs.lane_end_clear_min (plan distance) from the lane's ends, so the hand-over circle there is free
            ln = length(sub(head, foot))
            setb = st["lane_end_clear_min"] / math.hypot(slope[0], slope[1])
            c = add(lerp(foot, head, 0.5), mul(nrm, -0.09))
            put(hull_box(f"COL_BOX_Stair_{i:02d}", c, mul(slope, ln / 2 - setb), mul(lat, hw), mul(nrm, 0.09)))
            # side walls on the rail planes, 2.2 m above the nosing line
            rp = L.stair_w / 2 + st["stringer_thickness"] * 0.5
            for j, sl in enumerate((-1, 1), 1):
                o0 = mul(lat, sl * rp)
                ring = [add(foot, o0), add(head, o0), add(add(head, o0), (0, 0, WB_HEIGHT)), add(add(foot, o0), (0, 0, WB_HEIGHT))]
                if abs(head[2] - L.z_floor) < 1e-6:
                    # A flight that ends at the cab floor: its side walls stop at the floor's underside (the top of the
                    # tower steel). Carried 2.2 m above the nosing line they would stand up through the trap door opening
                    # into the cab (tower-td-note, ruling of 2026-10-11; no guard of any kind at the open hatch).
                    ring = clip_below(ring, L.z_steel)
                # thin, face on the rail plane toward the flight, thickness away from it
                put(hull_prism(f"CLIP_CVX_Flight{i:02d}_{j:02d}", ring, mul(lat, sl * self.P["checks"]["rail_wall_thickness"])))

        H, zf, zs, ze = L.cab_half, L.z_floor, L.z_sill, L.z_eave
        h = L.hatch
        ft = self.P["cab"]["floor_thickness"]
        n = 0
        for pa, pb, qa, qb in ((-H, H, -H, h["q0"]), (-H, h["p0"], h["q0"], h["q1"]), (h["p1"], H, h["q0"], h["q1"]), (-H, H, h["q1"], H)):
            (xa, ya), (xb, yb) = L.wall_xy(pa, qa), L.wall_xy(pb, qb)
            x0, x1, y0, y1 = min(xa, xb), max(xa, xb), min(ya, yb), max(ya, yb)
            if x1 - x0 > 0.01 and y1 - y0 > 0.01:
                n += 1
                put(hull_box(f"COL_BOX_CabFloor_{n:02d}", ((x0 + x1) / 2, (y0 + y1) / 2, zf - ft / 2), ((x1 - x0) / 2, 0, 0), (0, (y1 - y0) / 2, 0), (0, 0, ft / 2)))
        inner = H - self.P["cab"]["collision_inset"]           # collision interior 2.03 m square; the visible wall face is 0.03 m outside it
        for i, (sx, sy) in enumerate(((0, 1), (1, 0), (0, -1), (-1, 0)), 1):
            c = (sx * (inner + H) / 2, sy * (inner + H) / 2)
            ex = (abs(sy) * H, abs(sx) * H, 0)
            ey = (abs(sx) * (H - inner) / 2, abs(sy) * (H - inner) / 2, 0)
            put(hull_box(f"COL_BOX_CabWall_{i:02d}", (c[0], c[1], (zf + zs) / 2), ex, ey, (0, 0, (zs - zf) / 2)))
            put(hull_box(f"CLIP_BOX_CabBand_{i:02d}", (c[0], c[1], (zs + ze) / 2), ex, ey, (0, 0, (ze - zs) / 2)))
        put(hull_box("COL_BOX_CabCeiling_01", (0, 0, ze + 0.025), (H, 0, 0), (0, H, 0), (0, 0, 0.025)))

        fp = self.P["firefinder"]
        zt = zf + fp["ring_top"]
        put(hull_box("COL_BOX_Pedestal_01", (0, 0, (zf + zt - 0.06) / 2), (0.105, 0, 0), (0, 0.105, 0), (0, 0, (zt - 0.06 - zf) / 2)))
        ring = [(fp["ring_radius"] * math.cos(2 * math.pi * i / 12), fp["ring_radius"] * math.sin(2 * math.pi * i / 12), zt - 0.06) for i in range(12)]
        put(hull_prism("COL_CVX_FirefinderRing_01", ring, (0, 0, 0.06)))

        th = self.P["hatch"]["thickness"]
        x0, x1, y0, y1 = L.hatch_rect(-0.004)
        put(hull_box("COL_BOX_Hatch_01", ((x0 + x1) / 2, (y0 + y1) / 2, zf - th / 2), ((x1 - x0) / 2, 0, 0), (0, (y1 - y0) / 2, 0), (0, 0, th / 2)), C.hatch_pivot())
        # the volume the leaf sweeps, plus the opening down to 0.1 m below the floor (switched on while the leaf moves)
        x0, x1, y0, y1 = L.hatch_rect()
        sw = h["q1"] - h["q0"]
        lo, hi = zf - ft - 0.05, zf + sw
        put(hull_box("CLIP_BOX_HatchSwing_01", ((x0 + x1) / 2, (y0 + y1) / 2, (lo + hi) / 2), ((x1 - x0) / 2, 0, 0), (0, (y1 - y0) / 2, 0), (0, 0, (hi - lo) / 2)))
        for sdef in C.sash_frames():
            c = add(sdef["pivot"], (0, 0, -sdef["h"] / 2))
            nm = f"COL_BOX_Sash{sdef['side']}{sdef['kind']}_01"
            put(hull_box(nm, c, mul(sdef["right"], sdef["w"] / 2), mul(sdef["out"], 0.02), (0, 0, sdef["h"] / 2)), sdef["pivot"])
        out.sort(key=lambda o: o[0])
        self._colliders = out
        return out

    def leg_hull_sizes(self):
        """(size at the bed plate, size at the top of the steel) of the leg hulls: the straight taper, never less than
        the visible angle's size at any level, with the smallest sizes at the two ends that do that."""
        H = self.L.z_steel
        pts = []
        for z0, z1, sz in self.T.leg_sizes():
            pts += [(z0, sz), (z1, sz)]
        best = None
        for i in range(len(pts)):
            for j in range(i + 1, len(pts)):
                (za, sa), (zb, sb) = pts[i], pts[j]
                if abs(zb - za) < 1e-9:
                    continue
                m = (sb - sa) / (zb - za)
                a0, a1 = sa - m * za, sa + m * (H - za)
                if all(a0 + (a1 - a0) * z / H >= sz - 1e-9 for z, sz in pts) and (best is None or a0 + a1 < best[0] + best[1] - 1e-12):
                    best = (a0, a1)
        if best is None:
            best = (max(sz for _, sz in pts),) * 2
        return best

    def deck_walls(self, z, box, skip=()):
        """[(proxy name, plan footprint)] for a deck at height z: every collision proxy a body standing there can
        touch, found from the proxy list itself. The body occupies heights z + wall_slab_floor_clear (so the deck's own floor
        proxy and a closed trap door are floor, not walls) to z + body_height. `box` is the deck's plan bounding box;
        proxies further from it than a metre cannot matter and are left out. `skip` names proxies that are switched
        off in the state considered."""
        ck = self.P["checks"]
        z0, z1 = z + ck["wall_slab_floor_clear"], z + ck["body_height"]
        out = []
        for name, buf, _ in self.colliders():
            if name in skip:
                continue
            fp = mc39_walls.footprint(buf.verts, z0, z1)
            if fp and mc39_walls.bbox_gap(fp, box) <= 1.0:
                out.append((name, fp))
        return out

    def landing(self, k):
        """The run-out module's view of landing k, with its wall set."""
        if not hasattr(self, "_landings"):
            self._landings = {}
        if k not in self._landings:
            pg = self.L.landing_polygon(k)
            self._landings[k] = mc39_runouts.Landing(self.P, self.L, self.T, k, self.deck_walls(self.L.levels[k], poly.bbox(pg)))
        return self._landings[k]

    def empties(self):
        """(name, design-frame position): axis markers, lane ends and the hatch opening corners."""
        L = self.L
        out = [("MARK_Front", (0.0, 1.0, 0.0)), ("MARK_Up", (0.0, 0.0, 1.0))]
        for f in L.flights:
            out.append((f"LANE_F{f['index']:02d}_Foot", f["foot"]))
            out.append((f"LANE_F{f['index']:02d}_Head", f["head"]))
        x0, x1, y0, y1 = L.hatch_rect()
        for nm, x, y in (("SW", x0, y0), ("SE", x1, y0), ("NE", x1, y1), ("NW", x0, y1)):
            out.append((f"HATCH_Corner_{nm}", (x, y, L.z_floor)))
        fa = self.C.firefinder_axis()
        for text, v in sorted(getattr(self.C, "numerals", {}).items()):
            out.append((f"Ring_Numeral_{text}", (v["design_xy"][0], v["design_xy"][1], fa[2])))
        ap = self.C.alidade_points()
        out.append(("Firefinder_Eye", ap["rear_sight"]))
        out.append(("Firefinder_Pointer", ap["pointer_tip"]))
        for (fi, end), ro in sorted(self.runouts().items()):
            for i, pnt in enumerate(ro.get("path", [])[1:], 1):
                out.append((f"LANE_F{fi:02d}_{end.capitalize()}_R{i}", pnt))
        ex = self.exit_lane()
        if ex:
            out.append((ex["foot_empty"], ex["_design"][0]))
            out.append((ex["head_empty"], ex["_design"][1]))
        lo, hi = self.area_bounds()
        out.append(("AREA_Min", lo))
        out.append(("AREA_Max", hi))
        for a_ in self.anchors():
            out.append((a_["name"], a_["p"]))
        return out

    def anchors(self):
        """Named measurement points for T1 and P1 (agreed with t-qa): name, design-frame position, the work line it is
        on, the sheet, and whether the value is drawn, derived or an interpretation. They come from the same layout
        code as the geometry."""
        L, T, P = self.L, self.T, self.P
        st = P["stairs"]
        names = {(1, 1): "NE", (1, -1): "SE", (-1, -1): "SW", (-1, 1): "NW"}
        out = []

        def put(name, p, on, sheet, basis):
            out.append({"name": "ANCHOR_" + name, "p": (p[0], p[1], p[2]), "on": on, "sheet": sheet, "basis": basis})
        for c in CORNERS:
            put(f"LegHeel_Base_{names[c]}", T.corner(c, 0.0), "leg heel line at the top of the bed plate",
                "height table entry for this tower (20'-0 1/4\" base at 86'-3\"), by straight taper as ruled by team-lead", "derived")
            put(f"LegHeel_Top_{names[c]}", T.corner(c, L.z_steel), "leg heel line at the top of the tower steel", "USFS T-1002-A (7'-0\" square)", "drawn")
        for (cc, hs), c in zip(T.pad_boxes()[:4], CORNERS):
            put(f"Pad_{names[c]}", (cc[0], cc[1], cc[2] + hs[2]), "centre of the leg pad's top face", "IDECO E-4898 (footings); pad size from the parameter table", "interpretation")
            put(f"PadUnder_{names[c]}", (cc[0], cc[1], cc[2] - hs[2]), "centre of the leg pad's underside", "pad depth from the parameter table", "interpretation")
        for k in range(1, L.n_flights + 1):
            z = L.steel[k]
            nm = "Steel_Top" if k == L.n_flights else f"Steel_L{k:02d}"
            put(nm, (0.0, L.half(z) - 0.012, z), "north face girt's heel line, mid span", "USFS T-1002-A, level ladder", "drawn")
        sf, sh = T.pad_boxes()[4]
        put("Deck_Foot", (sf[0], sf[1], sf[2] + sh[2]), "top of the stair-foot landing, centre", "pads._stair_foot in the parameter table", "interpretation")
        for k in range(1, L.n_landings + 1):
            pg = L.landing_polygon(k)
            cx, cy = poly.centroid(pg)
            put(f"Deck_L{k:02d}", (cx, cy, L.levels[k]), "deck top (steel level + deck thickness), mean of the outline's corners", "USFS T-1006-A typical landing; landings._deck", "interpretation")
            put(f"Deck_L{k:02d}_Inner", L.landing_point(k, 0.0, 0.0), "midpoint of the platform's inner edge, on the tower's diagonal",
                "USFS T-1004-A (2'-1 3/8\" and 3'-1\" from the centre line, the rest by the riser chain)", "drawn" if k >= L.n_landings - 1 else "derived")
            fr = L.is_frame_landing(k)
            for tag, t, la in (("A", 0.0, -L.plat_hw), ("B", P["landings"]["platform_depth_in"] * 0.0254, L.plat_hw)):
                put(f"Platform_L{k:02d}_{tag}", L.landing_point(k, t, la),
                    "corner of the platform's drawn 5'-2\" x 2'-6\" rectangle before the tower faces cut it (A on the inner edge, B diagonally opposite); it may lie outside the built deck"
                    + ("; the top landing fills its drawn frame instead, so this rectangle is not its outline" if fr else ""),
                    "USFS T-1004-A", "interpretation" if fr else "drawn")
            runs = T.guard_runs(k)
            p0, p1 = max(runs, key=lambda r_: math.hypot(r_[1][0] - r_[0][0], r_[1][1] - r_[0][1]))
            gh = P["landings"]["guard_height"]
            put(f"Rail_L{k:02d}", ((p0[0] + p1[0]) / 2, (p0[1] + p1[1]) / 2, L.levels[k] + gh), "top guard rail's centre line, middle of the landing's longest run",
                "USFS T-1006-A (3'-6\" approx. above the deck)", "drawn")
        H = L.cab_half
        put("Deck_Cab", (-H / 2, H / 2, L.z_floor), "top of the cab floor boards, north-west quarter point", "USFS T-1001-A note (double 1 in floor); cab._floor", "interpretation")
        for side, o in (("N", (0, 1)), ("E", (1, 0)), ("S", (0, -1)), ("W", (-1, 0))):
            put(f"Sill_{side}", (o[0] * H, o[1] * H, L.z_sill), "sill angle line at mid wall (the sill rail's lower outer edge)", "USFS T-1001-A, 3'-6\" from the top of the tower steel (cab._heights)", "drawn")
            put(f"Head_{side}", (o[0] * H, o[1] * H, L.z_eave), "eave angle line at mid wall (the head rail's upper outer edge)", "USFS T-1001-A, 4'-0\" above the sill", "drawn")
        rh = L.roof_half
        for c in CORNERS:
            put(f"Eave_{names[c]}", (c[0] * rh, c[1] * rh, L.z_eave), "roof eave corner", "USFS T-1001-A (8'-0\" square at the eaves)", "drawn")
        put("Roof_Apex", (0.0, 0.0, L.z_apex), "roof apex, on the tower's axis", "USFS T-1001-A (1/4 pitch)", "derived")
        hw = L.stair_w / 2.0
        sd_ = st["stringer_depth"]
        for f in L.flights:
            lat = (f["lat"][0], f["lat"][1], 0.0)
            foot, head = f["foot"], f["head"]
            slope = norm(sub(head, foot))
            nrm = norm(cross(slope, lat))
            if nrm[2] < 0:
                nrm = mul(nrm, -1.0)
            s0 = add(lerp(foot, head, 0.02), mul(nrm, -(sd_ / 2.0 + 0.01)))
            s1 = add(head, mul(nrm, -(sd_ / 2.0 + 0.01)))
            for sl, tag in ((-1, "L"), (1, "R")):
                o = mul(lat, sl * hw)
                put(f"F{f['index']:02d}_Stringer{tag}_Foot", add(s0, o), "stringer's inner face, on its centre line, at its lower end", "USFS T-1006-A (2'-0\" between stringers)", "drawn")
                put(f"F{f['index']:02d}_Stringer{tag}_Head", add(s1, o), "stringer's inner face, on its centre line, at its upper end", "USFS T-1006-A (2'-0\" between stringers)", "drawn")
        return out

    def area_bounds(self):
        """The climbable volume with 0.5 m of margin: stair-foot pad to the roof apex."""
        L = self.L
        a = L.half(0.0) + 0.5
        return (-a, -a, -0.5), (a, a, L.z_apex + 0.5)

    # ---- corridor and eye-path sweep ---------------------------------------------------------------------------
    def sweep(self, width=0.61, height=1.9, eye=1.65, eye_half=0.05, step=0.01):
        """Every member whose section (not its centre line) enters the corridor over a lane's nosing line.

        Per hit: how far the surface reaches in from the corridor's edge, over what run, the lowest height above the
        nosing, and the member's closest distance to the eye region (eye m above the nosing, across the flight's band).
        Covers frame members, stringers, hand rails, guard rails and their posts; it does not cover treads, planks,
        the cab floor or the landing walk areas.
        """
        L = self.L
        hits = []
        for f in L.flights:
            foot, head = f["foot"], f["head"]
            d = sub(head, foot)
            run = math.hypot(d[0], d[1])
            dh = (d[0] / run, d[1] / run)
            lat = f["lat"]
            slope = d[2] / run
            for ob in self.T.obstacles:
                if ob["owner"] == ("flight", f["index"]):
                    continue
                r = ob["r"]
                n = max(1, int(length(sub(ob["p1"], ob["p0"])) / step))
                rec = None
                eye_min = None
                for i in range(n + 1):
                    q = lerp(ob["p0"], ob["p1"], i / n)
                    rx, ry = q[0] - foot[0], q[1] - foot[1]
                    al = rx * dh[0] + ry * dh[1]
                    la = rx * lat[0] + ry * lat[1]
                    alc = min(max(al, 0.0), run)
                    above = q[2] - (foot[2] + alc * slope)
                    if al < -r or al > run + r:
                        continue
                    intr = width / 2 - (abs(la) - r)
                    if intr <= 0 or above + r < 0.05 or above - r > height:
                        continue
                    lo_l = max(abs(la) - r, 0.0)
                    if rec is None:
                        rec = {"intr": intr, "a0": al - r, "a1": al + r, "low": above - r, "high": above + r, "lat": la, "l0": lo_l,
                               "side": 1 if la >= 0 else -1}
                    else:
                        if intr > rec["intr"]:
                            rec["intr"] = intr; rec["lat"] = la
                        rec["a0"] = min(rec["a0"], al - r); rec["a1"] = max(rec["a1"], al + r)
                        rec["low"] = min(rec["low"], above - r); rec["high"] = max(rec["high"], above + r); rec["l0"] = min(rec["l0"], lo_l)
                if rec:
                    eye_min = self._eye_flight(f, [ob], self.flight_band(f), eye=eye)[0]
                    hits.append({"flight": f["index"], "member": ob["name"], "kind": ob["kind"],
                                 "intrusion_from_corridor_edge": round(rec["intr"], 3), "lateral_at_deepest": round(rec["lat"], 3),
                                 "along_run_from": round(max(rec["a0"], 0.0), 3), "along_run_to": round(min(rec["a1"], run), 3),
                                 "lowest_above_nosing": round(rec["low"], 3), "closest_to_eye_path": round(eye_min, 3),
                                 # box round the intruding part, in the lane's frame (along the run from the foot, lateral
                                 # from the centre line, height above the nosing line); no padding is added
                                 "box": {"along": [round(max(rec["a0"], 0.0), 3), round(min(rec["a1"], run), 3)],
                                         "lateral": sorted([round(rec["side"] * rec["l0"], 3), round(rec["side"] * width / 2, 3)]),
                                         "height": [round(max(rec["low"], 0.05), 3), round(min(rec["high"], height), 3)], "padding": 0.0},
                                 "type": {"rail": "handrail", "guard": "guard rail"}.get(ob["kind"], "frame member")})
        hits.sort(key=lambda h: (h["flight"], h["member"]))
        return hits

    def flight_band(self, f, constant=None, no_opening=False):
        """Lateral band of the lane on flight f as a function of plan run from the foot: (width on the -lateral side,
        width on the +lateral side). At each end it is the lip width there (how far a body wall_reach from every wall
        can stand off the line, per side), easing linearly to checks.lane_lateral_slack over checks.lane_taper_length of slope length.
        `constant` gives the same width everywhere instead."""
        if constant is not None:
            return lambda al: (constant, constant)
        lips = self.flight_lips()
        foot, head = f["foot"], f["head"]
        run = math.hypot(head[0] - foot[0], head[1] - foot[1])
        cosp = run / length(sub(head, foot))
        # left of someone at the foot looking up the flight; at the head, looking down it, left is the other way
        sg = 1.0 if (-f["dir"][1] * f["lat"][0] + f["dir"][0] * f["lat"][1]) > 0 else -1.0
        lf, lh = lips[(f["index"], "foot")], lips[(f["index"], "head")]
        fpos, fneg = (lf["left"], lf["right"]) if sg > 0 else (lf["right"], lf["left"])
        hpos, hneg = (lh["right"], lh["left"]) if sg > 0 else (lh["left"], lh["right"])
        S = self.P["checks"]["lane_lateral_slack"]
        LIP_EASE = self.P["checks"]["lane_taper_length"]
        ops = self.flight_openings().get(f["index"], []) if not no_opening else []

        def band(al):
            d0, d1 = al / cosp, (run - al) / cosp
            if d0 < LIP_EASE and d0 <= d1:
                t = max(d0, 0.0) / LIP_EASE
                w = (fneg + (S - fneg) * t, fpos + (S - fpos) * t)
            elif d1 < LIP_EASE:
                t = max(d1, 0.0) / LIP_EASE
                w = (hneg + (S - hneg) * t, hpos + (S - hpos) * t)
            else:
                w = (S, S)
            for o in ops:
                # under an opening the body can drop through, the band is as wide as the opening lets the body be: the
                # wider of the two samples either side (no fitting position left out), easing to the slack below the
                # first sample over the taper length; within that of the head it still comes down to the head's lip width
                sl = al / cosp
                ss_, wn_, wp_ = o["s"], o["_neg"], o["_pos"]
                if sl < ss_[0]:
                    t = 1.0 - (ss_[0] - sl) / LIP_EASE
                    on, op = (S + (wn_[0] - S) * t, S + (wp_[0] - S) * t) if t > 0 else (0.0, 0.0)
                elif sl > ss_[-1]:
                    on, op = 0.0, 0.0
                else:
                    i = min(int((sl - ss_[0]) / o["_step"] + 1e-9), len(ss_) - 2)
                    on, op = max(wn_[i], wn_[i + 1]), max(wp_[i], wp_[i + 1])
                if d1 < LIP_EASE:
                    t = max(d1, 0.0) / LIP_EASE
                    on, op = min(on, hneg + (max(on, S) - hneg) * t), min(op, hpos + (max(op, S) - hpos) * t)
                w = (max(w[0], on), max(w[1], op))
            return w
        return band

    def flight_openings(self):
        """{flight index: [record]} for each stretch of a flight lane under an opening a body can drop through (today
        the top flight under the trap door). Per side, looking up the flight: how far off the lane's line a body
        wall_reach in radius can be while it comes down through the opening onto the nosing line, limited by the
        opening's edges and by the flight's own rail-plane walls; and the stretch over which the body fits at all."""
        if getattr(self, "_ops", None) is not None:
            return self._ops
        L = self.L
        wr = self.P["checks"]["wall_reach"]
        x0, x1, y0, y1 = L.hatch_rect()
        out = {}
        for f in L.flights:
            if abs(f["head"][2] - L.z_floor) > 1e-6:
                continue          # only a flight that ends at the cab floor passes under its opening
            foot, head = f["foot"], f["head"]
            run = math.hypot(head[0] - foot[0], head[1] - foot[1])
            slen = length(sub(head, foot))
            dh, lat = f["dir"], f["lat"]
            als, las = [], []
            for cx, cy in ((x0, y0), (x1, y0), (x1, y1), (x0, y1)):
                als.append((cx - foot[0]) * dh[0] + (cy - foot[1]) * dh[1])
                las.append((cx - foot[0]) * lat[0] + (cy - foot[1]) * lat[1])
            if abs(abs(dh[0]) + abs(dh[1]) - 1.0) > 1e-9:
                raise ValueError("the flight under the trap door must run square to the opening")
            a0, a1, l0, l1 = min(als), max(als), min(las), max(las)
            if a1 <= 0.0 or a0 >= run or not (l0 < 0.0 < l1):
                continue
            rp = L.stair_w / 2 + self.P["stairs"]["stringer_thickness"] * 0.5          # the CLIP_CVX_Flight walls' plane
            sg = 1.0 if (-dh[1] * lat[0] + dh[0] * lat[1]) > 0 else -1.0                 # is +lateral the left, looking up?
            k_ = slen / run
            # the stretch starts at the lowest point a drop can reach the lane: hatch.max_drop below the cab floor
            s0 = max(slen - self.P["hatch"]["max_drop"] * slen / (head[2] - foot[2]), max(a0, 0.0) * k_)
            s1 = min(a1, run) * k_
            step = 0.01
            ss_ = [round(s0 + step * i, 4) for i in range(int((s1 - s0) / step + 1e-9) + 1)]
            if round(s1, 4) > ss_[-1]:
                ss_.append(round(s1, 4))
            inner = L.cab_half - self.P["cab"]["collision_inset"]
            prof = {"pos": [], "neg": []}
            who = {"pos": [], "neg": []}
            for sv in ss_:
                al = min(sv, slen) / k_
                # the body's centre must be wall_reach inside the opening's two ends, and off the cab's wall proxies
                c = (foot[0] + dh[0] * al, foot[1] + dh[1] * al)
                end_clear = min(al - a0, a1 - al)
                wall_clear = min(inner - abs(c[0]), inner - abs(c[1]))
                for nm, ext, j in (("pos", l1, 2), ("neg", -l0, 1)):
                    by_open, by_wall = ext - wr, rp - wr
                    if end_clear < wr - 1e-9:
                        prof[nm].append(0.0); who[nm].append("the opening's end (cab floor): the body does not fit")
                    elif wall_clear < wr - 1e-9:
                        prof[nm].append(0.0); who[nm].append("a cab wall proxy: the body does not fit")
                    elif by_open <= by_wall:
                        prof[nm].append(round(max(by_open, 0.0), 6)); who[nm].append("the opening's side edge (cab floor)")
                    else:
                        prof[nm].append(round(max(by_wall, 0.0), 6)); who[nm].append(f"CLIP_CVX_Flight{f['index']:02d}_{j:02d}")
            lk, rk = ("pos", "neg") if sg > 0 else ("neg", "pos")

            def runs_(names):
                out_, cur_ = [], None
                for sv, n_ in zip(ss_, names):
                    if cur_ and cur_["bound_by"] == n_:
                        cur_["s_to"] = sv
                    else:
                        cur_ = {"bound_by": n_, "s_from": sv, "s_to": sv}
                        out_.append(cur_)
                return out_
            out.setdefault(f["index"], []).append({
                "opening": "MC39_Hatch", "swing_volume": "CLIP_BOX_HatchSwing_01", "wall_reach": wr, "max_drop": self.P["hatch"]["max_drop"],
                "s": ss_, "left": prof[lk], "right": prof[rk],
                "left_bound_by": runs_(who[lk]), "right_bound_by": runs_(who[rk]),
                "left_dir": unity_dir((-dh[1], dh[0], 0.0)), "sides": "left and right looking up the flight",
                "measured": "s is distance along the lane from its foot (slope length), every 10 mm from the lowest point a drop of max_drop reaches to the lane's head. left / right: how far off the lane's line, square to the flight, the axis of a body wall_reach in radius can be while it comes down through the opening onto the nosing line there; 0 where it does not fit. Raw values.",
                "lane_under_opening": [round(max(a0, 0.0) * k_, 4), round(s1, 4)],
                "opening_alone_allows": {"left": round((l1 if sg > 0 else -l0) - wr, 4), "right": round((-l0 if sg > 0 else l1) - wr, 4)},
                "flight_wall_allows": round(rp - wr, 4),
                "_pos": prof["pos"], "_neg": prof["neg"], "_step": step, "_lk": lk})
        self._ops = out
        return out

    def under_opening_records(self):
        """The manifest's under_opening record per flight: the profile, this build's own flight band at each sample,
        and every sample where the body fits further out than the band reaches."""
        out = {}
        for fi, recs in self.flight_openings().items():
            o = recs[0]
            f = self.L.flights[fi - 1]
            run = math.hypot(f["head"][0] - f["foot"][0], f["head"][1] - f["foot"][1])
            k_ = length(sub(f["head"], f["foot"])) / run
            band = self.flight_band(f)
            bn, bp = zip(*[band(min(sv / k_, run)) for sv in o["s"]])
            bl, br = (bp, bn) if o["_lk"] == "pos" else (bn, bp)
            short = []
            for sv, fl, fr, wl, wr_ in zip(o["s"], o["left"], o["right"], bl, br):
                for nm, fit, w in (("left", fl, wl), ("right", fr, wr_)):
                    if fit > w + 1e-9:
                        short.append({"s": sv, "side": nm, "fits": fit, "band": round(w, 4), "shortfall": round(fit - w, 4)})
            rec = {k: v for k, v in o.items() if not k.startswith("_")}
            rec.update({"band_left": [round(x, 4) for x in bl], "band_right": [round(x, 4) for x in br],
                        "band_note": "this build's evaluation of the flight band at each sample: slack 0.05 m, the wider of the neighbouring profile samples under the opening, easing to the slack over 0.30 m below the first sample, and down to the head's lip width within 0.30 m of the head",
                        "shortfalls": short, "largest_shortfall": max([x["shortfall"] for x in short], default=0.0)})
            out[fi] = rec
        return out

    def _eye_flight(self, f, obs, band, eye=1.65, eye_fn=None, step=0.01):
        """Smallest distance from any of `obs` (segments with a radius) to the eye region of flight f: every point
        eye (or eye_fn(run)) above the nosing line, anywhere across `band`. The eye region is sampled every `step` of
        run and each member every `step` of its length; the nearest eye sample is searched along the whole run, so a
        member above or below the line is measured square to it. Returns (distance, member name, run at the eye)."""
        foot, head = f["foot"], f["head"]
        d = sub(head, foot)
        run = math.hypot(d[0], d[1])
        dh = (d[0] / run, d[1] / run)
        lat = f["lat"]
        slope = d[2] / run
        n = max(1, int(run / step))
        als = [run * j / n for j in range(n + 1)]
        ehs = [eye_fn(a_) if eye_fn else eye for a_ in als]
        zs_ = [foot[2] + a_ * slope + e_ for a_, e_ in zip(als, ehs)]
        ws = [band(a_) for a_ in als]
        wmax = max(max(w) for w in ws)
        dip = max(ehs) - min(ehs)
        z0, z1 = foot[2] + max(ehs), head[2] + max(ehs)
        l2 = run * run + (z1 - z0) ** 2
        coarse = list(range(0, n + 1, 5)) + [n]
        best = (9.9, "", 0.0)
        for ob in obs:
            r = ob["r"]
            m = max(1, int(length(sub(ob["p1"], ob["p0"])) / step))
            for i in range(m + 1):
                q = lerp(ob["p0"], ob["p1"], i / m)
                rx, ry = q[0] - foot[0], q[1] - foot[1]
                al = rx * dh[0] + ry * dh[1]
                la = rx * lat[0] + ry * lat[1]
                # lower bound: square distance to the eye line in its vertical plane, widest band, deepest duck
                t = min(max((al * run + (q[2] - z0) * (z1 - z0)) / l2, 0.0), 1.0)
                d2 = math.hypot(al - run * t, q[2] - (z0 + (z1 - z0) * t))
                if math.hypot(max(abs(la) - wmax, 0.0), max(d2 - dip, 0.0)) - r >= best[0]:
                    continue

                def dist(j):
                    wn, wp = ws[j]
                    le = max(la - wp, -wn - la, 0.0)
                    return math.sqrt((al - als[j]) ** 2 + le * le + (q[2] - zs_[j]) ** 2) - r
                jb = min(coarse, key=dist)
                jb = min(range(max(jb - 5, 0), min(jb + 5, n) + 1), key=dist)
                dd = dist(jb)
                if dd < best[0]:
                    best = (dd, ob["name"], als[jb])
        return best

    def eye_clearance_min(self, eye=1.65, eye_half=None):
        """Smallest distance from any swept member's surface to any lane's eye region on the flights, with the member's
        name. The region is the flight's band (flight_band); eye_half gives a constant half-width instead."""
        best = (9.9, "", 0, 0.0)
        for row in self.eye_per_flight(eye, eye_half):
            if row["_d"] < best[0]:
                best = (row["_d"], row["member"], row["flight"], row["along_run"])
        return {"distance": round(best[0], 3), "member": best[1], "flight": best[2], "along_run": best[3]}

    def eye_per_flight(self, eye=1.65, eye_half=None):
        """The same, one row per flight."""
        out = []
        for f in self.L.flights:
            obs = [o for o in self.T.obstacles if not (o["owner"] == ("flight", f["index"]) and o["kind"] == "stringer")]
            r_ = self._eye_flight(f, obs, self.flight_band(f, eye_half), eye=eye)
            out.append({"flight": f["index"], "distance": round(r_[0], 3), "member": r_[1], "along_run": round(r_[2], 3), "_d": r_[0]})
        return out

    @staticmethod
    def _seg_dist(q, a, b):
        ab = sub(b, a)
        t = min(max(dot(sub(q, a), ab) / max(dot(ab, ab), 1e-12), 0.0), 1.0)
        return length(sub(q, add(a, mul(ab, t))))

    def extra_obstacles(self):
        """The cab floor's edge round the trap door opening and the open leaf, for the top flight's eye path."""
        L = self.L
        x0, x1, y0, y1 = L.hatch_rect()
        ft = self.P["cab"]["floor_thickness"]
        z = L.z_floor - ft / 2
        out = []
        cs = [(x0, y0), (x1, y0), (x1, y1), (x0, y1)]
        for i in range(4):
            a, b = cs[i], cs[(i + 1) % 4]
            out.append({"name": f"CabFloorEdge_{i + 1}", "p0": (a[0], a[1], z), "p1": (b[0], b[1], z), "r": ft / 2, "kind": "floor", "owner": None})
        # the leaf open at its rest angle stands against the hatch wall: its free edge and its middle
        h = L.hatch
        w = h["q1"] - h["q0"]
        ang = math.radians(self.P["hatch"]["open_angle_deg"])
        for frac, nm in ((1.0, "Free"), (0.5, "Mid")):
            q = h["q1"] - math.cos(ang) * w * frac
            zz = L.z_floor + math.sin(ang) * w * frac
            (xa, ya), (xb, yb) = L.wall_xy(h["p0"], q), L.wall_xy(h["p1"], q)
            out.append({"name": f"HatchLeafOpen_{nm}", "p0": (xa, ya, zz), "p1": (xb, yb, zz), "r": self.P["hatch"]["thickness"] / 2, "kind": "hatch", "owner": None})
        return out

    def top_flight_duck(self, eye=1.65, low=1.57):
        """Eye region on the top flight with t-designer's hatch duck: 1.65 m, easing to 1.57 m over 0.6 m of run that ends
        0.20 m before the eye reaches the opening's down-run edge, held to that edge, easing back to 1.65 m at the head;
        across the flight's band. Returns the smallest distance from the eye to any member's surface, floor edge and
        open leaf included."""
        L = self.L
        f = L.flights[-1]
        foot, head = f["foot"], f["head"]
        run = math.hypot(head[0] - foot[0], head[1] - foot[1])
        h = L.hatch
        # along-run position where the eye (at the feet's plan position) passes the opening's down-run edge
        edge_al = run - (h["p1"] - h["p0"])

        def sm(t):
            t = min(max(t, 0.0), 1.0)
            return t * t * (3 - 2 * t)

        def eye_h(al):
            if al <= edge_al - 0.8:
                return eye
            if al <= edge_al - 0.2:
                return eye + (low - eye) * sm((al - (edge_al - 0.8)) / 0.6)
            if al <= edge_al:
                return low
            return low + (eye - low) * sm((al - edge_al) / max(run - edge_al, 1e-6))
        obs = [o for o in self.T.obstacles if not (o["owner"] == ("flight", f["index"]) and o["kind"] == "stringer")] + self.extra_obstacles()
        best = self._eye_flight(f, obs, self.flight_band(f), eye=eye, eye_fn=eye_h)
        return {"distance": round(best[0], 3), "member": best[1], "along_run": round(best[2], 3),
                "opening_edge_along_run": round(edge_al, 3), "eye_low": low}

    def reachable(self, k, reach, grid=0.025, boundary=True):
        """Points of landing k's deck where a capsule centre can stand: at least `reach` from every rail-plane wall.
        A grid, plus (boundary) the edge of that region itself: points exactly `reach` from each wall, every 10 mm along
        it and every 5 degrees round its ends, where a body sliding along the wall rests."""
        L, T = self.L, self.T
        outline = L.landing_polygon(k)
        walls = T.guard_runs(k, under_stair=True)
        x0, y0, x1, y1 = poly.bbox(outline)
        pts = []
        nx, ny = int((x1 - x0) / grid), int((y1 - y0) / grid)
        for i in range(nx + 1):
            for j in range(ny + 1):
                q = (x0 + i * grid, y0 + j * grid)
                if poly.inside(outline, q) and all(poly.seg_dist(q, a, b) >= reach for a, b in walls):
                    pts.append(q)
        if boundary:
            cand = []
            for a, b in walls:
                ln = math.hypot(b[0] - a[0], b[1] - a[1])
                d = ((b[0] - a[0]) / ln, (b[1] - a[1]) / ln)
                m = max(1, int(ln / 0.01))
                for i in range(m + 1):
                    c = (a[0] + d[0] * ln * i / m, a[1] + d[1] * ln * i / m)
                    for sg in (-1.0, 1.0):
                        cand.append((c[0] - d[1] * reach * sg, c[1] + d[0] * reach * sg))
                for c in (a, b):
                    for j in range(72):
                        cand.append((c[0] + reach * math.cos(math.radians(5 * j)), c[1] + reach * math.sin(math.radians(5 * j))))
            for q in cand:
                if poly.inside(outline, q) and all(poly.seg_dist(q, a, b) >= reach - 1e-6 for a, b in walls):
                    pts.append(q)
        return pts

    def landing_eye_clearance(self, eye=1.65, grid=0.025, reach=None, boundary=True):
        """Per landing: over every point a capsule centre can reach (checks.wall_reach from the walls, where a sliding
        body rests), the smallest distance from the eye to any member's surface. Treads and planks are not members here."""
        L, T = self.L, self.T
        reach = self.P["checks"]["wall_reach"] if reach is None else reach
        out = []
        for k in range(1, L.n_landings + 1):
            z = L.levels[k]
            near = [o for o in T.obstacles if min(o["p0"][2], o["p1"][2]) - o["r"] < z + eye + 1.0 and max(o["p0"][2], o["p1"][2]) + o["r"] > z + eye - 1.0]
            best = (9.9, "", None)
            pts = self.reachable(k, reach, grid, boundary)
            for p in pts:
                q = (p[0], p[1], z + eye)
                for o in near:
                    dd = self._seg_dist(q, o["p0"], o["p1"]) - o["r"]
                    if dd < best[0]:
                        best = (dd, o["name"], (p[0], p[1], z))
            row = {"landing": k, "reach": reach, "reachable_samples": len(pts), "distance": round(best[0], 3) if pts else None,
                   "member": best[1], "at": unity(best[2]) if best[2] else None}
            if best[2]:
                walls = T.guard_runs(k, under_stair=True)
                row["at_distance_from_wall"] = round(min(poly.seg_dist(best[2], a, b) for a, b in walls), 4)
                wl = (9.9, "", "")
                for nm, pa, pb in self.landing_lines(k):
                    r_ = self.eye_on_line(pa, pb, near, z, eye)
                    if r_[0] < wl[0]:
                        wl = (r_[0], r_[1], nm)
                row["walking_line_min"], row["walking_line_surface"], row["walking_line_where"] = wl
                bar = self.P["checks"]["eye_clear"]
                row["occurs"] = ("over the bar" if best[0] >= bar else
                                 "also on the ordinary walking line" if wl[0] < bar else "only off the walking line (a body standing or sliding nearer a wall)")
            out.append(row)
        return out

    def landing_lines(self, k):
        """The ordinary walking line on landing k: each lane end to its hand-over point (the built-in run, or the
        linked run-out's segments) and the crossing between the two hand-over points. (name, a, b) in plan."""
        ck = self.P["checks"]
        la = self.landing(k)
        ros = self.runouts()
        out, hand = [], []
        for e in la.ends:
            ro = ros.get((e["flight"], e["end"]))
            if ro and "path" in ro:
                path = [(p_[0], p_[1]) for p_ in ro["path"]]
            else:
                path = [e["q"], (e["q"][0] + e["d"][0] * ck["built_in_run"], e["q"][1] + e["d"][1] * ck["built_in_run"])]
            for i, (pa, pb) in enumerate(zip(path, path[1:]), 1):
                out.append((f"flight {e['flight']} {e['end']} segment {i}", pa, pb))
            hand.append(path[-1])
        out.append(("crossing", hand[0], hand[1]))
        return out

    def junctions(self):
        """{(flight or 'exit', end): record} for every lane end: `lip` compares, per side, the band of the level path's
        first stretch at the lane end with the flight's lip width (they must be equal: every position a body can stand
        on the lip line is inside that band); `bend`, for a linked run-out, is the carry across its bend both ways."""
        lips = self.flight_lips()
        mw = self.mouth_widths()
        ros = self.runouts()
        out = {}
        for key, st in self._stretches().items():
            fi = self.L.n_flights if key[0] == "exit" else key[0]
            fl = lips[(fi, key[1])]
            bands = ros.get(key, {}).get("_bands") if key[0] != "exit" else None
            rec = {"lip": {}, "holds": True}
            for nm in ("left", "right"):
                # a collinear first stretch: its band at the lane end is the last sample of its own profile
                at_lip = bands["segment_1"][nm]["band_at_lane_end"] if bands else mw[key][nm]["band"][-1]
                short = bands["segment_1"][nm]["lip_shortfall"] if bands else max(fl[nm] - at_lip, 0.0)
                tol = self.P["checks"]["band_tolerance"]
                same = short <= tol and abs(at_lip - fl[nm]) <= tol
                rec["lip"][nm] = {"band_at_lane_end": at_lip, "flight_lip": fl[nm], "shortfall": round(short, 6), "equal": same}
                rec["holds"] = rec["holds"] and same
            if key[0] == "exit":
                f = self.L.flights[-1]
                a, b = st["a"], st["b"]
                ln = math.hypot(b[0] - a[0], b[1] - a[1])
                turn = math.degrees(math.acos(max(-1.0, min(1.0, (-f["dir"][0]) * (a[0] - b[0]) / ln + (-f["dir"][1]) * (a[1] - b[1]) / ln))))
                rec["turn_at_lane_end_deg"] = round(turn, 2)
                rec["note"] = "the exit lane turns at the top flight's head; both lip widths there are 0, so nothing is carried across the turn"
            if bands:
                rec["bend"] = {"at": unity((st["path"][1][0], st["path"][1][1], st["z"])), "bend_deg": bands["bend_deg"]}
                for nm in ("left", "right"):
                    s1, s2 = bands["segment_1"][nm], bands["segment_2"][nm]
                    rec["bend"][nm] = {"accept_at_bend": s1["accept_at_bend"], "reach_at_bend_segment_1": s1["reach_at_bend"],
                                       "accept_lands_in_segment_2": s1["accept_lands"],
                                       "segment_2_band_at_bend": s2["band"][-1], "segment_2_carried_into_segment_1": s2["carried_into_segment_1"]}
                    rec["holds"] = rec["holds"] and s2["carried_into_segment_1"]["fits"]
            out[key] = rec
        return out

    def first_segments(self):
        """{(flight, end): the first segment's record} for linked run-outs, in a mouth record's form: s from the lane
        end (0) to the bend (stretch_length), reach, what bounds it and the band per side."""
        ck = self.P["checks"]
        out = {}
        for key, ro in self.runouts().items():
            b = ro.get("_bands")
            if not b:
                continue
            l1 = b["segment_1_left"]
            rec = {"stretch": "linked run-out, first segment (collinear with the flight)", "stretch_length": b["first_segment_length"],
                   "wall_reach": ck["wall_reach"], "sample_spacing": 0.01, "s_origin": "lane_end",
                   "samples_note": "s from the segment's flight-side end, the lane end (0), to its bend (stretch_length); every sample_spacing and a last sample exactly at the bend; strictly increasing",
                   "half_width_cap": ck["join_mouth_default"] / 2.0, "slope_deg": ck["mouth_slope_deg"],
                   "rule": "band(s) = min(half_width_cap, reach(s), accept_at_bend + tan(slope_deg) x (stretch_length - s)), running minimum from the lane end; accept_at_bend is the largest width, no more than reach at the bend, at which a body across this segment lies inside the final segment's band where it stands (behind the bend, the band's sample at the bend; ahead of it, min(cap, the smaller neighbouring reach sample after the running minimum, lip width + tan(slope_deg) x distance to the bend))",
                   "sides": "left and right as seen looking along the segment toward the stair", "left_dir": unity_dir((l1[0], l1[1], 0.0))}
            for nm in ("left", "right"):
                x = b["segment_1"][nm]
                rec[nm] = {"s": x["s"], "reach": x["reach"], "reach_bound_by": x["reach_bound_by"], "band": x["band"], "sample_count": len(x["s"]),
                           "cap_side": ck["join_mouth_default"] / 2.0, "accept_at_bend": x["accept_at_bend"], "accept_lands": x["accept_lands"],
                           "band_at_lane_end": x["band_at_lane_end"], "flight_lip": x["flight_lip"], "lip_shortfall": x["lip_shortfall"]}
            out[key] = rec
        return out

    def envelope_penetration(self):
        """{(flight or 'exit', end): record}: the smallest wall distance of any point of the bands (the final stretch's
        and, for a linked run-out, the first segment's), less the solid radius (wall_reach - band_tolerance).
        Negative would mean a body in the band cuts a wall. Where the lane's own line is nearer a wall than the solid
        radius (the exit lane's foot), the line's own distance is the bar there, and the record says which solid the
        line is against and how near. Bands are taken linearly between their samples; points every 2 mm."""
        ck = self.P["checks"]
        fsegs = self.first_segments()
        out = {}
        for key, st in self._stretches().items():
            rec = self.mouth_widths()[key]
            solid = st["wreach"] - ck["band_tolerance"]
            # (stair-side end, far end, whether s runs from the far end, s, band left, band right)
            parts = [(st["a"], st["b"], True, rec["left"]["s"], rec["left"]["band"], rec["right"]["band"])]
            if key in fsegs:
                fs = fsegs[key]
                parts.append((st["path"][0], st["path"][1], False, fs["left"]["s"], fs["left"]["band"], fs["right"]["band"]))
            worst = (9.9, "", 0.0)
            line = (9.9, "")
            for a, b_, from_far, ss, bl, br in parts:
                ln = math.hypot(b_[0] - a[0], b_[1] - a[1])
                v = ((a[0] - b_[0]) / ln, (a[1] - b_[1]) / ln)          # toward the stair: left is (-v.y, v.x)
                b = b_ if from_far else (a[0] + v[0] * ln, a[1] + v[1] * ln)          # the point s is measured back from, along v
                if not from_far:
                    ss = [ln - x for x in reversed(ss)]
                    bl, br = list(reversed(bl)), list(reversed(br))
                    b = b_
                for i in range(len(ss) - 1):
                    m = max(1, int(round((ss[i + 1] - ss[i]) / 0.002)))
                    for j in range(m + 1):
                        t = j / m
                        s_ = min(ss[i] + (ss[i + 1] - ss[i]) * t, ln)
                        wl, wr_ = bl[i] + (bl[i + 1] - bl[i]) * t, br[i] + (br[i + 1] - br[i]) * t
                        c = (b[0] + v[0] * s_, b[1] + v[1] * s_)
                        c_nm, c_d = min(((nm_, fn(c)) for nm_, fn in st["walls"]), key=lambda x: x[1])
                        if c_d < line[0]:
                            line = (c_d, c_nm)
                        bar = min(solid, c_d)
                        n_ = max(1, int(math.ceil((wl + wr_) / 0.002)))
                        for q_ in range(n_ + 1):
                            o_ = -wr_ + (wl + wr_) * q_ / n_
                            q = (c[0] - v[1] * o_, c[1] + v[0] * o_)
                            nm_, d_ = min(((n2, fn(q)) for n2, fn in st["walls"]), key=lambda x: x[1])
                            if d_ - bar < worst[0]:
                                worst = (d_ - bar, nm_, s_)
            r_ = {"clear_of_solid_radius": round(worst[0], 4), "wall": worst[1], "at_s": round(worst[2], 3), "solid_radius": round(solid, 4),
                  "swept": "the band taken linearly between its samples, which is at least as wide everywhere as the stepped band the game uses (check_values.band_between_samples)",
                  "line_nearest_wall": line[1], "line_nearest_distance": round(line[0], 4)}
            if line[0] < solid:
                r_["note"] = (f"the lane's own line passes {round(line[0], 4)} m from {line[1]}, nearer than the solid radius, so there the bar is the "
                              "line's own distance and the band is zero wide: 0.000 here means the band adds nothing to the line, not a failed margin")
            out[key] = r_
        return out

    def cab_obstacles(self, sash_open=False):
        """Visible things in the cab an eye can come near, as segments with a radius: sash bars, sill and head rails,
        the lower wall's top edge, pedestal, ring, alidade sights, and the hatch leaf open against its wall."""
        L, C, P = self.L, self.C, self.P
        H, zf = L.cab_half, L.z_floor
        out = []

        def add_(name, p0, p1, r):
            out.append({"name": name, "p0": p0, "p1": p1, "r": r, "kind": "cab", "owner": None})
        ang = math.radians(P["cab"]["sash_open_angle_deg"])
        for sd in C.sash_frames():
            P0, right, o = sd["pivot"], sd["right"], sd["out"]
            w, h = sd["w"], sd["h"]
            swing = sash_open and sd["kind"] == "Hinged"

            def pt(x, zdn):
                # a point of the sash plane; an open hinged sash is turned about its top edge, bottom outward
                dz, do = (zdn * math.cos(ang), zdn * math.sin(ang)) if swing else (zdn, 0.0)
                return (P0[0] + right[0] * x + o[0] * do, P0[1] + right[1] * x + o[1] * do, P0[2] - dz)
            fw, mw = 0.035, 0.018
            pw = (w - 2 * fw - 2 * mw) / 3.0
            ph = (h - 2 * fw - 2 * mw) / 3.0
            nm = "Sash_" + sd["side"] + "_" + sd["kind"]
            for zz in (fw / 2, h - fw / 2):
                add_(nm + "_frame", pt(-w / 2, zz), pt(w / 2, zz), 0.0175)
            for x in (-w / 2 + fw / 2, w / 2 - fw / 2):
                add_(nm + "_frame", pt(x, 0), pt(x, h), 0.0175)
            for j in (1, 2):
                x = -w / 2 + fw + j * pw + (j - 0.5) * mw
                add_(nm + "_muntin", pt(x, fw), pt(x, h - fw), 0.009)
                zz = fw + j * ph + (j - 0.5) * mw
                add_(nm + "_muntin", pt(-w / 2 + fw, zz), pt(w / 2 - fw, zz), 0.009)
        for side, o, right in (("N", (0, 1), (-1, 0)), ("E", (1, 0), (0, 1)), ("S", (0, -1), (1, 0)), ("W", (-1, 0), (0, -1))):
            for nm, d, z, r in (("SillRail_", H - 0.035, L.z_sill + 0.012, 0.035), ("HeadRail_", H - 0.035, L.z_eave - 0.02, 0.035),
                                ("LowerWallTop_", H - 0.024, L.z_sill, 0.0)):
                add_(nm + side, (o[0] * d - right[0] * H, o[1] * d - right[1] * H, z), (o[0] * d + right[0] * H, o[1] * d + right[1] * H, z), r)
            add_("Mullion_" + side, (o[0] * (H - 0.03), o[1] * (H - 0.03), L.z_sill), (o[0] * (H - 0.03), o[1] * (H - 0.03), L.z_eave), 0.025)
        fp = P["firefinder"]
        zt = zf + fp["ring_top"]
        add_("Firefinder_Pedestal", (0, 0, zf), (0, 0, zt - 0.06), fp["pedestal_radius"])
        for i in range(24):
            a0, a1 = 2 * math.pi * i / 24, 2 * math.pi * (i + 1) / 24
            R = fp["ring_radius"]
            add_("Firefinder_Ring", (R * math.cos(a0), R * math.sin(a0), zt - 0.015), (R * math.cos(a1), R * math.sin(a1), zt - 0.015), 0.015)
        ap = C.alidade_points()
        for nm in ("rear_sight", "front_sight"):
            q = ap[nm]
            add_("Firefinder_Alidade_" + nm, (q[0], q[1], zt), q, 0.012)
        rh, ze, za = L.roof_half, L.z_eave, L.z_apex
        for cx, cy in ((rh, rh), (-rh, rh), (-rh, -rh), (rh, -rh)):
            add_("Roof_hip", (cx, cy, ze), (0, 0, za), 0.03)
        out += [o_ for o_ in self.extra_obstacles() if o_["kind"] == "hatch"]
        return out

    def cab_points(self, hatch_open, reach=None, boundary=False):
        """Points of the cab floor where a capsule centre can stand: `reach` (default checks.capsule_reach) clear of the
        wall proxies, the ring and, with the hatch open, the opening. A grid; with `boundary`, also the edge of that
        region itself (every 10 mm along the walls' offset lines, every 2 degrees round the ring and the opening's
        corners), where a body sliding along a surface rests."""
        L, P = self.L, self.P
        reach = P["checks"]["capsule_reach"] if reach is None else reach
        inner = L.cab_half - self.P["cab"]["collision_inset"]
        ring = P["firefinder"]["ring_radius"]
        x0, x1, y0, y1 = L.hatch_rect()
        pts = []
        g = 0.025
        n = int(2 * inner / g)
        for i in range(n + 1):
            for j in range(n + 1):
                x, y = -inner + i * g, -inner + j * g
                if abs(x) > inner - reach or abs(y) > inner - reach or math.hypot(x, y) < ring + reach:
                    continue
                if hatch_open and math.hypot(max(x0 - x, 0, x - x1), max(y0 - y, 0, y - y1)) < reach:
                    continue
                pts.append((x, y))
        if boundary:
            e = 1e-6
            cand = []
            w = inner - reach
            m = int(2 * w / 0.01)
            for i in range(m + 1):
                t = -w + 2 * w * i / m
                cand += [(t, w), (t, -w), (w, t), (-w, t)]
            for j in range(180):
                cand.append(((ring + reach) * math.cos(math.radians(2 * j)), (ring + reach) * math.sin(math.radians(2 * j))))
            if hatch_open:
                for i in range(int((x1 - x0) / 0.01) + 1):
                    cand += [(x0 + 0.01 * i, y0 - reach), (x0 + 0.01 * i, y1 + reach)]
                for i in range(int((y1 - y0) / 0.01) + 1):
                    cand += [(x0 - reach, y0 + 0.01 * i), (x1 + reach, y0 + 0.01 * i)]
                for cx, cy in ((x0, y0), (x1, y0), (x1, y1), (x0, y1)):
                    for j in range(180):
                        cand.append((cx + reach * math.cos(math.radians(2 * j)), cy + reach * math.sin(math.radians(2 * j))))
            for x, y in cand:
                if abs(x) > inner - reach + e or abs(y) > inner - reach + e or math.hypot(x, y) < ring + reach - e:
                    continue
                if hatch_open and math.hypot(max(x0 - x, 0, x - x1), max(y0 - y, 0, y - y1)) < reach - e:
                    continue
                pts.append((x, y))
        return pts

    def cab_eye_clearance(self, eye=1.65, reach=None, boundary=True):
        """Smallest eye-to-surface distance over the cab's standing points (checks.wall_reach from the wall proxies, the
        ring and the open hatch, where a sliding body rests), for each state of the hatch and sashes."""
        L = self.L
        reach = self.P["checks"]["wall_reach"] if reach is None else reach
        out = []
        for hatch_open in (False, True):
            for sash_open in (False, True):
                obs = self.cab_obstacles(sash_open)
                if not hatch_open:
                    obs = [o for o in obs if o["kind"] != "hatch"]
                best = (9.9, "", None)
                pts = self.cab_points(hatch_open, reach, boundary)
                for p in pts:
                    q = (p[0], p[1], L.z_floor + eye)
                    for o in obs:
                        dd = self._seg_dist(q, o["p0"], o["p1"]) - o["r"]
                        if dd < best[0]:
                            best = (dd, o["name"], (p[0], p[1], L.z_floor))
                out.append({"hatch": "open" if hatch_open else "closed", "sashes": "hinged open" if sash_open else "closed", "reach": reach,
                            "standing_samples": len(pts), "distance": round(best[0], 3), "surface": best[1], "at": unity(best[2])})
        return out

    def step_off(self, eye=1.65, slack=0.05):
        """The straight line on the cab floor from the top flight's head to the nearest point where a capsule stands
        clear with the hatch open; the eye's smallest distance to a visible surface along it, with lateral slack."""
        L = self.L
        head = L.flights[-1]["head"]
        pts = self.cab_points(True)
        tgt = min(pts, key=lambda p: math.hypot(p[0] - head[0], p[1] - head[1]))
        ln = math.hypot(tgt[0] - head[0], tgt[1] - head[1])
        d = ((tgt[0] - head[0]) / ln, (tgt[1] - head[1]) / ln)
        obs = self.cab_obstacles(False)
        x0, x1, y0, y1 = L.hatch_rect()
        best = (9.9, "", 0.0)
        over = 0.0
        n = int(ln / 0.01)
        for i in range(n + 1):
            sv = ln * i / n
            c = (head[0] + d[0] * sv, head[1] + d[1] * sv)
            if x0 < c[0] < x1 and y0 < c[1] < y1:
                over = sv
            for sl in (-slack, 0.0, slack):
                q = (c[0] - d[1] * sl, c[1] + d[0] * sl, L.z_floor + eye)
                for o in obs:
                    dd = self._seg_dist(q, o["p0"], o["p1"]) - o["r"]
                    if dd < best[0]:
                        best = (dd, o["name"], sv)
        inner = L.cab_half - self.P["cab"]["collision_inset"]
        return {"from": unity(head), "to": unity((tgt[0], tgt[1], L.z_floor)), "length": round(ln, 3),
                "length_over_open_hatch": round(over, 3), "eye_min": round(best[0], 3), "surface": best[1], "at_along": round(best[2], 3),
                "head_to_south_collision_plane": round(inner - abs(head[1 if L.wall == "east" else 0]), 3),
                "head_to_visible_lower_wall_face": round(L.cab_half - 0.024 - abs(head[1 if L.wall == "east" else 0]), 3),
                "visible_face_behind_proxy": 0.028}

    def runouts(self):
        if getattr(self, "_runouts", None) is None:
            lay = self.P.get("run_out_layout")
            if lay:
                # a build never searches: it verifies the stored layout through the search's own rule functions
                self._runouts = mc39_runouts.from_layout(self.P, self.L, self.T, self.landing, lay["landings"])
            elif getattr(self, "search_layout", False):
                self._runouts, self.layout_found = mc39_runouts.select(self.P, self.L, self.T, self.landing)          # the layout tool only
            else:
                raise ValueError("the parameter table has no run_out_layout block: run the layout tool through build_mc39.ps1 first")
        return self._runouts

    def exit_lane(self):
        """The flat lane on the cab floor from the top flight's head to the nearest point where a 0.30 m capsule stands
        clear with the hatch open (tower-td-note 12a). None when the head itself has that room."""
        L, P = self.L, self.P
        reach = P["checks"]["capsule_reach"]
        inner = L.cab_half - self.P["cab"]["collision_inset"]
        head = L.flights[-1]["head"]
        if inner - max(abs(head[0]), abs(head[1])) >= reach:
            return None
        x0, x1, y0, y1 = L.hatch_rect()
        ring = P["firefinder"]["ring_radius"]
        want = reach + P["checks"]["exit_lane_free_end_margin"]

        def clear_(q):
            return min(inner - abs(q[0]), inner - abs(q[1]), math.hypot(q[0], q[1]) - ring,
                       math.hypot(max(x0 - q[0], 0, q[0] - x1), max(y0 - q[1], 0, q[1] - y1)))
        # the nearest standing point that keeps the table's margin, so the free end is not on the region's boundary
        pool = [q for q in self.cab_points(True) if clear_(q) >= want] or self.cab_points(True)
        tgt = min(pool, key=lambda q: math.hypot(q[0] - head[0], q[1] - head[1]))
        ln = math.hypot(tgt[0] - head[0], tgt[1] - head[1])
        n = max(1, int(ln / 0.005))
        overlap, off, ring_min, other, mono, prev = 0.0, 0.0, 9.9, 9.9, True, None
        # the wall beside the stair's top is the one the head is nearest
        ax = 1 if (inner - abs(head[1])) < (inner - abs(head[0])) else 0
        for i in range(n + 1):
            q = (head[0] + (tgt[0] - head[0]) * i / n, head[1] + (tgt[1] - head[1]) * i / n)
            ov = max(reach - (inner - abs(q[ax])), 0.0)
            overlap = max(overlap, ov)
            if prev is not None and ov > prev + 1e-9:
                mono = False
            prev = ov
            other = min(other, inner - abs(q[1 - ax]))
            ring_min = min(ring_min, math.hypot(q[0], q[1]) - ring)
            if x0 < q[0] < x1 and y0 < q[1] < y1:
                off = max(off, min(q[0] - x0, x1 - q[0], q[1] - y0, y1 - q[1]))
        side = {(1, -1): "COL_BOX_CabWall_03", (1, 1): "COL_BOX_CabWall_01", (0, 1): "COL_BOX_CabWall_02", (0, -1): "COL_BOX_CabWall_04"}[(ax, 1 if head[ax] > 0 else -1)]
        return {"index": L.n_flights + 1, "kind": "exit", "foot_empty": f"LANE_F{L.n_flights + 1:02d}_Foot", "head_empty": f"LANE_F{L.n_flights + 1:02d}_Head",
                "foot": unity(head), "head": unity((tgt[0], tgt[1], L.z_floor)), "width": round(L.stair_w, 5), "length": round(ln, 4),
                "wall_proxy": side, "wall_overlap_max": round(overlap, 4), "wall_overlap_never_increases": mono,
                "wall_overlap_at_free_end": round(max(reach - (inner - abs(tgt[ax])), 0.0), 4),
                "other_wall_clear_min": round(other, 4), "ring_clear_min": round(ring_min, 4),
                "feet_from_floor_edge_max": round(off, 4),
                "free_end_clear": {"south_wall_proxy": round(inner - abs(tgt[ax]), 4), "ring": round(math.hypot(tgt[0], tgt[1]) - ring, 4),
                                   "open_hatch": round(math.hypot(max(x0 - tgt[0], 0, tgt[0] - x1), max(y0 - tgt[1], 0, tgt[1] - y1)), 4),
                                   "nearest_wall_proxy": round(min(inner - abs(tgt[0]), inner - abs(tgt[1])), 4), "margin_over_reach": round(clear_(tgt) - reach, 4)},
                "feet_note": "largest horizontal distance from the lane's centre line to the nearest edge of the cab floor while it crosses the open hatch; the top tread is below it there",
                "_design": (head, (tgt[0], tgt[1], L.z_floor))}

    def eye_on_line(self, a, b, obs, z, eye=1.65, slack=0.0):
        """Smallest eye-to-surface distance along the level line a-b (plan points) at floor height z: (distance, name)."""
        ln = math.hypot(b[0] - a[0], b[1] - a[1])
        n = max(1, int(ln / 0.01))
        d = ((b[0] - a[0]) / ln, (b[1] - a[1]) / ln) if ln > 1e-9 else (1.0, 0.0)
        best = (9.9, "")
        for i in range(n + 1):
            c = (a[0] + (b[0] - a[0]) * i / n, a[1] + (b[1] - a[1]) * i / n)
            for sl in ((0.0,) if slack == 0.0 else (-slack, 0.0, slack)):
                q = (c[0] - d[1] * sl, c[1] + d[0] * sl, z + eye)
                for o in obs:
                    dd = self._seg_dist(q, o["p0"], o["p1"]) - o["r"]
                    if dd < best[0]:
                        best = (dd, o["name"])
        return round(best[0], 3), best[1]

    def level_stretches(self):
        """Eye distance on every level stretch: on the line (eye_min), and anywhere across the stretch's join envelope
        (eye_min_across_envelope; mouth_widths' band on each side). The built-in run at each lane end, the linked
        run-outs, the exit lane, and the cab junction at the flight's slack values."""
        L = self.L
        rooms = self.run_room()
        out = []
        for f in L.flights:
            for end, k, sgn in (("foot", f["index"] - 1, -1.0), ("head", f["index"], 1.0)):
                q = f[end]
                ck_ = self.P["checks"]
                room_ = rooms[(f["index"], end)]
                run = ck_["built_in_run"] if room_ >= ck_["capsule_reach"] else min(ck_["capsule_reach"], room_)
                b = (q[0] + f["dir"][0] * sgn * run, q[1] + f["dir"][1] * sgn * run)
                obs = self.cab_obstacles(False) if k > L.n_landings else self.T.obstacles
                dist, name = self.eye_on_line(q, b, obs, q[2])
                row = {"stretch": f"flight {f['index']} {end} run", "length": round(run, 3), "eye_min": dist, "surface": name}
                if self._stretches().get((f["index"], end), {}).get("built_in"):
                    row.update(self.eye_across((f["index"], end)))
                out.append(row)
        for (fi, end), ro in sorted(self.runouts().items()):
            path = ro.get("path", [])
            best = (9.9, "")
            for a_, b_ in zip(path, path[1:]):
                r_ = self.eye_on_line(a_, b_, self.T.obstacles, a_[2])
                if r_[0] < best[0]:
                    best = r_
            if path:
                ln_ = sum(math.hypot(b_[0] - a_[0], b_[1] - a_[1]) for a_, b_ in zip(path, path[1:]))
                row = {"stretch": f"flight {fi} {end} linked run-out", "length": round(ln_, 4), "eye_min": best[0], "surface": best[1]}
                row.update(self.eye_across((fi, end)))
                out.append(row)
        ex = self.exit_lane()
        if ex:
            a, b = ex["_design"]
            obs = self.cab_obstacles(False)
            dist, name = self.eye_on_line(a, b, obs, L.z_floor)
            row = {"stretch": "exit lane, on the line", "length": ex["length"], "eye_min": dist, "surface": name}
            row.update(self.eye_across(("exit", "head")))
            out.append(row)
            fd = L.flights[-1]["dir"]
            for sl in (0.0, 0.02, 0.05):
                dist, name = self.eye_on_line(a, (a[0] + fd[0] * 1e-6, a[1] + fd[1] * 1e-6), obs, L.z_floor, slack=sl)
                out.append({"stretch": f"cab junction at the top flight's head, lateral slack {sl:.2f} across the flight", "length": 0.0, "eye_min": dist, "surface": name})
        return out

    def ground_hand_over(self):
        """Body clearance at the ground hand-over point, checks.built_in_run straight on from flight 1's foot like every
        built-in run: the smallest distance from a vertical 1.8 m body axis there to any frame member, stringer or rail
        surface (compare with capsule_reach, and with ground_wall_reach for the full-size body)."""
        f = self.L.flights[0]
        run = self.P["checks"]["built_in_run"]
        q = (f["foot"][0] - f["dir"][0] * run, f["foot"][1] - f["dir"][1] * run)
        best = (9.9, "")
        for o in self.T.obstacles:
            if min(o["p0"][2], o["p1"][2]) > 1.8:
                continue
            for i in range(19):
                dd = self._seg_dist((q[0], q[1], self.L.levels[0] + 0.1 * i), o["p0"], o["p1"]) - o["r"]
                if dd < best[0]:
                    best = (dd, o["name"])
        return {"point": unity((q[0], q[1], self.L.levels[0])), "nearest_member_surface": round(best[0], 3), "member": best[1]}

    def _stretches(self):
        """{(flight or 'exit', end): the final level stretch of that free end}: a (lip or junction) and b (free end) in
        plan, the walls and stair-mouth edges as (name, distance function), the deck test, the body's wall_reach there,
        and the whole level path from the lane end."""
        if getattr(self, "_st", None) is not None:
            return self._st
        L, P = self.L, self.P
        ck = P["checks"]
        out = {}
        ros = self.runouts()
        for k in range(1, L.n_landings + 1):
            la = self.landing(k)
            walls = la.named_walls          # every proxy a body on this deck can touch
            mouths = [(f"stair mouth {i} of landing {k}", (lambda q, ma=ma, mb=mb: poly.seg_dist(q, ma, mb))) for i, (ma, mb) in enumerate(la.mouths, 1)]
            for e in la.ends:
                ro = ros.get((e["flight"], e["end"]))
                if ro and "path" in ro:
                    path = [(p_[0], p_[1]) for p_ in ro["path"]]
                    kind, built = "linked run-out, final segment", False
                elif ro:
                    continue
                else:
                    a = e["q"]
                    path = [a, (a[0] + e["d"][0] * ck["built_in_run"], a[1] + e["d"][1] * ck["built_in_run"])]
                    kind, built = "built-in straight run", True
                out[(e["flight"], e["end"])] = {"a": path[-2], "b": path[-1], "path": path, "kind": kind, "built_in": built, "walls": walls, "mouths": mouths,
                                                "ok": (lambda q_, la=la: poly.inside(la.outline, q_, 1e-9)), "wreach": ck["wall_reach"],
                                                "z": L.levels[k], "cab": False, "outline": la.outline}
        f = L.flights[0]
        a = (f["foot"][0], f["foot"][1])
        b = (a[0] - f["dir"][0] * ck["built_in_run"], a[1] - f["dir"][1] * ck["built_in_run"])
        gw = self.deck_walls(L.levels[0], (min(a[0], b[0]) - 0.5, min(a[1], b[1]) - 0.5, max(a[0], b[0]) + 0.5, max(a[1], b[1]) + 0.5))
        out[(1, "foot")] = {"a": a, "b": b, "path": [a, b], "kind": "built-in straight run on the ground", "built_in": True,
                            "walls": [(nm, (lambda q, fp=fp: mc39_walls.dist(fp, q))) for nm, fp in gw],
                            "mouths": [], "ok": (lambda q: True), "wreach": ck["wall_reach"], "full_body": ck["ground_wall_reach"], "z": L.levels[0], "cab": False,
                            "outline": None}
        ex = self.exit_lane()
        if ex:
            inner = L.cab_half - self.P["cab"]["collision_inset"]
            ring = P["firefinder"]["ring_radius"]
            a, b = ex["_design"]
            x0, x1, y0, y1 = L.hatch_rect()
            # every proxy a body on the cab floor can touch; the swing blocker is switched off while the leaf is at rest
            H_ = L.cab_half
            cabw = [(nm, (lambda q, fp=fp: mc39_walls.dist(fp, q))) for nm, fp in self.deck_walls(L.z_floor, (-H_, -H_, H_, H_), skip=("CLIP_BOX_HatchSwing_01",))]
            out[("exit", "head")] = {"a": (a[0], a[1]), "b": (b[0], b[1]), "path": [(a[0], a[1]), (b[0], b[1])], "kind": "exit lane", "built_in": False,
                                     "walls": cabw, "mouths": [("the open trap door", lambda q: math.hypot(max(x0 - q[0], 0, q[0] - x1), max(y0 - q[1], 0, q[1] - y1)))],
                                     "ok": (lambda q: True), "wreach": ck["wall_reach"], "z": L.z_floor, "cab": True, "outline": None}
        self._st = out
        return out

    def flight_lips(self):
        """{(flight, end): lip widths}: at each lane end, how far to each side of the lane, square to the flight, a body
        wall_reach from every wall can stand on the deck (capped at half the default mouth). Sides are left and right
        as seen standing off the stair looking along the lane toward it. This is the flight's band at that end."""
        if getattr(self, "_lips", None) is not None:
            return self._lips
        L = self.L
        hd = self.P["checks"]["join_mouth_default"] / 2.0
        sts = self._stretches()
        out = {}
        for f in L.flights:
            for end in ("foot", "head"):
                st = sts.get((f["index"], end)) or (sts.get(("exit", "head")) if (f["index"] == L.n_flights and end == "head") else None)
                q = (f[end][0], f[end][1])
                v = (f["dir"][0], f["dir"][1]) if end == "foot" else (-f["dir"][0], -f["dir"][1])        # toward the stair
                rec = {"left_dir": unity_dir((-v[1], v[0], 0.0)), "measured": "square to the flight, at the lane end"}
                for nm, side in (("left", 1.0), ("right", -1.0)):
                    if st is None:
                        rec[nm], rec[nm + "_bound_by"] = 0.0, "no level stretch at this end"
                    else:
                        rec[nm], rec[nm + "_bound_by"] = mc39_runouts.reach_at(q, (-v[1] * side, v[0] * side), st["walls"], st["ok"], st["wreach"], hd)
                rec["wall_reach"] = st["wreach"] if st else None
                out[(f["index"], end)] = rec
        self._lips = out
        return out

    def eye_across(self, key, eye=1.65):
        """Smallest eye-to-surface distance anywhere across the join envelope of one free end's level path: on the final
        stretch, mouth_widths' band on each side at every 10 mm; on the first segment of a linked run-out, that
        segment's own band. Offsets every 10 mm."""
        st = self._stretches()[key]
        rec = self.mouth_widths()[key]
        z = st["z"]
        obs = self.cab_obstacles(False) if st["cab"] else self.T.obstacles
        mid = ((st["a"][0] + st["b"][0]) / 2, (st["a"][1] + st["b"][1]) / 2, z + eye)
        near = [o for o in obs if self._seg_dist(mid, o["p0"], o["p1"]) - o["r"] < 2.0]
        best = [9.9, "", 0.0, 0.0]

        def scan(c, leftv, wl, wr, s_):
            n_ = max(1, int(math.ceil((wl + wr) / 0.01)))
            for i in range(n_ + 1):
                o_ = -wr + (wl + wr) * i / n_
                q = (c[0] + leftv[0] * o_, c[1] + leftv[1] * o_, z + eye)
                for ob in near:
                    dd = self._seg_dist(q, ob["p0"], ob["p1"]) - ob["r"]
                    if dd < best[0]:
                        best[:] = [dd, ob["name"], s_, o_]
        a, b = st["a"], st["b"]
        ln = math.hypot(b[0] - a[0], b[1] - a[1])
        v = ((a[0] - b[0]) / ln, (a[1] - b[1]) / ln)
        leftv = (-v[1], v[0])
        for s_, bl, br in zip(rec["left"]["s"], rec["left"]["band"], rec["right"]["band"]):
            scan((b[0] + v[0] * s_, b[1] + v[1] * s_), leftv, bl, br, s_)
        path = st["path"]
        if len(path) > 2:
            fs = self.first_segments()[key]
            p0, p1 = path[0], path[1]
            l2 = math.hypot(p1[0] - p0[0], p1[1] - p0[1])
            v2 = ((p0[0] - p1[0]) / l2, (p0[1] - p1[1]) / l2)          # toward the stair; s runs the other way, from the lane end
            for s_, bl, br in zip(fs["left"]["s"], fs["left"]["band"], fs["right"]["band"]):
                s_ = min(s_, l2)
                scan((p0[0] - v2[0] * s_, p0[1] - v2[1] * s_), (-v2[1], v2[0]), bl, br, -1.0)
        return {"eye_min_across_envelope": round(best[0], 3), "surface_across_envelope": best[1],
                "at_s": round(best[2], 3) if best[2] >= 0 else "first segment", "at_offset_left": round(best[3], 3)}

    def mouth_widths(self):
        """{(flight or 'exit', end): record} for every free end: the join envelope on each side of the final level
        stretch (sides named standing at the free end looking along the lane toward the stair).

        reach(s): how far sideways a body wall_reach from every wall can stand on the deck, s from the free end.
        band(s) = min(cap_side, reach(s), lip_width + tan(mouth_slope_deg) x (stretch length - s)), as a running
        minimum from the free end. lip_width is reach at the last sample (the lip or junction): the band is not forced
        to zero there. cap_side is half the default mouth, reduced only where the cap or the slope term (not reach)
        binds at the free end, so that the band's edge there stays capsule_reach from every stair-mouth edge."""
        if getattr(self, "_mw", None) is not None:
            return self._mw
        ck = self.P["checks"]
        hd = ck["join_mouth_default"] / 2.0
        R = ck["capsule_reach"]
        slope = math.tan(math.radians(ck["mouth_slope_deg"]))
        out = {}
        for key, st in self._stretches().items():
            a, b, walls, mouths, ok, wreach = st["a"], st["b"], st["walls"], st["mouths"], st["ok"], st["wreach"]
            ln = math.hypot(b[0] - a[0], b[1] - a[1])
            v = ((a[0] - b[0]) / ln, (a[1] - b[1]) / ln)
            rec = {"stretch": st["kind"], "stretch_length": round(ln, 4), "wall_reach": wreach, "sample_spacing": 0.01, "s_origin": "free_end",
                   "samples_note": "s from the free end (0) to the lip or junction (stretch_length); every sample_spacing, and a last sample at stretch_length where that is not a whole number of spacings; strictly increasing",
                   "half_width_cap": hd, "slope_deg": ck["mouth_slope_deg"],
                   "rule": "band(s) = min(cap_side, reach(s), lip_width + tan(slope_deg) x (stretch_length - s)), running minimum from the free end; lip_width = reach at the last sample; cap_side = half_width_cap, reduced only where the cap or the slope term binds at the free end so the band's edge there stays capsule_reach from every stair-mouth edge",
                   "sides": "left and right as seen standing at the free end looking along the lane toward the stair",
                   "left_dir": unity_dir((-v[1], v[0], 0.0))}
            for nm, side in (("left", 1.0), ("right", -1.0)):
                left = (-v[1] * side, v[0] * side)
                ss, rs, bs = mc39_runouts.reach_profile(a, b, side, walls, ok, wreach, hd)
                lip = rs[-1]
                slope0 = lip + slope * ln

                def mouth_min(w_):
                    best = (9.9, "")
                    for i in range(int(round(w_ / 0.001)) + 1):
                        q = (b[0] + left[0] * 0.001 * i, b[1] + left[1] * 0.001 * i)
                        for mn, fn in mouths:
                            if fn(q) < best[0]:
                                best = (fn(q), mn)
                    return best
                # no cap from the stair mouths: every position in the envelope is one the controller can already stand
                # in before joining (t-td and t-qa, 2026-10-11). cap_side stays as a field for a later rule.
                cap_side, cut_for = hd, ""
                band, run = [], 9.9
                for s_, r_ in zip(ss, rs):
                    run = min(run, cap_side, r_, lip + slope * (ln - s_))
                    band.append(round(max(run, 0.0), 6))
                if rs[0] < min(cap_side, slope0) - 1e-9:
                    first = "reach"
                elif cap_side <= slope0:
                    first = "the cap" if cap_side >= hd else "the cap, reduced for a stair mouth"
                else:
                    first = "the slope term"
                md, mname = mouth_min(band[0]) if mouths else (None, "")
                # the narrower set an arriving player leaves from: offsets up to the flight's lip width, carried to the free end
                fi_ = self.L.n_flights if key[0] == "exit" else key[0]
                ff = self.L.flights[fi_ - 1]
                vf = (ff["dir"][0], ff["dir"][1]) if key[1] == "foot" else (-ff["dir"][0], -ff["dir"][1])
                carried = min(self.flight_lips()[(fi_, key[1])][nm] * max(vf[0] * v[0] + vf[1] * v[1], 0.0), band[0])
                ad = mouth_min(carried)[0] if mouths else None
                # how far inside the deck's edge the centre stays across the free end's cross-section
                inset = None
                if st["outline"]:
                    ol = st["outline"]
                    inset = min(min(poly.seg_dist((b[0] + left[0] * 0.001 * i, b[1] + left[1] * 0.001 * i), ol[j], ol[(j + 1) % len(ol)]) for j in range(len(ol)))
                                for i in range(int(round(band[0] / 0.001)) + 1))
                elif mouths:
                    inset = md          # the cab floor's only edge is the trap door opening
                sp, sp_end = mc39_runouts.reach_span(b, a, side, walls, ok, wreach)
                # where reach is the binding term and the band narrows faster than the slope: a wall forces the body in
                runs, cur = [], None
                for i in range(1, len(ss)):
                    d_s = ss[i] - ss[i - 1]
                    binds = abs(band[i] - rs[i]) < 1e-9 and rs[i] < min(cap_side, lip + slope * (ln - ss[i])) - 1e-9
                    rate = (band[i - 1] - band[i]) / d_s
                    if binds and rate > slope + 1e-9:
                        if cur and cur["wall"] == bs[i] and abs(cur["s_to"] - ss[i - 1]) < 1e-9:
                            cur["s_to"] = ss[i]
                            cur["steepest_narrowing_deg"] = max(cur["steepest_narrowing_deg"], round(math.degrees(math.atan(rate)), 1))
                        else:
                            if cur:
                                runs.append(cur)
                            cur = {"wall": bs[i], "s_from": ss[i - 1], "s_to": ss[i], "_w0": band[i - 1], "steepest_narrowing_deg": round(math.degrees(math.atan(rate)), 1)}
                        cur["sideways"] = round(cur["_w0"] - band[i], 3)
                        cur["length"] = round(cur["s_to"] - cur["s_from"], 4)
                if cur:
                    runs.append(cur)
                for c_ in runs:
                    del c_["_w0"]
                rec[nm] = {"s": ss, "reach": rs, "reach_bound_by": bs, "band": band, "sample_count": len(ss),
                           "lip_width": lip, "cap_side": cap_side, "cap_side_reduced_for": cut_for if cap_side < hd else "",
                           "reach_at_free_end": rs[0], "band_at_free_end": band[0], "band_at_free_end_set_by": first,
                           "band_at_free_end_reason": (f"reach ends at {bs[0]}: a body {wreach} m from it stands no further out" if first == "reach" else ""),
                           "arrival_mouth_distance": round(ad, 4) if ad is not None else None,
                           "arrival_note": "smallest distance to a stair-mouth edge over the offsets an arriving player leaves from: up to the flight's lip width on this side, carried across the turn to the free end",
                           "free_end_mouth_distance": round(md, 4) if md is not None else None, "free_end_nearest_mouth": mname,
                           "free_end_mouth_guard": ck["envelope_mouth_guard"],
                           "free_end_mouth_guard_margin": round(md - ck["envelope_mouth_guard"], 4) if md is not None else None,
                           "free_end_deck_inset": round(inset, 4) if inset is not None else None,
                           "free_end_deck_inset_margin": round(inset - ck["envelope_deck_inset_min"], 4) if inset is not None else None,
                           "reachable_span_uncapped": sp, "reachable_span_ended_by": sp_end,
                           "reachable_span_note": "informational: found in whole millimetres by an older routine; read by no rule, band or check",
                           "reachable_outside_mouth": round(max(sp - band[0], 0.0), 3),
                           "wall_bound_stretches": runs,
                           "wall_bound_note": "only where reach is the band's binding term and the band narrows faster than slope_deg: sideways = how far the wall forces the body in, over length"}
                if "full_body" in st:
                    # where the not-yet-joined, full-size body can stand beside this stretch, and that all of it is in the band
                    _, r40, b40 = mc39_runouts.reach_profile(a, b, side, walls, ok, st["full_body"], hd)
                    over = max(x - y for x, y in zip(r40, band))
                    rec[nm].update({"full_body_wall_reach": st["full_body"], "full_body_reach": r40, "full_body_reach_bound_by": b40,
                                    "full_body_inside_band": over <= 1e-9, "full_body_outside_band_max": round(max(over, 0.0), 4)})
            out[key] = rec
        self._mw = out
        return out

    def run_room(self):
        """Per lane end: how far a capsule centre of radius checks.capsule_reach can be carried straight on from the end,
        away from the flight, before it touches a wall proxy or runs out of deck. Capped at 1.0 m."""
        L, P = self.L, self.P
        reach = P["checks"]["capsule_reach"]
        out = {}
        inner = L.cab_half - self.P["cab"]["collision_inset"]
        ring = P["firefinder"]["ring_radius"]
        for f in L.flights:
            for end, k, sgn in (("foot", f["index"] - 1, -1.0), ("head", f["index"], 1.0)):
                q0 = f[end]
                d = (f["dir"][0] * sgn, f["dir"][1] * sgn)
                room = 0.0
                stop = "open ground"
                if k == 0:
                    room = 1.0
                else:
                    if 1 <= k <= L.n_landings:
                        outline = L.landing_polygon(k)
                        walls = self.T.guard_runs(k, under_stair=True)

                        def ok(p):
                            return poly.inside(outline, p, 1e-6) and all(poly.seg_dist(p, a, b) >= reach - 1e-9 for a, b in walls)
                    else:
                        def ok(p):
                            return abs(p[0]) <= inner - reach and abs(p[1]) <= inner - reach and math.hypot(p[0], p[1]) >= ring + reach
                    sv = 0.0
                    while sv <= 1.0 and ok((q0[0] + d[0] * sv, q0[1] + d[1] * sv)):
                        room = sv
                        sv += 0.005
                    if not ok((q0[0], q0[1])):
                        room = 0.0
                    pe = (q0[0] + d[0] * (room + 0.005), q0[1] + d[1] * (room + 0.005))
                    if room >= 1.0:
                        stop = "none within 1.0 m"
                    elif 1 <= k <= L.n_landings:
                        wi = min(range(len(walls)), key=lambda i_: poly.seg_dist(pe, walls[i_][0], walls[i_][1]))
                        stop = f"CLIP_BOX_Landing{k:02d}_{wi + 1:02d}" if poly.seg_dist(pe, walls[wi][0], walls[wi][1]) < reach else "deck edge"
                    else:
                        stop = "firefinder ring" if math.hypot(pe[0], pe[1]) < ring + reach else "COL_BOX_CabWall"
                out[(f["index"], end)] = round(min(room, 1.0), 3)
                out[(f["index"], end, "stop")] = stop
        return out

    def rail_counts(self):
        """Mid rails and posts per flight and per landing, counted from what was built."""
        fl, ld = {}, {}
        for o in self.T.obstacles:
            nm = o["name"]
            if nm.startswith("HandRail_F") or nm.startswith("HandPost_F"):
                key = int(nm.split("_F")[1][:2])
                e = fl.setdefault(key, {"flight": key, "top_rails": 0, "mid_rails": 0, "posts": 0})
                e["posts" if nm.startswith("HandPost") else ("top_rails" if nm.endswith("Top") else "mid_rails")] += 1
            elif nm.startswith("GuardRail_L") or nm.startswith("GuardPost_L"):
                key = int(nm.split("_L")[1][:2])
                e = ld.setdefault(key, {"landing": key, "top_rails": 0, "mid_rails": 0, "posts": 0})
                e["posts" if nm.startswith("GuardPost") else ("top_rails" if nm.endswith("Top") else "mid_rails")] += 1
        return {"flights": [fl[k] for k in sorted(fl)], "landings": [ld[k] for k in sorted(ld)]}

    def lane_end_clearance(self):
        """Plan distance from each lane end to the nearest landing wall (rail plane) or leg; the hand-over needs 0.30."""
        L, T = self.L, self.T
        out = []

        def seg_dist(q, a, b):
            ax, ay, bx, by = a[0], a[1], b[0], b[1]
            vx, vy = bx - ax, by - ay
            t = min(max(((q[0] - ax) * vx + (q[1] - ay) * vy) / max(vx * vx + vy * vy, 1e-12), 0.0), 1.0)
            return math.hypot(q[0] - ax - vx * t, q[1] - ay - vy * t)
        for f in L.flights:
            for end, k in (("foot", f["index"] - 1), ("head", f["index"])):
                q = f[end]
                if k < 1 or k > L.n_landings:
                    continue
                dmin, what = 9.9, ""
                for i, (p0, p1) in enumerate(T.guard_runs(k, under_stair=True), 1):
                    dd = seg_dist(q, p0, p1)
                    if dd < dmin:
                        dmin, what = dd, f"CLIP_BOX_Landing{k:02d}_{i:02d}"
                out.append({"flight": f["index"], "end": end, "landing": k, "clear": round(dmin, 3), "nearest": what})
        # the two ends that are not on a landing: the ground foot (open ground) and the cab head (collision walls)
        inner = L.cab_half - self.P["cab"]["collision_inset"]
        hd = L.flights[-1]["head"]
        cab = min((inner - abs(hd[0]), "COL_BOX_CabWall (east/west)"), (inner - abs(hd[1]), "COL_BOX_CabWall (north/south)"))
        out.append({"flight": L.n_flights, "end": "head", "landing": "cab", "clear": round(cab[0], 3), "nearest": cab[1]})
        ft = L.flights[0]["foot"]
        a = L.half(0.0) - 0.14
        out.append({"flight": 1, "end": "foot", "landing": "ground", "clear": round(min(a - abs(ft[0]), a - abs(ft[1])), 3), "nearest": "COL_CVX_Leg"})
        return out

    # ---- manifest ----------------------------------------------------------------------------------------------
    def manifest(self, tri_counts, objects):
        L, P, C = self.L, self.P, self.C
        hx0, hx1, hy0, hy1 = L.hatch_rect()
        # the leaf's free edge points away from the hatch wall; AngleAxis(+angle, cross(free, up)) lifts it
        free = unity_dir(L.wall_xy(0.0, -1.0) + (0.0,))
        hinge = [round(v, 6) + 0.0 for v in cross(free, (0.0, 1.0, 0.0))]
        def stands_on(k):
            return "ground" if k == 0 else ("cab" if k > L.n_landings else f"landing_{k}")
        flights = []
        for f in L.flights:
            d = sub(f["head"], f["foot"])
            flights.append({
                "foot_on": stands_on(f["index"] - 1), "head_on": stands_on(f["index"]),
                "foot_empty": f"LANE_F{f['index']:02d}_Foot", "head_empty": f"LANE_F{f['index']:02d}_Head",
                "index": f["index"], "risers": f["risers"], "riser": round(L.riser, 5), "going": round(f["going"], 5),
                "width": round(L.stair_w, 5), "pitch_deg": round(f["pitch_deg"], 3),
                "pitch_status": "as ruled: 12 rise in 8 run on the top flight (USFS T-1006-A; team-lead 2026-10-11, T1 row 56.3 +/- 2 deg)" if f["index"] == L.n_flights and abs(f["going"] - L.going) > 1e-6 else "as ruled: 12 rise in 9 1/4 run (T1 row 52 +/- 2 deg)",
                "foot": unity(f["foot"]), "head": unity(f["head"]), "first_riser": unity(f["first_riser"]),
                "dir": unity_dir((f["dir"][0], f["dir"][1], 0.0)), "lateral": unity_dir((f["lat"][0], f["lat"][1], 0.0)),
                "slope_length": round(length(d), 5),
            })
        landings = []
        for k in range(1, L.n_landings + 1):
            landings.append({"index": k, "y": round(L.levels[k], 5), "corner": "NE" if k % 2 == 1 else "SW",
                             "inner_edge_from_centre_line": round(L.edge[k], 5), "room_to_corner": round(L.depth[k], 5),
                             "shape": "drawn frame, interpretation (t-director ruling/lookout-tower-top-landing-and-step-off)" if L.is_frame_landing(k) else "5'-2\" x 2'-6\" platform cut by the faces (USFS T-1004-A)",
                             "polygon": [unity(p) for p in L.landing_polygon(k)],
                             "open_stretches": [{"edge": i, "from": unity((a[0] + (b[0] - a[0]) * s0 / max(length(sub(b, a)), 1e-9), a[1] + (b[1] - a[1]) * s0 / max(length(sub(b, a)), 1e-9), a[2])),
                                                 "to": unity((a[0] + (b[0] - a[0]) * s1 / max(length(sub(b, a)), 1e-9), a[1] + (b[1] - a[1]) * s1 / max(length(sub(b, a)), 1e-9), a[2])),
                                                 "why": why} for (i, a, b, s0, s1, why) in self.T.edge_openings(k)]})
        sashes = []
        for s in C.sash_frames():
            o = unity_dir(s["out"])
            ax = cross(o, (0.0, 1.0, 0.0))
            info = C.sash_info.get(s["name"], {})
            sashes.append({"name": P["name"].split("_")[0] + "_" + s["name"], "collider": f"COL_BOX_Sash{s['side']}{s['kind']}_01", "side": s["side"],
                           "hinged": s["kind"] == "Hinged", "pivot": unity(s["pivot"]), "outward": o,
                           "hinge_axis": [round(v, 6) for v in ax], "open_angle_deg": P["cab"]["sash_open_angle_deg"],
                           "width": round(s["w"], 5), "height": round(s["h"], 5),
                           "panes": info.get("panes", []), "missing": info.get("missing", 0),
                           "pane_cells": info.get("cells", []),
                           "pane_cells_note": "x0, z0, x1, z1 in metres from the pivot: x along the hinge axis toward the right seen from outside, z down from the hinge"})
        fa = C.firefinder_axis()
        hp = C.hatch_pivot()
        ap = C.alidade_points()
        inner = L.cab_half - self.P["cab"]["collision_inset"]
        hits = self.sweep()
        ack = set(P["stairs"].get("corridor_acknowledged", []))
        exceptions = [h for h in hits if h["kind"] in ("rail", "guard")]
        findings = [h for h in hits if h["kind"] not in ("rail", "guard")]
        rails = []
        for rr in self.T.rail_inventory:
            e = {k: v for k, v in rr.items() if k not in ("p0", "p1")}
            e["start"], e["end"] = unity(rr["p0"]), unity(rr["p1"])
            e["length"] = round(length(sub(rr["p1"], rr["p0"])), 4)
            for k in ("height_above_nosing", "height_above_deck", "stops_short_of_head_run"):
                if k in e:
                    e[k] = round(e[k], 4)
            rails.append(e)
        tl = L.n_landings
        gaps = [(i, a, b, s0, s1) for (i, a, b, s0, s1, why) in self.T.edge_openings(tl) if why == "under top flight"]
        rooms = self.run_room()
        for fe in flights:
            fe["foot_run_room"] = rooms[(fe["index"], "foot")]
            fe["head_run_room"] = rooms[(fe["index"], "head")]
            fe["foot_run_stopped_by"] = rooms[(fe["index"], "foot", "stop")]
            fe["head_run_stopped_by"] = rooms[(fe["index"], "head", "stop")]
        bis = mc39_runouts.built_in(self.P, self.L, self.T, self.landing)
        gh = self.ground_hand_over()
        for fe in flights:
            for end in ("foot", "head"):
                bi = bis.get((fe["index"], end))
                if bi:
                    rec = {k_: v_ for k_, v_ in bi.items() if k_ != "point"}
                    rec["point"] = unity(bi["point"])
                    fe[end + "_hand_over"] = rec
            if fe["index"] == 1:
                fe["foot_hand_over"] = {"kind": "built-in straight run on the ground", "run": self.P["checks"]["built_in_run"],
                                        "full_body_margin": round(gh["nearest_member_surface"] - self.P["checks"]["ground_wall_reach"], 4), "point": gh["point"], "nearest_member_surface": gh["nearest_member_surface"],
                                        "point_wall_margin": round(gh["nearest_member_surface"] - self.P["checks"]["capsule_reach"], 4), "member": gh["member"]}
        ros = self.runouts()
        for fe in flights:
            for end in ("foot", "head"):
                ro = ros.get((fe["index"], end))
                if not ro:
                    continue
                if "fail" in ro:
                    fe[end + "_run_out"] = {"points": [], "empties": [], "fail": ro["fail"], "attempts": ro.get("attempts", []), "status": "provisional"}
                    continue
                rec = {k_: v_ for k_, v_ in ro.items() if k_ != "path" and not k_.startswith("_")}
                rec["inner_mouths"] = [self.first_segments()[(fe["index"], end)]]
                rec["inner_mouths_note"] = "one entry per segment before the last, in order from the flight; the last segment is <end>_mouth"
                rec["points"] = [unity(p_) for p_ in ro["path"][1:]]
                rec["points_flat"] = [c_ for p_ in rec["points"] for c_ in p_]
                rec["empties"] = [f"LANE_F{fe['index']:02d}_{end.capitalize()}_R{i}" for i in range(1, len(ro["path"]))]
                rec["min_wall"] = round(min([self.P["checks"]["capsule_reach"] + ro["point_wall_margin"]] + ro["segment_line_clear"]), 4)
                rec["status"] = "provisional until the lane-end ruling is issued"
                fe[end + "_run_out"] = rec
        mw = self.mouth_widths()
        for fe in flights:
            for end in ("foot", "head"):
                r_ = mw.get((fe["index"], end))
                if r_:
                    # the measured width; default_fits says whether the default mouth fits. No 0-means-default
                    # encoding here: a true zero width exists and must not be read as "default"
                    fe[end + "_mouth"] = r_
        lips = self.flight_lips()
        jn, pen, ops = self.junctions(), self.envelope_penetration(), self.flight_openings()
        for fe in flights:
            for end in ("foot", "head"):
                fe[end + "_lip"] = lips[(fe["index"], end)]
                if (fe["index"], end) in jn:
                    fe[end + "_junction"] = jn[(fe["index"], end)]
                if (fe["index"], end) in pen and fe.get(end + "_mouth"):
                    fe[end + "_mouth"]["no_penetration"] = pen[(fe["index"], end)]
            if fe["index"] in ops:
                fe["under_opening"] = self.under_opening_records()[fe["index"]]
        ex = self.exit_lane()
        exit_lanes = [{k_: v for k_, v in ex.items() if not k_.startswith("_")}] if ex else []
        if exit_lanes and ("exit", "head") in mw:
            r_ = mw[("exit", "head")]
            exit_lanes[0]["head_mouth"] = r_
            r_["no_penetration"] = pen[("exit", "head")]
            if ("exit", "head") in jn:
                exit_lanes[0]["foot_junction"] = jn[("exit", "head")]
        lo, hi = self.area_bounds()
        man = {
            "schema": SCHEMA,
            "reach_bisection": mc39_runouts.REACH_BISECT,
            "reach_bisection_note": "true in every build of record: reach is found to sub-millimetre precision. false only in a comparison run (build_mc39.py --whole-millimetre-reach), which is never copied.",
            "check_values": {k_: v_ for k_, v_ in P["checks"].items() if not k_.startswith("_") and k_ != "known_findings"},
            "check_values_note": "the parameter table's checks block (bars, radii, rule values). `checks` is the list of this build's check results.",
            "anchors": [{"name": a_["name"], "position": unity(a_["p"]), "on": a_["on"], "sheet": a_["sheet"], "basis": a_["basis"]} for a_ in self.anchors()],
            "anchors_note": "named measurement points, each on a work line and never on a cut face; basis is drawn, derived (a chain from drawn values) or interpretation. Agreed with t-qa 2026-10-11. Lane ends are LANE_Fnn_Foot / _Head and the trap door opening's corners HATCH_Corner_*; they are not repeated here.",
            "name": P["name"],
            "frame": "Unity: X east, Y up, Z north (tower north = prefab +Z). Metres. Origin: tower centre on the pad-top plane.",
            "height_standard": P["height_standard"],
            "seed": P["seed"],
            "levels_y": [round(z, 5) for z in L.levels],
            "steel_levels_y": [round(z, 5) for z in L.steel],
            "levels_note": "steel_levels_y are the drawn levels (USFS T-1002-A: top of bed plate, the landing frames, top of the tower steel); girts and frames sit on them. levels_y are the walking surfaces the lanes use: the stair-foot landing, the landing decks and the cab floor, each deck_thickness above its steel level (interpretation, t-director ruling/lookout-tower-girt-and-deck-level; landings._deck, cab._floor and pads._stair_foot in the parameter table).",
            "deck_thickness": round(L.deck_lift, 5),
            "targets": {
                "base_square": round(L.base, 5), "top_square": round(L.top, 5),
                "cab_floor_y": round(L.z_steel, 5), "cab_floor_note": "top of the tower steel (the T1 datum); the walking surface is cab.floor_y", "sill_y": round(L.z_sill, 5), "eave_y": round(L.z_eave, 5),
                "apex_y": round(L.z_apex, 5), "cab_square": round(L.top, 5),
                "lower_wall": round(L.z_sill - L.z_steel, 5), "window_band": round(L.z_eave - L.z_sill, 5),
                "roof_eave_square": round(2 * L.roof_half, 5),
                "roof_pitch_rise_over_run": P["cab"]["roof_pitch_rise"] / P["cab"]["roof_pitch_run"],
                "stair_width": round(L.stair_w, 5), "stair_pitch_deg": round(L.pitch_deg, 3), "stair_pitch_top_flight_deg": round(L.flights[-1]["pitch_deg"], 3),
                "risers_total": sum(L.risers), "risers_per_flight": L.risers,
            },
            "flights": flights,
            "exit_lanes": exit_lanes,
            "landings": landings,
            "hatch": {
                "name": "MC39_Hatch", "collider": "COL_BOX_Hatch_01", "swing_volume": "CLIP_BOX_HatchSwing_01", "swing_blocker": "CLIP_BOX_HatchSwing_01", "pivot": unity(hp),
                "opening_min": unity((hx0, hy0, L.z_floor)), "opening_max": unity((hx1, hy1, L.z_floor)),
                "wall": L.wall, "hinge_axis": hinge, "open_angle_deg": P["hatch"]["open_angle_deg"],
                "thickness": P["hatch"]["thickness"], "leaf_width": round(L.hatch["q1"] - L.hatch["q0"], 5),
                "corner_empties": ["HATCH_Corner_SW", "HATCH_Corner_SE", "HATCH_Corner_NE", "HATCH_Corner_NW"],
                "note": "Quaternion.AngleAxis(+open_angle_deg, hinge_axis) lifts the free edge up against the hatch wall.",
            },
            "sashes": sashes,
            "sash_note": "Quaternion.AngleAxis(+open_angle_deg, hinge_axis) swings the bottom rail outward.",
            "firefinder": {
                "pedestal": "MC39_Firefinder_Pedestal", "ring": "MC39_Firefinder_Ring", "alidade": "MC39_Firefinder_Alidade",
                "colliders": ["COL_BOX_Pedestal_01", "COL_CVX_FirefinderRing_01"], "axis": unity(fa), "ring_radius": P["firefinder"]["ring_radius"],
                "ring_top_above_floor": P["firefinder"]["ring_top"],
                "alidade_rest_bearing_deg": 0.0,
                "rear_sight_rest": unity(ap["rear_sight"]), "front_sight_rest": unity(ap["front_sight"]), "pointer_tip_rest": unity(ap["pointer_tip"]),
                "eye_empty": "Firefinder_Eye", "pointer_empty": "Firefinder_Pointer", "numeral_empty_prefix": "Ring_Numeral_",
                "bearing": "0 = +Z (north), 90 = +X (east); the alidade turns by Quaternion.AngleAxis(bearing, up).",
                "numerals": {k: {"bearing_deg": v["bearing_deg"], "position": unity((v["design_xy"][0], v["design_xy"][1], fa[2]))}
                             for k, v in sorted(getattr(C, "numerals", {}).items())},
            },
            "cab": {"collision_interior_square": round(2 * inner, 5), "collision_inset": P["cab"]["collision_inset"], "floor_y": round(L.z_floor, 5), "floor_thickness": P["cab"]["floor_thickness"],
                    "sill_above_floor": round(L.z_sill - L.z_floor, 5), "eave_above_floor": round(L.z_eave - L.z_floor, 5),
                    "floor_note": "floor_y is the walking surface (top of the boards); sill and eaves are measured from the top of the tower steel (targets), so the sill stands sill_above_floor over the boards",
                    "walk_rect_min": unity((-inner, -inner, L.z_floor)), "walk_rect_max": unity((inner, inner, L.z_floor)),
                    "panes_total": 72, "panes_missing": sum(s["missing"] for s in sashes)},
            "pads": [{"centre": unity((c[0], c[1], c[2] + hs[2])), "centre_note": "centre of the pad's top face", "half_size": [round(hs[0], 4), round(hs[2] * 2, 4), round(hs[1], 4)]} for c, hs in self.T.pad_boxes()],
            "lod": {"screen_heights": P["lod"]["screen_heights"], "tris": tri_counts},
            "objects": objects,
            "materials": ["MC39_Opaque", "MC39_Cutout", "MC39_Glass", "MC39_Impostor"],
            "textures": {"albedo": P["name"] + "_Albedo.png", "normal": P["name"] + "_Normal.png", "mask": P["name"] + "_Mask.png",
                         "mask_channels": "R metallic, G occlusion, A smoothness", "size": P["budget"]["atlas_px"]},
            "area_min": unity(lo), "area_max": unity(hi),
            "rails": rails,
            "rail_counts": self.rail_counts(),
            "omissions": [
                {"id": "OM1", "what": "Flight %d handrails, both sides, stop below the cab floor" % L.n_flights,
                 "stops_short_of_head_run": round(next(r["stops_short_of_head_run"] for r in self.T.rail_inventory if r.get("flight") == L.n_flights), 4),
                 "rule": "rail top within stairs.top_flight_rail_floor_gap of the cab floor underside",
                 "reason": "carried on, the rails would pass through the cab floor and the closed trap door",
                 "approved": "rule approved by t-director (gate/lookout-tower-g1 revision 3, G2); to be re-confirmed after the 2026-10-11 layout change"},
                {"id": "OM2", "what": "Landing %d inner-edge guard rail under the top flight" % tl,
                 "stretches": [{"edge": i, "length": round(s1 - s0, 3)} for (i, a, b, s0, s1) in gaps],
                 "rule": "no rail where the top flight's underside is below guard height + 0.05 m (empty when the deck has no edge under the top flight)",
                 "reason": "the stair itself closes that edge", "approved": "rule approved by t-director (gate/lookout-tower-g1 revision 3, G2); to be re-confirmed after the 2026-10-11 layout change"},
            ],
            "corridor": {"width": 0.61, "height": 1.9, "eye_height": 1.65,
                         "scope": "frame members, stringers, hand rails, guard rails and posts against each lane; not treads, planks, cab floor or landing walk areas",
                         "exceptions": [dict(h, sheet="USFS T-1006-A") for h in exceptions],
                         "findings": [dict(h, acknowledged=h["member"] in ack) for h in findings],
                         "flight_band": {"slack": P["checks"]["lane_lateral_slack"], "ease_length": P["checks"]["lane_taper_length"],
                                         "rule": "on a flight the lane's band is, per side, the lip width at each end (flights[].foot_lip / head_lip) easing linearly to slack over ease_length of slope length"},
                         "proxy_to_visible": {
                             "landings": [{"landing": k, "rail_inside_proxy": 0.0225, "post_inside_proxy": 0.02} for k in range(1, L.n_landings + 1)],
                             "landings_note": "each CLIP_BOX_Landing wall's inner face is on the rail plane; the visible 0.045 m square guard rail is centred on that plane, so its inner face stands 0.0225 m inside the proxy (posts 0.020 m). A body wall_reach from the proxy has that much less to the rail itself. The eye distances here are measured to the visible members, not to the proxies.",
                             "cab_wall_face_outside_proxy": 0.028},
                         "eye_method": "flights: the eye region is 1.65 m above the nosing line across the flight's band; each member is measured to the nearest eye point anywhere along the run (square to the line). Landings and cab: every position checks.wall_reach from the walls, the edge of that region included.",
                         "eye_correction": "landing and cab sweeps before this export sampled a 25 mm grid only and missed the edge of the reachable region; with the edge included, at the old 0.30 m reach, landing 7 read 0.175 m where 0.189 m was reported, landings 1-2 11-13 mm less, landings 3, 5, 6 1-4 mm less. Every landing figure in earlier manifests was up to 14 mm optimistic (t-artist, self-reported 2026-10-11).",
                         "withdrawn_rule": "an earlier form of the join rule reduced the per-side cap and failed the build where the band's edge at a free end came within 0.26 m of a stair mouth. It was withdrawn by t-td and t-qa before any result of record. Under it: landing 5 (flight 5 head left / flight 6 foot right) 0.2433 m, landing 6 (flight 6 head left / flight 7 foot right) 0.2521 m, and flight 7 head's open side cut from 0.305 m to 0.021 m (figures on the pre-deck-datum scratch build). Replaced by stated figures and two guards at the free end's cross-section: checks.envelope_mouth_guard and checks.envelope_deck_inset_min.",
                         "eye_clearance_min": self.eye_clearance_min(),
                         "eye_clearance_per_flight": [{k_: v_ for k_, v_ in r_.items() if k_ != "_d"} for r_ in self.eye_per_flight()],
                         "top_flight_with_hatch_duck": self.top_flight_duck(),
                         "top_flight_standing": self.top_flight_duck(low=1.65),
                         "cab_eye_clearance": self.cab_eye_clearance(),
                         "level_stretches": self.level_stretches(),
                         "ground_hand_over": self.ground_hand_over(),
                         "eye_clearance_min_slack_0.02": self.eye_clearance_min(eye_half=0.02),
                         "step_off": self.step_off(),
                         "landing_eye_clearance": self.landing_eye_clearance(),
                         "lane_end_clearance": self.lane_end_clearance(),
                         "lane_end_clear_min_required": P["stairs"]["lane_end_clear_min"]},
            "landing_solution": {
                "rule": "USFS T-1004-A: top landing edge 2'-1 3/8\" and the next 3'-1\" from the tower centre line; a flight is n goings between landing edges, so edge(k-1) = risers(k) x going - edge(k)",
                "edges_from_centre_line": [round(v, 5) for v in L.edge],
                "going": round(L.going, 6), "risers": L.risers,
            },
        }
        man["lane_data_digest"], man["lane_data_digest_of"] = self.lane_data_digest(man)
        man["lane_data_digest_note"] = ("SHA-256 over a canonical text of everything the climb depends on (keys sorted, lists in order, numbers "
                                        "to six decimals): the manifest blocks and the collider and empty sets named in lane_data_digest_of. "
                                        "Nothing visual, no anchors, no check results. An unchanged digest means no lane data moved.")
        return man

    def lane_data_digest(self, man):
        """(hex digest, what went into it). Every value the climb depends on, in a canonical text: the manifest's lane
        blocks as written, every collision proxy (name, kind from its prefix, pivot, vertices) and every lane, run-out
        and hatch empty (name, position). Visual counts, anchors and check results are not in it."""
        # Keys whose value is prose (a sentence for a reader) are left out of both digests: rewording one is not a change of
        # data. Strings that are data (names, kinds, enumerated values such as turn, arrival, s_origin, reach_bound_by) stay in.
        prose = ("rule", "measured", "sides", "swept", "pitch_status", "band_at_free_end_reason", "reason", "status", "bearing")

        def is_prose(k):
            return k == "note" or k.endswith("_note") or k in prose

        def canon(v):
            if isinstance(v, dict):
                return "{" + ",".join(f"{json.dumps(str(k))}:{canon(v[k])}" for k in sorted(v, key=str) if not is_prose(str(k))) + "}"
            if isinstance(v, (list, tuple)):
                return "[" + ",".join(canon(x) for x in v) + "]"
            if isinstance(v, bool) or v is None or isinstance(v, str):
                return json.dumps(v)
            if isinstance(v, int):
                return str(v)
            x = round(float(v), 6)
            return "%.6f" % (0.0 if x == 0 else x)
        blocks = ["schema", "levels_y", "steel_levels_y", "deck_thickness", "reach_bisection",
                  "flights", "exit_lanes", "landings", "hatch", "area_min", "area_max"]
        data = {k: man[k] for k in blocks}
        # Table values a lane, band or junction figure is computed from, or that the game's reader asserts for the climb.
        # Bars that only judge a result (eye clearance, run room) are named as left out: changing one of those is not a
        # change of lane data (t-td, 2026-10-11). A table value named in neither list stops the build.
        cv_in = ["capsule_reach", "wall_reach", "ground_wall_reach", "built_in_run", "join_mouth_default", "mouth_slope_deg",
                 "band_tolerance", "band_between_samples", "envelope_mouth_guard", "envelope_deck_inset_min",
                 "run_out_first_segment_min", "run_out_final_segment_min", "run_out_margin_target", "carry_bend_max_toward",
                 "carry_bend_max_away", "stop_bend_min", "stop_bend_min_lever", "frozen_behaviour", "stop_fallback_allowed",
                 "shortest_first_segment", "first_segment_curve", "tower_walk_speed", "exit_lane_free_end_margin",
                 "bend_limit_guard_deg", "lane_lateral_slack", "lane_taper_length", "exit_lane_wall_overlap_max",
                 "body_height", "wall_slab_floor_clear", "rail_wall_thickness", "collinear_tolerance_deg"]
        cv_out = ["eye_clear", "run_room_min", "run_out_search_time_limit_s"]
        unsorted_ = sorted(k for k in man["check_values"] if k not in cv_in and k not in cv_out) + sorted(k for k in cv_in + cv_out if k not in man["check_values"])
        if unsorted_:
            raise ValueError(f"check values not sorted into the lane-data digest's two lists, or named there and missing from the table: {unsorted_}")
        data["check_values"] = {k: man["check_values"][k] for k in cv_in}
        cab_keys = ["collision_interior_square", "collision_inset", "floor_y", "floor_thickness", "walk_rect_min", "walk_rect_max"]
        data["cab_walk"] = {k: man["cab"][k] for k in cab_keys}
        data["colliders"] = [{"name": n, "kind": n.split("_")[0] + "_" + n.split("_")[1], "pivot": unity(pv),
                              "vertices": sorted(unity(v) for v in buf.verts)} for n, buf, pv in self.colliders()]
        keep = ("LANE_", "HATCH_")
        data["empties"] = [{"name": n, "position": unity(p_)} for n, p_ in sorted(self.empties()) if n.startswith(keep)]
        of = {"manifest_blocks": blocks, "check_values_in": cv_in, "check_values_out": cv_out, "cab": cab_keys,
              "prose_keys_dropped": ["note", "*_note"] + list(prose),
              "colliders": "every COL_ and CLIP_ proxy: name, kind (its prefix), pivot, every vertex (sorted); proxies carry no rotation or scale of their own",
              "empties": "every empty named LANE_* or HATCH_*: name and position",
              "not_in": "visual meshes and counts, materials, textures, rails, pads, sashes and firefinder (see interaction_data_digest), corridor, targets, anchors and ANCHOR_ empties, checks, known_findings, build_of_record_for_listed_checks"}
        # The hatch, sashes and firefinder as the game's wiring reads them: a separate digest, so a change there reruns
        # the interaction checks without counting as a change of lane data. The hatch block is in both.
        inter_blocks = ["hatch", "sashes", "firefinder"]
        inter = {k: man[k] for k in inter_blocks}
        inter["cab"] = {k: man["cab"][k] for k in ("floor_y", "floor_thickness")}
        # What the game's setup takes from the FBX, by the manifest's own names (t-engineer's list): each moving part's
        # node position and rest orientation, each named proxy's geometry, and the numeral empties.
        C = self.C
        movers = [{"name": man["hatch"]["name"], "position": unity(C.hatch_pivot())}] + [{"name": s_["name"], "position": s_["pivot"]} for s_ in man["sashes"]]
        movers += [{"name": man["firefinder"]["alidade"], "position": unity(C.firefinder_axis())},
                   {"name": man["firefinder"]["pedestal"], "position": unity((0.0, 0.0, 0.0))},
                   {"name": man["firefinder"]["ring"], "position": unity((0.0, 0.0, 0.0))}]
        for m_ in movers:
            m_["rotation"] = "identity"          # every node is exported unrotated and unscaled; its rest pose is in its mesh
            m_["scale"] = 1.0
        # the visible leaf of each part that swings, as its mesh's bounding box in the node's own frame, so a leaf
        # reshaped about an unchanged pivot (and so out of step with its proxy) moves the digest
        prefix_ = self.P["name"].split("_")[0] + "_"
        swing = [(man["hatch"]["name"], C.hatch(), C.hatch_pivot())]
        swing += [(prefix_ + s_["name"], C.sash(s_), s_["pivot"]) for s_ in C.sash_frames() if s_["kind"] == "Hinged"]
        swing.append((man["firefinder"]["alidade"], C.alidade(), C.firefinder_axis()))
        boxes = {}
        for nm_, buf_, pv_ in swing:
            loc = [unity(sub(v, pv_)) for v in buf_.verts]
            boxes[nm_] = {"min": [min(q[i] for q in loc) for i in range(3)], "max": [max(q[i] for q in loc) for i in range(3)]}
        for m_ in movers:
            if m_["name"] in boxes:
                m_["mesh_bounds_min"], m_["mesh_bounds_max"] = boxes[m_["name"]]["min"], boxes[m_["name"]]["max"]
        if set(boxes) - {m_["name"] for m_ in movers}:
            raise ValueError(f"swinging parts not among the manifest's nodes: {sorted(set(boxes) - {m_['name'] for m_ in movers})}")
        inter["nodes"] = movers
        named = {man["hatch"]["collider"], man["hatch"]["swing_volume"]} | {s_["collider"] for s_ in man["sashes"]} | set(man["firefinder"]["colliders"])
        cols = {c_["name"]: c_ for c_ in data["colliders"]}
        missing = sorted(n for n in named if n not in cols)
        if missing:
            raise ValueError(f"colliders named in the hatch, sash or firefinder blocks are not built: {missing}")
        inter["colliders"] = [cols[n] for n in sorted(named)]
        prefix = man["firefinder"]["numeral_empty_prefix"]
        inter["numeral_empties"] = [{"name": n, "position": unity(p_)} for n, p_ in sorted(self.empties()) if n.startswith(prefix)]
        man["interaction_data_digest"] = hashlib.sha256(canon(inter).encode("utf-8")).hexdigest()
        man["interaction_data_digest_of"] = {
            "manifest_blocks": inter_blocks, "cab": ["floor_y", "floor_thickness"],
            "nodes": [m_["name"] for m_ in movers],
            "nodes_fields": "position (the node's origin: the hinge pivot of the hatch and of each sash, the alidade's axis point), rotation (identity) and scale (1) as exported",
            "mesh_bounds_of": sorted(boxes), "mesh_bounds_fields": "mesh_bounds_min / mesh_bounds_max: the visible mesh's bounding box in the node's own frame, for the parts that swing",
            "colliders": sorted(named), "colliders_fields": "name, kind (its prefix), pivot, every vertex (sorted)",
            "numeral_empties": prefix + "* : name and position (%d of them)" % len(inter["numeral_empties"]),
            "prose_keys_dropped": ["note", "*_note"] + list(prose),
            "not_in": "the moving parts' visual meshes: a mesh reshaped about an unchanged node does not move this digest",
            "note": "same canonical form as lane_data_digest. The hatch block, the cab floor figures and the named colliders are also in the lane digest."}
        return hashlib.sha256(canon(data).encode("utf-8")).hexdigest(), of
