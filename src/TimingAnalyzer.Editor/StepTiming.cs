using System.Globalization;

namespace TimingAnalyzer.Editor;

/// <summary>Where to look for an onset: around <see cref="TimeMs"/>, within ±<see cref="WindowMs"/> (osu! time).</summary>
public sealed record OnsetQuery(double TimeMs, double WindowMs);

/// <summary>
/// Mapper-style step timing for short tempo steps that auto-timing cannot resolve (docs/PHASE3-STATUS.md, Yooh - RPG):
/// the user puts a red line on each step's accent; each line's BPM then follows from the gap to the next red line and
/// a whole (or 1.5) number of beats, counted against the tempo of the step before. As in the official RPG timing,
/// each step then starts exactly where the step before ends (174195 + 2 × 400 = 174995).
/// </summary>
public static class StepTiming
{
    /// <summary>Beats a step may hold: 1, 1.5, then 2 to <see cref="MaxBeats"/> (halves above 2 would make 4 vs 4.5 ambiguous).</summary>
    public static IReadOnlyList<double> BeatCounts { get; } = [1, 1.5, .. Enumerable.Range(2, (int)MaxBeats - 1).Select(n => (double)n)];

    /// <summary>
    /// A longer gap is a section, not a step: its line keeps its BPM, so a box selection that also catches the next
    /// section's first line (Yooh - RPG 178775, 72 beats to the next line) does not get that section's BPM wrong.
    /// </summary>
    public const double MaxBeats = 16;

    /// <summary>
    /// A step's BPM is rounded to a multiple of 5 when that moves the step's end by at most this (ms). Mappers' ramp
    /// steps are multiples of 5 (all 25 of the official RPG timing), and the onsets do not follow a chained ramp
    /// exactly: snapped RPG lines sit up to 9 ms off the official chain (178775 against 174195-176775). A 1-beat step
    /// is therefore always a multiple of 5; a long step is measured finely enough to keep its own BPM.
    /// </summary>
    public const double RoundFiveMs = 10;

    /// <summary>Otherwise rounded to a whole BPM within this (ms), else kept to osu!'s 3 decimals.</summary>
    public const double RoundWholeMs = 4;

    public sealed record Step(int Index, double Bpm, double Beats);

    /// <summary>The steps found, and the red lines with their BPMs set and the chained lines moved.</summary>
    public sealed record Result(IReadOnlyList<Step> Steps, IReadOnlyList<TimingPoint> Lines, int Moved);

    /// <summary>
    /// For each selected red line with a next red line at most <see cref="MaxBeats"/> away: the beat count nearest
    /// the previous step's beat length, and the BPM that fills the gap (rounded within <see cref="RoundFiveMs"/> or
    /// <see cref="RoundWholeMs"/>). The
    /// first selected line of a run counts against the red line before it (or itself) and stays put; each selected
    /// line after it moves onto the end of the step before (<see cref="Truncate"/>). Every step is measured between
    /// the lines as placed.
    /// </summary>
    public static Result FromSpacing(IReadOnlyList<TimingPoint> reds, IEnumerable<int> selected)
    {
        var chosen = selected.Where(i => i >= 0 && i < reds.Count).ToHashSet();
        var lines = reds.ToArray();
        var steps = new List<Step>();
        int previous = -2, moved = 0;
        double guess = 0;
        foreach (int i in chosen.Where(i => i + 1 < lines.Length).Order())
        {
            if (i != previous + 1) guess = i > 0 ? lines[i - 1].BeatLength : lines[i].BeatLength;
            // Measured between the lines as placed, not from the chained position: a step's rounding (up to
            // RoundFiveMs) would otherwise carry into the next step's measurement (RPG 117292 came out 150, not 155).
            double gap = reds[i + 1].Time - reds[i].Time;
            if (!(gap > 0) || gap / guess > MaxBeats + 0.5) continue;
            double beats = BeatCounts.MinBy(n => Math.Abs(gap / n - guess));
            double bpm = Round(beats * 60000 / gap, beats, gap);
            steps.Add(new Step(i, bpm, beats));
            lines[i] = lines[i] with { BeatLength = 60000 / bpm };
            if (chosen.Contains(i + 1))
            {
                double end = Truncate(lines[i].Time + beats * lines[i].BeatLength);
                if (end != lines[i + 1].Time) moved++;
                lines[i + 1] = lines[i + 1] with { Time = end };
            }
            guess = lines[i].BeatLength;
            previous = i;
        }
        return new(steps, lines, moved);
    }

    /// <summary>
    /// Whole ms, truncated as the osu! editor places a line on a tick: all 25 chained lines of the official Yooh - RPG
    /// timing match truncation, and 5 of them differ from rounding (218047 + 4 × 521.739 = 220133.96 → 220133).
    /// </summary>
    private static double Truncate(double time) => Math.Floor(time + 1e-6);

    private static double Round(double bpm, double beats, double gap)
    {
        foreach (var (candidate, within) in new[] { (Math.Round(bpm / 5) * 5, RoundFiveMs), (Math.Round(bpm), RoundWholeMs) })
            if (candidate > 0 && Math.Abs(beats * 60000 / candidate - gap) <= within) return candidate;
        return OsuPrecision.Bpm(bpm);
    }

    public static string Describe(IEnumerable<Step> steps) => string.Join(", ", steps.Select(s =>
        OsuPrecision.FormatBpm(s.Bpm) + " (" + s.Beats.ToString("0.#", CultureInfo.InvariantCulture) + (s.Beats == 1 ? " beat)" : " beats)")));
}
