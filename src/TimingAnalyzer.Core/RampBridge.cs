namespace TimingAnalyzer.Core;

/// <summary>
/// Phase 3 P2: tempo ramps between two confident sections. At a tempo change A → B, finds where A's grid stops
/// fitting the onsets (t1) and where B's grid starts fitting (t2). When a gap is left, it searches the step
/// sequences a mapper would write: each step starts on the previous step's beat, lasts a whole number of beats
/// (<see cref="MultiBpmOptions.RampStepBeats"/>), its BPM is a whole number moving monotonically from A towards B,
/// and the last step lands on B's grid at t2. The best sequence by onset strength on its beats replaces the
/// straight A → B switch only when it explains the gap clearly better than A or B alone, or any switch between them.
/// <para>Measured motivation (MAMBO COMBO): folding each reference ramp step alone, the step's own BPM is the strongest
/// period for 4 of 6 steps, but 3 s steps are too short for the windowed tracker to follow.</para>
/// </summary>
internal static class RampBridge
{
    private const int WindowBeats = 4;

    public static IReadOnlyList<TimingSection> Fill(double[] d, IReadOnlyList<TimingSection> sections, double songEnd, MultiBpmOptions o)
    {
        if (sections.Count < 2) return sections;
        var strength = Strength(d);
        var result = new List<TimingSection> { sections[0] };
        for (int s = 1; s < sections.Count; s++)
        {
            var a = result[^1];
            var b = sections[s];
            double next = s + 1 < sections.Count ? sections[s + 1].TimeMs : songEnd;
            if (Math.Abs(b.Bpm / a.Bpm - 1) >= o.RampMinChange && Bridge(strength, a, b, next, o) is { } bridge)
            {
                foreach (var step in bridge.Steps) if (step.TimeMs > result[^1].TimeMs) result.Add(step);
                if (bridge.BStart > result[^1].TimeMs) result.Add(b with { TimeMs = bridge.BStart });
            }
            else result.Add(b);
        }
        return result;
    }

    /// <summary>Rectified onset strength at each osu! ms (envelope peak within ±3 ms), 0 outside the envelope.</summary>
    private static double[] Strength(double[] d)
    {
        var m = new double[d.Length];
        for (int t = 0; t < d.Length; t++)
        {
            int c = t + (int)Math.Round(MultiBpmAnalyzer.PeakToOsuMs);
            double best = 0;
            for (int j = Math.Max(0, c - 3); j <= Math.Min(d.Length - 1, c + 3); j++) best = Math.Max(best, d[j]);
            m[t] = best;
        }
        return m;
    }

    private static double At(double[] m, double t) => t >= 0 && t < m.Length ? m[(int)Math.Round(t)] : 0;

    /// <summary>Mean onset strength on <paramref name="count"/> beats of a grid from <paramref name="start"/>.</summary>
    private static double OnBeats(double[] m, double start, double length, int count)
    {
        double sum = 0;
        for (int k = 0; k < count; k++) sum += At(m, start + k * length);
        return sum / count;
    }

    private sealed record Result(List<TimingSection> Steps, double BStart);

    /// <summary>
    /// Onset strength on a beat minus the gap's mean strength at any position. Without the baseline every beat adds
    /// non-negative evidence, so a faster grid (more beats in the same time) wins by luck: on Yooh - RPG a 146 BPM
    /// path beat the true 150/140/130/120 ramp that way.
    /// </summary>
    private sealed class Evidence(double[] m, double baseline)
    {
        public double At(double t) => RampBridge.At(m, t) - baseline;
        public double OnBeats(double start, double length, int count) => RampBridge.OnBeats(m, start, length, count) - baseline;
    }

