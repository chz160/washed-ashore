# Derived from w-qa's _bmad-output/poc/water-qa/scum_measure.py (landed WaterRipple.png bytes).
# w-artist variant: softness 0.11, tiles 11.5/14.2, and patches graded WHOLE: a patch is a connected component of the
# threshold field (thr > 0.5) that reaches the band (band >= 0.5); the band only fades opacity, it doesn't cut shapes.
# w-qa W5 scum measurement on the LANDED WaterRipple.png bytes + BellsBendWater.mat values,
# re-implementing BellsBendWater.shader lines 239-257 (scum term only, drift lines reported separately).
# Model: a straight bank along local +u, water at local v > 0 (sd = -v, |grad sd| = 1, so the full stretch applies).
# Grading per ruling/water-scum-size-grading and ruling/water-scum-aspect-metric.
import sys, math, numpy as np
from PIL import Image
from scipy import ndimage

root = sys.argv[1] if len(sys.argv) > 1 else '.'
img = np.asarray(Image.open(root + '/Assets/World/BellsBend/Water/WaterRipple.png').convert('RGBA')).astype(np.float64) / 255.0
img = img[::-1]  # PNG row 0 is the top; Unity texel row 0 is the bottom
H, W = img.shape[:2]
STRETCH, TILE_A, TILE_B = 4.0, 11.5, 14.2
COVER, SOFT, DRIFT_C = 0.33, 0.11, 0.25
SCUM_BAND = (-3.5, -2.8, -1.4, -0.9); DRIFT = (-5.0, 0.7); SD_JITTER = 0.6
ROT = np.array([[0.7986, -0.6018], [0.6018, 0.7986]])
CELL = 0.02


def sample(ch, x, z):
    # bilinear, repeat wrap, uv = world / tile
    u = x * W - 0.5; v = z * H - 0.5
    x0 = np.floor(u).astype(int); y0 = np.floor(v).astype(int); fx = u - x0; fy = v - y0
    def px(i, j): return img[np.mod(j, H), np.mod(i, W), ch]
    return (px(x0, y0) * (1 - fx) * (1 - fy) + px(x0 + 1, y0) * fx * (1 - fy)
            + px(x0, y0 + 1) * (1 - fx) * fy + px(x0 + 1, y0 + 1) * fx * fy)


def smooth(a, b, x):
    t = np.clip((x - a) / (b - a), 0, 1); return t * t * (3 - 2 * t)


def field(origin, ang, phase_blend):
    t = np.array([math.cos(ang), math.sin(ang)]); n = np.array([-t[1], t[0]])  # n points out to the water
    us = np.arange(0, 20, CELL); vs = np.arange(0, 6, CELL)
    U, V = np.meshgrid(us, vs)
    P = origin[:, None, None] + t[:, None, None] * U + n[:, None, None] * V
    sd = -V
    across = -n  # grad sd points inland
    F = P + across[:, None, None] * (sd * (STRETCH - 1.0))
    def layers(off):
        fa_b = sample(2, (F[0] - off[0]) / TILE_A, (F[1] - off[1]) / TILE_A)
        fa_a = sample(3, (F[0] - off[0]) / TILE_A, (F[1] - off[1]) / TILE_A)
        R = np.tensordot(ROT, F, axes=1); ro = ROT @ off
        fb_a = sample(3, (R[0] - ro[0]) / TILE_B, (R[1] - ro[1]) / TILE_B)
        fb_b = sample(2, (R[0] - ro[0]) / TILE_B, (R[1] - ro[1]) / TILE_B)
        return fa_b, fa_a, fb_a, fb_b
    l0 = layers(np.zeros(2))
    if phase_blend:  # w0 = 0.5: two flow phases half a cycle apart (0.25 m drift difference), averaged
        l1 = layers(t * 0.25)
        l0 = tuple(0.5 * (a + b) for a, b in zip(l0, l1))
    fa_b, fa_a, fb_a, fb_b = l0
    scumN = 0.5 * (fa_b + fb_a); driftN = 0.5 * (fa_a + fb_b)
    sdJ = sd + (fb_b - 0.5) * 2 * SD_JITTER
    band = smooth(SCUM_BAND[0], SCUM_BAND[1], sdJ) * (1 - smooth(SCUM_BAND[2], SCUM_BAND[3], sdJ))
    dband = 1 - smooth(0, DRIFT[1], np.abs(sdJ - DRIFT[0]))
    tS = 1 - math.sqrt(COVER * 0.5); tD = 1 - math.sqrt(DRIFT_C * 0.5)
    thr = smooth(tS - SOFT, tS + SOFT, scumN)
    lab, k = ndimage.label(thr > 0.5)
    keep = np.zeros(k + 1, bool); keep[np.unique(lab[(band >= 0.5) & (lab > 0)])] = True; keep[0] = False
    scum = thr * keep[lab]
    drift = dband * smooth(tD - SOFT, tD + SOFT, driftN)
    return scum, drift, band, dband


