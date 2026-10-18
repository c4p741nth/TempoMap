namespace TimingAnalyzer.Editor;

/// <summary>
/// Bar lines for runs of short red lines (a red line on every beat, as drift mode's 1 beat). In osu! every red line
/// starts a bar unless its "omit first barline" effect is set. Local mania maps do both: Sakuzyo vs. marasy - Innocent
/// omits it on 336 of 475 red lines (a bar line every 4 beats), JAPANESE SOUL BROTHERS on none of 743 (one every beat).
/// </summary>
public static class BarLines
{
    /// <param name="Omitted">Red lines from the downbeat on that now omit their bar line.</param>
    /// <param name="OffBar">Of those, lines longer than their meter: osu! still draws a bar line inside them, a meter
    /// after their start, which is not on the bar.</param>
    public sealed record Result(TimingPoint[] Lines, int Omitted, int OffBar);

    /// <summary>
    /// Counts whole beats from red line <paramref name="downbeat"/> (each line spans its beats to the next one) and sets
    /// "omit first barline" on every later line that does not start a bar of the meter; the downbeat and the lines on a
    /// bar start get their bar line back. A span that is not a whole number of beats (a tempo change mid-beat) makes
    /// the next line a bar start. Lines before the downbeat and the other effect bits are untouched.
    /// </summary>
    public static Result FromDownbeat(IReadOnlyList<TimingPoint> reds, int downbeat)
    {
        if (downbeat < 0 || downbeat >= reds.Count) throw new ArgumentOutOfRangeException(nameof(downbeat));
        var lines = reds.ToArray();
        int omitted = 0, offBar = 0, meter = 1;
        long beat = 0;
        bool barStart = true;
        for (int i = downbeat; i < lines.Length; i++)
        {
            if (barStart) { beat = 0; meter = Math.Max(1, lines[i].Meter); }
            else omitted++;
            lines[i] = lines[i] with
            {
                Effects = barStart ? lines[i].Effects & ~TimingPoint.OmitFirstBarline : lines[i].Effects | TimingPoint.OmitFirstBarline
            };
            if (i + 1 == lines.Length) break;
            double span = (lines[i + 1].Time - lines[i].Time) / lines[i].BeatLength, whole = Math.Round(span);
            if (!barStart && span > lines[i].Meter + 0.05) offBar++;
            if (Math.Abs(span - whole) > 0.05) { barStart = true; continue; }
            beat += (long)whole;
            barStart = beat % meter == 0;
        }
        return new(lines, omitted, offBar);
    }
}
