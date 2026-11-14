import unittest

import numpy as np

import resample


class ResampleTests(unittest.TestCase):
    def test_constant_stays_constant(self):
        a = np.full((64, 32, 3), 0.37)
        out = resample.resize(a, 16, 8)
        np.testing.assert_allclose(out, 0.37, atol=1e-12)

    def test_same_size_is_identity(self):
        a = np.random.default_rng(0).random((32, 32, 1))
        self.assertIs(resample.resize(a, 32, 32), a)

    def test_rows_sum_to_one(self):
        w = resample.weights(1024, 256)
        np.testing.assert_allclose(w.sum(axis=1), 1.0, atol=1e-12)

    def test_wrap_keeps_a_periodic_signal_periodic(self):
        x = np.arange(64)
        a = np.sin(2 * np.pi * x / 64)[None, :, None].repeat(4, axis=0)
        out = resample.resize(a, 4, 16)[0, :, 0]
        expect = np.sin(2 * np.pi * (np.arange(16) * 4 + 1.5) / 64)
        np.testing.assert_allclose(out, expect, atol=0.02)
        # Edge columns aren't darkened or brightened compared with the interior.
        self.assertAlmostEqual(abs(out[0]), abs(out[8]), places=2)

    def test_aspect_is_kept(self):
        self.assertEqual(resample.ship_dims(512, 1024, 512), (256, 512))
        self.assertEqual(resample.ship_dims(512, 1024, 1024), (512, 1024))

    def test_upscale_is_refused(self):
        with self.assertRaises(ValueError):
            resample.ship_dims(1024, 1024, 2048)


if __name__ == "__main__":
    unittest.main()
