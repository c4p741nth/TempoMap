# Tempomap

**Tempomap** is a timing editor for osu! by **C4P741N**: auto-timing for steady, multi-BPM and live-played songs
(down to a red line on every drum hit), bar lines, a timing table like osu!'s, and normalised slider velocity for
osu!mania. Download it from the Releases page (Windows x64, no .NET install needed) and run `Tempomap.exe`. MIT licensed (see `LICENSE`); audio goes through BASS by un4seen, which is free for
non-commercial use.

Audio imports are prepared as Ogg Vorbis quality 6 before loading; `ffmpeg` and `ffprobe` must be on PATH or beside
the executable. Original files are preserved. Copy applies the requested -33 ms osu! compensation; pasting within
Tempomap keeps source times and duplicates rows. See [audio import and clipboard](docs/AUDIO-IMPORT.md).

It started as a C# engine and Windows CLI rebuilding **TimingAnlyz 0.32.4 alpha**, with its recovered
specification and Python reference. It auto-detects an osu! song's **BPM and offset** and flags
parts that need extra timing sections. The C# CLI matches the original in **45/45 parity runs**.
The engine port is complete; a GUI and variable-tempo detection are outside its scope.

## Build and run the C# CLI

Requires Windows x64 and the .NET 10 SDK (verified with 10.0.402). From the repository root:

```powershell
$env:DOTNET_CLI_HOME = Join-Path $PWD ".dotnet-home"
$env:NUGET_PACKAGES = "D:\packages\nuget"  # this machine's prepared package cache
dotnet restore --source $env:NUGET_PACKAGES
dotnet build -c Release --no-restore

$cli = ".\src\TimingAnalyzer.Cli\bin\Release\net10.0\win-x64\timing-analyzer.exe"
& $cli "song.ogg"                  # auto BPM and offset
& $cli "song.ogg" 284.998          # supplied BPM, auto offset
& $cli "song.ogg" 284.998 136      # supplied BPM and displayed osu! offset
```

The restore command above is offline and uses the already populated cache. On another machine,
choose a writable cache and run `dotnet restore` against the normal NuGet sources first. Package
versions are pinned in `Directory.Packages.props`; no additional packages are needed here.
The build copies BASS 2.4.17 x64 and its companion natives next to the CLI; keep those files
together when moving the output directory. Core accepts `double[]` mono samples and a sample
rate and has no audio or file dependencies.

BPM `0` means auto, which also detects the offset automatically. Supplied offsets are folded
to the first beat at or after 30 ms internally. Decimal input and all numeric output use
invariant culture. The CLI follows the Python reference's output format, including writing
the timeline to stdout. For Judgement:

```text
Timeline   >>  #  #+    #      #               <<
Time (min) >>0     1     2     3     4     5   <<
BPM:     284.998
Offset:      136
```

Timeline rows appear only when flags are present. Approximate detections print only
`Approximate BPM:`. Exit codes are 0 for a result (including approximate), 1 for an analysis
or decoding error, and 2 for invalid arguments.

## Test the engine

Python stage fixtures require the packages in `requirements.txt`. This machine's prepared
`.venv\Scripts\python.exe` already has them. On another machine, create a venv with a working
Python installation and install that requirements file first.

The small SciPy biquad fixture is committed; regenerate it with
`.venv\Scripts\python.exe tools/dump_biquad.py` when needed. Full song fixtures are large and
gitignored. Edit the machine-specific paths in `tests/parity-songs.tsv` if necessary, then
generate both required golden directories before running the tests:

