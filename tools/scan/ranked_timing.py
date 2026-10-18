# Red-line structure of the user's ranked multi-BPM picks (2026-10-18). Reads every .osu in the given set folders,
# takes the consensus timing per set (mania difficulties first, as timing-eval select-mania does), and reports how
# the mappers placed red lines: count, BPM spread, section lengths in beats, phase resets, meters, gimmick lines.
# Also compares sets of the same song with each other (how much ranked mappers agree), and writes an eval list.
import os, sys, collections
import numpy as np

SONGS = r'G:\osu!\Songs'
OUT = os.path.dirname(os.path.abspath(__file__))


def parse(path):
    meta, reds, greens, sect = {}, [], 0, ''
    with open(path, encoding='utf-8-sig', errors='replace') as f:
        for raw in f:
            line = raw.strip()
            if line.startswith('['):
                sect = line
                continue
            if sect in ('[General]', '[Metadata]') and ':' in line:
                k, v = line.split(':', 1)
                meta.setdefault(k.strip(), v.strip())
            elif sect == '[TimingPoints]' and line and not line.startswith('//'):
                p = line.split(',')
                try:
                    t, bl = float(p[0]), float(p[1])
                except ValueError:
                    continue
                meter = int(p[2]) if len(p) > 2 and p[2].strip() else 4
                uninherited = p[6].strip() == '1' if len(p) > 6 else True
                if uninherited:
                    reds.append((t, bl, meter))
                else:
                    greens += 1
    reds.sort(key=lambda r: r[0])
    return meta, reds, greens


def consensus(folder):
    maps = []
    for name in sorted(os.listdir(folder)):
        if not name.endswith('.osu'):
            continue
        meta, reds, greens = parse(os.path.join(folder, name))
        audio = meta.get('AudioFilename')
        if not reds or not audio or not os.path.exists(os.path.join(folder, audio)):
            continue
        maps.append((os.path.join(folder, name), meta, reds, greens))
    if not maps:
        return None
    mania = [m for m in maps if m[1].get('Mode') == '3']
    pool = mania or maps
    groups = collections.Counter(tuple(r) for _, _, r, _ in [(m[0], m[1], tuple(m[2]), m[3]) for m in pool])
    best = groups.most_common(1)[0][0]
    chosen = next(m for m in pool if tuple(m[2]) == best)
    modes = collections.Counter(m[1].get('Mode', '0') for m in maps)
    agree = groups.most_common(1)[0][1]
    return chosen, modes, agree, len(pool), bool(mania)


def stats(reds):
    real = [(t, bl, m) for t, bl, m in reds if bl > 0 and 60 <= 60000 / bl <= 400]
    gimmick = len(reds) - len(real)
    bpm = np.array([60000 / bl for _, bl, _ in real]) if real else np.array([0.0])
    lengths, resets = [], 0
    for (t, bl, m), (t2, _, _) in zip(reds, reds[1:]):
        if bl <= 0:
            continue
        beats = (t2 - t) / bl
        lengths.append((beats, m))
        frac_ms = abs(beats - round(beats)) * bl
        if frac_ms > 3 and beats >= 0.5:
            resets += 1
    beats = np.array([b for b, _ in lengths]) if lengths else np.array([])
    bars = np.array([b / m for b, m in lengths]) if lengths else np.array([])
    distinct = []
    for b in sorted(bpm):
        if not distinct or b > distinct[-1] * 1.005:
            distinct.append(b)
    steps = np.abs(np.diff(bpm)) / bpm[:-1] if len(bpm) > 1 else np.array([])
    integer = np.mean(np.abs(bpm - np.round(bpm)) < 0.01)
    return dict(
        n=len(reds), gimmick=gimmick, distinct=len(distinct), lo=bpm.min(), hi=bpm.max(),
        lt2=np.mean(beats < 1.999) if len(beats) else 0,
        one_bar=np.mean(np.abs(bars - 1) < 0.01) if len(bars) else 0,
        le_bar=np.mean(bars <= 1.01) if len(bars) else 0,
        med_beats=np.median(beats) if len(beats) else 0,
        resets=resets / max(1, len(lengths)), integer=integer,
        small_steps=np.mean(steps < 0.02) if len(steps) else 0,
        meters=dict(collections.Counter(m for _, _, m in reds).most_common(4)),
    )


