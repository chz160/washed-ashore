import os
import tempfile
import unittest

import verify
from tests import fixtures


class VerifyTests(unittest.TestCase):
    def setUp(self):
        self._tmp = tempfile.TemporaryDirectory()
        self.dir = os.path.join(self._tmp.name, "src")
        self.files = fixtures.make_set(self.dir, size=(64, 64))
        self.aj = fixtures.asset_json(self.dir, list(self.files.values()))

    def tearDown(self):
        self._tmp.cleanup()

    def run_verify(self, r=None, aj="default"):
        return verify.verify(r or fixtures.recipe(), self.dir, self.aj if aj == "default" else aj)

    def test_passes_with_asset_json(self):
        v = self.run_verify()
        self.assertEqual(v["errors"], [])
        self.assertEqual(v["license"], "CC0-1.0")
        self.assertEqual(v["license_basis"], "asset.json")
        self.assertEqual(set(v["maps"]), {"albedo", "normal_gl", "roughness", "ao", "height"})
        self.assertEqual([u["map"] for u in v["unused"]], ["normal_dx"])
        self.assertTrue(all(s["verified_against"] == "asset.json" for s in v["sources"]))

    def test_tampered_byte(self):
        p = self.files["Roughness"]
        with open(p, "r+b") as f:
            f.seek(200)
            b = f.read(1)
            f.seek(200)
            f.write(bytes([b[0] ^ 1]))
        self.assertTrue(any("doesn't match asset.json" in e for e in self.run_verify()["errors"]))

    def test_unlisted_file(self):
        self.aj["files"] = [f for f in self.aj["files"] if "Displacement" not in f["path"]]
        self.assertTrue(any("not listed" in e for e in self.run_verify()["errors"]))

    def test_missing_required_map(self):
        os.remove(self.files["Displacement"])
        self.aj = fixtures.asset_json(self.dir, [p for k, p in self.files.items() if k != "Displacement"])
        e = self.run_verify()["errors"]
        self.assertTrue(any("missing required map for terrain-layer: height" in m for m in e), e)
        # A Lit material doesn't need height.
        self.assertEqual(self.run_verify(fixtures.recipe(role="lit"))["errors"], [])

    def test_detect_needs_height(self):
        os.remove(self.files["Displacement"])
        e = verify.verify(fixtures.recipe(role="lit", normal={"convention": "detect"}), self.dir, None)["errors"]
        self.assertTrue(any("needs a height map" in m for m in e), e)

    def test_licence_normalised_and_allowlist(self):
        self.assertEqual(self.run_verify()["license"], "CC0-1.0")   # asset.json says "CC0 1.0"
        self.aj["license"] = "GPL-3.0"
        self.assertTrue(any("allowlist" in e for e in self.run_verify(fixtures.recipe(license="GPL-3.0"))["errors"]))

    def test_recipe_licence_must_agree_with_asset_json(self):
        self.assertTrue(any("doesn't match asset.json" in e
                            for e in self.run_verify(fixtures.recipe(license="LicenseRef-Fab-Standard"))["errors"]))

    def test_fab_needs_acquired_via_fab(self):
        self.aj["license"] = "Fab Standard License"
        r = fixtures.recipe(license="LicenseRef-Fab-Standard")
        self.assertTrue(any("acquired_via 'fab'" in e for e in self.run_verify(r)["errors"]))
        self.aj["acquired_via"] = "fab"
        self.assertEqual(self.run_verify(r)["errors"], [])

    def test_bridge_refused(self):
        self.aj["acquired_via"] = "bridge"
        self.assertTrue(any("Unreal-only" in e for e in self.run_verify()["errors"]))

    def test_metadata_disagreement_is_only_a_flag(self):
        self.aj["maps"] = ["albedo", "normal_gl"]
        v = self.run_verify()
        self.assertEqual(v["errors"], [])
        self.assertIn("metadata_maps_disagree:folder_only=ao+height+roughness;metadata_only=-", v["flags"])

    def test_asset_library_vocabulary_agrees(self):
        # tools/asset-library's vocabulary: extra map types art-import never reads, and a bare "normal" filed
        # as normal_gl. None of that is a disagreement.
        self.aj["maps"] = ["albedo", "ao", "bump", "emission", "height", "mask_soil", "normal_gl", "normal_dx",
                           "roughness", "specular", "translucency"]
        self.assertFalse([f for f in self.run_verify()["flags"] if f.startswith("metadata_maps_disagree")])
        self.assertIsNone(verify.metadata_disagreement(["albedo", "normal_gl"], ["albedo", "normal_unknown"]))
        self.assertEqual(verify.metadata_disagreement(["albedo", "normal_gl", "height", "opacity"], ["albedo", "normal_gl"]),
                         "metadata_maps_disagree:folder_only=-;metadata_only=height+opacity")

    def test_no_asset_json(self):
        v = self.run_verify(aj=None)
        self.assertEqual(v["errors"], [])
        self.assertEqual(v["license_basis"], "recipe")
        self.assertIn("license_from_recipe", v["flags"])
        self.assertIn("unverified_no_asset_json", v["flags"])

    def test_archive_pin(self):
        info = {"name": "Fx001_1K-JPG.zip", "sha256": "a" * 64,
                "entries": [{"name": os.path.basename(p)} for p in self.files.values()]}
        r = fixtures.recipe(input={"archive": "Fx001_1K-JPG.zip", "archive_sha256": "a" * 64})
        v = verify.verify(r, self.dir, None, info)
        self.assertEqual(v["errors"], [])
        self.assertTrue(all(s["verified_against"] == "archive:sha256" for s in v["sources"]))
        r["input"]["archive_sha256"] = "b" * 64
        self.assertTrue(any("doesn't match the recipe pin" in e for e in verify.verify(r, self.dir, None, info)["errors"]))
        r["input"]["archive_sha256"] = None
        v = verify.verify(r, self.dir, None, info)
        self.assertTrue(all(s["verified_against"] == "archive:crc32" for s in v["sources"]))

    def test_duplicate_role_is_an_error(self):
        fixtures.make_set(self.dir, asset_id="Fx002", maps=("Color",))
        self.assertTrue(any("more than one file claims albedo" in e for e in self.run_verify(aj=None)["errors"]))

    def test_rough_ao_refused(self):
        fixtures.make_set(self.dir, asset_id="Fx001", maps=("Roughness",))
        os.rename(self.files["Roughness"], os.path.join(self.dir, "fx_rough_ao_1k.jpg"))
        self.assertTrue(any("rough_ao" in e for e in self.run_verify(aj=None)["errors"]))


if __name__ == "__main__":
    unittest.main()
