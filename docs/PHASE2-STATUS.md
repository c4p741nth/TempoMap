# Phase 2 status

| Task | Status | Commit | Notes |
|---|---|---|---|
| M0 Eval set + metric + baseline | done | a5d7ee9 | `GridAgreement` (Editor, 5 tests): recall/precision within 10 ms (F10), median/P90, half-tempo recall, and **aligned F10** after removing one constant shift (offset conventions differ between maps and decoders: several Set B maps sit a constant 10–32 ms from the original's calibration). `tools/TimingAnalyzer.Eval` (`timing-eval`): `select` scanned 6 064 maps / 1 502 sets → `tests/multibpm-songs.tsv` (seed 20261016): Set A 40 multi-BPM sets (plausible 60–400 BPM, sections ≥ 2 beats, consensus timing of submitted sets), Set B 30 single-BPM sets, Set M = MAMBO COMBO with the user's test map; A and B split tune/hold. `run` caches onset envelopes in `.eval-cache/` (gitignored); a full run takes ~30 s. Baseline below. |
| M1 Beat tracker | done | 95ed15e | `MultiBpmAnalyzer.TrackBeats` (`src/TimingAnalyzer.Core/MultiBpm.cs`): 5 ms frames, windowed autocorrelation tempogram (8 s / 0.5 s hop, 60-400 BPM) with a Viterbi path octave-anchored to the original detector's whole-song BPM, then an Ellis-style DP beat tracker whose transition cost follows the local period, each beat refined to the onset peak (+/-25 ms, parabolic). 3 unit tests. |
| M2 Segmentation into red lines | in-progress | 95ed15e | `SectionBuilder` (`src/TimingAnalyzer.Core/MultiBpmSections.cs`): optimal piecewise-constant DP segmentation of inter-beat intervals (outlier second pass), long sections get BPM/phase by epoch folding and short ones by regression continuing the previous grid, then `MergeExplained` drops red lines a neighbour's grid already explains and `PreferGlobal` hands mostly-single-tempo songs back to the original's precise grid. 3 of 4 gates pass; see below. |
| M3 Editor integration | done | | `MultiTimingProposal` + `MultiTimingDetection` (Editor, independent of Core as `TimingDetection` is), `EditorViewModel.AnalyzeMultiAsync` / `ApplyMultiProposal` (off the UI thread via `Task.Run`; Apply is one `Edits.Replace`, so one undo step, green lines untouched; the BPM box still forces one BPM and refuses), `CoreTiming.DetectMulti` reusing the cached envelope, **Auto-time multi-BPM** / **Apply multi-BPM...** buttons plus a per-section list in `MainWindow.axaml`. 7 view-model tests with a fake detector; app smoke-tested outside the sandbox (window opens, responds). README + `docs/EDITOR-MANUAL-TEST.md` updated. |
| M4 Tuning and report | done | | Tuning done on the tuning half only (`WindowMs` 4000, `HopMs` 250; see the gate notes); final table above; failure cases measured and documented below, and the limits are in the README. |

Status values: `todo`, `in-progress`, `done`, `blocked`.

## Results

Mean over songs (failed songs count 0). `aligned` = F10 after removing one constant shift.

| Algorithm | A tune F10 / aligned | A hold F10 / aligned | B tune F10 / aligned | B hold F10 / aligned | M aligned | One red line on B |
|---|---|---|---|---|---|---|
| baseline (original detector, one red line) | 0.366 / 0.397 | 0.524 / 0.611 | 0.755 / 0.933 | 0.747 / 0.956 | 0.479 | 30/30 |
| multi (M1+M2, window 8 s / hop 0.5 s) | 0.533 / 0.629 | 0.589 / 0.717 | 0.755 / 0.933 | 0.747 / 0.956 | 0.477 | 30/30 |
| **multi (tuned: window 4 s / hop 0.25 s)** | **0.572 / 0.661** | **0.600 / 0.768** | 0.755 / 0.933 | 0.747 / 0.956 | 0.474 | 30/30 |

Baseline notes: two Set A songs get no timing at all (Kamah, Ungrateful: detector status −1). Several Set A
songs with ≥ 2 BPMs still score ≥ 0.9 because one tempo dominates them.

## M2 gate status (measured 2026-10-16, `timing-eval run tests/multibpm-songs.tsv multi`)

- **Set B one red line: PASS.** 30/30, and both B summaries are *identical* to baseline to three decimals
  (`PreferGlobal` returns the original's grid whenever it explains the song), so the regression guard holds.
- **Set A held-out clearly above baseline: PASS.** With the tuned defaults A hold aligned **0.768 vs 0.611**
  (+0.157), median |error| **5.9 ms vs 9.7 ms**. A tune aligned 0.661 vs 0.397. The 2 baseline failures on
  A tune (Kamah, Ungrateful, detector status -1) now produce timing.
- **Tuning (part of M4) done on the tuning half only**, as the plan requires: `WindowMs` 8000 -> **4000** and
  `HopMs` 500 -> **250** won on A tune (aligned 0.661 vs 0.629) and carried over to the held-out half
  (0.768 vs 0.717). Also tried and rejected on A tune: hop 125 (0.632), window 6000 (0.641), window 3000
  (0.629), `change=200` (0.651). Set B stays identical to baseline under the new defaults.
- **MAMBO boundaries: FAIL (structural, not a tuning miss).** The gate wants changes near 91.5 s, the ramp
  steps, 104.5 s and ~170 s. With the merge passes disabled (`inherit=1.5 penalty=100`) the tracker *does*
  find 91.48 / 140.4 / 152.4 / 169.3 s - i.e. the boundaries exist in the beat track and the merge passes
  are not what loses them - but it jumps 157 -> 138.7 BPM at 93.7 s, **skipping the 150/145/140/135 ramp
  steps**, and adds spurious lines in steady parts (25.2 / 36.1 / 66.4 / 189.5 s). Swept `penalty`
  (1000/300/100), `inherit` (0.85/0.98/1.5), `window` (8000/4000/2500) and `change` (400/80/20): none
  recovers the ramp. `OctaveRange` is not the limit either - 1.6 admits 100-256 BPM, covering all ramp
  tempos. This matches PHASE2-PLAN's own prediction that 3 s ramp steps "defeat folding ... they need
  beat-level tracking, not window folding": each step is ~8 beats, too few for a tempogram window to
  resolve before the next step. Fixing it needs a different mechanism (per-beat tempo from inter-onset
  intervals, or a ramp model in the segmentation), which is new design work, not a knob.
- **The MAMBO "~24 ms phase error" is not ours - the reference map is wrong (investigated 2026-10-16).**
  `shift -22.4` looked like a fold bug, but the **baseline shows the same -23.5 ms** on this song, so no new
  code causes it. Three measurements settle it:
  1. **Calibration is right in general.** Baseline `shift` over all 69 eval songs: median **-1.2 ms**,
     53/69 within +/-10 ms. MAMBO is a per-song outlier, not a systematic 28.7 ms error.
  2. **The reference's first red line is untimeable.** `[Test].osu` starts at **0 ms with exactly 375 ms
     (160.000 BPM)** - round numbers - but the audio is **digital silence until 616 ms** (first sample above
     1 % of peak at 616.3 ms; first note attack 623.1 ms). There is no audible beat at 0 ms to time against,
     so that line was typed, not measured.
  3. **Against the audio, our grid wins.** Spectral-flux onsets (1 ms hop) over the steady first section
     (0.6-91.5 s), 220 strong onsets, median phase to the nearest beat: **ours +1.5 ms, the reference
     +16.5 ms**. The user's map is ~15 ms late; our red line is on the music.
  Consequence: **F10 against this reference understates MAMBO**, which is why aligned F10 (0.477) far exceeds
  raw F10 (0.417). Do not "fix" the fold to chase it. If MAMBO is to stay a gate, its reference needs
  re-timing first; `aligned` is the honest number for it meanwhile.

Parity after M1+M2: **45/45** (`tools/parity.py --songs tests/parity-songs.tsv`); full suite 142/142 green.

## Last stop

- When: 2026-10-16
- Who (executor/model): Claude Opus 5 (resumed after Codex hit its usage limit mid-M1/M2)
- Stopped because: M1, M3 and M4 are done; M2 meets 3 of its 4 gates. The open item is tempo-ramp recall
  (M2's MAMBO gate), which is a design limit rather than a tuning miss - see the M4 report above. Its phase
  "error" was investigated and belongs to the reference map, not to our fold. Committing the verified work
  rather than leaving it in the tree, per AGENTS.md.
- Working tree state: all of the above committed; no push. Resume at tempo-ramp tracking (M2's MAMBO gate) if Phase 2 is to continue.

## M4: final report and failure cases (measured 2026-10-16)

Final numbers are the **tuned `multi`** row in the Results table. Headline: Set A held-out aligned F10
**0.768 vs the 0.611 baseline**, median |error| **5.9 ms vs 9.7 ms**, and **Set B is bit-identical to the
baseline** (30/30 one red line), so single-BPM songs are provably not regressed.

### The dominant failure mode: tempo ramps

Counting the reference maps by shape (a section under 6 s from the next = ramp-like):

| Reference shape | songs | mean ref red lines | mean predicted | mean F10 | mean aligned |
|---|---|---|---|---|---|
| step-like (few long sections) | 18 | 4.4 | 1.8 | 0.629 | 0.705 |
| ramp-like (many short sections) | 22 | 15.7 | 3.1 | 0.551 | 0.678 |

**22 of 40 Set A songs are ramp-like**, so this is the majority of the multi-BPM set, not a corner case.
The system is *systematically conservative*: **39 of 40 Set A songs predict fewer red lines than the
reference and none predicts more.** Example (Hommarju - Sahara, 24 reference lines, 3 predicted): the
opening 95 BPM section is found correctly, then a 95 -> 100 -> 110 -> ... -> 145 BPM ramp in ~1 s steps is
collapsed into one section. MAMBO COMBO fails the same way.

Cause, as PHASE2-PLAN predicted: a tempogram window needs several seconds of steady tempo, but a ramp step
lasts ~1-3 s (~8 beats), so the window never sees a stable period before the next step. Sweeping
`penalty`, `inherit`, `window` and `change` does not recover it, and `OctaveRange` is not the constraint.
Fixing ramps needs a different mechanism (per-beat tempo from inter-onset intervals, or an explicit ramp
model in the segmentation), which is new design work rather than tuning.

### Other failure cases

- **Offset convention, not metre.** Several songs score far better aligned than raw, e.g. USAO - Extra Mode
  (F10 0.351 / aligned 0.976, shift -4.1 ms) and DragonForce - Through the Fire and Flames (0.547 / 0.993,
  shift -5.0 ms). The grid is right; the map sits a few ms from our calibration. The baseline shift over all
  69 songs is median -1.2 ms with 53/69 within +/-10 ms, so the calibration itself is sound.
- **Mistimed references.** MAMBO COMBO's own reference is ~16 ms late against its audio (see the gate notes
  above); raw F10 understates such songs and `aligned` is the honest figure.
- **Intros and breaks.** Sections with too few supported beats keep the previous grid by design
  (`WeakSupport`), which is right for breaks but hides a real change that happens inside one.
- **Half/double tempo.** Caught by the octave-tolerant aligned score rather than raw F10; the octave anchor
  from the original detector keeps whole-song metre stable.

### Honest summary

Phase 2 does what M1-M3 promised for songs whose tempo changes in **sustained** sections, and it does not
regress single-BPM songs at all. It does **not** solve tempo ramps, which are more than half of the
multi-BPM evaluation set, and it under-predicts red lines in general. The editor exposes the feature with
that limitation documented, and manual per-section auto-timing (Phase 1.3) remains the way to time ramps.
