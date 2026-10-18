# Phase 3 status

| Task | Status | Commit | Notes |
|---|---|---|---|
| P0 Mania benchmark + diagnostics | done | | `select-mania` → `tests/mania-songs.tsv`: Set C 36 (tournament / Camellia / Ludicin), D 65, E 7 (user's picks, held out). `beats`, `trace`, `unmerged`, `tracked` diagnostics. Starting table in PHASE3-PLAN. |
| P1 Ramp-capable beat tracking | done | | Joint (time, interval) tracker kept opt-in (beat-level gains did not carry to held-out red lines). The beat-level loss was fixed after tracking instead: half-tempo pass (`HalfTempoMaxBpm`, on). A dotted-beat (×1.5) pass was measured and left off. Remaining tracker limits are in "P3 final report". |
| P2 Ramp-aware segmentation | done | | `RampBridge` and the whole-BPM snap on by default; opt-in drift mode for drifting recordings. A ramp-run merge guard (keep monotone runs of red lines) was measured and not kept. Short-step and V-shaped ramps stay with the hand tools (E22). |
| P3 Tuning and report | done | | Final table below (2026-10-18), README limits refreshed. |

Status values: `todo`, `in-progress`, `done`, `blocked`.

## P1 measurements (2026-10-16)

`MultiBpmOptions.JointTracker` replaces the Ellis DP's fixed tempogram period with a joint (time, interval) state:
each beat pays a cost on its interval changing from the previous one (`TempoContinuity`), a weak cost on straying
from the tempogram period (`TempoPrior`), and a flat `JumpCost` for changes larger than `MaxStepChange`.

Tracker level, Set C tune (tuning half), aligned recall within 20 ms; "ramp" = reference beats in sections < 6 s:

| tracker | overall | ramp beats |
|---|---|---|
| windowed (Phase 2) | 0.554 | 591/1271 = 0.465 |
| joint, cont 100 | 0.493 | 0.488 |
| joint, cont 1000 | 0.549 | **675/1271 = 0.531** |
| joint, cont 3000 | **0.573** | 0.480 |
| joint, cont 10000 | 0.580 | 0.436 |

There is a trade-off rather than a clean win: loose continuity follows ramps but wanders on steady parts, tight
continuity is steadier but stops following ramps. On the user's free-tempo picks (Set E, held out) the joint tracker
clearly follows the drift better at the beat level — JAPANESE SOUL BROTHERS tracked-beat aligned F10 0.642 → 0.787,
Monochrome and Vivid 0.402 → 0.495, IRIS 0.374 → 0.438 — but My Dearest (0.525 → 0.477) and Multigenre Smackdown
(0.564 → 0.481) got worse.

End to end (final red lines), joint cont 1000 vs default:

| set | default aligned | joint aligned |
|---|---|---|
| C tune | 0.667 | 0.672 |
| D tune | 0.685 | 0.698 |
| A tune | 0.661 | 0.679 |
| C hold | 0.553 | 0.542 |
| D hold | 0.715 | 0.716 |
| A hold | 0.768 | 0.745 |
| E hold | 0.391 | 0.434 |
| B | 30/30 one red line, identical | 30/30, identical |

Gains on the tuning halves do not carry over to the held-out ones (C hold and A hold drop), so the default stays the
windowed tracker. **Why the gain is small:** on MAMBO the audio itself shows no clean periodicity in the ramp —
folding 2.4 s windows of the onset envelope, the strongest period matches the reference step in only ~4 of 13
windows (`ramp_evidence.py` in `.eval-cache/scan`), the rest lock to 1.25-beat or dotted patterns of the
syncopated drums. Neither tracker can follow a tempo the onsets do not express; a ramp red line there has to come
from continuity with the sections on either side, which is P2's job (segmentation), not the tracker's.

Both trackers find 100 % of a clean synthetic ramp (unit test `TheJointTrackerFollowsACleanTempoRamp`), so the gap
is about real, syncopated music, not the algorithm missing ramps in principle.

## P2 first sweep: merge strength (2026-10-16)

On the tuning halves, `inherit` 0.85–1.0, `penalty` 300–3000 and `snap=1` all land within ±0.02 aligned F10 of
the default on C tune and D tune (n = 20 / 32) — noise, not signal. The best on C tune, `penalty=300` (0.684 vs
0.667), was checked on the held-out halves and rejected: C hold 0.553 → 0.532, A hold 0.768 → 0.755, and **Set B
breaks** (B tune 0.933 → 0.907, one song gains a second red line). Defaults stay.

Conclusion so far: neither the tracker (P1) nor the merge knobs move complex maps much. The remaining lever is
structural — in ramps and drifting live tempo the onsets do not express the reference period (measured above), so a
red line per short step has to be inferred from continuity between the confident sections around it, or the editor
has to make hand-timing those steps fast. Next concrete step: a P2 that fits a monotone ramp (BPM interpolated
between the confident sections on each side, one red line per beat group) where a span's beats are weak, and is
judged on C/D tune with B as the guard.

## Option A, free tempo (2026-10-16): opt-in, does not help yet

`MultiBpmOptions.FreeTempo` (`src/TimingAnalyzer.Core/FreeTempo.cs`) keeps the section red lines and only *adds* a
line where tracked beats (joint tracker, continuity 1000) leave the grid gradually: each beat at most
`DriftMaxChange` (3 %) of a beat further off than the last, on the same side, for `DriftBeats` (4) beats, and the
new grid must fold `DriftFoldRatio` (1.25×) as strongly as the old one over the next 16 beats. A jump or an
off-beat flip never qualifies, so it cannot split a ramp or a section change.

Per song shape (`.eval-cache/scan/by_shape.py`; "free" = reference median section < 4 beats, "ramp" = half the
sections < 6 s), aligned F10, default → free, songs better / worse by more than 0.02:

| set / shape | n | default | free | better | worse |
|---|---|---|---|---|---|
| C free / ramp / step | 10 / 16 / 9 | 0.617 / 0.588 / 0.669 | 0.617 / 0.589 / 0.669 | 0 / 0 / 0 | 0 / 0 / 0 |
| D free / ramp / step | 17 / 13 / 34 | 0.677 / 0.610 / 0.739 | 0.678 / 0.610 / 0.728 | 0 / 0 / 0 | 0 / 0 / 1 |
| E free / ramp | 5 / 2 | 0.444 / 0.261 | 0.444 / 0.190 | 0 / 0 | 0 / 1 |
| A free / ramp / step | 12 / 9 / 18 | 0.616 / 0.641 / 0.823 | 0.617 / 0.641 / 0.823 | 0 / 0 / 0 | 0 / 0 / 0 |
| B (single BPM) | 30 | 0.944, 30/30 one line | 0.944, 30/30 one line | 0 | 0 |

**Ramp songs are not hurt** (C, D, A ramp unchanged), but it does not help free-tempo songs either: with rules
strict enough to keep Set B at one red line, almost no line qualifies. Looser rules (1.0×, 3 beats, 8 %) added
lines everywhere and cost every set 0.04–0.14 (first cut, not kept). The reason is visible on single-BPM songs: the
tracker wanders ±5–40 ms around a perfectly steady grid in busy passages (PSYQUI - Hype at 150–175 s), which looks
exactly like a slow drift, so drift alone cannot tell a live tempo from tracker noise.

**Per-beat lines** ("grouped1": a red line per tracked beat) are the upper bound of following the tracker. They win
only on JAPANESE SOUL BROTHERS (0.240 → 0.741) and IRIS (0.266 → 0.388), and lose on My Dearest (0.571 → 0.478),
Monochrome and Vivid (0.681 → 0.471) and all of C/D/A/B. Grouping 2/4/8 beats per line is worse still. So a
free-tempo mode helps one kind of song (live jazz with large, fast drift) and hurts the rest; it must stay an
explicit user choice, not an automatic decision.

Defaults are unchanged (all `multi` summaries identical to before); 151/151 tests; parity 45/45.

## P2 ramp bridge (2026-10-17, Claude, after the user reported MAMBO COMBO's ramps still missing)

**Is a ramp in the audio at all?** `timing-eval ramp-test` folds each reference ramp step on its own (best phase,
so offset conventions do not matter). On MAMBO COMBO the step's own BPM is the strongest period for 145, 140, 155
and 160 (8, 6, 4, 8 beats); the 150 step folds as 140, and the 4-beat 150 step at 166.9 s is indistinguishable.
So most steps *are* in the onsets; the windowed tracker (4 s windows) and the segmentation penalty simply cannot
resolve 3 s steps. Accents are not a usable step marker (`timing-eval anchors`): on Set C, 47 % of ramp steps
start on a peak ≥ 1.3× the typical beat, but such peaks occur 2.4 times per second inside ramps.

**`RampBridge`** (`src/TimingAnalyzer.Core/RampBridge.cs`), applied to the final red lines when `Ramps` is on: at
every tempo change A → B (≥ 2 %), 4-beat windows find where A's grid stops fitting (t1, ≥ `RampGoodRatio` of its
median window) and where B's grid starts (t2). If at least 4 beats are left between them, two models compete:
- **steps**: DP over (position, whole-number BPM), each step 4/6/8/12/16 beats starting on the previous step's
  beat, BPM monotone from A towards B, A running on for free and B running back for free, landing on B's grid;
