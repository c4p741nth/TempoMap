namespace TimingAnalyzer.Editor;

public sealed record SnapOption(int Divisor)
{
    public override string ToString() => "1/" + Divisor;
}

public static class BeatSnap
{
    public static IReadOnlyList<int> Divisors { get; } = [1, 2, 3, 4, 5, 6, 7, 8, 9, 12, 16];
    /// <summary>A position within this distance of a tick counts as on it, so a step always reaches the neighbour.</summary>
    public const double Tolerance = 0.5;

    /// <summary>
    /// The 1/<paramref name="divisor"/> tick strictly after (direction &gt; 0) or before (direction &lt; 0) the position.
    /// Each section ticks from its own red line; ticks before the first red line extend backwards from it.
    /// Red lines must be sorted (as <see cref="TimingEdits.RedLines"/> are).
    /// </summary>
    public static double Step(IReadOnlyList<TimingPoint> reds, double positionMs, int divisor, int direction)
    {
        if (!Divisors.Contains(divisor)) throw new ArgumentOutOfRangeException(nameof(divisor));
        if (!double.IsFinite(positionMs) || direction == 0) throw new ArgumentOutOfRangeException(nameof(positionMs));
        if (reds.Count == 0) return positionMs;
        return direction > 0 ? Next(reds, positionMs + Tolerance, divisor) : Previous(reds, positionMs - Tolerance, divisor);
    }

    // First tick > limit.
    private static double Next(IReadOnlyList<TimingPoint> reds, double limit, int divisor)
    {
        for (int i = Math.Max(0, Section(reds, limit)); ; i++)
        {
            double step = reds[i].BeatLength / divisor;
            double k = Math.Floor((limit - reds[i].Time) / step) + 1;
            if (i > 0) k = Math.Max(k, 0);
            double tick = reds[i].Time + k * step;
            if (i + 1 == reds.Count || tick < reds[i + 1].Time) return tick;
        }
    }

    // Last tick < limit.
    private static double Previous(IReadOnlyList<TimingPoint> reds, double limit, int divisor)
    {
        for (int i = Math.Max(0, Section(reds, limit)); ; i--)
        {
            if (i + 1 < reds.Count) limit = Math.Min(limit, reds[i + 1].Time);
            double step = reds[i].BeatLength / divisor;
            double k = Math.Ceiling((limit - reds[i].Time) / step) - 1;
            if (i == 0 || k >= 0) return reds[i].Time + k * step;
        }
    }

    // Last red line at or before the time, or -1 before the first.
    private static int Section(IReadOnlyList<TimingPoint> reds, double time)
    {
        int section = -1;
        for (int i = 0; i < reds.Count && reds[i].Time <= time; i++) section = i;
        return section;
    }
}