```powershell
$songs = Import-Csv tests/parity-songs.tsv -Delimiter "`t" -Header Group, Audio
$judgement = $songs | Where-Object { $_.Audio -like "*Judgement*" } | Select-Object -First 1 -ExpandProperty Audio
$hayakore = $songs | Where-Object { $_.Audio -like "*Hayakore*" } | Select-Object -First 1 -ExpandProperty Audio
.venv\Scripts\python.exe tools/dump_golden.py $judgement tests/golden/Judgement
.venv\Scripts\python.exe tools/dump_golden.py $hayakore tests/golden/Hayakore
dotnet test -c Release --no-build --no-restore
.venv\Scripts\python.exe -m unittest discover -s tests -p test_parity.py
```

The golden script writes little-endian float64 mono, envelope and autocorrelation arrays,
the sample rate, and expected analysis results. Stage tests bypass decoding and compare both
songs against Python, including supplied BPM/offset modes. They also check FFT against a
naive DFT, autocorrelation against direct sums, all biquad cascades against SciPy, silence,
and the duration cap. Missing golden data fails with a generation instruction. Set
`TIMING_ANALYZER_GOLDEN` to use an alternate root containing `Judgement/` and `Hayakore/`.

Verified on 2026-10-16: Release build with 0 warnings/errors, 7 C# tests and 5 Python harness
tests passing. The song envelopes match Python exactly; autocorrelation and timing results
meet the tolerances in [`docs/PORT-PLAN.md`](docs/PORT-PLAN.md).

## Run parity against the original

The original exe and the song files must be available locally. From the root, after building:

```powershell
.venv\Scripts\python.exe tools/parity.py --candidate $cli --songs tests/parity-songs.tsv
```

The default oracle is `G:\osu!\TimingAnlyz 0.32.4.exe`; supply another original exe as the
first positional argument when needed. The runner starts it in its own directory so it can
load its x86 BASS.dll. It compares printed BPM (3 decimals), offset (whole milliseconds),
and the timeline including spaces. Modes are auto, half the oracle BPM with auto offset,
and the oracle BPM with displayed offset increased by 7 ms. Use `--modes auto` for a subset.
The runner exits nonzero on any mismatch or candidate failure.

Result on 2026-10-16: **45/45 exact C# matches**, 15 songs in 3 modes, with all four timeline
symbols covered. The set has 7 development songs and 8 held-out songs. BASS 2.4.17 x64 needs
no MP3 delay correction, including Hayakore, lapix and Jamie without Xing/Info headers.
Task commits and verification details are in [`docs/PORT-STATUS.md`](docs/PORT-STATUS.md).

## What the original does

1. Decodes MP3/OGG with BASS to mono float at 44.1 kHz. Songs are capped at 10 min.
2. Builds an onset-strength signal at 1 kHz. It runs a 5-band octave filter bank, takes each band's log energy, and computes a forward/backward smoothed derivative.
3. **BPM**: FFT autocorrelation of that signal, then a least-squares fit of harmonic peaks with parabolic interpolation. Above 210 BPM it prefers a multiple-period candidate when available. A fit-quality gate decides between "exact" and "approximate".
4. **Offset**: folds the signal modulo the beat period and takes the argmax, with a −28.7 ms latency calibration.
5. **Timeline**: per 10 s, it checks on-beat versus off-beat energy and the deviation of peaks from the beat grid:
   - `#`: needs another timing section
   - `+` / `-`: offset is late / early here
   - `.`: drift or jitter

The full specification, with every constant and address, is in [`docs/ALGORITHM.md`](docs/ALGORITHM.md).
The address-to-role map is in [`docs/FUNCTION-MAP.md`](docs/FUNCTION-MAP.md).

## Layout

```
src/TimingAnalyzer.Core/   double-precision DSP and analysis, no I/O dependencies
src/TimingAnalyzer.Audio/  BASS float decoding, channels summed into double mono
src/TimingAnalyzer.Cli/    invariant-culture command-line interface
tests/TimingAnalyzer.Core.Tests/  numerical unit tests and golden stage comparisons
tests/parity-songs.tsv     machine-specific development and held-out audio paths
docs/ALGORITHM.md          recovered algorithm (observed / inferred / unknown marked)
docs/FUNCTION-MAP.md       address → role, recovered signatures, CRT helper identities
src/TimingAnalyzer.Core/coefficients.json  all biquad tables + checked scalar constants (extracted)
src/TimingAnalyzer.App/    the Tempomap editor (Avalonia)
tools/extract_coefficients.py   re-extracts coefficients from the exe (bytes only, never executes it)
reference/timing_analyzer.py    faithful Python port of the spec (parity-verified)
tools/parity.py                 runs original vs Python or external CLI and compares output
tools/dump_golden.py            regenerates per-song Python stages and expected results
tools/dump_biquad.py            regenerates the small committed SciPy filter fixture
```

## Reproduce

The decompiler output itself (pseudocode, disassembly, raw evidence) is not published; these commands regenerate it
locally from your own copy of the exe.

Requirements: Node ≥ 24, `npm i -g rea-agents` (6.5.0), Ghidra 12.1.x, JDK 21, and `GHIDRA_INSTALL_DIR` set.

