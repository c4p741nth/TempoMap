namespace TimingAnalyzer.Editor;

/// <summary>
/// How well a predicted timing reproduces a reference timing, compared beat by beat.
/// <para>Recall side: each reference beat's distance to the nearest predicted beat (median, P90, share within
/// 5/10/20 ms). Precision side: share of predicted beats with a reference beat within 10 ms. A double-tempo
/// prediction has full recall but half precision; a half-tempo one the reverse, which
/// <see cref="HalfTempoWithin10"/> (recall against a twice-as-fine predicted grid) exposes.</para>
/// </summary>
public sealed record GridScore(int ReferenceBeats, int PredictedBeats, double MedianMs, double P90Ms,
    double Within5, double Within10, double Within20, double Precision10, double HalfTempoWithin10,
    double ShiftMs = 0, double AlignedF10 = 0)
{
    public double F10 => Harmonic(Within10, Precision10);
    internal static double Harmonic(double a, double b) => a + b == 0 ? 0 : 2 * a * b / (a + b);
}

public static class GridAgreement
{
    /// <summary>Scores from the first reference red line to <paramref name="endMs"/>. Grids extend backwards
    /// from their first red line, as osu! applies the first timing point to earlier times.</summary>
    public static GridScore Score(IReadOnlyList<TimingPoint> reference, IReadOnlyList<TimingPoint> predicted, double endMs)
    {
        var refs = BeatGrid.Sort(reference);
        var preds = BeatGrid.Sort(predicted);
        if (refs.Length == 0 || preds.Length == 0) throw new ArgumentException("Both timings need a red line.");
        double start = refs[0].Time;
        var refBeats = Beats(refs, start, endMs);
        var predBeats = Beats(preds, start, endMs);
        var errors = refBeats.Select(t => Nearest(preds, t, 1)).Order().ToArray();
        double Share(IEnumerable<double> values, double limit, int count) => count == 0 ? 0 : values.Count(e => e <= limit) / (double)count;
        // Offset conventions differ between maps (and decoders); the shape score removes one constant shift.
        double shift = Percentile(refBeats.Select(t => NearestSigned(preds, t, 1)).Order().ToArray(), 0.5);
        var aligned = preds.Select(p => p with { Time = p.Time - shift }).ToArray();
        double alignedRecall = Share(refBeats.Select(t => Nearest(aligned, t, 1)), 10, refBeats.Count);
        double alignedPrecision = Share(Beats(aligned, start, endMs).Select(t => Nearest(refs, t, 1)), 10, predBeats.Count);
        return new GridScore(refBeats.Count, predBeats.Count,
            Percentile(errors, 0.5), Percentile(errors, 0.9),
            Share(errors, 5, errors.Length), Share(errors, 10, errors.Length), Share(errors, 20, errors.Length),
            Share(predBeats.Select(t => Nearest(refs, t, 1)), 10, predBeats.Count),
            Share(refBeats.Select(t => Nearest(preds, t, 2)), 10, refBeats.Count),
            shift, GridScore.Harmonic(alignedRecall, Math.Min(1, alignedPrecision)));
    }

    /// <summary>Whole beats in [start, end), with section 0 extended backwards.</summary>
    public static List<double> Beats(IReadOnlyList<TimingPoint> reds, double start, double end)
    {
        var beats = new List<double>();
        for (int i = 0; i < reds.Count; i++)
        {
            double t0 = reds[i].Time, step = reds[i].BeatLength;
            double limit = i + 1 < reds.Count ? Math.Min(end, reds[i + 1].Time) : end;
            double from = i == 0 ? start : Math.Max(start, t0);
            for (double k = Math.Ceiling((from - t0) / step); ; k++)
            {
                double t = t0 + k * step;
                if (t >= limit) break;
                if (t >= start) beats.Add(t);
            }
        }
        return beats;
    }

    /// <summary>Distance from <paramref name="time"/> to the nearest 1/<paramref name="division"/> beat of the timing.</summary>
    public static double Nearest(IReadOnlyList<TimingPoint> reds, double time, int division) => Math.Abs(NearestSigned(reds, time, division));

    /// <summary>Nearest beat minus <paramref name="time"/> (positive: the timing's beat is later).</summary>
    public static double NearestSigned(IReadOnlyList<TimingPoint> reds, double time, int division)
    {
        int s = 0;
        while (s + 1 < reds.Count && reds[s + 1].Time <= time) s++;
        double t0 = reds[s].Time, step = reds[s].BeatLength / division;
        double k = Math.Floor((time - t0) / step);
        double before = t0 + k * step, after = before + step;
        if (s + 1 < reds.Count && after >= reds[s + 1].Time) after = reds[s + 1].Time;
        double best = time - before <= after - time ? before - time : after - time;
        if (s > 0 && k == 0)
        {
            // Just after a red line: the previous section's last beat may be closer.
            double prevStep = reds[s - 1].BeatLength / division;
            double last = reds[s - 1].Time + (Math.Ceiling((t0 - reds[s - 1].Time) / prevStep) - 1) * prevStep;
            if (Math.Abs(last - time) < Math.Abs(best)) best = last - time;
        }
        return best;
    }

    private static double Percentile(double[] sorted, double q)
    {
        if (sorted.Length == 0) return double.NaN;
        double pos = q * (sorted.Length - 1);
        int lo = (int)Math.Floor(pos), hi = (int)Math.Ceiling(pos);
        return sorted[lo] + (sorted[hi] - sorted[lo]) * (pos - lo);
    }
}
