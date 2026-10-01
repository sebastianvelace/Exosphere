#!/usr/bin/env python3
"""Exercise solar-state acceptance with the observed mislabeled-night regression."""
import importlib.util
import unittest
from pathlib import Path

spec = importlib.util.spec_from_file_location("render_state", Path(__file__).resolve().parents[1] / "validate_atmosphere_render_state.py")
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


def fixture(expected, actual, override="none"):
    return (f"ATMOS_APPLY slug=case targetSunElevation={expected}\n"
            f"ATMOS_RENDER slug=case sunElevation={actual} sunOverride={override} source=bound_sky_parameters\n")


class SolarStateTests(unittest.TestCase):
    def test_bound_daylight_cannot_pass_as_night(self):
        with self.assertRaisesRegex(ValueError, "Wrong rendered Sun"):
            module.validate(fixture(-35, 28))

    def test_unobserved_or_nonfinite_sky_fails(self):
        for log in ["ATMOS_APPLY slug=case targetSunElevation=-35", fixture(-35, "nan")]:
            with self.subTest(log=log), self.assertRaises(ValueError):
                module.validate(log)

    def test_matching_physical_fixtures_pass(self):
        for angle in [45, -1, 1, -35]:
            self.assertEqual(1, module.validate(fixture(angle, angle)))

    def test_override_is_rejected_even_with_matching_angle(self):
        with self.assertRaisesRegex(ValueError, "presentation override"):
            module.validate(fixture(45, 45, "active"))


if __name__ == "__main__":
    unittest.main()
