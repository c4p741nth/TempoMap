using System.Globalization;
using TimingAnalyzer.Editor;

namespace TimingAnalyzer.Eval;

public sealed record EvalSong(string Set, string Split, string Audio, string Reference)
{
    public string Name => Path.GetFileName(Path.GetDirectoryName(Reference)) ?? Reference;
}

/// <summary>Picks reference timing from an osu! Songs folder: plausible, consensus, deterministic.</summary>
public static class SongSelection
{
    public const int Seed = 20261016;
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>Musically plausible: every red line 60–400 BPM, every section but the last at least two beats.</summary>
    public static bool Plausible(IReadOnlyList<TimingPoint> reds) =>
        reds.Count > 0 && reds.All(p => p.Bpm is >= 60 and <= 400) &&
        reds.Zip(reds.Skip(1)).All(p => p.Second.Time - p.First.Time >= 2 * p.First.BeatLength - 1);

    /// <summary>Tempos that differ by more than 0.5 % count as distinct.</summary>
    public static int DistinctBpms(IReadOnlyList<TimingPoint> reds)
    {
        var bpms = new List<double>();
        foreach (double bpm in reds.Select(p => p.Bpm).Order())
            if (bpms.Count == 0 || bpm > bpms[^1] * 1.005) bpms.Add(bpm);
        return bpms.Count;
    }

    public static List<EvalSong> Select(string songsDir, int multiCount, int singleCount, IEnumerable<EvalSong> extra)
    {
        var multi = new List<EvalSong>(); var single = new List<EvalSong>();
        // Submitted sets only ("<id> Artist - Title"); local "beatmap-..." folders are unfinished maps.
        foreach (var folder in Directory.EnumerateDirectories(songsDir).Where(d => char.IsDigit(Path.GetFileName(d)[0])).Order(StringComparer.Ordinal))
        {
            var maps = new List<(string Path, string Audio, TimingPoint[] Reds)>();
            foreach (string osu in Directory.EnumerateFiles(folder, "*.osu").Order(StringComparer.Ordinal))
            {
                try
                {
                    var doc = OsuFile.Load(osu);
                    if (doc.AudioFilename is not { } audio || !File.Exists(Path.Combine(folder, audio))) continue;
                    var reds = BeatGrid.Sort(doc.RedLines);
                    if (Plausible(reds)) maps.Add((osu, Path.Combine(folder, audio), reds));
                }
                catch (Exception ex) when (ex is FormatException or ArgumentException or IOException or OverflowException) { }
            }
            if (maps.Count == 0) continue;
            // The timing most difficulties share; ties go to the first file name.
            var chosen = maps.GroupBy(m => string.Join(";", m.Reds.Select(p => p.ToOsuLine())))
                .OrderByDescending(g => g.Count()).First().First();
            var song = new EvalSong("", "", chosen.Audio, chosen.Path);
            int distinct = DistinctBpms(chosen.Reds);
            if (distinct >= 2) multi.Add(song with { Set = "A" });
            else if (chosen.Reds.Length == 1) single.Add(song with { Set = "B" });
        }
        var random = new Random(Seed);
        var a = multi.OrderBy(_ => random.Next()).Take(multiCount).ToList();
        var b = single.OrderBy(_ => random.Next()).Take(singleCount).ToList();
        // Alternate tuning / held-out within each set, in the shuffled order.
        return extra.Concat(a.Select((s, i) => s with { Split = i % 2 == 0 ? "tune" : "hold" }))
            .Concat(b.Select((s, i) => s with { Split = i % 2 == 0 ? "tune" : "hold" })).ToList();
    }

