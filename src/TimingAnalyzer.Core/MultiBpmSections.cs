namespace TimingAnalyzer.Core;

/// <summary>
/// Tracked beats → red lines. Tempo boundaries come from an optimal piecewise-constant segmentation of the
/// inter-beat intervals (immune to the tracker flipping onto off-beats). Long sections then get BPM and phase by
/// epoch folding of the onset envelope, the original's calibrated method; short ones (tempo ramps) by regression
/// on their beats, starting on the previous grid when that fits. Weak sections keep the previous grid.
/// </summary>
internal static class SectionBuilder
{
    private sealed record Span(int First, int Last, double Interval, double Support);

    public static List<TimingSection> Build(double[] d, List<TrackedBeat> beats, MultiBpmOptions o, TimingSection? global = null) =>
        Merge(d, beats, Sections(d, beats, o).Sections, o, global);

    /// <summary>The red lines of the constant-tempo spans, before any merge pass, and how many spans there were.</summary>
    public static (int Spans, List<TimingSection> Sections) Sections(double[] d, List<TrackedBeat> beats, MultiBpmOptions o)
    {
        var weights = beats.Where(b => b.Weight > 0).Select(b => b.Weight).Order().ToArray();
        double median = weights.Length == 0 ? 0 : weights[weights.Length / 2];
        var supported = beats.Select(b => b.Weight > 0 && b.Weight >= o.SupportRatio * median).ToArray();
        var times = beats.Select(b => b.TimeMs).ToArray();

        var result = new List<TimingSection>();
        var spans = Spans(times, supported, o);
        foreach (var span in spans)
        {
            double start = times[span.First], end = times[span.Last];
            if (span.Support < o.WeakSupport || !(span.Interval > 0)) continue;
            TimingSection? previous = result.Count > 0 ? result[^1] : null;
            double length, first;
            if (end - start >= o.LongSectionMs)
            {
                length = FoldPeriod(d, start, end, span.Interval, o);
                length = Snap(length, end - start, o);
                double phase = FoldPhase(d, start, end, length);
                first = phase + Math.Round((start - phase) / length) * length;
            }
            else
            {
                (length, first) = Regression(times, supported, span, o);
                length = Snap(length, end - start, o);
                if (previous is not null)
                {
                    double prevLength = 60000 / previous.Bpm;
                    double onGrid = previous.TimeMs + Math.Round((first - previous.TimeMs) / prevLength) * prevLength;
                    if (Math.Abs(onGrid - first) < o.ContinuityShare * length) first = onGrid;
                }
            }
            if (previous is not null && first <= previous.TimeMs + o.MinSectionBeats * 60000 / previous.Bpm / 2) continue;
            result.Add(new TimingSection(first, 60000 / length));
        }
        return (spans.Count, result);
    }

    /// <summary>Drops red lines a neighbour's grid explains, then hands sections the whole-song grid explains back to it.</summary>
    public static List<TimingSection> Merge(double[] d, List<TrackedBeat> beats, List<TimingSection> sections, MultiBpmOptions o, TimingSection? global)
    {
        double songEnd = beats[^1].TimeMs + (sections.Count > 0 ? 60000 / sections[^1].Bpm : 0);
        var merged = MergeExplained(d, sections, songEnd, o);
        return global is null ? merged : PreferGlobal(d, merged, global, songEnd, o);
    }

    /// <summary>
    /// Sections the whole-song grid explains (same test as <see cref="MergeExplained"/>) take that grid, so a
    /// mostly single-tempo song keeps the original's precise BPM and phase; neighbours on it collapse into one.
    /// </summary>
    private static List<TimingSection> PreferGlobal(double[] d, List<TimingSection> sections, TimingSection global, double songEnd, MultiBpmOptions o)
    {
        double length = 60000 / global.Bpm;
        var result = new List<TimingSection>();
        bool previousGlobal = false;
        for (int s = 0; s < sections.Count; s++)
        {
            double from = sections[s].TimeMs, to = s + 1 < sections.Count ? sections[s + 1].TimeMs : songEnd;
            bool explained = Explained(d, global, from, to) / Own(d, sections[s], from, to) >= o.InheritRatio;
            if (!explained) { result.Add(sections[s]); previousGlobal = false; continue; }
            if (previousGlobal) continue; // The global grid already runs through here.
            double first = global.TimeMs + Math.Round((from - global.TimeMs) / length) * length;
            if (result.Count > 0 && first <= result[^1].TimeMs) first += length;
            result.Add(new TimingSection(first, global.Bpm));
            previousGlobal = true;
        }
        return result;
    }

