"""Separable Lanczos-3 resampling with periodic (wrap) boundaries, so a tiling texture still tiles."""
from __future__ import annotations

import math

import numpy as np

LOBES = 3


def lanczos(x: np.ndarray, a: int = LOBES) -> np.ndarray:
    x = np.asarray(x, dtype=np.float64)
    out = np.sinc(x) * np.sinc(x / a)
    return np.where(np.abs(x) < a, out, 0.0)


def weights(n_in: int, n_out: int, a: int = LOBES) -> np.ndarray:
    """Dense n_out x n_in matrix; rows sum to 1; taps past an edge wrap to the other side."""
    scale = n_in / n_out
    f = max(scale, 1.0)
    support = a * f
    w = np.zeros((n_out, n_in), dtype=np.float64)
    for i in range(n_out):
        centre = (i + 0.5) * scale - 0.5
        lo = int(math.floor(centre - support))
        hi = int(math.ceil(centre + support))
        js = np.arange(lo, hi + 1)
        k = lanczos((js - centre) / f, a)
        np.add.at(w[i], js % n_in, k)
    w /= w.sum(axis=1, keepdims=True)
    return w


def resize(img: np.ndarray, out_h: int, out_w: int) -> np.ndarray:
    """Resize an HxWxC float64 image. The same size returns the input unchanged."""
    h, w = img.shape[:2]
    if (h, w) == (out_h, out_w):
        return img
    x = img
    if out_h != h:
        x = np.einsum("oh,hwc->owc", weights(h, out_h), x, optimize=False)
    if out_w != w:
        x = np.einsum("pw,owc->opc", weights(w, out_w), x, optimize=False)
    return x


def ship_dims(h: int, w: int, ship_size: int):
    """Output (h, w) with the long edge equal to ship_size. Refuses an upscale or a non-integer size."""
    long_edge = max(h, w)
    if ship_size > long_edge:
        raise ValueError(f"ship_size {ship_size} is larger than the source long edge {long_edge}; no upscale")
    if (h * ship_size) % long_edge or (w * ship_size) % long_edge:
        raise ValueError(f"{w}x{h} doesn't scale to a whole size at long edge {ship_size}")
    return h * ship_size // long_edge, w * ship_size // long_edge
