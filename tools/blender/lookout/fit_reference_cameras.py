"""Solves the camera of each reference photo from points read off the photo, for render_lookdev.py's SHOTS table.

Run with the system Python (numpy only):  python tools/blender/lookout/fit_reference_cameras.py
It prints 'pos', 'look', 'roll' and 'vfov' per photo; those numbers are pasted into render_lookdev.SHOTS.

Each correspondence is a point of the tower in the design frame (x east, y north, z up; taken from the layout,
so it follows the parameter table) and where it sits in the photo, in pixels of the photo scaled to 1500 x 2000.
The photo is only measured here. The nearest corner in both photos carries the first landing, which is the
model's north-east corner; the face on its left is the east face, the one on its right the north face.
"""
import json
import math
import os
import sys

import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
from mc39_layout import Layout  # noqa: E402

W, H = 1500.0, 2000.0


def corners(L, z):
    a = L.half(z)
    return {"SE": (a, -a, z), "SW": (-a, -a, z), "NE": (a, a, z), "NW": (-a, a, z)}


def correspondences(L):
    zf = L.z_floor
    lv = L.levels
    main = [
        (corners(L, 0.0)["NE"], (905, 1938)), (corners(L, 0.0)["SE"], (372, 1925)), (corners(L, 0.0)["NW"], (1103, 1895)),
        (corners(L, lv[1])["NE"], (890, 1491)), (corners(L, lv[2])["NE"], (878, 1122)), (corners(L, lv[4])["NE"], (853, 658)),
        (corners(L, zf)["NE"], (826, 271)), (corners(L, zf)["SE"], (718, 308)), (corners(L, zf)["NW"], (877, 354)),
        (corners(L, L.z_eave)["NE"], (826, 186)),
    ]
    upper = [
        (corners(L, zf)["NE"], (840, 590)), (corners(L, zf)["SE"], (563, 690)), (corners(L, zf)["NW"], (1030, 748)),
        (corners(L, lv[7])["NE"], (843, 805)), (corners(L, lv[7])["SE"], (522, 940)),
        (corners(L, lv[6])["NE"], (850, 1095)), (corners(L, lv[6])["SE"], (480, 1228)),
        (corners(L, lv[5])["NE"], (862, 1490)), (corners(L, lv[5])["SE"], (418, 1555)),
        (corners(L, L.z_sill)["NE"], (838, 470)), (corners(L, L.z_sill)["SE"], (568, 572)),
    ]
    return {"burns-nhlr-lutes-main.jpg": main, "burns-nhlr-lutes-feb2023.jpg": upper}


def project(p, pts):
    px, py, pz, lx, ly, lz, roll, vfov = p
    pos = np.array([px, py, pz])
    f = np.array([lx, ly, lz]) - pos
    f /= np.linalg.norm(f)
    r = np.cross(f, [0.0, 0.0, 1.0]); r /= np.linalg.norm(r)
    u = np.cross(r, f)
    c, s = math.cos(roll), math.sin(roll)
    r, u = c * r + s * u, -s * r + c * u
    d = pts - pos
    z = d @ f
    fl = (H / 2.0) / math.tan(vfov / 2.0)
    return np.stack([W / 2 + fl * (d @ r) / z, H / 2 - fl * (d @ u) / z], 1)


def fit(pairs, start):
    pts = np.array([a for a, _ in pairs], float)
    obs = np.array([b for _, b in pairs], float)

    def cost(p):
        if p[7] < 0.1 or p[7] > 2.2 or p[2] < 0.5:
            return 1e12
        return float(np.sum((project(p, pts) - obs) ** 2))

    rng = np.random.default_rng(7)
    best, bc = np.array(start, float), cost(np.array(start, float))
    scale = np.array([3.0, 3.0, 0.5, 2.0, 2.0, 3.0, 0.03, 0.2])
    for it in range(60000):
        k = scale * (0.5 ** (it // 6000))
        cand = best + rng.normal(0, 1, 8) * k * (rng.random(8) < 0.5)
        cc = cost(cand)
        if cc < bc:
            best, bc = cand, cc
    return best, math.sqrt(bc / len(pairs))


def main():
    with open(os.path.join(HERE, "mc39_params.json"), "r", encoding="utf-8") as fh:
        L = Layout(json.load(fh))
    starts = {"burns-nhlr-lutes-main.jpg": [17.0, 9.0, 1.6, 0.0, 0.0, 14.0, 0.0, math.radians(67)],
              "burns-nhlr-lutes-feb2023.jpg": [30.0, 19.0, 1.6, 0.0, 0.0, 23.0, 0.0, math.radians(18)]}
    for photo, pairs in correspondences(L).items():
        p, rms = fit(pairs, starts[photo])
        print(f"{photo}: rms {rms:.1f} px (of 1500 x 2000)")
        print(f'  "pos": ({p[0]:.2f}, {p[1]:.2f}, {p[2]:.2f}), "look": ({p[3]:.2f}, {p[4]:.2f}, {p[5]:.2f}), "roll": {math.degrees(p[6]):.2f}, "vfov": {math.degrees(p[7]):.1f}')


if __name__ == "__main__":
    main()
