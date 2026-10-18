"""Regenerate the small committed SciPy fixture used by the T2 stage tests."""
import json
import sys
from pathlib import Path

import numpy as np
from scipy.signal import sosfilt

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "reference"))
import timing_analyzer as ref


def main():
    coef = ref.load_coefficients()
    samples = np.random.default_rng(20261016).normal(size=256)
    samples[0] += 1  # exercise the zero-state impulse response as well
    filters = [*coef["bands"], coef["smooth"], coef["post"]]
    data = {"input": samples.tolist(), "outputs": [sosfilt(s, samples).tolist() for s in filters]}
    out = Path(__file__).resolve().parents[1] / "tests/TimingAnalyzer.Core.Tests/Fixtures/biquad.json"
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_text(json.dumps(data, indent=2) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()