```powershell
$t = "G:\osu!\TimingAnlyz 0.32.4.exe"
rea analyze $t --provider ghidra --snapshot ta.snapshot
rea decompile $t 0x402280 --provider ghidra --snapshot ta.snapshot
rea instructions $t 0x402fc0 --provider ghidra --snapshot ta.snapshot
python -I tools/extract_coefficients.py $t src/TimingAnalyzer.Core/coefficients.json
```

## Limits of the original (rebuild opportunities)

All of these come from the recovered code:

- **Single BPM / single offset**: no variable-tempo support. `#` only tells you that another section is needed; it doesn't compute one.
- Filters are hard-designed for **44.1 kHz**. Other rates only produce a warning.
- **10-minute cap** from the fixed 2²⁰-point FFT table.
- Fundamentals are searched only at lags ≤ 999 ms (≥ 60 BPM). Above 210 BPM, the result is replaced by a candidate whose period is a multiple of it, when one exists.
- Timeline resolution is fixed at 10 s per character. There is no per-beat view and no waveform.
- Channels are summed, not averaged, and the timeline thresholds (`S>3`, `A>3`) are absolute. Timeline flags are therefore probably loudness- and channel-count-dependent (inferred, not tested).
- A console UI, with the BASS.dll dependency next to the exe.

## Python reference

`reference/timing_analyzer.py` is a faithful Python port of the spec (numpy/scipy/soundfile).
Without `--candidate`, `tools/parity.py` runs it next to the original exe and compares the printed BPM, offset and
timeline in three modes: auto, BPM given, and BPM + offset given.

```powershell
python -m venv .venv; .venv\Scripts\pip install -r requirements.txt
.venv\Scripts\python reference\timing_analyzer.py "song.ogg"            # same output format as the original
.venv\Scripts\python tools\parity.py "G:\osu!\TimingAnlyz 0.32.4.exe" "a.ogg" "b.mp3" ...
```

Result on 2026-10-16: **45/45 exact matches** over 15 osu! songs, 3 modes each. 7 songs formed
the development set and 8 were held out. All four timeline flags occur in the set, and so do
tempos above 210 BPM.

One decoder quirk was found and handled in the Python reference. For MP3s with no Xing/Info
gapless header, BASS outputs 529 samples earlier than mpg123, which shifted the offset by
12 ms. Python drops those samples (see `load_audio`); the C# BASS decoder applies no shim.

Note that the original writes status lines and the timeline to **stderr**, and only `BPM:` and
`Offset:` to stdout.

## Phase 1 desktop timing editor

Timing Analyzer is made by **C4P741N**; **Help → About Tempomap** shows the version (releases are named
vYYYYMMDD.n) and credits.

The window has a menu bar (**File**: open, save, copy timing points, copy normalized; **Edit**: undo/redo, add or
move a timing point here, delete, select, copy and paste rows; **Timing**: auto-time and apply, auto-time range, ms shifts
and the per-line tools;
**Settings**: metronome, follow, restore default settings; **Help**: keys and colours (F1), about). Under it are
three toolbars: playback (Play / Pause, Follow, beat snap, speed, metronome and music volume), Auto-time (mode, BPM
box, followed by a separate result row with offset and **Apply...** before the main BPM normalization note) and Shift (❮ step ❯ for beats and for ms, the step picked in a drop-down). The
editor remembers volumes, metronome, follow, speed, beat snap, shift steps, auto-time mode, table tab and window size between
runs (`%APPDATA%\Tempomap\settings.json`), with the BPM step and the two Auto-time choices; **Settings → Restore default settings** resets them.

Shortcuts: Ctrl+O open .osu, Ctrl+Shift+O open audio, Ctrl+S save, Ctrl+Shift+C copy timing points, Ctrl+Shift+N
copy normalized, Ctrl+T auto-time, Ctrl+Enter apply, Ctrl+R auto-time range, Ctrl+V paste timing points
(Ctrl+Shift+V replacing all red lines), Ctrl+K check sections, Ctrl+L fill a ramp, Ctrl+P add a red timing point and focus BPM, Ctrl+G add a green inherited point and focus SV (from any tab), Ctrl+Shift+P move the selected point to the playhead, Home/End go to song start/end, Ctrl+←/→ shift the selected lines 1 ms (Ctrl+Shift+←/→ 5 ms), Ctrl+↑/↓ tune their BPM by the step in the Shift row (Ctrl+Shift: ten steps), Shift+←/→ highlight
a range by the beat snap, M metronome, F follow, F1 the full list. Esc closes the Keys, About and confirm windows.

