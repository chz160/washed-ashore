import os
import tempfile
import unittest
from pathlib import Path
from unittest import mock

import common


class GuardTests(unittest.TestCase):
    def test_library_roots_refused_without_resolve(self):
        with mock.patch.object(Path, "resolve", side_effect=AssertionError("Path.resolve called")) as res:
            for p in (r"Y:\AssetLibrary\ambientcg\x.png", r"y:\assetlibrary\x", r"\\tank\public\AssetLibrary\a\b.png"):
                with self.assertRaises(common.GuardError, msg=p):
                    common.guard_write(p)
            res.assert_not_called()

    def test_env_library_root_refused(self):
        with tempfile.TemporaryDirectory() as d, mock.patch.dict(os.environ, {"ASSET_LIBRARY_ROOT": d}):
            with self.assertRaises(common.GuardError):
                common.guard_write(os.path.join(d, "sub", "x.png"))
            common.guard_write(d + "-sibling")   # a prefix that isn't a parent folder is fine

    def test_input_dir_refused(self):
        with tempfile.TemporaryDirectory() as d:
            src = os.path.join(d, "src")
            with self.assertRaises(common.GuardError):
                common.guard_write(os.path.join(src, "x.png"), inputs=(src,))
            common.guard_write(os.path.join(d, "out", "x.png"), inputs=(src,))

    def test_repo_staging_refused(self):
        with self.assertRaises(common.GuardError):
            common.guard_write(common.REPO_STAGING / "ambientcg" / "x.png")

    def test_assets_needs_allow(self):
        p = common.REPO_ASSETS / "World" / "BellsBend" / "Art" / "Library" / "x.png"
        with self.assertRaises(common.GuardError):
            common.guard_write(p)
        self.assertEqual(common.guard_write(p, allow_assets=True), p)

    def test_refused_write_leaves_nothing(self):
        with tempfile.TemporaryDirectory() as d:
            src = os.path.join(d, "src")
            with self.assertRaises(common.GuardError):
                common.write_bytes_atomic(os.path.join(src, "x.bin"), b"x", inputs=(src,))
            self.assertFalse(os.path.exists(src))


if __name__ == "__main__":
    unittest.main()
