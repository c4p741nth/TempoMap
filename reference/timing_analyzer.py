"""Reference re-implementation of TimingAnlyz 0.32.4 (see docs/ALGORITHM.md).

Faithful port, not an improvement: same stages, constants and quirks, so its output
can be checked against the original binary. Addresses in comments point at the
recovered code.

Usage: python reference/timing_analyzer.py <audio> [bpm] [offset]
"""
import json
import math
import sys
from pathlib import Path

import numpy as np
import soundfile as sf
from scipy.signal import sosfilt

# The extracted tables live beside the C# engine that embeds them (moved from reverse/, which is not published).
COEF_PATH = Path(__file__).resolve().parent.parent / "src" / "TimingAnalyzer.Core" / "coefficients.json"
OFFSET_DISPLAY = 22.5   # wmain: displayed osu! offset = internal - 22.5
OFFSET_LATENCY = 6.2    # detect_offset subtracts, build_timeline adds back
UNSET_OFFSET = -10000.0


def load_coefficients(path=COEF_PATH):
    c = json.loads(Path(path).read_text(encoding="utf-8"))
    to_sos = lambda secs: np.array([[s["gain"], s["gain"] * s["n1"], s["gain"] * s["n2"], 1.0, s["d1"], s["d2"]]
                                    for s in secs])
    return {
        "bands": [to_sos(b["sections"]) for b in c["bands"]],
        "smooth": to_sos([c["envelope_smoother_44100"]["section"]]),
        "post": to_sos([c["post_filter_1000"]["section"]]),
    }


MP3_DECODER_DELAY = 529  # samples; see mp3_has_gapless_header


def mp3_has_gapless_header(path):
    """True when the first MPEG frame carries a Xing/Info (LAME gapless) header."""
    with open(path, "rb") as fh:
        head = fh.read(10)
        start = 0
        if head[:3] == b"ID3":
            start = 10 + ((head[6] & 0x7F) << 21 | (head[7] & 0x7F) << 14 | (head[8] & 0x7F) << 7 | (head[9] & 0x7F))
        fh.seek(start)
        frame = fh.read(200)
    return b"Xing" in frame or b"Info" in frame


def load_audio(path):
    """load_audio_bass @0x401c60: decode to float, sum (not average) the channels.

    Decoder parity: libsndfile (mpg123) keeps the 529-sample MP3 decoder delay when
    the file has no Xing/Info header, while BASS does not. Dropping it makes the
    output match the original exactly (verified on the parity set). MP3s with a
    gapless header and OGG already line up.
    """
    data, rate = sf.read(path, dtype="float32", always_2d=True)
    mono = data.sum(axis=1, dtype=np.float64)
    if str(path).lower().endswith(".mp3") and not mp3_has_gapless_header(path):
        mono = mono[MP3_DECODER_DELAY:]
    return mono, float(rate)


def interp(y, i):
    """Parabolic peak position used throughout."""
    return ((y[i + 1] - y[i - 1]) * 0.5) / ((y[i] - y[i + 1]) + (y[i] - y[i - 1])) + i


def onset_envelope(mono, rate, coef):
    """compute_onset_envelope @0x401e80 -> 1 kHz onset strength D (index = ms)."""
    acc = np.ones_like(mono)
    for band in coef["bands"]:
        y = sosfilt(band, mono)
        acc *= 1.0 + sosfilt(coef["smooth"], y * y)
    log_e = sosfilt(coef["bands"][0], np.log(acc))

    ratio = 1000.0 / rate                                   # resample_nearest @0x401b30
    m = int(math.ceil(len(log_e) * ratio))
    src = np.floor(np.arange(m) * (1.0 / ratio) + 0.5).astype(np.int64)
    x = np.where(src < len(log_e), log_e[np.minimum(src, len(log_e) - 1)], 0.0)

    fwd = sosfilt(coef["post"], x)
    bwd_rev = sosfilt(coef["post"], x[::-1])                 # bwd_rev[k] is time m-1-k
    d = np.empty(m)
    d[m - 1] = -fwd[m - 2]
    d[1:m - 1] = bwd_rev[m - 3::-1] - fwd[0:m - 2]           # D[i] = B[i+1] - F[i-1]
    d[0] = bwd_rev[m - 2]
    return d


