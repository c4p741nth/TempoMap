namespace TimingAnalyzer.Editor;

public readonly record struct MeasureRow(double StartMs, double EndMs);
public static class MeasureLayout
{
    /// <summary>Rows end after actual bar boundaries, including tempo/meter changes and omitted first bar lines.</summary>
    public static MeasureRow[] Rows(IEnumerable<TimingPoint> timing, double viewStart, int rows = 4, int barsPerRow = 4)
    {
        if (!double.IsFinite(viewStart) || rows is < 1 or > 16 || barsPerRow is < 1 or > 16) throw new ArgumentOutOfRangeException(nameof(viewStart));
        var points = BeatGrid.Effective(timing);
        if (points.Length == 0) points = [new(0, 500)];
        double Length(TimingPoint p) => (p.BeatLength is >= 50 and <= 60000 ? p.BeatLength : 500) * Math.Min(32, p.Meter);
        int section = Math.Max(0, Array.FindLastIndex(points, p => p.Time <= viewStart));
        var initial = points[section]; double measure = Length(initial);
        double cursor = Math.Max(0, initial.Time + Math.Floor((viewStart - initial.Time) / measure) * measure);
        var result = new MeasureRow[rows];
        for (int row = 0; row < rows; row++)
        {
            double start = cursor; int bars = 0;
            for (int safety = 0; bars < barsPerRow && safety < points.Length + barsPerRow * 2; safety++)
            {
                section = Math.Max(0, Array.FindLastIndex(points, p => p.Time <= cursor + 1e-6));
                var point = points[section]; measure = Length(point);
                double nextBar = point.Time + (Math.Floor((cursor - point.Time) / measure + 1e-9) + 1) * measure;
                double nextRed = section + 1 < points.Length ? points[section + 1].Time : double.PositiveInfinity;
                if (nextRed <= nextBar + 1e-6)
                {
                    cursor = nextRed;
                    if (!points[section + 1].OmitsFirstBarline) bars++;
                }
                else { cursor = nextBar; bars++; }
            }
            if (!(cursor > start)) cursor = start + measure * barsPerRow;
            result[row] = new(start, cursor);
        }
        return result;
    }
}
