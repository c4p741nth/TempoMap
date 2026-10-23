namespace TimingAnalyzer.Editor;

/// <summary>A stretch of the song to re-time: the editor's highlighted range, and what Auto-time range asks for.</summary>
public sealed record RangeRequest(double StartMs, double EndMs, double? Bpm, int DriftBeats = 0, bool BeatModel = false);

/// <summary>Red lines for a re-timed range (Auto-time range).</summary>
public static class RangeEdit
{
    /// <summary>
    /// The red lines inside [<paramref name="start"/>, <paramref name="end"/>) give way to <paramref name="lines"/>
    /// (those inside the range, at whole ms). The music after the range keeps its timing: the red line in effect at
    /// the end continues from its first beat at or after the end, as a copy on its own grid, unless the copy would hold
    /// less than a beat before the next red line or the last new line already has that grid. Returns the red lines and
    /// that copy (null when none).
    /// </summary>
    public static (TimingPoint[] Lines, TimingPoint? Continuation) Replace(IReadOnlyList<TimingPoint> reds, double start, double end,
        IReadOnlyList<TimingPoint> lines)
    {
        if (!(end > start)) throw new ArgumentOutOfRangeException(nameof(end));
        var fresh = lines.Select(p => p with { Time = OsuPrecision.Time(p.Time) }).Where(p => p.Time >= start && p.Time < end)
            .GroupBy(p => p.Time).Select(g => g.First()).OrderBy(p => p.Time).ToList();
        if (fresh.Count == 0) throw new InvalidOperationException("The analysis found no red line inside the range.");
        var kept = reds.Where(p => p.Time < start || p.Time >= end).ToList();
        TimingPoint? continuation = null;
        if (reds.LastOrDefault(p => p.Time < end) is { } governing)
        {
            double beat = governing.Time + Math.Ceiling((end - governing.Time) / governing.BeatLength - 1e-9) * governing.BeatLength;
            double time = OsuPrecision.Time(beat);
            var last = fresh[^1];
            bool sameGrid = OsuPrecision.Bpm(last.Bpm) == OsuPrecision.Bpm(governing.Bpm) &&
                Math.Abs(last.Time + Math.Round((beat - last.Time) / last.BeatLength) * last.BeatLength - beat) < 1;
            if ((kept.FirstOrDefault(p => p.Time >= end) is not { } next || next.Time - time >= governing.BeatLength) && !sameGrid)
                continuation = governing with { Time = time };
        }
        var result = kept.Concat(fresh);
        if (continuation is not null) result = result.Append(continuation);
        return (BeatGrid.Sort(result), continuation);
    }

    /// <summary>
    /// Pasted red lines (timing point rows) join the timing; one at the same time as an existing red line replaces it.
    /// With <paramref name="replaceAll"/> they replace every red line. Green rows are left out (they are read-only
    /// here) and counted.
    /// </summary>
    public static (TimingPoint[] Lines, TimingPoint[] Pasted, int Replaced, int Greens) Paste(IReadOnlyList<TimingPoint> reds, string text, bool replaceAll)
    {
        var rows = TimingClipboard.Parse(text);
        var pasted = rows.Where(p => p.Uninherited).GroupBy(p => p.SerializedTime).Select(g => g.Last() with { Time = g.Key }).ToArray();
        if (pasted.Length == 0)
            throw new InvalidOperationException(rows.Length == 0
                ? "The clipboard holds no timing point rows (copy them from a .osu [TimingPoints] section or the osu! editor)."
                : "The clipboard holds only green lines; green lines are read-only here.");
        var times = pasted.Select(p => p.Time).ToHashSet();
        var kept = replaceAll ? [] : reds.Where(p => !times.Contains(OsuPrecision.Time(p.Time))).ToList();
        int replaced = replaceAll ? reds.Count : reds.Count - kept.Count;
        return (BeatGrid.Sort(kept.Concat(pasted)), pasted, replaced, rows.Count(p => !p.Uninherited));
    }
}