    /// <summary>
    /// Drops red lines a neighbour's grid already explains: fold each section with the neighbour's grid (fixed
    /// phase) and with its own; when the neighbour reaches <see cref="MultiBpmOptions.InheritRatio"/> of its own
    /// peak, one grid covers both. This removes metre switches of the tracker (e.g. dotted-beat ⅔ tempo in
    /// syncopated parts), off-beat flips and spurious sections in breaks, while real tempo changes smear the
    /// neighbour's grid and stay. Merges the best-explained boundary first, until none qualifies.
    /// </summary>
    private static List<TimingSection> MergeExplained(double[] d, List<TimingSection> sections, double songEnd, MultiBpmOptions o)
    {
        var list = sections.ToList();
        while (list.Count > 1)
        {
            double bestRatio = 0; int bestBoundary = -1; bool keepLeft = true;
            for (int s = 0; s + 1 < list.Count; s++)
            {
                // Two grids over their own ranges vs one grid over both, weighted by duration, so a short
                // section's noisy fold cannot outweigh its long neighbour.
                double from = list[s].TimeMs, middle = list[s + 1].TimeMs, to = s + 2 < list.Count ? list[s + 2].TimeMs : songEnd;
                double left = middle - from, right = to - middle;
                double separate = (Explained(d, list[s], from, middle) * left + Explained(d, list[s + 1], middle, to) * right) / (left + right);
                if (!(separate > 0)) separate = double.Epsilon;
                double forward = Explained(d, list[s], from, to) / separate;
                double backward = Explained(d, Extend(list[s + 1], from), from, to) / separate;
                if (forward > bestRatio) { bestRatio = forward; bestBoundary = s; keepLeft = true; }
                if (backward > bestRatio) { bestRatio = backward; bestBoundary = s; keepLeft = false; }
            }
            if (bestRatio < o.InheritRatio || bestBoundary < 0) break;
            if (keepLeft) list.RemoveAt(bestBoundary + 1);
            else
            {
                var extended = Extend(list[bestBoundary + 1], list[bestBoundary].TimeMs);
                if (bestBoundary > 0 && extended.TimeMs <= list[bestBoundary - 1].TimeMs) extended = extended with { TimeMs = extended.TimeMs + 60000 / extended.Bpm };
                list[bestBoundary] = extended;
                list.RemoveAt(bestBoundary + 1);
            }
        }
        return list;
    }

    /// <summary>The same grid, starting at its beat nearest <paramref name="from"/>.</summary>
    private static TimingSection Extend(TimingSection grid, double from)
    {
        double length = 60000 / grid.Bpm;
        return grid with { TimeMs = grid.TimeMs - Math.Floor((grid.TimeMs - from) / length + 0.5) * length };
    }

    /// <summary>Mean folded onset strength on a grid's beats (±3 ms) over [from, to).</summary>
    internal static double Explained(double[] d, TimingSection grid, double from, double to)
    {
        double length = 60000 / grid.Bpm;
        double firstBeat = grid.TimeMs + Math.Ceiling((from - grid.TimeMs) / length) * length;
        var (h, folds, a) = Fold(d, firstBeat, Math.Max(firstBeat + length, to), length);
        if (folds == 0) return 0;
        double expected = firstBeat + MultiBpmAnalyzer.PeakToOsuMs - a;
        double best = double.NegativeInfinity;
        for (int j = Math.Max(0, (int)Math.Floor(expected - 3)); j <= Math.Min(h.Length - 1, (int)Math.Ceiling(expected + 3)); j++) best = Math.Max(best, h[j]);
        return Math.Max(0, best / folds);
    }