def autocorrelate(d, lags):
    """autocorrelate_fft @0x4017a0 (Wiener-Khinchin, power-of-two zero padding)."""
    nfft = 1 << int(math.ceil(math.log2(len(d) + lags - 0.5)))
    spec = np.fft.rfft(d, nfft)
    return np.fft.irfft(spec.real ** 2 + spec.imag ** 2, nfft)[:lags]


def detect_bpm(d):
    """detect_bpm @0x402280 -> (status, bpm). status: -1 fail, 0/1 ok, 16 approximate."""
    nl = int(math.floor(len(d) * 0.5))
    a = autocorrelate(d, nl)
    max_l = min(nl, 2000)
    peaks = [l for l in range(2, max_l - 2)
             if a[l] > 0 and a[l - 1] < a[l] and a[l + 1] < a[l] and a[l - 2] < a[l - 1] and a[l + 2] < a[l + 1]]

    cands, best, pick = [], 0.0, None
    for ci, lag in enumerate(peaks):
        if lag > 999:
            break
        t, k, skk, skx, hits, hsum, misses, u, slope = lag, 1, 0.0, 0.0, 0, 0.0, 0, ci, float(lag)
        while t < max_l - 10 and misses == 0:
            found = False
            while u < len(peaks):
                if peaks[u] >= t - 10:
                    found = peaks[u] <= t + 10
                    break
                u += 1
            if found:
                q = peaks[u]
                hits += 1
                hsum += a[q]
                x = interp(a, q)
                skk += k * k
                skx += k * x
                slope = skx / skk
                nxt = x + slope
            else:
                misses += 1
                nxt = t + slope
            t = int(math.floor(nxt + 0.5))
            k += 1
        if hits > 0 and hsum / hits > a[lag] * 0.7:
            mean = hsum / hits
            if best * 1.2 < mean:
                pick = len(cands)
            best = max(best, mean)
            cands.append((skx / skk, mean))
    if not best > 0:
        return -1, None

    period = cands[pick][0]
    if period < 285.7142857142857:                           # > 210 BPM: take a multiple-period candidate
        for p, _ in cands[pick + 1:]:
            if math.fmod(p + 10.0, period) <= 20.0:
                period = p
                break

    t, k, skk, skx, sxx, m, misses, slope = int(math.floor(period + 0.5)), 1, 0.0, 0.0, 0.0, 0, 0, period
    while t < nl - 10 and misses == 0:
        win = a[t - 10:t + 11]
        j = int(np.argmax(win))
        peak, at = win[j], t - 10 + j
        if peak <= 0 or abs(at - t) > 8:
            misses += 1
            nxt = t + slope
        else:
            x = interp(a, at)
            skx += k * x
            skk += k * k
            sxx += x * x
            m += 1
            slope = skx / skk
            nxt = x + slope
        k += 1
        t = int(math.floor(nxt + 0.5))
    if m < 4:
        return -1, None
    bpm = skk * 60000.0 / skx
    sigma = math.sqrt(max(sxx - skx * skx / skk, 0.0) / (m - 1))
    rel = math.sqrt(max(sxx * skk / (skx * skx) - 1.0, 0.0) / (m - 1))
    if sigma > 2.4 or rel > 3e-5:
        return 16, bpm
    return (0 if misses < 1 and sigma <= 0.6 else 1), bpm


def detect_offset(d, bpm):
    """detect_offset @0x402c70: fold D over the beat period -> internal offset."""
    n, period = len(d), 60000.0 / bpm
    c = int(math.ceil(period))
    h = np.zeros(c + 40)
    for k in range(int(math.ceil(n / period))):
        s = int(math.floor(k * period + 0.5))
        cnt = min(len(h), n - s)
        if cnt > 0:
            h[:cnt] += d[s:s + cnt]
    j = 35 + int(np.argmax(h[35:c + 35]))
    return interp(h, j) - OFFSET_LATENCY


def fold_user_offset(offset, bpm):
    """wmain: displayed offset -> internal offset folded into [30, 30+P)."""
    o = offset + OFFSET_DISPLAY
    p = 60000.0 / bpm
    return o - math.floor((o - 30.0) * bpm / 60000.0) * p


