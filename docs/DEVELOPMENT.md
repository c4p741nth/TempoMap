# Development

[Documentation index](INDEX.md) · [User guide](USER-GUIDE.md) · [CI and releases](RELEASE.md)

## Build and run

Use Windows x64 and the .NET 10 SDK (verified with 10.0.402). Packages are pinned in `Directory.Packages.props`.
From the repository root:

```powershell
$env:DOTNET_CLI_HOME = Join-Path $PWD ".dotnet-home"
dotnet restore
dotnet build -c Release --no-restore
./src/TimingAnalyzer.App/bin/Release/net10.0/win-x64/TempoMap.exe
```

The app's output folder contains its native audio/rendering libraries and `Models/beatnet.onnx`.
Keep that folder together. Imports need external `ffmpeg` and `ffprobe` on PATH or beside the executable.
See [audio import](AUDIO-IMPORT.md). Create a distributable build with [the packaging script](RELEASE.md).

On the prepared offline workstation, use the existing cache instead of contacting NuGet:

```powershell
$env:NUGET_PACKAGES = "D:/packages/nuget"
dotnet restore --source $env:NUGET_PACKAGES -p:NuGetAudit=false
```

Do not add packages for a local fix. Keep `UsedAvaloniaProducts` empty in the App project: the optional
Avalonia statistics task writes outside the workspace. Dates and numeric text use `InvariantCulture`.

## Tests

```powershell
dotnet build -c Release --no-restore
dotnet test -c Release --no-build --no-restore --filter "FullyQualifiedName!~GoldenTests"
```

CI runs the tests above. Golden stage tests need local song fixtures, so the unfiltered suite is run locally
after preparing them. Python tooling uses `requirements.txt`; this workstation has `.venv/Scripts/python.exe`.

```powershell
$songs = Import-Csv tests/parity-songs.tsv -Delimiter "`t" -Header Group, Audio
$judgement = $songs | Where-Object { $_.Audio -like "*Judgement*" } | Select-Object -First 1 -ExpandProperty Audio
$hayakore = $songs | Where-Object { $_.Audio -like "*Hayakore*" } | Select-Object -First 1 -ExpandProperty Audio
.venv/Scripts/python.exe tools/dump_golden.py $judgement tests/golden/Judgement
.venv/Scripts/python.exe tools/dump_golden.py $hayakore tests/golden/Hayakore
dotnet test -c Release --no-build --no-restore
.venv/Scripts/python.exe -m unittest discover -s tests -p test_parity.py
```

Set `TIMING_ANALYZER_GOLDEN` to an alternate directory containing `Judgement/` and `Hayakore/`.
The tests compare the mono signal, envelope, autocorrelation and timing results with the Python reference.
Generate the small committed filter fixture with `tools/dump_biquad.py`. Song paths and full golden data stay local.
Editor test beatmaps are synthetic; `tools/make_fixtures.py` regenerates them.
Manual audio, osu! and desktop checks are tracked in [the acceptance checklist](EDITOR-MANUAL-TEST.md).

## Command-line detector and parity

```powershell
$cli = "./src/TimingAnalyzer.Cli/bin/Release/net10.0/win-x64/timing-analyzer.exe"
& $cli "song.ogg"                  # detect BPM and offset
& $cli "song.ogg" 160              # keep BPM, detect offset
& $cli "song.ogg" 160 352          # supplied BPM and displayed osu! offset
.venv/Scripts/python.exe tools/parity.py --candidate $cli --songs tests/parity-songs.tsv
```

BPM `0` selects automatic detection. Exit codes: `0` for a result, `1` for analysis/decoding failure,
`2` for invalid arguments. Keep the BASS native libraries beside the CLI.

Parity compares the CLI with a local copy of `TimingAnlyz 0.32.4.exe` in auto, supplied-BPM and supplied-offset
modes. The default original path is `G:/osu!/TimingAnlyz 0.32.4.exe`; pass another as the first positional argument
when needed. `--modes auto` selects only automatic detection. Historical results: [port status](PORT-STATUS.md).

`reference/timing_analyzer.py` is the parity-verified specification. Do not edit the reference, decompiler output
or expected results to make a C# change pass. The [algorithm](ALGORITHM.md), [function map](FUNCTION-MAP.md)
and [port plan](PORT-PLAN.md) explain the reconstruction and local evidence-generation commands.

## App branding

`tools/make-icon.ps1` generates the ICO and header PNG from the five-bar mark in `Assets/icon.svg`.
The header PNG is exactly the largest ICO frame; the executable and window use that ICO.

If Explorer keeps an earlier icon after replacing a build at the same path:

```powershell
./tools/refresh-app-icon.ps1 -RebuildCache
```

This sends Windows Shell icon notifications and refreshes an open output-folder view. It does not restart Explorer.
The app is named **TempoMap** throughout the UI, executable, release templates and packaging script.

## Source layout

| Directory | Purpose |
| --- | --- |
| `src/TimingAnalyzer.App` | Avalonia desktop UI |
| `src/TimingAnalyzer.Editor` | Editing state, file handling and UI-independent tools |
| `src/TimingAnalyzer.Audio` | BASS decoding and playback |
| `src/TimingAnalyzer.Core` | Parity-protected DSP and analysis |
| `src/TimingAnalyzer.Cli` | Original detector's CLI |
| `tests` | C# tests, Python harnesses and synthetic fixtures |
| `tools` | Packaging, measurements and data-generation tools |
| `reference` | Python specification; unchanged by product fixes |

Use [TODO](TODO.md) for current work and the [documentation index](INDEX.md) for plans and measurements.
