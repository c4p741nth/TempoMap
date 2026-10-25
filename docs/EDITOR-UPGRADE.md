# Editor upgrade — approved scope

The user approved F01–F18 and A1/A2/A3/A6, all B and C findings on 2026-10-25.
A4/A5/A7 are explicitly excluded. Existing research is in [FEATURE-REVIEW](FEATURE-REVIEW.md).

## Decisions

- Opening audio uses the original file. Save preserves the mapset's original audio reference.
- Ogg conversion is an explicit command with sample-rate/quality controls (default quality 6).
- Preserve the original backup, use atomic file replacement and recover unsaved editing sessions.
- Keep manual SV and typed BPM locks. Preview/analysis must not silently change committed timing.
- No new packages. No Git state changes or GitHub operations; user handles commits/push.
- Performance work in parity-protected DSP requires measured stage hashes before/after on multiple songs.

## Implementation checklist

- [x] U1 — Atomic saves/first backup; original audio on open/save; explicit configurable conversion (A1–A3).
- [x] U2 — Dirty guard, autosave/recovery, projects, Save As/Revert/recent directories (F01/F13, C3/C4).
- [x] U3 — Bounded metronome, analysis cache lifecycle/reuse and measured DSP parallelism (B1–B6).
- [x] U4 — Progress/cancellation, responsive transport and alternative BPM results (F05/F17, C1).
- [x] U5 — Range loop/pre-roll, rate comparison, anchors and tap tempo (F02/F07/F11/F16).
- [x] U6 — Filter/onset/spectrogram, SV/Kiai, ruler/overview/bookmarks/multi-row (F03/F06/F08/F12/F14, C6–C9).
- [x] U7 — Proposal ghost/A-B audition, per-section review/navigation (F04/F09, C2).
- [x] U8 — Shortcut configuration, command menus, undo labels, input/accessibility/help and copy profiles (F10/F18, C5/C10–C16).
- [x] U9 — Export .osz, conversion UI and import diagnostics (F15/F17).
- [x] U10 — Remove obsolete caches/build copies (A6); final full tests, real-input UI checks and normal Release build.

U1–U10 are implemented and verified. The A6 deletion was run by the user on 2026-10-26 (see "A6 cleanup" below).

## U1 validation

- Repeated-save guard failed on the original implementation; Editor 257 tests now pass.
- Atomic replacement preserves the first backup across repeated saves/document reloads. Existing backups
  receive no writes; a new session uses a distinct name. Locked-target failure leaves original bytes intact.
- Opening MP3/WAV/FLAC/Ogg bypasses encoder/probe even when both are unavailable. Native smoke opens,
  decodes, plays silently and seeks into the last 100 ms of each original synthetic format.
- Found and fixed a native FLAC length rounding case: Windows reported 1 s for 1.25 s of decoded PCM.
  Playback uses the known decoded duration and decodes only the short tail past the native seek limit.
- Explicit conversion produced 48 kHz Vorbis with quality 4; source references stay unchanged on Save.
- Normal App Release: 0 warnings/errors. Evidence: `.dotnet-home/editor-upgrade/u1-*` and `native/native-result.json`.
- Conversion UI and session/recovery work were subsequently completed in U2/U9 below.


## Final validation — 2026-10-26

- C# **313 passed** (Editor 271, Core 42 including golden stages); Python **10 passed**.
- Actual MainWindow input smoke: **131 checks** at 1280×720 and 900×500 with 553 points. Covers insert/focus,
  remapped keys and preferences Save, ComboBox safety, tap/loop/anchors, bookmark/filter/onset/spectrum controls,
  proposal list/A-B/candidates, typed BPM 220 and 143.125 through actual Auto-time, Cancel, dirty guard and startup recovery.
- Layout with the large synthetic list: waveform 369/171 px and table 367/190 px at those sizes. Proposals retain
  waveform room; transport remains available during analysis. Headless measurements establish geometry and input behavior, not GPU frame rate or listening quality.