    private static Result? Bridge(double[] m, TimingSection a, TimingSection b, double next, MultiBpmOptions o)
    {
        double la = 60000 / a.Bpm, lb = 60000 / b.Bpm;
        // A's grid in 4-beat windows over its own range: t1 = end of the last window that still fits.
        var aStarts = new List<double>();
        var aValues = new List<double>();
        for (double x = a.TimeMs; x + WindowBeats * la <= b.TimeMs + WindowBeats * la; x += la)
        {
            aStarts.Add(x); aValues.Add(OnBeats(m, x, la, WindowBeats));
        }
        if (aValues.Count < 2 * WindowBeats) return null;
        double aLevel = Median(aValues);
        int lastGood = aValues.FindLastIndex(v => v >= o.RampGoodRatio * aLevel);
        if (lastGood < 0 || !(aLevel > 0)) return null;
        double t1 = aStarts[lastGood] + WindowBeats * la;

        // B's grid (extended back as far as A's red line) in 4-beat windows: t2 = start of the first fitting one after t1.
        double bFirst = b.TimeMs - Math.Floor((b.TimeMs - t1) / lb) * lb;
        var bValuesOwn = new List<double>();
        for (double x = b.TimeMs; x + WindowBeats * lb <= next; x += lb) bValuesOwn.Add(OnBeats(m, x, lb, WindowBeats));
        if (bValuesOwn.Count < WindowBeats) return null;
        double bLevel = Median(bValuesOwn);
        if (!(bLevel > 0)) return null;
        double t2 = double.NaN;
        for (double x = bFirst; x + WindowBeats * lb <= next; x += lb)
            if (OnBeats(m, x, lb, WindowBeats) >= o.RampGoodRatio * bLevel) { t2 = x; break; }
        if (double.IsNaN(t2)) return null;
        double gap = t2 - t1;
        if (gap < o.RampMinGapBeats * Math.Min(la, lb) || gap > o.RampMaxMs) return null;

        // The ramp may already start inside A's last fitting window (its first beat is often still on A's grid), so
        // the search begins at that window; A runs on for free, so starting early costs nothing.
        double origin = Math.Max(a.TimeMs + la, aStarts[lastGood]);
        // Symmetrically, B's first fitting window may still hold a ramp beat, so a ramp may land up to one window
        // past t2. Every candidate is scored over the same [origin, end].
        double end = t2 + WindowBeats * lb;
        while (end >= next - lb / 2 && end > t2) end -= lb;
        // Evidence is strength above the gap's mean, in units of a typical beat above it, so the per-step penalty
        // is "beats of evidence" and a beat on nothing costs instead of counting.
        double baseline = 0;
        int from0 = Math.Max(0, (int)origin), to0 = Math.Min(m.Length, (int)end);
        for (int t = from0; t < to0; t++) baseline += m[t];
        baseline /= Math.Max(1, to0 - from0);
        double unit = (aLevel + bLevel) / 2 - baseline;
        if (!(unit > 0)) return null;
        var e = new Evidence(m, baseline);
        var steps = Search(e, a.Bpm, b.Bpm, origin, end, unit, o);
        var smooth = Smooth(e, a.Bpm, b.Bpm, origin, end, unit, o);
        if (smooth is not null && (steps is null || smooth.Value.Score > steps.Value.Score)) steps = smooth;
        if (steps is null) return null;
        double bStart = steps.Value.BStart;

        // Alternatives with no ramp: A's grid, B's grid, or A then B switching at any of A's beats in the gap.
        double best = double.NegativeInfinity;
        for (double x = origin; x <= end + 1e-6; x += la)
        {
            int na = (int)Math.Round((x - origin) / la);
            double bx = b.TimeMs - Math.Floor((b.TimeMs - x) / lb) * lb;
            int nb = Math.Max(0, (int)Math.Round((end - bx) / lb));
            double score = (na > 0 ? e.OnBeats(origin, la, na) * na : 0) + (nb > 0 ? e.OnBeats(bx, lb, nb) * nb : 0);
            best = Math.Max(best, score / unit - (na > 0 && nb > 0 ? o.RampStepPenalty : 0));
        }
        if (steps.Value.Score < best + o.RampMargin || steps.Value.Lines.Count == 0) return null;
        return new Result(steps.Value.Lines, bStart);
    }

