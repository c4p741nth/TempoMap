# Phase 2 plan: multi-BPM auto-timing

Goal: from audio alone, propose **all red lines** of a song whose tempo changes, at osu! precision (whole-ms
offsets, BPM to 3 decimals). Single-BPM songs must still come out as **one** red line, no worse than today.

Status lives in [`PHASE2-STATUS.md`](PHASE2-STATUS.md). Phase 1 rules still apply: the parity-locked Core
paths (`OnsetEnvelope`, `BpmDetector`, `OffsetDetector`, `Timeline`, `Analyzer`) do not change behaviour
(Core tests and `tools/parity.py` stay green); new algorithms are new files.

## What we know (Phase 1 measurements, MAMBO COMBO)

- The onset envelope `D` (1 sample/ms) and folding are accurate where the tempo is steady: sections ≥ 10 s
  matched the user's hand-made map within 2–3 ms in three of five sections.
- The original BPM detector finds one tempo for the whole song. Its timeline `#` flags show *where* the
  grid stops fitting, but not the new tempo.
- Short steps (3 s ramps of 150/145/140 BPM) defeat folding: 3 s windows locked ¼ beat off. They need
  beat-level tracking, not window folding.
- Some passages lock ½ or ¼ beat off at any window length, so phase must be decided from strong beats
  across neighbouring context, not per window in isolation.

## Evaluation first

Every algorithm change is judged on fixed song sets with one metric, so tuning is measured, not guessed.

- **Reference timing** comes from the user's osu! `Songs` folder (6 064 maps, 1 502 sets). Many
  multi-BPM-looking maps are mania charts that abuse red lines for scroll gimmicks (0.1 or 275 854 BPM),
  so the selector keeps only *musically plausible* timing: every red line 60–400 BPM, sections ≥ 2 beats,
  one map per set (the timing most difficulties share). Paths are machine-local, like
  `tests/parity-songs.tsv`.
- **Set A (multi-BPM):** sets with ≥ 2 distinct BPMs, plus MAMBO COMBO with the user's test map.
- **Set B (single-BPM):** a fixed, seeded sample of one-red-line sets — the regression guard.
- **Metric — grid agreement:** expand the reference red lines into beats over the audio; for each beat,
  the distance to the nearest beat of the predicted timing. Per song: median |error|, P90, share within
  5 / 10 / 20 ms, red-line count. Also an octave-tolerant share (half/double tempo counted) to tell
  "wrong metre" from "wrong phase". Summaries per set; Set A is split into a tuning half and a held-out
  half so tuning cannot overfit.
- **Conventions:** a beat at envelope index `j` is osu! time `j − 28.7 ms` (`OffsetLatency 6.2 +
  OffsetDisplay 22.5`), the calibration the original offset uses and the one that matched the user's map.

## Tasks

| # | Task | Done when |
|---|---|---|
| M0 | **Eval set + metric + baseline.** `tools/TimingAnalyzer.Eval` (console): `select` writes `tests/multibpm-songs.tsv` (Set A/B, seeded, deterministic) from the Songs folder; `run <algorithm>` decodes each song, runs the algorithm, scores grid agreement and prints per-song rows and set summaries. Baseline = today's single-BPM `Analyzer` (one red line). | Sets written and committed; baseline table recorded in PHASE2-STATUS; metric unit-tested (a timing scored against itself = 0 ms; a +5 ms shift = 5 ms; half tempo caught by the octave-tolerant share). |
| M1 | **Beat tracker.** Local tempo from a sliding-window autocorrelation of `D` (tempogram, 60–400 BPM with octave preference matching the original's > 210 BPM halving), then a dynamic-programming beat tracker (Ellis-style) whose transition cost follows the local tempo, so it can follow steps and ramps. Each beat refined to the local onset peak (±25 ms, parabolic, as the timeline does). | On Set A the tracked beats agree with reference beats (within 20 ms) better than the baseline grid; single-BPM songs keep a steady period. |
| M2 | **Segmentation into red lines.** Optimal piecewise-linear fit of beat times (dynamic programming over beats, cost = squared residual + penalty per red line, ≥ 4 beats per section) → sections of constant BPM. Per section: BPM to 3 decimals, snap to a "round" BPM (integer, .5) when that costs < 1 ms RMS; phase refined by folding the section's slice of `D`; offsets to whole ms; merge neighbours with equal BPM and continuous phase. | Set B: ≥ 95 % of songs give exactly one red line and grid agreement no worse than baseline. Set A held-out: median share within 10 ms clearly above baseline. MAMBO: changes found near 91.5 s, the ramp steps, 104.5 s and ~170 s. |
| M3 | **Editor integration.** Auto-time song proposes multi-BPM timing when the song has tempo changes (the BPM box still forces one locked BPM); the proposal lists sections; Apply replaces red lines in one undo step (green lines kept). Long analyses stay off the UI thread. | View-model tests with a fake detector; app smoke test; README/checklist. |
| M4 | **Tuning and report.** Tune penalties/thresholds on the tuning half only; report both halves; document failure cases (rubato, half-time, intros without beats). | Final table in PHASE2-STATUS; known limits in README. |

## Out of scope (Phase 2)

Downbeat/measure detection (red lines land on beats; metre stays 4/4 unless the reference says
otherwise), time-signature changes, green-line (SV) generation, songs over 10 minutes.
