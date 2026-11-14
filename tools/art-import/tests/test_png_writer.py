import struct
import tempfile
import unittest
import zlib
import os

import numpy as np

import pixels


def _decode_paeth(data: bytes):
    """A tiny reference decoder: parse chunks, inflate IDAT, undo filter 4 byte by byte."""
    i = 8
    ihdr = idat = None
    while i < len(data):
        (n,) = struct.unpack(">I", data[i:i + 4])
        kind = data[i + 4:i + 8]
        body = data[i + 8:i + 8 + n]
        if kind == b"IHDR":
            ihdr = struct.unpack(">IIBBBBB", body)
        elif kind == b"IDAT":
            idat = body
        i += 12 + n
    w, h, depth, ctype = ihdr[:4]
    c = {0: 1, 2: 3, 6: 4}[ctype]
    raw = zlib.decompress(idat)
    stride = w * c
    out = bytearray()
    prev = bytearray(stride)
    for y in range(h):
        row = raw[y * (stride + 1):(y + 1) * (stride + 1)]
        assert row[0] == 4
        cur = bytearray(stride)
        for x in range(stride):
            a = cur[x - c] if x >= c else 0
            b = prev[x]
            cc = prev[x - c] if x >= c else 0
            p = a + b - cc
            pa, pb, pc = abs(p - a), abs(p - b), abs(p - cc)
            pred = a if pa <= pb and pa <= pc else (b if pb <= pc else cc)
            cur[x] = (row[1 + x] + pred) & 0xFF
        out += cur
        prev = cur
    return np.frombuffer(bytes(out), dtype=np.uint8).reshape(h, w, c)


class PngWriterTests(unittest.TestCase):
    def setUp(self):
        self.img = np.random.default_rng(7).integers(0, 256, (8, 8, 4), dtype=np.uint8)

    def test_only_ihdr_idat_iend(self):
        self.assertEqual(pixels.png_chunks(pixels.encode_png(self.img)), ["IHDR", "IDAT", "IEND"])

    def test_paeth_round_trip(self):
        for c in (1, 3, 4):
            img = self.img[:, :, :c].copy()
            np.testing.assert_array_equal(_decode_paeth(pixels.encode_png(img)), img)

    def test_identical_bytes_twice(self):
        self.assertEqual(pixels.encode_png(self.img), pixels.encode_png(self.img.copy()))

    def test_pixel_hash_depends_on_shape(self):
        a = np.zeros((2, 8, 1), dtype=np.uint8)
        self.assertNotEqual(pixels.pixel_sha256(a), pixels.pixel_sha256(a.reshape(4, 4, 1)))

    @unittest.skipUnless(__import__("importlib").util.find_spec("png"), "pypng not installed")
    def test_pypng_reads_our_output(self):
        with tempfile.TemporaryDirectory() as d:
            p = os.path.join(d, "x.png")
            pixels.write_png(p, self.img)
            back, depth = pixels.read_png(p)
            self.assertEqual(depth, 8)
            np.testing.assert_array_equal(back, self.img)


if __name__ == "__main__":
    unittest.main()
