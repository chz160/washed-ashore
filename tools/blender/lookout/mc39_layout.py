"""Layout maths for the MC-39 lookout tower: levels, taper, flights, landings, hatch. Pure Python (no bpy).

Design frame: x = east, y = north, z = up, metres, origin at the tower centre on the pad-top plane.
`steel` and `z_steel` are the drawn steel levels; `levels` and `z_floor` are the walking surfaces on top of them.
The stair is a dogleg on the SW-NE diagonal: D points to the NE corner, U is the lateral direction (to the SE).
Flights 1..n-1 run along D or -D; the last flight runs along one wall up to the trap door: west along the north
wall, or (the mirror image about the diagonal) south along the east wall. `wall_xy` maps wall coordinates
(p along the wall, q toward it) to x, y, so the cab, hatch and proxies are written once for both.
"""
import math

import mc39_poly as poly

FT = 0.3048
IN = 0.0254
SQ2 = math.sqrt(2.0)
D = (1.0 / SQ2, 1.0 / SQ2)
U = (1.0 / SQ2, -1.0 / SQ2)


def diag(along, lateral, z):
    """Point given by distance along the diagonal (NE positive), lateral offset (SE positive) and height."""
    return (along * D[0] + lateral * U[0], along * D[1] + lateral * U[1], z)


