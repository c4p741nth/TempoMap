"""Compare the Python reference or an external CLI against TimingAnlyz.exe.

Usage: python tools/parity.py <exe> <audio> [<audio> ...] [--modes auto,bpm,bpm+offset]
       python tools/parity.py [<exe>] --candidate <cli.exe> --songs tests/parity-songs.tsv

For each file, runs the original as an oracle, then runs reference/timing_analyzer.py
or --candidate with the same arguments and compares the printed BPM, offset and timeline.
Modes: auto = "<file> 0"; bpm = "<file> <oracle bpm / 2>"; bpm+offset = "<file> <bpm> <offset+7>".
"""
import argparse
import re
import subprocess
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "reference"))


def run_original(exe, args):
    # status lines and the timeline go to stderr, BPM/Offset to stdout
    p = subprocess.run([str(exe), *args], cwd=str(Path(exe).parent), capture_output=True,
                       text=True, timeout=600)
    return parse_output(p.stderr + p.stdout)


def parse_output(out):
    res = {}
    if m := re.search(r"Approximate BPM:\s*([\d.]+)", out):
        res["approx_bpm"] = float(m.group(1))
    if m := re.search(r"^BPM:\s*([\d.]+)", out, re.M):
        res["bpm"] = m.group(1)
    if m := re.search(r"^Offset:\s*(-?[\d.]+)", out, re.M):
        res["offset"] = m.group(1)
    if m := re.search(r"Timeline   >>(.*)<<", out):
        res["timeline"] = m.group(1)
    if "ERROR" in out:
        res["error"] = out[out.index("ERROR"):].splitlines()[0]
    return res


def run_candidate(exe, args):
    p = subprocess.run([str(exe), *args], capture_output=True, text=True, timeout=600)
    result = parse_output(p.stderr + p.stdout)
    if p.returncode != 0 and "error" not in result:
        raise RuntimeError("Candidate exited with code %d: %s" % (p.returncode, p.stderr + p.stdout))
    if not result:
        raise RuntimeError("Candidate produced no timing result: " + p.stderr + p.stdout)
    return result


def run_reference(path, bpm, offset, coef):
    import timing_analyzer as ref

    r = ref.analyze(path, bpm, offset, coef)
    res = {}
    if "error" in r:
        res["error"] = r["error"]
    elif "approx_bpm" in r:
        res["approx_bpm"] = round(r["approx_bpm"], 1)
    else:
        res["bpm"] = "%.3f" % r["bpm"]
        res["offset"] = "%.0f" % r["offset"]
        if r["flags"]:
            res["timeline"] = r["timeline"]
        res["_raw"] = (r["bpm"], r["offset"])
    return res


def same(o, r):
    keys = ("bpm", "offset", "timeline", "approx_bpm")
    if "error" in o or "error" in r:
        return ("error" in o) == ("error" in r)
    return bool(o) and bool(r) and all(o.get(k) == r.get(k) for k in keys)


def read_songs(path):
    files = []
    for line in path.read_text(encoding="utf-8-sig").splitlines():
        if not line.strip() or line.lstrip().startswith("#"):
            continue
        fields = line.split("\t", 1)
        if len(fields) != 2 or not fields[1].strip():
            raise ValueError("Expected group<TAB>audio-path in " + str(path))
        audio = Path(fields[1])
        files.append(str(audio if audio.is_absolute() else path.parent / audio))
    return files


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("exe", nargs="?", type=Path, default=Path("G:/osu!/TimingAnlyz 0.32.4.exe"),
                        help="original exe (defaults to this machine's G:/osu! copy)")
    parser.add_argument("files", nargs="*")
    parser.add_argument("--candidate", type=Path, help="external candidate CLI; omit to run Python reference")
    parser.add_argument("--songs", type=Path, help="UTF-8 group<TAB>path song list")
    parser.add_argument("--modes", default="auto,bpm,bpm+offset")
    options = parser.parse_args()
    modes = options.modes.split(",")
    if not modes or any(m not in ("auto", "bpm", "bpm+offset") for m in modes):
        parser.error("--modes must contain auto, bpm, or bpm+offset")
    exe = options.exe.resolve()
    candidate = options.candidate.resolve() if options.candidate else None
    files = options.files + (read_songs(options.songs) if options.songs else [])
    if not files:
        parser.error("provide audio files or --songs")
    for path in [exe, *([candidate] if candidate else []), *map(Path, files)]:
        if not path.is_file():
            parser.error("file does not exist: " + str(path))
    coef = None
    if candidate is None:
        import timing_analyzer as ref
        coef = ref.load_coefficients()
    total = passed = 0
    for f in files:
        auto = run_original(exe, [f, "0"])
        for mode in modes:
            if mode == "auto":
                args, bpm, off = [f, "0"], 0.0, None
            elif "bpm" not in auto:
                continue
            elif mode == "bpm":
                bpm = round(float(auto["bpm"]) / 2, 3)
                args, off = [f, repr(bpm)], None
            else:
                bpm, off = float(auto["bpm"]), float(auto["offset"]) + 7
                args = [f, repr(bpm), repr(off)]
            o = auto if mode == "auto" else run_original(exe, args)
            try:
                r = run_candidate(candidate, args) if candidate else run_reference(f, bpm, off, coef)
            except (OSError, RuntimeError, subprocess.TimeoutExpired) as error:
                # A crashed/empty candidate must never look like a matching empty result.
                r = {"_failure": str(error)}
            ok = "_failure" not in r and same(o, r)
            total += 1
            passed += ok
            print("%s  %-11s %s" % ("PASS" if ok else "FAIL", mode, Path(f).name))
            print("      original : %s" % {k: v for k, v in o.items()})
            print("      %-9s: %s" % ("candidate" if candidate else "reference", r))
            sys.stdout.flush()
    print("\n%d/%d match" % (passed, total))
    return 0 if passed == total and total > 0 else 1


if __name__ == "__main__":
    sys.exit(main())
