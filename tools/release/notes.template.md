**Tempomap {tag}** - timing editor for osu! by C4P741N. Windows x64, self-contained: unzip, run `Tempomap.exe`; no .NET install is needed.

This is a **pre-release** until it has been listened to; the changes since the last release are listed below.

- SHA-256 of the zip: `{sha256}`
- Built by GitHub Actions from commit {commit} with `tools/package-release.ps1` (the same script anyone can run on a clone: `pwsh tools/package-release.ps1`).
- Check that the zip is the one this repository built: `gh attestation verify Tempomap-{tag}-win-x64.zip --repo {repo}`.
- The program is not code-signed, so Windows SmartScreen may ask once; the source is in this repository (MIT). Audio goes through BASS by un4seen (free for non-commercial use).
