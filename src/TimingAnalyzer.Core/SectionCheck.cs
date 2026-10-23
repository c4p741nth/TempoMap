namespace TimingAnalyzer.Core;

/// <summary>What the onsets say about one red line's section (the editor's "check" marks).</summary>
/// <param name="Fit">Onset strength at the line's beats, minus the section's mean, over its standard deviation.</param>
/// <param name="BestRatio">The tempo ratio (⅔, ¾, 4/3, 1.5, 2) whose best-phase grid fits clearly better than the line's
/// BPM at its best phase, or null.</param>
/// <param name="BestRatioFit">That grid's fit.</param>
/// <param name="ShiftMs">The move (−½ to +½ beat) that puts the line's grid at its best phase, when that fits clearly
/// better, or null.</param>
/// <param name="ShiftFit">That grid's fit.</param>
public sealed record SectionReport(double Fit, double? BestRatio, double BestRatioFit, double? ShiftMs, double ShiftFit)
{
    /// <summary>The line's beats sit on no more onset than the section's average: worth a listen.</summary>
    public const double WeakFit = 0;
    public bool Weak => Fit < WeakFit;
}

/// <summary>
/// <para>Scores each red line's grid against the onset envelope, so the editor can point at the sections worth checking by
/// ear instead of the user listening to the whole song. Three findings: the grid sits on few onsets (weak), another
/// tempo ratio fits clearly better, or the same tempo shifted by part of a beat fits clearly better. Evidence is
/// baseline-subtracted (onset strength at the beats minus the section's mean), so faster grids get no free credit.
/// A tempo ratio is judged against the line's BPM at its best phase, so an offset error is not taken for a tempo one
/// (a crowd of rebellion - Ill, the map's 131722 @ 142: at the line −0.46, ⅔ tempo 1.55, but 142 shifted 44 ms
/// earlier 2.0). Half tempo is never offered: on Camellia - Parallel Universe Shifter (2026-10-18) every steady
/// section's half tempo scored above the map's BPM, because every second beat is the stronger one in most music.</para>
/// </summary>
public static class SectionCheck
{
    /// <summary>
    /// How much better (in the fit's units) another grid must be to be reported. Chosen on mappers' own timing, where
    /// every mark is a false alarm or a map error (timing-eval check, 1 059 sections of the multi-BPM, osu!mania and
    /// ranked-pick sets): margin 0.5 / any move ≥ 5 ms / weak below 0.5 marked 0.57-0.80 per section; margin 1.0 /
    /// moves ≥ 25 ms / weak below 0 marks 0.09-0.25 (1 of the 30 single-BPM songs) and still marks Ill's 131 722
    /// (−45 ms) and Parallel Universe Shifter's 283 730 @ 250 (+59 ms, own grid 0.19 vs 1.28 shifted).
    /// </summary>
    public const double Margin = 1.0;
    /// <summary>Sections shorter than this many beats are ramp steps: too short to judge.</summary>
    public const int MinBeats = 8;
    /// <summary>
    /// Each beat takes the strongest onset within this many ms. One sample per beat made the fit jump with 1 ms moves
    /// (Parallel Universe Shifter 221 452 @ 100: 0.98 at the line, 1.77 a millisecond earlier).
    /// </summary>
    public const int ToleranceMs = 3;
    /// <summary>
    /// Smaller moves are left out: on mappers' own timing (timing-eval check, 2026-10-18) 214 of the 424 moves
    /// reported with no minimum were 5-14 ms, a constant offset convention rather than a misplaced line.
    /// </summary>
    public const double MinShiftMs = 25;
    public static IReadOnlyList<double> Ratios { get; } = [2.0 / 3, 0.75, 4.0 / 3, 1.5, 2];

