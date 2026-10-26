# Agent instructions

This repo is **TempoMap** (named Redline until 2026-10-18), a timing editor for osu! by C4P741N (MIT). It started by
rebuilding TimingAnlyz 0.32.4, an osu! BPM/offset detector. The GitHub account that hosted it was suspended on
2026-10-18; the local branches `history-main` / `history-full` keep the pre-release history (see docs/PHASE1-STATUS.md
E34). Test fixtures are synthetic (`tools/make_fixtures.py`): never commit someone else's beatmap.

- **Open work across all phases: `docs/TODO.md`.** Phase 3 (complex multi-BPM) is done: `docs/PHASE3-PLAN.md`,
  results in `docs/PHASE3-STATUS.md` ("P3 final report").
- Phase 1 editor: `docs/PHASE1-PLAN.md` (tasks E1–E9 and later). The checkpoint is
  `docs/PHASE1-STATUS.md`. Update it at the end of every task and before stopping, the same way as
  the port checkpoint below.
- Done: the C# engine port, `docs/PORT-PLAN.md` T1–T6. Parity is 45/45. Its rules below still protect `src/TimingAnalyzer.Core`.

## Hard rules

- `reverse/` (decompiler output) is local only and not in the repository; the extracted tables are
  `src/TimingAnalyzer.Core/coefficients.json`.
- `reference/timing_analyzer.py` is the spec. It matches the original exe 45/45. Never edit
  `reference/`, `reverse/` or the expected parity results to make C# pass. Fix the C# instead.
- Format every number and date with `CultureInfo.InvariantCulture`. This machine runs th-TH with the
  Thai Buddhist calendar.
- Do one task at a time. Build and test until green, then `git commit`. Never leave verified work
  uncommitted. Push or release only when the user asks; releases are tagged vYYYYMMDD.n (the n-th that day), with
  the App csproj `InformationalVersion` set to match (`tools/bump-version.ps1` sets both), and ship a self-contained win-x64 zip without .pdb files and
  without the unused ffmpeg libraries. `tools/package-release.ps1` builds that zip and `.github/workflows/release.yml`
  drafts the GitHub release from `main`; branches, CI and the whole flow are in `docs/RELEASE.md`.
- Count things with a script, not by eye, when reporting numbers (failures, mismatches, sizes).
- If the same parity mismatch survives two fix attempts, stop. Report the stage-by-stage diff
  instead of loosening thresholds.
- **Checkpoint**: at the end of every task, update `docs/PORT-STATUS.md` (status, commit hash,
  notes) and commit it with the task. When stopping early for any reason, fill in its
  "Last stop" section before you stop. Resume from the first task there that is not `done`.

## Environment

- .NET SDK 10.0.402. NuGet cache: `NUGET_PACKAGES=D:\packages\nuget`.
- **Codex sandbox limits (verified 2026-10-16):**
  - **HTTPS does not work inside the sandbox** (Schannel `SEC_E_NO_CREDENTIALS`), so NuGet cannot reach nuget.org.
    Every package in `Directory.Packages.props` is already in the cache and restores offline.
    **Do not add new packages.** If one is truly required, stop and report it.
  - Set `DOTNET_CLI_HOME` to `.dotnet-home` inside the repo (gitignored) before running `dotnet`.
    Otherwise `dotnet` tries to write `.dotnet`/`.templateengine` under the user profile and is denied.
  - When access to a path outside the workspace is denied, first try a workspace-local path
    or an env-var override. Stop only if that does not work either.
  - The App csproj sets `UsedAvaloniaProducts` empty on purpose. Otherwise Avalonia.BuildServices writes
    `%LOCALAPPDATA%\AvaloniaUI\...` and the build fails in the sandbox. Keep it that way.
  - GUI apps cannot be checked visually here. Keep logic in `TimingAnalyzer.Editor` with tests. The planner
    smoke-runs the app outside the sandbox.
- Python: `.venv\Scripts\python.exe`, with numpy, scipy and soundfile installed.
- Original exe: `G:\osu!\TimingAnlyz 0.32.4.exe`. Run it with cwd `G:\osu!`. Status lines and the timeline go to stderr, BPM/Offset to stdout.
- Parity songs: `tests/parity-songs.tsv` (machine-specific paths).