class Layout:
    def __init__(self, P):
        self.P = P
        std = P["standards"][P["height_standard"]]
        self.height = std["height_ft"] * FT
        self.base = std["base_ft"] * FT
        self.top = P["top_square_ft"] * FT
        self.risers = list(std["risers"])
        st = P["stairs"]
        self.riser = st["riser_in"] * IN
        self.going = self.riser * st["pitch_run"] / st["pitch_rise"]
        self.pitch_deg = math.degrees(math.atan2(st["pitch_rise"], st["pitch_run"]))
        self.stair_w = st["width_ft"] * FT
        if abs(sum(self.risers) * self.riser - self.height) > 1e-6:
            raise ValueError("riser counts do not add up to the cab-floor height")

        # Steel levels (the drawn ones): top of bed plate, each landing frame, top of the tower steel.
        self.steel = [0.0]
        for n in self.risers:
            self.steel.append(self.steel[-1] + n * self.riser)
        self.steel[-1] = self.height
        self.n_flights = len(self.risers)
        self.n_landings = self.n_flights - 1

        # Walking surfaces: each deck lies on its steel, one deck thickness up (see landings._deck in the table). The
        # stair keeps its drawn risers between walking surfaces, so its foot stands the same thickness above the bed
        # plates (pads._stair_foot). The metric thicknesses are put back in the table for the other modules.
        cab = P["cab"]
        P["landings"]["plank_thickness"] = P["landings"]["deck_thickness_in"] * IN
        cab["floor_thickness"] = cab["floor_thickness_in"] * IN
        self.deck_lift = P["landings"]["plank_thickness"]
        if abs(cab["floor_thickness"] - self.deck_lift) > 1e-9:
            raise ValueError("cab floor and landing deck thickness differ: the top flight would not rise a whole number of risers")
        self.levels = [z + self.deck_lift for z in self.steel]
        self.z_steel = self.height
        self.z_floor = self.z_steel + cab["floor_thickness"]
        self.z_sill = self.z_steel + cab["lower_wall_ft"] * FT
        self.z_eave = self.z_sill + cab["window_band_ft"] * FT
        self.roof_half = cab["roof_eave_square_ft"] * FT / 2.0
        self.z_apex = self.z_eave + self.roof_half * cab["roof_pitch_rise"] / cab["roof_pitch_run"]
        self.cab_half = self.top / 2.0

        self._solve_flights()

    def half(self, z):
        """Half width of the tower (to the outer face of the legs) at height z; constant above the cab floor."""
        t = min(max(z / self.height, 0.0), 1.0)
        return (self.base + (self.top - self.base) * t) / 2.0

    def _solve_flights(self):
        st = self.P["stairs"]
        nd = self.n_flights - 1          # diagonal flights
        # e[k] = distance from the tower centre line to the inner edge of landing k (k = 0 is the ground foot), on that
        # landing's side, along the flight. T-1004-A dimensions the top two (2'-1 3/8" and 3'-1"); a flight is n goings
        # between landing edges for n risers, so the rest follow down the tower from the riser counts.
        e = [0.0] * (nd + 1)
        e[nd] = st["top_landing_edge_in"] * IN
        for k in range(nd, 0, -1):
            e[k - 1] = self.risers[k - 1] * self.going - e[k]
        if nd >= 2 and abs(e[nd - 1] - st["second_landing_edge_in"] * IN) > 0.005:
            raise ValueError(f"second landing edge {e[nd - 1]:.4f} m does not meet the sheet's {st['second_landing_edge_in']} in")
        self.edge = e
        lp = self.P["landings"]
        self.plat_hw = lp["platform_width_in"] * IN / 2.0
        # room from a landing's inner edge to the tower corner, and the platform's depth (cut short of the leg)
        self.depth = [None] + [SQ2 * self.half(self.levels[k]) - e[k] for k in range(1, nd + 1)]
        self.plat_depth = [None] + [min(lp["platform_depth_in"] * IN, self.depth[k] - 0.12) for k in range(1, nd + 1)]
        for k in range(1, nd + 1):
            if e[k] <= 0.05 or self.plat_depth[k] < 0.6:
                raise ValueError(f"landing {k} does not fit (edge {e[k]:.3f}, room {self.depth[k]:.3f})")

        self.flights = []
        for k in range(1, nd + 1):
            up = 1.0 if k % 2 == 1 else -1.0           # odd flights climb to the NE corner
            lat = st["lateral_offset_in"] * IN * up * st.get("lateral_sign", 1)
            # the nosing line meets the lower level at that landing's edge; the first nosing is one going out from it
            a0 = -up * e[k - 1] + up * self.going
            self.flights.append(self._flight(k, diag(a0, lat, self.levels[k - 1]), (up * D[0], up * D[1]), self.risers[k - 1]))
        # Top flight along a wall. Its first riser stands on the top landing; its top nosing is the hatch edge.
        tf = st["top_flight"]
        self.wall = tf["wall"]
        if self.wall not in ("north", "east"):
            raise ValueError("top_flight.wall must be 'north' or 'east'")
        n = self.risers[-1]
        if "head_from_wall_in" in tf:
            tf = dict(tf, head=-(self.cab_half - tf["head_from_wall_in"] * IN))
        tg = self.riser * tf.get("pitch_run", st["pitch_run"]) / tf.get("pitch_rise", st["pitch_rise"])
        p0 = tf["head"] + (n - 1) * tg
        qc = tf["band_min"] + self.stair_w / 2.0
        x, y = self.wall_xy(p0, qc)
        self.flights.append(self._flight(self.n_flights, (x, y, self.levels[-2]), self.wall_xy(-1.0, 0.0), n, tg))

        h = self.P["hatch"]
        # opening in wall coordinates: p0..p1 along the wall, q0..q1 toward it (the hinge is on the q1 edge)
        self.hatch = {"p0": tf["head"], "p1": tf["head"] + h["length"], "q0": h["band_min"], "q1": h["band_min"] + h["width"]}

    def wall_xy(self, p, q):
        return (p, q) if self.wall == "north" else (q, p)

    def hatch_rect(self, grow=0.0):
        """x0, x1, y0, y1 of the trap door opening."""
        h = self.hatch
        (xa, ya), (xb, yb) = self.wall_xy(h["p0"], h["q0"]), self.wall_xy(h["p1"], h["q1"])
        return (min(xa, xb) - grow, max(xa, xb) + grow, min(ya, yb) - grow, max(ya, yb) + grow)

    def _flight(self, index, first_riser, direction, n, going=None):
        """first_riser: centre of the first riser's foot. Nosing i (1..n) is at first_riser + (i-1)*going*dir, z0 + i*riser."""
        x, y, z0 = first_riser
        dx, dy = direction
        g = self.going if going is None else going
        run = (n - 1) * g
        return {
            "going": g, "pitch_deg": math.degrees(math.atan2(self.riser, g)),
            "index": index, "risers": n, "dir": (dx, dy), "lat": (dy, -dx),
            "first_riser": (x, y, z0),
            "foot": (x - dx * g, y - dy * g, z0),         # where the nosing line meets the lower level
            "head": (x + dx * run, y + dy * run, z0 + n * self.riser),      # top nosing = edge of the upper level
            "z0": z0, "z1": z0 + n * self.riser, "run": run,
        }

    def nosing(self, f, i):
        x, y, z0 = f["first_riser"]
        dx, dy = f["dir"]
        g = f["going"]
        return (x + dx * (i - 1) * g, y + dy * (i - 1) * g, z0 + i * self.riser)

    def half_width_at(self, k, t, inset=0.05):
        """Half width of landing k's platform at distance t from its inner edge: the 5'-2" strip, cut by the tower faces."""
        return max(min(self.plat_hw, self.depth[k] - t - inset), 0.0)

    def landing_tl(self, k):
        """Platform outline of landing k in (t, lateral) coordinates: t from the inner edge toward the corner."""
        T = self.plat_depth[k]
        w0, wT = self.half_width_at(k, 0.0), self.half_width_at(k, T)
        tc = self.depth[k] - 0.05 - self.plat_hw            # where the faces start to cut the strip
        left = [(0.0, -w0)]
        right = [(0.0, w0)]
        if 0.0 < tc < T:
            left.append((tc, -self.plat_hw)); right.append((tc, self.plat_hw))
        left.append((T, -wT)); right.append((T, wT))
        return right + list(reversed(left))

    def landing_point(self, k, t, lat, z=None):
        s = 1.0 if k % 2 == 1 else -1.0
        return diag(s * (self.edge[k] + t), lat, self.levels[k] if z is None else z)

    def is_frame_landing(self, k):
        return k == self.n_landings and self.P["landings"].get("top_landing") == "drawn_frame"

    def landing_polygon(self, k):
        """Deck outline of landing k (1-based) in the design frame, at deck height. Odd landings sit in the NE corner.

        Ordinary landings: the 5'-2" x 2'-6" platform, cut by the tower faces. The top landing, when
        landings.top_landing is "drawn_frame": the corner between the two walls, from the edge where the last diagonal
        flight ends to the line where the top flight starts, with the leg's corner cut off."""
        z = self.levels[k]
        if not self.is_frame_landing(k):
            return [self.landing_point(k, t, lat) for t, lat in self.landing_tl(k)]
        s = 1.0 if k % 2 == 1 else -1.0
        a = self.half(z) - 0.05
        ex = SQ2 * self.edge[k]                       # inner edge: s * (x + y) = ex
        pts = [(s * (ex - a), s * a), (s * a, s * (ex - a)), (s * a, s * a)]
        f = self.flights[-1]
        fx, fy, _ = f["foot"]
        pts = poly.clip_half_plane(pts, (fx, fy), (-f["dir"][0], -f["dir"][1]))      # the deck stops at the top flight's start line
        pts = poly.clip_half_plane(pts, (s * (a - 0.12), s * a), (-s * D[0], -s * D[1]))   # the leg's corner
        return [(x, y, z) for x, y in pts]

    def plank_frame(self, k):
        """(origin, along, across): planks run `along`, stacked `across` from the origin edge into the deck."""
        if not self.is_frame_landing(k):
            s = 1.0 if k % 2 == 1 else -1.0
            o = self.landing_point(k, 0.0, 0.0)
            return (o[0], o[1]), U, (s * D[0], s * D[1])
        f = self.flights[-1]
        return (f["foot"][0], f["foot"][1]), (f["lat"][0], f["lat"][1]), (-f["dir"][0], -f["dir"][1])
