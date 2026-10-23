namespace TimingAnalyzer.Core;

/// <summary>
/// <para>Where the first bar starts (docs/PHASE1-STATUS.md E43). Auto-time finds a grid's phase by folding the onsets at the
/// beat length, and the original keeps the first fold position after 35 ms, so the first red line can sit on any beat of
/// the music's first bar, or a beat before the music begins. A mapper puts the first red line on a bar's first beat (the
/// "big tick"), so after Auto-time the line had to be moved back or forward by whole beats by hand.</para>
/// <para>The beat model's second head predicts mappers' bar starts (downbeats). On the 143 held-out reference maps with a
/// 4/4 first section of at least 24 beats (<c>timing-eval bar-start</c>), its best phase of the map's own grid is the map's
/// first red line's phase in 133 (93 %); moving the Auto-time offset (the map's grid folded into 35 ms + one beat) to the
/// earliest supported beat on that phase gives the map's first red line itself in 91 (64 %), against 22 (15 %) for the offset
/// as it was. The rest differ by whole bars (the mapper started a bar or two later or earlier than the first beat with onsets).</para>
/// </summary>
public static class BarStart
{
    public const int BeatsPerBar = 4;
    /// <summary>The model's frame rate (<see cref="BeatModelTiming.Fps"/>).</summary>
    public const double Fps = BeatModelTiming.Fps;
    /// <summary>A beat has support when an onset peak within ±25 ms reaches this share of the median beat peak.</summary>
    public const double SupportRatio = 0.2;
    /// <summary>The first section must have this many beats left after the move; shorter ones are too short to judge.</summary>
    public const int MinBeats = 16;
    /// <summary>The preferred phase needs this mean downbeat probability, and to beat the next phase by this factor.</summary>
    public const double MinDownbeat = 0.1, MinMargin = 1.02;
    /// <summary>Beats inspected for the median onset peak, and for the bar phase.</summary>
    private const int MedianBeats = 48, PhaseBeats = 256;

    /// <summary>
    /// Whole beats to move the first red line (negative: earlier) so that it starts a bar; 0 leaves it. The line goes to the
    /// earliest beat of its grid, not before time 0, that has onset support and falls on the downbeat phase the model's
    /// <paramref name="downbeat"/> activation (per 10 ms frame) prefers over the section's beats.
    /// </summary>
    /// <param name="envelope">The onset envelope (1 sample per ms).</param>
    /// <param name="first">The first red line.</param>
    /// <param name="sectionEndMs">Where its section ends: the next red line, or the song's end.</param>
    public static int BeatsToBarStart(double[] envelope, float[] downbeat, TimingSection first, double sectionEndMs)
    {
        double length = 60000 / first.Bpm;
        if (!(length > 0) || (sectionEndMs - first.TimeMs) / length < MinBeats) return 0;
        int kMin = (int)Math.Ceiling(-first.TimeMs / length);
        var ks = new List<int>();
        for (int k = kMin; first.TimeMs + k * length < sectionEndMs - length; k++) ks.Add(k);
        if (ks.Count < MinBeats) return 0;

        double Support(int k)
        {
            int j = (int)Math.Round(first.TimeMs + k * length + MultiBpmAnalyzer.PeakToOsuMs);
            double peak = 0;
            for (int q = Math.Max(0, j - 25); q <= j + 25 && q < envelope.Length; q++) peak = Math.Max(peak, envelope[q]);
            return peak;
        }
        double Down(int k)
        {
            int f = (int)Math.Round((first.TimeMs + k * length) / (1000 / Fps));
            double peak = 0;
            for (int q = Math.Max(0, f - 4); q <= f + 4 && q < downbeat.Length; q++) peak = Math.Max(peak, downbeat[q]);
            return peak;
        }

        var early = ks.Where(k => k >= 0).Take(MedianBeats).Select(Support).Order().ToList();
        double median = early.Count > 0 ? early[early.Count / 2] : 0;
        if (!(median > 0)) return 0;
        int supported = ks.FirstOrDefault(k => Support(k) >= SupportRatio * median, int.MinValue);
        if (supported == int.MinValue) return 0;

        var score = new double[BeatsPerBar];
        var count = new int[BeatsPerBar];
        foreach (int k in ks.Where(k => k >= supported).Take(PhaseBeats))
        {
            int phase = ((k % BeatsPerBar) + BeatsPerBar) % BeatsPerBar;
            score[phase] += Down(k);
            count[phase]++;
        }
        int best = 0;
        for (int p = 1; p < BeatsPerBar; p++)
            if (count[p] > 0 && (count[best] == 0 || score[p] / count[p] > score[best] / count[best])) best = p;
        if (count[best] == 0) return 0;
        // No evidence: the downbeat head is silent, or two phases tie. (Wrong phases on the reference maps had a margin of
        // 1.07-1.28, right ones as little as 1.04, so a larger margin would not have told them apart.)
        double bestMean = score[best] / count[best];
        double second = Enumerable.Range(0, BeatsPerBar).Where(p => p != best && count[p] > 0).Select(p => score[p] / count[p]).DefaultIfEmpty(0).Max();
        if (bestMean < MinDownbeat || bestMean < MinMargin * second) return 0;

        int target = supported;
        while (((target % BeatsPerBar) + BeatsPerBar) % BeatsPerBar != best) target++;
        if ((sectionEndMs - (first.TimeMs + target * length)) / length < MinBeats) return 0;
        return target;
    }

    /// <summary>The first red line moved by <paramref name="beats"/> whole beats along its own grid.</summary>
    public static TimingSection Move(TimingSection first, int beats) => first with { TimeMs = first.TimeMs + beats * 60000 / first.Bpm };
}
