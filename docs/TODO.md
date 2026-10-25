# To do (all phases)

One list of what is left, collected from the status files. Details and measurements stay in those files; this
page only says what is open and where to read about it. Updated 2026-10-18 (Phase 3 closed).

## Done on 2026-10-18 (user's items 1-4)

- [x] 1. Maps with two red lines at the same time open (first one wins, as osu!) — E24, `PHASE1-STATUS.md`.
- [x] 2. Whole-BPM snap of final multi-BPM lines (8 ms), on by default — `PHASE3-STATUS.md` "Whole-BPM snap".
- [x] 3. Beat level: half-tempo sections doubled from their folded onsets, on by default — "Half-tempo correction".
- [x] 4. Opt-in **Auto-time drift** (a red line every 4/8/16/32 beats on one continuous grid) — "Drift mode".
- [x] 5. Drift **1 beat**: a red line on every drum hit for live drums (Billie Jean for osu!mania: 0.5 ms median,
  6 ms worst on the hits both drum finders agree on, against 4.6 / 32 ms with 16 beats) — "Drift mode, every beat".
- [x] 6. Bar lines every 4 beats on per-beat red lines (**Bar line here** moves them), **Copy normalized** (red and
  green lines with osu!mania scroll speed held at osu!'s main BPM), and applied red lines keep the hitsound samples
  and volume — E25 in `PHASE1-STATUS.md`.

## Port (`PORT-STATUS.md`)

Nothing open. T1-T6 done, parity 45/45 (rechecked 2026-10-18).

## Phase 1 editor (`PHASE1-STATUS.md`, `EDITOR-MANUAL-TEST.md`)

- [ ] **Run the manual checklist** in `EDITOR-MANUAL-TEST.md` (116 unchecked items): audio/click sync at 1.0 and
  0.75, drag feel, volume response, follow, save into osu! and reopen in the osu! editor, and the new Phase 3
  section (duplicate red lines, drift mode on Billie Jean). Only a person at the speakers can do these.
- [ ] Listen for crackles with the unbuffered output (E18); fallback is a small buffer.
- [x] Synthetic playback comparison recorded: Tempomap at offset 1000/120 BPM stays within 0.05 ms; osu! controls
  show delay and timing steps. At the user's request, the existing Copy/Ctrl+C now subtracts 33 ms externally;
  internal paste/Save retain source times. No further capture is required for this request.
  See `OSU-AUDIO-TIMING.md` (E45). Real-song listening/paste checks remain in the manual checklist.
- [x] Clear analysis inputs on open/drop; live full-audio BPM normalization with x1 main sections; duplicate red/green
  paste with overlap markers and undo; 1/64 shifts; automatic adjacent Ogg q6 conversion (E46, `AUDIO-IMPORT.md`).
- [x] Point-edit workflow: Ctrl+P focuses BPM, Ctrl+G adds green everywhere, tab-aware +/Insert, Enter/blur saves,
  fixed +/minus/move buttons, dedicated Auto-time result row and seek start/end controls (E48).
- [ ] Before a portable release, decide how to supply/licence external ffmpeg/ffprobe tools; current imports use PATH
  or executables beside Tempomap. No third-party binary was bundled in this task.

## Auto-time on songs like ReCreate of the Red (2026-10-23, E43-E44 in `PHASE1-STATUS.md`)

- [ ] **Octave of a high one-tempo answer the model cannot settle.** recreate.ogg: the detector says 286.0, the user's 143.
  The beat head's alternate-beat strength is 0.50 there; the only clear cases (0.00-0.09: Rebellious exorcist, Dusk,
  Ragnarok) are halved by themselves, but Setsuna Trip (290, the map keeps 290) measures 0.37 and VIVIDVELOCITY (256, map
  at 128) 0.53, so no threshold above 0.2 passes "equal or better on every set". The status line hints (below 0.6 from 240
  BPM) and ÷2 then Fit offset, or typing 143 in the BPM box, gives the user's lines.
- [ ] **A short tempo section** (the 18 beats of 180 BPM at 49 108-55 108 ms, quiet, between two 143 BPM parts) is not
  found by Normal; Shift+drag over it and Auto-time range gives 180 @ 49 092 (49 108 with the edge lag).
- [ ] Phase check by ear of the beat-head phase on the 38 evaluation songs it made worse (M1LLI0N PP 0.951 → 0.579,
  IRIS 0.752 → 0.592): the sets improved (every set equal or better), single songs did not all.

## Phase 2 multi-BPM (`PHASE2-STATUS.md`)

- [x] M2 gate "MAMBO boundaries": the M reference was re-saved in osu! from our own output on 2026-10-17, so the
  original timing is gone; set M was removed from `tests/multibpm-songs.tsv` on 2026-10-18 (user's decision).

## Phase 3 complex multi-BPM (`PHASE3-PLAN.md`, `PHASE3-STATUS.md`)

P0-P3 done (2026-10-18). Final table and the measured remaining losses are in `PHASE3-STATUS.md` "P3 final
report". No open task; possible future work, each needing a new idea rather than tuning:

- [ ] Double-tempo sections (240-360 BPM written at half by the map) and 3/2, 3/4 metre switches: no rule found that
  survives the held-out halves.
- [ ] Long ramps merged into one line (M1LLI0N PP 152 → 175): a ramp-run merge guard was tried and lost elsewhere.
- [ ] Ramps with 1-4 beat steps, V-shaped ramps, an end ritardando: hand tools (E22) cover them.
- [ ] Half-tempo rule's known misses: real slow sections with busy half beats (Camellia - Electromagnetic Stealth
  Girl's 110 BPM ending, Hommarju - Sahara, Frums - goTe EX).
- [ ] A model trained on osu! maps themselves (the Beat This! probe showed a general tracker is not enough): a
  separate project.

Billie Jean has no reference map (the user is mapping it in osu! from the editor's output); drift mode is checked
against its own drum hits.

## Ideas measured and not kept (do not redo without a new reason)

Joint tracker on by default, merge-strength sweep, free tempo (option A), per-beat red lines by default (kept only as
the opt-in drift 1 beat), 2-beat ramp steps,
free (non-monotone) ramps, Beat This! ML tracker, independent per-N-beat fits (`grouped4`), whole-BPM snap above
10 ms, neighbour guard on the half-tempo rule, dotted-beat (×1.5) pass, ramp-run merge guard, live-drum wobble check for
an automatic drift 1 beat. Numbers for each are in `PHASE3-STATUS.md`.

Auto-time range over a tempo ramp (2026-10-21, E41 in `PHASE1-STATUS.md`, Moonlit Occultation 206 → 240): the whole
song's BPM as the range's octave anchor, 10 s of context each side, both, and context only when the range has no
exact BPM all lower some evaluation set (5-9 of 16 below); kept off as `MultiBpmOptions.AnchorBpm` and
`RangeTiming.Analyze(contextMs)`. Whole-song Normal times that ramp within 5 ms on its clear beats. Still open there:
the beat model halves the beat inside the ramp (in-between beats at 0.30-0.49 probability).

Re-analyzing every whole-song section of 20 s or more on its own slice after Auto-time (2026-10-18, E33 follow-up):
held-out aligned F10 unchanged on A, B, C, D and E hold (± 0.002); on Ill it turns 189.3 into 142 but keeps the false
ramp after it, because the whole-song section boundaries are already wrong there. Auto-time range by hand covers it.
