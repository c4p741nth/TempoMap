# TempoMap

A timing editor for osu! with BPM/offset detection, a waveform and editable red/green timing points.

## Get started

1. Download a Windows x64 build from [Releases](https://github.com/c4p741nth/TempoMap/releases), extract the folder and run **TempoMap.exe**.
2. Open a song or `.osu` file, then use **Auto-time** to propose timing. Type a BPM to keep it fixed and fit only the offset.
3. Review the grid, click **Apply**, and copy the timing points into osu! or save the `.osu` file.

Release builds include .NET. Audio import also needs `ffmpeg` and `ffprobe` on PATH or beside the app;
original audio files are preserved. Review [audio import and clipboard behavior](docs/AUDIO-IMPORT.md) before pasting timing into osu!.

## Features

- Detect steady BPM, tempo changes or drifting tempo; refine a selected range or section.
- Edit timing points with keyboard shortcuts, waveform navigation and a metronome.
- Add SV and Kiai points; keep manual SV values or enable automatic BPM compensation.
- Adjust BPM with repeatable multipliers, fit offsets and fill tempo ramps.
- Switch between light, dark and system themes.

## Documentation

| Looking for | Read |
| --- | --- |
| Everyday editing and shortcuts | [User guide](docs/USER-GUIDE.md) |
| Tool explanations and examples in Thai | [คู่มือเครื่องมือภาษาไทย](docs/TOOLS-TH.md) — also **F2** in the app |
| Build, tests and command-line tools | [Development](docs/DEVELOPMENT.md) |
| Branches, CI and release packaging | [Release workflow](docs/RELEASE.md) |
| Known limitations and planned work | [To do](docs/TODO.md) |
| Algorithms, measurements and project history | [Documentation index](docs/INDEX.md) |

## Build from source

Windows x64 and the **.NET 10 SDK** are required.

```powershell
dotnet restore
dotnet build src/TimingAnalyzer.App -c Release --no-restore
./src/TimingAnalyzer.App/bin/Release/net10.0/win-x64/TempoMap.exe
```

Offline setup and test commands are in the [development guide](docs/DEVELOPMENT.md).

## Credits and license

Created by **C4P741N**. [MIT license](LICENSE). Audio uses **BASS** by un4seen, free for non-commercial use.
The original steady-tempo detector was rebuilt from TimingAnlyz 0.32.4; its specification and port history
are linked from the [documentation index](docs/INDEX.md).
