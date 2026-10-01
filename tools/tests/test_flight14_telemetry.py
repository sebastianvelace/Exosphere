import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

spec = importlib.util.spec_from_file_location("flight14_compare", Path(__file__).resolve().parents[1] / "compare_flight14_telemetry.py")
comparison = importlib.util.module_from_spec(spec)
spec.loader.exec_module(comparison)


def reference(times):
    return {"speed_frame": "not_specified_by_broadcast", "physics_authority": False,
            "anchors": [{"displayed_elapsed_seconds": t, "displayed_speed_kmh": 3600,
                         "displayed_altitude_km": 10, "view": "fixture", "frame_sha256": "test"} for t in times]}


class Flight14TelemetryTests(unittest.TestCase):
    def test_reports_both_frames_without_rotation_or_speed_inference(self):
        rows = [{"missionElapsedSeconds": 10, "geodeticAltitudeM": 10000,
                 "radialAltitudeM": 13000, "inertialSpeedMps": 1400,
                 "atmosphereRelativeSpeedMps": 1000}]
        report = comparison.compare(rows, reference([10]), 1)[0]
        self.assertEqual(report["residuals"], {"geodeticAltitudeM": 0, "radialAltitudeM": 3000,
            "inertialSpeedMps": 400, "atmosphereRelativeSpeedMps": 0})

    def test_missing_coverage_and_large_gaps_are_not_interpolated_or_extrapolated(self):
        rows = [{"missionElapsedSeconds": 10}, {"missionElapsedSeconds": 100}]
        reports = comparison.compare(rows, reference([9, 50, 101]), 2)
        self.assertTrue(all(r["status"] == "missing-sample" for r in reports))
        self.assertTrue(all("residuals" not in r for r in reports))

    def test_real_sample_offset_and_phase_remain_explicit(self):
        rows = [{"missionElapsedSeconds": 12, "phase": "COAST", "inertialSpeedMps": 1000}]
        report = comparison.compare(rows, reference([12]), 0)[0]
        self.assertEqual(report["samplePhase"], "COAST")
        self.assertEqual(report["sampleOffsetSeconds"], 0)
        self.assertNotIn("atmosphereRelativeSpeedMps", report["residuals"])

    def read_text(self, text, epoch=None):
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder)/"trace.log"
            path.write_text(text)
            return comparison.read_samples(path, epoch)

    def test_legacy_trace_requires_explicit_liftoff_epoch(self):
        text = "TRACE_ASCENT t=20 mission=ASCENT_SH alt=10000 spd=1000\n"
        with self.assertRaisesRegex(ValueError, "launch-clock"):
            self.read_text(text)
        self.assertEqual(self.read_text(text, 5)[0]["missionElapsedSeconds"], 15)

    def test_recorded_callback_epoch_does_not_accept_conflicting_manual_alignment(self):
        text = "FLIGHT_CLOCK liftoffEpoch=5 source=LaunchCommitted\nTRACE_ASCENT t=20 mission=ASCENT_SH alt=10000 spd=1000\n"
        self.assertEqual(self.read_text(text)[0]["missionElapsedSeconds"], 15)
        with self.assertRaisesRegex(ValueError, "disagrees"):
            self.read_text(text, 3)

    def test_rewind_and_nonfinite_physics_are_rejected(self):
        for rows in ([{"missionElapsedSeconds": 20}, {"missionElapsedSeconds": 10}],
                     [{"missionElapsedSeconds": 20, "inertialSpeedMps": float("nan")}],
                     [{"t": 20}]):
            with self.assertRaises(ValueError):
                self.read_text("\n".join(json.dumps(r) for r in rows))

    def test_explicit_elapsed_preserves_precision_and_rejects_conflicting_trace_clock(self):
        text = "FLIGHT_CLOCK liftoffEpoch=5.023 source=LaunchCommitted\nTRACE_ASCENT t=20.0 mission=ASCENT_SH alt=10000 spd=1000 missionElapsed=14.951\n"
        self.assertEqual(self.read_text(text)[0]["missionElapsedSeconds"], 14.951)
        with self.assertRaisesRegex(ValueError, "mission clock disagrees"):
            self.read_text(text.replace("14.951", "14.7"))

    def test_nonfinite_manual_epoch_is_rejected_even_with_a_recorded_callback(self):
        text = "FLIGHT_CLOCK liftoffEpoch=5 source=LaunchCommitted\nTRACE_ASCENT t=20 mission=ASCENT_SH alt=10000 spd=1000\n"
        with self.assertRaisesRegex(ValueError, "must be finite"):
            self.read_text(text, float("nan"))

    def test_preroll_without_launch_clock_is_not_a_flight_sample(self):
        text = "TRACE_ASCENT t=0 mission=IGNITION alt=0 spd=0 missionElapsed=NaN\nTRACE_ASCENT t=20 mission=ASCENT_SH alt=10000 spd=1000 missionElapsed=15\n"
        self.assertEqual(len(self.read_text(text)), 1)


if __name__ == "__main__":
    unittest.main()
