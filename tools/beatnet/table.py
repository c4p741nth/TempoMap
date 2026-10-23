"""Phase 4 N3 report: aligned F10 per evaluation set for each variant from variants.sh, the hindsight best of algorithm
and model+onset per song, and the ship rule (no set below the algorithm alone).

    python tools/beatnet/table.py <outDir>
"""
import re
import sys
from collections import defaultdict

VARIANTS = ["alg", "model", "model+onset", "model+octave", "alg+modeloct", "hybrid"]
SETS = ["multibpm-songs", "mania-songs", "ranked-picks"]


def summary(path):
    out = {}
    for line in open(path, encoding="utf-8", errors="replace"):
        m = re.match(r"\s+(\w) (\w+)\s+n=\s*(\d+)\s+mean F10 ([\d.]+)\s+aligned ([\d.]+)", line)
        if m:
            out[(m[1], m[2])] = (int(m[3]), float(m[5]))
    return out


def songs(path):
    out = {}
    for line in open(path, encoding="utf-8", errors="replace"):
        f = line.split()
        if len(f) > 11 and len(f[0]) == 1 and f[1] in ("tune", "hold"):
            try:
                out[(f[0], f[1], " ".join(f[11:]))] = float(f[9])
            except ValueError:
                pass
    return out


def main():
    d = sys.argv[1]
    table, per_song = defaultdict(dict), defaultdict(dict)
    for v in VARIANTS:
        for s in SETS:
            for k, (n, a) in summary(f"{d}/{v}-{s}.txt").items():
                table[k][v] = (n, a)
            for k, a in songs(f"{d}/{v}-{s}.txt").items():
                per_song[k][v] = a
    # Hindsight best of alg and model+onset per song, averaged per set.
    best = defaultdict(list)
    for k, vs in per_song.items():
        if "alg" in vs and "model+onset" in vs:
            best[(k[0], k[1])].append(max(vs["alg"], vs["model+onset"]))
    print("aligned F10 per set (n songs); * = below the algorithm alone")
    print(f"{'set':10}" + "".join(f"{v:>14}" for v in VARIANTS) + f"{'best of 2':>12}")
    worse = defaultdict(int)
    for k in sorted(table):
        base = table[k]["alg"][1]
        cells = []
        for v in VARIANTS:
            a = table[k].get(v, (0, float("nan")))[1]
            star = "*" if a < base - 1e-9 else " "
            worse[v] += star == "*"
            cells.append(f"{a:13.3f}{star}")
        b = sum(best[k]) / len(best[k]) if best[k] else float("nan")
        print(f"{k[0]} {k[1]:5} n={table[k]['alg'][0]:3}" + "".join(cells) + f"{b:12.3f}")
    print("sets below the algorithm: " + ", ".join(f"{v} {worse[v]}" for v in VARIANTS[1:]))
    for name in ("My Dearest", "MAMBO"):
        hits = [(k, vs) for k, vs in per_song.items() if name.lower() in k[2].lower()]
        for k, vs in hits:
            print(f"  {k[2][:50]:50} " + "  ".join(f"{v} {vs.get(v, float('nan')):.3f}" for v in VARIANTS))


if __name__ == "__main__":
    main()