- Atomic repeated-save guard was red before the fix. Locked target failures preserve original/backup bytes;
  project roundtrips preserve manual SV, typed BPM, bookmarks and source references. `.osz` keeps original audio bytes and does not save the source map.
- Native original-format smoke: **11/12** advertised extensions open/play silently and seek at zero,
  middle, late tail and EOF. MP3/WAV/FLAC/Ogg all pass without encoder/probe. Opus remains unavailable in the native decoder;
  an actionable error directs users to the explicit converter. Vorbis conversion verifies 48 kHz/quality 4; UI defaults remain Original/quality 6.
- Found two native seek boundaries: FLAC length rounded down by Windows, and WMA packet rounding (including a
  seek-to-zero jumping past the first packet). Fresh streams remain at zero; rounded seeks decode forward to the requested frame.
- Old AudioContent is collectable before analyzing another song. Full and range inference reuse their actual cached outputs;
  range cache retains the original ±10 s context. ONNX/session/cache disposal and selective invalidation are in place.
- Final envelope SHA-256, autocorrelation SHA-256 and full analysis result match the pre-change recordings on **3 songs**.
  Core arithmetic/reference/expected fixtures were not changed. Normal Release solution build: **0 warnings/errors**.

## B5 parallelism experiment

Parallel bands in bounded blocks retained all recorded numerical results. Warmed end-to-end envelope timings below
were slower on most inputs, so the scalar path is retained with cooperative cancellation/progress. A speedup is not claimed.

| Input | Scalar mean ms | Parallel mean ms | Parallel change |
| --- | ---: | ---: | ---: |
| Song 1 | 945.27 | 1040.06 | +10.0% |
| Song 2 | 554.63 | 595.80 | +7.4% |
| Song 3 | 1349.30 | 1324.28 | -1.9% |


## A6 cleanup (done 2026-10-26)

Inventory: **36 targets / 3741 files / 3,335,745,202 bytes** under ignored workspace paths.
Includes the old `.eval-cache`, earlier scratch copies and the active probes' bin/obj copies. There are no tracked
files or reparse points in the checked scopes. DOTNET_CLI_HOME configuration and current evidence/source probes are retained.

Automatic approval review in Codex rejected both the manifest command and a direct literal-path deletion with **“blocked by policy”**;
it provided no more specific reason. The user then ran the revalidating helper
`.dotnet-home/editor-upgrade/cleanup-approved.ps1` from their own terminal (`-WhatIf` listed all 36 targets, then the real run).
A read-only check afterwards found **0 of the 36 manifest paths on disk** and no `.eval-cache`; what remains under `.dotnet-home`
is `.dotnet`, `.templateengine` and `editor-upgrade` (198 files, 7,343,958 bytes: evidence, logs and helper scripts).
The script and `cleanup-manifest.json` stay beside the evidence.

Evidence: `.dotnet-home/editor-upgrade/validation.json`, `final-suite/*.trx`, `ui/final.log`, `ui/result.json`,
`native/native-result.json`, `cache/result.json`, `perf/before.json`, `perf/repeated.json`, `perf/final.json` and build/test logs.
The new GUI probes remain local; A4/manual-checklist work was explicitly excluded. Listening and osu! acceptance remain user checks.

Normal App/Editor/Audio/Core DLL hashes match the exact MainWindow smoke output. The large-meter guard failed
with DivideByZeroException before widening meter arithmetic (`meter-before.log`). Malformed recovery sections
are skipped instead of blocking startup; null settings collections use safe defaults.

## Main-window controls (E58, 2026-10-26)

The Shift row (beat, ms and BPM steps) was no longer on the main window: its controls lived only in F3 → Listening (the manual test and a tooltip
still called it "the Shift row", so it existed before; which change removed it is not recorded). The metronome switch also disappeared on windows
under 1100 px. The user found it hard to work that way. (This section first also added a Selected/All scope box; E61 removed it again.)

