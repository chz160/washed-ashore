"""Load the research GeoJSON/landmark inputs and emit Unity-space vector data.

Game space: +X east, +Z north, 1 unit = 1 game metre, origin at the playable
polygon's area centroid (UTM 16N), x = (E - Ec) * horizontalScale.
"""
import json
from pathlib import Path

import numpy as np

import geo

RESEARCH = Path("_bmad-output/research/domain-bells-bend-map-2026-10-05")
SHORELINE = RESEARCH / "digests/shoreline.geojson"
ROADS = RESEARCH / "digests/roads.geojson"
HERE = Path(__file__).resolve().parent
REQUIRED_ROADS = ("Old Hickory Boulevard", "Pecan Valley Road", "Tidwell Hollow Road",
                  "Cleeces Ferry Road", "Cleeces Rerry Road")


class Frame:
    """UTM <-> game transform anchored on the polygon centroid."""

    def __init__(self, e_c, n_c, hs):
        self.e_c, self.n_c, self.hs = e_c, n_c, hs

    def to_game(self, lat, lon):
        e, n = geo.latlon_to_utm(lat, lon)
        return (e - self.e_c) * self.hs, (n - self.n_c) * self.hs

    def game_to_utm(self, x, z):
        return self.e_c + np.asarray(x) / self.hs, self.n_c + np.asarray(z) / self.hs


def _feature(fc, name):
    for f in fc["features"]:
        if f["properties"].get("name") == name:
            return f
    raise KeyError(f"{name} not in GeoJSON")


def load_shoreline(root):
    fc = json.loads((Path(root) / SHORELINE).read_text(encoding="utf-8"))
    ring = np.array(_feature(fc, "bells_bend_playable_polygon")["geometry"]["coordinates"][0])
    bank = np.array(_feature(fc, "bells_bend_inner_bank_full")["geometry"]["coordinates"])
    if np.allclose(ring[0], ring[-1]):
        ring = ring[:-1]
    return {"ring_lonlat": ring, "bank_lonlat": bank, "north_lat": fc["properties"]["north_line_lat"],
            "attribution": fc["properties"]["attribution"]}


def make_frame(shore, hs):
    e, n = geo.latlon_to_utm(shore["ring_lonlat"][:, 1], shore["ring_lonlat"][:, 0])
    area, e_c, n_c = geo.polygon_area_centroid(e, n)
    # whole metres keep game samples on the 1 m DEM pixel lattice
    return Frame(round(e_c), round(n_c), hs), area


def _pts(frame, lonlat):
    x, z = frame.to_game(lonlat[:, 1], lonlat[:, 0])
    return x, z


def _xz_list(x, z):
    return [[round(float(a), 2), round(float(b), 2)] for a, b in zip(x, z)]


def landmarks(root, frame, shore, roads_fc):
    src = json.loads((HERE / "landmarks.json").read_text(encoding="utf-8"))
    bx, bz = _pts(frame, shore["bank_lonlat"])
    rx, rz = _pts(frame, shore["ring_lonlat"])
    out = []
    for lm in src["landmarks"]:
        item = dict(lm)
        if "derive" in lm:  # e.g. landing = end of a named road nearest the bank
            best = None
            for f in roads_fc["features"]:
                if f["properties"].get("name") not in lm["roads"]:
                    continue
                c = np.array(f["geometry"]["coordinates"])
                for lon, lat in (c[0], c[-1]):
                    x, z = frame.to_game(lat, lon)
                    d = geo.point_segment_distance(float(x), float(z), bx, bz)
                    if best is None or d < best[0]:
                        best = (d, lat, lon)
            item["lat"], item["lon"] = round(float(best[1]), 6), round(float(best[2]), 6)
        x, z = (float(v) for v in frame.to_game(item["lat"], item["lon"]))
        item["sourceX"], item["sourceZ"] = round(x, 2), round(z, 2)
        item["insidePolygon"] = geo.point_in_ring(x, z, rx, rz)
        if "derive" in lm or not item["insidePolygon"]:
            # landing: road end carried to the bank; outside points: pulled onto land
            px, pz = _nearest_on_polyline(x, z, bx, bz)
            sign = 1.0 if not item["insidePolygon"] else -1.0
            dx, dz = sign * (px - x), sign * (pz - z)
            norm = max(np.hypot(dx, dz), 1e-6)
            x, z = px + dx / norm * SNAP_INLAND, pz + dz / norm * SNAP_INLAND
            item["snapped"] = f"moved onto land {SNAP_INLAND} m inside the bank"
        item["x"], item["z"] = round(x, 2), round(z, 2)
        item["sourceInsidePolygon"] = item["insidePolygon"]
        item["insidePolygon"] = geo.point_in_ring(x, z, rx, rz)
        item["distToShore"] = round(geo.point_segment_distance(x, z, bx, bz), 1)
        out.append(item)
    return out


