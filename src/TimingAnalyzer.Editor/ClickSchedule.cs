namespace TimingAnalyzer.Editor;

public readonly record struct ScheduledClick(long Sample, bool Downbeat);

public static class ClickSchedule
{
    public static long SampleAt(double timeMs, int rate) => checked((long)Math.Floor(timeMs * rate / 1000 + 0.5));

    /// <summary>Returns each rounded click once in [startSample, endSample).</summary>
    public static ScheduledClick[] Query(IReadOnlyList<TimingPoint> reds, int rate,
        long startSample, long endSample, bool accentDownbeats = true)
    {
        if (rate <= 0 || endSample < startSample) throw new ArgumentOutOfRangeException(nameof(rate));
        // Include beats just outside the unrounded interval, then filter by their rounded sample.
        // A red line that omits its first bar line is not a downbeat; its later bar lines are.
        var clicks = BeatGrid.Query(reds, (startSample - 1.0) * 1000 / rate, (endSample + 1.0) * 1000 / rate)
            .Select(g => new ScheduledClick(SampleAt(g.Time, rate), accentDownbeats &&
                (g.Kind == GridKind.Measure || g.Kind == GridKind.RedLine && !reds[g.Section].OmitsFirstBarline)))
            .Where(c => c.Sample >= startSample && c.Sample < endSample);
        return clicks.GroupBy(c => c.Sample).Select(g => new ScheduledClick(g.Key, g.Any(c => c.Downbeat))).ToArray();
    }
}
