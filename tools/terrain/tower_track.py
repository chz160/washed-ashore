"""Lookout tower access track: least-grade route from the tower pad to the nearest painted road.

    /c/Python313/python.exe tools/terrain/tower_track.py          # writes tools/terrain/tower_track.json
    /c/Python313/python.exe tools/terrain/tower_track.py --check  # recompute and compare, write nothing

Run after build_terrain.py (it reads the built tiles), then rerun build_terrain.py so
map_vectors.json carries the track as the road "Lookout Tower Track". The site (centre offset
from the projected landmark, target road, box to keep out of) comes from the LookoutTower entry in
landmarks.json. The route depends only on the tile RAWs and that entry, so it is the same on every run.
"""
import argparse
import hashlib
import heapq
import json
import math
import sys
from pathlib import Path

import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parent))
import mapconfig  # noqa: E402
import vectors  # noqa: E402

ROOT = Path(__file__).resolve().parents[2]
HERE = Path(__file__).resolve().parent
OUT = HERE / "tower_track.json"
NAME = "Lookout Tower Track"
CELL = 4            # routing grid, game metres
MARGIN = 200        # search window around the tower-target box
SEARCH_RADIUS = 1500  # only road points this close to the tower are goals
GRADE_REF_DEG = 18  # grades near this cost about 5x flat ground
WATER_CLEAR = 1.5   # never route below WaterLevelY + this
START_OFFSET = 6.0  # the track stops this far from the tower centre (the pad edge)
POINT_SPACING = 8.0
# 16-neighbour moves: straight, diagonal and knight steps, so the route isn't locked to 45 degrees
MOVES = [(-1, 0), (1, 0), (0, -1), (0, 1), (-1, -1), (-1, 1), (1, -1), (1, 1),
         (-2, -1), (-2, 1), (2, -1), (2, 1), (-1, -2), (-1, 2), (1, -2), (1, 2)]


def tower_site(frame):
    """Game XZ of the tower centre and the landmark entry (projection is the pipeline's own)."""
    src = json.loads((HERE / "landmarks.json").read_text(encoding="utf-8"))
    lm = next(m for m in src["landmarks"] if m["id"] == "LookoutTower")
    x, z = (round(float(v), 2) for v in frame.to_game(lm["lat"], lm["lon"]))
    site = lm["site"]
    return (x + site["offsetX"], z + site["offsetZ"]), lm


def load_heights(out_dir):
    m = json.loads((out_dir / "manifest.json").read_text(encoding="utf-8"))
    res, n = m["heightmapResolution"], m["heightmapResolution"] - 1
    y = np.zeros((m["tilesZ"] * n + 1, m["tilesX"] * n + 1), np.float32)
    sha = hashlib.sha256()
    for t in sorted(m["tiles"], key=lambda t: t["file"]):
        raw = (out_dir / t["file"]).read_bytes()
        sha.update(raw)
        a = np.frombuffer(raw, "<u2").reshape(res, res)
        y[t["iz"] * n:t["iz"] * n + res, t["ix"] * n:t["ix"] * n + res] = a / 65535.0 * m["terrainHeight"] + m["terrainBaseY"]
    return y, m, sha.hexdigest()[:16]


def in_box(p, box):
    return box[0] <= p[0] <= box[2] and box[1] <= p[1] <= box[3]


def target_points(frame, road_name, avoid):
    """Centreline points (every CELL/2 m) of the named road, outside the avoid box."""
    fc = json.loads((ROOT / vectors.ROADS).read_text(encoding="utf-8"))
    out = []
    for f in fc["features"]:
        if f["properties"].get("name") != road_name:
            continue
        c = np.array(f["geometry"]["coordinates"])
        x, z = frame.to_game(c[:, 1], c[:, 0])
        out.extend(p for p in densify(np.stack([x, z], axis=1), CELL / 2) if not in_box(p, avoid))
    if not out:
        raise KeyError(f"road {road_name!r} not in {vectors.ROADS} (or wholly inside the avoid box)")
    return np.array(out)


def densify(pts, step):
    out = []
    for a, b in zip(pts[:-1], pts[1:]):
        n = max(1, int(math.ceil(float(np.hypot(*(b - a))) / step)))
        out.extend(a + (b - a) * (k / n) for k in range(n))
    out.append(pts[-1])
    return np.array(out)