- **continuous**: BPM linear in the beat index from A to B, one red line every 1/2/4 beats (the only free choices
  are the starting A beat and the length, so it needs little evidence).
Scores are onset strength on the predicted beats in units of a typical beat, minus `RampStepPenalty` (0.5) per
step line or `RampLinePenalty` (0.3) per continuous line. The ramp is kept only if it beats the best plain switch
(A's grid, B's grid, or A then B at any beat) by `RampMargin` (1 beat). The search starts at A's last fitting
window and may land up to one window past t2, because the ramp's first and last beats often still sit on the
neighbouring grids (found with a synthetic 150/145/140 ramp that otherwise came out as 148/146/143).

Tuned on the tuning halves only (`RampGoodRatio` 0.6 → 0.7, `RampStepPenalty` 1 → 0.5; `RampMargin` 0.5 lowered
A tune 0.666 → 0.640 through false ramps, so it stays 1). `ramp10` = share of reference beats inside sections
< 6 s with a predicted beat within 10 ms after the song's constant shift:

| split | set | aligned F10 off → on | ramp10 off → on |
|---|---|---|---|
| tune | C | 0.667 → 0.682 | 0.127 → 0.242 |
| tune | D | 0.685 → 0.688 | 0.117 → 0.161 |
| tune | A | 0.661 → 0.666 | 0.188 → 0.262 |
| tune | M (MAMBO) | 0.474 → 0.483 | 0.224 → 0.327 |
| hold | C | 0.553 → 0.559 | 0.061 → 0.154 |
| hold | D | 0.715 → 0.714 | 0.267 → 0.315 |
| hold | A | 0.768 → 0.773 | 0.113 → 0.178 |
| hold | E | 0.391 → 0.392 | 0.202 → 0.202 |
| both | B | 0.933 / 0.956 unchanged, 30/30 one red line | – |

MAMBO COMBO now: 160 → **155, 152, 146, 142, 139** → 135 between 92.6 and 104.1 s (reference 160, 150, 145, 140,
135 from 91.5 to 104.5 s), as a continuous ramp. The second ramp (145 → 150, 155 → 160 at 166.9-170 s, 4-beat
steps) is still a plain switch: its best ramp beats the switch by 0.77 beats, under the margin.

