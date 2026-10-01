#!/usr/bin/env python3
"""Verify real decoding and prevent fabricated mission-clock alignment."""
import importlib.util
import json
import subprocess
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
TOOL = ROOT / "tools/extract_reference_frames.py"
spec = importlib.util.spec_from_file_location("reference_frames", TOOL)
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class ReferenceFrameTests(unittest.TestCase):
    def test_alignment_requires_observed_clock(self):
        with self.assertRaisesRegex(ValueError, "clock evidence"):
            module.validate_windows([{"start_s": 0, "end_s": 1,
                                     "mission_time_at_start_s": 20}], 2)

    def test_nonfinite_and_outside_video_windows_are_rejected(self):
        for start, end in [(0, 3), (float("nan"), 1), (1, 0)]:
            with self.subTest(start=start, end=end), self.assertRaises(ValueError):
                module.validate_windows([{"start_s": start, "end_s": end}], 2)

    def test_decoded_frames_keep_video_and_mission_times_distinct(self):
        with tempfile.TemporaryDirectory() as temp:
            path = Path(temp)
            video, plan, output = path / "source.mp4", path / "plan.json", path / "frames"
            subprocess.run(["ffmpeg", "-hide_banner", "-loglevel", "error", "-f", "lavfi",
                            "-i", "testsrc2=size=64x64:rate=10:duration=2", str(video)], check=True)
            plan.write_text(json.dumps({"windows": [{"start_s": 0, "end_s": 2,
                "mission_time_at_start_s": 600, "clock_evidence": "synthetic test anchor"}]}))
            subprocess.run(["python3", str(TOOL), str(video), str(plan), str(output)], check=True)
            manifest = json.loads((output / "index.json").read_text())
            self.assertEqual([0, 1], [r["video_time_s"] for r in manifest["frames"]])
            self.assertEqual([600, 601], [r["mission_time_s"] for r in manifest["frames"]])
            self.assertEqual(64, len(manifest["video_sha256"]))
            self.assertTrue(all((output / r["file"]).stat().st_size > 100 for r in manifest["frames"]))
            # A different recording must not inherit the annotated mission clock.
            plan.write_text(json.dumps({"video_sha256": "0" * 64,
                "windows": [{"start_s": 0, "end_s": 1}]}))
            rejected = path / "rejected"
            result = subprocess.run(["python3", str(TOOL), str(video), str(plan), str(rejected)],
                                    capture_output=True, text=True)
            self.assertNotEqual(0, result.returncode)
            self.assertIn("does not match the source track", result.stderr)
            self.assertFalse(rejected.exists())


if __name__ == "__main__":
    unittest.main()
