"""Beat model data (docs/PHASE4-PLAN.md N1): log-mel spectrograms and mapper beat targets.

For each <key>.pcm (22 050 Hz float32 mono from `timing-eval dump-set`) and <key>.json (the map's red lines), writes
<key>.npz: mel (frames x 128, float16, log), beat and downbeat targets (frames, uint8: 2 = on the beat frame, 1 = the
frame either side), and deletes the .pcm (the raw audio is 20 MB per song; the mel is about 1 MB).

    python tools/beatnet/data.py G:/tempomap-ml/cache

The mel front end is what the editor will reimplement in C#, so it is kept simple: Hann window 2048, hop 220.5 →
rounded per frame (100 fps exactly), 128 triangular mel bands 30-11 000 Hz (HTK formula), log(1 + 1000·power).
"""
import json
import os
import sys

import numpy as np

RATE = 22050
FPS = 100
NFFT = 2048
MELS = 128
FMIN, FMAX = 30.0, 11000.0


def mel_filters():
    def hz_to_mel(f): return 2595.0 * np.log10(1.0 + f / 700.0)
    def mel_to_hz(m): return 700.0 * (10 ** (m / 2595.0) - 1.0)
    edges = mel_to_hz(np.linspace(hz_to_mel(FMIN), hz_to_mel(FMAX), MELS + 2))
    bins = np.fft.rfftfreq(NFFT, 1.0 / RATE)
    fb = np.zeros((MELS, len(bins)), dtype=np.float32)
    for m in range(MELS):
        lo, mid, hi = edges[m], edges[m + 1], edges[m + 2]
        up = (bins - lo) / (mid - lo)
        down = (hi - bins) / (hi - mid)
        fb[m] = np.maximum(0, np.minimum(up, down))
    return fb


FILTERS = mel_filters()
WINDOW = np.hanning(NFFT).astype(np.float32)


def log_mel(x):
    """Frame t is centred on sample round(t * RATE / FPS)."""
    frames = int(len(x) * FPS // RATE)
    pad = np.pad(x, (NFFT // 2, NFFT // 2))
    centres = np.round(np.arange(frames) * RATE / FPS).astype(np.int64)
    out = np.empty((frames, MELS), dtype=np.float32)
    for s in range(0, frames, 2048):
        idx = centres[s:s + 2048, None] + np.arange(NFFT)[None, :]
        spec = np.abs(np.fft.rfft(pad[idx] * WINDOW, axis=1)) ** 2
        out[s:s + 2048] = np.log1p(1000.0 * (spec @ FILTERS.T) / NFFT)
    return out


def targets(meta, frames):
    """Beat / downbeat frames of the map's grid, from the first red line to the last note (else the audio end)."""
    reds = meta["reds"]
    end = meta["last_note_ms"] or meta["duration_ms"]
    end = min(end, meta["duration_ms"])
    beat = np.zeros(frames, dtype=np.uint8)
    down = np.zeros(frames, dtype=np.uint8)
    for i, (t0, length, meter, omit) in enumerate(reds):
        t1 = reds[i + 1][0] if i + 1 < len(reds) else end
        # Gimmick red lines (Barely Alive & Eliminate - Crankin has a 6e-96 ms beat for 0.01 ms) are no beat grid:
        # under 50 ms per beat (1 200 BPM) the span stays unlabelled; walking it beat by beat never ended.
        if length < 50 or t1 <= t0:
            continue
        k = 0
        while True:
            t = t0 + k * length
            if t >= t1 - 1e-6 or t > end:
                break
            f = int(round(t * FPS / 1000.0))
            is_down = (k % max(1, meter) == 0) and not (k == 0 and omit)
            for arr, on in ((beat, True), (down, is_down)):
                if not on:
                    continue
                for d, v in ((-1, 1), (0, 2), (1, 1)):
                    if 0 <= f + d < frames:
                        arr[f + d] = max(arr[f + d], v)
            k += 1
    return beat, down


def main():
    cache = sys.argv[1]
    keys = sorted(f[:-5] for f in os.listdir(cache) if f.endswith(".json"))
    made = 0
    for i, key in enumerate(keys):
        npz, pcm = os.path.join(cache, key + ".npz"), os.path.join(cache, key + ".pcm")
        if os.path.exists(npz):
            if os.path.exists(pcm): os.remove(pcm)
            continue
        if not os.path.exists(pcm):
            continue
        meta = json.load(open(os.path.join(cache, key + ".json"), encoding="utf-8"))
        x = np.fromfile(pcm, dtype=np.float32)
        mel = log_mel(x)
        beat, down = targets(meta, len(mel))
        np.savez(npz + ".tmp.npz", mel=mel.astype(np.float16), beat=beat, down=down)
        os.replace(npz + ".tmp.npz", npz)
        os.remove(pcm)
        made += 1
        if made % 50 == 0:
            print(f"{i + 1}/{len(keys)}", flush=True)
    print(f"made {made} mel files; {len(keys)} songs in {cache}")


if __name__ == "__main__":
    main()
