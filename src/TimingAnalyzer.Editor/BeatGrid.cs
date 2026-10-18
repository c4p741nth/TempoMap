namespace TimingAnalyzer.Editor;

public enum GridKind { Subdivision, Beat, Measure, RedLine }
public sealed record GridLine(double Time, int Section, long Step, int Division, GridKind Kind);

public static class BeatGrid
{
    public static TimingPoint[] Sort(IEnumerable<TimingPoint> points)
    {
        var result = points.Select(p => p.Validate()).OrderBy(p => p.Time).ToArray();
        if (result.Any(p => !p.Uninherited) || result.Zip(result.Skip(1)).Any(p => p.First.Time == p.Second.Time))
            throw new ArgumentException("Red lines must have distinct times.");
        return result;
    }

    /// <summary>Half-open range [start, end); a new red line truncates the previous section.</summary>
    public static IEnumerable<GridLine> Query(IReadOnlyList<TimingPoint> reds, double start, double end, int division = 1)
    {
        if (!double.IsFinite(start) || !double.IsFinite(end) || division is < 1 or > 16)
            throw new ArgumentOutOfRangeException(nameof(start));
        for (int i = 0; i < reds.Count; i++)
        {
            var p = reds[i];
            double limit = Math.Min(end, i + 1 < reds.Count ? reds[i + 1].Time : end);
            double step = p.BeatLength / division;
            long first = Math.Max(0, (long)Math.Floor((start - p.Time) / step));
            for (long k = first; ; k++)
            {
                double t = p.Time + k * step;
                if (t >= limit) break;
                if (t < start) continue;
                var kind = k == 0 ? GridKind.RedLine : k % (p.Meter * division) == 0 ? GridKind.Measure :
                    k % division == 0 ? GridKind.Beat : GridKind.Subdivision;
                yield return new(t, i, k, division, kind);
            }
        }
    }
}
