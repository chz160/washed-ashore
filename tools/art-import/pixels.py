"""Image decode, the PNG writer and the pixel hash.

Decode: JPEG through Pillow (8-bit only; EXIF orientation is never applied), PNG through pypng (8 or 16
bit). The format comes from the file's magic bytes; an extension that disagrees is an error.
Encode: own PNG writer with stdlib zlib. Chunks are IHDR, one IDAT and IEND only; every row uses the
Paeth filter; zlib level 9. The pixel hash covers the decoded 8-bit pixels, not the file bytes.
"""
from __future__ import annotations

import os
import struct
import zlib

import numpy as np

from common import PIXEL_HASH_TAG, sha256_bytes, write_bytes_atomic

PNG_SIG = b"\x89PNG\r\n\x1a\n"
JPEG_SIG = b"\xff\xd8\xff"
MAX_PIXELS = 8192 * 8192
ZLIB_LEVEL = 9


class PixelError(Exception):
    pass


def sniff(path) -> str:
    with open(path, "rb") as f:
        head = f.read(8)
    if head.startswith(PNG_SIG):
        kind = "png"
    elif head.startswith(JPEG_SIG):
        kind = "jpeg"
    else:
        raise PixelError(f"not a PNG or JPEG: {os.path.basename(path)}")
    ext = os.path.splitext(path)[1].lower()
    if (kind == "png") != (ext == ".png"):
        raise PixelError(f"{os.path.basename(path)}: content is {kind} but the extension is {ext}")
    return kind


def decode(path):
    """Return (float64 array HxWxC in 0..1, {"format", "bitdepth", "channels"})."""
    kind = sniff(path)
    if kind == "jpeg":
        u8 = _decode_jpeg(path)
        return u8.astype(np.float64) / 255.0, {"format": "jpeg", "bitdepth": 8, "channels": u8.shape[2]}
    arr, bitdepth = read_png(path)
    scale = 255.0 if bitdepth == 8 else 65535.0
    return arr.astype(np.float64) / scale, {"format": "png", "bitdepth": bitdepth, "channels": arr.shape[2]}


def _decode_jpeg(path) -> np.ndarray:
    from PIL import Image

    Image.MAX_IMAGE_PIXELS = MAX_PIXELS
    with Image.open(path) as im:
        if im.format != "JPEG":
            raise PixelError(f"{os.path.basename(path)}: Pillow reports {im.format}, expected JPEG")
        if im.mode not in ("L", "RGB"):
            raise PixelError(f"{os.path.basename(path)}: JPEG mode {im.mode} is not supported (L or RGB only)")
        if im.width * im.height > MAX_PIXELS:
            raise PixelError(f"{os.path.basename(path)}: {im.width}x{im.height} is too large")
        a = np.asarray(im, dtype=np.uint8)
    if a.ndim == 2:
        a = a[:, :, None]
    return np.ascontiguousarray(a)


def _pypng():
    try:
        import png  # pypng
    except ImportError as e:
        raise PixelError("pypng is required to read PNG files: install tools/art-import/requirements.txt into the venv") from e
    return png


def read_png(path):
    """Return (uint8 or uint16 array HxWxC, bitdepth) for an 8 or 16-bit gray, gray+alpha, RGB or RGBA PNG."""
    png = _pypng()
    with open(path, "rb") as f:
        try:
            w, h, rows, info = png.Reader(file=f).asDirect()
            if w * h > MAX_PIXELS:
                raise PixelError(f"{os.path.basename(path)}: {w}x{h} is too large")
            bitdepth = info["bitdepth"]
            planes = info["planes"]
            if bitdepth not in (8, 16):
                raise PixelError(f"{os.path.basename(path)}: bit depth {bitdepth} is not supported")
            dtype = np.uint8 if bitdepth == 8 else np.uint16
            arr = np.vstack([np.asarray(r, dtype=dtype) for r in rows]).reshape(h, w, planes)
        except png.Error as e:
            raise PixelError(f"{os.path.basename(path)}: {e}") from e
    return np.ascontiguousarray(arr), bitdepth


def to_u8(x: np.ndarray) -> np.ndarray:
    """The single rounding step: float 0..1 -> uint8."""
    return np.clip(np.floor(x * 255.0 + 0.5), 0, 255).astype(np.uint8)


def pixel_sha256(a: np.ndarray) -> str:
    if a.dtype != np.uint8 or a.ndim != 3:
        raise PixelError("pixel hash expects a uint8 HxWxC array")
    h, w, c = a.shape
    head = f"{PIXEL_HASH_TAG} {w}x{h}x{c} u8\n".encode("ascii")
    return sha256_bytes(head + np.ascontiguousarray(a).tobytes("C"))


def _chunk(kind: bytes, data: bytes) -> bytes:
    return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data) & 0xFFFFFFFF)


def paeth_filter(a: np.ndarray) -> np.ndarray:
    """Filter type 4 for every row of a uint8 HxWxC image; returns H x (1 + W*C) bytes with the filter byte."""
    h, w, c = a.shape
    x = a.reshape(h, w * c).astype(np.int16)
    left = np.zeros_like(x)
    left[:, c:] = x[:, :-c]
    up = np.zeros_like(x)
    up[1:, :] = x[:-1, :]
    upleft = np.zeros_like(x)
    upleft[1:, c:] = x[:-1, :-c]
    p = left + up - upleft
    pa = np.abs(p - left)
    pb = np.abs(p - up)
    pc = np.abs(p - upleft)
    pred = np.where((pa <= pb) & (pa <= pc), left, np.where(pb <= pc, up, upleft))
    out = ((x - pred) & 0xFF).astype(np.uint8)
    return np.hstack([np.full((h, 1), 4, dtype=np.uint8), out])


def encode_png(a: np.ndarray) -> bytes:
    if a.dtype != np.uint8 or a.ndim != 3 or a.shape[2] not in (1, 3, 4):
        raise PixelError("PNG writer expects a uint8 HxWx{1,3,4} array")
    h, w, c = a.shape
    colour_type = {1: 0, 3: 2, 4: 6}[c]
    ihdr = struct.pack(">IIBBBBB", w, h, 8, colour_type, 0, 0, 0)
    raw = paeth_filter(np.ascontiguousarray(a)).tobytes("C")
    return PNG_SIG + _chunk(b"IHDR", ihdr) + _chunk(b"IDAT", zlib.compress(raw, ZLIB_LEVEL)) + _chunk(b"IEND", b"")


def write_png(path, a: np.ndarray, **guard) -> bytes:
    data = encode_png(a)
    write_bytes_atomic(path, data, **guard)
    return data


def is_writer_form(data: bytes, shape) -> bool:
    """True if data has the chunk layout and IHDR encode_png would write for an HxWxC uint8 image.

    The zlib stream itself isn't compared, so a file from another zlib build still counts.
    """
    try:
        if png_chunks(data) != ["IHDR", "IDAT", "IEND"]:
            return False
    except (PixelError, struct.error, UnicodeDecodeError):
        return False
    h, w, c = shape
    if c not in (1, 3, 4):
        return False
    want = struct.pack(">IIBBBBB", w, h, 8, {1: 0, 3: 2, 4: 6}[c], 0, 0, 0)
    return data[16:29] == want


def png_chunks(data: bytes) -> list[str]:
    """Chunk types in order; used by tests and the validator."""
    if not data.startswith(PNG_SIG):
        raise PixelError("not a PNG")
    out, i = [], len(PNG_SIG)
    while i < len(data):
        (n,) = struct.unpack(">I", data[i:i + 4])
        out.append(data[i + 4:i + 8].decode("ascii"))
        i += 12 + n
    return out
