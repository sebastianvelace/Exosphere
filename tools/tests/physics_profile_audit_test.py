"""Evidence guards for interrupted, empty and skipped profile audit runs."""

import importlib.util
from contextlib import redirect_stdout
import io
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch

SPEC = importlib.util.spec_from_file_location("profile_audit", Path(__file__).resolve().parents[1] / "physics_profile_audit.py")
AUDIT = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(AUDIT)


class ProfileEvidenceTests(unittest.TestCase):
    def invoke(self, output, *extra):
        with patch.object(sys, "argv", ["audit", "--output", str(output), *extra]), \
                patch.object(AUDIT, "identities", return_value={"source": "current"}), \
                patch.object(AUDIT, "MATRIX", [("Profile", "variant", ["Case.Flight"], "test scope")]), \
                patch.object(AUDIT, "tank_coverage", return_value={}), redirect_stdout(io.StringIO()):
            return AUDIT.main()

    def test_interrupted_rerun_cannot_certify_the_old_success(self):
        with tempfile.TemporaryDirectory() as directory:
            output = Path(directory)
            for name in ("profiles.trx", "report.json", "test-exit-code.txt"):
                (output / name).write_text("old success")
            with patch.object(AUDIT.subprocess, "run", side_effect=KeyboardInterrupt):
                with self.assertRaises(KeyboardInterrupt):
                    self.invoke(output, "--no-build")
            self.assertFalse((output / "profiles.trx").exists())
            self.assertFalse((output / "report.json").exists())
            self.assertFalse((output / "test-exit-code.txt").exists())
            with self.assertRaises(FileNotFoundError):
                self.invoke(output, "--verify-only")

    def test_successful_process_without_new_trx_cannot_reuse_old_results(self):
        with tempfile.TemporaryDirectory() as directory:
            output = Path(directory)
            (output / "profiles.trx").write_text("old success")
            with patch.object(AUDIT.subprocess, "run", return_value=subprocess.CompletedProcess([], 0)):
                with self.assertRaises(FileNotFoundError):
                    self.invoke(output, "--no-build")

    def test_empty_and_skipped_results_fail_while_complete_evidence_passes(self):
        for outcome in (None, "NotExecuted", "Passed"):
            with self.subTest(outcome=outcome), tempfile.TemporaryDirectory() as directory:
                output = Path(directory)
                (output / "inputs.json").write_text(json.dumps({"source": "current"}))
                (output / "test-exit-code.txt").write_text("0")
                row = "" if outcome is None else f'<UnitTestResult testName="Case.Flight" outcome="{outcome}"/>'
                (output / "profiles.trx").write_text(
                    '<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010"><Results>'
                    + row + '</Results></TestRun>')
                self.assertEqual(0 if outcome == "Passed" else 1, self.invoke(output, "--verify-only"))


if __name__ == "__main__":
    unittest.main()
