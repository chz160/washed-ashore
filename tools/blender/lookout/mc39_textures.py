"""Generates the MC-39 atlas (albedo + alpha, normal, mask) from seeded noise. numpy and zlib only.

No downloaded texture, scan or photograph goes in: every pixel comes from the functions below, so the build needs
no network and is repeatable. The look targets come from the director's notes: paint that has had 35 years of
neglect but still reads pale from far off, rust at joints and streaking down the cab panels, grey split planks,
dirty glass, tarnished brass, old stained concrete.

Mask channels follow URP Lit's metallic map: R = metallic, G = occlusion, A = smoothness.
"""
import math
import os
import struct
import zlib

import numpy as np

from mc39_atlas import ATLAS_PX, N_STEEL, N_WOOD, REGIONS

PAINT = np.array([0.80, 0.80, 0.76])
GALV = np.array([0.50, 0.52, 0.52])
RUST_LIGHT = np.array([0.56, 0.36, 0.20])
RUST_DARK = np.array([0.26, 0.13, 0.07])
WOOD_GREY = np.array([0.50, 0.49, 0.46])


def write_png(path, rgba8):
    """8-bit RGBA PNG, fixed compression settings, no ancillary chunks (so the bytes depend on the pixels only)."""
    h, w, c = rgba8.shape
    assert c == 4 and rgba8.dtype == np.uint8
    raw = np.empty((h, w * 4 + 1), dtype=np.uint8)
    raw[:, 0] = 0
    raw[:, 1:] = rgba8.reshape(h, w * 4)

    def chunk(tag, data):
        return struct.pack(">I", len(data)) + tag + data + struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF)
    with open(path, "wb") as fh:
        fh.write(b"\x89PNG\r\n\x1a\n")
        fh.write(chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 6, 0, 0, 0)))
        fh.write(chunk(b"IDAT", zlib.compress(raw.tobytes(), 6)))
        fh.write(chunk(b"IEND", b""))


def smooth(t):
    return t * t * (3.0 - 2.0 * t)


def noise(rng, h, w, cell_y, cell_x):
    """Value noise in 0..1 with the given cell size in pixels (cells may be stretched)."""
    gy, gx = int(h / cell_y) + 2, int(w / cell_x) + 2
    g = rng.random((gy, gx))
    y = np.arange(h) / cell_y
    x = np.arange(w) / cell_x
    y0, x0 = y.astype(int), x.astype(int)
    fy, fx = smooth(y - y0)[:, None], smooth(x - x0)[None, :]
    a = g[y0][:, x0]; b = g[y0][:, x0 + 1]; c = g[y0 + 1][:, x0]; d = g[y0 + 1][:, x0 + 1]
    return (a * (1 - fx) + b * fx) * (1 - fy) + (c * (1 - fx) + d * fx) * fy


def fbm(rng, h, w, cell_y, cell_x, octaves=4):
    out = np.zeros((h, w))
    amp, tot = 1.0, 0.0
    for _ in range(octaves):
        out += amp * noise(rng, h, w, max(cell_y, 1.0), max(cell_x, 1.0))
        tot += amp
        amp *= 0.5
        cell_y *= 0.5
        cell_x *= 0.5
    return out / tot


def ramp(x, lo, hi):
    return np.clip((x - lo) / (hi - lo), 0.0, 1.0)


def cover(field, frac, soft=0.035):
    """Soft mask over the top `frac` of a noise field (noise sums bunch round 0.5, so thresholds go by quantile)."""
    thr = float(np.quantile(field, 1.0 - min(max(frac, 0.0005), 0.9995)))
    return ramp(field, thr - soft, thr + soft)


def zscore(field):
    return (field - field.mean()) / (field.std() + 1e-9)


def mix(a, b, t):
    return a * (1.0 - t[..., None]) + b * t[..., None]


def smear_down(m, decay):
    """Carry a mask down the image with exponential decay: rust and dirt streaking under their source."""
    out = m.copy()
    for y in range(1, m.shape[0]):
        out[y] = np.maximum(out[y], out[y - 1] * decay)
    return out


class Tile:
    """One region being painted: colour, alpha, metallic, smoothness, height."""

    def __init__(self, h, w):
        self.rgb = np.zeros((h, w, 3)); self.a = np.ones((h, w))
        self.metal = np.zeros((h, w)); self.sm = np.full((h, w), 0.2); self.ht = np.zeros((h, w))


