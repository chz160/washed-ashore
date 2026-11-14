import unittest

import numpy as np

import colour
import pixels


class ColourTests(unittest.TestCase):
    def test_round_trip_is_identity_for_every_8bit_value(self):
        v = np.arange(256, dtype=np.float64) / 255.0
        back = pixels.to_u8(colour.linear_to_srgb(colour.srgb_to_linear(v)))
        np.testing.assert_array_equal(back, np.arange(256, dtype=np.uint8))

    def test_known_points(self):
        self.assertAlmostEqual(float(colour.srgb_to_linear(np.array(0.5))), 0.214041140, places=8)
        self.assertEqual(float(colour.srgb_to_linear(np.array(0.0))), 0.0)
        self.assertAlmostEqual(float(colour.linear_to_srgb(np.array(1.0))), 1.0, places=12)


if __name__ == "__main__":
    unittest.main()
