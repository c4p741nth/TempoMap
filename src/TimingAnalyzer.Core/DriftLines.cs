namespace TimingAnalyzer.Core;

/// <summary>
/// Drift mode (Phase 3, opt-in, the user's item 4 on 2026-10-18): one red line every N beats that follows a tempo
/// drifting slowly through the whole song (Billie Jean: 116.9-117.5 BPM, 71 ms off one grid). The tracked beats are
/// fitted by a continuous grid whose beat length is constant within each group of N beats: beat k of group g sits at
/// t0 + Σ_{h&lt;g} N·L_h + (k − gN)·L_g. Continuity means no phase jump at a red line; only beats with an onset
/// count, and beats far off the fit (tracker off-beat flips) are dropped and the fit repeated. Not for songs with
/// tempo changes: there the multi-BPM analyzer's sections are the answer. With <see cref="EveryBeat"/> the lines sit
/// on the tracked beats themselves, for drums played live rather than a tempo that only drifts.
/// </summary>
public static class DriftLines
{
    /// <summary>Beat-length change between neighbouring groups costs this per ms², against ms² of beat residual.</summary>
    public const double Smoothness = 4;

    /// <summary>
    /// <c>beatsPerLine</c> for a red line on every tracked beat (2026-10-18, user: "a red line on every drum hit" for
    /// osu!mania). Billie Jean's drums are played: beat-to-beat intervals spread 6 ms (p5-p95 501-523 ms), so even the
    /// best line every 16 beats fitted straight to the hits is up to 25 ms off one, every 2 beats up to 9 ms, while the
    /// tracked beats sit within 0.6 ms (median) of the template-matched kick and snare.
    /// </summary>
    public const int EveryBeat = 1;

    /// <summary>Beats per group of the grid that decides which tracked beats a per-beat line may trust.</summary>
    public const int RepairBeats = 4;

    public static IReadOnlyList<TimingSection> Fit(double[] d, int beatsPerLine, MultiBpmOptions? options = null)
    {
        var o = options ?? new MultiBpmOptions();
        var original = Analyzer.AnalyzeEnvelope(d);
        double? anchor = original.Bpm ?? original.ApproximateBpm;
        return Fit(MultiBpmAnalyzer.TrackBeats(d, o, anchor), beatsPerLine);
    }

    internal static IReadOnlyList<TimingSection> Fit(IReadOnlyList<TrackedBeat> beats, int beatsPerLine)
    {
        if (beatsPerLine < 1) throw new ArgumentOutOfRangeException(nameof(beatsPerLine));
        if (beats.Count < 4) return [];
        if (beatsPerLine == EveryBeat) return OnEveryBeat(beats);
        if (Robust(beats, beatsPerLine) is not { } fit) return [];
        var (x, _, groups) = fit;
        var lines = new List<TimingSection>();
        double t = x[0];
        for (int g = 0; g < groups; g++)
        {
            if (x[g + 1] > 0) lines.Add(new TimingSection(t, 60000 / x[g + 1]));
            t += beatsPerLine * x[g + 1];
        }
        return lines;
    }

    /// <summary>
    /// The continuous grid through the beats with onsets, dropping beats far off it and refitting. Returns the
    /// solution, which beats it kept, and the group count; null when no beat is left.
    /// </summary>
    private static (double[] X, bool[] Use, int Groups)? Robust(IReadOnlyList<TrackedBeat> beats, int per)
    {
        int n = beats.Count;
        int groups = Math.Max(1, (n - 2) / per + 1);
        var use = beats.Select(b => b.Weight > 0).ToArray();
        double[] x = [];
        for (int pass = 0; pass < 4; pass++)
        {
            x = Solve(beats, use, per, groups);
            if (Limit(beats, use, x, per, groups) is not { } limit) return null;
            bool changed = false;
            for (int k = 0; k < n; k++)
                if (use[k] && Math.Abs(beats[k].TimeMs - Time(x, k, per, groups)) > limit) { use[k] = false; changed = true; }
            if (!changed) break;
        }
        return (x, use, groups);
    }

    /// <summary>Beats further than this off the grid are dropped: max(20 ms, 3 × the median residual of the kept beats).</summary>
    private static double? Limit(IReadOnlyList<TrackedBeat> beats, bool[] use, double[] x, int per, int groups)
    {
        var residuals = Enumerable.Range(0, beats.Count).Where(k => use[k]).Select(k => Math.Abs(beats[k].TimeMs - Time(x, k, per, groups))).Order().ToArray();
        return residuals.Length == 0 ? null : Math.Max(20, 3 * residuals[residuals.Length / 2]);
    }

