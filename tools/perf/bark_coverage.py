#!/usr/bin/env python
"""Estimate how much of each still a single brown trunk-bark region fills (issue #9 pose check).

Supporting evidence for the by-eye pose rule (no object within 5 m filling more
than 25% of a compass still). Brown bark only: grey bark (the red tree's trunk)
and foliage are outside the mask, and distance is not measured.

Mask: HSV hue 6-28 deg, saturation 0.30-0.80, value 0.20-0.90; closed (3 iterations),
then the largest connected region as a share of the frame.

Usage:
  python tools/perf/bark_coverage.py <stills_dir> [--spots river_bank river_c1 ...] [--out table.md]
Stills are named <spot>-r1-w00-A-h<000|090|180|270|own>.png.
"""
import argparse
import os
import sys

import numpy as np
from PIL import Image
from scipy import ndimage as ndi

HUE = (6.0, 28.0)
SAT = (0.30, 0.80)
VAL = (0.20, 0.90)
CLOSE_ITER = 3
HEADINGS = ["000", "090", "180", "270", "own"]


def coverage(path):
    hsv = np.array(Image.open(path).convert("RGB").convert("HSV")).astype(float)
    h = hsv[..., 0] * 360.0 / 255.0
    s = hsv[..., 1] / 255.0
    v = hsv[..., 2] / 255.0
    mask = ((h >= HUE[0]) & (h <= HUE[1]) & (s >= SAT[0]) & (s <= SAT[1])
            & (v >= VAL[0]) & (v <= VAL[1]))
    total = mask.mean()
    closed = ndi.binary_closing(mask, iterations=CLOSE_ITER)
    labels, n = ndi.label(closed)
    largest = max((labels == i).sum() for i in range(1, n + 1)) / mask.size if n else 0.0
    return total, largest


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("dir")
    ap.add_argument("--spots", nargs="*", default=["river_bank", "river_c1", "river_c2", "river_c3"])
    ap.add_argument("--out")
    a = ap.parse_args()
    lines = ["| spot | " + " | ".join(HEADINGS) + " |", "|---|" + "---|" * len(HEADINGS)]
    for spot in a.spots:
        cells = []
        for hd in HEADINGS:
            f = os.path.join(a.dir, f"{spot}-r1-w00-A-h{hd}.png")
            if not os.path.exists(f):
                cells.append("missing")
                continue
            t, b = coverage(f)
            cells.append(f"{b * 100:.0f} ({t * 100:.0f})")
        lines.append(f"| {spot} | " + " | ".join(cells) + " |")
    out = "\n".join(lines)
    print(out)
    if a.out:
        with open(a.out, "w", encoding="utf-8", newline="\n") as fh:
            fh.write(out + "\n")


if __name__ == "__main__":
    sys.exit(main())