def painted_steel(rng, h, w, rust_level, decay):
    """Paint over galvanised steel. rust_level 0..1 is how far the rust has got."""
    t = Tile(h, w)
    grime = fbm(rng, h, w, 40, 160, 4)
    flake = fbm(rng, h, w, 14, 30, 4)
    fine = fbm(rng, h, w, 3, 3, 2)
    paint = PAINT[None, None, :] * (0.86 + 0.16 * grime[..., None]) * (0.97 + 0.06 * fine[..., None])
    # flakes are broad and soft-edged, so their borders span a few texels and don't read as pixels close up
    peel = cover(flake + 0.06 * fine, 0.07 + 0.16 * decay * (0.5 + rust_level), 0.05)
    col = mix(paint, GALV[None, None, :] * (0.9 + 0.2 * fine[..., None]), peel)
    rust = fbm(rng, h, w, 18, 26, 5) + 0.05 * fine
    return t, col, peel, rust, fine


def tile_steel_strip(rng, h, w, level, decay):
    """Angle iron, u along the member with the joint at u = 0: rust gathers at the joint and under the bolts."""
    t, col, peel, rust, fine = painted_steel(rng, h, w, level, decay)
    u = np.arange(w)[None, :] / w
    near = np.exp(-u / (0.025 + 0.05 * level))                    # 4.2 m strip: the first 10 to 30 cm
    # rust belongs to the joint: away from it only the rustiest strips keep a few specks
    z = zscore(rust) + near * (1.6 + 3.0 * level) * (0.4 + 0.6 * decay)
    thr = 3.3 - 0.7 * level * decay
    core = ramp(z, thr, thr + 0.6)
    halo = ramp(z, thr - 1.1, thr + 0.3)
    stain = np.maximum(halo, smear_right(core, 0.985) * 0.5)
    col = mix(col, RUST_LIGHT[None, None, :], 0.55 * stain)
    col = mix(col, mix(np.broadcast_to(RUST_LIGHT, col.shape).copy(), np.broadcast_to(RUST_DARK, col.shape).copy(), fine), core)
    # two bolts at the joint end
    yy, xx = np.mgrid[0:h, 0:w]
    for bx, by in ((22, h * 0.3), (46, h * 0.7)):
        d = np.hypot(xx - bx, yy - by)
        head = ramp(7.0 - d, 0.0, 1.5)
        ring = ramp(13.0 - d, 0.0, 6.0) * (1 - head)
        col = mix(col, RUST_DARK[None, None, :] * 1.15, head)
        col = mix(col, RUST_LIGHT[None, None, :], 0.6 * ring * decay)
        t.ht += head * 1.0
    t.rgb = col
    t.sm = 0.24 - 0.14 * np.maximum(core, peel * 0.3)
    t.metal = 0.55 * peel * (1 - core)
    t.ht += -0.5 * core * fine + 0.25 * (1 - peel)
    return t


def smear_right(m, decay):
    out = m.copy()
    for x in range(1, m.shape[1]):
        out[:, x] = np.maximum(out[:, x], out[:, x - 1] * decay)
    return out


def tile_wood(rng, h, w, decay):
    t = Tile(h, w)
    grain = fbm(rng, h, w, 2.5, 220, 4)
    band = fbm(rng, h, w, 9, 500, 3)
    tone = fbm(rng, h, w, 30, 300, 3)
    col = WOOD_GREY[None, None, :] * (0.62 + 0.55 * (0.6 * grain + 0.4 * band))[..., None]
    col = col * (0.85 + 0.3 * tone[..., None])
    # weather checks: thin dark cracks along the grain
    cr = fbm(rng, h, w, 1.6, 140, 2)
    crack = cover(cr, 0.05 + 0.07 * decay, 0.006) * cover(fbm(rng, h, w, 20, 90, 2), 0.6, 0.05)
    col = mix(col, np.array([0.10, 0.09, 0.08])[None, None, :], 0.85 * crack)
    # pale lichen and a green cast in patches
    li = cover(fbm(rng, h, w, 14, 26, 4), 0.07)
    col = mix(col, np.array([0.55, 0.58, 0.47])[None, None, :], 0.5 * li * decay)
    # dark, wet ends
    u = np.arange(w)[None, :] / w
    ends = np.maximum(ramp(0.035 - u, 0.0, 0.035), ramp(u - 0.965, 0.0, 0.035))
    col = col * (1.0 - 0.45 * ends[..., None])
    t.rgb = col
    t.sm[:] = 0.08
    t.ht = 0.6 * grain - 1.2 * crack
    return t


