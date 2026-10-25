"""Writes the editor test fixtures: synthetic .osu files made for this repository (no one else's beatmap).

They keep what the tests rely on: CRLF without a BOM, a final newline, every section osu! writes, fractional beat
lengths written the way osu! stable writes them, red lines with several BPMs and effects, green lines between and on
red lines, and Unicode metadata. Run from the repository root:  python tools/make_fixtures.py
"""
import os

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "tests", "TimingAnalyzer.Editor.Tests", "Fixtures")


def beat(bpm):
    # osu! stable writes beat lengths with up to 15 significant digits, a whole one without ".0".
    length = float(f"{60000 / bpm:.15g}")
    return str(int(length)) if length == int(length) else repr(length)


def write(name, title, title_unicode, artist, artist_unicode, version, mode, audio, timing, objects, events=()):
    lines = [
        "osu file format v14", "",
        "[General]", f"AudioFilename: {audio}", "AudioLeadIn: 0", "PreviewTime: 30000", "Countdown: 0",
        "SampleSet: Soft", "StackLeniency: 0.7", f"Mode: {mode}", "LetterboxInBreaks: 0", "WidescreenStoryboard: 1", "",
        "[Editor]", "DistanceSpacing: 1", "BeatDivisor: 4", "GridSize: 4", "TimelineZoom: 1", "",
        "[Metadata]", f"Title:{title}", f"TitleUnicode:{title_unicode}", f"Artist:{artist}", f"ArtistUnicode:{artist_unicode}",
        "Creator:TempoMap tests", f"Version:{version}", "Source:", "Tags:synthetic fixture", "BeatmapID:0", "BeatmapSetID:-1", "",
        "[Difficulty]", "HPDrainRate:5", "CircleSize:4", "OverallDifficulty:7", "ApproachRate:9", "SliderMultiplier:1.4",
        "SliderTickRate:1", "",
        "[Events]", "//Background and Video events", "//Break Periods", *events,
        "//Storyboard Layer 0 (Background)", "//Storyboard Layer 1 (Fail)", "//Storyboard Layer 2 (Pass)",
        "//Storyboard Layer 3 (Foreground)", "//Storyboard Sound Samples", "",
        "[TimingPoints]", *timing, "", "",
        "[HitObjects]", *objects,
    ]
    data = ("\r\n".join(lines) + "\r\n").encode("utf-8")
    with open(os.path.join(OUT, name), "wb") as f:
        f.write(data)
    print(name, len(data), "bytes")


def red(t, bpm, meter=4, sample=2, index=1, volume=50, effects=0):
    return f"{t},{beat(bpm)},{meter},{sample},{index},{volume},1,{effects}"


def green(t, sv, sample=2, index=1, volume=50, effects=0):
    # osu! writes a whole beat length without ".0" (-125, not -125.0).
    length = float(f"{-100 / sv:.15g}")
    return f"{t},{int(length) if length == int(length) else repr(length)},4,{sample},{index},{volume},0,{effects}"


def circles(times):
    return [f"{64 + (i * 37) % 384},{48 + (i * 53) % 288},{int(t)},1,0,0:0:0:0:" for i, t in enumerate(times)]


# steady-multi.osu: 48 red lines over 12 BPMs (169-176 and a few others), every fourth one omitting its bar line, a
# green line on each red line and one between (as my-dearest-tv.osu had, which this replaces).
timing, t, bpms = [], 639.0, [170, 170, 169, 170, 169, 174, 169, 163, 165.5, 171, 172.25, 176]
for k in range(48):
    bpm = bpms[k % len(bpms)]
    timing.append(red(round(t), bpm, effects=8 if k % 4 == 0 else 0))
    timing.append(green(round(t) + (0 if k % 2 == 0 else 1), [0.99, 0.97, 1.03, 1.0][k % 4]))
    t += (2 + k % 3) * 60000 / bpm
write("steady-multi.osu", "Synthetic Steady", "シンセティック", "TempoMap Fixtures", "テンポマップ", "Many Red Lines", 0,
      "audio.mp3", timing, circles(range(1000, 90000, 353)), events=["2,40000,42000"])

# ramp-mania.osu: osu!mania, 27 red lines, a ramp of 25 distinct BPMs and 178 green lines (as m1ll10n-pp.osu had).
timing, t = [], 2005.0
for k, bpm in enumerate([234] + [215 + 8 * i for i in range(24)] + [234, 234]):
    timing.append(red(round(t), bpm, sample=2, index=1, volume=30 if k == 0 else 65))
    for j in range(6 if k < 26 else 22):
        timing.append(green(round(t) + 37 * (j + 1), 0.8 + 0.05 * j, volume=65))
    t += (16 if k == 0 else 4) * 60000 / bpm
write("ramp-mania.osu", "Synthetic Ramp", "合成ランプ", "TempoMap Fixtures", "テンポマップ", "Ramp", 3,
      "audio.ogg", timing, [f"{64 + 128 * (i % 4)},192,{2005 + 120 * i},1,0,0:0:0:0:" for i in range(800)])

# one-red.osu: one red line, 12 green lines (as architecture.osu had).
timing = [red(14460, 300, sample=1, index=0, volume=50)] + [green(14460 + 3200 * (j + 1), 1 + 0.1 * j, sample=1, index=0) for j in range(12)]
write("one-red.osu", "Synthetic Single", "合成シングル", "TempoMap Fixtures", "テンポマップ", "One Red Line", 0,
      "audio.mp3", timing, circles(range(14460, 120000, 200)))
