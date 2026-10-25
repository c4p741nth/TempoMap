"""Known injected delays check the measurement independently of either player's clock."""
import sys
import tempfile
import unittest
from pathlib import Path
import zipfile

import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "tools"))
import osu_audio_calibration as calibration


class AudioCalibrationTests(unittest.TestCase):
    @staticmethod
    def recording(rate, delay):
        audio = calibration.reference_audio(rate).astype(np.float64)
        t = np.arange(round(rate * .015)) / rate
        tick = .15 * np.cos(2 * np.pi * 1800 * t) * np.exp(-t * 300)
        for start in range(rate, 23 * rate, rate // 2):
            offset = start + round(rate * delay / 1000)
            audio[offset:offset + len(tick), :] += tick[:, None]
        # A common recording delay must cancel from the measured relative delay.
        return np.pad(audio, ((round(rate * .137), 0), (0, 0)))

    def test_reports_known_early_zero_and_late_ticks_at_both_sample_rates(self):
        for rate in (44100, 48000):
            for delay in (-25, 0, 20):
                with self.subTest(rate=rate, delay=delay):
                    result = calibration.measure(self.recording(rate, delay), rate)
                    self.assertAlmostEqual(result["tick_late_ms"], delay, delta=.5)
                    self.assertEqual(result["copy_shift_ms"], -delay)
                    self.assertGreaterEqual(result["pairs"], 40)

    def test_rejects_mono_missing_ticks_mixed_channels_and_clipping(self):
        audio = self.recording(44100, 20)
        for bad in (audio.mean(axis=1), calibration.reference_audio(),
                    np.tile(audio.mean(axis=1)[:, None], (1, 2)), audio * 5):
            with self.subTest(shape=bad.shape):
                with self.assertRaises(ValueError):
                    calibration.measure(bad, 44100)

    def test_counts_only_reference_pulses_when_tick_channels_do_not_cancel(self):
        rate = 48000
        audio = calibration.reference_audio(rate).astype(np.float64) * .15
        t = np.arange(round(rate * .015)) / rate
        tick = .15 * np.cos(2 * np.pi * 1800 * t) * np.exp(-t * 300)
        for start in range(0, 24 * rate, rate // 2):
            offset = start + round(rate * .022)
            audio[offset:offset + len(tick), 0] += tick * 1.3
            audio[offset:offset + len(tick), 1] += tick
        result = calibration.measure(audio, rate)
        # There are 44 reference pulses; the first and last are omitted from the estimate.
        self.assertEqual(result['pairs'], 42)
        self.assertAlmostEqual(result['tick_late_ms'], 22, delta=.5)
        self.assertEqual(result['copy_shift_ms'], -22)

    def test_creates_importable_package_from_synthetic_audio(self):
        with tempfile.TemporaryDirectory() as folder:
            package = calibration.create(Path(folder))
            with zipfile.ZipFile(package) as archive:
                self.assertIn("reference.wav", archive.namelist())
                map_name = next(name for name in archive.namelist() if name.endswith(".osu"))
                self.assertIn(b"1000,500,4,1,0,100,1,0", archive.read(map_name))

    def test_reports_rare_early_ticks_hidden_by_tenth_percentile(self):
        rate = 48000
        audio = self.recording(rate, 2)
        t = np.arange(round(rate * .015)) / rate
        tick = .15 * np.cos(2 * np.pi * 1800 * t) * np.exp(-t * 300)
        for index in (2, 15, 35):
            at = round(rate * .137) + rate + index * (rate // 2) + round(rate * .002)
            audio[at:at + len(tick), :] -= tick[:, None]
            early = at - round(rate * .010)
            audio[early:early + len(tick), :] += tick[:, None]
        result = calibration.measure(audio, rate)
        self.assertGreater(result['p10_ms'], 0)  # Three of 42 early ticks are below the percentile's coverage.
        self.assertAlmostEqual(result['min_ms'], -8, delta=.1)
        self.assertAlmostEqual(result['max_ms'], 2, delta=.1)
        self.assertAlmostEqual(result['range_ms'], 10, delta=.1)
        self.assertIn('median only', result['note'])


if __name__ == "__main__":
    unittest.main()
