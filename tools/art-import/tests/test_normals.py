import unittest

import numpy as np

import normals
import pixels
from tests import fixtures


class NormalTests(unittest.TestCase):
    def test_unit_length_after_round_trip(self):
        v = np.random.default_rng(3).random((16, 16, 3))
        n = normals.renormalise(normals.decode(v, "gl"))
        np.testing.assert_allclose(np.linalg.norm(n, axis=2), 1.0, atol=1e-12)

    def test_gray_normal_is_a_named_error(self):
        with self.assertRaisesRegex(ValueError, "at least 2 channels"):
            normals.decode(np.zeros((4, 4, 1)), "gl")

    def test_dx_negates_green(self):
        v = np.random.default_rng(4).random((4, 4, 3))
        gl = normals.decode(v, "gl")
        dx = normals.decode(v, "dx")
        np.testing.assert_allclose(dx[:, :, 1], -gl[:, :, 1])
        np.testing.assert_allclose(dx[:, :, [0, 2]], gl[:, :, [0, 2]])

    def test_flat_encodes_to_128_128_255(self):
        n = np.zeros((2, 2, 3))
        n[:, :, 2] = 1
        u8 = pixels.to_u8(normals.encode(n))
        np.testing.assert_array_equal(u8[0, 0], [128, 128, 255])

    def test_zero_vector_becomes_flat(self):
        n = normals.renormalise(np.zeros((1, 1, 3)))
        np.testing.assert_array_equal(n[0, 0], [0, 0, 1])

    def test_two_channel_reconstructs_z(self):
        v = np.full((1, 1, 2), 0.5)
        n = normals.decode(v, "gl")
        np.testing.assert_allclose(n[0, 0], [0, 0, 1], atol=1e-12)

    def test_detector(self):
        hf = fixtures.heightfield(64, 64)
        gl = normals.decode(fixtures.gl_normal_from_height(hf), "gl")
        self.assertEqual(normals.detect_y_convention(gl, hf)[0], "gl")
        dx = gl.copy()
        dx[:, :, 1] *= -1
        self.assertEqual(normals.detect_y_convention(dx, hf)[0], "dx")
        noise = normals.decode(np.random.default_rng(9).random((64, 64, 3)), "gl")
        self.assertIsNone(normals.detect_y_convention(noise, hf)[0])


if __name__ == "__main__":
    unittest.main()
