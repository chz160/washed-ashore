"""Horizon metrics on look shots, mirroring w-level's BellsBendSky metrics (water pod W5, ruling water-haze-hue).

  row step          = largest mean (over the centre third of columns, every 4th px) of the max-channel step between
                      adjacent rows (lower is softer); "horizon zone" restricts it to rows 380-560 (the horizon for
                      eye heights of 2-10 m at FOV 60), so near scum edges don't mask the horizon
  lower third diff  = mean |a - b| over channels in the bottom third (near water) between two shots of one pose
  upper sky diff    = the same over the top quarter (w-director's limit 0.05)
  blue excess       = mean (b - max(r, g)) over the bottom half (water in these framings); > 0 is a blue cast
Usage: python horizon-metrics.py <shot.png> [<reference.png>]
"""
import sys

import numpy as np
from PIL import Image


def load(path):
    return np.asarray(Image.open(path).convert("RGB"), dtype=np.float64) / 255.0  # row 0 = top


def horizon_step(px, band=None):
    """Whole image (w-level's measure), or only rows band=(top, bottom): the horizon zone, excluding near scum edges."""
    h, w, _ = px.shape
    cols = px[:, w // 3: 2 * w // 3: 4, :]
    step = np.abs(cols[1:] - cols[:-1]).max(axis=2).mean(axis=1)
    lo, hi = band if band else (0, len(step))
    seg = step[lo:hi]
    return float(seg.max()), lo + int(seg.argmax())


def mean_diff(a, b, rows):
    return float(np.abs(a[rows] - b[rows]).mean())


def blue_excess(px):
    h = px.shape[0]
    lower = px[h // 2:]
    return float((lower[..., 2] - lower[..., :2].max(axis=2)).mean())


if __name__ == "__main__":
    shot = load(sys.argv[1])
    h = shot.shape[0]
    step, row = horizon_step(shot)
    zstep, zrow = horizon_step(shot, (380, 560))
    print(f"{sys.argv[1]}: row step whole={step:.3f} (row {row}); horizon zone rows 380-560={zstep:.3f} (row {zrow}); blue excess={blue_excess(shot):.3f}")
    if len(sys.argv) > 2:
        ref = load(sys.argv[2])
        rstep, rrow = horizon_step(ref)
        rz, rzr = horizon_step(ref, (380, 560))
        print(f"  reference {sys.argv[2]}: row step whole={rstep:.3f} (row {rrow}); horizon zone={rz:.3f} (row {rzr}); blue excess={blue_excess(ref):.3f}")
        print(f"  lower third diff={mean_diff(shot, ref, slice(2 * h // 3, h)):.4f}; upper quarter (sky) diff={mean_diff(shot, ref, slice(0, h // 4)):.4f}")
