namespace TimingAnalyzer.Editor;

/// <summary>
/// A smooth tempo ramp between two red lines the user trusts: one red line per beat, each beat's BPM a straight step
/// from A's to B's, ending on B. That is how ranked maps write long accelerandos: Camellia - Parallel Universe Shifter
/// (-mint-) writes 105 of its 113 ramp lines over 143-190 s as one beat at +1 BPM each (111 → 174 over 69-96 s, 301 →
/// 435 over 351-379 s, the same). Core's ramp search (RampBridge, given the map's lines on both sides) found only 3 of
/// its 10 ramps, because it looks for steps of several beats; with the two ends known, the shape needs no search. Given the map's first ramp
/// line and the line after the ramp, the straight ramp hits 290 of the map's 790 ramp beats within 10 ms (37 %),
/// against 105 for the onset search and 86 for a plain switch; full marks on the 5 ramps without a hold inside. A ramp
/// that holds a tempo midway (143-190 s holds 200 BPM for 17 beats) needs a red line at the hold: fill each piece.
/// </summary>
public static class RampFill
{
    /// <summary>
    /// The red lines from the beat after <paramref name="a"/>'s start up to <paramref name="b"/> (exclusive): the
    /// number of beats n is chosen so that beats whose BPM moves linearly from A's to B's (beat k at
    /// A + (B − A)·k/n BPM) span the gap best; the last beat is stretched or squeezed by the remainder, so the ramp
    /// lands exactly on B. Each line keeps <paramref name="a"/>'s meter, samples and effects; the BPM is at osu!
    /// precision and the times at whole ms. A gap shorter than 2 beats of either tempo has no ramp.
    /// </summary>
    public static TimingPoint[] Between(TimingPoint a, TimingPoint b)
    {
        double gap = b.Time - a.Time;
        if (!(gap > 0)) throw new ArgumentOutOfRangeException(nameof(b), "The second red line must come after the first.");
        if (gap < 2 * Math.Max(a.BeatLength, b.BeatLength)) throw new InvalidOperationException("The gap is shorter than two beats: no room for a ramp.");
        double Span(int n) { double sum = 0; for (int k = 0; k < n; k++) sum += 60000 / Bpm(k, n); return sum; }
        double Bpm(int k, int n) => a.Bpm + (b.Bpm - a.Bpm) * k / n;
        // The beat count whose span is nearest the gap (the span grows with n, so scan until it passes the gap).
        int best = 2;
        for (int n = 2; n < 100000; n++)
        {
            if (Math.Abs(Span(n) - gap) < Math.Abs(Span(best) - gap)) best = n;
            if (Span(n) > gap) break;
        }
        // Spread the remainder over every beat in proportion to its length, so no single beat is off by much.
        double scale = gap / Span(best);
        var lines = new List<TimingPoint>();
        double t = a.Time;
        for (int k = 0; k < best; k++)
        {
            double length = 60000 / Bpm(k, best) * scale;
            if (k > 0) lines.Add(a with { Time = OsuPrecision.Time(t), BeatLength = 60000 / OsuPrecision.Bpm(60000 / length), Effects = a.Effects | TimingPoint.OmitFirstBarline });
            t += length;
        }
        // The first line is A itself, at the ramp's first BPM (A's BPM rescaled by the remainder).
        return [a with { BeatLength = 60000 / OsuPrecision.Bpm(60000 / (60000 / a.Bpm * scale)) }, .. lines];
    }

    /// <summary>
    /// Ramps through consecutive selected red lines: each pair of neighbours in <paramref name="indices"/> (sorted)
    /// gets <see cref="Between"/>, the last selected line being the end that stays. Red lines between a pair are
    /// replaced; the others stay. Returns the new red lines and how many ramp lines were written.
    /// </summary>
    public static (TimingPoint[] Lines, int Added) Fill(IReadOnlyList<TimingPoint> reds, IReadOnlyList<int> indices)
    {
        var chosen = indices.Distinct().Order().ToArray();
        if (chosen.Length < 2 || chosen.Any(i => i < 0 || i >= reds.Count))
            throw new InvalidOperationException("Select the red line where the ramp starts and the one where it ends (Ctrl+click in the table).");
        var result = new List<TimingPoint>();
        for (int i = 0; i < chosen[0]; i++) result.Add(reds[i]);
        int added = 0;
        for (int k = 0; k + 1 < chosen.Length; k++)
        {
            var ramp = Between(reds[chosen[k]], reds[chosen[k + 1]]);
            result.AddRange(ramp);
            added += ramp.Length - 1;
        }
        for (int i = chosen[^1]; i < reds.Count; i++) result.Add(reds[i]);
        return (BeatGrid.Sort(result.GroupBy(p => p.Time).Select(g => g.First())), added);
    }
}