    /// <summary>Mean folded onset strength of a section's own period at its best phase.</summary>
    private static double Own(double[] d, TimingSection section, double from, double to)
    {
        double peak = FoldPeak(d, from, Math.Max(from + 60000 / section.Bpm, to), 60000 / section.Bpm).Height;
        return peak > 0 ? peak : double.PositiveInfinity;
    }

    /// <summary>Optimal segmentation of the inter-beat intervals into constant-tempo spans (beat index ranges).</summary>
    private static List<Span> Spans(double[] t, bool[] supported, MultiBpmOptions o)
    {
        int n = t.Length - 1; // intervals
        var q = new double[n];
        var u = new double[n];
        for (int k = 0; k < n; k++) { q[k] = t[k + 1] - t[k]; u[k] = supported[k] && supported[k + 1] ? 1 : 0; }
        int m = Math.Max(2, o.MinSectionBeats - 1);
        var cuts = Segment(q, u, m, o.SectionPenalty);
        // Second pass without outlier intervals (off-beat flips, missed or doubled beats).
        foreach (var (i, j) in cuts)
        {
            double mean = Mean(q, u, i, j);
            for (int k = i; k <= j; k++) if (mean > 0 && Math.Abs(q[k] - mean) > o.IntervalOutlier * mean) u[k] = 0;
        }
        cuts = Segment(q, u, m, o.SectionPenalty);
        return cuts.Select(c =>
        {
            int supportedBeats = 0;
            for (int k = c.Item1; k <= c.Item2 + 1; k++) if (supported[k]) supportedBeats++;
            return new Span(c.Item1, c.Item2 + 1, Mean(q, u, c.Item1, c.Item2), supportedBeats / (double)(c.Item2 - c.Item1 + 2));
        }).ToList();
    }

    private static List<(int, int)> Segment(double[] q, double[] u, int m, double penalty)
    {
        int n = q.Length;
        if (n < m) return n == 0 ? [] : [(0, n - 1)];
        var su = new double[n + 1]; var sq = new double[n + 1]; var sqq = new double[n + 1];
        for (int k = 0; k < n; k++) { su[k + 1] = su[k] + u[k]; sq[k + 1] = sq[k] + u[k] * q[k]; sqq[k + 1] = sqq[k] + u[k] * q[k] * q[k]; }
        double Sse(int i, int j)
        {
            double U = su[j + 1] - su[i];
            if (U <= 0) return 0;
            double Q = sq[j + 1] - sq[i];
            return Math.Max(0, sqq[j + 1] - sqq[i] - Q * Q / U);
        }
        var best = new double[n + 1];
        var from = new int[n + 1];
        Array.Fill(best, double.PositiveInfinity);
        best[0] = 0;
        for (int j = m; j <= n; j++)
            for (int i = 0; i <= j - m; i++)
            {
                if (double.IsPositiveInfinity(best[i])) continue;
                double v = best[i] + Sse(i, j - 1) + penalty;
                if (v < best[j]) { best[j] = v; from[j] = i; }
            }
        if (double.IsPositiveInfinity(best[n])) return [(0, n - 1)];
        var cuts = new List<(int, int)>();
        for (int j = n; j > 0; j = from[j]) cuts.Add((from[j], j - 1));
        cuts.Reverse();
        return cuts;
    }

    private static double Mean(double[] q, double[] u, int i, int j)
    {
        double U = 0, Q = 0;
        for (int k = i; k <= j; k++) { U += u[k]; Q += u[k] * q[k]; }
        if (U > 0) return Q / U;
        // No supported interval: the plain median keeps the span usable as a weak one.
        var all = q[i..(j + 1)].Order().ToArray();
        return all[all.Length / 2];
    }

