"""Cab, roof, sashes, trap door and firefinder for the MC-39 build. Pure Python (no bpy).

Moving parts (hatch leaf, the eight sashes, the alidade) are returned as their own MeshBuf with a pivot, in the
design frame (x = east, y = north, z = up). Digit outlines for the bearing ring are passed in by the build script.
"""
import math

from mc39_atlas import N_WOOD, region
from mc39_layout import IN
from mc39_mesh import (MAT_CUTOUT, MAT_GLASS, MAT_OPAQUE, MeshBuf, add, bar, box, cross, cylinder, lerp, mul, norm,
                       prism, sub)

UP = (0.0, 0.0, 1.0)
# side, outward normal, 'right' along the wall as seen from OUTSIDE
SIDES = [
    ("N", (0.0, 1.0, 0.0), (-1.0, 0.0, 0.0)),
    ("E", (1.0, 0.0, 0.0), (0.0, 1.0, 0.0)),
    ("S", (0.0, -1.0, 0.0), (1.0, 0.0, 0.0)),
    ("W", (-1.0, 0.0, 0.0), (0.0, -1.0, 0.0)),
]


class Cab:
    def __init__(self, P, L, tower):
        self.P, self.L, self.T = P, L, tower
        self.cp = P["cab"]
        self.sash_info = {}
        self.pane_plan = self._plan_panes()

    # ---- which panes are gone ----------------------------------------------------------------------------------
    def _plan_panes(self):
        """Exactly round(fraction * 72) panes missing, spread unevenly: each sash draws its own weight."""
        r = self.T.rng("panes")
        keys, weights = [], []
        for side, _, _ in SIDES:
            for kind in ("Hinged", "Fixed"):
                sw = 0.25 + 2.2 * r.random() ** 1.5
                for i in range(9):
                    keys.append((side, kind, i))
                    weights.append(sw * (0.5 + r.random()))
        n_missing = int(round(self.cp["pane_missing_fraction"] * len(keys)))
        state = {k: "glass" for k in keys}
        pool = list(range(len(keys)))
        for _ in range(n_missing):
            tot = sum(weights[i] for i in pool)
            x = r.random() * tot
            for j, i in enumerate(pool):
                x -= weights[i]
                if x <= 0:
                    break
            state[keys[pool.pop(j)]] = "missing"
        left = [k for k in keys if state[k] == "glass"]
        for k in left:
            if r.random() < self.cp["pane_cracked_fraction"]:
                state[k] = "cracked"
        return state

    # ---- static cab shell --------------------------------------------------------------------------------------
    def shell(self, lod):
        L, cp, T = self.L, self.cp, self.T
        buf = MeshBuf(f"Cab_LOD{lod}")
        r = T.rng("cab")
        H = L.cab_half
        zf, zs, ze = L.z_steel, L.z_sill, L.z_eave          # the walls stand on the tower steel; the floor lies inside them
        wt = cp["wall_thickness"]
        for i, (side, out, right) in enumerate(SIDES):
            o = mul(out, H - 0.004)
            inn = mul(out, H - 0.004 - wt)
            a, b = add(o, mul(right, -H)), add(o, mul(right, H))
            pr = region(f"PANEL{i % 2}")
            # outside face (seen from outside, left to right) and inside face
            buf.quad_region(add(a, (0, 0, zf)), add(b, (0, 0, zf)), add(b, (0, 0, zs)), add(a, (0, 0, zs)), pr,
                            f=((0.0, 0.0, 1.0, 1.0) if i < 2 else (1.0, 0.0, 0.0, 1.0)))
            if lod < 2:
                # close the sheet's thickness top and bottom, so nothing looks into the gap between its two faces
                a2c, b2c = add(inn, mul(right, -H)), add(inn, mul(right, H))
                dk = region("DARK")
                buf.quad_region(add(a, (0, 0, zs - 0.002)), add(b, (0, 0, zs - 0.002)), add(b2c, (0, 0, zs - 0.002)), add(a2c, (0, 0, zs - 0.002)), dk)
                buf.quad_region(add(b, (0, 0, zf - 0.002)), add(a, (0, 0, zf - 0.002)), add(a2c, (0, 0, zf - 0.002)), add(b2c, (0, 0, zf - 0.002)), dk)
            if lod < 2:
                a2, b2 = add(inn, mul(right, -H)), add(inn, mul(right, H))
                buf.quad_region(add(b2, (0, 0, zf)), add(a2, (0, 0, zf)), add(a2, (0, 0, zs)), add(b2, (0, 0, zs)), region("PANEL_IN"))
                # sill and head rails, centre mullion
                for z, w, h in ((zs + 0.012, 0.07, 0.024), (ze - 0.02, 0.07, 0.04)):
                    c = mul(out, H - 0.035)
                    # rails stop at the corner posts' flanges, so neighbouring walls' rails never overlap in the same plane
                    bar(buf, add(add(c, mul(right, -H + 0.07)), (0, 0, z)), add(add(c, mul(right, H - 0.07)), (0, 0, z)), out, w, h, T.steel(r, 0.5), u_off=r.random() * 0.4)
                c = mul(out, H - 0.03)
                bar(buf, add(c, (0, 0, zs + 0.02)), add(c, (0, 0, ze - 0.04)), right, 0.05, 0.04, T.steel(r, 0.4), u_off=r.random() * 0.4)
            else:
                # far LOD: the window band reads as a dark strip
                buf.quad_region(add(a, (0, 0, zs)), add(b, (0, 0, zs)), add(b, (0, 0, ze)), add(a, (0, 0, ze)), region("DARK"))
        if lod == 2:
            return buf
        # floor boards run along the hatch wall, laid in three bands so the trap door opening falls on board edges
        zf = L.z_floor
        h = L.hatch
        Hi = H - 0.012
        ft = cp["floor_thickness"]
        along = L.wall_xy(1.0, 0.0) + (0.0,)
        bands = [(-Hi, h["q0"], [(-Hi, Hi)]), (h["q0"], h["q1"], [(-Hi, h["p0"]), (h["p1"], Hi)]), (h["q1"], Hi, [(-Hi, Hi)])]
        for q0, q1, spans in bands:
            nb = max(1, int(round((q1 - q0) / 0.14)))
            bw = (q1 - q0) / nb
            for j in range(nb):
                qa, qb = q0 + j * bw + 0.0015, q0 + (j + 1) * bw - 0.0015
                for p0, p1 in spans:
                    if p1 - p0 < 0.02:
                        continue
                    wood = region(f"WOOD{r.randrange(N_WOOD)}")
                    uo = r.random() * (wood.length - (p1 - p0) - 0.02)
                    quad = [L.wall_xy(p0, qa), L.wall_xy(p1, qa), L.wall_xy(p1, qb), L.wall_xy(p0, qb)]
                    if lod == 0:
                        # bedded a millimetre off the steel it lies on, so no board face shares the girts' plane
                        ring = T._ccw_up([(x, y, zf - ft + 0.001) for x, y in quad])
                        prism(buf, ring, (0, 0, ft - 0.001), wood, along, u_off=uo)
                    else:
                        pq = [(p0, qa), (p1, qa), (p1, qb), (p0, qb)]
                        pts = [(x, y, zf) for x, y in quad]
                        uvs = [wood.uv(uo + pp - p0, wood.width if qq == qb else 0.0) for pp, qq in pq]
                        if T._ccw_up(pts) is not pts:
                            pts.reverse(); uvs.reverse()
                        buf.face(pts, uvs)
        if lod == 0:
            # trimmers round the opening, under the boards
            zj = zf - ft - 0.05

            def w3(p, q):
                x, y = L.wall_xy(p, q)
                return (x, y, zj)
            for p in (h["p0"] - 0.045, h["p1"] + 0.045):
                if abs(p) < Hi - 0.05:
                    bar(buf, w3(p, -Hi), w3(p, Hi), UP, 0.1, 0.05, T.steel(r, 0.3), u_off=r.random())
            bar(buf, w3(h["p0"], h["q0"] - 0.045), w3(min(h["p1"], Hi), h["q0"] - 0.045), UP, 0.1, 0.05, T.steel(r, 0.3), u_off=r.random())
            # leaf litter blown into the corners (director note N6: leaf litter only)
            lv = region("LEAVES")
            for cp_, cq, wp, wq, flip in ((-0.72, -0.78, 0.55, 0.36, 0), (0.7, -0.8, 0.5, 0.32, 1), (-0.82, 0.05, 0.3, 0.5, 0), (0.15, -0.86, 0.45, 0.25, 1)):
                cx, cy = L.wall_xy(cp_, cq)
                w, d = L.wall_xy(wp, wq)
                z = zf + 0.004
                f = (0.0, 0.0, 1.0, 1.0) if not flip else (1.0, 0.0, 0.0, 1.0)
                buf.quad_region((cx - w / 2, cy - d / 2, z), (cx + w / 2, cy - d / 2, z), (cx + w / 2, cy + d / 2, z), (cx - w / 2, cy + d / 2, z), lv, MAT_CUTOUT, f)
        return buf

    def roof(self, lod):
        L, T = self.L, self.T
        buf = MeshBuf(f"Roof_LOD{lod}")
        r = T.rng("roof")
        rh, ze, za = L.roof_half, L.z_eave, L.z_apex
        apex = (0.0, 0.0, za)
        cs = [(rh, rh), (-rh, rh), (-rh, -rh), (rh, -rh)]
        top, under = region("ROOF"), region("ROOF_UNDER")
        for i in range(4):
            a = (cs[i][0], cs[i][1], ze)
            b = (cs[(i + 1) % 4][0], cs[(i + 1) % 4][1], ze)
            buf.tri_region(a, b, apex, top)
            if lod < 2:
                d = (0, 0, -max(0.012, self.cp["roof_fascia"] - 0.002)) if self.cp["roof_fascia"] > 0.06 else (0, 0, -0.012)
                buf.tri_region(add(b, d), add(a, d), add(apex, d), under)
                fa = self.cp["roof_fascia"]
                buf.quad_region(add(a, (0, 0, -fa)), add(b, (0, 0, -fa)), b, a, region("ROOF_UNDER" if fa > 0.06 else "GALV"), f=(0.0, 0.0, 1.0, 0.2))
            if lod == 0:
                hip0 = add(a, (0, 0, 0.012))
                bar(buf, hip0, add(apex, (0, 0, 0.012)), UP, 0.03, 0.06, T.steel(r, 0.8), u_off=r.random() * 0.3)
        if lod < 2:
            sides = 10 if lod == 0 else 6
            galv = region("GALV")
            cylinder(buf, (0, 0, za - 0.08), (0, 0, za + 0.16), 0.085, 0.085, sides, galv)
            cylinder(buf, (0, 0, za + 0.19), (0, 0, za + 0.28), 0.16, 0.0, sides, galv)
            if lod == 0:
                box(buf, (0, 0, za - 0.03), (0.15, 0, 0), (0, 0.15, 0), (0, 0, 0.004), galv)
        return buf

    # ---- sashes ------------------------------------------------------------------------------------------------
    def sash_frames(self):
        """Hinge point, right vector (seen from outside), outward normal, size and name of all eight sashes."""
        L = self.L
        H = L.cab_half
        clear = 2.0 * (H - 3 * IN)
        ws = (clear - 0.05) / 2.0 - 0.01
        z_top, z_bot = L.z_eave - 0.045, L.z_sill + 0.026
        out = []
        for side, o, right in SIDES:
            for kind, sgn in (("Hinged", 1.0), ("Fixed", -1.0)):
                # the hinged sash is on the left seen from inside, which is the right seen from outside
                c = add(mul(o, H - 0.03), mul(right, sgn * (0.03 + ws / 2.0)))
                out.append({"name": f"Sash_{side}_{kind}", "side": side, "kind": kind,
                            "pivot": (c[0], c[1], z_top), "right": right, "out": o, "w": ws, "h": z_top - z_bot})
        return out

    def sash(self, s):
        T = self.T
        buf = MeshBuf(s["name"])
        r = T.rng(s["name"])
        P0, right, out = s["pivot"], s["right"], s["out"]
        w, h = s["w"], s["h"]
        fw, fd, mw = 0.035, 0.03, 0.018

        def pt(x, z, d=0.0):
            """x along the wall from the sash centre, z down from the hinge, d outward."""
            return add(add(P0, mul(right, x)), add((0.0, 0.0, -z), mul(out, d)))

        for z in (fw / 2, h - fw / 2):
            bar(buf, pt(-w / 2, z), pt(w / 2, z), out, fd, fw, T.steel(r, 0.5), u_off=r.random() * 0.4)
        for x in (-w / 2 + fw / 2, w / 2 - fw / 2):
            bar(buf, pt(x, fw), pt(x, h - fw), out, fd, fw, T.steel(r, 0.5), u_off=r.random() * 0.4)
        pw = (w - 2 * fw - 2 * mw) / 3.0
        ph = (h - 2 * fw - 2 * mw) / 3.0
        for j in (1, 2):
            x = -w / 2 + fw + j * pw + (j - 0.5) * mw
            bar(buf, pt(x, fw - 0.004), pt(x, h - fw + 0.004), out, fd * 0.6, mw, T.steel(r, 0.5), u_off=r.random() * 0.4, caps=False)
            z = fw + j * ph + (j - 0.5) * mw
            # the cross bars are a little shallower than the uprights, so the two don't share a face where they cross
            bar(buf, pt(-w / 2 + fw - 0.004, z), pt(w / 2 - fw + 0.004, z), out, fd * 0.5, mw * 0.9, T.steel(r, 0.5), u_off=r.random() * 0.4, caps=False)
        panes = []
        cells = []
        for row in range(3):
            for col in range(3):
                idx = row * 3 + col
                state = self.pane_plan[(s["side"], s["kind"], idx)]
                x0 = -w / 2 + fw + col * (pw + mw)
                z0 = fw + row * (ph + mw)
                panes.append(state)
                cells.append([round(x0, 4), round(z0, 4), round(x0 + pw, 4), round(z0 + ph, 4)])
                if state != "missing":
                    g = region("GLASS1" if state == "cracked" else "GLASS0")
                    flip = r.random() < 0.5
                    f = (1.0, 0.0, 0.0, 1.0) if flip else (0.0, 0.0, 1.0, 1.0)
                    buf.quad_region(pt(x0, z0 + ph), pt(x0 + pw, z0 + ph), pt(x0 + pw, z0), pt(x0, z0), g, MAT_GLASS, f)
                    continue
                # shards left in the putty: triangles standing on the pane's edges
                sh = region("SHARD")
                for _ in range(r.randrange(0, 4)):
                    edge = r.randrange(4)
                    base = 0.04 + 0.11 * r.random()
                    tall = 0.03 + 0.10 * r.random()
                    lean = (r.random() - 0.5) * base
                    if edge in (0, 2):          # bottom or top edge
                        u = r.random() * (pw - base)
                        zb = z0 + ph if edge == 0 else z0
                        zt = zb - tall if edge == 0 else zb + tall
                        tri = [pt(x0 + u, zb), pt(x0 + u + base, zb), pt(x0 + u + base / 2 + lean, zt)]
                    else:                        # left or right edge
                        v = r.random() * (ph - base)
                        xb = x0 if edge == 1 else x0 + pw
                        xt = xb + tall if edge == 1 else xb - tall
                        tri = [pt(xb, z0 + v), pt(xb, z0 + v + base), pt(xt, z0 + v + base / 2 + lean)]
                    buf.tri_region(tri[0], tri[1], tri[2], sh, MAT_GLASS, f=((0.1, 0.05), (0.9, 0.05), (0.5, 0.95)))
        self.sash_info[s["name"]] = {"panes": panes, "missing": panes.count("missing"), "cells": cells}
        return buf

    # ---- trap door ---------------------------------------------------------------------------------------------
    def hatch_pivot(self):
        h = self.L.hatch
        x, y = self.L.wall_xy((h["p0"] + h["p1"]) / 2.0, h["q1"])
        return (x, y, self.L.z_floor)

    def hatch(self):
        L, T = self.L, self.T
        h, th = L.hatch, self.P["hatch"]["thickness"]
        buf = MeshBuf("Hatch")
        r = T.rng("hatch")
        zf = L.z_floor
        p0, p1 = h["p0"] + 0.004, h["p1"] - 0.004
        q0, q1 = h["q0"] + 0.004, h["q1"] - 0.004
        along = L.wall_xy(1.0, 0.0) + (0.0,)
        across = L.wall_xy(0.0, 1.0) + (0.0,)

        def w3(p, q, z):
            x, y = L.wall_xy(p, q)
            return (x, y, z)
        nb = 5
        bw = (q1 - q0) / nb
        for j in range(nb):
            qa, qb = q0 + j * bw + 0.001, q0 + (j + 1) * bw - 0.001
            wood = region(f"WOOD{r.randrange(N_WOOD)}")
            uo = r.random() * (wood.length - (p1 - p0) - 0.02)
            ring = T._ccw_up([w3(p0, qa, zf - th), w3(p1, qa, zf - th), w3(p1, qb, zf - th), w3(p0, qb, zf - th)])
            prism(buf, ring, (0, 0, th), wood, along, u_off=uo)
        for p in (p0 + 0.22, p1 - 0.22):         # strap hinges across the boards
            box(buf, w3(p, (q0 + q1) / 2, zf + 0.002), mul(across, (q1 - q0) / 2), mul(along, 0.022), (0, 0, 0.002), T.steel(r, 0.9), u_off=r.random())
        # pull ring plate near the free edge
        box(buf, w3((p0 + p1) / 2, q0 + 0.07, zf + 0.002), mul(along, 0.05), mul(across, 0.03), (0, 0, 0.002), T.steel(r, 0.9), u_off=r.random())
        return buf

    # ---- firefinder --------------------------------------------------------------------------------------------
    def firefinder_axis(self):
        return (0.0, 0.0, self.L.z_floor + self.P["firefinder"]["ring_top"])

    def firefinder_static(self, glyphs):
        """Pedestal and bearing ring as two buffers. Ticks every degree, numerals every 10, 000 at north, clockwise."""
        L, fp = self.L, self.P["firefinder"]
        zf = L.z_floor
        R, zt = fp["ring_radius"], zf + fp["ring_top"]
        galv, brass, dark = region("GALV"), region("BRASS"), region("DARK")
        ped = MeshBuf("Firefinder_Pedestal")
        cylinder(ped, (0, 0, zf), (0, 0, zf + 0.025), 0.15, 0.15, 12, galv)
        cylinder(ped, (0, 0, zf + 0.025), (0, 0, zt - 0.06), fp["pedestal_radius"], fp["pedestal_radius"], 12, galv)
        # the plate under the ring: the strip runs round its rim (u) at one height of the strip (v). Mapped the other way,
        # each facet took a different band across the strip and the rim read as stripes (G2-8).
        rp_, z0_, z1_ = R + 0.015, zt - 0.06, zt - 0.03
        rim = [(rp_ * math.cos(2 * math.pi * i / 24), rp_ * math.sin(2 * math.pi * i / 24)) for i in range(24)]
        for i in range(24):
            (xa, ya), (xb, yb) = rim[i], rim[(i + 1) % 24]
            u0, u1 = 0.05 + 0.9 * i / 24, 0.05 + 0.9 * (i + 1) / 24
            ped.face([(xa, ya, z0_), (xb, yb, z0_), (xb, yb, z1_), (xa, ya, z1_)],
                     [galv.uv01(u0, 0.4), galv.uv01(u1, 0.4), galv.uv01(u1, 0.6), galv.uv01(u0, 0.6)])
        ped.face([(x, y, z1_) for x, y in rim], [galv.uv01(0.5 + 0.4 * x / rp_, 0.5 + 0.1 * y / rp_) for x, y in rim])
        ped.face([(x, y, z0_) for x, y in reversed(rim)], [galv.uv01(0.5 + 0.4 * x / rp_, 0.5 + 0.1 * y / rp_) for x, y in reversed(rim)])

        ring = MeshBuf("Firefinder_Ring")
        seg = 72
        ri = R - 0.045
        zb = zt - 0.03

        def P(rad, deg, z):
            t = math.radians(deg)
            return (rad * math.sin(t), rad * math.cos(t), z)      # bearing: 0 = north (+y), clockwise seen from above

        for i in range(seg):
            a0, a1 = 360.0 * i / seg, 360.0 * (i + 1) / seg
            u0, u1 = (i % 12) / 12.0, (i % 12 + 1) / 12.0
            # the bearing runs clockwise seen from above, so faces are wound a1 -> a0 on the outside
            ring.face([P(R, a1, zt), P(R, a0, zt), P(ri, a0, zt), P(ri, a1, zt)],
                      [brass.uv01(u1, 0.0), brass.uv01(u0, 0.0), brass.uv01(u0, 0.6), brass.uv01(u1, 0.6)])
            ring.face([P(R, a1, zb), P(R, a0, zb), P(R, a0, zt), P(R, a1, zt)],
                      [brass.uv01(u1, 0.6), brass.uv01(u0, 0.6), brass.uv01(u0, 1.0), brass.uv01(u1, 1.0)])
            ring.face([P(ri, a0, zb), P(ri, a1, zb), P(ri, a1, zt), P(ri, a0, zt)],
                      [brass.uv01(u0, 0.6), brass.uv01(u1, 0.6), brass.uv01(u1, 1.0), brass.uv01(u0, 1.0)])
        # faded map disc inside the ring
        md = region("MAPDISC")
        zm = zt - 0.012
        rm = ri + 0.002
        for i in range(24):
            a0, a1 = 15.0 * i, 15.0 * (i + 1)
            def muv(deg, rad=1.0):
                t = math.radians(deg)
                return md.uv01(0.5 + 0.5 * rad * math.sin(t), 0.5 + 0.5 * rad * math.cos(t))
            ring.face([(0, 0, zm), P(rm, a1, zm), P(rm, a0, zm)], [md.uv01(0.5, 0.5), muv(a1), muv(a0)])
        # engraving: ticks and numerals just proud of the ring
        ze = zt + 0.0004
        for deg in range(360):
            ln = 0.014 if deg % 10 == 0 else (0.010 if deg % 5 == 0 else 0.006)
            hw = 0.0006 if deg % 5 == 0 else 0.0004
            hd = math.degrees(hw / R)
            ro, rin = R - 0.0015, R - 0.0015 - ln
            ring.face([P(ro, deg + hd, ze), P(ro, deg - hd, ze), P(rin, deg - hd, ze), P(rin, deg + hd, ze)], [dark.uv01(0.5, 0.5)] * 4)
        nh = fp["numeral_height"]
        adv = nh * 0.66
        rn = R - 0.0015 - 0.014 - 0.004           # numeral tops sit just inside the long ticks
        numerals = {}
        for deg in range(0, 360, 10):
            t = math.radians(deg)
            up = (math.sin(t), math.cos(t), 0.0)              # tops point outward
            rt = (math.cos(t), -math.sin(t), 0.0)             # reads left to right seen from above
            base = (up[0] * (rn - nh), up[1] * (rn - nh), ze)
            text = f"{deg:03d}"
            for ci, ch in enumerate(text):
                ox = (ci - 1) * adv - glyphs[ch]["width"] * nh / 2.0
                for tri in glyphs[ch]["tris"]:
                    pts = [add(base, add(mul(rt, ox + x * nh), mul(up, y * nh))) for x, y in tri]
                    if cross(sub(pts[1], pts[0]), sub(pts[2], pts[1]))[2] < 0:
                        pts.reverse()
                    ring.face(pts, [dark.uv01(0.5, 0.5)] * 3)
            numerals[text] = {"bearing_deg": float(deg), "design_xy": [up[0] * (rn - nh / 2), up[1] * (rn - nh / 2)]}
        self.numerals = numerals
        return ped, ring

    def alidade_points(self):
        """Rest-pose reference points in the design frame: where the eye sits behind the rear sight, and the pointer tip."""
        o = self.firefinder_axis()
        R = self.P["firefinder"]["ring_radius"]
        return {"rear_sight": (o[0], o[1] - (R - 0.04), o[2] + 0.004 + 0.13), "front_sight": (o[0], o[1] + (R - 0.04), o[2] + 0.004 + 0.13),
                "pointer_tip": (o[0], o[1] + R - 0.001, o[2] + 0.004)}

    def alidade(self):
        """Sighting arm; rest pose points north. Pointer and front sight at the north end, rear sight at the south end."""
        fp = self.P["firefinder"]
        buf = MeshBuf("Firefinder_Alidade")
        brass = region("BRASS")
        R = fp["ring_radius"]
        o = self.firefinder_axis()
        z = 0.004

        def at(x, y, zz):
            return (o[0] + x, o[1] + y, o[2] + zz)

        box(buf, at(0, 0.0035, z), (0, R - 0.0215, 0), (0.012, 0, 0), (0, 0, 0.003), brass)
        cylinder(buf, at(0, 0, z), at(0, 0, z + 0.014), 0.03, 0.03, 10, brass)
        # pointer: tapers to the ring's outer edge at the bearing
        tip, b0, b1 = at(0, R - 0.001, z), at(-0.012, R - 0.018, z), at(0.012, R - 0.018, z)
        buf.tri_region(b0, b1, tip, brass)
        buf.tri_region(add(b1, (0, 0, -0.003)), add(b0, (0, 0, -0.003)), add(tip, (0, 0, -0.003)), brass)
        # front sight: an open frame with a vertical hair
        yf, hs = R - 0.04, 0.13
        for x in (-0.022, 0.022):
            box(buf, at(x, yf, z + hs / 2), (0, 0, hs / 2), (0.003, 0, 0), (0, 0.002, 0), brass)
        box(buf, at(0, yf, z + hs), (0.025, 0, 0), (0, 0.002, 0), (0, 0, 0.003), brass)
        box(buf, at(0, yf, z + hs / 2), (0, 0, hs / 2), (0.0008, 0, 0), (0, 0.0008, 0), region("DARK"))
        # rear sight: a plate with a slit
        yr = -(R - 0.04)
        for x in (-0.0115, 0.0115):
            box(buf, at(x, yr, z + hs / 2), (0, 0, hs / 2), (0.010, 0, 0), (0, 0.0015, 0), brass)
        box(buf, at(0, yr, z + hs + 0.003), (0.0215, 0, 0), (0, 0.0015, 0), (0, 0, 0.003), brass)
        return buf

    # ---- LOD1 stand-ins: the movers shown closed (they are separate objects in LOD0 only) ---------------------------
    def movers_lod1(self):
        L, T = self.L, self.T
        buf = MeshBuf("Movers_LOD1")
        r = T.rng("movers_lod1")
        for s in self.sash_frames():
            P0, right, out = s["pivot"], s["right"], s["out"]
            w, h = s["w"], s["h"]
            fw, mw = 0.035, 0.018

            def pt(x, z):
                return add(add(P0, mul(right, x)), (0.0, 0.0, -z))

            def strip(x0, z0, x1, z1):
                buf.quad_region(pt(x0, z1), pt(x1, z1), pt(x1, z0), pt(x0, z0), T.steel(r, 0.5), f=(0.1, 0.0, 0.5, 0.4))
            strip(-w / 2, 0, w / 2, fw); strip(-w / 2, h - fw, w / 2, h)
            strip(-w / 2, fw, -w / 2 + fw, h - fw); strip(w / 2 - fw, fw, w / 2, h - fw)
            pw = (w - 2 * fw - 2 * mw) / 3.0
            ph = (h - 2 * fw - 2 * mw) / 3.0
            for j in (1, 2):
                x = -w / 2 + fw + j * pw + (j - 1) * mw
                strip(x, fw, x + mw, h - fw)
                z = fw + j * ph + (j - 1) * mw
                strip(-w / 2 + fw, z, w / 2 - fw, z + mw)
            for row in range(3):
                for col in range(3):
                    if self.pane_plan[(s["side"], s["kind"], row * 3 + col)] == "missing":
                        continue
                    x0 = -w / 2 + fw + col * (pw + mw)
                    z0 = fw + row * (ph + mw)
                    buf.quad_region(pt(x0, z0 + ph), pt(x0 + pw, z0 + ph), pt(x0 + pw, z0), pt(x0, z0), region("GLASS0"), MAT_GLASS)
        x0, x1, y0, y1 = L.hatch_rect(-0.004)
        zf = L.z_floor
        wood = region("WOOD0")
        buf.face([(x0, y0, zf), (x1, y0, zf), (x1, y1, zf), (x0, y1, zf)], [wood.uv(0.2, 0), wood.uv(1.7, 0), wood.uv(1.7, wood.width), wood.uv(0.2, wood.width)])
        fp = self.P["firefinder"]
        zt = zf + fp["ring_top"]
        cylinder(buf, (0, 0, zf), (0, 0, zt - 0.06), fp["pedestal_radius"], fp["pedestal_radius"], 6, region("GALV"), caps=False)
        cylinder(buf, (0, 0, zt - 0.06), (0, 0, zt), fp["ring_radius"], fp["ring_radius"], 12, region("BRASS"))
        return buf
