"""End to end: zip -> extract -> verify -> repack, run twice into different work and out folders."""
import importlib.util
import io
import json
import os
import tempfile
import unittest
import zipfile

import pixels
from art_import import run_all
from repack import MANIFEST_NAME
from tests import fixtures

HAVE_PYPNG = importlib.util.find_spec("png") is not None


class DeterminismTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls._tmp = tempfile.TemporaryDirectory()
        root = cls._tmp.name
        cls.root = root
        maps_dir = os.path.join(root, "maps")
        files = fixtures.make_set(maps_dir, size=(256, 512))   # 2:1, like Ground078
        cls.staging = os.path.join(root, "staging")
        os.makedirs(cls.staging)
        with zipfile.ZipFile(os.path.join(cls.staging, "Fx001_1K-JPG.zip"), "w") as z:
            for p in files.values():
                z.write(p, os.path.basename(p))
            z.writestr("Fx001.png", b"preview, not a map")
            z.writestr("Fx001_1K-JPG.blend", b"not a map")
        cls.recipes = os.path.join(root, "recipes")
        os.makedirs(cls.recipes)
        fixtures.write_json(os.path.join(cls.recipes, "Fx001.json"), fixtures.recipe(
            ship_size=256, tile_metres=[8, 4], input={"archive": "Fx001_1K-JPG.zip", "archive_sha256": None},
            unity={"layer_key": "Test", "slot": 0, "legacy_smoothness": 0.05}))

    @classmethod
    def tearDownClass(cls):
        cls._tmp.cleanup()

    def run_into(self, work, out):
        res, fails = run_all(self.recipes, self.staging, os.path.join(self.root, work), os.path.join(self.root, out),
                             out_stream=io.StringIO())
        self.assertEqual(fails, [])
        return res

    def read(self, *p):
        with open(os.path.join(self.root, *p), "rb") as f:
            return f.read()

    def test_rebuild_is_byte_stable(self):
        r1 = self.run_into("w1", "o1")
        r2 = self.run_into("w2", "o2")
        e1, e2 = r1[0][0], r2[0][0]
        self.assertEqual([o["pixel_sha256"] for o in e1["outputs"]], [o["pixel_sha256"] for o in e2["outputs"]])
        self.assertEqual(self.read("o1", MANIFEST_NAME), self.read("o2", MANIFEST_NAME))
        for o in e1["outputs"]:
            self.assertEqual(self.read("o1", *o["file"].split("/")), self.read("o2", *o["file"].split("/")))
            self.assertEqual(pixels.png_chunks(self.read("o1", *o["file"].split("/"))), ["IHDR", "IDAT", "IEND"])
        # Downscaled 512x256 -> 256x128 and kept the aspect.
        self.assertEqual({(o["width"], o["height"]) for o in e1["outputs"]}, {(256, 128)})
        self.assertEqual([o["file"] for o in e1["outputs"]],
                         ["ambientcg/Fx001/Fx001_Albedo.png", "ambientcg/Fx001/Fx001_Normal.png",
                          "ambientcg/Fx001/Fx001_TerrainMask.png"])
        # No absolute paths and no per-run state in the manifest.
        text = self.read("o1", MANIFEST_NAME).decode("utf-8")
        self.assertNotIn(self.root.replace("\\", "\\\\"), text)
        self.assertNotIn("written", text)
        self.assertNotIn("\r", text)
        m = json.loads(text)["assets"][0]
        self.assertEqual(m["normal"]["detected"], "gl")
        self.assertEqual(m["normal"]["alternates"][0]["detected"], "dx")
        self.assertEqual(m["smoothness"]["legacy_constant"], 0.05)
        self.assertEqual(m["archive"]["name"], "Fx001_1K-JPG.zip")

        # A third run into o1 writes nothing and leaves files and manifest untouched.
        paths = [os.path.join(self.root, "o1", *o["file"].split("/")) for o in e1["outputs"]]
        paths.append(os.path.join(self.root, "o1", MANIFEST_NAME))
        before = {p: (os.stat(p).st_mtime_ns, self.read(p)) for p in paths}
        r3 = self.run_into("w3", "o1")
        self.assertTrue(all(not l["written"] and l["skip_basis"] == "manifest" for l in r3[0][1]))
        self.assertEqual(before, {p: (os.stat(p).st_mtime_ns, self.read(p)) for p in paths})

    @unittest.skipUnless(HAVE_PYPNG, "pypng not installed")
    def test_skip_by_decoded_pixels_without_manifest(self):
        self.run_into("w4", "o4")
        os.remove(os.path.join(self.root, "o4", MANIFEST_NAME))
        r = self.run_into("w5", "o4")
        self.assertTrue(all(not l["written"] and l["skip_basis"] == "pixels" for l in r[0][1]))


if __name__ == "__main__":
    unittest.main()
