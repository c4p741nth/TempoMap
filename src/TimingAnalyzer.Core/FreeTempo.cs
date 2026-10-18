namespace TimingAnalyzer.Core;

/// <summary>
/// Free tempo (Phase 3, option A): red lines that follow a drifting tempo, for live or old recordings whose maps put
/// a red line on many beats. It never removes or moves the section red lines; it only adds a line where several
/// tracked beats in a row drift the same way off the current grid at a steady pace, so a ramp or a jump between
/// confident sections is left alone (a jump changes the interval at once; an off-beat flip moves half a beat).
/// </summary>
internal static class FreeTempoLines
{
    public static IReadOnlyList<TimingSection> Add(double[] d, IReadOnlyList<TimingSection> sections, List<TrackedBeat> beats, MultiBpmOptions o)
    {
        var weights = beats.Where(b => b.Weight > 0).Select(b => b.Weight).Order().ToArray();
        if (weights.Length < o.DriftBeats + 1) return sections;
        double floor = o.SupportRatio * weights[weights.Length / 2];
        var result = sections.ToList();
        var grid = result[0];
        int next = 1; // Index of the next section red line in `result`.
        // A drift starts from a beat on the grid and moves away a little each beat; a phase flip (off-beat, quarter
        // beat) jumps away in one beat and is ignored. `start` = first beat of the current drift, -1 = none.
        int start = -1, run = 0;
        double previousError = double.NaN;
        for (int k = 0; k < beats.Count; k++)
        {
            var beat = beats[k];
            while (next < result.Count && result[next].TimeMs <= beat.TimeMs + 1) { grid = result[next]; next++; run = 0; start = -1; previousError = double.NaN; }
            if (beat.Weight <= 0 || beat.Weight < floor || beat.TimeMs < grid.TimeMs) { run = 0; start = -1; previousError = double.NaN; continue; }
            double length = 60000 / grid.Bpm;
            double error = beat.TimeMs - (grid.TimeMs + Math.Round((beat.TimeMs - grid.TimeMs) / length) * length);
            double step = error - previousError; // NaN when the previous beat was unsupported.
            bool small = Math.Abs(step) <= o.DriftMaxChange * length;
            if (run == 0)
            {
                // Begin only from a beat on the grid, with the first step already leaving it gently.
                if (Math.Abs(previousError) < o.DriftMs && small && Math.Abs(step) > 0.5) { run = 1; start = k - 1; }
            }
            else if (small && Math.Abs(error) >= Math.Abs(previousError) - 1 && (Math.Sign(error) == Math.Sign(previousError) || Math.Abs(previousError) < o.DriftMs))
                run++; // Still moving away from the grid (or holding), on the same side.
            else { run = 0; start = -1; }
            previousError = error;
            if (run == 0 || run < o.DriftBeats || Math.Abs(error) < o.DriftMs) continue;
            // A steady drift: fit a red line through the drifting beats and keep it only if it explains the next
            // stretch at least as well as the grid it replaces (an envelope fold, as the section merges use).
            var line = Fit(beats, start, k);
            if (line is null || line.TimeMs <= grid.TimeMs + length / 2) { run = 0; start = -1; continue; }
            double until = next < result.Count ? result[next].TimeMs : line.TimeMs + 16 * 60000 / line.Bpm;
            until = Math.Min(until, line.TimeMs + 16 * 60000 / line.Bpm);
            double mine = SectionBuilder.Explained(d, line, line.TimeMs, until);
            double theirs = SectionBuilder.Explained(d, grid, line.TimeMs, until);
            if (mine >= o.DriftFoldRatio * theirs && mine > 0)
            {
                result.Insert(next, line);
                grid = line;
                next++;
                previousError = beat.TimeMs - (line.TimeMs + Math.Round((beat.TimeMs - line.TimeMs) / (60000 / line.Bpm)) * 60000 / line.Bpm);
            }
            run = 0; start = -1;
        }
        return result;
    }

    /// <summary>Least-squares grid through beats <paramref name="from"/>..<paramref name="to"/> (one beat apart), starting at the first.</summary>
    private static TimingSection? Fit(List<TrackedBeat> beats, int from, int to)
    {
        int n = to - from + 1;
        if (n < 2) return null;
        double sx = 0, sy = 0, sxx = 0, sxy = 0;
        for (int i = 0; i < n; i++)
        {
            double y = beats[from + i].TimeMs;
            sx += i; sy += y; sxx += i * i; sxy += i * y;
        }
        double den = n * sxx - sx * sx;
        if (den <= 0) return null;
        double slope = (n * sxy - sx * sy) / den, intercept = (sy - slope * sx) / n;
        return slope > 0 ? new TimingSection(intercept, 60000 / slope) : null;
    }
}