    /// <summary>
    /// Dynamic programming over (position, BPM). A may run on past t1 for free (any of its beats in the gap can start
    /// the ramp); then steps of whole-number BPM between A and B (monotone) and
    /// <see cref="MultiBpmOptions.RampStepBeats"/> beats; the ramp ends on any beat of B's grid at or before t2, B
    /// running back from t2 to there (scored too, so every candidate covers the same [t1, t2]). Positions are binned
    /// to 5 ms; each cell keeps its best path.
    /// </summary>
    private static (List<TimingSection> Lines, double Score, double BStart)? Search(Evidence e, double bpmA, double bpmB, double t1, double t2, double unit, MultiBpmOptions o)
    {
        int dir = Math.Sign(bpmB - bpmA);
        double lo = Math.Min(bpmA, bpmB) * (1 - o.RampSlack), hi = Math.Max(bpmA, bpmB) * (1 + o.RampSlack);
        var bpms = new List<double>();
        for (double v = Math.Ceiling(lo); v <= Math.Floor(hi); v++) bpms.Add(v);
        if (bpms.Count == 0) return null;
        const double bin = 5;
        int bins = (int)Math.Ceiling((t2 + o.RampLandMs - t1) / bin) + 1;
        // Cell: best score at (position bin, BPM index); the exact position and the path back.
        var score = new double[bins, bpms.Count + 1];
        var pos = new double[bins, bpms.Count + 1];
        var from = new (int Bin, int Bpm)[bins, bpms.Count + 1];
        for (int i = 0; i < bins; i++) for (int j = 0; j <= bpms.Count; j++) score[i, j] = double.NegativeInfinity;
        int start = bpms.Count; // the virtual "A" state: A's own BPM, no red line paid yet
        double la = 60000 / bpmA, lb = 60000 / bpmB;
        // A continues for free over any of its beats in the gap, collecting their evidence.
        double run = 0;
        for (int k = 0; t1 + k * la < t2; k++)
        {
            int i = (int)Math.Round(k * la / bin);
            if (i >= bins) break;
            score[i, start] = run; pos[i, start] = t1 + k * la; from[i, start] = (-1, -1);
            run += e.At(t1 + k * la) / unit;
        }
        double Bpm(int j) => j == start ? bpmA : bpms[j];
        // Evidence of B's grid run back from t2 to a landing beat.
        double BRun(double landing)
        {
            double sum = 0;
            for (double x = landing; x < t2 - 1; x += lb) sum += e.At(x) / unit;
            return sum;
        }
        for (int i = 0; i < bins; i++)
            for (int j = 0; j <= bpms.Count; j++)
            {
                if (double.IsNegativeInfinity(score[i, j])) continue;
                double current = Bpm(j), p = pos[i, j];
                for (int k = 0; k < bpms.Count; k++)
                {
                    double v = bpms[k];
                    // Monotone from A towards B (a step may keep the current BPM).
                    if (dir < 0 ? v > current + 1e-9 : dir > 0 ? v < current - 1e-9 : false) continue;
                    double length = 60000 / v;
                    foreach (int n in o.RampStepBeats)
                    {
                        double end = p + n * length;
                        if (end > t2 + o.RampLandMs) continue;
                        double gain = e.OnBeats(p, length, n) * n / unit - (Math.Abs(v - current) > 1e-9 || j == start ? o.RampStepPenalty : 0);
                        int ei = (int)Math.Round((end - t1) / bin);
                        if (ei >= bins || ei <= i) continue;
                        double total = score[i, j] + gain;
                        if (total > score[ei, k]) { score[ei, k] = total; pos[ei, k] = end; from[ei, k] = (i, j); }
                    }
                }
            }
        // The best path ending on a beat of B's grid (t2 or earlier), plus B's beats from there to t2.
        double bestScore = double.NegativeInfinity, bestLanding = t2; (int Bin, int Bpm) bestCell = (-1, -1);
        for (int i = 0; i < bins; i++)
            for (int j = 0; j < bpms.Count; j++)
            {
                if (double.IsNegativeInfinity(score[i, j])) continue;
                double back = Math.Round((t2 - pos[i, j]) / lb), landing = t2 - back * lb;
                if (back < 0 || Math.Abs(pos[i, j] - landing) > o.RampLandMs) continue;
                // B's BPM may be passed only towards B (the monotone search already keeps steps between A and B).
                double total = score[i, j] + BRun(landing);
                if (total > bestScore) { bestScore = total; bestCell = (i, j); bestLanding = landing; }
            }
        if (bestCell.Bin < 0) return null;
        // Walk back: a red line wherever a step starts with a new BPM.
        var lines = new List<TimingSection>();
        for (var cell = bestCell; from[cell.Bin, cell.Bpm].Bin >= 0; cell = from[cell.Bin, cell.Bpm])
        {
            var prev = from[cell.Bin, cell.Bpm];
            double stepBpm = Bpm(cell.Bpm), prevBpm = Bpm(prev.Bpm);
            if (prev.Bpm == start || Math.Abs(stepBpm - prevBpm) > 1e-9) lines.Add(new TimingSection(pos[prev.Bin, prev.Bpm], stepBpm));
        }
        lines.Reverse();
        // A first step at A's own BPM is just A continuing; a last one at B's BPM is B starting early.
        if (lines.Count > 0 && Math.Abs(lines[0].Bpm - bpmA) < 1e-9) lines.RemoveAt(0);
        if (lines.Count > 0 && Math.Abs(lines[^1].Bpm - bpmB) < 1e-9)
        {
            bestLanding = t2 - Math.Round((t2 - lines[^1].TimeMs) / lb) * lb; // its beats are B's grid (±RampLandMs)
            lines.RemoveAt(lines.Count - 1);
        }
        return (lines, bestScore, bestLanding);
    }

