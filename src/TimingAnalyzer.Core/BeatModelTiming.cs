namespace TimingAnalyzer.Core;

/// <summary>
/// The beat model's part of Auto-time (docs/PHASE4-STATUS.md N4): its beats as red lines, and the per-song choice
/// between those and the multi-BPM analyzer's red lines. The model itself runs in App (ONNX Runtime); Core takes its
/// beat activation, so Core stays free of packages.
/// </summary>
public static class BeatModelTiming
{
    public const int Fps = 100;
    /// <summary>The checkpoint's threshold (tools/beatnet/train.py picks it on the validation songs).</summary>
    public const double Threshold = 0.5;

    /// <summary>Beat times (osu! ms) from the model's per-frame beat probability: peaks above the threshold, 70 ms
    /// apart, each placed by a parabola through its neighbours (tools/beatnet/infer.py).</summary>
    public static double[] Beats(float[] probability)
    {
        var frames = new List<int>();
        for (int i = 1; i + 1 < probability.Length; i++)
        {
            if (!(probability[i] > probability[i - 1] && probability[i] >= probability[i + 1] && probability[i] > Threshold)) continue;
            if (frames.Count > 0 && i - frames[^1] < 7) { if (probability[i] > probability[frames[^1]]) frames[^1] = i; }
            else frames.Add(i);
        }
        return frames.Select(f =>
        {
            double a = probability[Math.Max(f - 1, 0)], b = probability[f], c = probability[Math.Min(f + 1, probability.Length - 1)];
            double den = a - 2 * b + c, off = den < 0 ? 0.5 * (a - c) / den : 0;
            return (f + off) * 1000.0 / Fps;
        }).ToArray();
    }

    /// <summary>
    /// The model's beats as red lines: a new line where the beat spacing changes by more than 2 % from the mean
    /// interval of the current line, so a steady song is one line and a tempo change a new one (timing-eval
    /// ModelOnlyAlgorithm, the variant scored in docs/PHASE4-STATUS.md).
    /// </summary>
    public static IReadOnlyList<TimingSection> Lines(IReadOnlyList<double> beats)
    {
        if (beats.Count < 4) return [];
        var t = beats.Order().ToArray();
        var lines = new List<TimingSection>();
        int start = 0, count = 0;
        double sum = 0;
        for (int k = 1; k < t.Length; k++)
        {
            double gap = t[k] - t[k - 1];
            if (count >= 4 && Math.Abs(gap / (sum / count) - 1) > 0.02)
            {
                lines.Add(new TimingSection(t[start], 60000 / ((t[k - 1] - t[start]) / count)));
                start = k - 1; sum = 0; count = 0;
            }
            sum += gap; count++;
        }
        if (count > 0) lines.Add(new TimingSection(t[start], 60000 / ((t[^1] - t[start]) / count)));
        return lines;
    }

    /// <summary>
    /// Whether to use the model's red lines instead of the multi-BPM analyzer's, from signals known without the
    /// answer, tuned on the model's 126 validation songs (tools/beatnet/select.py): the two timings agree on at most
    /// half of the analyzer's beats, the model's beats sit on onset peaks at least 5 points more often, and the model's
    /// BPM spread (log of max / min) is at least 0.05. On the evaluation sets no set got worse and 7 of 10 better
    /// (E hold 0.489 → 0.544, F hold 0.557 → 0.602); 15 of 200 songs switched, 14 of them better.
    /// </summary>
    public static bool PreferModel(double[] envelope, IReadOnlyList<TimingSection> analyzer, IReadOnlyList<TimingSection> model, double songEndMs)
    {
        if (analyzer.Count == 0) return model.Count > 0;
        if (model.Count == 0) return false;
        double start = Math.Max(analyzer[0].TimeMs, model[0].TimeMs);
        var analyzerBeats = GridBeats(analyzer, start, songEndMs);
        var modelBeats = GridBeats(model, start, songEndMs);
        if (analyzerBeats.Length == 0 || modelBeats.Length == 0) return false;
        double agree = analyzerBeats.Count(t => Nearest(model, t) <= 10) / (double)analyzerBeats.Length;
        double spread = model.Count < 2 ? 0 : Math.Log(model.Max(s => s.Bpm) / model.Min(s => s.Bpm));
        return agree <= 0.5 && spread >= 0.05 && OnPeaks(envelope, modelBeats) - OnPeaks(envelope, analyzerBeats) >= 0.05;
    }

    // The two helpers below are Editor's GridAgreement.Beats and Nearest (division 1) on Core's sections, line for line,
    // so the editor decides exactly as the evaluation that chose the thresholds (timing-eval select-features).

    /// <summary>Whole beats in [start, end), with section 0 extended backwards.</summary>
    internal static double[] GridBeats(IReadOnlyList<TimingSection> reds, double start, double end)
    {
        var beats = new List<double>();
        for (int i = 0; i < reds.Count; i++)
        {
            double t0 = reds[i].TimeMs, step = 60000 / reds[i].Bpm;
            double limit = i + 1 < reds.Count ? Math.Min(end, reds[i + 1].TimeMs) : end;
            double from = i == 0 ? start : Math.Max(start, t0);
            for (double k = Math.Ceiling((from - t0) / step); ; k++)
            {
                double t = t0 + k * step;
                if (t >= limit) break;
                if (t >= start) beats.Add(t);
            }
        }
        return [.. beats];
    }

    /// <summary>Distance from <paramref name="time"/> to the nearest beat of the timing.</summary>
    internal static double Nearest(IReadOnlyList<TimingSection> reds, double time)
    {
        int s = 0;
        while (s + 1 < reds.Count && reds[s + 1].TimeMs <= time) s++;
        double t0 = reds[s].TimeMs, step = 60000 / reds[s].Bpm;
        double k = Math.Floor((time - t0) / step);
        double before = t0 + k * step, after = before + step;
        if (s + 1 < reds.Count && after >= reds[s + 1].TimeMs) after = reds[s + 1].TimeMs;
        double best = time - before <= after - time ? before - time : after - time;
        if (s > 0 && k == 0)
        {
            double prevStep = 60000 / reds[s - 1].Bpm;
            double last = reds[s - 1].TimeMs + (Math.Ceiling((t0 - reds[s - 1].TimeMs) / prevStep) - 1) * prevStep;
            if (Math.Abs(last - time) < Math.Abs(best)) best = last - time;
        }
        return Math.Abs(best);
    }

    /// <summary>Share of the beats with an envelope peak above mean + 1 sd within 10 ms (index = osu! time + 28.7).</summary>
    private static double OnPeaks(double[] d, double[] beats)
    {
        double mean = d.Average(), sd = Math.Sqrt(d.Average(x => (x - mean) * (x - mean)));
        int hits = 0;
        foreach (double t in beats)
        {
            int c = (int)Math.Round(t + MultiBpmAnalyzer.PeakToOsuMs);
            double best = double.MinValue;
            for (int j = Math.Max(0, c - 10); j <= Math.Min(d.Length - 1, c + 10); j++) best = Math.Max(best, d[j]);
            if (best > mean + sd) hits++;
        }
        return beats.Length == 0 ? 0 : hits / (double)beats.Length;
    }
}
