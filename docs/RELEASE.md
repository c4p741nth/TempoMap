# Branches, CI and releases

How a change gets from a branch to a release on GitHub. The files behind it: `.github/workflows/ci.yml`,
`.github/workflows/release.yml`, `.github/dependabot.yml`, `tools/package-release.ps1`, `tools/release/`.

## The flow

```
feature branch ──PR──▶ develop ──PR──▶ main ──(release.yml)──▶ draft pre-release ──(you press Publish)──▶ release
```

1. **Work on a branch, open a pull request into `develop`.** `ci.yml` builds and tests it (Windows runner, .NET 10).
2. **Merge into `develop`.** No extra push CI runs. An open `develop` -> `main` pull request tests the updated merge result.
3. **When `develop` is ready to release, bump the version in a commit on `develop`:** `Version` (`2026.10.23.0`) and
   `InformationalVersion` (`20261023.0`) in `src/TimingAnalyzer.App/TimingAnalyzer.App.csproj`, which must say the same
   date and number (`YYYYMMDD.n`, the n-th release that day; `Version` has no leading zeros). The tag is `v` + the
   InformationalVersion. The packaging script refuses a mismatch.
4. **Open a pull request from `develop` into `main`.** CI builds, tests and, for this pull request only, builds the
   release zip too (kept as a workflow artifact for 7 days), so a packaging problem shows before the merge.
5. **Merge it.** `release.yml` runs on the push to `main`: it builds, tests, packages, attests the zip and creates a
   **draft pre-release** `vYYYYMMDD.n` with the zip, its `.sha256` and notes (what it is, the SHA-256, how to verify it,
   and the pull requests merged since the last release).
6. **Open the draft under Releases, read it, try the zip if you want to, then press Publish** (untick *pre-release*
   once it has been listened to). Nothing is public before that.

A push to `main` whose version already has a release, a draft or a tag builds nothing: the run only says
"raise Version and InformationalVersion to release again". So a docs-only merge into `main` is harmless.

## Which checks run

| Event | Build and test | Package preview |
| --- | --- | --- |
| PR into develop | Runs | Skipped |
| PR into main | Runs on the merge result | Runs after tests |
| Push to develop | No CI run | No CI run |

The old configuration also ran CI on each develop push. With a develop -> main PR open, that produced both
`build-test (push)` and `build-test (pull_request)` for the same update. The push trigger has been removed.
Keep the main PR check: GitHub checks the proposed merge with main, not just the develop branch in isolation.
See [GitHub's pull_request event documentation](https://docs.github.com/en/actions/reference/workflows-and-actions/events-that-trigger-workflows#pull_request).

Skipped jobs still appear as checks; `package (pull_request)` on a develop PR does not execute packaging.
Dependabot config validation is a separate GitHub check. The number of check rows is not the number of builds.
The separate Release workflow still validates and packages the actual main commit before creating its draft;
its artifact and attestation must describe that commit. Existing draft/version safeguards stay in place.

## What the packaging script does

`pwsh tools/package-release.ps1` (PowerShell 7; the same file runs on a clone and in Actions):

- publishes the App self-contained for win-x64 into `artifacts/` (git-ignored), building in `artifacts/build` so a
  TempoMap running from `src/.../bin` cannot hold a file the build needs;
- takes out `.pdb` and `.lib` files and the ffmpeg libraries `ppy.osu.Framework.NativeLibs` brings along (unused, LGPL;
  `av_libglesv2.dll` is ANGLE's GL library and stays);
- adds `README.txt` (from `tools/release/README.template.txt`, `{version}` filled in) and `LICENSE.txt` (the repository's
  `LICENSE`);
- checks the folder (TempoMap.exe, the BASS and ONNX Runtime libraries and `Models\beatnet.onnx` are there, nothing
  unwanted is, the exe's ProductVersion is the InformationalVersion) and fails if not;
- zips it as `TempoMap-v<version>-win-x64.zip` with a folder of that name inside, and writes `<zip>.sha256`.

Change what a release carries in the script and its README template, not by hand.

## One-time setup on GitHub

- **Branches:** create `develop` from `main`. Settings → Rules → Rulesets: for `main` and `develop` require a pull request
  before merging, require the status check **CI / build-test** (and **CI / package** for `main`), and block force
  pushes. Whoever merges can be you; the rule is there so nothing reaches `main` without CI.
- **Actions:** Settings → Actions → General → Workflow permissions can stay at *Read repository contents*;
  `release.yml` asks for what it needs in the workflow itself (`contents: write` to make the draft, and
  `id-token`/`attestations`/`artifact-metadata: write` to attest). Dependabot proposes monthly updates to the actions into
  `develop`.
- **Default branch:** `main`, so the repository page shows what was released.
- **Attestations** exist for public repositories on every plan; the workflow skips that step in a private one.
  Anyone can then check a downloaded zip with `gh attestation verify TempoMap-v<version>-win-x64.zip --repo <owner>/<repo>`.

## If something goes wrong

- **CI fails on `NU1901`-`NU1904`:** a package has a published advisory (the build treats them as errors, see
  `Directory.Build.props`); update the package in `Directory.Packages.props`.
- **The golden tests are not run in CI** (`--filter "FullyQualifiedName!~GoldenTests"`): they compare Core with stages
  dumped from the original exe by `tools/dump_golden.py` on the author's machine (`tests/golden/` is git-ignored). Run the
  full suite locally before a release (`dotnet test -c Release`), and the parity check (`tests/test_parity.py`) when Core changes.
- **The draft has the wrong content:** delete the draft (no tag was created yet), fix, and run *Release* again from the
  Actions tab (*Run workflow* on `main`), or merge the fix.
- **A published release is wrong:** do not move its tag; raise the version and release again, and delete the bad release
  (and its tag) if it should not stay.
- **A build for a quick test without the pull request:** `pwsh tools/package-release.ps1` on any clone gives the same zip.

## Notes for keeping the account safe

The repository was suspended once (2026-10-18, reason not given; docs/PHASE1-STATUS.md E34). Releases are the part most
like what GitHub filters for (a zip with an `.exe` inside), so: publish source first, keep releases as pre-releases until
they have been tried, put the SHA-256 and the attestation in every release, ship nothing decompiled and no one else's
beatmap (`tools/make_fixtures.py` makes synthetic ones), and never open a second account to host the same project: appeal
the first instead.

## Not done here

- Code signing (Windows SmartScreen may ask once for an unsigned exe).
- Pinning the actions to commit hashes (they use their major version tags; Dependabot keeps them current).
