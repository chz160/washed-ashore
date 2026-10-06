"""Minimal single-band GeoTIFF reader (numpy only).

Handles what the 3DEP ImageServer returns: little- or big-endian classic TIFF,
striped or tiled layout, float32/int16/uint16 samples, compression none (1)
or Deflate (8 / 32946) with no predictor. Georeferencing is not parsed; the
caller keeps the extent from the exportImage JSON response.
"""
import struct
import zlib

import numpy as np

_TYPE_SIZE = {1: 1, 2: 1, 3: 2, 4: 4, 5: 8, 6: 1, 7: 1, 8: 2, 9: 4, 10: 8, 11: 4, 12: 8, 16: 8}
_TYPE_FMT = {1: "B", 3: "H", 4: "I", 8: "h", 9: "i", 11: "f", 12: "d", 16: "Q"}


def _read_ifd(buf, end):
    (off,) = struct.unpack(end + "I", buf[4:8])
    (count,) = struct.unpack(end + "H", buf[off:off + 2])
    tags = {}
    for i in range(count):
        e = off + 2 + 12 * i
        tag, typ, n = struct.unpack(end + "HHI", buf[e:e + 8])
        size = _TYPE_SIZE.get(typ, 1) * n
        data = buf[e + 8:e + 12] if size <= 4 else buf[struct.unpack(end + "I", buf[e + 8:e + 12])[0]:][:size]
        fmt = _TYPE_FMT.get(typ)
        tags[tag] = list(struct.unpack(end + fmt * n, data[:size])) if fmt else data[:size]
    return tags


def _decode(chunk, compression):
    if compression == 1:
        return chunk
    if compression in (8, 32946):
        return zlib.decompress(chunk)
    raise ValueError(f"unsupported TIFF compression {compression}")


def read_tiff(path):
    """Return a 2D numpy array (row 0 = first image row, i.e. north for north-up rasters)."""
    with open(path, "rb") as fh:
        buf = fh.read()
    end = {b"II": "<", b"MM": ">"}.get(buf[:2])
    if end is None or struct.unpack(end + "H", buf[2:4])[0] != 42:
        raise ValueError(f"{path}: not a classic TIFF")
    tags = _read_ifd(buf, end)
    width, height = tags[256][0], tags[257][0]
    bits = tags.get(258, [8])[0]
    fmt = tags.get(339, [1])[0]
    compression = tags.get(259, [1])[0]
    if tags.get(277, [1])[0] != 1:
        raise ValueError(f"{path}: expected one sample per pixel")
    if tags.get(317, [1])[0] != 1:
        raise ValueError(f"{path}: TIFF predictor not supported")
    kind = {1: "u", 2: "i", 3: "f"}[fmt]
    dtype = np.dtype(f"{end}{kind}{bits // 8}")
    out = np.empty((height, width), dtype=dtype.newbyteorder("="))
    if 322 in tags:
        tw, th = tags[322][0], tags[323][0]
        offsets, counts = tags[324], tags[325]
        across = (width + tw - 1) // tw
        for idx, (o, c) in enumerate(zip(offsets, counts)):
            tile = np.frombuffer(_decode(buf[o:o + c], compression), dtype=dtype, count=tw * th).reshape(th, tw)
            r, q = divmod(idx, across)
            y0, x0 = r * th, q * tw
            h, w = min(th, height - y0), min(tw, width - x0)
            out[y0:y0 + h, x0:x0 + w] = tile[:h, :w]
    else:
        rps = tags.get(278, [height])[0]
        offsets, counts = tags[273], tags[279]
        for idx, (o, c) in enumerate(zip(offsets, counts)):
            y0 = idx * rps
            rows = min(rps, height - y0)
            strip = np.frombuffer(_decode(buf[o:o + c], compression), dtype=dtype, count=rows * width)
            out[y0:y0 + rows] = strip.reshape(rows, width)
    return out