The Avalonia editor opens audio or an osu! `.osu` beatmap, displays waveform peaks and a section-aware
beat grid, and plays audio with a metronome at 1.0, 0.75 or 0.5 speed with pitch preserved. It supports
manual multi-BPM editing, and one **Auto-time** button: in Normal mode it proposes the original detector's single
red line for a steady song and a red line for every tempo change otherwise; the Drift modes follow a drifting tempo
or put a red line on every drum hit.

With the prepared packages, launch from the repository root:

```powershell
$env:DOTNET_CLI_HOME = "$PWD/.dotnet-home"
$env:NUGET_PACKAGES = 'D:\packages\nuget'
dotnet build -c Release --no-restore
dotnet run --project src/TimingAnalyzer.App -c Release --no-build --no-restore
```

Open a map beside its audio file (File → Open, or drop the `.osu` or an audio file anywhere on the window). Click to seek, Space to play/pause, Ctrl+wheel to zoom, and wheel or
the bottom slider to scroll. The icons at its ends (Home/End) seek the yellow playhead and view to the start/end of the song,
including while playing with Follow off. Drag a red line to move its offset; Ctrl+drag a later beat to stretch BPM
with the section start fixed. The table toolbar keeps **+**, **−**, **⇥** together and visible:
**+** / Insert adds a red point in All/Timing or a green point in Inherited. **Ctrl+P** always adds red and selects its BPM text;
**Ctrl+G** always adds green and selects its SV text, switching to All when needed so the point is visible.
**−** / Delete removes selected points; automatic normalization rows remain where needed.
**⇥** / Ctrl+Shift+P moves the selected point and other selected points of the same kind onto the playhead.
Ctrl+Z/Ctrl+Y undo/redo. Time, BPM, meter and green SV edits save on **Enter**, **Tab** or clicking outside;
**Esc** in a field restores its saved value. Invalid Enter keeps focus and reports the error; leaving an invalid field restores its saved value.
Unchanged fields preserve exact source precision and add no undo step. Clicking a row in the timing table only selects it (the playhead stays);
double-clicking jumps there (playhead on the line, line one third from the left); while playing with Follow on, the list selects and scrolls to the red line under the playhead,
except while typing or working with inherited points. Space
always plays/pauses, even right after clicking another button (only text boxes keep it). The loaded song's
"Artist - Title [Difficulty] (Mapper)" (or the audio file name) heads the window.

The time below the waveform reads like the osu! editor, `mm:ss:ms / mm:ss:ms` (position / song length):
`00:01:531` is the same instant as offset 1531. **Beat snap** (1/1–1/9, 1/12, 1/16,
default 1/4) sets the dim grid ticks, and **←/→** step the playhead to the previous/next tick of the
section under it, landing on red lines when crossing them. The view scrolls to keep the playhead on screen.
Hold ←/→ past 0.3 s and the playhead runs on, faster the longer you hold (like Audacity's cursor keys: 0.3 views per
second at first, doubling every 0.7 s up to 4); the sound is paused during the run and seeks to the head when the key
goes up, playing on if it was playing. Shift+←/→ held extends the highlighted range the same way.

Drag across empty waveform space to box-select every red line in that band (Ctrl+click in the timing table adds
to the selection, Ctrl+A selects all, Esc clears; Shift+drag highlights a range instead, see Auto-time range). Selected lines are orange in the waveform and marked ● in the
list; the fields edit the highlighted row. A plain click still seeks.

**osu! precision.** osu! takes red-line offsets as whole ms and BPM with three decimals, so every value the
editor writes is rounded that way: auto-time, Apply fields, Insert, drag, stretch and shifts. Only the
edited field is rounded — a shift rounds the time and keeps that line's beat length — and lines you do
not touch keep their exact original values, so an unedited map saves byte-for-byte.

