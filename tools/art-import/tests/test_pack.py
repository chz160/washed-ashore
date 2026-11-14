import unittest

import numpy as np

import pack


class PackTests(unittest.TestCase):
    def setUp(self):
        self.shape = (4, 4)
        self.metal = np.full(self.shape, 0.1)
        self.ao = np.full(self.shape, 0.7)
        self.rough = np.full(self.shape, 0.8)

    def test_lit_channel_order(self):
        m = pack.lit_mask(self.shape, metal=self.metal, ao=self.ao, smooth=pack.smoothness(self.rough))
        np.testing.assert_allclose(m[0, 0], [0.1, 0.7, 0.0, 0.2])

    def test_lit_defaults(self):
        m = pack.lit_mask(self.shape, smooth=pack.smoothness(gloss=np.full(self.shape, 0.3)))
        np.testing.assert_allclose(m[0, 0], [0.0, 1.0, 0.0, 0.3])

    def test_terrain_channel_order_and_stretch(self):
        h = np.tile(np.linspace(0.2, 0.6, 201), (4, 1))
        rgba, lo, hi, flags = pack.terrain_mask(h.shape, metal=None, ao=np.full(h.shape, 0.9), height=h,
                                                smooth=np.full(h.shape, 0.25))
        self.assertEqual(flags, [])
        self.assertAlmostEqual(lo, 0.2 + 0.4 * 0.005, places=9)
        self.assertAlmostEqual(hi, 0.2 + 0.4 * 0.995, places=9)
        np.testing.assert_allclose(rgba[0, 100], [0.0, 0.9, 0.5, 0.25], atol=1e-9)
        self.assertEqual(rgba[:, :, 2].min(), 0.0)
        self.assertEqual(rgba[:, :, 2].max(), 1.0)

    def test_flat_height(self):
        b, lo, hi, flags = pack.height_stretch(np.full((4, 4), 0.4))
        self.assertEqual(flags, ["height_flat"])
        np.testing.assert_array_equal(b, 0.5)

    def test_arm_unpack(self):
        arm = np.stack([np.full(self.shape, v) for v in (0.9, 0.6, 0.0)], axis=2)
        ao, rough, metal = pack.unpack_arm(arm)
        self.assertEqual((ao[0, 0], rough[0, 0], metal[0, 0]), (0.9, 0.6, 0.0))

    def test_smoothness_is_raw(self):
        np.testing.assert_allclose(pack.smoothness(np.array([0.0, 0.25, 1.0])), [1.0, 0.75, 0.0])


class AlbedoCheckTests(unittest.TestCase):
    def _img(self, lo, hi):
        y = np.linspace(lo, hi, 1000)
        return np.repeat(np.round(y).astype(np.uint8)[None, :, None], 3, axis=2)

    def test_inside_both(self):
        self.assertEqual(pack.albedo_stats(self._img(60, 230))[1], [])

    def test_soft_only_inside(self):
        self.assertEqual(pack.albedo_stats(self._img(40, 230))[1], ["albedo_strict_range"])

    def test_outside_both(self):
        self.assertEqual(pack.albedo_stats(self._img(5, 250))[1], ["albedo_soft_range", "albedo_strict_range"])

    def test_strict_high_edge(self):
        # p99 between 240 and 243 breaks soft but not strict.
        self.assertEqual(pack.albedo_stats(self._img(60, 244))[1], ["albedo_soft_range"])


if __name__ == "__main__":
    unittest.main()
