namespace TimingAnalyzer.Editor;

public sealed record TimingDrag(int Index, TimingPoint Original, double PointerStart, double Beat)
{
    public TimingPoint Preview(double pointerTime)
    {
        double delta = pointerTime - PointerStart;
        // Preview at osu! precision, so the drop lands where the preview showed.
        return (Beat == 0 ? Original with { Time = OsuPrecision.Time(Original.Time + delta) } :
            Original with { BeatLength = OsuPrecision.BeatLength((Beat * Original.BeatLength + delta) / Beat) }).Validate();
    }
    public void Apply(TimingEdits edits, double pointerTime)
    {
        var result = Preview(pointerTime);
        if (Beat == 0) edits.MoveRedLine(Index, result.Time);
        else edits.StretchRedLine(Index, Beat, Original.Time + Beat * result.BeatLength);
    }
}

public static class TimingInteraction
{
    public static TimingDrag? Hit(IReadOnlyList<TimingPoint> reds, double time, double tolerance, bool stretch)
    {
        if (tolerance < 0 || !double.IsFinite(time)) return null;
        if (stretch)
        {
            var beat = BeatGrid.Query(reds, time - tolerance, time + tolerance + 1e-9)
                .Where(g => g.Step > 0).MinBy(g => Math.Abs(g.Time - time));
            return beat is null ? null : new(beat.Section, reds[beat.Section], time, beat.Step);
        }
        var hit = reds.Select((p, i) => (Point: p, Index: i)).Where(p => Math.Abs(p.Point.Time - time) <= tolerance)
            .OrderBy(p => Math.Abs(p.Point.Time - time)).ToArray();
        return hit.Length == 0 ? null : new(hit[0].Index, hit[0].Point, time, 0);
    }
}