def grid(reds, end):
    out = []
    for i, (t, bl, _) in enumerate(reds):
        stop = reds[i + 1][0] if i + 1 < len(reds) else end
        if bl <= 0 or not 60 <= 60000 / bl <= 400:
            continue
        k = 0
        while t + k * bl < stop - 1e-6:
            out.append(t + k * bl)
            k += 1
    return np.array(out)


def compare(a, b):
    end = min(a[-1][0], b[-1][0]) + 60000
    ga, gb = grid(a, end), grid(b, end)
    start = max(ga[0], gb[0])
    ga = ga[(ga >= start) & (ga <= end)]
    gb = gb[(gb >= start) & (gb <= end)]
    if len(ga) == 0 or len(gb) == 0:
        return None
    idx = np.clip(np.searchsorted(gb, ga), 1, len(gb) - 1)
    near = np.where(np.abs(gb[idx] - ga) < np.abs(gb[idx - 1] - ga), gb[idx], gb[idx - 1])
    d = near - ga
    return np.median(np.abs(d)), np.mean(np.abs(d) <= 5), np.mean(np.abs(d) <= 10), np.median(d)


def main():
    ids = sys.argv[1].split(',')
    folders = {}
    for name in os.listdir(SONGS):
        sid = name.split(' ')[0]
        if sid in ids:
            folders[sid] = os.path.join(SONGS, name)
    rows, by_song = [], collections.defaultdict(list)
    print('set       mode  diffs agree  reds gimk  BPMs  BPM range      med beats  <2b  =1bar  <=bar  reset  int  step<2%  meters   song')
    for sid in ids:
        if sid not in folders:
            print(sid, 'not found')
            continue
        c = consensus(folders[sid])
        if c is None:
            print(sid, 'no usable .osu')
            continue
        (path, meta, reds, greens), modes, agree, pool, mania = c
        s = stats(reds)
        key = (meta.get('Artist', '') + ' - ' + meta.get('Title', '')).lower()
        by_song[key].append((sid, reds))
        rows.append((sid, path, meta, s))
        mode = 'mania' if mania else {'0': 'std', '1': 'taiko', '2': 'ctb'}.get(modes.most_common(1)[0][0], '?')
        print(f"{sid:9s} {mode:5s} {pool:5d} {agree:5d} {s['n']:5d} {s['gimmick']:4d} {s['distinct']:5d} "
              f"{s['lo']:6.1f}-{s['hi']:6.1f} {s['med_beats']:9.1f} {s['lt2']:5.2f} {s['one_bar']:5.2f} {s['le_bar']:5.2f} "
              f"{s['resets']:5.2f} {s['integer']:4.2f} {s['small_steps']:6.2f}  {str(s['meters']):8s} "
              f"{meta.get('Artist', '')} - {meta.get('Title', '')}")
    print()
    print('same song, different sets (grid of the first set vs each other, no shift):')
    for key, sets in by_song.items():
        for (sa, ra), (sb, rb) in [(sets[0], s) for s in sets[1:]]:
            r = compare(ra, rb)
            if r:
                print(f'  {key[:50]:50s} {sa} vs {sb}: reds {len(ra)}/{len(rb)}  median |diff| {r[0]:5.1f} ms  '
                      f'<=5 ms {r[1]:.2f}  <=10 ms {r[2]:.2f}  signed median {r[3]:+.1f}')
    with open(os.path.join(OUT, 'ranked.tsv'), 'w', encoding='utf-8') as f:
        f.write('# set\tsplit\taudio\treference (user ranked multi-BPM picks, 2026-10-18)\n')
        for sid, path, meta, s in rows:
            audio = os.path.join(os.path.dirname(path), meta['AudioFilename'])
            f.write(f'F\thold\t{audio}\t{path}\n')


main()
