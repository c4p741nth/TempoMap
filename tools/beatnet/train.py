"""Beat model training (docs/PHASE4-PLAN.md N2): mapper beat and downbeat activations at 100 fps from log-mel frames.

    python tools/beatnet/train.py <cacheDir> <outDir> [--epochs N]

Song-grouped split: every 10th song (by cache key) is validation. Reports frame-level beat F-measure on validation at
±7 ms (a frame either side) after peak picking, and keeps the best checkpoint (model.pt) and its ONNX export.
"""
import argparse
import glob
import json
import os
import random
import time

import numpy as np
import torch
import torch.nn as nn
import torch.nn.functional as F

FPS = 100
CROP = 12 * FPS  # 12 s training crops


class TCNBlock(nn.Module):
    def __init__(self, ch, dilation, dropout):
        super().__init__()
        self.conv1 = nn.Conv1d(ch, ch, 5, padding=2 * dilation, dilation=dilation)
        self.conv2 = nn.Conv1d(ch, ch, 1)
        self.norm = nn.GroupNorm(8, ch)
        self.drop = nn.Dropout(dropout)

    def forward(self, x):
        y = F.gelu(self.norm(self.conv1(x)))
        return x + self.drop(self.conv2(y))


class BeatNet(nn.Module):
    """Mel (batch, frames, 128) -> logits (batch, frames, 2): beat, downbeat. Receptive field ~ 2 · 4 · Σ dilations frames."""

    def __init__(self, mels=128, ch=96, dilations=(1, 2, 4, 8, 16, 32, 64, 128), dropout=0.15):
        super().__init__()
        self.front = nn.Sequential(
            nn.Conv2d(1, 16, (3, 3), padding=(1, 1)), nn.GELU(), nn.MaxPool2d((1, 4)),
            nn.Conv2d(16, 32, (3, 3), padding=(1, 1)), nn.GELU(), nn.MaxPool2d((1, 4)),
        )
        self.proj = nn.Conv1d(32 * (mels // 16), ch, 1)
        self.blocks = nn.Sequential(*[TCNBlock(ch, d, dropout) for d in dilations])
        self.head = nn.Conv1d(ch, 2, 1)

    def forward(self, mel):
        b, t, m = mel.shape
        x = self.front(mel.unsqueeze(1))  # (b, 32, t, m/16)
        x = x.permute(0, 1, 3, 2).reshape(b, -1, t)
        x = self.blocks(self.proj(x))
        return self.head(x).transpose(1, 2)


def load_songs(cache):
    songs = []
    for npz in sorted(glob.glob(os.path.join(cache, "*.npz"))):
        z = np.load(npz)
        if (z["beat"] == 2).sum() < 16:
            continue
        songs.append((os.path.basename(npz)[:-4], z["mel"], z["beat"], z["down"]))
    return songs


def batches(songs, batch, rng):
    while True:
        xs, ys = [], []
        for _ in range(batch):
            key, mel, beat, down = songs[rng.randrange(len(songs))]
            if len(mel) <= CROP:
                s = 0
            else:
                s = rng.randrange(len(mel) - CROP)
            m = mel[s:s + CROP].astype(np.float32)
            y = np.stack([beat[s:s + CROP], down[s:s + CROP]], -1).astype(np.float32) / 2.0
            if len(m) < CROP:
                m = np.pad(m, ((0, CROP - len(m)), (0, 0)))
                y = np.pad(y, ((0, CROP - len(y)), (0, 0)))
            # Augment: random gain in the log domain, a little frequency masking.
            m = m + rng.uniform(-1.0, 1.0)
            if rng.random() < 0.5:
                f0 = rng.randrange(0, 112); m[:, f0:f0 + rng.randrange(4, 16)] = 0
            xs.append(m); ys.append(y)
        yield torch.from_numpy(np.stack(xs)), torch.from_numpy(np.stack(ys))


def peaks(act, threshold):
    """Local maxima above threshold, at least 7 frames (70 ms) apart."""
    p = np.where((act[1:-1] > act[:-2]) & (act[1:-1] >= act[2:]) & (act[1:-1] > threshold))[0] + 1
    out = []
    for i in p:
        if out and i - out[-1] < 7:
            if act[i] > act[out[-1]]: out[-1] = i
        else:
            out.append(i)
    return np.array(out, dtype=np.int64)


def f_measure(pred, ref, tol=1):
    if len(ref) == 0 or len(pred) == 0:
        return 0.0
    used, hits = set(), 0
    for p in pred:
        j = np.searchsorted(ref, p)
        for k in (j - 1, j):
            if 0 <= k < len(ref) and k not in used and abs(ref[k] - p) <= tol:
                used.add(k); hits += 1; break
    prec, rec = hits / len(pred), hits / len(ref)
    return 0.0 if hits == 0 else 2 * prec * rec / (prec + rec)


@torch.no_grad()
def evaluate(model, songs, device):
    model.eval()
    scores = {0.3: [], 0.4: [], 0.5: []}
    for key, mel, beat, down in songs:
        x = torch.from_numpy(mel.astype(np.float32)).unsqueeze(0).to(device)
        act = torch.sigmoid(model(x))[0, :, 0].cpu().numpy()
        ref = np.where(beat == 2)[0]
        for th in scores:
            scores[th].append(f_measure(peaks(act, th), ref))
    model.train()
    return {th: float(np.mean(v)) for th, v in scores.items()}


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("cache"); ap.add_argument("out")
    ap.add_argument("--epochs", type=int, default=40); ap.add_argument("--steps", type=int, default=300)
    ap.add_argument("--batch", type=int, default=16); ap.add_argument("--lr", type=float, default=1e-3)
    a = ap.parse_args()
    os.makedirs(a.out, exist_ok=True)
    rng = random.Random(7); torch.manual_seed(7)
    songs = load_songs(a.cache)
    val = [s for i, s in enumerate(songs) if i % 10 == 0]
    train = [s for i, s in enumerate(songs) if i % 10 != 0]
    print(f"{len(train)} training songs, {len(val)} validation songs", flush=True)
    device = "cuda" if torch.cuda.is_available() else "cpu"
    model = BeatNet().to(device)
    print(f"{sum(p.numel() for p in model.parameters()) / 1e6:.2f} M parameters on {device}", flush=True)
    opt = torch.optim.AdamW(model.parameters(), lr=a.lr, weight_decay=1e-4)
    sched = torch.optim.lr_scheduler.OneCycleLR(opt, max_lr=a.lr, total_steps=a.epochs * a.steps)
    pos = torch.tensor([4.0, 12.0], device=device)  # positives are rare (about 1 frame in 30, downbeats 1 in 120)
    gen = batches(train, a.batch, rng)
    best, history = -1.0, []
    for epoch in range(a.epochs):
        t0, total = time.time(), 0.0
        for _ in range(a.steps):
            x, y = next(gen)
            x, y = x.to(device), y.to(device)
            loss = F.binary_cross_entropy_with_logits(model(x), y, pos_weight=pos)
            opt.zero_grad(); loss.backward(); nn.utils.clip_grad_norm_(model.parameters(), 1.0); opt.step(); sched.step()
            total += loss.item()
        f = evaluate(model, val, device)
        th, score = max(f.items(), key=lambda kv: kv[1])
        history.append({"epoch": epoch + 1, "loss": total / a.steps, "val_f": f})
        print(f"epoch {epoch + 1:3d}  loss {total / a.steps:.4f}  val beat F (+-10 ms) " +
              "  ".join(f"th{k}: {v:.3f}" for k, v in f.items()) + f"  {time.time() - t0:.0f} s", flush=True)
        if score > best:
            best = score
            torch.save({"model": model.state_dict(), "threshold": th, "epoch": epoch + 1}, os.path.join(a.out, "model.pt"))
    json.dump(history, open(os.path.join(a.out, "history.json"), "w"), indent=1)
    print(f"best validation beat F {best:.3f}", flush=True)


if __name__ == "__main__":
    main()
