"""Ranked status of every local beatmap, from osu! stable's own osu!.db (read only; no network, no osu! API).

osu! keeps its last check against the beatmap repository in osu!.db (field layout:
https://github.com/ppy/osu/wiki/Legacy-database-file-structure), so the status is the one osu! last saw: a map
ranked or loved since then reads as pending until osu! rechecks it. Usage:
    python tools/osu_db.py "G:/osu!/osu!.db" [out.tsv]
prints the count per status and, with out.tsv, writes folder / .osu file / status / mode / beatmap ID per map.
"""
import struct
import sys
from collections import Counter

STATUS = {0: "unknown", 1: "unsubmitted", 2: "pending", 3: "unused", 4: "ranked", 5: "approved", 6: "qualified", 7: "loved"}


class Reader:
    def __init__(self, data):
        self.d, self.p = data, 0

    def skip(self, item_size):
        """Skips an Int-counted list of fixed-size items."""
        n = self.take("i")
        self.p += n * item_size

    def take(self, fmt):
        v = struct.unpack_from("<" + fmt, self.d, self.p)
        self.p += struct.calcsize("<" + fmt)
        return v[0]

    def uleb(self):
        result = shift = 0
        while True:
            b = self.d[self.p]; self.p += 1
            result |= (b & 0x7F) << shift
            if b < 0x80: return result
            shift += 7

    def string(self):
        flag = self.d[self.p]; self.p += 1
        if flag == 0: return ""
        if flag != 0x0B: raise ValueError(f"bad string flag {flag:#x} at {self.p - 1}")
        n = self.uleb()
        s = self.d[self.p:self.p + n].decode("utf-8", "replace"); self.p += n
        return s


def read(path):
    r = Reader(open(path, "rb").read())
    version = r.take("i")
    r.take("i"); r.take("?"); r.take("q"); r.string()
    count = r.take("i")
    maps = []
    for _ in range(count):
        if version < 20191106: r.take("i")
        artist = r.string(); r.string(); title = r.string(); r.string(); r.string(); version_name = r.string()
        r.string(); r.string()  # audio file, MD5
        osu_file = r.string()
        status = r.take("B")
        r.take("h"); r.take("h"); r.take("h"); r.take("q")
        for _ in range(4): r.take("B" if version < 20140609 else "f")
        r.take("d")
        if version >= 20140609:
            pair = 10 if version >= 20250107 else 14
            for _ in range(4): r.skip(pair)
        r.take("i"); r.take("i"); r.take("i")
        r.skip(17)  # timing points
        r.take("i"); beatmap_id = r.take("i"); r.take("i")
        r.p += 4  # grades
        r.take("h"); r.take("f")
        mode = r.take("B")
        r.string(); r.string(); r.take("h"); r.string(); r.take("?"); r.take("q"); r.take("?")
        folder = r.string()
        r.take("q"); r.p += 5
        if version < 20140609: r.take("h")
        r.take("i"); r.take("B")
        maps.append((folder, osu_file, STATUS.get(status, str(status)), mode, beatmap_id, artist, title, version_name))
    return version, maps


if __name__ == "__main__":
    version, maps = read(sys.argv[1])
    print(f"osu!.db version {version}: {len(maps)} beatmaps")
    for status, n in Counter(m[2] for m in maps).most_common(): print(f"  {status:12} {n}")
    if len(sys.argv) > 2:
        with open(sys.argv[2], "w", encoding="utf-8") as f:
            f.write("# folder\tosu file\tstatus\tmode\tbeatmap id\n")
            for m in maps: f.write("\t".join(str(x) for x in m[:5]) + "\n")
