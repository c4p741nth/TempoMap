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

- [ ] **Run the manual checklist** in `EDITOR-MANUAL-TEST.md` (67 unchecked items): audio/click sync at 1.0 and
  0.75, drag feel, volume response, follow, save into osu! and reopen in the osu! editor, and the new Phase 3
  section (duplicate red lines, drift mode on Billie Jean). Only a person at the speakers can do these.
- [ ] Listen for crackles with the unbuffered output (E18); fallback is a small buffer.

## Phase 2 multi-BPM (`PHASE2-STATUS.md`)

- [ ] M2 gate "MAMBO boundaries" is recorded as a structural FAIL of Phase 2. Phase 3's ramp bridge covers it,
  but the M reference was re-saved from our own output on 2026-10-17, so MAMBO is no longer an independent check.
  Restore the original test map from git history if it should be a gate again.

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
