namespace TimingAnalyzer.Core;

/// <summary>
/// Onset features of a section at candidate tempos, for the octave selector (docs/PHASE1-STATUS.md E36): which
/// multiple of the detected BPM a mapper would write. For each candidate ratio r: the baseline-subtracted fit of the
/// grid at BPM × r at its best phase (onset strength at the beats, strongest within ±3 ms, minus the section's mean,
/// over its standard deviation), and the share of the section's onset peaks that lie within 15 ms of that grid. Plus
/// the BPM itself (mappers rarely write below 100 or above 300 even when the onsets allow it).
/// </summary>
public static class OctaveFeatures
{
    private const int ToleranceMs = 3;
    private const double HitMs = 15;

    public static IReadOnlyList<string> Names(IReadOnlyList<double> ratios)
    {
        var names = new List<string> { "log2_bpm" };
        foreach (double r in ratios) names.Add("fit_" + Label(r));
        foreach (double r in ratios) names.Add("hits_" + Label(r));
        return names;
    }

    private static string Label(double r) => r.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture).Replace('.', 'p');

    public static double[] Measure(double[] envelope, double bpm, double startMs, double endMs, IReadOnlyList<double> ratios)
    {
        int lo = Math.Clamp(Index(startMs), 1, envelope.Length - 1), hi = Math.Clamp(Index(endMs), lo, envelope.Length - 1);
        double mean = 0, sq = 0;
        for (int j = lo; j < hi; j++) { mean += envelope[j]; sq += envelope[j] * envelope[j]; }
        int n = Math.Max(1, hi - lo);
        mean /= n;
        double sd = Math.Sqrt(Math.Max(1e-12, sq / n - mean * mean));
        // Onset peaks: local maxima above mean + 1 sd, in osu! time.
        var peaks = new List<double>();
        for (int j = lo + 1; j < hi - 1; j++)
            if (envelope[j] > mean + sd && envelope[j] >= envelope[j - 1] && envelope[j] > envelope[j + 1]) peaks.Add(j - MultiBpmAnalyzer.PeakToOsuMs);
        var result = new List<double> { Math.Log2(bpm) };
        var phases = new double[ratios.Count];
        for (int k = 0; k < ratios.Count; k++)
        {
            double length = 60000 / (bpm * ratios[k]), best = double.NegativeInfinity;
            for (double phase = startMs; phase < startMs + length; phase += 1)
            {
                double fit = (Mean(envelope, length, phase, startMs, endMs) - mean) / sd;
                if (fit > best) { best = fit; phases[k] = phase; }
            }
            result.Add(best);
        }
        for (int k = 0; k < ratios.Count; k++)
        {
            double length = 60000 / (bpm * ratios[k]);
            int hits = peaks.Count(t => { double d = ((t - phases[k]) % length + length) % length; return Math.Min(d, length - d) <= HitMs; });
            result.Add(peaks.Count == 0 ? 0 : hits / (double)peaks.Count);
        }
        return [.. result];
    }

    private static int Index(double osuMs) => (int)Math.Round(osuMs + MultiBpmAnalyzer.PeakToOsuMs);

    private static double Mean(double[] d, double length, double phase, double a, double b)
    {
        double sum = 0;
        int count = 0;
        for (double t = phase + Math.Ceiling((a - phase) / length) * length; t < b; t += length)
        {
            int j = Index(t);
            if (j - ToleranceMs < 0 || j + ToleranceMs >= d.Length) continue;
            double peak = d[j];
            for (int k = j - ToleranceMs; k <= j + ToleranceMs; k++) peak = Math.Max(peak, d[k]);
            sum += peak; count++;
        }
        return count == 0 ? 0 : sum / count;
    }
}
