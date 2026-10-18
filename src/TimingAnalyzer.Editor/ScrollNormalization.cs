namespace TimingAnalyzer.Editor;

/// <summary>
/// osu!mania scrolls each red line's section at its BPM against the map's main BPM; mappers cancel that with a green
/// line on every red line whose slider velocity is main BPM / BPM. Of 481 local mania maps with at least three red
/// lines off the main BPM, 295 normalise like that (279 of them to osu!'s main BPM below, 10 to a whole BPM near it),
/// 146 mix it with SV effects and 40 let the speed follow the BPM. The green line sits at the red line's time with the
/// red line's sample set and volume (14 796 of 14 835 such lines).
/// </summary>
public static class ScrollNormalization
{
    /// <summary>Slider velocity range written (osu!mania green lines go from 0.01x to 10x).</summary>
    public const double MinSv = 0.01, MaxSv = 10;

    /// <param name="Points">Every red line and the green lines, ready for <see cref="TimingClipboard.Write"/>.</param>
    /// <param name="MainBpm">The BPM the scroll speed is normalised to.</param>
    /// <param name="Reference">The BPM the map's green lines were already normalised to, or null when they were not.</param>
    /// <param name="Added">Green lines added on red lines that had none.</param>
    /// <param name="Scaled">Existing green lines whose SV changed.</param>
    /// <param name="Clamped">SVs outside <see cref="MinSv"/>-<see cref="MaxSv"/>, written at the limit.</param>
    public sealed record Result(TimingPoint[] Points, double MainBpm, double? Reference, int Added, int Scaled, int Clamped);

    /// <summary>
    /// osu!'s main beat length, as lazer's <c>Beatmap.GetMostCommonBeatLength</c> (kept to match stable's osu!mania
    /// scroll speed): the beat length (to 3 decimals) held longest, the first red line counted from 0 and the last
    /// up to <paramref name="lastTime"/>, the end of the last note; a red line after it counts nothing. Ties go to the
    /// first beat length in time.
    /// </summary>
    public static double MainBeatLength(IReadOnlyList<TimingPoint> reds, double lastTime)
    {
        if (reds.Count == 0) throw new ArgumentException("No red lines.", nameof(reds));
        var totals = new List<(double BeatLength, double Duration)>();
        for (int i = 0; i < reds.Count; i++)
        {
            double time = reds[i].SerializedTime;
            double duration = time > lastTime ? 0 : (i + 1 == reds.Count ? lastTime : reds[i + 1].SerializedTime) - (i == 0 ? 0 : time);
            double key = Math.Round(reds[i].BeatLength * 1000) / 1000;
            int at = totals.FindIndex(t => t.BeatLength == key);
            if (at < 0) totals.Add((key, duration));
            else totals[at] = (key, totals[at].Duration + duration);
        }
        var best = totals[0];
        foreach (var t in totals) if (t.Duration > best.Duration) best = t;
        return best.BeatLength;
    }

    /// <summary>
    /// The BPM the green lines already normalise to: BPM × SV on each red line (SV of the green line at its time, else
    /// 1), when at least 80 % of the red lines agree on it within 0.5 % and those have two BPMs or more; else null.
    /// </summary>
    public static double? ExistingReference(IReadOnlyList<TimingPoint> reds, IReadOnlyList<TimingPoint> greens)
    {
        var products = reds.Select(r => (Bpm: r.Bpm, Product: r.Bpm * (GreenAt(greens, r)?.Sv ?? 1))).ToArray();
        foreach (var candidate in products.Select(p => p.Product).Distinct().Order())
        {
            var agree = products.Where(p => Math.Abs(p.Product / candidate - 1) < 0.005).ToArray();
            if (agree.Length >= 0.8 * products.Length && agree.Select(p => Math.Round(p.Bpm, 3)).Distinct().Count() >= 2)
                return agree.Select(p => p.Product).Order().ElementAt(agree.Length / 2);
        }
        return null;
    }

    /// <summary>
    /// Every red line plus green lines for constant scroll speed at the main BPM: a red line without a green line at its
    /// time gets one with SV main / BPM (its sample set, index, volume and kiai), unless that is 1; each existing green
    /// line keeps its own SV on top, times main / BPM of the red line it now falls under. Green lines that already
    /// normalise (see <see cref="ExistingReference"/>, against <paramref name="authoredReds"/>, the red lines they were
    /// written for) are divided by that first, so normalising twice changes nothing.
    /// </summary>
    public static Result Normalize(IReadOnlyList<TimingPoint> reds, IReadOnlyList<TimingPoint> greens,
        IReadOnlyList<TimingPoint> authoredReds, double lastTime)
    {
        reds = BeatGrid.Sort(reds);
        var authored = authoredReds.Count > 0 ? BeatGrid.Sort(authoredReds) : reds;
        var green = greens.Where(g => !g.Uninherited).OrderBy(g => g.Time).ToArray();
        double main = MainBeatLength(reds, lastTime);
        double? reference = ExistingReference(authored, green);
        int added = 0, scaled = 0, clamped = 0;
        double Clamp(double sv)
        {
            if (sv is >= MinSv and <= MaxSv) return sv;
            clamped++;
            return Math.Clamp(sv, MinSv, MaxSv);
        }
        // Within 0.001 % of 1 is 1: 116.959 BPM (513.0002 ms) against a main beat of 513 ms needs no green line, and
        // a green line that is already right keeps its text.
        static double Exact(double sv) => Math.Abs(sv - 1) < 1e-5 ? 1 : sv;
        double Factor(TimingPoint red) => Exact(red.BeatLength / main);
        double Own(TimingPoint g) => reference is { } r ? Exact(g.Sv * RedAt(authored, g.Time).Bpm / r) : g.Sv;
        var output = new List<TimingPoint>(reds);
        foreach (var g in green)
        {
            double sv = Clamp(Own(g) * Factor(RedAt(reds, g.Time)));
            if (Math.Abs(sv / g.Sv - 1) < 1e-9) { output.Add(g); continue; }
            scaled++;
            output.Add(g with { BeatLength = -100 / sv });
        }
        foreach (var red in reds)
        {
            double sv = Factor(red);
            if (GreenAt(green, red) is not null || sv == 1) continue;
            output.Add(new TimingPoint(red.SerializedTime, -100 / Clamp(sv), red.Meter, red.SampleSet, red.SampleIndex,
                red.Volume, false, red.Effects & TimingPoint.Kiai));
            added++;
        }
        return new(output.ToArray(), 60000 / main, reference, added, scaled, clamped);
    }

    /// <summary>The last green line at the red line's (whole ms) time: osu! applies the last of several. A green line
    /// even a fraction of a ms earlier belongs to the section before.</summary>
    private static TimingPoint? GreenAt(IReadOnlyList<TimingPoint> greens, TimingPoint red) =>
        greens.LastOrDefault(g => Math.Abs(g.Time - red.SerializedTime) < 1e-6);

    /// <summary>The red line in effect at a time; before the first red line osu! uses the first.</summary>
    private static TimingPoint RedAt(IReadOnlyList<TimingPoint> reds, double time) =>
        reds.LastOrDefault(r => r.SerializedTime <= time + 1e-6) ?? reds[0];
}
