"""Beat model inference for evaluation (docs/PHASE4-PLAN.md N3): beat times per song, for timing-eval.

    python tools/beatnet/infer.py <model.pt> <cacheDir> <envCacheDir>

For every <key>.npz in cacheDir (mel from data.py; keys are timing-eval's envelope cache keys), runs the model and
writes <envCacheDir>/<key>.beats: one beat time per line in seconds of the decoded audio, the format
EnvelopeCache.ExternalBeats reads. Peaks are picked as in training (the checkpoint's threshold, 70 ms apart), then
refined to the onset envelope by the C# side (MultiBpmOptions.ExternalBeats). The beats are osu! times (the map's grid),
so score with `timing-eval run <set> multi ext=1 lag=28.7` (the envelope peak sits 28.7 ms after the osu! time,
MultiBpmAnalyzer.PeakToOsuMs): with lag 0 every beat refined to the onset before the hit (probe on 500 songs:
A hold aligned 0.634 at lag 0, 0.810 at 28.7).
"""
import glob
import os
import sys

import numpy as np
import torch

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from train import BeatNet, peaks, FPS  # noqa: E402


def main():
    ckpt_path, cache, env_cache = sys.argv[1:4]
    ckpt = torch.load(ckpt_path, map_location="cpu")
    device = "cuda" if torch.cuda.is_available() else "cpu"
    model = BeatNet().to(device)
    model.load_state_dict(ckpt["model"])
    model.eval()
    threshold = ckpt["threshold"]
    n = 0
    for npz in sorted(glob.glob(os.path.join(cache, "*.npz"))):
        key = os.path.basename(npz)[:-4]
        mel = np.load(npz)["mel"].astype(np.float32)
        with torch.no_grad():
            act = torch.sigmoid(model(torch.from_numpy(mel).unsqueeze(0).to(device)))[0, :, 0].cpu().numpy()
        frames = peaks(act, threshold)
        # Sub-frame position: parabola through the peak and its neighbours.
        times = []
        for f in frames:
            a, b, c = act[max(f - 1, 0)], act[f], act[min(f + 1, len(act) - 1)]
            den = a - 2 * b + c
            off = 0.5 * (a - c) / den if den < 0 else 0.0
            times.append((f + off) / FPS)
        with open(os.path.join(env_cache, key + ".beats"), "w", encoding="utf-8") as fh:
            fh.write("\n".join(f"{t:.4f}" for t in times) + "\n")
        n += 1
    print(f"wrote beats for {n} songs (threshold {threshold}) -> {env_cache}")


if __name__ == "__main__":
    main()
