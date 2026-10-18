# Redline

**Redline** is a timing editor for osu! by **C4P741N**: auto-timing for steady, multi-BPM and live-played songs
(down to a red line on every drum hit), bar lines, a timing table like osu!'s, and normalised slider velocity for
osu!mania. Download it from [Releases](https://github.com/c4p741nth/redline/releases) (Windows x64, no .NET install
needed) and run `Redline.exe`. MIT licensed (see `LICENSE`); audio goes through BASS by un4seen, which is free for
non-commercial use.

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
src/TimingAnalyzer.App/    the Redline editor (Avalonia)
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

Timing Analyzer is made by **C4P741N**; **Help → About Redline** shows the version (releases are named
vYYYYMMDD.n) and credits.

The window has a menu bar (**File**: open, save, copy timing points, copy normalized; **Edit**: undo/redo, insert,
delete, select and copy rows; **Timing**: auto-time and apply, move to playhead, ms shifts and the per-line tools;
**Settings**: metronome, follow, restore default settings; **Help**: keys and colours (F1), about). Under it are
three toolbars: playback (Play / Pause, Follow, beat snap, speed, metronome and music volume), Auto-time (mode, BPM
box and, while a proposal waits, its offset and **Apply...**) and Shift (beat and ms buttons, **To playhead**). The
editor remembers volumes, metronome, follow, speed, beat snap, auto-time mode, table tab and window size between
runs (`%APPDATA%\Redline\settings.json`); **Settings → Restore default settings** resets them.

Shortcuts: Ctrl+O open .osu, Ctrl+Shift+O open audio, Ctrl+S save, Ctrl+Shift+C copy timing points, Ctrl+Shift+N
copy normalized, Ctrl+T auto-time, Ctrl+Enter apply, Ctrl+Shift+P move the selected lines to the playhead,
Ctrl+←/→ shift them 1 ms (Ctrl+Shift+←/→ 5 ms), M metronome, F follow, F1 the full list.

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

Open a map beside its audio file. Click to seek, Space to play/pause, Ctrl+wheel to zoom, and wheel or
the bottom slider to scroll. Drag a red line to move its offset; Ctrl+drag a later beat to stretch BPM
with the section start fixed. Insert or Ctrl+P adds a red line at the playhead, Delete removes the
selected lines, and Ctrl+Z/Ctrl+Y undo/redo. The side panel also accepts time, BPM and meter edits via
Apply fields. Clicking a row in the timing table only selects it (the playhead stays, so the line can be moved
onto it with **To playhead**); double-clicking jumps there (playhead on the line, line one third from the
left); while playing with Follow on, the list selects and scrolls to the red line under the playhead. Space
always plays/pauses, even right after clicking another button (only text boxes keep it). The loaded song's
"Artist - Title [Difficulty] (Mapper)" (or the audio file name) heads the window.

The time below the waveform reads like the osu! editor, `mm:ss:ms / mm:ss:ms` (position / song length):
`00:01:531` is the same instant as offset 1531. **Beat snap** (1/1–1/9, 1/12, 1/16,
default 1/4) sets the dim grid ticks, and **←/→** step the playhead to the previous/next tick of the
section under it, landing on red lines when crossing them. The view scrolls to keep the playhead on screen.

Drag across empty waveform space to box-select every red line in that band (Shift+drag adds to the
selection, Ctrl+A selects all, Esc clears). Selected lines are orange in the waveform and marked ● in the
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

The Shift group moves the selected red lines (pick them in the timing table or box-select them; Ctrl+A for all) by −1, −½, −¼, −⅛, +⅛, +¼, +½ or +1 beat,
or nudges by −5, −1, +1 or +5 ms. Beat shifts use each red line's own beat length, and each moved line
then lands on a whole ms. Each press is one undo step; negative times are allowed.
**Green lines (inherited SV/sample changes) stay fixed.** A shift that would make a red line cross or
touch an unmoved neighbour (or reorder lines) is rejected with a status message and leaves timing
unchanged; selected lines may move past each other's old positions. **To playhead** (Ctrl+Shift+P) moves the
selected lines by the distance from the line clicked last to the playhead, so that line lands on the playhead:
place the playhead on the hit by ear or with ←/→, click the line in the table, then press it.

**Auto-time** changes nothing until **Apply...** is confirmed; Apply replaces every red line in one undo step and
keeps green lines. In **Normal** mode a steady song comes back as one red line whose offset can be edited before
Apply (the original detector's line: multi-BPM returns exactly it when it finds one tempo). The **BPM** box next
to it is empty for detection; type a BPM (e.g. `160`) to lock it, and Auto-time proposes one red line at that BPM
with only the offset fitted (MAMBO COMBO: detected 159.997 / 351, locked 160 / 352). **Auto-time section** and **Fit
offset** (right panel) work on the selected red line.

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
the selected red lines are what the editing tools act on. **Copy selected** (Ctrl+C) copies the selected rows, red
and green, as timing point rows to paste into the osu! editor; Ctrl+A selects every row of the tab. Green lines are
read-only here.

**Copy normalized** copies red and green lines for osu!mania with the scroll speed held constant: every red line
gets a green line with SV = main BPM / its BPM, the main BPM being the one held longest up to the last note, as
osu! counts it (the end of the audio when the map has no notes yet). That is what most local mania maps with tempo
changes do (295 of 481; 279 of them to that same main BPM). Existing green lines keep their own SV on top and their
samples; a map that is already normalised is not normalised twice. The timing in the editor is not changed. Applying
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