def tile_panel(rng, h, w, decay, inside=False):
    """Cab lower wall: galvanised sheets with flat-lock seams. Row 0 is the sill; rust streaks run down from it."""
    t, col, peel, rust, fine = painted_steel(rng, h, w, 0.7, decay)
    if inside:
        base = np.array([0.50, 0.55, 0.50])[None, None, :] * (0.8 + 0.3 * fbm(rng, h, w, 50, 60, 3)[..., None])
        col = mix(base, GALV[None, None, :], peel)
    yy, xx = np.mgrid[0:h, 0:w]
    seam = np.zeros((h, w))
    for sx in (w // 4, w // 2, 3 * w // 4):
        seam = np.maximum(seam, ramp(2.0 - np.abs(xx - sx), 0.0, 1.0))
    src = np.zeros((h, w))
    src[:8] = cover(fbm(rng, 8, w, 3, 7, 2), 0.55, 0.05)
    src = np.maximum(src, seam * cover(fbm(rng, h, w, 14, 3, 2), 0.30, 0.05))
    bolts = (rng.random((h, w)) > 0.9990) & (yy < h * 0.85)
    src = np.maximum(src, bolts.astype(float))
    amt = 0.55 if inside else 1.0
    streak = smear_down(src, 0.991) * amt
    streak *= 0.45 + 0.55 * fbm(rng, h, w, 80, 4, 2)
    streak = np.clip(streak * 1.25, 0, 1)
    patch = cover(rust + 0.30 * ramp(yy / h, 0.75, 1.0), 0.01 + 0.03 * decay, 0.05) * amt
    foot = ramp(yy / h, 0.92, 1.0) * cover(fbm(rng, h, w, 6, 14, 3), 0.6, 0.05)
    core = np.clip(patch + foot + 0.8 * ramp(streak, 0.75, 1.0), 0, 1)
    col = mix(col, RUST_LIGHT[None, None, :], 0.9 * np.clip(streak, 0, 1) * (0.5 + 0.5 * decay))
    col = mix(col, mix(np.broadcast_to(RUST_LIGHT, col.shape).copy(), np.broadcast_to(RUST_DARK, col.shape).copy(), fine), core)
    col = col * (1.0 - 0.5 * seam[..., None])
    t.rgb = col
    t.sm = 0.22 - 0.12 * core
    t.metal = 0.5 * peel * (1 - core)
    t.ht = -0.8 * seam - 0.4 * core * fine
    return t


def tile_roof(rng, h, w, decay, under=False):
    t = Tile(h, w)
    fine = fbm(rng, h, w, 3, 3, 2)
    base = np.array([0.36, 0.36, 0.35]) if not under else np.array([0.15, 0.145, 0.14])
    col = base[None, None, :] * (0.75 + 0.5 * fbm(rng, h, w, 40, 40, 4)[..., None])
    rust = fbm(rng, h, w, 20, 20, 5)
    core = cover(rust, 0.05 + 0.10 * decay, 0.05)
    yy, xx = np.mgrid[0:h, 0:w]
    seam = np.zeros((h, w))
    for sx in range(w // 6, w, w // 3):
        seam = np.maximum(seam, ramp(2.0 - np.abs(xx - sx), 0.0, 1.0))
    core = np.clip(core + 0.7 * smear_down(seam * cover(fbm(rng, h, w, 8, 3, 2), 0.15, 0.03), 0.98), 0, 1)
    rc = mix(np.broadcast_to(RUST_LIGHT * 0.8, col.shape).copy(), np.broadcast_to(RUST_DARK, col.shape).copy(), fine)
    col = mix(col, rc * (0.6 if under else 1.0), core * (0.8 if under else 1.0))
    t.rgb = col * (1.0 - 0.4 * seam[..., None])
    t.sm = 0.2 - 0.1 * core
    t.metal = 0.5 * (1 - core)
    t.ht = 0.9 * seam - 0.4 * core * fine
    return t


def tile_concrete(rng, h, w, decay):
    t = Tile(h, w)
    col = np.array([0.50, 0.49, 0.45])[None, None, :] * (0.72 + 0.4 * fbm(rng, h, w, 50, 50, 5)[..., None])
    speck = rng.random((h, w))
    col = col * (1.0 - 0.25 * (speck > 0.93)[..., None]) + 0.08 * (speck < 0.04)[..., None]
    moss = cover(fbm(rng, h, w, 18, 18, 4), 0.14, 0.03) * decay
    col = mix(col, np.array([0.20, 0.25, 0.14])[None, None, :], 0.7 * moss)
    yy, xx = np.mgrid[0:h, 0:w]
    d = np.hypot(xx - w * 0.5, yy - h * 0.5) / w
    stain = ramp(0.33 - d, 0.0, 0.3) * cover(fbm(rng, h, w, 12, 12, 3), 0.5, 0.08)
    col = mix(col, RUST_LIGHT[None, None, :] * 0.9, 0.5 * stain * decay)
    crack = ramp(np.abs(fbm(rng, h, w, 70, 70, 3) - 0.5), 0.004, 0.0)
    t.rgb = col * (1.0 - 0.55 * crack[..., None])
    t.sm[:] = 0.07
    t.ht = 0.5 * speck - 1.5 * crack
    return t


def tile_tread(rng, h, w, decay):
    """Perforated tread plate: staggered round holes cut out in alpha, a solid lip at the nosing and back."""
    t, col, peel, rust, fine = painted_steel(rng, h, w, 0.6, decay)
    col = mix(col, GALV[None, None, :] * 0.85, np.full((h, w), 0.55))       # foot traffic took most of the paint long ago
    core = cover(rust, 0.08 + 0.14 * decay, 0.05)
    col = mix(col, mix(np.broadcast_to(RUST_LIGHT, col.shape).copy(), np.broadcast_to(RUST_DARK, col.shape).copy(), fine), core)
    yy, xx = np.mgrid[0:h, 0:w].astype(float)
    pitch = 15.0
    row = np.floor(yy / pitch)
    cx = (np.floor((xx - (row % 2) * pitch / 2) / pitch) + 0.5) * pitch + (row % 2) * pitch / 2
    cy = (row + 0.5) * pitch
    d = np.hypot(xx - cx, yy - cy)
    hole = d < 4.2
    lip = (yy < 9) | (yy > h - 9) | (xx < 8) | (xx > w - 8)
    hole &= ~lip
    col = mix(col, RUST_DARK[None, None, :], 0.5 * ramp(7.0 - d, 0.0, 3.0) * (~lip))
    t.rgb = col
    t.a = np.where(hole, 0.0, 1.0)
    t.sm = 0.3 - 0.18 * core
    t.metal = 0.6 * (1 - core)
    t.ht = -0.3 * core
    return t


def tile_brass(rng, h, w):
    """Old brass gone dull brown-green, even in tone; the band at v = 0.55..0.68 is the ring's raised outer edge, worn bright."""
    t = Tile(h, w)
    tar = fbm(rng, h, w, 40, 120, 3)
    fine = fbm(rng, h, w, 1.5, 12, 2)
    col = np.broadcast_to(np.array([0.30, 0.27, 0.15]), (h, w, 3)) * (0.88 + 0.24 * tar[..., None])
    verd = cover(fbm(rng, h, w, 14, 20, 3), 0.10, 0.08)
    col = mix(col, np.array([0.27, 0.33, 0.25])[None, None, :], 0.45 * verd)
    yy = np.arange(h)[:, None] / h
    worn = ramp(0.07 - np.abs(yy - 0.385), 0.0, 0.05) * (0.6 + 0.4 * fbm(rng, h, w, 3, 30, 2))
    worn = np.broadcast_to(worn, (h, w))
    col = mix(col, np.array([0.58, 0.46, 0.22])[None, None, :], 0.8 * worn)
    t.rgb = col * (0.94 + 0.12 * fine[..., None])
    t.metal = 1.0 - 0.6 * verd
    t.sm = 0.30 + 0.25 * worn - 0.1 * verd
    t.ht = 0.2 * fine
    return t


def tile_mapdisc(rng, h, w):
    """A map disc left in the sun for decades: the print is gone, only the paper and its stains remain."""
    t = Tile(h, w)
    col = np.array([0.74, 0.69, 0.56])[None, None, :] * (0.78 + 0.3 * fbm(rng, h, w, 40, 40, 4)[..., None])
    stain = cover(fbm(rng, h, w, 26, 26, 4), 0.3, 0.06)
    col = mix(col, np.array([0.47, 0.38, 0.24])[None, None, :], 0.55 * stain)
    yy, xx = np.mgrid[0:h, 0:w].astype(float)
    rr = np.hypot(xx - w / 2, yy - h / 2) / (w / 2)
    rings = ramp(0.012 - np.abs((rr * 5.0) % 1.0 - 0.5) * 0.2, 0.0, 0.012) * 0.0
    ghost = ramp(0.5 - np.abs(((np.arctan2(xx - w / 2, yy - h / 2) / math.pi * 18.0) % 1.0) - 0.5), 0.47, 0.5)
    ghost = np.maximum(ghost, ramp(0.5 - np.abs((rr * 4.0) % 1.0 - 0.5), 0.47, 0.5))
    col = col * (1.0 - 0.07 * (ghost + rings)[..., None] * ramp(fbm(rng, h, w, 30, 30, 2), 0.4, 0.7)[..., None])
    t.rgb = col
    t.sm[:] = 0.1
    return t


def tile_glass(rng, h, w, cracked=False, shard=False):
    t = Tile(h, w)
    dirt = fbm(rng, h, w, 22, 22, 4)
    yy, xx = np.mgrid[0:h, 0:w].astype(float)
    edge = np.minimum(np.minimum(xx, w - 1 - xx), np.minimum(yy, h - 1 - yy)) / (0.5 * min(h, w))
    # dirt gathers at the putty line and along the bottom rail; the middle of a pane stays mostly see-through
    grime = np.clip(0.85 * ramp(0.22 - edge, 0.0, 0.22) ** 2 + 0.5 * ramp(yy / h, 0.72, 1.0) ** 1.5 * (0.6 + 0.4 * dirt), 0, 1)
    runs = smear_down(((rng.random((h, w)) > 0.9985) & (yy < h * 0.4)).astype(float), 0.985) * 0.22
    grime = np.clip(grime + runs, 0, 1)
    col = mix(np.broadcast_to(np.array([0.30, 0.36, 0.36]), (h, w, 3)).copy(), np.broadcast_to(np.array([0.50, 0.47, 0.38]), (h, w, 3)).copy(), grime)
    a = 0.05 + 0.75 * grime
    if shard:
        a = 0.45 + 0.4 * grime
        col = col + 0.12
    if cracked:
        ox, oy = w * (0.3 + 0.4 * rng.random()), h * (0.3 + 0.4 * rng.random())
        ang = np.arctan2(yy - oy, xx - ox)
        cr = np.zeros((h, w))
        for k in range(7):
            a0 = rng.random() * 2 * math.pi
            dth = np.abs(((ang - a0 + math.pi) % (2 * math.pi)) - math.pi)
            cr = np.maximum(cr, ramp(1.3 - dth * np.hypot(xx - ox, yy - oy), 0.0, 1.0))
        col = mix(col, np.array([0.85, 0.88, 0.86])[None, None, :], 0.8 * cr)
        a = np.maximum(a, 0.9 * cr)
    t.rgb = col
    t.a = a
    t.sm[:] = 0.85
    t.sm -= 0.6 * grime
    return t


def tile_leaves(rng, h, w):
    t = Tile(h, w)
    t.a[:] = 0.0
    yy, xx = np.mgrid[0:h, 0:w].astype(float)
    cols = np.array([[0.30, 0.25, 0.19], [0.36, 0.31, 0.24], [0.24, 0.21, 0.17], [0.40, 0.36, 0.29]])
    for _ in range(110):
        cx, cy = rng.random() * w, rng.random() * h
        # thicker toward one corner, so the card reads as a drift
        if rng.random() > 1.25 - (cx / w + cy / h) * 0.6:
            continue
        L, B = 9 + 9 * rng.random(), 3.5 + 3 * rng.random()
        th = rng.random() * math.pi
        dx, dy = xx - cx, yy - cy
        u = dx * math.cos(th) + dy * math.sin(th)
        v = -dx * math.sin(th) + dy * math.cos(th)
        m = ((u / L) ** 2 + (v / B) ** 2 < 1.0) & (np.abs(v) < B * (1.0 - 0.5 * np.abs(u) / L))
        c = cols[int(rng.random() * 4)] * (0.8 + 0.4 * rng.random())
        vein = 1.0 - 0.25 * ramp(1.2 - np.abs(v), 0.0, 1.2)
        t.rgb[m] = (c[None, :] * vein[m][:, None])
        t.a[m] = 1.0
    t.sm[:] = 0.1
    return t


def tile_galv(rng, h, w, decay):
    t = Tile(h, w)
    fine = fbm(rng, h, w, 3, 3, 2)
    col = GALV[None, None, :] * (0.8 + 0.35 * fbm(rng, h, w, 20, 40, 4)[..., None])
    core = cover(fbm(rng, h, w, 9, 14, 4), 0.03 + 0.07 * decay, 0.05)
    col = mix(col, mix(np.broadcast_to(RUST_LIGHT, col.shape).copy(), np.broadcast_to(RUST_DARK, col.shape).copy(), fine), core)
    t.rgb = col
    t.metal = 0.7 * (1 - core)
    t.sm = 0.35 - 0.2 * core
    return t


def tile_flat(h, w, colour, sm=0.1):
    t = Tile(h, w)
    t.rgb[:] = colour
    t.sm[:] = sm
    return t


def tile_impostor(B, h, w, reg):
    """Side view of the far LOD, drawn as coverage: a thin pale lattice with a dark cab (director note N15)."""
    L = B["L"]
    buf = B["lod2"]
    ss = 4
    H, W = h * ss, w * ss
    sx, sz = W / reg.length, H / reg.width
    pale = np.zeros((H, W)); dark = np.zeros((H, W)); roof = np.zeros((H, W))
    yy, xx = np.mgrid[0:H, 0:W]
    for f in buf.faces:
        pts = [buf.verts[i] for i in f]
        px = np.array([(p[0] + reg.length / 2) * sx for p in pts])
        py = np.array([H - p[2] * sz for p in pts])
        zc = sum(p[2] for p in pts) / len(pts)
        x0, x1 = int(max(px.min() - 1, 0)), int(min(px.max() + 2, W))
        y0, y1 = int(max(py.min() - 1, 0)), int(min(py.max() + 2, H))
        if x1 <= x0 or y1 <= y0:
            continue
        gx, gy = xx[y0:y1, x0:x1] + 0.5, yy[y0:y1, x0:x1] + 0.5
        inside = np.ones(gx.shape, bool)
        area = 0.0
        n = len(pts)
        for i in range(n):
            area += px[i] * py[(i + 1) % n] - px[(i + 1) % n] * py[i]
        if abs(area) < 0.5:
            # edge-on to this view: draw it as a one-sample line so the far faces still thicken the lattice
            continue
        sgn = 1.0 if area > 0 else -1.0
        for i in range(n):
            ex, ey = px[(i + 1) % n] - px[i], py[(i + 1) % n] - py[i]
            inside &= sgn * (ex * (gy - py[i]) - ey * (gx - px[i])) >= 0
        target = roof if zc > L.z_eave + 0.05 else (dark if (zc > L.z_sill and zc < L.z_eave + 0.05 and len(pts) == 4 and abs(px.max() - px.min()) > 1.5 * sx) else pale)
        target[y0:y1, x0:x1] = np.maximum(target[y0:y1, x0:x1], inside)

    def down(m):
        return m.reshape(h, ss, w, ss).mean(axis=(1, 3))
    pale, dark, roof = down(pale), down(dark), down(roof)
    t = Tile(h, w)
    cover = np.clip(np.maximum(pale, np.maximum(dark, roof)), 0, 1)
    col = np.broadcast_to(PAINT * 0.93, (h, w, 3)).copy()
    col = mix(col, np.array([0.30, 0.29, 0.28])[None, None, :], np.clip(roof, 0, 1))
    col = mix(col, np.array([0.07, 0.075, 0.08])[None, None, :], np.clip(dark, 0, 1))
    t.rgb = col
    t.a = cover
    t.sm[:] = 0.1
    return t


def build_atlas(P, B):
    seed = int(P["seed"])
    decay = float(P["decay"]["amount"])
    n = ATLAS_PX
    rgb = np.empty((n, n, 3)); rgb[:] = PAINT
    alpha = np.ones((n, n))
    metal = np.zeros((n, n)); sm = np.full((n, n), 0.2); ht = np.zeros((n, n))
    tiles = {}
    for name in sorted(REGIONS):
        reg = REGIONS[name]
        x, y, w, h = reg.px
        rng = np.random.default_rng([seed, zlib.crc32(name.encode("ascii"))])
        if name.startswith("STEEL"):
            t = tile_steel_strip(rng, h, w, int(name[5:]) / (N_STEEL - 1.0), decay)
        elif name.startswith("WOOD"):
            t = tile_wood(rng, h, w, decay)
        elif name in ("PANEL0", "PANEL1"):
            t = tile_panel(rng, h, w, decay)
        elif name == "PANEL_IN":
            t = tile_panel(rng, h, w, decay, inside=True)
        elif name == "ROOF":
            t = tile_roof(rng, h, w, decay)
        elif name == "ROOF_UNDER":
            t = tile_roof(rng, h, w, decay, under=True)
        elif name == "CONCRETE":
            t = tile_concrete(rng, h, w, decay)
        elif name.startswith("TREAD"):
            t = tile_tread(rng, h, w, decay)
        elif name == "BRASS":
            t = tile_brass(rng, h, w)
        elif name == "MAPDISC":
            t = tile_mapdisc(rng, h, w)
        elif name == "GLASS0":
            t = tile_glass(rng, h, w)
        elif name == "GLASS1":
            t = tile_glass(rng, h, w, cracked=True)
        elif name == "SHARD":
            t = tile_glass(rng, h, w, shard=True)
        elif name == "LEAVES":
            t = tile_leaves(rng, h, w)
        elif name == "GALV":
            t = tile_galv(rng, h, w, decay)
        elif name == "DARK":
            t = tile_flat(h, w, (0.045, 0.045, 0.05))
        elif name == "PALE":
            t = tile_flat(h, w, tuple(PAINT))
        elif name == "IMPOSTOR":
            t = tile_impostor(B, h, w, reg)
        else:
            raise ValueError("no painter for atlas region " + name)
        tiles[name] = t
        # pad every region 4 px into its gutter by repeating its edge, so mip levels don't pull in a neighbour
        pad = 4
        for arr, src in ((rgb, t.rgb), (alpha, t.a), (metal, t.metal), (sm, t.sm), (ht, t.ht)):
            padw = ((pad, pad), (pad, pad), (0, 0)) if src.ndim == 3 else ((pad, pad), (pad, pad))
            big = np.pad(src, padw, mode="edge")
            ya, yb, xa, xb = max(y - pad, 0), min(y + h + pad, n), max(x - pad, 0), min(x + w + pad, n)
            arr[ya:yb, xa:xb] = big[ya - (y - pad):yb - (y - pad), xa - (x - pad):xb - (x - pad)]
        for arr, src in ((rgb, t.rgb), (alpha, t.a), (metal, t.metal), (sm, t.sm), (ht, t.ht)):
            arr[y:y + h, x:x + w] = src
    return rgb, alpha, metal, sm, ht


def to8(a):
    return np.clip(np.rint(a * 255.0), 0, 255).astype(np.uint8)


def write_all(P, B, out_dir):
    rgb, alpha, metal, sm, ht = build_atlas(P, B)
    n = ATLAS_PX
    name = P["name"]
    srgb = np.where(rgb <= 0.0031308, rgb * 12.92, 1.055 * np.power(np.clip(rgb, 0.0031308, None), 1 / 2.4) - 0.055)
    # colours above are picked as display values, so they are written as they are (the albedo is an sRGB texture)
    del srgb
    al = np.dstack([to8(rgb), to8(alpha)[..., None]])
    write_png(os.path.join(out_dir, name + "_Albedo.png"), al)
    # normal from height: x = -dh/du, y = -dh/dv (v runs up the image), z up
    k = 1.6
    dx = np.zeros((n, n)); dy = np.zeros((n, n))
    dx[:, 1:-1] = (ht[:, 2:] - ht[:, :-2]) * 0.5
    dy[1:-1, :] = (ht[2:, :] - ht[:-2, :]) * 0.5
    nx, ny, nz = -dx * k, dy * k, np.ones((n, n))
    ln = np.sqrt(nx * nx + ny * ny + nz * nz)
    nrm = np.dstack([to8(nx / ln * 0.5 + 0.5), to8(ny / ln * 0.5 + 0.5), to8(nz / ln * 0.5 + 0.5), np.full((n, n), 255, np.uint8)])
    write_png(os.path.join(out_dir, name + "_Normal.png"), nrm)
    mask = np.dstack([to8(metal), np.full((n, n), 255, np.uint8), np.zeros((n, n), np.uint8), to8(sm)])
    write_png(os.path.join(out_dir, name + "_Mask.png"), mask)
