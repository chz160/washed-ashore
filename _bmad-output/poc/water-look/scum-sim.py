"""Offline scum patch-size simulation (w-artist, water pod W5; ruling water-scum-aspect-metric).

Re-implements the shader's scum term on a 2 cm grid over a 20 x 6 m bank window (x along the bank, y across),
with random-seed noise of the same spectra as BellsBendWaterMaps.RippleBytes (not the landed bytes; w-qa
measures those). Reports median (p10-p90) of patch length, mean width (area / length), moment axis ratio
sqrt(lmax / lmin) of pixel covariance (graded, median 4-6) and length / mean width (reported only).
Usage: python scum-sim.py [stretch ...]   (default: 3.45 3.6 4.0 4.2)
"""
import math
import sys

import numpy as np
from scipy import ndimage

B_K, A_K = (9, 14), (8, 13)   # RippleBytes: B and A flat spectra, 32 waves each (cycles per tile)
TILES = (13.0, 16.0)          # _FoamTiling
ROT_DEG = 37.0                # second layer rotation
COVERAGE, SOFT = 0.33, 0.11   # _ScumCoverage, _FoamSoftness (0.07 until slot #10)
SEEDS = range(3, 15)


def layer(seed, kmin, kmax, n=256, count=32):
    rng = np.random.default_rng(seed)
    v = np.zeros((n, n))
    yy, xx = np.mgrid[0:n, 0:n]
    for _ in range(count):
        while True:
            kx, kz = rng.integers(-kmax, kmax + 1, 2)
            if kmin * kmin <= kx * kx + kz * kz <= kmax * kmax:
                break
        v += np.sin(2 * math.pi / n * (kx * xx + kz * yy) + rng.random() * 2 * math.pi)
    return (v.ravel().argsort().argsort() / (v.size - 1)).reshape(n, n)  # histogram-equalised


def sample(tex, tile, x, z):
    n = tex.shape[0]
    u, w = (x / tile * n) % n, (z / tile * n) % n
    i0, j0 = np.floor(u).astype(int), np.floor(w).astype(int)
    fu, fw = u - i0, w - j0
    i1, j1 = (i0 + 1) % n, (j0 + 1) % n
    return (tex[j0, i0] * (1 - fu) * (1 - fw) + tex[j0, i1] * fu * (1 - fw)
            + tex[j1, i0] * (1 - fu) * fw + tex[j1, i1] * fu * fw)


def run(stretch, layers):
    rows, covs = [], []
    c, s = math.cos(math.radians(ROT_DEG)), math.sin(math.radians(ROT_DEG))
    for b, a in layers:
        x, y = np.meshgrid(np.arange(0, 20, 0.02), np.arange(0, 6, 0.02))
        ys = y * stretch
        noise = 0.5 * (sample(b, TILES[0], x, ys) + sample(a, TILES[1], c * x - s * ys, s * x + c * ys))
        t = 1 - math.sqrt(COVERAGE / 2)
        f = np.clip((noise - (t - SOFT)) / (2 * SOFT), 0, 1)
        f = f * f * (3 - 2 * f)
        covs.append(np.mean(f > 0.5))
        lab, _ = ndimage.label(f > 0.5)
        for i, sl in enumerate(ndimage.find_objects(lab)):
            m = lab[sl] == i + 1
            if m.sum() < 20:
                continue
            py, px = np.nonzero(m)
            ev = np.linalg.eigvalsh(np.cov(np.vstack([px, py]) * 0.02))
            length = (sl[1].stop - sl[1].start) * 0.02
            width = m.sum() * 0.0004 / length
            rows.append((length, width, math.sqrt(ev[1] / max(ev[0], 1e-9)), length / width))
    r = np.array(rows)

    def q(j):
        return f"{np.median(r[:, j]):.2f} ({np.percentile(r[:, j], 10):.2f}-{np.percentile(r[:, j], 90):.2f})"
    return (f"stretch {stretch}: coverage {np.mean(covs):.2f} | length m {q(0)} | width m {q(1)} | "
            f"moment axis {q(2)} | length/width {q(3)}")


if __name__ == "__main__":
    stretches = [float(a) for a in sys.argv[1:]] or [3.45, 3.6, 4.0, 4.2]
    layers = [(layer(sd, *B_K), layer(sd + 100, *A_K)) for sd in SEEDS]
    print(f"noise B k{B_K} A k{A_K}, tiles {TILES} m, rot {ROT_DEG} deg, coverage {COVERAGE}, edge +-{SOFT}, seeds {len(SEEDS)}")
    for st in stretches:
        print(run(st, layers))