    /// <summary>A song whose timing mappers care about most: osu!mania tournament pools (Mania World Cup and the
    /// like) and the artists with the most complex tempo maps (Camellia, Ludicin).</summary>
    private static readonly System.Text.RegularExpressions.Regex Tournament = new(
        @"\b(mwc|owc|mania world cup|world cup|4k ?wc|7k ?wc|tournament|tourney|qualifiers?|playoffs?|grand ?finals?|semi ?finals?|quarter ?finals?|round of \d+|ro\d{2})\b",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>
    /// osu!mania sets: every submitted set whose mania difficulties' consensus timing is plausible and has at least two
    /// BPMs. Set C = "complex" songs (tournament pools, Camellia, Ludicin), Set D = the rest, both split tune/hold by
    /// song so several sets of one song stay in one half; Set E = <see cref="Requested"/>, held out.
    /// Deterministic: sorted, then shuffled with <see cref="Seed"/>.
    /// </summary>
    /// <summary>
    /// Set E: hard songs the user picked (2026-10-16), by beatmap set id. Most fail the plausibility filter because
    /// their timing is a red line per beat (live or old recordings with drifting tempo) or has sub-2-beat steps, so
    /// they are held out entirely and only ever reported, never tuned on.
    /// </summary>
    private static readonly string[] Requested =
    [
        "2160393", // supercell - My Dearest (TV Size): old recording, tempo drifts 161-175 BPM
        "1802547", // Frums - Thinker Walker Dreamer Scatter
        "1683567", // T-SQUARE SUPER BAND SPECIAL - JAPANESE SOUL BROTHERS (Cut & Live ver.): live jazz, 400 red lines
        "1550061", // penoreri - Monochrome and Vivid
        "1908304", // DeltaH, Gardens, ... - Rite of Ascendancy
        "1721944", // ReeK - Multigenre Smackdown
        "2027242", // takehirotei as Infinite Limit - IRIS
    ];

    public static List<EvalSong> SelectMania(string songsDir)
    {
        var found = new List<(EvalSong Song, string Key, bool Complex)>();
        var requested = new List<EvalSong>();
        foreach (var folder in Directory.EnumerateDirectories(songsDir).Where(d => char.IsDigit(Path.GetFileName(d)[0])).Order(StringComparer.Ordinal))
        {
            var maps = new List<(string Path, string Audio, TimingPoint[] Reds, string Text, string Artist, string Title)>();
            foreach (string osu in Directory.EnumerateFiles(folder, "*.osu").Order(StringComparer.Ordinal))
            {
                try
                {
                    var meta = Metadata(osu);
                    if (meta.GetValueOrDefault("Mode") != "3") continue;
                    var doc = OsuFile.Load(osu);
                    if (doc.AudioFilename is not { } audio || !File.Exists(Path.Combine(folder, audio))) continue;
                    maps.Add((osu, Path.Combine(folder, audio), BeatGrid.Sort(doc.RedLines),
                        string.Join(" ", meta.GetValueOrDefault("Version"), meta.GetValueOrDefault("Tags"), meta.GetValueOrDefault("Source")),
                        meta.GetValueOrDefault("Artist") ?? "", meta.GetValueOrDefault("Title") ?? ""));
                }
                catch (Exception ex) when (ex is FormatException or ArgumentException or IOException or OverflowException) { }
            }
            if (maps.Count == 0) continue;
            var chosen = maps.GroupBy(m => string.Join(";", m.Reds.Select(p => p.ToOsuLine())))
                .OrderByDescending(g => g.Count()).First().First();
            if (Requested.Any(r => Path.GetFileName(folder).StartsWith(r + " ", StringComparison.Ordinal)))
            {
                // Hand-picked hard cases: kept whatever the plausibility filter says, as Set E.
                requested.Add(new EvalSong("E", "hold", chosen.Audio, chosen.Path));
                continue;
            }
            if (!Plausible(chosen.Reds) || DistinctBpms(chosen.Reds) < 2) continue;
            bool complex = maps.Any(m => Tournament.IsMatch(m.Text)) ||
                chosen.Artist.Contains("camellia", StringComparison.OrdinalIgnoreCase) || chosen.Artist.Contains("ludicin", StringComparison.OrdinalIgnoreCase);
            string key = (chosen.Artist + " - " + System.Text.RegularExpressions.Regex.Replace(chosen.Title, @"\s*[\(\[].*?[\)\]]", "")).Trim().ToLowerInvariant();
            found.Add((new EvalSong("C", "", chosen.Audio, chosen.Path), key, complex));
        }
        var random = new Random(Seed);
        // One split per song (several sets of the same song share a key), alternating in shuffled song order within
        // each set, so the small complex set splits evenly. A song counts as complex when any of its sets is.
        var complexKeys = found.Where(f => f.Complex).Select(f => f.Key).ToHashSet();
        var split = new Dictionary<string, string>();
        foreach (var group in found.Select(f => f.Key).Distinct().Order(StringComparer.Ordinal).OrderBy(_ => random.Next())
                     .GroupBy(complexKeys.Contains))
        {
            int i = 0;
            foreach (string key in group) split[key] = i++ % 2 == 0 ? "tune" : "hold";
        }
        return found.OrderByDescending(f => complexKeys.Contains(f.Key)).ThenBy(f => f.Song.Name, StringComparer.Ordinal)
            .Select(f => f.Song with { Split = split[f.Key], Set = complexKeys.Contains(f.Key) ? "C" : "D" })
            .Concat(requested).ToList();
    }

    private static Dictionary<string, string> Metadata(string osu)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        string section = "";
        foreach (string raw in File.ReadLines(osu))
        {
            string line = raw.Trim();
            if (line.StartsWith('[')) { section = line; if (section is not ("[General]" or "[Metadata]")) { if (result.Count > 0 && section != "[Editor]" && section != "[Difficulty]") break; } continue; }
            if (section is "[General]" or "[Metadata]" && line.IndexOf(':') is > 0 and var colon)
                result.TryAdd(line[..colon].Trim(), line[(colon + 1)..].Trim());
        }
        return result;
    }

    public static void Write(string path, IEnumerable<EvalSong> songs) => File.WriteAllLines(path,
        new[] { "# set\tsplit\taudio\treference (written by: timing-eval select; machine-local paths)" }
            .Concat(songs.Select(s => string.Join('\t', s.Set, s.Split, s.Audio, s.Reference))));

    public static List<EvalSong> Read(string path) => File.ReadAllLines(path)
        .Where(l => l.Length > 0 && !l.StartsWith('#'))
        .Select(l => l.Split('\t')).Select(f => new EvalSong(f[0], f[1], f[2], f[3])).ToList();
}
