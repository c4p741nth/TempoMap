# Phase 1 plan: Avalonia timing editor (MVP)

Goal: a desktop editor where a mapper can do the following:
- load a song (or an osu! `.osu` beatmap)
- see the waveform with the red-line beat grid
- play it with a sample-accurate metronome tick
- drag and stretch red lines until they sit on the beat
- export the result to osu!

Multi-BPM *auto* timing is Phase 2. Phase 1 only offers the existing single-BPM detector as one button.

This document is self-contained for an executor. Read `AGENTS.md` first. Its sandbox rules apply here too:
offline packages only, `DOTNET_CLI_HOME=.dotnet-home`, commit per green task, never push.

## Ground rules

- **Logic lives in `TimingAnalyzer.Editor`.** It has no Avalonia or BASS dependency and is unit tested.
  `TimingAnalyzer.App` holds only Avalonia views and controls, and it should stay thin.
- Packages are fixed. They are listed in `Directory.Packages.props` and already restored:
  - Avalonia 12.1.4: Avalonia, Avalonia.Desktop, Avalonia.Themes.Fluent, Avalonia.Fonts.Inter
  - CommunityToolkit.Mvvm 8.4.2
  - ManagedBass, ManagedBass.Mix, ManagedBass.Fx 4.0.2
  - BASS natives 2.4.17
  **Do not add packages.** HTTPS does not work in the sandbox.
- **Avalonia 12 APIs differ from 11.** There is no internet. Check API shapes in the package XML docs, e.g.
  `D:\packages\nuget\avalonia\12.1.4\lib\net10.0\Avalonia.Base.xml`, and by building.
- Use `CultureInfo.InvariantCulture` for every number in files, the clipboard and the UI.
  This machine runs th-TH with the Buddhist calendar.
- Never edit `reference/`, `reverse/` or `src/TimingAnalyzer.Core` behavior. The Core engine is parity-locked at 45/45.

## Layout (scaffolded in E0)

```
src/TimingAnalyzer.Editor/   timing model, .osu IO, beat grid, waveform peaks, click schedule, undo, view models
src/TimingAnalyzer.Audio/    + PlaybackEngine (BASS output, BASSmix mixer, click track, BASS_FX tempo)
src/TimingAnalyzer.App/      Avalonia 12 WinExe: MainWindow, WaveformControl
tests/TimingAnalyzer.Editor.Tests/   xunit 2, fixtures/*.osu (3 real beatmaps)
```

The reference direction is `App → Editor, Audio, Core`, `Audio → Editor`, and `Editor → (nothing)`.
`Editor` defines `IPlaybackEngine`, and `Audio` implements it.

## osu! timing facts

- In `[TimingPoints]`, each line is `time,beatLength,meter,sampleSet,sampleIndex,volume,uninherited,effects`.
  - `uninherited=1` is a **red line**. Its `beatLength` = 60000/BPM, in ms.
  - `uninherited=0` is a **green line** (inherited). Its `beatLength` is negative (an SV multiplier).
  - Old maps can have lines with only `time,beatLength`. Treat them as red lines.
- `time` is in ms. Write red-line times as **integers**, because osu! stable reads integers. Use
  `(int)Math.Floor(t + 0.5)`. Keep `beatLength` at full double precision with `R` formatting.
- A red line starts a new measure. Its section lasts until the next red line.
  - beat k of a section is at `time + k*beatLength`
  - measures are every `meter` beats
- `[General] AudioFilename:` names the song relative to the `.osu` folder.
- **Writing a `.osu`:**
  - replace only the red lines and leave every green line and other section untouched
  - output lines are ordered by time; a red line comes before a green line at the same time
  - keep the original encoding (UTF-8, BOM if present) and line endings
  - write `<name>.osu.bak` first

## Tasks (in order; each ends green and committed, and `docs/PHASE1-STATUS.md` is updated)