Tests: `AStepRampBetweenTwoSectionsGetsItsSteps` (synthetic 160 → 150/145/140 → 135 with weaker ramp onsets: 90 %
of ramp beats within 6 ms, worse with `Ramps = false`) and `AStraightTempoSwitchGetsNoRampLines`. 153/153 tests;
parity paths untouched (Analyzer, OnsetEnvelope, BpmDetector, OffsetDetector, Timeline unchanged; Core golden
tests green).

## P2 follow-up: Yooh - RPG (2026-10-17, user report "not 100 %")

All four local sets of Yooh - RPG share one 32-line timing (`rpg.tsv` in the scratchpad: 1633250, 1678400, 1747426,
2462688). Default result: aligned F10 **0.921**, ramp10 0.502, shift -7.6 ms (the maps sit 7.6 ms later than our
calibration). Right: 115 → 160 accelerando at 218-229 s line for line. Missed, and why:
- 116 s, 190 → 170 → 165 → 155 within 1.5 s: shorter than the minimum gap.
- 174-179 s, 150/140/130 for 2 beats each then 120 for 4 → 110: the step DP only had 4+ beat steps. A path probe
  showed the true path *was* found once 2-beat steps were allowed, but lost to a faster wrong path because every
  beat added non-negative evidence (more beats in the same time = more luck). **Fixed**: evidence is now strength
  minus the gap's mean strength (`Evidence`), so empty beats cost; this is on by default and neutral elsewhere.
- 252-255 s (160 down to 110 then a jump to 210) and 300-304 s (210 → 190 → 220): not monotone between the
  neighbouring sections, so outside the ramp model; they come out as one wrong section each (192, 211).
- 354-358 s ritardando at the very end: no section after it to bridge to.

2-beat steps (`rshort=2`) with a 3-beat minimum gap lift RPG to aligned 0.931 / ramp10 0.600 and the tuning halves
slightly (C ramp10 0.245 → 0.256, A 0.262 → 0.266), but on the held-out C half one song (Halv vs kuro - Evreka)
falls 0.64 → 0.01: its change point moves to the right place, yet its sections end up 12 ms apart in offset, which
the single-shift aligned metric punishes completely. Not clearly better on held-out data, so not enabled.

Note: the M reference (MAMBO COMBO `[Test].osu`) was re-saved by the user from our own multi-BPM output on
2026-10-17 (it now holds our 10 red lines), so M scores 1.000 against itself and is **no longer an independent
check**; keep the original timing from git history of this doc if MAMBO is to be a gate again.

Defaults after this change (151/151 → 159/159 tests): tune C 0.682 / D 0.688 / A 0.666 / B 0.933; hold C 0.559 /
D 0.715 / E 0.392 / A 0.773 / B 0.956 (B 30/30 one red line).

### Free ramps experiment (2026-10-17, after the user asked to check RPG against the osu! maps)

The osu!standard and osu!mania sets of Yooh - RPG agree line for line. Their ramps are mapper-style: steps of
**1-4 beats** (even 1.5), BPM moving by 5-20 per step, **not monotone** between the neighbouring sections
(252 s: 160 → 150, 140, 120, 110 → jump to 210; 300 s: 210 → 200, 190, 190, 200, 210 → 220). The onsets do support
the reference beats (shifted by our -7.6 ms calibration, onset strength 4-15 on the reference beats against a
mean of about 1 in these regions).

A "free" step search (any direction, each step within 15 % of the last, 1/1.5/2/3/4/6/8/12/16 beats, down to 35 %
below the slower section, and — after the first run went to 233-281 BPM on the hi-hats — never above the faster
one) was tried. On RPG it got the 300 s V shape nearly right (302228@200, 303428@209 vs 302230@200, 303430@210)
and the first two 174 s steps (174187@150, 174987@139 vs 174195@150, 174995@140), but then wandered (159, 138, 146)
and at 252 s took a 161/183/202/188/216 path through the denser texture: with 1-2 onsets per step any path through
strong onsets scores. RPG aligned 0.921 → 0.916; tuning halves neutral (C 0.682 → 0.681, A 0.666 → 0.665, D and B
unchanged). Not kept. Short-step ramps need a human placing each step on its accent; the deterministic part
(BPM of each step from the spacing of the red lines) is a tool, not a detector.

### Hand tools for short steps (E22, 2026-10-17)

The user supplied the official osu! Featured Artist timing (`Songs/Yooh_-_RPG`, same audio by SHA-256): its 32
red lines equal the Zelq, Alvearia and [Crz]xz1z1z sets exactly (the DPS set is +5 ms after 229 s), so the
reference used above is the official one. Every ramp step there is a multiple of 5 BPM and starts exactly where
the step before ends, truncated to whole ms (25 of 25 lines; rounding would miss 5).

Instead of detecting such steps, the editor now has **Snap to onsets** and **BPM from spacing** (README, docs/
PHASE1-STATUS.md E22). Measured in the real window with the RPG audio, over the six ramps (25 steps) and six sets
of placement (five with simulated ear error up to 30 ms, one exact): 132/150 step BPMs exact and on the official
chain; for the five ear-error sets 110/125, against 9/125 from the placed lines without snapping; exact lines need
no snap (25/25 alone, 22/25 snapped). Snap weighting over all six: 1 / (1 + (d/20)²) 132, 1 − (d/60)² 127,
σ 30 130, 15 127, 10 126. The steady misses
are lines with nothing under them: 357095 (onset strength 0.0 at the official line) and the 110 BPM section
start at 178775 (weak, with a stronger hit 35 ms before). Snapped RPG lines sit a median 7 ms before the
official ones; across 144 reference maps with aligned F10 ≥ 0.5 our grid is a median 2.6 ms early (quartiles
-7.7 to +2.0), within mapper offset variation, so the calibration is unchanged.

## ML beat tracker probe: Beat This! (2026-10-17, user asked whether ML is worth it)

