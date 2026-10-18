"""Negative controls for the external CLI parity harness (standard library only)."""
import subprocess
import sys
import unittest
from pathlib import Path
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "tools"))
import parity


class ParityTests(unittest.TestCase):
    def test_real_output_preserves_spaces_and_printed_precision(self):
        output = "Timeline   >>  # +   <<\nTime (min) >>0       <<\nBPM:     284.998\nOffset:      136\n"
        self.assertEqual(parity.parse_output(output), {"bpm": "284.998", "offset": "136", "timeline": "  # +   "})

    def test_each_printed_field_can_fail_parity(self):
        original = {"bpm": "284.998", "offset": "136", "timeline": "  # +   "}
        for field, changed in (("bpm", "284.999"), ("offset", "137"), ("timeline", "  # +  ")):
            with self.subTest(field=field):
                self.assertFalse(parity.same(original, dict(original, **{field: changed})))
        self.assertTrue(parity.same(original, dict(original)))

    def test_empty_results_do_not_pass(self):
        self.assertFalse(parity.same({}, {}))

    @patch.object(parity.subprocess, "run")
    def test_crash_and_empty_candidate_are_rejected(self, run):
        for code, text in ((1, "BPM: 120.000\nOffset: 0"), (0, "unrelated banner")):
            run.return_value = subprocess.CompletedProcess([], code, stdout=text, stderr="")
            with self.assertRaises(RuntimeError):
                parity.run_candidate(Path("candidate.exe"), ["song.ogg", "0"])

    @patch.object(parity.subprocess, "run")
    def test_candidate_reads_timeline_from_stderr(self, run):
        run.return_value = subprocess.CompletedProcess([], 0, stdout="BPM: 120.000\nOffset: -7\n", stderr="Timeline   >> # <<\n")
        self.assertEqual(parity.run_candidate(Path("candidate.exe"), []), {"bpm": "120.000", "offset": "-7", "timeline": " # "})


if __name__ == "__main__":
    unittest.main()