| # | Task | Done when |
|---|---|---|
| E0 | Scaffold projects, packages, fixtures (**planner**) | build 0 warnings, inside and outside the sandbox |
| E1 | `TimingPoint`, `OsuFile` parse/write, `[TimingPoints]` clipboard text | tests: all 3 fixtures round-trip **byte-identical** when unchanged; editing red lines changes only red-line lines; green lines are preserved; the 2-field legacy format parses |
| E2 | `BeatGrid` + edit operations + `UndoStack` | tests: grid lines in a time range across several red lines (beat/measure/subdivision 1/2, 1/4); `MoveRedLine` (offset changes, BPM kept); `StretchRedLine` (beat k lands on the target time, start fixed); add a red line at time t (it inherits that section's BPM/meter); delete; undo/redo of each |
| E3 | `WaveformPeaks`: a min/max pyramid (×2 decimation per level) from float samples, and a query for (startSample, endSample, pixelWidth) | tests vs a naive min/max at several zooms; levels are memory bounded |
| E4 | `ClickSchedule`: click sample indices for a sample range, given red lines, a rate and the downbeat flag | tests: index = `floor(t*rate/1000 + 0.5)`; no duplicates across adjacent ranges; a downbeat on each measure |
| E5 | `PlaybackEngine` (Audio): BASS default device; song decode stream (Float) → BASSmix decode mixer + click STREAMPROC (driven by `ClickSchedule`, so clicks are sample-accurate) → BASS_FX tempo stream (speed 0.5/0.75/1.0, pitch preserved) → play | `Play/Pause/Seek(ms)/PositionMs` (source position minus device latency), `ClickEnabled`, `Speed`, `Dispose`. Tests cover only the non-device parts. Code that needs a device is skipped when there is none. Everything else goes on the manual checklist |
| E6 | App shell: MainWindow, Open audio, Open `.osu` (loads AudioFilename + timing), `WaveformControl` (waveform from peaks; red, measure, beat and subdivision lines in distinct colors; playhead), zoom (Ctrl+wheel), scroll, click to seek, Space play/pause, speed selector, click on/off, a side panel listing red lines (time, BPM, meter; editable) | app builds; view models are tested in Editor.Tests |
| E7 | Editing in the control: click near a red line to select it, drag to move it, Ctrl+drag a beat line to stretch, Insert = add a red line at the playhead, Delete, Ctrl+Z/Ctrl+Y | operations go through the E2 commands (tested); app builds |
| E8 | Auto-time button: `Core.Analyzer` on the loaded audio (background thread) → proposes one red line (BPM, osu! offset) and shows exact/approximate status. Apply replaces the red lines only after confirmation. **Export**: copy `[TimingPoints]` to the clipboard; Save `.osu` (with backup) | tests for the proposal mapping and the save path (temp copy of a fixture) |
| E9 | `docs/EDITOR-MANUAL-TEST.md`: a checklist for the user (audio/click sync at 1.0 and 0.75, drag feel, save into osu! then open the osu! editor) + README section | written |

The planner smoke-runs the app (launch, open fixture, close) after E6–E8, because a GUI cannot be judged in the sandbox.

## Phase 1.1 additions (user request, 2026-10-16)

| # | Task | Done when |
|---|---|---|
| E10 | **Separate volumes for music and metronome.** `IPlaybackEngine` gets `MusicVolume` and `ClickVolume` (0.0–1.0, clamped; defaults music 1.0, click 0.7). Changing either takes effect **immediately without rebuilding the BASS graph** (no gap, no seek), and the value survives every rebuild (seek/speed/timing change). Click gain is applied inside `ClickTrack.Render` (add a gain parameter; the callback reads the current value). Music gain is BASS_ATTRIB_VOL on the song source in the mixer. Check in the ManagedBass/BASSmix XML docs that the mixer applies source volume; if it does not, use a DSP or mixer envelope instead. UI: two sliders, "Music" and "Metronome" (0–100 %), next to the speed selector. 0 % = silent | tests: rendered click samples scale exactly by the gain; gain 0 gives silence; clamping; the view model passes slider values to a fake engine and they persist after a reload of the same song. Build 0 warnings |
| E11 | **Shift red lines by a beat fraction or by ms.** `TimingEdits.ShiftRedLines(scope, beats)` moves each affected red line by `beats × that line's own beatLength`, with beats ∈ {−1, −½, −¼, +¼, +½, +1}. `ShiftRedLinesMs(scope, ms)` moves by a fixed ms (±1, ±5). Scope = **All** red lines or **Selected** red line only. A shift is **one undo step**. Green lines are not moved (document this in the UI tooltip and the README). Selected-only shifts must not cross a neighbouring red line: reject with a status message and leave the timing unchanged. Negative times are allowed. Times stay double in memory; export rounding is unchanged. UI: a "Shift" group with an All/Selected toggle, beat buttons −1, −½, −¼, +¼, +½, +1 and ms buttons −5, −1, +1, +5. Playback picks up the new timing the same way as other edits | tests on the my-dearest-tv fixture: shift All by +½ moves every red line by exactly its beatLength/2 and keeps BPM/meter; undo restores the original; after undo the export is byte-identical to the fixture; the ms nudge works; a selected-only shift that would cross is rejected; scope Selected moves only that line. Build 0 warnings |

| E12 | **Follow playhead (auto-scroll while playing).** `FollowPlayhead` toggle, default on, with a "Follow" checkbox. While playing with follow on, the viewport keeps the playhead at **⅓ of the visible width from the left**: `StartMs = clamp(position − width/3, 0, max)`. At the end of the song, clamping stops the scroll. **Smooth:** update every display frame (Avalonia 12 `TopLevel.RequestAnimationFrame`, checked in the XML docs, or a ~16 ms timer). Between engine reads, extrapolate the position with a pure `PlayheadClock` in Editor: `last engine position + elapsed wall time × speed`. Re-sync to each new engine read without moving backwards visibly: ease out small corrections, snap large ones such as a seek. **Suspension:** a manual scroll or zoom during playback suspends follow until the next Play or until the Follow checkbox is toggled. Click-to-seek keeps follow on. Pausing leaves the view where it is | tests: viewport math (anchor ⅓, clamps at start/end, the zoom width is respected); `PlayheadClock` (speed scaling at 0.75/1.0, monotonic while playing, a seek snaps, small drift eases out); suspension rules through the view model with a fake engine. Build 0 warnings |

### Phase 1.2 additions (user request, 2026-10-16)

| # | Task | Done when |
|---|---|---|
| E13 | **Beat snap divisor + arrow-key navigation (like the osu! editor).** `SnapDivisor` ∈ {1, 2, 3, 4, 5, 6, 7, 8, 9, 12, 16}, default 4, chosen from a "Beat snap 1/N" dropdown. The waveform grid draws ticks for that divisor: red line > measure > beat > tick, with distinct colours. Use the existing `BeatGrid` division parameter. **← / →** move the playhead to the previous/next snap tick *strictly* before/after the current position. The tick is computed in the timing section that contains the position. Crossing a section boundary lands on that red line or that section's ticks. A position already on a tick (within 0.5 ms) moves to the neighbouring tick. Works while paused (sets the position) and while playing (seeks). With Follow on, the view follows | tests: next/prev tick for every divisor, including 1/3, 1/5, 1/7, 1/9, 1/12 (non-integer ms ticks); exact-tick and ±0.4 ms cases; crossing a red line forward and backward; before the first red line (ticks extend backwards from the first red line); end of song clamps |
| E14 | **Time display `m:ss / m:ss`** (position / song duration), invariant culture, floored to whole seconds, `h:mm:ss` from 1 h. Shown prominently near the transport and updated every frame while playing. Keep the precise ms readout as a secondary label | tests: the formatter (0, 59.999 → 0:59, 60 000 → 1:00, 3 600 000 → 1:00:00); the view model exposes it and updates it with the playhead |
| E15 | **osu! precision.** osu! accepts BPM with **3 decimals** and red-line offsets as **integer ms**. Every point the app creates or edits is quantized at commit time: auto-time apply, Insert, Apply fields, move/drag, stretch, every E11 shift (beat and ms). Quantization: `time = floor(t + 0.5)`, `bpm = round(60000/beatLength, 3, MidpointRounding.AwayFromZero)`, `beatLength = 60000 / bpm`. Shifting by beat fractions rounds each moved line to integer ms after the move. **Lines the user did not touch keep their exact original values**, so the byte-identical round-trip still holds. Display BPM with up to 3 decimals (trim trailing zeros: `160`, `159.997`) and time as an integer everywhere: list, fields, proposal text, status. The playback/click grid uses the quantized values, so the metronome matches what osu! will play | tests: the proposal `159.9968637393969` → shows and applies `159.997`, offset integer; Apply fields with `159.99964` → `160`; shift +½ beat on a 375.007 ms line → integer result; stretch result has a 3-decimal BPM; the untouched fixture round-trip is still byte-identical; export of an edited line has an integer time and a beatLength equal to 60000/bpm₃ formatted `R` |
| E16 | **Add-red-line shortcut + box selection of several red lines.** Shortcuts: **Ctrl+P** (osu!-style) adds a red line at the playhead, and Insert keeps working. **Ctrl+A** selects all red lines. **Esc** clears the selection, or cancels a drag. Show them in the help line and README. **Box select:** a left-drag that starts on empty waveform space (not on a red-line handle, more than 4 px of movement) draws a translucent band and, on release, selects every red line inside its time range. A plain click (≤ 4 px) still seeks. **Shift+drag** adds to the selection. Selected lines are highlighted in the waveform and the list. A multi-selection works with the existing actions: the E11 shift buttons with scope "Selected" move **all selected lines** (each by its own beat fraction or ms, then E15 quantization); Delete removes all of them. Each is **one undo step**. The crossing rule: reject the shift (with a status message) if any selected line would cross or land on an unselected red line. Selected lines keep their relative order. Field editing applies to the primary (last clicked) selection | tests: time-range selection, add-to-selection, select-all/clear; multi-shift moves each selected line by its own beat (fixture my-dearest-tv, several lines); rejection when crossing an unselected neighbour, leaving the timing unchanged; multi-delete with undo/redo; the shortcut add uses the E15 quantization |

### Phase 1.3 additions (user requests, 2026-10-16)

| # | Task | Done when |
|---|---|---|
| E17 | **Lock BPM + per-section auto-time (multi-BPM aid).** A BPM box (empty = detect) feeds the whole-song auto-time, so the detector fits only the offset (Core already supports `Analyze(…, bpm)`). For multi-BPM songs: **Fit offset** keeps the selected red line's BPM and moves it to the nearest detected beat of its section (that line up to the next one, or the song end); **Auto-time section** also detects the section BPM, or uses the box. Analysis slices the cached whole-song onset envelope, so a section has no filter start-up transient; the whole song runs exactly `Analyze`. One undo step, never crosses a neighbour, other lines untouched. Sections < 4 s are refused with a hint (snap to the previous section's beat, then Ctrl+P); moves > ⅛ beat are flagged | tests: request routing (whole song / section bounds / BPM box / kept BPM), nearest-beat placement, rounding, rejection paths; real-song acceptance on MAMBO COMBO + the user's test map |
| E18 | **Follow, volume and time display fixes.** Zoom keeps following (only scrolling suspends). Volume and click-gain changes are heard immediately: the BASS_FX output channel is unbuffered (`ChannelAttribute.Buffer = 0`), verified by a silent control probe. Replace the top `m:ss` clock and the `0.0 / 0.0 ms` label with one osu!-style `mm:ss:ms / mm:ss:ms` label below the waveform (whole ms, as offsets) | tests: zoom keeps the ⅓ anchor while playing, plain zoom when paused; osu! time format |
| E19 | **Follow resumes after scrolling.** User report: after opening, follow only worked after toggling the checkbox. Cause (reproduced headless with the real window and BASS engine): one wheel scroll while playing suspended follow, and E12 kept the suspension through click-seeks. New rule: a view scrolled ahead resumes when the playhead reaches its ⅓ anchor (seamless); a view scrolled back stays until seek, ←/→, Play or the checkbox; seek now resumes | tests: ahead → waits then resumes without a jump (wheel and slider); back → stays; seek/checkbox/Play resume; follow off never moves the view |

## Guardrails

- Keep UI code thin. If a behavior can be expressed as a function of data, put it in Editor and test it.
- The same failure after two fix attempts means stop. Fill in `docs/PHASE1-STATUS.md` "Last stop".
- Environment denials: try a workspace-local path or an env override first. Stop only if that fails.
