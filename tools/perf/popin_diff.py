#!/usr/bin/env python
"""Pixel-diff helper for the #9 visible-difference review (trial still against its in-frame authored twin).

For each <spot>-r<run>-w<NN>-FAR-h<heading>.png it finds localized changes: pixels whose max channel
delta is over 24/255, opened (2 iterations) to drop speckle, dilated (6), then connected regions of at
least 150 px. Each region is reported as area, bbox and peak delta.

Verdict rule used by technical-artist for FAR (a judgement rule, not an engine fact):
  FAIL when a region of >= 500 px with peak delta >= 40 lies in the skyline zone (top of bbox above y 700 of
  1080), because a flip between the two stills shows a changed silhouette. Soft near-field changes
  (shadow-edge shifts, <= ~48/255) are reported but do not fail.

Usage:
  python tools/perf/popin_diff.py <run_dir> [--runs r1 r2 r3] [--variant FAR] [--skip cab]
  python tools/perf/popin_diff.py <run_dir> --composite <still.png> <out.png> [x0,y0,x1,y1]
    (composite: authored on top, trial in the middle, |delta| x8 below)
"""
import argparse
import glob
import os
import sys

import numpy as np
from PIL import Image
from scipy import ndimage as ndi

DELTA = 24
MIN_AREA = 150
FAIL_AREA = 500
FAIL_PEAK = 40
SKY_ZONE_Y = 700


def regions(still):
    t = np.array(Image.open(still).convert("RGB")).astype(int)
    u = np.array(Image.open(still[:-4] + "-authored.png").convert("RGB")).astype(int)
    dd = np.abs(t - u).max(axis=2)
    m = ndi.binary_dilation(ndi.binary_opening(dd > DELTA, iterations=2), iterations=6)
    lab, _ = ndi.label(m)
    out = []
    for i, sl in enumerate(ndi.find_objects(lab), 1):
        area = int((lab[sl] == i).sum())
        if area >= MIN_AREA:
            out.append((area, sl[1].start, sl[0].start, sl[1].stop, sl[0].stop, int(dd[sl].max())))
    return sorted(out, reverse=True), int((dd > 8).sum())


def verdict(regs):
    bad = [r for r in regs if r[0] >= FAIL_AREA and r[5] >= FAIL_PEAK and r[2] < SKY_ZONE_Y]
    return ("FAIL", bad) if bad else ("PASS", [])


def composite(still, out, box):
    t = Image.open(still).convert("RGB").crop(box)
    u = Image.open(still[:-4] + "-authored.png").convert("RGB").crop(box)
    d = np.abs(np.array(t).astype(int) - np.array(u).astype(int)).max(axis=2)
    dm = Image.fromarray(np.clip(d * 8, 0, 255).astype("uint8")).convert("RGB")
    w, h = t.size
    c = Image.new("RGB", (w, h * 3))
    c.paste(u, (0, 0))
    c.paste(t, (0, h))
    c.paste(dm, (0, h * 2))
    c.save(out)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("run_dir")
    ap.add_argument("--runs", nargs="*", default=["r1", "r2", "r3"])
    ap.add_argument("--variant", default="FAR")
    ap.add_argument("--skip", nargs="*", default=["cab"])
    ap.add_argument("--composite", nargs="*")
    a = ap.parse_args()
    if a.composite:
        box = tuple(int(v) for v in a.composite[2].split(",")) if len(a.composite) > 2 else (0, 0, 1920, 1080)
        composite(a.composite[0], a.composite[1], box)
        return 0
    print("| still | px>8 | verdict | regions (area, x0,y0,x1,y1, peak) |")
    print("|---|---|---|---|")
    for run in a.runs:
        pat = os.path.join(a.run_dir, f"s*-{run}-*", f"*-{a.variant}-h*.png")
        for f in sorted(glob.glob(pat)):
            if f.endswith("-authored.png") or f.endswith("-mask.png"):
                continue
            spot = os.path.basename(f).split("-")[0]
            if spot in a.skip:
                continue
            regs, n8 = regions(f)
            v, bad = verdict(regs)
            show = ", ".join(f"({r[0]},{r[1]},{r[2]},{r[3]},{r[4]},{r[5]})" for r in regs[:4]) or "none"
            print(f"| {os.path.basename(f)[:-4]} | {n8} | {v} | {show} |")
    return 0


if __name__ == "__main__":
    sys.exit(main())