Music and Metronome sliders set independent volumes from 0 to 100 % (defaults: 100 % and 70 %).
Zero mutes that source. Volume changes apply during playback without a graph rebuild, seek or pause;
the values survive seeking, speed/timing changes and reopening the song. The output channel is unbuffered,
so a change is heard after about 90 ms (the pitch-preserving tempo stage's lookahead) instead of the
~580 ms that BASS's default 500 ms playback buffer added.

Follow starts checked. During playback the view scrolls smoothly with the playhead one third from the
left, stopping at the song boundaries. Zooming keeps following (at one third of the new width). Wheel or
slider scrolling while playing pauses follow: scrolled **ahead**, it resumes by itself when the playhead
reaches one third of that view (no jump); scrolled **back**, the view stays so you can look back. A click
to seek, ←/→, Play or an off/on toggle of Follow resumes it at once. Pausing leaves the view in place.

The Shift group moves the selected red lines (pick them in the timing table or box-select them; Ctrl+A for all) one step earlier (**❮**) or
later (**❯**): the beat step is 1/1, 1/2, 1/4, 1/8, 1/16 or 1/32 of a beat, the ms step 1, 2, 5, 10, 20 or 50 ms, each
picked in the drop-down between the arrows. Beat shifts use each red line's own beat length, and each moved line
then lands on a whole ms. Each press is one undo step; negative times are allowed.
**Green lines (inherited SV/sample changes) stay fixed.** A shift that would make a red line cross or
touch an unmoved neighbour (or reorder lines) is rejected with a status message and leaves timing
unchanged; selected lines may move past each other's old positions.

**Beat model.** Normal-mode Auto-time also runs a small neural network (0.48 M parameters, `Models\beatnet.onnx`
beside the exe, about 2 s per 4-minute song on the CPU) trained on the ranked and loved maps in the local osu!.db, which
marks where mappers put beats. A rule chosen on 126 songs the network never trained on uses its red lines instead of the
analyzer's when the two disagree on most beats, the network's beats sit on onsets clearly more often, and it hears a
tempo change; the status line says so. On the evaluation sets no set got worse and 7 of 10 better (hard multi-BPM sets:
0.489 → 0.553, 0.557 → 0.609, 0.647 → 0.692 aligned). The **Beat model** mode (beside Normal) uses the network's red
lines for the whole song, for hard songs Normal keeps on the analyzer (Camellia - Parallel Universe Shifter: network
0.585, analyzer 0.296); listen to the result. Training: `tools/beatnet/` and `docs/PHASE4-STATUS.md`.

**Check and fix** (right panel) is for after auto-time. **Check sections** (Ctrl+K) scores every red line's section
against the onsets and marks the ones worth a listen with a violet band (and selects them in the table): the line's
beats sit on no more onset than the section's average, or the same tempo fits clearly better 25 ms or more away. On
mappers' own timing it marks about one section in ten (0.09-0.25 per section over 1 059 sections of the evaluation
sets), so a mark means "listen", not "wrong"; it marks Ill's 131 722 (the drums 45 ms earlier) and Parallel Universe
Shifter's 283 730 @ 250 (a quarter beat off the onsets). It also says when maps of songs like this one usually write
another octave ("maps usually write ÷2 (188 BPM)"), from a model trained on the ranked and loved maps in the local
osu!.db; it never changes the timing by itself: press the button it names. **×2 / ÷2 / ×1.5 / ×⅔** (more under Timing → Tempo of
selected lines) multiply the BPM of the selected red lines and keep their times, for a section timed at the wrong
octave or ratio: 5 of the 30 single-BPM test songs come out at double or half the map's BPM. **Fill ramp** (Ctrl+L)
puts a red line on every beat between the selected red lines, the BPM moving in a straight line from one to the next,
as ranked maps write long accelerandos; where the music holds a tempo or jumps midway, select that line too and each
part is filled. On Parallel Universe Shifter's 111 → 175 accelerando, given the start, the end and the lines at its
two holds and its one jump, it writes all 56 of the map's ramp lines within 10 ms and 1.5 BPM.

**Auto-time range** re-times part of the song. Highlight the part first: Shift+drag across the waveform (also over a
red line), or Shift+←/→, which step the playhead by the beat snap and highlight from where the Shift steps began.
Esc clears the range. Then **Auto-time range** (right panel, Ctrl+R) analyzes only that part: its red lines are
replaced by what it finds there, a tempo change inside it gets its own red line, the BPM box locks one line at that
BPM, and a drift mode puts drift lines in it. The music after the range keeps its timing: the red line in effect at
its end continues from its first beat after it. One undo step; the new lines are selected. Use it where whole-song
Auto-time went wrong. On a crowd of rebellion - Ill, whole-song Auto-time gives the 142 BPM part (131.7-169.7 s) as
189.3 BPM followed by a false ramp; a range over that part gives one 142 BPM red line on the drums. On reference
sections where whole-song timing hit under half the beats (osu!mania and multi-BPM evaluation sets), analyzing the
section alone raised the beats within 10 ms from 9-28 % to 30-66 %; where whole-song timing was already right it
changed little (`timing-eval range-section`).

