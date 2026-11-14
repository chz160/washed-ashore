import unittest

from maps import classify


class MapNameTests(unittest.TestCase):
    def test_ambientcg(self):
        cases = {
            "Ground078_1K-JPG_Color.jpg": "albedo", "Ground078_1K-JPG_NormalGL.jpg": "normal_gl",
            "Ground078_1K-JPG_NormalDX.jpg": "normal_dx", "Ground078_1K-JPG_Roughness.jpg": "roughness",
            "Ground078_1K-JPG_AmbientOcclusion.jpg": "ao", "Ground078_1K-JPG_Displacement.jpg": "height",
            "Rock051_1K-JPG_Metalness.jpg": "metallic", "Leaf001_2K-PNG_Opacity.png": "opacity",
        }
        for name, role in cases.items():
            self.assertEqual(classify(name), role, name)

    def test_poly_haven(self):
        cases = {
            "rocky_trail_diff_2k.jpg": "albedo", "x_nor_gl_2k.png": "normal_gl", "x_nor_dx_2k.png": "normal_dx",
            "x_rough_ao_2k.png": "packed_rough_ao", "x_arm_2k.png": "packed_arm", "x_rough_2k.png": "roughness",
            "x_ao_2k.jpg": "ao", "x_disp_2k.png": "height", "metal_plate_diff_2k.jpg": "albedo",
        }
        for name, role in cases.items():
            self.assertEqual(classify(name), role, name)

    def test_megascans(self):
        self.assertEqual(classify("vdbmbcd_4K_BaseColor.jpg"), "albedo")
        self.assertEqual(classify("vdbmbcd_4K_Normal.jpg"), "normal_unknown")
        self.assertEqual(classify("vdbmbcd_4K_Gloss.jpg"), "gloss")
        self.assertEqual(classify("vdbmbcd_4K_Height.exr"), None)

    def test_not_maps(self):
        for name in ("Ground037.png", "Ground037_1K-JPG.blend", "Ground037_1K-JPG.usdc", "Ground037_1K-JPG.mtlx",
                     "Ground037_1K-JPG.tres", "readme.txt", "x_nor_2k.png"):
            self.assertIsNone(classify(name), name)

    def test_case_and_ext(self):
        self.assertEqual(classify("A_COLOR.JPEG"), "albedo")
        self.assertEqual(classify("dir/A_Color.jpg"), "albedo")


if __name__ == "__main__":
    unittest.main()
