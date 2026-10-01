"""Behavior checks for the clear-image feature mask used by the rendered A/B gate."""
import importlib.util
from pathlib import Path
import unittest
from PIL import Image

spec = importlib.util.spec_from_file_location("cloud_gate", Path(__file__).parents[1] / "validate_cloud_occlusion.py")
gate = importlib.util.module_from_spec(spec)
spec.loader.exec_module(gate)


class CloudContrastTests(unittest.TestCase):
    def images(self, attenuated=True):
        clear = Image.new("RGB", (80, 80), (80, 80, 80))
        obscured = clear.copy()
        for y in range(20, 51):
            clear.putpixel((40, y), (240, 240, 240))
            obscured.putpixel((40, y), (120, 120, 120) if attenuated else (240, 240, 240))
        return clear, obscured

    def test_resolves_a_one_pixel_hull(self):
        clear, obscured = gate.hull_feature_contrast(*self.images(), 40, 20, 50)
        self.assertAlmostEqual(obscured / clear, 0.25)

    def test_unchanged_hull_cannot_pass(self):
        clear, obscured = gate.hull_feature_contrast(*self.images(False), 40, 20, 50)
        self.assertGreaterEqual(obscured / clear, 1)

    def test_a_changed_background_invalidates_comparison(self):
        images = self.images()
        images[1].putpixel((30, 25), (120, 120, 120))
        with self.assertRaisesRegex(AssertionError, "background changed"):
            gate.hull_feature_contrast(*images, 40, 20, 50)

    def test_unresolved_hull_is_not_occlusion_evidence(self):
        image = Image.new("RGB", (80, 80), (80, 80, 80))
        with self.assertRaisesRegex(AssertionError, "insufficient"):
            gate.hull_feature_contrast(image, image, 40, 20, 50)


if __name__ == "__main__":
    unittest.main()