Question: would a pretrained neural beat tracker give better multi-BPM structure than our tempogram + DP tracker?
Model: Beat This! (CPJKU, ISMIR 2024; MIT licence for code and weights), checkpoint `final0`, minimal postprocessing,
CPU torch in `.venv-ml` (675 MB; the trading-bot venv had no torch). `tools/beat_probe.py` runs it on our own
decoder's samples (`timing-eval dump-audio`), so beats share the envelope's time base: 147 audio files in 1011 s
(about 6 s per song on 8 CPU threads). Its beats come at 20 ms steps (50 frames/s); 97-98 % lie within 40 ms of an
onset peak, median lag -4 ms (quartiles -6 .. -2), so each is refined onto the envelope as our tracked beats are
(`MultiBpmOptions.ExternalBeats`, `RefineBeats`) and the rest of the pipeline is unchanged.

Beat level against the reference's main BPM (`timing-eval ext-octave`): the same on 83 of 179 songs, half tempo on
29, 2/3 or 3/4 on 16, other 51 (mostly multi-BPM songs where the median interval says little). Hence a variant
that first brings the beats to the whole-song estimate's level (`ExternalToOctave`, `ToBeatLevel`).

Aligned F10 (`timing-eval run … multi ext=1 [extoct=1] [lag=-4]`):

| set | ours | Beat This! | + lag -4 | + our beat level |
|---|---|---|---|---|
| C hold | 0.559 | 0.554 | 0.521 | 0.457 |
| C tune | 0.682 | 0.606 | 0.584 | 0.558 |
| D hold | 0.715 | 0.593 | 0.605 | 0.710 |
| D tune | 0.688 | 0.687 | 0.700 | 0.652 |
| E hold | 0.392 | 0.412 | 0.407 | 0.436 |
| A hold | 0.773 | 0.675 | 0.662 | 0.670 |
| A tune | 0.666 | 0.603 | 0.607 | 0.595 |
| B hold / tune | 0.956 / 0.933 | same | same | same |

Per song (147, Set B aside, beat-level variant): better by more than 0.05 on 13, worse on 39, within 0.05 on 95;
mean 0.685 ours, 0.627 Beat This!, and 0.703 when the better of the two is picked per song with hindsight. The big
losses are whole-grid misses (Evreka 0.641 → 0.002, Chemical Interference 0.609 → 0.013), the gains songs where our
tracker already struggles (E's 47-line collab 0.137 → 0.488, Extra Mode 0.351 → 0.665).

**Not worth it now.** A general-purpose beat tracker does not know osu! conventions (which level the mapper calls
the beat, where a red line goes), and the ms precision still has to come from our envelope; even a perfect per-song
choice between the two gains 0.018. What would be needed instead is a model trained on osu! maps themselves
(thousands in the local Songs folder): a separate project with label noise of several ms between mappers. The probe
code stays (all off by default) so a future model can be measured the same way.

## Full procedure rehearsed on Yooh - RPG (E23, 2026-10-17, user: "try it all")

Headless real window, RPG audio only, the official FA file only for scoring. Steps: Auto-time multi-BPM and apply
(16 lines); delete the 151/146/192/211 BPM lines; box-select 218-229 s and BPM from spacing (115 … 150, fixes the
auto 149); each of the five ramps placed with Seek + Insert (Ctrl+P) at official time + ear error, the next
section's start given its BPM, Snap to onsets (not the silent 357095), BPM from spacing; finally one shift of all
lines by the median difference. Two ways of giving each ramp its section start, four ear-error sets each
(±5-15, ±4-15, ±5-25 ms, exact):

| section start | BPM equal to the official (of 32) |
|---|---|
| delete the auto line, place by ear, type the BPM, snap | 28, 29, 30, 30 |
| move the auto line by whole beats (±1 beat buttons), no snap | 29, 31, 28, 31 |

31 is the ceiling: our first line is 308@190, the official's 8@190 one beat earlier on the same grid. Misses: the
quiet ending (355895, 357095 snapped onto other sounds when placed 9-14 ms off) and a line placed 25 ms off snapping
to a neighbouring hit (175852 → 122). Offsets, moved-line runs: before any shift every line is 5-16 ms early; after
shifting all lines +8 ms 12 of 31 are exact, 17 within 1 ms, 23-24 within 5 ms, max 10 ms in the clean runs. The
remaining differences are per section and fixed in the official (first section and 116 s ramp +16 ms against our
onsets, most sections +5..8, the ending −2), so ms-exact equality needs the official itself.

## Billie Jean, audio only (2026-10-18, user: "as hard as JAPANESE SOUL BROTHERS")

`E:\Music\Michael Jackson - Michael Songs From The Motion Picture - Billie Jean.ogg` (294 s, 44.1 kHz). No osu! map of
it exists locally, so there is no reference timing; it was checked against its own onsets. Single BPM: **Approximate
117.2** (status 16), and the original exe loops on "Cannot determine the accurate BPM" without an answer. Multi-BPM
(default, joint tracker, ramps) gives the same single line, 103 @ 117.213; `free=1` adds one at 272423 @ 117.763
that makes the last window worse.

**Peak check** (`.eval-cache/billie/peaks.py`): every predicted beat is matched to the strongest onset peak within
±1/6 beat; the median signed offset per 10 s window shows where a grid leaves the audio. Calibrated on the 25 Set B
songs whose single red line matches their map (aligned F10 ≥ 0.95): window medians 0.7 ms, worst window 1-18 ms,
none over 30 ms. Billie Jean's single grid: worst window **71 ms** (intro −71, −43, −51, −35, −23 ms, +27 at 90 s,
−15 at the end), 51 % of beats within 10 ms. The tracked beats and a fold of the envelope show the same slow arc:
local tempo 116.9-117.5 BPM per 20 s, no steps. So the song is a free-tempo case, but a gentle one: a tempo
spread of ±0.3 BPM, against JAPANESE SOUL BROTHERS' 124.6-222.6 (p10-p90 136.6-151.6) over 743 red lines.

