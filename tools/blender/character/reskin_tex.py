"""Texture work for reskin_survivor.py: one-time luma sampling and the MetallicGloss packing (td-49 F11 option A)."""
import hashlib
import os

import bpy
import numpy as np


def load_rgba(path):
    img = bpy.data.images.load(os.path.abspath(path))   # relative paths (repo-relative args) resolve against the working directory
    w, h = img.size
    buf = np.empty(w * h * img.channels, dtype=np.float32)
    img.pixels.foreach_get(buf)
    return img, buf.reshape(h, w, img.channels)


def luma_at(path, uv):
    """Luma (0..1, as stored) of the base colour at each vertex UV. Read once."""
    img, px = load_rgba(path)
    h, w = px.shape[:2]
    x = np.clip((uv[:, 0] * w).astype(int), 0, w - 1)
    y = np.clip((uv[:, 1] * h).astype(int), 0, h - 1)
    out = (px[y, x, :3] @ np.array([0.2126, 0.7152, 0.0722], dtype=np.float32)).astype(np.float64)
    bpy.data.images.remove(img)
    return out


def pack_metallic_gloss(metal_path, rough_path, out_path, size=2048):
    """R = metallic, G = B = 0, A = 1 - roughness, 2048, linear (sRGB OFF is an importer setting). Returns the sha1 and a rough-pixel check.
    The roughness inversion is checked on the roughest 1% of texels: their alpha must be the lowest."""
    mi, m = load_rgba(metal_path)
    ri, r = load_rgba(rough_path)
    if m.shape[:2] != r.shape[:2]:
        raise RuntimeError("metallic and roughness sizes differ")
    h, w = m.shape[:2]
    f = h // size
    def down(a):
        return a[:size * f, :size * f].reshape(size, f, size, f).mean(axis=(1, 3))
    metal = down(m[:, :, 0])
    rough = down(r[:, :, 0])
    out = np.zeros((size, size, 4), dtype=np.float32)
    out[:, :, 0] = metal
    out[:, :, 3] = 1.0 - rough
    img = bpy.data.images.new("MetallicGloss", size, size, alpha=True, float_buffer=False)
    img.colorspace_settings.name = "Non-Color"
    img.pixels.foreach_set(out.ravel())
    img.filepath_raw = out_path
    img.file_format = "PNG"
    img.save()
    thr = np.percentile(rough, 99)
    rough_px = rough >= thr
    check = {"rough_p99": round(float(thr), 4), "alpha_on_roughest_1pct": round(float(out[:, :, 3][rough_px].mean()), 4),
             "alpha_mean_all": round(float(out[:, :, 3].mean()), 4), "metal_mean": round(float(metal.mean()), 5)}
    check["inverted_ok"] = check["alpha_on_roughest_1pct"] < check["alpha_mean_all"]
    for i in (mi, ri, img):
        bpy.data.images.remove(i)
    return hashlib.sha1(open(out_path, "rb").read()).hexdigest(), check


def skin_texel_uv(path, uv, V, centre, radius=0.10):
    """UV of a SKIN texel sampled from his own surface near `centre` (director-49: the knee tears show skin through them): among the vertices within `radius` metres of the centre whose base colour is
    skin (warm: r > g > b, r >= 0.45, r - b >= 0.10), the one whose luma is the median. Returns (uv, colour, candidates); raises when none is found."""
    img, px = load_rgba(path)
    h, w = px.shape[:2]
    near = np.nonzero(np.linalg.norm(V - centre, axis=1) < radius)[0]
    x = np.clip((uv[near, 0] * w).astype(int), 0, w - 1)
    y = np.clip((uv[near, 1] * h).astype(int), 0, h - 1)
    col = px[y, x, :3]
    tried = []
    for rmin, rb in ((0.45, 0.10), (0.30, 0.08), (0.20, 0.06), (0.12, 0.04)):   # image pixels are scene-linear: dirty skin is dark, so the thresholds step down
        skin = (col[:, 0] > col[:, 1]) & (col[:, 1] > col[:, 2]) & (col[:, 0] >= rmin) & ((col[:, 0] - col[:, 2]) >= rb)
        tried.append((rmin, rb, int(skin.sum())))
        if skin.any():
            break
    bpy.data.images.remove(img)
    if not skin.any():
        raise RuntimeError("no skin texel within %.2f m of %s (candidates %s; colour percentiles r %s g %s b %s)" % (
            radius, np.round(centre, 3).tolist(), tried, np.percentile(col[:, 0], [5, 50, 95]).round(3).tolist(), np.percentile(col[:, 1], [5, 50, 95]).round(3).tolist(), np.percentile(col[:, 2], [5, 50, 95]).round(3).tolist()))
    idx = near[skin]
    lum = col[skin] @ np.array([0.2126, 0.7152, 0.0722])
    k = int(np.argsort(lum)[len(lum) // 2])
    return uv[idx[k]], col[skin][k].tolist(), int(skin.sum())
