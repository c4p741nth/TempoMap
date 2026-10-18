namespace TimingAnalyzer.Core;

public static class OffsetDetector
{
    public static double Detect(double[] d, double bpm)
    {
        double period = 60000.0 / bpm;
        int c = (int)Math.Ceiling(period);
        var h = new double[c + 40];
        int beats = (int)Math.Ceiling(d.Length / period);
        for (int k = 0; k < beats; k++)
        {
            int s = Peaks.Round(k * period);
            int count = Math.Min(h.Length, d.Length - s);
            for (int j = 0; j < count; j++) h[j] += d[s + j];
        }
        int peak = Peaks.ArgMax(h, TimingConstants.OffsetSearchStart, c + TimingConstants.OffsetSearchStart);
        return Peaks.Interpolate(h, peak) - TimingConstants.OffsetLatency;
    }

    public static double FoldUserOffset(double offset, double bpm)
    {
        double o = offset + TimingConstants.OffsetDisplay;
        double p = 60000.0 / bpm;
        return o - Math.Floor((o - TimingConstants.MinimumUserOffset) * bpm / 60000.0) * p;
    }
}