- **Shift row restored** under Auto-time: ❮ ❯ beside beat / ms / BPM steps (and, until E61, a scope box). `Ctrl+Alt+←/→` (configurable: "Shift earlier/later by beat step")
  and the Timing menu now reach the beat step too; ms and BPM keys are unchanged.
- **Metronome** stays on the transport row at any width: checkbox with sliders from 1100 px, a ♪ toggle below. A second transport row cost the
  waveform its 160 px minimum at 900×500 (137 px measured), so the narrow form is one 32 px button.
- **Loop and Play range** moved to the waveform header, beside the range they act on (no extra height). F3 keeps pre-roll, tap, anchors and the rest.
- **Defects found while checking:** the tooltip shortener cut every long tip at its first ". ", leaving only the key ("Ctrl+T.") for 6 of 15 long tips
  (now `MainWindow.ShortTip`); table header "Sample" ran into "Vol." (columns 56/52 px); F3 and preferences tab titles were 24 px; the review list had no label.
- **Still behind one more click** (unchanged, by design): the "Timing tools" expander is collapsed (Auto-time range/section, Fit offset, Check sections,
  Multiply BPM, Snap to onsets, BPM from spacing, Fill ramp, Bar line here; all also in the Timing menu); F3 holds tap tempo, anchors, alternate speed,
  display layers/lanes/measure view, bookmarks, A/B audition and BPM candidates.
- Validation: headless probe 185 checks (Shift buttons incl. scope All, undo back to start, ♪ toggle, Play range, tooltips; 1280×720 and 900×500),
  C# 313 passed, Release solution 0 warnings/errors. Screenshots: `.dotnet-home/editor-upgrade/ui/{layout,screen,tools-*,preferences}-*.png`.

## Windows, dialogs and themes (E59, 2026-10-26)

The user asked which windows fall out of the theme or look dated, to move the menu to the top left, and to fix F3. Method: a headless gallery
(`.dotnet-home/editor-upgrade/gallery`) renders the main window (empty, with song, with proposal, measure view) and every dialog in Dark and
Light (36 PNG in `gallery/out`); each picture was read. The behavioural probe (`ui`, 185 checks) and C# tests (313) still pass.

- **Found:** all code-built dialogs were bare text with left-aligned buttons and no main action; F1 was a plain text list that overflowed;
  About had no logo; Audio diagnostics printed a raw `ToolStatus { … }` record; ListBox had no frame (an empty list looked like a pale gap
  in Light) and a selected row was white on mint in Dark; NumericUpDown had a black border unlike every other control; idle tab titles were
  near-invisible in Light; the overview, measure view and the waveform overlays (ghost grid, onsets, SV lane, Kiai, bookmarks, time labels)
  used fixed colours (`Brushes.Gold`, `Cyan`, `Gray`…), poor on Light; F3 was one long column with full-width sliders, the volume value on its own line.
- **Header:** menu first at the top left; the logo shrank to a 22 px mark with a muted name beside the theme switch (the title bar already
  names the app); the bar is 11 px lower, which the waveform gets (313 px at 1280×720).
- **One family:** `Styles/Workspace.axaml` now carries the 28 px control heights, NumericUpDown, ListBox frame/selection, tab titles, and the
  `primary` / `danger` / `hint` / `title` / `chip` / `error` classes; `MainWindow.Dialogs.cs` holds `NewDialog`, `DialogBody`, `Card`,
  `ButtonRow`, `LabeledRow` and the dialogs built from them (Choose, Confirm, Recover, About, Keys, Thai guide). Prompts put the main action
  in the accent colour on the right and Discard in red.