SNAP_INLAND = 5.0


def _nearest_on_polyline(x, z, lx, lz):
    best = (np.inf, x, z)
    for k in range(len(lx) - 1):
        ax, az, dx, dz = lx[k], lz[k], lx[k + 1] - lx[k], lz[k + 1] - lz[k]
        ll = dx * dx + dz * dz
        t = 0.0 if ll == 0 else min(max(((x - ax) * dx + (z - az) * dz) / ll, 0.0), 1.0)
        qx, qz = ax + t * dx, az + t * dz
        d = np.hypot(x - qx, z - qz)
        if d < best[0]:
            best = (d, float(qx), float(qz))
    return best[1], best[2]


def sample_points():
    return json.loads((HERE / "l2_sample_points.json").read_text(encoding="utf-8"))["points"]


def export(root, out_dir, frame, shore, cfg, north_z_data):
    """Write game-space JSON for level/engineer/qa. Returns the dict that was written."""
    roads_fc = json.loads((Path(root) / ROADS).read_text(encoding="utf-8"))
    rx, rz = _pts(frame, shore["ring_lonlat"])
    bx, bz = _pts(frame, shore["bank_lonlat"])
    roads = []
    for f in roads_fc["features"]:
        p = f["properties"]
        c = np.array(f["geometry"]["coordinates"])
        x, z = _pts(frame, c)
        roads.append({"name": p.get("name"), "highway": p.get("highway"), "surface": p.get("surface"),
                      "osm_id": p.get("osm_id"), "zone": p.get("zone"), "bridge": p.get("bridge") == "yes",
                      "required": p.get("name") in REQUIRED_ROADS, "points": _xz_list(x, z)})
    wx, wz = float(bx[0]), float(bz[0])
    ex, ez = float(bx[-1]), float(bz[-1])
    data = {
        "frame": {"utmEpsg": 26916, "originE": frame.e_c, "originN": frame.n_c,
                  "horizontalScale": frame.hs, "axes": "+X east, +Z north, Y up"},
        "attribution": "(c) OpenStreetMap contributors, ODbL 1.0 (https://www.openstreetmap.org/copyright)",
        "playablePolygon": _xz_list(rx, rz),
        "innerBank": _xz_list(bx, bz),
        "northLine": {
            "dataLatitude": shore["north_lat"], "dataZ": round(north_z_data, 2),
            "configZ": cfg.northLineZ, "westCrossing": [round(wx, 2), round(wz, 2)],
            "eastCrossing": [round(ex, 2), round(ez, 2)],
            "note": "Terrain masking uses the data line; barrier/backstop/clamp use MapConfig.northLineZ. "
                    "The data line tilts by under 2 m across the map (UTM grid convergence).",
        },
        "roads": roads,
        "landmarks": landmarks(root, frame, shore, roads_fc),
    }
    (Path(out_dir) / "map_vectors.json").write_text(json.dumps(data, indent=1), encoding="utf-8")
    return data
