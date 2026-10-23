namespace TimingAnalyzer.Core;

/// <summary>
/// <para>Auto-time's lines on the waveform's leading edge (docs/PHASE1-STATUS.md E43). The original detector places the
/// offset at the peak of its onset function minus 28.7 ms. That function looks ahead through its smoothing, so on a hit
/// with a sharp attack the peak comes only about 10 ms after the first sample of the hit and the offset lands 15-20 ms
/// before it (a crowd of rebellion - ReCreate of the Red: the music starts at 437 ms from digital silence, the onset
/// peak says 418). A mapper looking at the waveform or listening to the metronome puts the line on the hit, so every
/// Auto-time needed "+1/16 or +1/32 of a beat" by hand.</para>
/// <para>The leading edge is measured on the audio itself: the steepest rise of the log of the energy over 4 ms (energy of
/// 2 ms windows smoothed over 3 ms), which a loud hit and a quiet one share, searched from 30 ms before to 50 ms after
/// the onset peak. On the user's own recreate.ogg grid (143 BPM from 437 ms, 180 BPM from 49 108 ms, 143 BPM from 55 108 ms)
/// the edge sits -0.2, +6.0 and -0.2 ms from his lines (the onset peak convention: -20.6, -18.4, -20.8). Over 91
/// reference maps by other mappers the edge sits 18.7 ms (quartiles 13.7-20.7) after the peak convention, on 13 maps of the
/// user's own 17.7 ms; those maps' lines sit on the peak convention (their lines 2.7 ms after the peak on average), 13.6 ms before the edge.</para>
/// </summary>
public static class EdgeAlign
{
    /// <summary>Used when a song has too few clear hits to measure (the median over the reference maps, rounded).</summary>
    public const double DefaultLagMs = 18;
    /// <summary>The lag is kept within this range: a soft-attack song measures near 3 ms, none measured past 24.</summary>
    public const double MinLagMs = 0, MaxLagMs = 28;
    /// <summary>At least this many onset peaks must back a measurement.</summary>
    public const int MinBeats = 12;
    /// <summary>The strongest onset peaks measured (100: 2.9 ms from the grid-based measure, 300: 2.6, 150 in between).</summary>
    public const int DefaultTop = 150;
    /// <summary>The edge is searched from this far before the onset peak to this far after it.</summary>
    private const double EdgeBeforeMs = 30, EdgeAfterMs = 50;

    /// <summary>
    /// The steepest-rise function at 1 ms steps: <c>rise[t] = ln E[t] - ln E[t - 4]</c> with E the energy of 2 ms windows
    /// smoothed over 3 ms, floored at -70 dB so digital silence does not give an infinite rise. Its peak is 2 ms after the
    /// edge it marks (the middle of the 4 ms difference).
    /// </summary>
    public static double[] LogRise(double[] mono, double rate)
    {
        int n = (int)(mono.Length * 1000.0 / rate);
        var energy = new double[n];
        for (int t = 0; t < n; t++)
        {
            int s0 = (int)(t * rate / 1000), s1 = Math.Min(mono.Length, (int)((t + 2) * rate / 1000));
            double sum = 0;
            for (int i = s0; i < s1; i++) sum += mono[i] * mono[i];
            energy[t] = s1 > s0 ? sum / (s1 - s0) : 0;
        }
        var rise = new double[n];
        for (int t = 4; t < n; t++)
            rise[t] = Math.Log(Smooth(energy, t) + 1e-7) - Math.Log(Smooth(energy, t - 4) + 1e-7);
        return rise;
    }

    private static double Smooth(double[] e, int t)
    {
        double sum = 0; int count = 0;
        for (int j = t - 1; j <= t + 1; j++) if (j >= 0 && j < e.Length) { sum += e[j]; count++; }
        return count > 0 ? sum / count : 0;
    }

    /// <summary>
    /// How much later than the onset peak's osu! time (envelope index - 28.7 ms) the leading edge sits, as the median over
    /// the song's <paramref name="top"/> strongest onset peaks (at least 60 ms apart): the steepest rise of the audio within
    /// -30 / +50 ms of each. Needs no grid, so one number serves every mode. Against the same measure on a map's own grid
    /// (the stronger half of the beats whose onset peak lies within ±40 ms of it) it differs by 2.9 ms on average over
    /// the 161 reference songs (a constant 18.7 ms would differ by 4.3; <c>timing-eval edge-lag</c>). Null when fewer than <see cref="MinBeats"/> peaks
    /// give an edge.
    /// </summary>
    public static double? SongLag(double[] envelope, double[] rise, int top = DefaultTop)
    {
        var all = new List<(int J, double V)>();
        for (int j = 40; j < envelope.Length - 60; j++)
            if (envelope[j] > 0 && envelope[j] >= envelope[j - 1] && envelope[j] > envelope[j + 1]) all.Add((j, envelope[j]));
        var chosen = new List<int>();
        foreach (var (j, _) in all.OrderByDescending(p => p.V))
        {
            if (chosen.Count >= top) break;
            if (chosen.Any(c => Math.Abs(c - j) < 60)) continue;
            chosen.Add(j);
        }
        var lags = new List<double>();
        foreach (int j in chosen)
            if (EdgeNear(rise, j - MultiBpmAnalyzer.PeakToOsuMs) is { } edge) lags.Add(edge - (j - MultiBpmAnalyzer.PeakToOsuMs));
        return lags.Count < MinBeats ? null : Median(lags);
    }

    /// <summary>The measured lag clamped to its range, or <see cref="DefaultLagMs"/> when nothing could be measured.</summary>
    public static double Clamp(double? measured) => Math.Clamp(measured ?? DefaultLagMs, MinLagMs, MaxLagMs);

    // The leading edge near an onset peak (osu! ms): the steepest rise within -30 / +50 ms of it, minus 2 ms.
    private static double? EdgeNear(double[] rise, double peak)
    {
        int lo = (int)Math.Round(peak - EdgeBeforeMs), hi = (int)Math.Round(peak + EdgeAfterMs);
        if (lo < 4 || hi >= rise.Length) return null;
        int best = lo; double top = double.NegativeInfinity;
        for (int j = lo; j <= hi; j++) if (rise[j] > top) { top = rise[j]; best = j; }
        return best - 2.0;
    }

    private static double Median(IEnumerable<double> values)
    {
        var sorted = values.Order().ToList();
        return sorted.Count == 0 ? double.NaN : sorted[sorted.Count / 2];
    }
}
