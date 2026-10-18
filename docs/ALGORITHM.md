# TimingAnlyz 0.32.4: recovered algorithm

This is a specification recovered from the shipped binary by static analysis (REA 6.5.0 + Ghidra 12.1.4).
Nothing here was observed at runtime. Addresses are virtual addresses at image base `0x400000`.
Each stage notes how certain it is:

- **observed**: read directly from decompiled code, disassembly, or raw file bytes.
- **inferred**: an interpretation of observed code, such as the purpose or name of a helper.
- **unknown**: not established.

## Target identity

| Field | Value |
|---|---|
| File | `TimingAnlyz 0.32.4.exe` (161 792 bytes) |
| SHA-256 | `0077c48973701b4dbc0799ad99c20b009f8e3ae0539fb724215df6894b71608b` |
| Format | PE32, x86, native C++ console app, MSVC 2010 (static CRT, SSE2) |
| PDB path | `D:\Documents\Visual Studio 2010\Projects\Timing Analyzer\Release\TimingAnlyz.pdb` (not available) |
| Version string | `v0.32.4 alpha` |
| Imports | `BASS.dll` (decode only), `COMDLG32.GetOpenFileNameW`, `KERNEL32` |
| REA evidence | overview `ev_1c3204519d70f30853c40a88b69a711c41103b0268e12ec7cb4cb9edbee75f45` |

## Pipeline at a glance

```
file ─BASS decode (float)─► mono 44.1 kHz
   └► 5 octave band-pass filters ─► square ─► 14 Hz envelope ─► Π(1+e_b) ─► ln
      └► 300 Hz low-pass ─► decimate to 1 kHz (1 sample = 1 ms)
         └► forward/backward 31 Hz smoothing ─► D[i] = B[i+1] − F[i−1]   (onset strength)
              ├─► autocorrelation (FFT) ─► harmonic peak fit ─► BPM
              ├─► fold D modulo beat period ─► argmax ─► offset
              └─► per-10 s beat statistics ─► timeline flags  # + - .
```

Because the envelope runs at 1 kHz, every index into it is a time in **milliseconds**. Every
autocorrelation lag is a period in milliseconds.

---

## 1. Entry and UI: `wmain` @ `0x4037a0` (observed)

