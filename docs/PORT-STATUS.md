# Port status (checkpoint)

Progress log for `docs/PORT-PLAN.md`. Update this file at the end of every task, or when stopping
early, and commit it together with that task's work. A new run resumes from the first task here
that is not `done`.

| Task | Status | Commit | Notes |
|---|---|---|---|
| T1 Scaffold | done | d271581 | Done by the planner outside the sandbox: HTTPS fails inside it, so packages were restored into D:\packages\nuget beforehand. Release build 0 warnings. Inside the sandbox (workspace-write, DOTNET_CLI_HOME=.dotnet-home) build/test/run work offline. BASS 2.4.17 natives (ppy.osu.Framework.NativeLibs 2025.806.0-nativelibs) are copied next to the Cli exe. |
| T2 Biquad / FFT / coefficients | done | b55c002 | Release build: 0 warnings/errors. 4 tests pass: 1024-point FFT vs naive DFT and inverse, random autocorrelation vs direct sum, every coefficient cascade vs regenerated SciPy fixture. Coefficients embedded directly from reverse/coefficients.json. |
| T3 Envelope / BPM / offset / timeline + golden | done | fdfde11 | Release build: 0 warnings/errors; 7 tests pass. Judgement and Hayakore golden stages: envelope max abs diff 0 for both; acf max abs diff 9.3132257461547852e-10 and 1.0477378964424133e-9 respectively, within relative 1e-9 tolerance. Status/BPM/internal offset/flags/timeline match auto and supplied BPM/offset modes. Silence and duration-cap checks pass. Golden files are regenerable and ignored. |
| T4 Audio (BASS) + Cli | done | 44087ba | Release build: 0 warnings/errors; 7 tests pass. Judgement stdout matches the required BPM 284.998, offset 136 and timeline exactly. Auto output matches the original on Judgement, Hayakore, lapix and Jamie (4/4); no BASS delay shim needed. Usage/missing file/invalid BPM/invalid offset/silence checks: 5/5. Documented decoder distinction in ALGORITHM.md. |
| T5 Parity 45/45 | done | baec6fa | Full C# CLI vs original: 45/45 exact printed BPM/offset/timeline matches, 15 per mode (auto, bpm, bpm+offset), all four timeline symbols covered. No parity fix attempts needed. Runner supports --candidate and --songs; original positional Python-reference mode still passes Judgement auto (1/1). Five Python harness tests pass; previous empty-result behavior makes its new negative control fail as expected. Release build: 0 warnings/errors. Full log: .dotnet-home/parity-csharp.log (local, ignored). |
| T6 README | done | 624b2a5 | Documents prerequisites, offline restore/build, all CLI modes and exit codes, golden-data generation, C#/Python tests, and 45/45 C# parity. Offline restore and documented fixture selection verified. Final Release build: 0 warnings/errors; 7 C# tests and 5 Python harness tests pass. No packages added; reference and reverse files preserved. |

Status values: `todo`, `in-progress`, `done`, `blocked`.

## Resolved blockers (history)

1. **Store Python blocked in the sandbox** (`CreateProcessAsUserW` 1920).
   Fixed: `.venv` rebuilt on python.org Python 3.13.15.
2. **`dotnet` denied writing `.templateengine` / `.dotnet` under the user profile.**
   Fixed: set `DOTNET_CLI_HOME` to the repo-local `.dotnet-home` (gitignored).
   Note: a standalone `codex sandbox` defaults to read-only. Probes need `-c sandbox_mode="workspace-write"`.
3. **HTTPS unavailable in the sandbox** (Schannel `SEC_E_NO_CREDENTIALS`), so NuGet cannot reach nuget.org.
   Fixed: all plan packages are pre-restored into `D:\packages\nuget`, and restore works offline.
   Do not add new packages.

## Last stop

E49 (2026-10-25): rebuilt E48 into the standard Release path after confirming the user closed the old App.
App build 0 warnings/errors; App/Editor SHA256 match the verified E48 preview. C# 282/282 pass.
Removed 8 generated duplicate-output targets, 457 files, 998,587,067 logical bytes; no tracked files,
workspace escapes or reparse points. Normal exe/model/native DLLs verified after cleanup.

- When: 2026-10-25
- Who (executor/model): Codex
- Status/commit: T1-T6 remain done; E45-E49 uncommitted on base 821a2f6 as previously requested.
- Stopped because: normal rebuild and cleanup complete. No product-code, engine, package or version changes.
- Next step: `src/TimingAnalyzer.App/bin/Release/net10.0/win-x64/Tempomap.exe`.
  Preview/scratch compiled copies are removed; validation sources/results remain. E48 UI checks remain 132 + 81
  (not rerun for this cleanup). Manual listening/acceptance and portable FFmpeg packaging remain separate.
  No git mutation or network git action. Evidence: `.dotnet-home/e49-rebuild/`.

## Phase 1 editor follow-up

E45 recording follow-up: `osu_audio_calibration.py` now matches the known reference waveform so tick residue at
the song's edges cannot count as music. Its regression was observed failing before the fix; Python 9/9 now pass.
The real recording and remaining control are in docs/OSU-AUDIO-TIMING.md. No C#/Core/decoder changes in this follow-up.

E45 (2026-10-25, base 821a2f6; task commit pending user approval): added calibrated Copy to osu! and synthetic
stereo audio calibration tooling, documented the DLL decode/output comparisons and the unmeasured stable-session
residual. Core/reference/reverse and package versions are unchanged. Release build: 0 warnings/errors;
Editor 225/225, Core 41/41; Python 8/8; headless MainWindow clicks/clipboard 40 checks. Disabling the copy shift
failed 7 of 12 focused cases; exact source restoration was hash-checked, followed by the final green checks.
Actual osu! editor capture/listening remains on the manual checklist. No fixed offset preset was introduced.

E1-E12 are implemented and verified; see docs/PHASE1-STATUS.md for task commits and validation (E10 c4bc132; E11 0ce8221; E12 555955b). Core, reference and reverse remain unchanged. Latest Release check used normal output paths: 0 warnings/errors; Core 7/7, Editor 83/83, 90 total, no failures/skips. E12 adds smooth follow with a one-third anchor, pure interpolation and tested navigation suspension; its disabled-guard negative control failed all 3 navigation cases before exact restoration and full green checks. Manual editor/audio/osu! acceptance remains; see docs/EDITOR-MANUAL-TEST.md.

E45 clarification: for the user's local copy-and-listen workflow, recommend Copy shift -33 ms (1000 -> 967), selected from the actual captures; no more recording needed to choose a usable setting. Shift 0 preserves source timing and was a different objective. The -36 trial was worse in the provided controls. No setting or source timing is changed automatically; variable tick timing remains.