A red line every N tracked beats (least-squares BPM over the group, whole ms, three decimals):

| lines | BPM range | beats ≤ 10 ms | worst 10 s window |
|---|---|---|---|
| 1 (default) | 117.213 | 0.51 | 71 ms |
| every 128 beats, 5 | 116.956-117.236 | 0.34 | 35 ms |
| every 64 beats, 9 | 116.938-117.298 | 0.49 | 38 ms |
| every 32 beats, 18 | 116.936-117.504 | 0.67 | 21 ms |
| every 16 beats, 36 | 116.401-117.832 | 0.73 | 13 ms |
| every 4 beats, 144 (`grouped4`) | – | 0.98 | 2.3 ms |

The grouped rows are fitted on the same envelope the check reads, so they are an upper bound, not an independent
score. Greedy "fewest lines within tol ms of the tracked beats" does not separate drift from tracker jitter: steady
Set B songs need 9-83 lines at 20 ms too, so only the peak check is used above.

## Ranked multi-BPM picks (2026-10-18, user: "what can we learn from these, and can we put a red line every bar?")

29 ranked sets the user named (Frums ×5, ReeK ×4, Camellia ×8, Gardens ×3, USAO ×4, Gram vs Camellia ×2, KASAI,
steganography, Galaxy Collapse, Kai Dan, QZKago), 24 distinct audio files: `tests/ranked-picks.tsv` (Set F, all
held out; 7 were already in mania/multibpm sets). Map structure from `tools/scan/ranked_timing.py`, scores from
`timing-eval run tests/ranked-picks.tsv … ` and `beats`.

**How ranked mappers time these** (one consensus timing per set, 1926 red lines):
- 73 % of red lines are whole BPMs, 4 % halves, 23 % other. The "other" lines are almost all in two maps that put a
  line on every beat with a free BPM (Multigenre Smackdown mania 238 of 262, goTe EX 50 of 123).
- A red line covers a median 3.5 bars. Only 2 of 24 average one bar or less per line (Multigenre Smackdown taiko
  0.4, HALL 0.9). The share of song time inside sections of ≤ 1 bar has a median of 11 %; only BLACK RAY (63 %) and
  Ragnarok (49 %) are above 40 %. Lines are mostly per-phrase tempo changes or ramps with steps of 1-4 beats,
  not a line on every bar.
- Ramps are written as small whole or half BPM steps: USAO - Dynamite 200 → 200.5 → 201 … 216 every 2 beats, Ragnarok
  106 → 115 → 130 … 234 in 33 lines.
- Two ranked sets of the same audio usually agree on the grid but not on the offset: Dynamite 5-10 ms apart, Rafflesia
  4 ms (same audio), Electromagnetic 6 ms (different audio). Ragnarok's two sets have different audio (hashes differ)
  and are 95 ms apart, a decoder or encoder delay, not a timing difference. So ±5-10 ms is mapper variation.
- 1627304 (ReeK, taiko) has two red lines at the same time (200911). `BeatGrid.Sort` throws, so the eval skips it
  and **the editor's open fails** ("Open failed: Red lines must have distinct times.") on a ranked map.

**How we do** (aligned F10, 23 songs; 1627304 failed as above):

| mode | mean aligned F10 | best on |
|---|---|---|
| single BPM (`baseline`) | 0.349 | 3 |
| multi-BPM (default) | **0.561** | 19 |
| line every 4 tracked beats (`grouped4`) | 0.403 | 0 |
| line every tracked beat (`grouped1`) | 0.431 | 4 |

("best on" counts ties for each mode: QZKago and Ragnarok ×2 tie single and multi at one red line.) Compared with
multi-BPM by more than 0.02, a line every bar is better on 2 songs and worse on 19; a line every beat 3 and 19. Picking the better of the two per song with
hindsight only reaches 0.570. Why: the tracked beats themselves are wrong on most of these songs. Only 6 of 23 have
rec20 ≥ 0.7. 12 have the right beat spacing but sit a quarter to three quarters of a beat off (qrt20 - rec20 ≥ 0.3:
syncopated kicks and offbeat hats), and 8 run at half or double tempo on ≥ 25 % of the intervals. A red line
per bar only copies those errors onto the map, and adds a chance of a phase jump at every line. Where the tracker is
right (Dynamite, Electromagnetic, QZKago), multi-BPM already scores 0.95-0.99, apart from offset.

Multi-BPM's section BPMs within 3 % of the map's: 81 sections, median error 0.006 BPM, 37 equal to the map's BPM as
written, 47 after rounding to a whole BPM. So rounding to a whole BPM when within about 0.02 is a cheap win
(73 % of ranked lines are whole). Not done yet: it needs the same tune/hold check as any default change.

## Whole-BPM snap (2026-10-18, item 2 of the user's list): on by default

`MultiBpmOptions.WholeBpmDriftMs = 8`: on songs with more than one red line, each final line's BPM becomes the
nearest whole BPM when that changes its section's length by ≤ 8 ms over all its beats; the snapped grid keeps the
section's middle beat in place (a first cut that kept the line's time moved every later beat and cost Lorna Shore -
Of the Abyss raw F10 0.569 → 0.220 at an unchanged aligned score). Swept 3 / 5 / 8 / 10 / 12 / 15 / 20 ms on the
tuning halves; 8-10 were best, 12+ snapped real fractional BPMs and lost on A tune (0.651 at 12, 0.646 at 20).

Final red lines with exactly the map's BPM (of those within 3 % of it): mania sets C/D/E 128 → **161** of 219,
Set A 35 → 43 of 60, Set F 36 → 42 of 77.

