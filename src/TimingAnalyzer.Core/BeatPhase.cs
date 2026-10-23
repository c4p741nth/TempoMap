namespace TimingAnalyzer.Core;

/// <summary>
/// <para>The phase of a grid whose BPM is known, read from the beat model's beat head (docs/PHASE1-STATUS.md E44). The
/// original's offset is the argmax of the onset function folded at the beat length, which locks onto whatever is
/// strongest at one phase of the beat: off-beat chugs or hats can beat the kicks (a crowd of rebellion - ReCreate of the
/// Red: the user's 143 BPM from 437 ms folded half a beat off over the first 48 s, +209 ms).</para>
/// <para>The head was trained on mappers' own grids, so folded at the right period it peaks where a mapper puts the
/// beat. On 606 sections (16 beats or more, the 203 held-out reference songs, <c>timing-eval phase</c>) with the map's BPM given,
/// against the map's own grid: the original's fold is within 10 / 25 / 50 ms in 61 / 80 / 83 % (median error -2.7 ms), the
/// head's in 74 / 94 / 96 % (median -0.6 ms). On the 91 songs of the multi-BPM and ranked lists it was better at every
/// confidence level, so no fallback is needed when the model is there.</para>
/// </summary>
public static class BeatPhase
{
    /// <summary>At least this many whole beats are needed to fold.</summary>
    public const int MinBeats = 8;
    /// <summary>Sections of at least this many beats are aligned by <see cref="AlignSections"/>.</summary>
    public const int MinSectionBeats = 24;
    /// <summary>
    /// <see cref="AlignSections"/> moves a section only by this share of a beat or more: the original fold locks onto the
    /// wrong lobe (an off-beat, half a beat away) in a fifth of the sections and the head does not. Smaller moves were not
    /// kept: of the 113 evaluation songs whose largest move was under 0.05 beat, 6 scored better and 14 worse (the
    /// analyzer's start and BPM were fitted together), against 30 better and 2 worse of the 37 whose largest move was
    /// 0.35-0.5 beat (+8.3 of aligned F10 in total).
    /// </summary>
    public const double MinMoveBeats = 0.2;
    private const double FrameMs = 1000.0 / BeatModelTiming.Fps;

    /// <summary>
    /// Folded at two beats, how strong the beat head is on the second beat against the first (the stronger of the two).
    /// Near 1: the model hears a beat on every beat of the grid. Near 0: only on every second one, so the grid is at double
    /// the tempo a mapper would write. Over the 78 one-tempo answers of the evaluation sets, ratios of 0.00 / 0.06 / 0.07 /
    /// 0.09 were Rebellious exorcist (map at ½), Ragnarok (½, twice) and Dusk (½); the next lowest were 0.37 (Setsuna Trip,
    /// the map at the same BPM), 0.52 (a map at double the BPM) and 0.53 (VIVIDVELOCITY, ½); the rest 0.70 or more.
    /// Null for fewer than <see cref="MinBeats"/> beats.
    /// </summary>
    public static double? AlternateBeatRatio(float[] beat, double lengthMs, double startMs, double endMs)
    {
        if (!(lengthMs > 0) || (endMs - startMs) / lengthMs < MinBeats) return null;
        int bins = (int)Math.Ceiling(2 * lengthMs / FrameMs);
        var fold = new double[bins];
        for (double t = startMs; t + 2 * lengthMs <= endMs; t += 2 * lengthMs)
        {
            int f0 = (int)Math.Round(t / FrameMs);
            for (int j = 0; j < bins && f0 + j < beat.Length; j++) if (f0 + j >= 0) fold[j] += beat[f0 + j];
        }
        int a = Array.IndexOf(fold, fold.Max());
        double first = fold[a];
        if (!(first > 0)) return null;
        int half = (int)Math.Round(lengthMs / FrameMs);
        double second = 0;
        for (int d = -3; d <= 3; d++) second = Math.Max(second, fold[((a + half + d) % bins + bins) % bins]);
        return second / first;
    }

    /// <summary>
    /// Each section of <see cref="MinSectionBeats"/> beats or more moves to the nearest beat of the grid the beat head folds
    /// highest at its BPM (the first 1.5 s of a section can still hold the previous one's tail, so the fold starts after
    /// them), when that is <see cref="MinMoveBeats"/> of a beat or more from where it was and stays inside its neighbours.
    /// BPMs and the other sections' times are kept.
    /// </summary>
    public static IReadOnlyList<TimingSection> AlignSections(IReadOnlyList<TimingSection> sections, float[] beat, double songEndMs)
    {
        var result = sections.ToList();
        for (int i = 0; i < result.Count; i++)
        {
            double length = 60000 / result[i].Bpm, start = result[i].TimeMs, end = i + 1 < result.Count ? result[i + 1].TimeMs : songEndMs;
            if ((end - start) / length < MinSectionBeats || FirstBeat(beat, length, start + 1500, end) is not { } phase) continue;
            double moved = phase + Math.Round((start - phase) / length) * length;
            if (Math.Abs(moved - start) < MinMoveBeats * length) continue;
            if (moved <= (i > 0 ? result[i - 1].TimeMs : double.NegativeInfinity) || moved >= end - length) continue;
            result[i] = result[i] with { TimeMs = moved };
        }
        return result;
    }

    /// <summary>
    /// The time of the first beat at or after <paramref name="startMs"/> of the grid with beat length
    /// <paramref name="lengthMs"/> whose phase is where the beat probability (per 10 ms frame) folds highest over
    /// [<paramref name="startMs"/>, <paramref name="endMs"/>); null for fewer than <see cref="MinBeats"/> beats.
    /// </summary>
    public static double? FirstBeat(float[] beat, double lengthMs, double startMs, double endMs)
    {
        if (!(lengthMs > 0) || (endMs - startMs) / lengthMs < MinBeats) return null;
        int bins = (int)Math.Ceiling(lengthMs);
        var fold = new double[bins];
        int folds = 0;
        for (double t = startMs; t + lengthMs <= endMs; t += lengthMs, folds++)
            for (int j = 0; j < bins; j++)
            {
                double pos = (t + j) / FrameMs;
                int k = (int)Math.Floor(pos);
                if (k < 0 || k + 1 >= beat.Length) continue;
                double frac = pos - k;
                fold[j] += beat[k] * (1 - frac) + beat[k + 1] * frac;
            }
        if (folds == 0) return null;
        // A circular box of ±3 ms so a peak on the seam of the fold is not split between its two ends.
        int best = 0;
        double top = double.NegativeInfinity;
        for (int j = 0; j < bins; j++)
        {
            double sum = 0;
            for (int d = -3; d <= 3; d++) sum += fold[((j + d) % bins + bins) % bins];
            if (sum > top) { top = sum; best = j; }
        }
        return top > 0 ? startMs + best : null;
    }
}
