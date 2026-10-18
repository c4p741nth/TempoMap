# Phase 3 plan: complex multi-BPM timing, osu!mania first

Goal: time songs with many tempo changes and ramps — Camellia, Ludicin, osu!mania tournament pools (Mania World
Cup and others) — from audio alone. Phase 2's analyzer handles songs with a few long sections but collapses ramps
(`docs/PHASE2-STATUS.md`, M4). The user plays mostly osu!mania, so mania maps are the benchmark.

Status lives in [`PHASE3-STATUS.md`](PHASE3-STATUS.md). Phase 1 and 2 rules still apply: parity-locked Core paths
do not change behaviour, Set B (single BPM) must stay one red line and no worse than the baseline, and tuning uses
the tuning halves only.

## Benchmark: osu!mania sets C and D (`tests/mania-songs.tsv`)

`timing-eval select-mania <Songs> tests/mania-songs.tsv` scans every submitted set and keeps those whose
**osu!mania** consensus timing has ≥ 2 BPMs and is musically plausible (Phase 2's filter: 60–400 BPM, sections ≥ 2
beats). That filter is essential on mania: of 573 multi-BPM mania difficulties, 327 (152 sets) are SV/scroll
gimmicks (0.1 BPM, 275 855 BPM, hundreds of red lines) that no audio analysis should reproduce.

- **Set C (complex):** tournament pools (tags/source/version mention MWC, world cup, tournament, qualifiers,
  grand finals, RO16…), Camellia and Ludicin — 36 sets, median 9 distinct BPMs. Includes M1LLI0N PP (27 red lines,
  125–234 BPM), Ludicin - Bismuth and Everlasting Eternity, B L A C K - R A Y, TremENDouS, SUPER MWC KART.
- **Set D (other multi-BPM mania):** 65 sets.
- **Set E (user's hard picks, held out, never tuned on):** supercell - My Dearest (old recording, tempo drifts
  161–175 BPM), Frums - Thinker Walker Dreamer Scatter, T-SQUARE - JAPANESE SOUL BROTHERS (Cut & Live; live jazz,
  400 red lines, ~one per beat), penoreri - Monochrome and Vivid, Rite of Ascendancy, ReeK - Multigenre
  Smackdown, IRIS. All but one fail the plausibility filter — their maps put a red line on (nearly) every beat to
  follow a drifting live tempo, or have sub-2-beat steps — which is exactly why they are interesting.
- Split tune/hold **by song**, so several sets of one song (M1LLI0N PP has three) stay in one half.

Sets A, B and M (`tests/multibpm-songs.tsv`) stay as regression guards.

## Starting point (measured 2026-10-16)

| Algorithm | C tune F10 / aligned | C hold F10 / aligned | D tune | D hold |
|---|---|---|---|---|
| baseline (one red line) | 0.340 / 0.304 | 0.247 / 0.281 | 0.449 / 0.519 | 0.348 / 0.560 |
| Phase 2 multi | 0.612 / 0.667 | 0.449 / 0.553 | 0.549 / 0.685 | 0.457 / 0.715 |

Set E (7 songs, held out): baseline mean F10 0.241 / aligned 0.298 (JAPANESE SOUL BROTHERS gets no timing);
Phase 2 multi 0.377 / 0.391. My Dearest and Multigenre Smackdown come out as **one** red line today.

**Free-tempo songs need a different answer.** When the reference has a red line per beat (live jazz, an old
recording), no "few sections" timing can match it; the target there is a red line wherever the drift exceeds
osu!'s tolerance — beat-level tempo following, the same capability P1 needs for ramps.

Stage diagnostics (`timing-eval beats`, `run unmerged`, `run tracked`) on Set A and C:

- **The tracker itself is the main loss on ramps.** Only 43 % of reference beats inside ramp steps (sections
  < 6 s) have a tracked beat within 20 ms on Set C (50 % on Set A). Traces through Sahara and MAMBO COMBO show
  why: the tracker follows a ramp for a few steps, then locks to a tempo **faster than the music**
  (e.g. 300–350 ms intervals where the reference is 414–444 ms) or drifts a quarter beat off, because its
  period comes from a smoothed tempogram path that cannot change fast enough.
- **Octave / metre switches:** on 13 of 36 Set C songs more than 30 % of tracked intervals are half or double
  the reference period.
- **Merging is not the main loss.** With the merge passes off ("unmerged") every set scores *worse*
  (C hold aligned 0.553 → 0.538 tune / 0.576 hold mix), so the merges remove more wrong lines than right ones.
- **Even a perfect segmenter could not fix it:** using every tracked beat as its own red line ("tracked")
  scores below the final answer, so the beats themselves must get better first.

## Tasks

| # | Task | Done when |
|---|---|---|
| P0 | **Mania benchmark + diagnostics.** `select-mania`, sets C/D, `beats`/`trace`/`unmerged`/`tracked`. | Committed; starting table above. |
| P1 | **Ramp-capable beat tracking.** Replace the per-window tempogram path with tempo that can change every few beats: re-estimate the period from the tracked inter-beat intervals (local, robust) and re-run the DP, or track beats in a joint (period, phase) state space so a ramp is a path, not a jump. Keep the octave anchor. | On Set C tune, ramp-step tracker recall (20 ms, aligned) clearly above 43 %, and overall tracker recall up, without Set B regressing. |
| P2 | **Ramp-aware segmentation.** Short sections (2–8 beats) are allowed when their beats fit a clean grid; consecutive short sections with monotone BPM are kept rather than merged. Snap to whole BPM where it costs < 1 ms. | C hold aligned F10 above the Phase 2 multi row; Set B unchanged; MAMBO ramp steps found. |
| P3 | **Tune and report** on C/D tune halves; report hold halves and A/B/M guards. | Final table in PHASE3-STATUS; README limits updated. |

## Out of scope

SV / scroll-speed gimmick red lines (the filter above excludes them on purpose), green lines, time-signature
changes, songs over 10 minutes.