| set | aligned off → on | F10 off → on |
|---|---|---|
| C tune / hold | 0.682 → 0.683 / 0.559 → 0.558 | 0.624 → 0.623 / 0.455 → 0.455 |
| D tune / hold | 0.688 → 0.691 / 0.715 → 0.715 | 0.551 → 0.548 / 0.456 → 0.469 |
| A tune / hold | 0.666 → 0.665 / 0.773 → 0.773 | 0.576 → 0.578 / 0.603 → 0.603 |
| E hold | 0.392 → 0.409 | 0.377 → 0.395 |
| F hold | 0.544 → 0.545 | 0.372 → 0.385 |
| B tune / hold | identical row by row, 30/30 one red line | identical |

Test `NearWholeBpmsSnapAroundTheSectionMiddle`; parity 45/45; Editor 159/159, Core 15/15.

## Half-tempo correction (2026-10-18, item 3 "fix the beat tracker"): on by default

**Where the final red lines lose** (`.eval-cache/ranked/loss.py`: each reference beat classified by the BPM ratio
of our section at that time and its phase after the song's shift). C/D tune, 48 806 reference beats, before:
on the beat 46.0 %, right BPM but off-beat 3.7 %, right BPM 10 ms-⅛ beat off 2.3 %, **beat level wrong 38.2 %**,
other tempo 9.8 %. Of the beat-level errors 80 % are **half tempo** (A tune the same, 80 %), at a median map BPM
of 224 against 200 where we match: the original's > 210 BPM halving anchors the tracker an octave low. The
phase (¼-¾ beat) losses that `timing-eval beats` shows for the tracked beats mostly do not survive into the
final lines; the octave does.

**Rule** (`MultiBpmOptions.HalfTempoMaxBpm = 130`, `HalfTempoRatio = 0.5`, `MultiBpmAnalyzer.DoubleHalfTempo`):
on songs with more than one red line, a final section of ≥ 8 beats at ≤ 130 BPM is doubled when the onsets folded
at its beat length peak at the half beat at least half as high as on the beat. The line keeps its time. Features
measured on 704 final sections (`octave_features.py`); the rule was picked on the tuning halves (C/D/A tune: 70
half-tempo sections fixed, 5 right ones broken) and checked on hold (29 fixed, 3 broken). A guard that skips a
section whose neighbour has a similar BPM (to protect ramps into a slow part) blocked more fixes than breaks.

| set | aligned before → after | F10 before → after | ramp10 before → after |
|---|---|---|---|
| C tune / hold | 0.683 → **0.740** / 0.558 → **0.647** | 0.623 → 0.685 / 0.455 → 0.509 | 0.243 → 0.242 / 0.155 → 0.130 |
| D tune / hold | 0.691 → 0.733 / 0.715 → 0.727 | 0.548 → 0.584 / 0.469 → 0.477 | 0.187 → 0.239 / 0.315 → 0.324 |
| A tune / hold | 0.665 → 0.737 / 0.773 → 0.787 | 0.578 → 0.633 / 0.603 → 0.606 | 0.262 → 0.286 / 0.178 → 0.190 |
| E hold | 0.409 → **0.489** | 0.395 → 0.407 | 0.204 → 0.229 |
| F hold (ranked picks) | 0.545 → 0.557 | 0.385 → 0.398 | 0.176 → 0.181 |
| B tune / hold | identical row by row, 30/30 one red line | identical | – |

Per song (> 0.01 aligned): 36 better, 6 worse. The worst are real slow sections with busy half beats: Camellia -
Electromagnetic Stealth Girl's last 110 BPM section (both sets, 0.981 → 0.861 and −0.114), Hommarju - Sahara
(−0.030), Frums - goTe EX (−0.042). After the change the C/D tune loss is 65.9 % on the beat, beat level 14.3 %,
other tempo 9.8 %, off-beat 6.9 %: the octave is no longer the largest loss on most songs; "other tempo" is next.

Test `AHalfTempoSectionIsDoubledAndARealSlowOneIsNot`; parity 45/45; MAMBO M 1.000; Editor 159/159, Core 16/16.

## Drift mode (2026-10-18, item 4 "a red line every bar from the drums"): opt-in editor button

`DriftLines.Fit` (`src/TimingAnalyzer.Core/DriftLines.cs`): one continuous grid through the tracked beats whose beat
length is constant within each group of N beats (beat k of group g at t0 + Σ N·L_h + (k − gN)·L_g), solved by least
squares with a smoothness term between neighbouring groups (4 per ms²); unsupported beats are left out and beats
more than max(20 ms, 3 × median residual) off are dropped and the fit repeated. Continuity means a red line never
jumps phase, the failure of the earlier "line every N tracked beats" (`grouped4`). Editor: **Auto-time drift** with
4/8/16/32 beats (default 16), reviewed and applied like a multi-BPM proposal.

Billie Jean (no map; peak check against its onsets, calibrated as above):

| lines | beats ≤ 10 ms | worst 10 s window |
|---|---|---|
| 1 (auto-time) | 0.51 | 71 ms |
| drift, 32 beats (18 lines) | 0.80 | 9.4 ms |
| drift, 16 beats (36 lines) | 0.85 | 4.3 ms |
| drift, 8 beats (72 lines) | 0.92 | 2.7 ms |
| drift, 4 beats (144 lines) | 0.96 | 2.3 ms |
| earlier `bars16` (independent fits) | 0.73 | 12.8 ms |

Against maps (aligned F10): Set E multi-BPM 0.489, drift 4 / 8 / 16 / 32 beats 0.447 / 0.424 / 0.374 / 0.290 — better
than multi-BPM only on JAPANESE SOUL BROTHERS (0.240 → 0.620 at 4 beats), Multigenre Smackdown (0.460 → 0.611) and
My Dearest (0.571 → 0.660 at 8). Set B with drift 8: 0.728 / 0.786 against 0.933 / 0.956 for one red line. So it
stays a user choice for drifting recordings, never automatic. Tests `DriftLinesFollowASlowlyDriftingTempo`
(synthetic ±0.3 BPM arc: worst beat < 3 ms with 16-beat lines, one straight line > 20 ms; every eighth beat flipped
half a beat still < 4 ms) and `DriftModePassesTheChosenBeatsAndAppliesLikeMultiBpm`. Editor 160/160, Core 17/17;
the window opens and closes (smoke run).

## Drift mode, every beat (2026-10-18, user: "a red line wherever the drums hit", Billie Jean for osu!mania)

The user's report: with drift lines each red line sits on its first drum hit and the next hits wander off. Measured
on the audio alone (`.eval-cache/billie/drums/`), with two drum-hit finders that do not use the tracker: the steepest
2 ms rise of the 1-4 kHz band (`hits.py`), and normalised cross-correlation with kick and snare templates learned
from the song (`tmpl2.py`, 150 Hz-10 kHz). The templates show beat 1/3 = kick, 2/4 = snare through the whole song
(the other drum's template fits better on 3 of 574 beats, all in the fade-out).

**The drums are played, not a slow drift.** Beat-to-beat intervals: kick → snare median 512.0 ms, snare → kick 513.6,
both sd 6 ms, p5-p95 501-523 ms (114.8-119.7 BPM per beat). No line spanning several beats can follow that.
Fitting the drum hits themselves with the drift model (oracle, `resid2.py`/`consensus.py`, 522 beats where the
template and the tracked beat agree within 3 ms):

| lines | median | p90 | max | ≤ 5 ms |
|---|---|---|---|---|
| drift 16 (editor output) | 4.6 ms | 11.2 | 31.7 | 55 % |
| drift 4 (editor output) | 3.2 | 7.7 | 17.7 | 70 % |
| best possible, every 16 beats | 4.6 | 11.2 | 25.1 | 55 % |
| best possible, every 4 / 2 beats | 2.7 / 1.7 | 6.6 / 4.3 | 12.7 / 9.2 | 79 / 94 % |

So the drift fit was already at the model's limit; only a line per beat removes the error. The tracked beats
already sit on the hits: tracked beat + 23.1 ms vs template hit, |difference| median 0.64 ms. On the five ranked
single-BPM .ogg maps of Set B the same detector gives hit − tracked beat +21.5..+23.2 ms and tracked beat − map line
−5.8..+5.2 ms (`calib3.py`), so Billie Jean's offset needs no correction; ranked maps put the red line 16-26 ms
before the decoded transient (osu!stable convention; lazer adds 15 ms on Windows to match it).

`DriftLines.EveryBeat` (`beatsPerLine` 1, editor choice **1 beat**): a red line on every tracked beat within the drop
limit of the final 4-beat drift grid (`RepairBeats`, judged again after the fit so a beat dropped while an outlier
still pulled the fit comes back); a dropped beat (no onset, an off-beat flip) is spanned by one line from the kept
beat before. Times are whole ms with the BPM of the whole-ms spacing, so each beat ends exactly on the next red line,
and a line with the same spacing as the one before is left out (lossless). Billie Jean: 553 red lines for 574
beats (`drift1.txt`). Against the drum hits (global offset removed, `evaldrift1.py`):

| reference | lines | median | p90 | max | ≤ 5 ms |
|---|---|---|---|---|---|
| template and attack detector agree (296 beats) | drift 16 | 4.6 ms | 12.6 | 31.9 | 53 % |
| | drift 4 | 3.6 | 8.4 | 18.1 | 66 % |
| | **drift 1** | **0.5** | **1.4** | **6.2** (fade-out) | **100 %** |
| template only, ncc ≥ 0.6 (494 beats) | drift 1 | 0.7 | 2.2 | 23.5 | 96 % |

Unresolved: 19 beats (3.3 %) where the template's drum hit is 10-23 ms after the line, never before it. At those
beats (`look.py`) there are two attacks 16-20 ms apart: something on top first (1-4 kHz and above), then the drum
whose low band rises with the template hit. The tracker takes the first, the template the second; which one a player
hears as the beat needs listening: 00:38:855, 01:04:491, 01:20:414, 01:28:602, 01:43:468, 01:49:617, 01:50:642,
02:01:906, 02:25:477, 03:31:002, 03:37:147, 03:47:875, 03:53:530, 03:54:533, 03:59:126, 04:18:090, 04:19:114,
04:20:137, 04:22:185 (osu! time of the line). Off-beat notes still interpolate between two beat lines:
the hi-hat eighths wander too (80 clear ones, spread median 4.5 ms, p90 15 ms), which only hand placement fixes.
Each red line would also start a bar line in osu!: the proposal sets "omit first barline" off the 4/4 bars counted
from its first line, and **Bar line here** recounts from another line (E25 in `PHASE1-STATUS.md`). The per-beat BPM
(111.7-124.2) would also move the osu!mania scroll speed by up to ±5 %; **Copy normalized** writes the green lines
that hold it at the main BPM (116.959 for these lines, SV 0.942-1.047).

Opt-in only, defaults unchanged: per-beat lines lost on most maps of Sets B-E ("Per-beat lines" above), and drift
4/16 output on Billie Jean is byte-identical to before. Tests `PerBeatDriftLinesLandOnEveryPlayedBeat` and
`DriftOnEveryBeatAsksForOneBeatAndSummarisesHundredsOfLines`; the multi-BPM description gives the BPM range instead of
listing more than 12 sections. Core 18/18, Editor 161/161; the analyzer and CLI path are untouched (parity not rerun).

## Live-drum check for a one-button auto-time (2026-10-18): measured, not enough

User: "can one Auto button decide which red lines are best?" Multi-BPM already gives the single-BPM answer on steady
songs (Set B identical, 30/30 one line), so only drift 1 beat would need a decision. Probe
(`.eval-cache/live-probe/probe.py`): around each tracked beat the steepest 1-4 kHz rise, and over three consecutive
beats the wobble |a_k − (a_{k−1} + a_{k+1}) / 2|, median per song, counting songs with at least 20 measurable triples.
Billie Jean 5.10 ms (138 triples). Set B steady: 14 of 30 measurable, 0.10-0.80 ms for 12 of them but 2.45 (gingus -
homeswitcher) and 2.70 (USAO - Try Again). Set A tune multi-BPM: 9 of 20 measurable, 0.10-1.62. The songs whose maps
put a red line on every beat (JAPANESE SOUL BROTHERS, Franz Ferdinand - This Fffire, Between the Buried and Me - The
Parallax II, My Dearest, Innocent, Paganini): **0 of 6** measurable (at most 19 triples; dense rock mixes, piano,
violin). So the check separates Billie Jean but cannot be measured on the live songs it is for, and two steady songs
come close; drift stays a user choice.

## Dotted-beat pass (2026-10-18): measured, off

`ThirdBeatMaxBpm` (0 = off): speed a section up by 1.5 when its folded onsets peak at both ⅓ and ⅔ of the beat at
least 0.6 as high as on the beat (the music's beat is two thirds of ours). On the 704 section features it looked
clean (C/D/A tune 4 fixed / 1 broken, hold 9 / 0), but end to end C tune aligned fell 0.740 → 0.725 (cap 260; 0.718
at 300): M1LLI0N PP −0.175 and DiSANiMATE −0.143, where triplet fills sit inside right sections; only XNOR XNOR
XNOR gained (+0.058). Left in Core as an off option.

## Ramp-run merge guard (2026-10-18): measured, removed

M1LLI0N PP's 152 → 175 BPM build-up (247-277 s, 6 map lines) is found before the merge passes (unmerged 152, 160,
166, 172, then 196) and then collapsed into one 156 BPM line. Not merging inside monotone runs of ≥ 3-5 red lines
(steps 1.5-15 %, sections < 30 s), in `MergeExplained` and also in `PreferGlobal`, with or without the run's two
end boundaries: best case C tune 0.740 → 0.744, but A tune 0.737 → 0.704-0.715 and D tune 0.733 → 0.716-0.721, and
on M1LLI0N PP itself the kept lines broke other sections (0.695 → 0.519-0.687). Removed from the code.

Off-beat sections (right BPM, ¼-¾ beat off the map) are few: 19 of 164 C/D tune sections with the map's BPM,
4 of 53 on A. A re-fold of their phase would put 5 of the 19 on the map's beats and none of A's; on most of them
the onsets fold more strongly at our phase than at the map's (HyuN - Duplicity 7.07 vs 5.61), so the audio itself
favours our choice. Nothing to gain from the onsets there.

## P3 final report (2026-10-18)

Every default as of `e390c8c`/`732cd06` (multi-BPM with ramp bridge, whole-BPM snap, half-tempo pass; drift mode
off). `baseline` = the original detector, one red line. "Phase 2+P2" = today's code with the two 2026-10-18 passes
off (`whole=0 hmax=0`), i.e. the state before this session. Mean over songs, failed songs count 0.

| set | n | baseline F10 / aligned | Phase 2 + P2 F10 / aligned | **final** F10 / aligned | final median error | ramp10 final |
|---|---|---|---|---|---|---|
| C tune (complex mania) | 20 | 0.340 / 0.304 | 0.624 / 0.682 | **0.685 / 0.740** | 6.7 ms | 0.242 |
| C hold | 16 | 0.247 / 0.281 | 0.455 / 0.559 | **0.509 / 0.647** | 11.0 ms | 0.130 |
| D tune (other mania) | 32 | 0.449 / 0.519 | 0.551 / 0.688 | **0.584 / 0.733** | 5.9 ms | 0.239 |
| D hold | 33 | 0.348 / 0.560 | 0.456 / 0.715 | **0.477 / 0.727** | 10.9 ms | 0.324 |
| E hold (user's hard picks) | 7 | 0.241 / 0.298 | 0.377 / 0.392 | **0.407 / 0.489** | 26.8 ms | 0.229 |
| F hold (user's ranked picks) | 24 | 0.247 / 0.341 | 0.372 / 0.544 | **0.398 / 0.557** | 38.8 ms | 0.181 |
| A tune (multi-BPM, all modes) | 20 | 0.366 / 0.397 | 0.576 / 0.666 | **0.633 / 0.737** | 7.7 ms | 0.286 |
| A hold | 20 | 0.524 / 0.611 | 0.603 / 0.773 | **0.606 / 0.787** | 5.6 ms | 0.190 |
| B tune / hold (single BPM) | 15 / 15 | 0.755 / 0.933, 0.747 / 0.956 | identical | **identical, 30/30 one red line** | 6.6 / 5.2 ms | – |
| M (MAMBO, self-referenced) | 1 | 0.471 / 0.462 | 1.000 | 1.000 (not independent) | – | – |

The two D hold failures are Raphiiel - RoquiRa (both sets over 10 minutes, the FFT cap of the original), not
detection errors. Parity 45/45 throughout; Set B is identical row by row.

**What is left, by measured size** (C/D tune, share of reference beats, final): on the beat 65.9 %, beat level
14.3 % (double tempo now the largest part: sections at 240-360 BPM that the map writes at half; 3/2 and 3/4
switches; neither has a rule that survives the held-out halves), other tempo 9.8 % (a quarter of it tempo changes
we do not mark, median 10.7 % change over 6.8 s; the rest long ramps merged into one line, as M1LLI0N PP, or
non-musical gimmick timing), off-beat 6.9 % (the onsets favour our phase), drift 3 %. These are the limits of an
onset-only method with these heuristics; the README's multi-BPM limits say the same in short.