- **F3:** cards (318 px; two columns at the default 720 px, one when narrow), each slider on one line, the Listening tab fits 720×680 without scrolling.
- **F1:** command keys as chips, fixed keys, colour swatches that use the same theme brushes as the canvas; Change shortcuts… opens the editor.
- **Canvas colours:** `CanvasPalette` reads the new `Overlay*` brushes (Dark and Light) and follows a theme change.
- **Not changed:** the native file pickers follow the OS, not the app theme.

## Startup prompt, overview, layout memory (E60, 2026-10-26)

- **Prompt on a fresh start (bug):** with "Auto normalize SV" remembered on, the saved baseline of an empty timing was captured with it off, so
  `IsDirty` was true before any file was open and closing asked "Unsaved changes". Reproduced headless (`gallery --startup`: fresh with the choice on →
  dirty, prompt; off → clean). Fixed twice over: `IsDirty` needs a workspace (audio, beatmap or project), and `ApplySettings` re-bases the saved timing
  while nothing is open. With a song open the choice still counts as an edit, as it changes what Save writes. Tests: `StartingWithRememberedNormalizeSv…`.
- **Overview:** the frame logic was right (view 10–20 s of 20 s ends at 100%); what looked short was geometry: the 20 px strip touched the card's rounded
  bottom, which clipped the frame's bottom edge and the playhead. Now 22 px with a 6 px gap, the frame drawn half a pixel inside with rounded corners, the
  playhead clamped one pixel off the ends, the wave using 92% of the height.
- **Splitter:** the bar between waveform and table is transparent at rest, a soft accent under the pointer and the accent while dragging; `Cursor=SizeWestEast`
  is set explicitly because that cursor is now the only cue.
- **Timing tools:** starts open, folds with its header, state remembered. Its Expander header/body were Fluent's near-black/grey; now theme colours.
- **Remembered (new):** waveform layer, onsets, SV/Kiai lanes, four rows, ghost grid, loop, pre-roll, alternate speed, table panel width (338 px up to 60% of
  the window), Timing tools open. Already remembered: volumes, metronome, follow, speed, snap, Shift steps, BPM step, auto-time choices, table tab, theme,
  window size and maximised state, shortcuts, copy profiles, recent files. Not yet: window position, F3 window.
- Validation: C# 315, probe 185 (900×500 still keeps the 160 px waveform), `gallery --layout` (apply → change → close → reopen), Release 0 warnings.

## Overview wave, toolbar heights, scope box removed (E61, 2026-10-26)

- **Scope box removed from the Shift row (user's call, and a mistake of mine):** the user selects the lines in the table first, and `EditorViewModel` already said
  "The window has no scope box: lines are picked in the timing table (Ctrl+A for all)". E58 had put it back from an outdated manual-test line. The row now acts on the
  selection; `ShiftScope` stays in the view model (default Selected, covered by the Shift tests) but nothing in the window sets it.
- **Overview wave drawn outside its strip (real bug, found with the user's own song):** the strip and the measure view scaled peaks by raw value, the main waveform by
  `0.4 × height ÷ max(1, loudest peak)`. A song with peaks near 1.9 was drawn about twice the strip's height, over the card's bottom edge, so the frame and the playhead (the strip's
  height) looked smaller than the wave. The synthetic test song never showed it. Both now use `WaveformControl.PeakScale` (the same rule, 92% of the strip at the loudest peak),
  the strip clips to its bounds, and the gap under it is 8 px.
- **Toolbar card heights:** the playback card was 72 px against 50 px for Auto-time and Shift because Fluent's horizontal slider is 50 px tall (thumb and track in the middle of reserved tick
  rows). A style gives it `Margin="0,-11"`: drawn as before, 28 px of layout. (`Height="28"` was tried first and cut the thumbs in half.) The seek bar and F3 sliders shrink the same way:
  the waveform gained 22 px (347 px at 1280×720).
- Validation: probe 189 checks (adds: no scope box, Ctrl+A then shift moves every line, card heights equal), C# 315, Release 0 warnings, gallery pictures with the user's real song read.