    /// <summary>
    /// A red line on every tracked beat within the drop limit of the final <see cref="RepairBeats"/>-beat grid (judged
    /// again at the end, so a beat dropped while an outlier still pulled the fit comes back); a beat off it (no onset,
    /// an off-beat flip) is spanned by one line from the kept beat before, so the beats between are spaced evenly. Lines
    /// are whole ms with the BPM of the whole-ms spacing, so each beat ends exactly on the next red line in osu!; a
    /// line with the same spacing as the one before is left out, since that line already lands on it.
    /// </summary>
    private static IReadOnlyList<TimingSection> OnEveryBeat(IReadOnlyList<TrackedBeat> beats)
    {
        if (Robust(beats, RepairBeats) is not { } fit) return [];
        var (x, use, groups) = fit;
        double limit = Limit(beats, use, x, RepairBeats, groups) ?? 0;
        var kept = Enumerable.Range(0, beats.Count)
            .Where(k => beats[k].Weight > 0 && Math.Abs(beats[k].TimeMs - Time(x, k, RepairBeats, groups)) <= limit).ToArray();
        var lines = new List<TimingSection>();
        double previous = double.NaN;
        for (int i = 0; i + 1 < kept.Length; i++)
        {
            double from = Math.Floor(beats[kept[i]].TimeMs + 0.5), to = Math.Floor(beats[kept[i + 1]].TimeMs + 0.5);
            double length = (to - from) / (kept[i + 1] - kept[i]);
            if (length <= 0 || length == previous) continue;
            lines.Add(new TimingSection(from, 60000 / length));
            previous = length;
        }
        return lines;
    }

    /// <summary>Model time of beat <paramref name="k"/>; x = [t0, L_0 .. L_{groups-1}].</summary>
    private static double Time(double[] x, int k, int per, int groups)
    {
        int g = Math.Min(k / per, groups - 1);
        double t = x[0];
        for (int h = 0; h < g; h++) t += per * x[h + 1];
        return t + (k - g * per) * x[g + 1];
    }

    /// <summary>Weighted least squares with a smoothness term, by the normal equations (groups + 1 unknowns).</summary>
    private static double[] Solve(IReadOnlyList<TrackedBeat> beats, bool[] use, int per, int groups)
    {
        int m = groups + 1;
        var a = new double[m, m];
        var b = new double[m];
        var row = new double[m];
        for (int k = 0; k < beats.Count; k++)
        {
            if (!use[k]) continue;
            Array.Clear(row);
            int g = Math.Min(k / per, groups - 1);
            row[0] = 1;
            for (int h = 0; h < g; h++) row[h + 1] = per;
            row[g + 1] = k - g * per;
            for (int i = 0; i < m; i++)
            {
                if (row[i] == 0) continue;
                b[i] += row[i] * beats[k].TimeMs;
                for (int j = 0; j < m; j++) a[i, j] += row[i] * row[j];
            }
        }
        // Neighbouring groups' beat lengths stay close; a group with no onsets takes its neighbours' tempo.
        for (int g = 1; g < groups; g++)
        {
            a[g, g] += Smoothness; a[g + 1, g + 1] += Smoothness;
            a[g, g + 1] -= Smoothness; a[g + 1, g] -= Smoothness;
        }
        // A tiny ridge towards the median tracked interval keeps the system solvable when everything is silent.
        var intervals = Enumerable.Range(1, beats.Count - 1).Select(k => beats[k].TimeMs - beats[k - 1].TimeMs).Where(v => v > 0).Order().ToArray();
        double typical = intervals.Length > 0 ? intervals[intervals.Length / 2] : 500;
        for (int g = 1; g < m; g++) { a[g, g] += 1e-6; b[g] += 1e-6 * typical; }
        a[0, 0] += 1e-9; b[0] += 1e-9 * beats[0].TimeMs;
        return Gauss(a, b);
    }

    private static double[] Gauss(double[,] a, double[] b)
    {
        int n = b.Length;
        for (int c = 0; c < n; c++)
        {
            int p = c;
            for (int r = c + 1; r < n; r++) if (Math.Abs(a[r, c]) > Math.Abs(a[p, c])) p = r;
            if (p != c)
            {
                for (int j = 0; j < n; j++) (a[c, j], a[p, j]) = (a[p, j], a[c, j]);
                (b[c], b[p]) = (b[p], b[c]);
            }
            for (int r = c + 1; r < n; r++)
            {
                double f = a[r, c] / a[c, c];
                if (f == 0) continue;
                for (int j = c; j < n; j++) a[r, j] -= f * a[c, j];
                b[r] -= f * b[c];
            }
        }
        var x = new double[n];
        for (int r = n - 1; r >= 0; r--)
        {
            double s = b[r];
            for (int j = r + 1; j < n; j++) s -= a[r, j] * x[j];
            x[r] = s / a[r, r];
        }
        return x;
    }
}
