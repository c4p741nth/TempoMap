namespace TimingAnalyzer.Editor;

/// <summary>The fixed correction for osu! clipboard text, applied only to exported rows.</summary>
public static class OsuTimingExport
{
    public const int ShiftMs = -33;

    public static string Write(IEnumerable<TimingPoint> points)
    {
        // Shift greens too: sample, volume, kiai and SV changes must keep their position relative to the red lines.
        // Negative times are valid osu! offsets. Clamping them would change the grid and collapse distinct rows.
        return TimingClipboard.Write(points.Select(p => p with { Time = p.Time + ShiftMs }));
    }
}
