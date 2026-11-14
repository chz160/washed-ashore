"""Bad input fails one recipe, never the batch. Also: stale outputs, the PNG form behind a skip, and the
manifest and toolchain rules."""
import importlib.util
import io
import os
import tempfile
import unittest
import zipfile
from unittest import mock

import numpy as np

import art_import
import common
import pixels
import repack
from art_import import run_all
from repack import MANIFEST_NAME
from tests import fixtures

HAVE_PYPNG = importlib.util.find_spec("png") is not None


class RunFailureTests(unittest.TestCase):
    def setUp(self):
        self._tmp = tempfile.TemporaryDirectory()
        self.root = self._tmp.name
        self.maps = fixtures.make_set(os.path.join(self.root, "maps"), size=(256, 256))
        self.staging = os.path.join(self.root, "staging")
        self.recipes = os.path.join(self.root, "recipes")
        os.makedirs(self.staging)
        os.makedirs(self.recipes)
        self.n = 0

    def tearDown(self):
        self._tmp.cleanup()

    def add(self, sid, entries, **over):
        """A zip of (name, bytes) entries in staging, and a recipe for it."""
        with zipfile.ZipFile(os.path.join(self.staging, f"{sid}.zip"), "w") as z:
            for name, data in entries:
                z.writestr(name, data)
        r = fixtures.recipe(library_id=f"ambientcg:{sid}", source_id=sid, ship_size=256,
                            input={"archive": f"{sid}.zip", "archive_sha256": None}, **over)
        fixtures.write_json(os.path.join(self.recipes, f"{sid}.json"), r)
        return r

    def entries(self, sid, skip=()):
        out = []
        for m, p in self.maps.items():
            if m not in skip:
                with open(p, "rb") as f:
                    out.append((f"{sid}_1K-JPG_{m}.jpg", f.read()))
        return out

    def run_once(self, out="out", **kw):
        self.n += 1
        with mock.patch("sys.stderr", io.StringIO()):
            return run_all(self.recipes, self.staging, os.path.join(self.root, f"work{self.n}"),
                           os.path.join(self.root, out), out_stream=io.StringIO(), **kw)

    def test_bad_inputs_fail_their_recipe_and_the_batch_goes_on(self):
        from PIL import Image

        # A JPEG named .png.
        self.add("Bad001", [(n.replace("_Color.jpg", "_Color.png"), d) for n, d in self.entries("Bad001")])
        # A gray (1-channel) normal map.
        gray = os.path.join(self.root, "gray.jpg")
        Image.fromarray(np.full((256, 256), 128, dtype=np.uint8)).save(gray, format="JPEG")
        with open(gray, "rb") as f:
            gray_bytes = f.read()
        self.add("Bad002", [(n, gray_bytes if n.endswith("_NormalGL.jpg") else d)
                            for n, d in self.entries("Bad002", skip=("NormalDX",))])
        # Not a zip at all.
        self.add("Bad003", [])
        with open(os.path.join(self.staging, "Bad003.zip"), "wb") as f:
            f.write(b"not a zip")
        # A good recipe sorted after them still runs.
        self.add("Good001", self.entries("Good001"))
        results, failures = self.run_once()
        self.assertEqual([r[0]["library_id"] for r in results], ["ambientcg:Good001"])
        why = dict(failures)
        self.assertEqual(sorted(why), ["Bad001.json", "Bad002.json", "Bad003.json"])
        self.assertIn("content is jpeg but the extension is .png", why["Bad001.json"])
        self.assertIn("at least 2 channels", why["Bad002.json"])

    def test_role_change_removes_old_outputs(self):
        r = self.add("Fx001", self.entries("Fx001"))
        _, fails = self.run_once()
        self.assertEqual(fails, [])
        folder = os.path.join(self.root, "out", "ambientcg", "Fx001")
        self.assertIn("Fx001_TerrainMask.png", os.listdir(folder))
        open(os.path.join(folder, "Fx001_TerrainMask.png.meta"), "w").close()
        open(os.path.join(folder, "Fx001_Extra.png"), "wb").close()   # not in the manifest: only flagged
        fixtures.write_json(os.path.join(self.recipes, "Fx001.json"), dict(r, role="lit"))
        res, fails = self.run_once()
        self.assertEqual(fails, [])
        entry, log = res[0]
        self.assertEqual(sorted(os.listdir(folder)),
                         ["Fx001_Albedo.png", "Fx001_Extra.png", "Fx001_Mask.png", "Fx001_Normal.png"])
        self.assertIn({"file": "ambientcg/Fx001/Fx001_TerrainMask.png", "written": False, "skip_basis": None,
                       "removed": True}, log)
        self.assertIn("stale_output:Fx001_Extra.png", entry["flags"])
        with open(os.path.join(self.root, "out", MANIFEST_NAME), encoding="utf-8") as f:
            self.assertNotIn("TerrainMask", f.read())

    def test_manifest_naming_an_output_elsewhere_is_refused(self):
        self.add("Fx001", self.entries("Fx001"))
        self.run_once()
        mpath = os.path.join(self.root, "out", MANIFEST_NAME)
        m = repack.load_manifest(mpath)
        m["assets"][0]["outputs"].append({"file": "ambientcg/Fx001/../../victim.png"})
        with open(mpath, "w", encoding="utf-8", newline="\n") as f:
            f.write(common.dump_json(m))
        _, fails = self.run_once()
        self.assertIn("outside ambientcg/Fx001/", fails[0][1])

    @unittest.skipUnless(HAVE_PYPNG, "pypng not installed")
    def test_pixel_skip_needs_the_writers_png_form(self):
        self.add("Fx001", self.entries("Fx001"))
        self.run_once()
        out = os.path.join(self.root, "out")
        os.remove(os.path.join(out, MANIFEST_NAME))
        p = os.path.join(out, "ambientcg", "Fx001", "Fx001_Normal.png")
        with open(p, "rb") as f:
            data = f.read()
        # Same pixels with a tEXt chunk before IEND, as an image editor would save it.
        with open(p, "wb") as f:
            f.write(data[:-12] + pixels._chunk(b"tEXt", b"Software\x00an image editor") + data[-12:])
        with open(p, "rb") as f:
            self.assertFalse(pixels.is_writer_form(f.read(), (256, 256, 3)))
        res, _ = self.run_once()
        log = {l["file"]: l for l in res[0][1]}
        self.assertTrue(log["ambientcg/Fx001/Fx001_Normal.png"]["written"])
        self.assertEqual(log["ambientcg/Fx001/Fx001_Normal.png"]["skip_basis"], None)
        self.assertEqual(log["ambientcg/Fx001/Fx001_Albedo.png"]["skip_basis"], "pixels")
        with open(p, "rb") as f:
            self.assertEqual(pixels.png_chunks(f.read()), ["IHDR", "IDAT", "IEND"])

    def test_manifest_location(self):
        assets_out = os.path.join(common.REPO_ASSETS, "World", "BellsBend", "Art", "Library")
        self.assertEqual(repack.default_manifest_path(assets_out), repack.REPO_MANIFEST)
        scratch = os.path.join(self.root, "o")
        self.assertEqual(repack.default_manifest_path(scratch), os.path.join(scratch, MANIFEST_NAME))
        self.add("Fx001", self.entries("Fx001"))
        _, fails = self.run_once(manifest_path=os.path.join(common.REPO_ASSETS, "x", "m.json"))
        self.assertEqual(len(fails), 1)
        self.assertIn("may not live under Assets/", fails[0][1])
        self.assertFalse(os.path.exists(os.path.join(common.REPO_ASSETS, "x")))