def build_timeline(d, bpm, offset_int):
    """build_timeline @0x402fc0 -> (flags OR, per-10s marks)."""
    n, period = len(d), 60000.0 / bpm
    nb = int(math.ceil(n * 0.001 * 0.1))
    w_, m1, m2, q, r = (np.zeros(nb) for _ in range(5))
    half = int(math.floor(period * 0.7 + 0.5))
    t = offset_int + OFFSET_LATENCY
    while t <= n - 1:
        i = int(math.floor(t + 0.5))
        a, p10 = i, d[i]
        for j in range(max(i - 10, 0), min(i + 10, n - 1) + 1):
            if d[j] > p10:
                a, p10 = j, d[j]
        p30 = p10
        for j in range(max(i - 30, 0), min(i + 30, n - 1) + 1):
            if d[j] > p30:
                a, p30 = j, d[j]
        lo, hi = max(i - half, 0), min(i + half, n)
        pw = max(p30, d[lo:hi].max()) if hi > lo else p30
        b = int(math.floor(t * 0.001 * 0.1))
        if p10 > 0 and abs(a - t) <= 25.0 and 0 < a < n - 1:
            dev = (d[a + 1] - d[a - 1]) * 0.5 / ((d[a] - d[a - 1]) + (d[a] - d[a + 1])) + (a - t)
            w_[b] += p10
            m1[b] += p10 * dev
            m2[b] += p10 * dev * dev
            q[b] += p30
        else:
            q[b] += pw
            r[b] += pw - p10
        t += period

    k = period * 0.001 * 0.1
    marks = np.zeros(nb, dtype=np.int64)
    with np.errstate(divide="ignore", invalid="ignore"):
        s, ratio = q * k, r / q
        mu, sigma, amp = m1 / w_, np.sqrt(m2 / w_), w_ * k
        thr = np.sqrt(sigma * 0.2 * sigma + 6.0)
        for b in range(nb):
            if ((s[b] > 3 and ratio[b] > 0.7) or (s[b] > 4 and ratio[b] > 0.5)
                    or (s[b] > 6 and ratio[b] > 0.4) or (s[b] > 10 and ratio[b] > 0.3)):
                marks[b] = 1
            elif s[b] > 3 and mu[b] > thr[b]:
                marks[b] = 2
            elif s[b] > 3 and -thr[b] > mu[b]:
                marks[b] = 4
            elif amp[b] > 3 and sigma[b] > 10:
                marks[b] = 8
    return int(np.bitwise_or.reduce(marks)) if nb else 0, marks


def analyze(path, bpm=0.0, offset=None, coef=None):
    coef = coef or load_coefficients()
    mono, rate = load_audio(path)
    result = {"rate": rate, "seconds": len(mono) / rate, "status": None}
    if len(mono) / rate > 600:
        result["error"] = "longer than 10 minutes"
        return result
    d = onset_envelope(mono, rate, coef)
    result["envelope_len"] = len(d)
    if bpm < 1e-30:
        status, bpm = detect_bpm(d)
        result["status"] = status
        if status < 0:
            result["error"] = "cannot determine timing"
            return result
        if status >= 16:
            result["approx_bpm"] = bpm
            return result
        offset = None
    offset_int = detect_offset(d, bpm) if offset is None else fold_user_offset(offset, bpm)
    flags, marks = build_timeline(d, bpm, offset_int)
    chars = {1: "#", 2: "+", 4: "-", 8: "."}
    result.update(bpm=bpm, offset_int=offset_int, offset=offset_int - OFFSET_DISPLAY, flags=flags,
                  timeline="".join(chars.get(int(v), " ") for v in marks))
    return result


def main():
    args = sys.argv[1:]
    if not args:
        print(__doc__)
        return 2
    bpm = float(args[1]) if len(args) > 1 else 0.0
    offset = float(args[2]) if len(args) > 2 else None
    res = analyze(args[0], bpm, offset)
    if "error" in res:
        print("ERROR:", res["error"])
        return 1
    if "approx_bpm" in res:
        print("Approximate BPM: %8.1f" % res["approx_bpm"])
        return 0
    if res["flags"]:
        print("Timeline   >>%s<<" % res["timeline"])
        print("Time (min) >>%s<<" % "".join(str(u // 6) if u % 6 == 0 else " " for u in range(len(res["timeline"]))))
    print("BPM:    %8.3f" % res["bpm"])
    print("Offset: %8.0f" % res["offset"])
    return 0


if __name__ == "__main__":
    sys.exit(main())
