"""Training songs for the octave selector (docs/PHASE1-STATUS.md E36): one consensus map per ranked/approved/loved song
folder from osu!.db (read by tools/osu_db.py), audio present, minus the folders the evaluation sets use. Writes a
songs.tsv that timing-eval reads (set = "T" + the chosen map's mode, split = "tune"). Read only; no network.

    python tools/osu_db.py "G:/osu!/osu!.db" db.tsv
    python tools/octave_pairs.py db.tsv "G:/osu!/Songs" eval_folders.txt out.tsv
"""
import collections
import os
import sys

GOOD = {"ranked", "approved", "loved"}


def reds(path):
    """Red lines (time, beat length) and the audio file name of a .osu."""
    audio, out, section = None, [], ""
    with open(path, encoding="utf-8-sig", errors="replace") as f:
        for raw in f:
            line = raw.strip()
            if line.startswith("["):
                section = line
                continue
            if section == "[General]" and line.startswith("AudioFilename:"):
                audio = line.split(":", 1)[1].strip()
            elif section == "[TimingPoints]" and line and not line.startswith("//"):
                p = line.split(",")
                try:
                    t, length = float(p[0]), float(p[1])
                except (ValueError, IndexError):
                    continue
                if (p[6].strip() if len(p) > 6 else "1") == "1" and length > 0:
                    out.append((round(t), round(length, 6)))
    return audio, tuple(sorted(out))


def main():
    db, songs, eval_list, out = sys.argv[1:5]
    held = {l.strip() for l in open(eval_list, encoding="utf-8") if l.strip()}
    folders = collections.defaultdict(list)
    for line in open(db, encoding="utf-8"):
        if line.startswith("#"):
            continue
        folder, osu, status, mode, _ = line.rstrip("\n").split("\t")
        if status in GOOD:
            folders[folder].append((osu, mode))
    rows, skipped = [], collections.Counter()
    for folder in sorted(folders):
        if folder in held:
            skipped["evaluation set"] += 1
            continue
        maps = []
        for osu, mode in folders[folder]:
            path = os.path.join(songs, folder, osu)
            try:
                audio, lines = reds(path)
            except OSError:
                continue
            if audio and lines and os.path.exists(os.path.join(songs, folder, audio)):
                maps.append((path, mode, os.path.join(songs, folder, audio), lines))
        if not maps:
            skipped["no audio or timing"] += 1
            continue
        # The timing most difficulties share (mania first, as timing-eval select-mania picks), one map per song.
        pool = [m for m in maps if m[1] == "3"] or maps
        common = collections.Counter(m[3] for m in pool).most_common(1)[0][0]
        path, mode, audio, _ = next(m for m in pool if m[3] == common)
        rows.append(f"T{mode}\ttune\t{os.path.normpath(audio)}\t{os.path.normpath(path)}")
    with open(out, "w", encoding="utf-8") as f:
        f.write("# set\tsplit\taudio\treference (ranked/approved/loved training songs from osu!.db; machine-local paths)\n")
        f.write("\n".join(rows) + "\n")
    modes = collections.Counter(r.split("\t")[0] for r in rows)
    print(f"{len(rows)} training songs ({dict(modes)}); skipped {dict(skipped)}")


if __name__ == "__main__":
    main()
