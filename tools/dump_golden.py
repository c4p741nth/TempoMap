"""Dump reproducible float64 DSP stages: dump_golden.py <audio> <outdir>.

Raw arrays are little-endian float64. Uses the untouched Python reference, including
its decoder delay correction; C# stage tests read mono directly and bypass decoding.
"""
import argparse
import json
import sys
from pathlib import Path

import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "reference"))
import timing_analyzer as ref


def analyze_envelope(d, bpm=0.0, offset=None):
    result = {"status": None, "envelope_len": len(d)}
    if bpm < 1e-30:
        status, bpm = ref.detect_bpm(d)
        result["status"] = status
        if status < 0:
            return dict(result, error="cannot determine timing")
        if status >= 16:
            return dict(result, approx_bpm=bpm)
        offset = None
    offset_int = ref.detect_offset(d, bpm) if offset is None else ref.fold_user_offset(offset, bpm)
    flags, marks = ref.build_timeline(d, bpm, offset_int)
    chars = {1: "#", 2: "+", 4: "-", 8: "."}
    return dict(result, bpm=bpm, offset_int=offset_int, offset=offset_int - ref.OFFSET_DISPLAY,
                flags=flags, timeline="".join(chars.get(int(v), " ") for v in marks))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("audio", type=Path)
    parser.add_argument("outdir", type=Path)
    args = parser.parse_args()
    args.outdir.mkdir(parents=True, exist_ok=True)
    mono, rate = ref.load_audio(args.audio)
    d = ref.onset_envelope(mono, rate, ref.load_coefficients())
    acf = ref.autocorrelate(d, 2000)
    for name, data in (("mono", mono), ("envelope", d), ("acf", acf)):
        np.asarray(data, dtype="<f8").tofile(args.outdir / (name + ".f64"))
    (args.outdir / "rate.txt").write_text(repr(rate) + "\n", encoding="utf-8")
    result = analyze_envelope(d)
    result.update(rate=rate, seconds=len(mono) / rate)
    modes = []
    if "bpm" in result:
        bpm = round(result["bpm"], 3)
        offset = round(result["offset"]) + 7.0
        for b, o in ((round(bpm / 2, 3), None), (bpm, offset)):
            modes.append(dict(input_bpm=b, input_offset=o, result=analyze_envelope(d, b, o)))
    result["modes"] = modes
    (args.outdir / "result.json").write_text(json.dumps(result, indent=2, allow_nan=False) + "\n", encoding="utf-8")
    print("Golden data written:", args.outdir)


if __name__ == "__main__":
    main()