    /// <summary>One report per red line (null for a section too short to judge). Times in osu! ms.</summary>
    public static IReadOnlyList<SectionReport?> Check(double[] envelope, IReadOnlyList<TimingSection> reds, double songEndMs)
    {
        var smooth = Smooth(envelope);
        var result = new List<SectionReport?>();
        for (int i = 0; i < reds.Count; i++)
        {
            double a = Math.Max(0, reds[i].TimeMs), b = i + 1 < reds.Count ? reds[i + 1].TimeMs : songEndMs;
            double length = 60000 / reds[i].Bpm;
            if ((b - a) / length < MinBeats) { result.Add(null); continue; }
            var (mean, sd) = Stats(smooth, a, b);
            if (sd <= 0) { result.Add(null); continue; }
            double Fit(double bpm, double phase) => (Mean(smooth, 60000 / bpm, phase, a, b) - mean) / sd;
            double own = Fit(reds[i].Bpm, reds[i].TimeMs);
            // The line's BPM at every phase: the best one, as a move of at most half a beat either way.
            double bestPhase = reds[i].TimeMs, shiftFit = own;
            for (double phase = a; phase < a + length; phase += 1)
            {
                double fit = Fit(reds[i].Bpm, phase);
                if (fit > shiftFit) { shiftFit = fit; bestPhase = phase; }
            }
            double move = ((bestPhase - reds[i].TimeMs) % length + length) % length;
            if (move > length / 2) move -= length;
            double? shift = shiftFit >= own + Margin && Math.Abs(move) >= MinShiftMs ? move : null;
            double? ratio = null;
            double ratioFit = double.NegativeInfinity;
            foreach (double r in Ratios)
            {
                double fit = BestPhase(smooth, reds[i].Bpm * r, a, b, mean, sd);
                if (fit > ratioFit) { ratioFit = fit; ratio = r; }
            }
            if (ratioFit < Math.Max(own, shiftFit) + Margin) ratio = null;
            result.Add(new SectionReport(own, ratio, ratioFit, shift, shiftFit));
        }
        return result;
    }

    // ±2 ms triangle smoothing at osu! time (envelope index − PeakToOsuMs), so a 1-2 ms phase error still finds the peak.
    private static double[] Smooth(double[] d)
    {
        var s = new double[d.Length];
        for (int j = 2; j + 2 < d.Length; j++) s[j] = (d[j - 2] + 2 * d[j - 1] + 3 * d[j] + 2 * d[j + 1] + d[j + 2]) / 9;
        return s;
    }

    private static int Index(double osuMs) => (int)Math.Round(osuMs + MultiBpmAnalyzer.PeakToOsuMs);

    private static (double Mean, double Sd) Stats(double[] s, double a, double b)
    {
        int lo = Math.Clamp(Index(a), 0, s.Length), hi = Math.Clamp(Index(b), lo, s.Length);
        if (hi - lo < 2) return (0, 0);
        double sum = 0, sq = 0;
        for (int j = lo; j < hi; j++) { sum += s[j]; sq += s[j] * s[j]; }
        double mean = sum / (hi - lo);
        return (mean, Math.Sqrt(Math.Max(0, sq / (hi - lo) - mean * mean)));
    }

    private static double Mean(double[] s, double length, double phase, double a, double b)
    {
        double sum = 0;
        int n = 0;
        for (double t = phase + Math.Ceiling((a - phase) / length) * length; t < b; t += length)
        {
            int j = Index(t);
            if (j - ToleranceMs < 0 || j + ToleranceMs >= s.Length) continue;
            double peak = s[j];
            for (int k = j - ToleranceMs; k <= j + ToleranceMs; k++) peak = Math.Max(peak, s[k]);
            sum += peak; n++;
        }
        return n == 0 ? 0 : sum / n;
    }

    private static double BestPhase(double[] s, double bpm, double a, double b, double mean, double sd)
    {
        double length = 60000 / bpm, best = double.NegativeInfinity;
        for (double phase = a; phase < a + length; phase += 1) best = Math.Max(best, (Mean(s, length, phase, a, b) - mean) / sd);
        return best;
    }
}