    /// <summary>Least squares of supported beat times on beat numbers counted with the span's mean interval.</summary>
    private static (double Length, double First) Regression(double[] t, bool[] supported, Span span, MultiBpmOptions o)
    {
        double W = 0, K = 0, KK = 0, Y = 0, KY = 0;
        for (int k = span.First; k <= span.Last; k++)
        {
            if (!supported[k]) continue;
            double number = Math.Round((t[k] - t[span.First]) / span.Interval), y = t[k] - t[span.First];
            W++; K += number; KK += number * number; Y += y; KY += number * y;
        }
        double det = W * KK - K * K;
        if (W < 3 || det <= 1e-9) return (span.Interval, t[span.First]);
        double slope = (W * KY - K * Y) / det;
        double intercept = (Y - slope * K) / W;
        if (!(slope > 0) || Math.Abs(slope / span.Interval - 1) > o.IntervalOutlier) return (span.Interval, t[span.First]);
        return (slope, t[span.First] + intercept);
    }

    /// <summary>Whole or half BPM when the difference drifts less than <see cref="MultiBpmOptions.SnapDriftMs"/> over the span.</summary>
    private static double Snap(double length, double spanMs, MultiBpmOptions o)
    {
        double bpm = 60000 / length, beats = Math.Max(1, spanMs / length);
        foreach (double candidate in new[] { Math.Round(bpm), Math.Round(bpm * 2) / 2 })
            if (beats * Math.Abs(60000 / candidate - length) <= o.SnapDriftMs) return 60000 / candidate;
        return 60000 / Math.Round(bpm, 3, MidpointRounding.AwayFromZero);
    }

    /// <summary>The period (ms) whose epoch-folded onset envelope has the highest peak, near the tracked interval.</summary>
    private static double FoldPeriod(double[] d, double start, double end, double interval, MultiBpmOptions o)
    {
        double best = interval, bestPeak = double.NegativeInfinity;
        void Search(double from, double to, double step)
        {
            for (double length = from; length <= to; length += step)
            {
                double peak = FoldPeak(d, start, end, length).Height;
                if (peak > bestPeak) { bestPeak = peak; best = length; }
            }
        }
        Search(interval * (1 - o.FoldSearch), interval * (1 + o.FoldSearch), 0.05);
        double coarse = best;
        Search(coarse - 0.05, coarse + 0.05, 0.002);
        return best;
    }

    /// <summary>osu! time of a beat of the folded grid (any beat; callers pick the one they need).</summary>
    private static double FoldPhase(double[] d, double start, double end, double length) => FoldPeak(d, start, end, length).Time;

    private static (double Height, double Time) FoldPeak(double[] d, double start, double end, double length)
    {
        var (h, folds, a) = Fold(d, start, end, length);
        if (folds == 0) return (double.NegativeInfinity, start);
        int peak = 1;
        for (int j = 1; j < h.Length - 1; j++) if (h[j] > h[peak]) peak = j;
        double at = MultiBpmAnalyzer.Interpolate(h, peak);
        return (h[peak] / folds, a + at - MultiBpmAnalyzer.PeakToOsuMs);
    }

    /// <summary>Epoch fold of the envelope (index = osu! time + 28.7 ms) from half a beat before <paramref name="start"/>.</summary>
    private static (double[] H, int Folds, int Start) Fold(double[] d, double start, double end, double length)
    {
        int a = Math.Max(0, (int)Math.Round(start + MultiBpmAnalyzer.PeakToOsuMs - length / 2));
        int b = Math.Min(d.Length, (int)Math.Round(end + MultiBpmAnalyzer.PeakToOsuMs + length / 2));
        int c = (int)Math.Ceiling(length) + 2;
        var h = new double[c];
        int folds = 0;
        for (int k = 0; ; k++)
        {
            int s = a + (int)Math.Round(k * length);
            if (s + c > b) break;
            for (int j = 0; j < c; j++) h[j] += d[s + j];
            folds++;
        }
        return (h, folds, a);
    }
}
