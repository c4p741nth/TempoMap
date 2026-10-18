namespace TimingAnalyzer.Core;

public static class TimingConstants
{
    public const double OffsetDisplay = 22.5;
    public const double OffsetLatency = 6.2;
    public const double MinimumUserOffset = 30;
    public const int OffsetSearchStart = 35;
    public const double FastPeriod = 285.7142857142857;
    public const double MaximumSeconds = 600;
}

internal static class Peaks
{
    public static double Interpolate(double[] y, int i) =>
        ((y[i + 1] - y[i - 1]) * 0.5) / ((y[i] - y[i + 1]) + (y[i] - y[i - 1])) + i;

    public static int ArgMax(double[] y, int start, int end)
    {
        int best = start;
        for (int i = start + 1; i < end; i++) if (y[i] > y[best]) best = i;
        return best;
    }

    public static int Round(double x) => (int)Math.Floor(x + 0.5);
}
