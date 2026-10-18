"""Beat This! probe (docs/PHASE3-STATUS.md, "ML beat tracker probe"): outside beats for the eval sets.

  .venv-ml\\Scripts\\python tools\\beat_probe.py tests\\mania-songs.tsv tests\\multibpm-songs.tsv [--checkpoint final0]

For each song's audio (timing-eval audio-keys) the samples come from our own decoder (timing-eval dump-audio), so
the beats share the onset envelope's time base; Beat This! (CPJKU, MIT licence for code and weights) runs on them
and .eval-cache/<key><suffix>.beats gets one beat per line: seconds, then 1 for a downbeat. Existing files are kept.
Then: timing-eval ext-lag <tsv>, and timing-eval run <tsv> multi ext=1 lag=<ms>.

Setup: py -3.13 -m venv .venv-ml; pip install torch torchaudio --index-url https://download.pytorch.org/whl/cpu;
pip install numpy soxr einops rotary-embedding-torch tqdm soundfile beat-this
"""
import argparse
import os
import subprocess
import sys
import time

import numpy as np
from beat_this.inference import Audio2Beats


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("tsv", nargs="+")
    ap.add_argument("--checkpoint", default="final0")
    ap.add_argument("--device", default="cpu")
    ap.add_argument("--suffix", default="", help="cache name suffix, to keep several checkpoints' beats apart")
    ap.add_argument("--limit", type=int, default=0)
    args = ap.parse_args()

    repo = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    exe = os.path.join(repo, "tools", "TimingAnalyzer.Eval", "bin", "Release", "net10.0", "win-x64", "timing-eval.exe")
    cache = os.path.join(repo, ".eval-cache")
    os.makedirs(cache, exist_ok=True)
    keys = subprocess.run([exe, "audio-keys", *args.tsv], capture_output=True, text=True, check=True).stdout.splitlines()
    if args.limit:
        keys = keys[: args.limit]
    model = Audio2Beats(checkpoint_path=args.checkpoint, device=args.device, dbn=False)
    done = skipped = 0
    started = time.time()
    for line in keys:
        key, audio = line.split("\t", 1)
        out = os.path.join(cache, key + args.suffix + ".beats")
        if os.path.exists(out):
            skipped += 1
            continue
        raw = os.path.join(cache, key + ".f32.tmp")
        try:
            subprocess.run([exe, "dump-audio", audio, raw], check=True, capture_output=True)
            with open(raw, "rb") as f:
                rate, count = (int(v) for v in np.frombuffer(f.read(8), dtype="<i4"))
                signal = np.fromfile(f, dtype="<f4", count=count)
        finally:
            if os.path.exists(raw):
                os.remove(raw)
        t0 = time.time()
        beats, downbeats = model(signal, rate)
        down = set(np.round(np.asarray(downbeats, dtype=float), 6))
        with open(out + ".tmp", "w", encoding="utf-8", newline="\n") as f:
            for b in np.asarray(beats, dtype=float):
                f.write(f"{b:.6f}\t{1 if round(b, 6) in down else 0}\n")
        os.replace(out + ".tmp", out)
        done += 1
        print(f"{done + skipped:4d}/{len(keys)}  {len(beats):5d} beats  {count / rate:6.1f} s audio  {time.time() - t0:5.1f} s  {os.path.basename(os.path.dirname(audio))[:60]}", flush=True)
    print(f"done {done}, kept {skipped}, {time.time() - started:.0f} s", flush=True)
    return 0


if __name__ == "__main__":
    sys.exit(main())