| argc | Behaviour |
|---|---|
| 1 | `GetOpenFileNameW`: filter `Audio Files (*.mp3;*.ogg)` / `All Files (*.*)`, initial dir `Songs` (relative, so it opens osu!'s `Songs` folder when the exe sits in the osu! directory), flags `0x1804` = FILEMUSTEXIST, PATHMUSTEXIST, HIDEREADONLY. Then runs in **interactive mode**. |
| 2 | `TimingAnlyz <file>`: interactive mode. |
| 3 | `TimingAnlyz <file> <bpm>`: runs once. BPM `0` means auto. The offset is always auto. |
| 4 | `TimingAnlyz <file> <bpm> <offset>`: runs once with both supplied. |
| >4 | Prints usage. |

Flow:

1. `init_fft_tables()`
2. `load_audio_bass(path)`: on failure, prints `ERROR: Cannot read audio file.`
3. If `samples / rate > 600` s: `ERROR: Cannot process songs longer than 10 minutes.`
4. If `|rate − 44100| > 0.5`: prints a warning. The filters are designed for 44.1 kHz only.
5. `env = compute_onset_envelope(samples, rate)`, then the raw samples are freed.
6. Loop. Interactive mode repeats this forever, prompting `Input BPM` and `Input offset` each time. An empty answer means auto.
   - If BPM < 1e-30: `detect_bpm(env)`.
     - `< 0`: `ERROR: Cannot determine the timing…`
     - `>= 16`: `Approximate BPM: %8.1f`, and offset/timeline are skipped.
     - Otherwise: continue.
   - If the offset is unset (sentinel `-10000`, tested `< -0.001`): `detect_offset(env, bpm)`.
   - `flags = build_timeline(env, bpm, offset_int)`. If `flags != 0`, prints the INFO lines and the timeline.
   - Prints `BPM: %8.3f` and `Offset: %8.0f` of `offset_int − 22.5`.

### Offset conventions (observed)

- Internal offset `offset_int` = displayed osu! offset + **22.5 ms**.
- Auto-detected: `offset_int = argmax_fold − 6.2`, so **displayed = argmax_fold − 28.7 ms**.
- A user-supplied offset `O` is folded to the first beat at or after 30 ms:
  ```
  O' = O + 22.5
  offset_int = O' − floor((O' − 30) · bpm / 60000) · (60000 / bpm)     # ∈ [30, 30+P)
  ```
- The timeline adds `+6.2` back, so it expects onset peaks at `displayed + 28.7 ms`.
  Treat 22.5 and 6.2 as calibration for decoder and filter latency. (The split between the two is inferred.)

## 2. Audio load: `load_audio_bass` @ `0x401c60` (observed)

```
BASS_Init(device=0 /*no sound*/, 44100, 0, 0, 0)
h = BASS_StreamCreateFile(FALSE, path, 0, 0, 0x80220100)
    # BASS_UNICODE | BASS_STREAM_DECODE | BASS_STREAM_PRESCAN | BASS_SAMPLE_FLOAT
BASS_ChannelGetInfo(h) -> freq, chans
bytes = BASS_ChannelGetLength(h, BASS_POS_BYTE)
BASS_ChannelGetData(h, buf, bytes | BASS_DATA_FLOAT)
mono[i] = Σ_c buf[i*chans + c]          # channels SUMMED, not averaged
```

**Decoder parity (verified by running both).** On MP3s **without** a Xing/Info gapless header,
BASS outputs 529 samples (the MP3 decoder delay) earlier than libsndfile/mpg123. The Python
reference drops the first 529 samples of those files to match the original. This correction is
specific to libsndfile/mpg123; a rebuild using BASS must not apply it. OGG files, and MP3s that
have the header, already line up. Any other decoder you use needs the same check.

The C# decoder uses BASS 2.4.17 x64 without a delay correction. Its auto BPM, printed offset,
and timeline match BASS 2.4.15 x86 in the original on Judgement (OGG) and all three MP3s without
a Xing/Info header in the parity set (Hayakore, lapix, Jamie), verified on 2026-10-16.

## 3. Onset envelope: `compute_onset_envelope` @ `0x401e80` (observed; coefficients in `reverse/coefficients.json`)

All filters are cascades of Direct-Form-II biquads (`biquad_cascade` @ `0x401580`). Each section
has 5 floats `n1 n2 d1 d2 g`:

```
w[n] = g·x[n] − d1·w[n−1] − d2·w[n−2]
y[n] = w[n] + n1·w[n−1] + n2·w[n−2]
```

(While filtering, the routine switches the FPU to 24-bit precision.)

Band bank @ `0x4193f0`. Band `b` uses sections `[T[b], T[b+1])`, where `T = {0, 2, 6, 10, 14, 16}`:

| Band | Sections | Type | −3 dB @ 44.1 kHz |
|---|---|---|---|
| 0 | 0–1 | 4th-order low-pass (`n = 2, 1`) | < 300 Hz |
| 1 | 2–5 | 8th-order band-pass (`n = 0, −1`) | 300–600 Hz |
| 2 | 6–9 | 8th-order band-pass | 600–1200 Hz |
| 3 | 10–13 | 8th-order band-pass | 1200–2400 Hz |
| 4 | 14–15 | 4th-order high-pass (`n = −2, 1`) | > 2400 Hz |

```
acc[i] = 1
for b in 0..4:
    y = biquad_cascade(copy(mono), bank[T[b]:T[b+1]])
    e = y²
    e = biquad(e, SMOOTH)          # @0x419530: g=0.048, d1=−1.948, d2=0.9481 (two real poles ≈0.998/0.950;
                                   #   DC gain ≈480; −3 dB ≈14 Hz)
    acc *= (1 + e)
L = ln(acc)                        # = Σ_b ln(1 + e_b); CRT log @0x413958 (natural log, verified by its ln2/series constants)
L = biquad_cascade(L, bank[0:2])   # reuses band-0 low-pass as anti-alias (~300 Hz)
X = resample_nearest(L, 1000/rate) # @0x401b30: X[i] = L[round(i·rate/1000)], len = ceil(len·1000/rate)
F = biquad(copy(X), POST)          # @0x419544: g=0.2, d1=−1.4, d2=0.48 (poles 0.8, 0.6; DC 2.5; −3 dB ≈31 Hz @1 kHz)
B = reverse(biquad(reverse(X), POST))
D[0] = B[1];  D[n−1] = −F[n−2]
D[i] = B[i+1] − F[i−1]            # future-smoothed minus past-smoothed log-energy
```

`D` is the onset-strength signal used by every later stage. It is signed, and it is not rectified.

## 4. BPM: `detect_bpm` @ `0x402280` (observed)

Helpers:

- `autocorrelate_fft` @ `0x4017a0`: zero-pad to `2^ceil(log2(len + lags − 0.5))`, run an FFT, take `|X|²`, run the same FFT again, take the real part, scale by `1/N`, and keep `lags` values. This is Wiener–Khinchin.
- The FFT @ `0x4010b0` is an iterative radix-2 FFT driven by global tables. `init_fft_tables` @ `0x401000` fills twiddles `e^{−2πik/2²⁰}` (`5.992112452678286e-06 = 2π/2²⁰`) and a 2²⁰ bit-reversal table. The maximum FFT is 2²⁰ points, which is the real reason for the 10-minute cap.
- `interp(y, i)` throughout means parabolic peak interpolation: `i + 0.5·(y[i+1] − y[i−1]) / ((y[i] − y[i−1]) + (y[i] − y[i+1]))`.

```
n    = len(D);  NL = floor(n/2)
A    = autocorrelate(D, NL)
maxL = min(NL, 2000)                               # candidate search ≤ 2000 ms
peaks = [L for L in 2..maxL−3 if A[L] > 0 and
         A[L−1] < A[L] > A[L+1] and A[L−2] < A[L−1] and A[L+2] < A[L+1]]

# Stage 1: harmonic candidates
cands = []; best = 0; pick = None
for L in peaks (ascending), while L ≤ 999:
    t = L; k = 1; Σkk = Σkx = 0; hits = 0; hsum = 0
    while t < maxL − 10:
        q = first peak (searching forward from L's index) with q ≥ t − 10
        if q is None or q > t + 10: break
        x = interp(A, q); hits += 1; hsum += A[q]
        Σkk += k²; Σkx += k·x; slope = Σkx/Σkk
        t = round(x + slope); k += 1
    if hits > 0 and hsum/hits > 0.7·A[L]:
        if hsum/hits > 1.2·best: pick = len(cands)
        best = max(best, hsum/hits)
        cands.append((slope, hsum/hits))
if best ≤ 0: return −1

P = cands[pick].period
if P < 285.714 ms:                                 # > 210 BPM: prefer a multiple (halve tempo)
    for j > pick: if fmod(cands[j].period + 10, P) ≤ 20: P = cands[j].period; break
                                                   # 2-arg CRT intrinsic @0x41438a on the x87 stack;
                                                   # fmod is inferred from operands (cand+10.0, P) vs 20.0

# Stage 2: refine over the whole ACF
t = round(P); k = 1; Σkk = Σkx = Σxx = 0; m = 0; miss = 0
while t < NL − 10 and miss == 0:
    a = argmax A[t−10 .. t+10]
    if A[a] ≤ 0 or |a − t| > 8: miss += 1; break
    x = interp(A, a); Σkx += k·x; Σkk += k²; Σxx += x²; m += 1
    slope = Σkx/Σkk; t = round(x + slope); k += 1
BPM   = 60000 / slope
sigma = sqrt((Σxx − Σkx²/Σkk) / (m−1))                  # ms, residual of fit through origin
rel   = sqrt((Σxx·Σkk/Σkx² − 1) / (m−1))
if m < 4: return −1                                     # "Cannot determine the timing"
if sigma > 2.4 or rel > 3e−5: return 16                 # "Approximate BPM" (offset skipped)
return 0 if (miss == 0 and sigma ≤ 0.6) else 1          # both accepted
```

## 5. Offset: `detect_offset` @ `0x402c70` (observed)

This is epoch folding: it averages `D` over every beat period.

```
P = 60000/BPM;  C = ceil(P)
H[j] = Σ_{k=0}^{ceil(n/P)−1} D[round(k·P) + j]   for j in 0..C+39 (bounded by n)
j* = argmax_{j ∈ [35, C+35)} H[j]
offset_int = interp(H, j*) − 6.2
```

## 6. Timeline diagnostics: `build_timeline` @ `0x402fc0` (observed from disassembly)

Each output character covers a **10 s bucket** (`b = floor(t/10000)`, `nb = ceil(n/10000)`). Under
the character row, the time row prints the minute digit every 6 characters.

```
w = round(0.7·P)
for t = offset_int + 6.2; t ≤ n−1; t += P:
    i = round(t)
    p10, a = max/argmax D[max(i−10,0) .. i+10]
    p30, a = max/argmax over D[max(i−30,0) .. i+30], seeded with (p10, a)   # argmax carries over
    pW     = max over D[max(i−w,0) .. i+w), seeded with p30
    b = floor(t/10000)
    if p10 > 0 and |a − t| ≤ 25:                       # on-beat hit
        d = (a − t) + parabolic_delta(D, a)            # deviation in ms
        W[b] += p10;  M1[b] += p10·d;  M2[b] += p10·d²;  Q[b] += p30
    else:                                              # miss / off-beat energy
        Q[b] += pW;   R[b] += pW − p10

k = P/10000                                            # converts bucket sums to per-beat means
for each bucket:
    S = Q·k  (mean peak strength/beat)   r = R/Q (off-beat share)
    μ = M1/W (mean deviation)  σ = sqrt(M2/W) (RMS deviation)  A = W·k
    thr = sqrt(0.2·σ² + 6)
    if (S>3 ∧ r>0.7) ∨ (S>4 ∧ r>0.5) ∨ (S>6 ∧ r>0.4) ∨ (S>10 ∧ r>0.3):  flag 1  '#'  needs another timing section
    elif S>3 ∧ μ >  thr:                                                flag 2  '+'  onsets late vs. timing
    elif S>3 ∧ μ < −thr:                                                flag 4  '-'  onsets early vs. timing
    elif A>3 ∧ σ>10:                                                    flag 8  '.'  BPM drift / jitter
return OR of all flags                                 # main prints INFO lines per bit
```

Empty buckets yield NaN or ∞ comparisons, which evaluate false, so they print as a space.

## 7. Constants

| Value | Where | Meaning |
|---|---|---|
| 44100 | main, BASS_Init | design sample rate |
| 600 s | main | length cap (FFT table = 2²⁰) |
| 22.5 ms | main | display offset to internal offset |
| 30 ms | main | fold user offset to first beat ≥ 30 ms |
| 6.2 ms | offset / timeline | envelope latency correction |
| 2000 / 999 ms | BPM | max lag for candidates / max fundamental (≥ 60 BPM) |
| ±10 / ±8 ms | BPM | harmonic search window / acceptance |
| 0.7, 1.2 | BPM | harmonic strength ratio / "clearly stronger" ratio |
| 285.714 ms, 10, 20 | BPM | 210 BPM ceiling, multiple-of-period tolerance |
| 2.4 ms, 3e−5, 0.6 ms | BPM | approximate / exact thresholds |
| 35 | offset | fold search start (≈ 30 ms + latency) |
| ±10, ±30, ±0.7P, 25 ms | timeline | windows / max hit deviation |
| 3, 4, 6, 10 / 0.7, 0.5, 0.4, 0.3 | timeline | `#` strength / ratio pairs |
| 0.2, 6, 10 ms | timeline | `+`/`−` threshold, `.` jitter threshold |

## 8. Unknowns and limits of this analysis

- The 2-arg intrinsic @ `0x41438a` is assumed to be `fmod`. Its operands fit that reading, but the dispatcher was not traced.
- The meaning of the split between 22.5 ms and 6.2 ms is inferred. Only their sum (28.7 ms) affects output.
- Rounding helpers: Ghidra inlined SSE2 `floor(x+0.5)` sequences, written here as `round`. Exact tie behaviour was not verified.
- **Validated by execution (2026-10-16).** `reference/timing_analyzer.py` implements this document and matches the original's printed BPM, offset and timeline in **45/45 runs** (15 songs, 3 modes each, 8 of them held out). The set includes all four timeline flags and tempos above 210 BPM. Agreement is at printed precision (3 decimals BPM, 1 ms offset). Internal float32 vs float64 differences are not visible at that precision.
