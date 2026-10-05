import importlib.util
from pathlib import Path
import unittest

spec = importlib.util.spec_from_file_location("booster_comparison", Path(__file__).parents[1] / "compare_flight14_booster.py")
comparison = importlib.util.module_from_spec(spec)
spec.loader.exec_module(comparison)


class BoosterComparisonTests(unittest.TestCase):
    def setUp(self):
        self.sample = {"missionElapsedSeconds": 240.02, "phase": "Coast", "geodeticAltitudeM": 99000,
                       "atmosphereRelativeSpeedMps": 250, "bodyCentredInertialSpeedMps": 450}
        self.reference = {"physics_authority": False, "source_url": "https://example.test/reference", "anchors": [
            {"broadcast_met_seconds": 240, "booster_altitude_display_km": 98.9,
             "booster_speed_display_kmh": 977, "capture_sha256": "fixture"}]}

    def test_reports_units_and_distinct_velocity_frames_without_acceptance_gate(self):
        report = comparison.compare([self.sample], [self.reference])
        row = report["anchors"][0]
        self.assertEqual(100, row["altitude_display_residual_m"])
        self.assertEqual(-77, row["atmosphere_relative_speed_display_residual_kmh"])
        self.assertEqual(643, row["body_centred_inertial_speed_display_residual_kmh"])
        self.assertFalse(report["physics_authority"])
        self.assertEqual("reference-comparison-only", report["status"])

    def test_missing_clock_is_not_replaced_by_last_terminal_state(self):
        self.sample["missionElapsedSeconds"] = 230
        row = comparison.compare([self.sample], [self.reference])["anchors"][0]
        self.assertEqual("model-sample-missing", row["status"])
        self.assertNotIn("altitude_display_residual_m", row)

    def test_legacy_trace_does_not_invent_an_inertial_speed(self):
        del self.sample["bodyCentredInertialSpeedMps"]
        row = comparison.compare([self.sample], [self.reference])["anchors"][0]
        self.assertNotIn("model_body_centred_inertial_speed_kmh", row)

    def test_rejects_authoritative_reference_and_nonfinite_or_unordered_samples(self):
        self.reference["physics_authority"] = True
        with self.assertRaises(ValueError):
            comparison.compare([self.sample], [self.reference])
        self.reference["physics_authority"] = False
        with self.assertRaises(ValueError):
            comparison.compare([self.sample, self.sample], [self.reference])
        self.sample["geodeticAltitudeM"] = float("nan")
        with self.assertRaises(ValueError):
            comparison.compare([self.sample], [self.reference])

    def test_separates_coast_and_terminal_readings_and_ignores_qualitative_board(self):
        self.reference["anchors"].append({"broadcast_met_seconds": 180, "booster_engine_board": "unlit"})
        self.reference["anchors"][0]["booster_altitude_display_km"] = 0.1
        report = comparison.compare([self.sample], [self.reference])
        self.assertEqual(1, len(report["anchors"]))
        self.assertEqual(0, report["summaries"]["coast"]["altitude_display_residual_m"]["count"])
        self.assertEqual(1, report["summaries"]["terminal"]["altitude_display_residual_m"]["count"])


if __name__ == "__main__":
    unittest.main()
