# Phase 4 plan: a beat model trained on osu! maps (option A, step 2)

User decision 2026-10-18 after E36 ("1, then 3"; "yes, go on"): train a neural network on the local ranked and loved
maps and wire it into the editor through ONNX Runtime, shipped only if no evaluation set gets worse (the rule the user
set with My Dearest as the example). Approved: PyTorch + CUDA in a separate venv on G: (D: has 18 GB free), a
spectrogram cache on G:, and the ONNX Runtime NuGet package in the editor (an exception to AGENTS.md's no-new-packages
rule, asked and granted).

## Why a beat model, and what it must learn

- The whole-song pipeline already has ms precision from the onset envelope; its losses are structural: the wrong
  beat level (octave, E36: 1 020 of 1 073 training songs are one tempo, so the octave is the mapper's convention) and
  red lines in the wrong place on songs that change tempo.
- Beat This! (Phase 3 probe) was a general beat tracker: it did not know which level mappers call the beat and lost
  0.06 aligned on average. This model is trained on the mappers' grids themselves, so the beat level it outputs is
  theirs.
- Output per 10 ms frame (100 fps): probability of a mapper's beat and of a mapper's downbeat (red-line grid, every
  beat; downbeat = bar start by the map's meter and omit-first-barline flags). The existing tracker's DP and the
  segmentation into red lines stay; the model's beat activation replaces (or is mixed into) the onset envelope that
  drives the tempo and beat tracking, and ms placement is still refined on the onset envelope (`RefineBeats`).

## Data

- Songs: `tools/octave_pairs.py` (one consensus map per ranked/approved/loved folder from osu!.db, 1 264 songs,
  evaluation folders left out). Labels from the map's red lines only (green lines carry no timing).
- Input: log-mel spectrogram, 22 050 Hz mono from our own decoder (`timing-eval dump-audio`, the same samples the
  envelope uses), 128 mel bands, 10 ms hop. Cached as float16 .npy on G:\tempomap-ml\cache.
- Targets: beat frames of the map's grid (every beat from the first red line to the last note / audio end), widened to
  ±1 frame with weight 0.5 (beat-tracking practice for 10 ms frames); downbeats the same.
- Split: song-grouped 90 / 10 for model selection; the evaluation sets (A-F, M) stay untouched until the end.

## Model

- Small enough for CPU inference in the editor (a 5-minute song in a few seconds): conv front end over the mel bands,
  then a temporal conv network (dilated 1-D convs, receptive field ~10 s) - the TCN family that beat tracking uses,
  about 0.5-1 M parameters. Trained on 12 s random crops, Adam, BCE with positive weighting.
- Export to ONNX (opset 17), run in C# with Microsoft.ML.OnnxRuntime (CPU).

## Integration and the ship rule

1. `timing-eval` gains an algorithm that runs the multi-BPM pipeline with the model's beat activation (read from a
   per-song cache written by the Python side), so it is scored exactly like today's algorithm on A-F and M.
2. Ship only if every set's aligned F10 is equal or better than the baseline in docs/PHASE1-STATUS.md E36 (A hold
   0.787, A tune 0.737, B hold 0.956, B tune 0.933, C hold 0.647, C tune 0.740, D hold 0.727, D tune 0.733,
   E hold 0.489, F hold 0.557; set M, MAMBO COMBO, was removed on 2026-10-18 because its reference had been re-saved
   from our own output) and single-BPM songs (set B) still come out as one red line.
3. Then the editor: ONNX Runtime in App (model file beside the exe), mel spectrogram in C# matching the Python one
   bit for bit (tested against cached Python output), the activation fed to Core.

## Steps (checkpoint: docs/PHASE4-STATUS.md)

| Step | What |
|---|---|
| N1 | Data: decode, mel cache, beat/downbeat targets for the training songs; sanity plots/counts |
| N2 | Model + training loop on GPU; frame-level F-measure on the 10 % validation songs |
| N3 | Activation export for the evaluation songs; timing-eval algorithm with the model activation; per-set scores |
| N4 | Ship decision by the rule; if it passes, ONNX export, C# mel + ONNX Runtime in the editor, tests, headless run |
