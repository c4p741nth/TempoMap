"""Phase 4 N4: per-song choice between the algorithm and the beat model, without the answer.

    python tools/beatnet/select.py <val.csv> <eval.csv>

Rules are tuned on the validation songs only (the beat model never trained on them; none is in an evaluation set),
then each candidate is checked once per evaluation set against the algorithm alone: the ship rule is that no set may
get worse.
"""
import csv
import itertools
import sys
from collections import defaultdict

import numpy as np

NUM = ["alg", "model", "alg_lines", "model_lines", "alg_in_model", "model_in_alg", "alg_on_peaks", "model_on_peaks",
       "orig_status", "model_beats_per_min", "seconds", "alg_spread", "model_spread"]


def load(path):
    rows = list(csv.DictReader(open(path, encoding="utf-8")))
    for r in rows:
        for k in NUM:
            r[k] = float(r[k])
    return rows


def rule(params):
    """Use the model when the algorithm looks unsure and the model looks sure.

    params: (min_alg_lines, max_agree, min_peak_gain, min_model_spread)
    - the algorithm wrote at least min_alg_lines red lines (a one-line song is the original detector's answer: keep it),
    - the two timings agree on at most max_agree of the algorithm's beats (when they agree, nothing to gain),
    - the model's beats sit on onsets at least min_peak_gain more often than the algorithm's.
    """
    min_lines, max_agree, min_gain, min_spread = params

    def pick(r):
        return (r["alg_lines"] >= min_lines and r["alg_in_model"] <= max_agree
                and r["model_on_peaks"] - r["alg_on_peaks"] >= min_gain and r["model_spread"] >= min_spread)
    return pick


def score(rows, pick):
    return np.mean([r["model"] if pick(r) else r["alg"] for r in rows])


def main():
    val, ev = load(sys.argv[1]), load(sys.argv[2])
    print(f"validation: {len(val)} songs, algorithm {np.mean([r['alg'] for r in val]):.3f}, model {np.mean([r['model'] for r in val]):.3f}, "
          f"hindsight best {np.mean([max(r['alg'], r['model']) for r in val]):.3f}")
    grid = list(itertools.product([1, 2, 3, 5, 8], [0.3, 0.5, 0.7, 0.9, 1.01], [-0.05, 0.0, 0.02, 0.05, 0.1], [0, 0.02, 0.05]))
    results = []
    for p in grid:
        pick = rule(p)
        s = score(val, pick)
        worse = sum(1 for r in val if pick(r) and r["model"] < r["alg"] - 0.02)
        results.append((s, -worse, p))
    results.sort(key=lambda t: (t[0] - 0.003 * -t[1]), reverse=True)
    print("best rules on validation (mean aligned, songs made worse by > 0.02):")
    for s, w, p in results[:8]:
        print(f"  {s:.3f}  worse {-w:3d}  lines>={p[0]} agree<={p[1]} peak gain>={p[2]} model spread>={p[3]}")
    # Each of the top rules on the evaluation sets, per set, against the algorithm alone.
    sets = defaultdict(list)
    for r in ev:
        sets[(r["set"], r["split"])].append(r)
    for s, w, p in results[:5]:
        pick = rule(p)
        cells, below = [], 0
        for k in sorted(sets):
            a = np.mean([r["alg"] for r in sets[k]])
            c = score(sets[k], pick)
            below += c < a - 1e-9
            cells.append(f"{k[0]} {k[1]} {a:.3f}->{c:.3f}{'*' if c < a - 1e-9 else ''}")
        used = sum(1 for r in ev if pick(r))
        print(f"rule {p}: model used on {used}/{len(ev)} eval songs; sets below the algorithm {below}")
        print("   " + "  ".join(cells))


if __name__ == "__main__":
    main()