class ToolchainTests(unittest.TestCase):
    def test_pinned_toolchain_has_no_drift(self):
        tc = dict(common.PINNED_TOOLCHAIN, python=common.PINNED_TOOLCHAIN["python"] + ".9")
        self.assertEqual(common.toolchain_drift(tc), [])

    def test_drift_is_named(self):
        tc = dict(common.PINNED_TOOLCHAIN, pillow="11.1.0", python="3.12.1")
        self.assertEqual(common.toolchain_drift(tc),
                         ["pillow: have 11.1.0, pinned 10.4.0", "python: have 3.12, pinned 3.13"])

    def test_writers_refuse_drift_unless_allowed(self):
        with mock.patch.object(art_import, "toolchain_drift", return_value=["zlib: have 1.3.1.zlib-ng, pinned 1.3.1"]):
            with self.assertRaises(art_import.ToolchainError):
                art_import.check_toolchain(False)
            with mock.patch("sys.stderr", io.StringIO()) as err:
                art_import.check_toolchain(True)
            self.assertIn("zlib", err.getvalue())
            with mock.patch("sys.stderr", io.StringIO()):
                self.assertEqual(art_import.main(["run", "--recipes", "r", "--staging", "s", "--work", "w", "--out", "o"]), 1)


if __name__ == "__main__":
    unittest.main()
