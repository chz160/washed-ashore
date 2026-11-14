import unittest

from recipe import recipe_sha256, validate
from tests.fixtures import recipe


class RecipeTests(unittest.TestCase):
    def test_valid(self):
        self.assertEqual(validate(recipe()), [])

    def test_unknown_and_missing_keys(self):
        r = recipe(extra=1)
        del r["height_metres"]
        e = validate(r)
        self.assertIn("unknown key: extra", e)
        self.assertIn("missing key: height_metres", e)

    def test_schema(self):
        self.assertTrue(validate(recipe(schema="x/2")))

    def test_tile_must_divide_1024(self):
        self.assertTrue(any("doesn't divide" in m for m in validate(recipe(tile_metres=[5, 5]))))
        self.assertEqual(validate(recipe(tile_metres=[8, 4])), [])

    def test_ship_size(self):
        for bad in (128, 1000, 8192, "1024", True):
            self.assertTrue(validate(recipe(ship_size=bad)), bad)

    def test_role_and_convention(self):
        self.assertTrue(validate(recipe(role="decal")))
        self.assertTrue(validate(recipe(normal={"convention": "auto"})))

    def test_library_id_matches(self):
        self.assertTrue(validate(recipe(library_id="ambientcg:Other")))
        self.assertTrue(validate(recipe(source_id="../x", library_id="ambientcg:../x")))

    def test_height_positive(self):
        self.assertTrue(validate(recipe(height_metres=0)))

    def test_archive_pin_shape(self):
        self.assertTrue(validate(recipe(input={"archive": "a.zip", "archive_sha256": "ABC"})))
        self.assertTrue(validate(recipe(input={"archive": "../a.zip", "archive_sha256": None})))

    def test_hash_ignores_key_order(self):
        a = recipe()
        b = dict(reversed(list(a.items())))
        self.assertEqual(recipe_sha256(a), recipe_sha256(b))


if __name__ == "__main__":
    unittest.main()
