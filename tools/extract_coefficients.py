"""Extract the biquad filter tables and scalar constants from TimingAnlyz 0.32.4.exe.

Usage: python -I tools/extract_coefficients.py "G:/osu!/TimingAnlyz 0.32.4.exe" [out.json]

Reads bytes only; never executes the target. Prints each band's measured
passband so the tables can be checked against docs/ALGORITHM.md.
"""
import cmath
import hashlib
import json
import math
import struct
import sys

EXPECTED_SHA256 = "0077c48973701b4dbc0799ad99c20b009f8e3ae0539fb724215df6894b71608b"

BANK_ADDR = 0x4193F0      # 16 biquad sections, 5 floats each
BAND_SECTIONS = [0, 2, 6, 10, 14, 16]  # band b uses sections [b, b+1)
ENV_SMOOTH_ADDR = 0x419530  # 1 section, applied to squared band output @ 44.1 kHz
POST_1K_ADDR = 0x419544     # 1 section, applied forward and time-reversed @ 1 kHz
DOUBLES = {
    0x41A640: "0.5", 0x41A690: "0.3", 0x41A698: "10.0", 0x41A6A0: "0.4",
    0x41A6A8: "4.0", 0x41A6B0: "3.0", 0x41A6B8: "6.0", 0x41A6C0: "0.2",
    0x41A6D0: "0.7", 0x41A710: "1000.0", 0x41A740: "20.0",
}


class Pe:
    def __init__(self, data):
        self.data = data
        pe = struct.unpack_from("<I", data, 0x3C)[0]
        nsec = struct.unpack_from("<H", data, pe + 6)[0]
        optsz = struct.unpack_from("<H", data, pe + 20)[0]
        self.base = struct.unpack_from("<I", data, pe + 24 + 28)[0]
        self.sections = []
        for i in range(nsec):
            o = pe + 24 + optsz + i * 40
            vsz, va, rsz, raw = struct.unpack_from("<IIII", data, o + 8)
            self.sections.append((va, max(vsz, rsz), raw))

    def offset(self, addr):
        rva = addr - self.base
        for va, size, raw in self.sections:
            if va <= rva < va + size:
                return raw + rva - va
        raise ValueError("address %#x is not file-backed" % addr)

    def floats(self, addr, n):
        return list(struct.unpack_from("<%df" % n, self.data, self.offset(addr)))

    def double(self, addr):
        return struct.unpack_from("<d", self.data, self.offset(addr))[0]


def section_dict(c):
    # Direct Form II: w = g*x - d1*w1 - d2*w2 ; y = w + n1*w1 + n2*w2
    return {"n1": c[0], "n2": c[1], "d1": c[2], "d2": c[3], "gain": c[4]}


def response(sections, f, fs):
    z = cmath.exp(-1j * 2 * math.pi * f / fs)
    h = 1
    for s in sections:
        h *= s["gain"] * (1 + s["n1"] * z + s["n2"] * z * z) / (1 + s["d1"] * z + s["d2"] * z * z)
    return abs(h)


def passband(sections, fs):
    freqs = [5 * 1.01 ** k for k in range(1200) if 5 * 1.01 ** k < fs / 2]
    mags = [(f, response(sections, f, fs)) for f in freqs]
    peak_f, peak = max(mags, key=lambda t: t[1])
    inside = [f for f, m in mags if m >= peak / math.sqrt(2)]
    return {"peak_hz": round(peak_f, 1), "peak_gain": round(peak, 4),
            "minus3db_low_hz": round(inside[0], 1), "minus3db_high_hz": round(inside[-1], 1)}


def main():
    path = sys.argv[1]
    data = open(path, "rb").read()
    digest = hashlib.sha256(data).hexdigest()
    if digest != EXPECTED_SHA256:
        print("warning: sha256 %s differs from the analysed build" % digest, file=sys.stderr)
    pe = Pe(data)
    bank = [section_dict(pe.floats(BANK_ADDR + i * 20, 5)) for i in range(16)]
    bands = []
    for b in range(5):
        secs = bank[BAND_SECTIONS[b]:BAND_SECTIONS[b + 1]]
        bands.append({"band": b, "sections": secs, "response_44100": passband(secs, 44100.0)})
    smooth = section_dict(pe.floats(ENV_SMOOTH_ADDR, 5))
    post = section_dict(pe.floats(POST_1K_ADDR, 5))
    out = {
        "source": {"file": path, "sha256": digest},
        "band_bank_address": hex(BANK_ADDR),
        "bands": bands,
        "envelope_smoother_44100": {"address": hex(ENV_SMOOTH_ADDR), "section": smooth,
                                    "dc_gain": round(response([smooth], 0.001, 44100.0), 3)},
        "post_filter_1000": {"address": hex(POST_1K_ADDR), "section": post,
                             "dc_gain": round(response([post], 0.001, 1000.0), 3)},
        "doubles": {hex(a): pe.double(a) for a in DOUBLES},
    }
    for a, want in DOUBLES.items():
        if abs(out["doubles"][hex(a)] - float(want)) > 1e-12:
            print("warning: constant at %#x is %r, expected %s" % (a, out["doubles"][hex(a)], want), file=sys.stderr)
    text = json.dumps(out, indent=2)
    if len(sys.argv) > 2:
        open(sys.argv[2], "w", encoding="utf-8").write(text + "\n")
    for b in bands:
        r = b["response_44100"]
        print("band %d: %d sections, -3 dB %.0f..%.0f Hz" % (b["band"], len(b["sections"]), r["minus3db_low_hz"], r["minus3db_high_hz"]))


if __name__ == "__main__":
    main()
