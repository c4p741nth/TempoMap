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
