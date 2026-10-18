# Function map

The application code occupies `0x401000–0x405050`. BASS import thunks start at `0x405054`, and the
static MSVC CRT follows. Ghidra names are `FUN_00xxxxxx`. The role names below are ours.

| Address | Role | Signature (recovered) | Pseudocode |
|---|---|---|---|
| `0x401000` | `init_fft_tables` | `void()`: twiddles @`0x8211c0` (2²⁰ × {cos,sin} float), bit-reverse @`0x4211c0` (2²⁰ int) | `reverse/pseudocode/app/401000_init_fft_tables.c` |
| `0x4010b0` | `fft_radix2` | `(complex* in, complex* out, CL=log2 N)`: iterative radix-2, out-of-place | `4010b0_fft_radix2.c` |
| `0x401580` | `biquad_cascade` | `(ESI=vector<float>* io, EAX=nsections, [esp]=float coef[5·n])` | `401580_biquad_cascade.c` |
| `0x4017a0` | `autocorrelate_fft` | `(ECX=vector<float>* x, lags, vector<float>* out)` | `4017a0_autocorrelate_fft.c` |
| `0x401b30` | `resample_nearest` | `(EAX=vector<float>* out, vector<float>* in, double ratio)` | `401b30_resample_nearest.c` |
| `0x401c60` | `load_audio_bass` | `bool(path, double* rate, EDI=vector<float>* mono)` | `401c60_load_audio_bass.c` |
| `0x401e80` | `compute_onset_envelope` | `(vector<float>* mono, double rate, EDI=vector<float>* out)` | `401e80_compute_onset_envelope.c` (+ `.asm`) |
| `0x402280` | `detect_bpm` | `int(ECX=vector<float>* env, double* bpm)`: −1 fail, 0/1 ok, 16 approximate | `402280_detect_bpm.c` (+ `.asm`) |
| `0x402c70` | `detect_offset` | `int(vector<float>* env, double* offset, XMM1=bpm)`: −1 fail, 0 ok | `402c70_detect_offset.c` |
| `0x402fc0` | `build_timeline` | `uint flags(env, vector<int>* marks, double bpm, double offset)` | `402fc0_build_timeline.c` (+ `.asm`; the asm is authoritative, because the decompiler merged the 5 accumulator vectors) |
| `0x4037a0` | `wmain` | `(argc, argv)` | `4037a0_wmain.c` (+ `.asm`) |
| `0x404000–0x404fd0` | `std::vector<float/double/int>` internals | copy, fill, grow, insert (`"vector<T> too long"`) | `reverse/pseudocode/stl/` |
| `0x401550` | `std::bad_alloc` scalar deleting dtor | | `stl/FUN_00401550.c` |

CRT helpers. The names come from code shape and call-site usage (inferred). Only `log` and `ceil` were checked against their internals:

| Address | Identity | Basis |
|---|---|---|
| `0x413958` | `log` (natural) | SSE2 table log; constants ln2 hi/lo @`0x41a7c0`, series 1/3, −1/4, 1/5, −1/2 |
| `0x405db0` | `ceil` | returns 1.0 for 0<x<1, trunc+1 when trunc<x |
| `0x413f70` | `__ftol2` | x87 ST0 to int |
| `0x41438a` | 2-arg intrinsic dispatcher (`__cintrindisp2`), likely `fmod` | operands `(cand+10.0, P)` compared to 20.0 |
| `0x4141d3` / `0x41401b` | `cos` / `sin` | used only to fill the twiddle table |
| `0x4064f9` | `_wtof` | parses argv / console BPM and offset |
| `0x405ecd` | `__iob_func` | `+0x40` = **stderr** (verified by running it). Status lines and the timeline go to stderr. Only `BPM:`/`Offset:`/`Approximate BPM` go to stdout via `wprintf`. |
| `0x406282` | `operator delete` / `free` | |

Calling conventions: the build uses LTCG custom conventions, so many arguments travel in
EAX/ECX/ESI/EDI and Ghidra shows those calls as argument-less. Check the `.asm` files when a call
looks empty.
