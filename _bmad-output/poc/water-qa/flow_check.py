# w-qa independent W5 flow-direction check: shader drift dir (BellsBendWater.shader lines 166-185) from WaterMaps.png,
# vs local downstream bank tangent, anchored to real flow (CCW bank => downstream = -bank direction).
import json, math, sys, numpy as np
from PIL import Image
root = sys.argv[1] if len(sys.argv) > 1 else '.'
v = json.load(open(root + '/Data/terrain/build/map_vectors.json'))
bank = np.array(v['innerBank'], float)
img = np.asarray(Image.open(root + '/Assets/World/BellsBend/Water/WaterMaps.png')).astype(float)
H, W = img.shape[:2]
ST = (0.00024414062, 0.0001953125, 0.5095215, 0.48671874); FLOW_SIGN = 1.0; NORTH_Z = 1986.7; NECK_X = -414.12; BLEND = 30.0
FALLBACK = np.array([0.0, -1.0]); FLIP = None


def tex(u, vv, ch):
    x = u * W - 0.5; y = vv * H - 0.5
    x0 = int(math.floor(x)); y0 = int(math.floor(y)); fx = x - x0; fy = y - y0

    def px(i, j):
        i = min(max(i, 0), W - 1); j = min(max(j, 0), H - 1)
        r = (H - 1 - j) if FLIP else j
        return img[r, i, ch] / 255.0
    return (px(x0, y0) * (1 - fx) * (1 - fy) + px(x0 + 1, y0) * fx * (1 - fy)
            + px(x0, y0 + 1) * (1 - fx) * fy + px(x0 + 1, y0 + 1) * fx * fy)


def uv(p): return (p[0] * ST[0] + ST[2], p[1] * ST[1] + ST[3])
def sd(p): u, vv = uv(p); return tex(u, vv, 0) * 255.0 - 128.0


for FLIP in (True, False):
    if sd((0.0, 0.0)) > 0 and sd((-1800.0, -2300.0)) < 0:
        break
print('row flip', FLIP, 'sd(centroid)', round(sd((0, 0)), 1), 'sd(far SW)', round(sd((-1800, -2300)), 1))


def smooth(a, b, x):
    t = min(max((x - a) / (b - a), 0), 1); return t * t * (3 - 2 * t)


def flow(p):
    u, vv = uv(p); s = tex(u, vv, 0) * 255 - 128
    g = np.array([tex(u + 2 * ST[0], vv, 0) * 255 - 128 - s, tex(u, vv + 2 * ST[1], 0) * 255 - 128 - s]) * 0.5
    gl = np.linalg.norm(g); tan = np.array([-g[1], g[0]]) * (FLOW_SIGN / max(gl, 1e-4))
    v2 = FALLBACK + (tan - FALLBACK) * min(max(gl * 2, 0), 1)
    d = v2 / math.sqrt(max(v2 @ v2, 1e-6))
    nW = smooth(NORTH_Z - BLEND, NORTH_Z + BLEND, p[1])
    ch = np.array([0.0, FLOW_SIGN if p[0] < NECK_X else -FLOW_SIGN])
    v3 = d + (ch - d) * nW
    return v3 / max(np.linalg.norm(v3), 1e-6), s, gl


seg = np.linalg.norm(np.diff(bank, axis=0), axis=1); cum = np.concatenate([[0], np.cumsum(seg)]); L = cum[-1]
rows = []; against = []
for k in range(80):
    s_arc = 22 + (L - 44) * (k + 0.5) / 80
    i = int(np.searchsorted(cum, s_arc) - 1); t = (s_arc - cum[i]) / seg[i]
    p = bank[i] + t * (bank[i + 1] - bank[i])
    tdir = (bank[i + 1] - bank[i]) / seg[i]
    down = -tdir
    outward = np.array([tdir[1], -tdir[0]])
    q = p.copy()
    for _ in range(60):
        if sd(q) <= -10: break
        q = q + outward * 0.5
    f, s, gl = flow(q)
    c = float(f @ down)
    rows.append((k, q[0], q[1], s, f[0], f[1], down[0], down[1], c))
    if c <= 0: against.append(rows[-1])
area = 0.5 * np.sum(bank[:, 0] * np.roll(bank[:, 1], -1) - np.roll(bank[:, 0], -1) * bank[:, 1])
print('bank orientation', 'CCW' if area > 0 else 'CW')
print('stations', len(rows), 'downstream (cos>0):', sum(r[8] > 0 for r in rows), 'min cos %.2f' % min(r[8] for r in rows),
      'median cos %.2f' % float(np.median([r[8] for r in rows])))
for r in against: print('AGAINST', ['%.1f' % x for x in r])
print('sign flips between neighbours', sum(1 for a, b in zip(rows, rows[1:]) if (a[8] > 0) != (b[8] > 0)))
zmin = min(r[2] for r in rows)


def anchor(name, sel, comp):
    ok = [r for r in rows if sel(r)]; good = [r for r in ok if comp(r)]
    print('anchor', name, f'{len(good)}/{len(ok)}')


anchor('east side heads south', lambda r: r[1] > 150 and r[2] > 800, lambda r: r[5] < 0)
anchor('west side near neck heads north', lambda r: r[1] < -900 and r[2] > 1500, lambda r: r[5] > 0)
anchor('south tip heads west', lambda r: r[2] < zmin + 300, lambda r: r[4] < 0)
for x, z in [(-1255, 1866), (-1085, 1674), (257, 1423), (497, 1866)]:
    f, s, gl = flow(np.array([x, z], float)); print('artist station', (x, z), 'sd %.1f' % s, 'flow (%.2f, %.2f)' % tuple(f))