def route(y, m, centre, targets, water_y, avoid):
    gx0, gz0 = m["gridOrigin"]
    near = targets[np.hypot(targets[:, 0] - centre[0], targets[:, 1] - centre[1]) <= SEARCH_RADIUS]
    lo = np.floor(np.minimum(near.min(axis=0), centre) - MARGIN)
    hi = np.ceil(np.maximum(near.max(axis=0), centre) + MARGIN)
    lo = np.maximum(lo, [gx0, gz0])
    hi = np.minimum(hi, [gx0 + y.shape[1] - 1, gz0 + y.shape[0] - 1])
    x0, z0 = int(lo[0]), int(lo[1])
    nx, nz = int((hi[0] - x0) // CELL) + 1, int((hi[1] - z0) // CELL) + 1
    xs, zs = x0 + np.arange(nx) * CELL, z0 + np.arange(nz) * CELL
    h = y[np.ix_(zs - gz0, xs - gx0)]

    def cell(p):
        return int(round((p[1] - z0) / CELL)), int(round((p[0] - x0) / CELL))

    goal = {cell(p) for p in near}
    blocked = np.array([[in_box((x, z), avoid) for x in xs] for z in zs])
    src = cell(centre)
    dist = np.full((nz, nx), np.inf)
    dist[src] = 0.0
    prev, heap, end = {}, [(0.0, src)], None
    while heap:
        d, (j, i) = heapq.heappop(heap)
        if d > dist[j, i]:
            continue
        if (j, i) in goal:
            end = (j, i)
            break
        for dj, di in MOVES:
            a, b = j + dj, i + di
            if not (0 <= a < nz and 0 <= b < nx) or blocked[a, b] or h[a, b] < water_y + WATER_CLEAR:
                continue
            length = math.hypot(dj, di) * CELL
            grade = math.degrees(math.atan(abs(float(h[a, b]) - float(h[j, i])) / length))
            cost = d + length * (1 + 4 * (grade / GRADE_REF_DEG) ** 3)
            if cost < dist[a, b]:
                dist[a, b] = cost
                prev[(a, b)] = (j, i)
                heapq.heappush(heap, (cost, (a, b)))
    if end is None:
        raise RuntimeError("no route to the target road")
    cells = [end]
    while cells[-1] in prev:
        cells.append(prev[cells[-1]])
    return np.array([[xs[i], zs[j]] for j, i in reversed(cells)], dtype=np.float64)


def smooth(pts, passes=3):
    """Moving average with fixed ends; takes the grid stair-steps out of the route."""
    p = pts.copy()
    for _ in range(passes):
        q = p.copy()
        q[1:-1] = (p[:-2] + 2 * p[1:-1] + p[2:]) / 4
        p = q
    return p


def resample(pts, step):
    seg = np.hypot(*(pts[1:] - pts[:-1]).T)
    s = np.concatenate([[0.0], np.cumsum(seg)])
    n = max(2, int(round(s[-1] / step)) + 1)
    t = np.linspace(0.0, s[-1], n)
    return np.stack([np.interp(t, s, pts[:, 0]), np.interp(t, s, pts[:, 1])], axis=1)


def trim_start(pts, centre, offset):
    d = np.hypot(pts[:, 0] - centre[0], pts[:, 1] - centre[1])
    k = int(np.argmax(d >= offset))
    a, b = pts[k - 1], pts[k]
    t = (offset - d[k - 1]) / max(d[k] - d[k - 1], 1e-6)
    return np.vstack([a + (b - a) * t, pts[k:]])


def sample(y, m, pts):
    gx0, gz0 = m["gridOrigin"]
    fx, fz = pts[:, 0] - gx0, pts[:, 1] - gz0
    c, r = np.floor(fx).astype(int), np.floor(fz).astype(int)
    u, w = fx - c, fz - r
    return y[r, c] * (1 - u) * (1 - w) + y[r, c + 1] * u * (1 - w) + y[r + 1, c] * (1 - u) * w + y[r + 1, c + 1] * u * w


def build():
    cfg = mapconfig.load(ROOT)
    shore = vectors.load_shoreline(ROOT)
    frame, _ = vectors.make_frame(shore, cfg.horizontalScale)
    centre, lm = tower_site(frame)
    out_dir = ROOT / Path(cfg.manifestPath).parent
    y, m, sha = load_heights(out_dir)
    site = lm["site"]
    targets = target_points(frame, site["trackTargetRoad"], site["trackAvoidBox"])
    raw = route(y, m, np.array(centre), targets, cfg.water_level_y, site["trackAvoidBox"])
    pts = resample(trim_start(smooth(raw), centre, START_OFFSET), POINT_SPACING)
    hs = sample(y, m, pts)
    seg = np.hypot(*(pts[1:] - pts[:-1]).T)
    grade = np.degrees(np.arctan(np.abs(np.diff(hs)) / seg))
    return {
        "name": NAME,
        "note": "Authored by tools/terrain/tower_track.py (least-grade route on the built tiles). Game XZ. Paint only: "
                "the level build does not level the ground along it.",
        "source": {"tileRawSha256_16": sha, "towerCentre": [round(centre[0], 2), round(centre[1], 2)],
                   "targetRoad": site["trackTargetRoad"], "avoidBox": site["trackAvoidBox"], "cell": CELL,
                   "gradeRefDeg": GRADE_REF_DEG,
                   "startOffset": START_OFFSET, "pointSpacing": POINT_SPACING},
        "lengthMeters": round(float(seg.sum()), 1),
        "maxGradeDeg": round(float(grade.max()), 1),
        "meanGradeDeg": round(float(grade.mean()), 1),
        "dropMeters": round(float(hs[0] - hs[-1]), 1),
        "points": [[round(float(a), 2), round(float(b), 2)] for a, b in pts],
    }


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--check", action="store_true", help="recompute and compare with tower_track.json; write nothing")
    args = ap.parse_args()
    data = build()
    text = json.dumps(data, indent=1) + "\n"
    summary = (f"{data['name']}: {len(data['points'])} points, {data['lengthMeters']} m, max grade {data['maxGradeDeg']} deg, "
               f"mean {data['meanGradeDeg']} deg, drop {data['dropMeters']} m, tiles {data['source']['tileRawSha256_16']}")
    if args.check:
        same = OUT.exists() and OUT.read_text(encoding="utf-8") == text
        print(("unchanged: " if same else "DIFFERENT: ") + summary)
        sys.exit(0 if same else 1)
    OUT.write_text(text, encoding="utf-8", newline="\n")
    print(f"wrote {OUT}: {summary}")


if __name__ == "__main__":
    main()