**Auto-time** changes nothing until **Apply...** is confirmed; Apply replaces every red line in one undo step and
keeps green lines. In **Normal** mode a steady song comes back as one red line whose offset can be edited before
Apply (the original detector's line: multi-BPM returns exactly it when it finds one tempo). The **BPM** box next
to it is empty for detection; type a BPM (e.g. `160`) to lock it, and Auto-time proposes one red line at that BPM
with only the offset fitted (MAMBO COMBO: detected 159.997 / 351, locked 160 / 352). **Auto-time section** and **Fit
offset** (right panel) work on the selected red line.

**Where Auto-time puts its lines.** The original detector's offset is the peak of its onset function minus 28.7 ms; on a
sharp hit (a kick from silence) that is 15-20 ms before the first sample of the hit, which is why every Auto-time
needed "+1/16 or +1/32 of a beat" by hand. Auto-time now measures, on the song's 150 strongest hits, how far the
waveform's leading edge (the steepest rise of the log energy over 4 ms) sits after that peak, and puts every line
there: 18.7 ms in the median over 161 reference songs (quartiles 13.7-20.7), 19.7 ms on ReCreate of the Red, where the
lines then sit within 0.2 ms of the user's own on two of his sections and 6 ms on a third. Ranked maps by other mappers sit on the
detector's convention, about 14 ms before that edge, so **Settings → Auto-time on the waveform's edge** turns it off.
The first red line goes to the first beat of a bar that has music (**Settings → Auto-time starts on a bar line**):
the beat model's second head predicts where mappers start bars, and on 143 held-out maps its best phase is the map's
first red line's phase in 133; the Auto-time offset alone was the map's first red line in 22 and with this in 91, the
rest a bar or two earlier or later. Where the BPM is known (the BPM box, Auto-time section, Fit offset, Auto-time range,
and, in Normal, sections of 24 beats or more that the analyzer put half a beat or so off) the beat model's beat head folded at that
BPM gives the phase instead of the original fold of the onset function, which can lock onto off-beat chugs or hats: within
25 ms of the map's grid in 94 % of 606 reference sections against 80 %, within 50 ms in 96 % against 83 %; in Normal that
raised aligned F10 on 8 of the 10 evaluation sets (A hold 0.809 → 0.847, C hold 0.693 → 0.799, F hold 0.608 → 0.681) and lowered none. A one-tempo answer of 200 BPM or more
that the model hears only on every second beat is halved (Dusk 256 → 128, the map's BPM; Rebellious exorcist and
Ragnarok show the same low ratio, and the model rule already gives their 188 and 234); where it hears them mostly on
every second beat the status line says the map may be at half of it. ReCreate of the Red is such a case (286.0 for the user's 143): type 143 in the BPM box and Auto-time, or
÷2 and then Fit offset; with the BPM given, its sections 143 @437, 180 @49108 and 143 @55108 come out 0.3, 3.3 and
0.3 ms from the user's own lines.

With several tempos, Normal mode proposes one red line per tempo section; the status line gives their number and
BPM range before you commit.
Measured on 30 single-BPM
maps and, on their held-out halves, 40 multi-BPM, 101 multi-BPM osu!mania, 7 hard and 24 ranked picks (final table
in `docs/PHASE3-STATUS.md`, "P3 final report"): single-BPM songs come out bit-identical to the single-BPM
detector; on held-out multi-BPM songs aligned F10 is 0.787 (Set A, single-BPM detector 0.611, median error 5.6 ms),
0.647 on complex osu!mania sets (0.281), 0.727 on other mania (0.560). It still **under-predicts red lines**
on most complex maps. **Tempo ramps** (steps every 1-3 s) are too short for the tempo window, so a second pass bridges
each tempo change: where the grid before stops fitting and the grid after starts, it tries whole-BPM steps
moving from one tempo to the other (4-16 beats each, each starting on the previous step's beat and landing on
the next section's grid), or a continuous ramp with a red line every 1, 2 or 4 beats, and keeps the ramp only
when the onsets support it clearly better than a plain switch. On held-out ramp beats this roughly doubles
the hits (osu!mania complex set 0.061 → 0.154, Set A 0.113 → 0.178) without changing any single-BPM song.
Ramps whose steps leave little trace in the audio (MAMBO COMBO's 4-beat 150/155 steps at 167-170 s), and
V-shaped ramps (down then up), still need hand timing as below.
Two final passes (2026-10-18, songs with more than one red line only): a BPM within 8 ms over its section of a
whole number becomes that number (ranked maps write 73 % of their red lines as whole BPMs; lines with exactly the
map's BPM on the osu!mania sets 128 → 161 of 219), and a section at or below 130 BPM whose onsets peak at the half
beat at least half as high as on the beat is doubled. That fixes the commonest error, half tempo on fast songs
(80 % of the beat-level misses): held-out aligned F10 on the osu!mania complex set 0.558 → 0.647, Set A 0.773 →
0.787, the user's hard picks 0.409 → 0.489. A real slow section with busy half beats can be doubled by mistake
(Camellia - Electromagnetic Stealth Girl's 110 BPM ending): halve it back with Apply fields.

**Drift** modes (the box beside Auto-time) are for a tempo that drifts slowly through the whole
song, as a live band or an old recording does (Billie Jean: 116.9-117.5 BPM, so one red line is 71 ms off in the
intro). It tracks the drums and fits one continuous grid with a red line every 4, 8, 16 or 32 beats; there is no phase jump at any line, and off-beat tracker hits are left out of the fit. On
Billie Jean, checked against its own onsets, the worst 10 s window is 4 ms off with 16 beats per line (9 ms with
32, 2 ms with 4). Do not use it on steady or multi-BPM songs: on 30 single-BPM maps one red line scores aligned
0.93-0.96 and drift (8 beats) 0.73-0.79, and on the free-tempo maps of Set E it beats multi-BPM only on JAPANESE
SOUL BROTHERS, Multigenre Smackdown and My Dearest. Review the list, then Apply... replaces the red lines. In Normal
mode Auto-time measures how much the drum attacks wobble from beat to beat and, from 4 ms over 20 measured beats,
suggests Drift 1 beat (Billie Jean 4.9 ms; sequenced songs 0.1-2.5 ms). It is only a hint: on the live songs whose
maps use a red line per beat (dense rock, piano, violin) too few clean attacks can be measured.

With **1 beat** in that box the red lines sit on every tracked beat instead, for drums played live, where every hit
is a few ms early or late and no line over several beats can follow it (Billie Jean: beat-to-beat intervals spread
6 ms; even the best line every 16 beats is up to 25 ms off a hit). On Billie Jean that gives 553 red lines, 0.5 ms
(median) and at most 6 ms from the kick and snare where two independent drum finders agree, against 4.6 / 32 ms with
16 beats. Beats with no onset or an off-beat tracker hit are spanned evenly from the beats around them. On 19 beats
another attack sits 16-20 ms before the drum and the line takes the earlier one (times in `docs/PHASE3-STATUS.md`):
check those by ear. Notes between beats are still interpolated. So that osu! does not draw a bar line on every
beat, the proposal sets "omit first barline" on every red line off a 4/4 bar counted from its first line; if the
bars are off, select the red line on a downbeat and press **Bar line here** (right panel) to count from it.

The **timing table** (right) has osu!'s three tabs, **All**, **Timing** and **Inherited**, with its columns (offset,
BPM or SV, meter, sample, volume, kiai). Click a row to select it, double-click to jump there; Ctrl+click and Shift+click select several, and
the selected red lines are what the editing tools act on. **Copy** (Ctrl+C) copies the selected rows, red
and green, as timing point rows to paste into the osu! editor; Ctrl+A selects every row of the tab. **Paste**
(Ctrl+V) adds red and green timing rows, retaining duplicates and marking same-kind overlaps orange;
Ctrl+Shift+V replaces all red lines (asks first). Copy to osu! subtracts 33 ms; internal copy/paste keeps original times.
Green points support Time/SV editing, addition with Ctrl+G, moving and deletion; required automatic normalization rows remain.

**Copy normalized** copies red and green lines for osu!mania with the scroll speed held constant: every red line
gets a green line with SV = main BPM / its BPM, the main BPM being the one held longest over the whole audio.
That is what most local mania maps with tempo
changes do (295 of 481; 279 of them to that same main BPM). Existing green lines keep their own SV on top and their
samples; a map that is already normalised is not normalised twice. Normalized greens are visible in the editor and saved. Applying
an auto-time proposal now keeps the sample set, volume and kiai in effect at each new red line.

**Multi-BPM songs, by hand:** put a red line near each tempo change, select one, then
- **Fit offset** keeps that line's BPM and moves it onto the nearest detected beat of its section (from the
  line to the next red line);
- **Auto-time section** also detects the section's BPM, or uses the BPM box.
Each is one undo step and touches no other line. Sections need at least 4 s: on MAMBO COMBO, 3 s windows
usually locked a quarter beat off. For a short tempo step, put its red line on a beat of the previous
section instead (←/→ to the beat, then Ctrl+P). Some passages can still lock half or a quarter beat off,
so a move larger than ⅛ beat is flagged in the status line — check it by ear. On MAMBO COMBO the five
sections of 10 s or more gave the same result whatever the start (±25/60 ms), and three agreed with the
hand-made test map within 3 ms. **Copy timing points** copies the red and inherited green timing point rows
only (no `[TimingPoints]` header), ready to paste into the osu! editor.

**Short tempo steps (ramps of 1-4 beats per step, Yooh - RPG style):** auto-timing cannot resolve them (one or
two onsets per step), so time them like a mapper. Put a red line on the accent where each step starts and one
where the next section starts (0.5x speed, ←/→ or Ctrl+P), box-select them, then
- **Snap to onsets** moves each selected line onto the strongest onset near it (within a quarter beat, at most
  60 ms; an onset 20 ms away counts half). Snap only lines that start on a clear hit;
- **BPM from spacing** gives each selected line the BPM that fills the gap to the next red line: the nearest
  1, 1.5, 2… 16 beats at the previous step's tempo, rounded to a multiple of 5 (within 10 ms) or a whole BPM
  (within 4 ms). The selected lines after the first move onto the end of the step before, truncated to whole
  ms as the osu! editor does, so the steps chain exactly. A line more than 16 beats from the next keeps its BPM.
When auto-time left a line of the next section near its start (RPG: 179858@110, 255447@210), move that line onto
the start with the ±1 beat buttons instead of placing a new one, and leave it out of the
snap: its grid is fitted over the whole section, so the last step measures against it better than against one
onset. Do not snap in quiet passages (RPG's ending at 354-358 s); place those lines by ear. The last red line of a
song has no next line to measure: type its BPM. Rehearsed on RPG from the audio alone (auto-time, delete the four
off-tempo lines, BPM from spacing on the 218-229 s accelerando, the five ramps): BPM equal to the official on 31 of
32 lines in the best runs (the first line is the same 190 grid one beat later); after one +8 ms shift of all lines
12 lines exact, 17 within 1 ms, the rest within 10 ms; the official sits 16 ms later than our onsets in the first
section and 2 ms earlier in the ending, which no audio analysis can know.
Both are one undo step. On the official Yooh - RPG timing (Featured Artist), with lines placed up to 30 ms off by
"ear", the two buttons gave 110 of 125 step BPMs exactly, each on the official chain (BPM from spacing without
snapping: 9 of 125). Lines placed exactly need no snap: BPM from spacing alone gave 25 of 25, snapping first 22.
The misses are lines with no hit under them (the quiet ending at
354-358 s, the 110 BPM section start at 178775). Snapped lines on RPG sit about 7 ms before the official ones;
across 144 reference maps our grid is a median 2.6 ms early (quartiles -7.7 to +2.0 ms).
A map with two red lines at the same time (10 of 6 065 local maps, ranked ones included) opens with the first of them,
as osu! reads it, and the status line says how many were ignored; saving after an edit writes each red line once.
Save .osu replaces only red timing rows and preserves other map data, UTF-8 BOM and line endings.
Existing files are backed up to `<name>.osu.bak` before writing; each save replaces the backup.
Saving from audio creates a minimal beatmap with a relative audio path.

Logic resides in `TimingAnalyzer.Editor` with xunit tests; Avalonia views and BASS device output require
desktop checks. Follow [the manual acceptance checklist](docs/EDITOR-MANUAL-TEST.md), especially audio/click
sync at 1.0 and 0.75, drag feel, and reopening a saved map in osu!. These manual checks remain pending.
Task commits and automated validation are recorded in [the Phase 1 checkpoint](docs/PHASE1-STATUS.md).

## Provenance

Static analysis only, of a binary the user owns. Target SHA-256
`0077c48973701b4dbc0799ad99c20b009f8e3ae0539fb724215df6894b71608b`. The original exe and BASS.dll
are not included.
