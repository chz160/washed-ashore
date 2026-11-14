import io
import os
import tempfile
import unittest
import zipfile
from unittest import mock

import archive


class ArchiveTests(unittest.TestCase):
    def setUp(self):
        self._tmp = tempfile.TemporaryDirectory()
        self.root = self._tmp.name
        self.n = 0

    def tearDown(self):
        self._tmp.cleanup()

    def zip_with(self, entries):
        self.n += 1
        p = os.path.join(self.root, f"a{self.n}.zip")
        with zipfile.ZipFile(p, "w") as z:
            for n, data in entries:
                z.writestr(n, data)
        return p

    def into(self):
        return os.path.join(self.root, f"out{self.n}")

    def test_extracts_maps_only_and_flat(self):
        p = self.zip_with([("X_1K-JPG_Color.jpg", b"c"), ("sub/X_1K-JPG_NormalGL.jpg", b"n"), ("X.png", b"p"),
                           ("X_1K-JPG.blend", b"b"), ("X_1K-JPG.usdc", b"u")])
        info = archive.extract(p, self.into())
        self.assertEqual(sorted(os.listdir(self.into())), ["X_1K-JPG_Color.jpg", "X_1K-JPG_NormalGL.jpg"])
        self.assertEqual(info["skipped"], ["X.png", "X_1K-JPG.blend", "X_1K-JPG.usdc"])
        self.assertEqual(info["name"], os.path.basename(p))
        self.assertEqual(len(info["sha256"]), 64)
        self.assertEqual([e["name"] for e in info["entries"]], ["X_1K-JPG_Color.jpg", "X_1K-JPG_NormalGL.jpg"])

    def test_refuses_unsafe_names(self):
        for bad in ("../x_Color.jpg", "/abs_Color.jpg", "C:/x_Color.jpg", "a/../../x_Color.jpg"):
            p = self.zip_with([(bad, b"x")])
            with self.assertRaises(archive.ArchiveError, msg=bad):
                archive.extract(p, self.into())
            self.assertFalse(os.path.exists(os.path.join(self.root, "x_Color.jpg")))

    def test_refuses_backslash_and_drive_names(self):
        # zipfile rewrites backslashes when writing, so the name check is tested directly.
        for bad in ("..\\x_Color.jpg", "C:x_Color.jpg", "a\x00_Color.jpg"):
            with self.assertRaises(archive.ArchiveError, msg=bad):
                archive.check_entry_name(bad)

    def test_refuses_symlink(self):
        self.n += 1
        p = os.path.join(self.root, "ln.zip")
        with zipfile.ZipFile(p, "w") as z:
            info = zipfile.ZipInfo("x_Color.jpg")
            info.external_attr = 0o120777 << 16
            z.writestr(info, b"target")
        with self.assertRaises(archive.ArchiveError):
            archive.extract(p, self.into())

    def test_refuses_too_many_entries(self):
        p = self.zip_with([(f"x{i}_Color.jpg", b"x") for i in range(archive.MAX_ENTRIES + 1)])
        with self.assertRaises(archive.ArchiveError):
            archive.extract(p, self.into())

    def test_refuses_oversize_entry(self):
        old = archive.MAX_ENTRY_BYTES
        archive.MAX_ENTRY_BYTES = 10
        try:
            p = self.zip_with([("x_Color.jpg", b"x" * 11)])
            with self.assertRaises(archive.ArchiveError):
                archive.extract(p, self.into())
        finally:
            archive.MAX_ENTRY_BYTES = old

    def test_total_inflated_bytes_capped_even_when_headers_understate(self):
        # CPython's zipfile stops at the declared size, so a lying header is simulated: every entry inflates to
        # 200 bytes while the headers declare 1, which passes the declared-size check.
        p = self.zip_with([("a_Color.jpg", b"x"), ("a_NormalGL.jpg", b"x")])
        fake_open = lambda self, info, *a, **k: io.BytesIO(b"y" * 200)  # noqa: E731
        with mock.patch.object(archive, "MAX_TOTAL_BYTES", 300), mock.patch.object(zipfile.ZipFile, "open", fake_open):
            with self.assertRaisesRegex(archive.ArchiveError, "grew past 300"):
                archive.extract(p, self.into())

    def test_refuses_non_empty_folder(self):
        p = self.zip_with([("x_Color.jpg", b"x")])
        os.makedirs(self.into())
        open(os.path.join(self.into(), "planted.py"), "w").close()
        with self.assertRaises(archive.ArchiveError):
            archive.extract(p, self.into())

    def test_refuses_extracting_into_the_input_folder(self):
        p = self.zip_with([("x_Color.jpg", b"x")])
        with self.assertRaises(Exception):
            archive.extract(p, os.path.join(self.root, "inside"), inputs=(self.root,))

    def test_crc_checked(self):
        p = self.zip_with([("x_Color.jpg", b"hello world, hello world")])
        with open(p, "rb") as f:
            data = bytearray(f.read())
        i = data.find(b"hello world")
        data[i] ^= 1
        with open(p, "wb") as f:
            f.write(bytes(data))
        with self.assertRaises(zipfile.BadZipFile):
            archive.extract(p, self.into())


if __name__ == "__main__":
    unittest.main()