    /// <summary>
    /// A continuous accelerando or ritardando as mappers write it: BPM linear in the beat index from A to B, one red
    /// line every <see cref="MultiBpmOptions.RampSmoothBeats"/> beats. Its only free choices are the A beat it starts
    /// on and its length, and it must land on B's grid, so it needs little onset evidence to be placed.
    /// </summary>
    private static (List<TimingSection> Lines, double Score, double BStart)? Smooth(Evidence e, double bpmA, double bpmB, double t1, double t2, double unit, MultiBpmOptions o)
    {
        double la = 60000 / bpmA, lb = 60000 / bpmB;
        (List<TimingSection> Lines, double Score, double BStart)? best = null;
        foreach (int s in o.RampSmoothBeats)
        {
            double aRun = 0;
            for (double x0 = t1; x0 < t2; x0 += la)
            {
                for (int groups = 2; ; groups++)
                {
                    double p = x0, evidence = 0;
                    var lines = new List<TimingSection>();
                    for (int g = 0; g < groups; g++)
                    {
                        double bpm = bpmA + (bpmB - bpmA) * (g + 1) / (groups + 1.0);
                        lines.Add(new TimingSection(p, bpm));
                        for (int k = 0; k < s; k++) { evidence += e.At(p) / unit; p += 60000 / bpm; }
                    }
                    if (p > t2 + o.RampLandMs) break;
                    double back = Math.Round((t2 - p) / lb), landing = t2 - back * lb;
                    if (back < 0 || Math.Abs(p - landing) > o.RampLandMs) continue;
                    double bRun = 0;
                    for (double x = landing; x < t2 - 1; x += lb) bRun += e.At(x) / unit;
                    double total = aRun + evidence + bRun - o.RampLinePenalty * groups;
                    if (best is null || total > best.Value.Score) best = (lines, total, landing);
                }
                aRun += e.At(x0) / unit;
            }
        }
        return best;
    }

    private static double Median(List<double> values)
    {
        var sorted = values.Order().ToArray();
        return sorted.Length == 0 ? 0 : sorted[sorted.Length / 2];
    }
}