def patches(m):
    lab, k = ndimage.label(m > 0.5)
    out = []
    for i in range(1, k + 1):
        ys, xs = np.nonzero(lab == i)
        if len(xs) < 20: continue
        L = (xs.max() - xs.min() + 1) * CELL; A = len(xs) * CELL * CELL
        c = np.cov(np.vstack([xs, ys]).astype(float)) if len(xs) > 2 else np.eye(2)
        ev = np.sort(np.linalg.eigvalsh(c)); ev[0] = max(ev[0], 1e-9)
        out.append((L, A / L, math.sqrt(ev[1] / ev[0]), L / (A / L)))
    return np.array(out)


def edge_share(m, pt):
    # 10-90 % ramp width across the bank at the patch boundary, as a share of median patch width
    g = np.abs(np.gradient(m, CELL, axis=0))
    sel = (m > 0.3) & (m < 0.7) & (g > 1e-6)
    ramp = 0.8 / np.median(g[sel]) if sel.any() else float('nan')
    return ramp, ramp / np.median(pt[:, 1])


rng = np.random.default_rng(20261006)
for blend in (False,):
    allp = []; covs = []; dp = []; ramps = []
    for s in range(12):
        origin = rng.uniform(-2000, 2000, 2); ang = rng.uniform(0, 2 * math.pi)
        scum, drift, band, dband = field(origin, ang, blend)
        p = patches(scum)
        if len(p): allp.append(p); ramps.append(edge_share(scum, p)[0])
        inb = band > 0.5
        covs.append(float((scum[inb] > 0.5).mean()))
        d = patches(drift)
        if len(d): dp.append(d)
    P = np.vstack(allp); D = np.vstack(dp) if dp else np.zeros((0, 4))
    def q(a): return f"{np.median(a):.2f} (p10-p90 {np.percentile(a, 10):.2f}-{np.percentile(a, 90):.2f})"
    print(f"== phase blend w0={'0.5' if blend else '1.0'}: {len(P)} scum patches over 12 windows (20 x 6 m, 2 cm)")
    print(' length m      ', q(P[:, 0]))
    print(' width m       ', q(P[:, 1]))
    print(' moment aspect ', q(P[:, 2]), '  [graded: median 4-6]')
    print(' len/meanwidth ', q(P[:, 3]), '  [reported only]')
    print(f" band coverage  {np.median(covs):.2f} (min {min(covs):.2f}, max {max(covs):.2f})  [bar 0.25-0.40]")
    ramp = float(np.median(ramps)); print(f" edge ramp 10-90% {ramp:.3f} m = {ramp / np.median(P[:, 1]):.0%} of median width  [bar >= 30%]")
    if len(D): print(' drift streaks: length', q(D[:, 0]), 'width', q(D[:, 1]), 'aspect', q(D[:, 2]))
