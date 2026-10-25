namespace TimingAnalyzer.Editor;

/// <summary>Store green-line effects independently of normalization; derive visible SVs from the current red grid.</summary>
public static class LiveNormalization
{
    private static TimingPoint At(IReadOnlyList<TimingPoint> reds, double time) =>
        reds.LastOrDefault(r => r.SerializedTime <= time + 1e-6) ?? reds.FirstOrDefault() ?? new(0, 500);

    public static TimingPoint[] FromDocument(IReadOnlyList<TimingPoint> reds, IEnumerable<TimingPoint> greens)
    {
        var effective = BeatGrid.Effective(reds);
        var rows = greens.ToArray();
        double? reference = ScrollNormalization.ExistingReference(effective, rows);
        return reference is not { } main ? rows : rows.Select(g => g with
        {
            BeatLength = -100 / (g.Sv * At(effective, g.Time).Bpm / main)
        }).ToArray();
    }

    public static ScrollNormalization.Result Build(IReadOnlyList<TimingPoint> reds, IReadOnlyList<TimingPoint> effects, double end)
    {
        var effective = BeatGrid.Effective(reds);
        if (effective.Length == 0) return new(effects.ToArray(), 120, null, 0, 0, 0);
        if (effective.Select(r => OsuPrecision.Bpm(r.Bpm)).Distinct().Count() < 2)
            return new(reds.Concat(effects).ToArray(), effective[0].Bpm, null, 0, 0, 0);
        var result = ScrollNormalization.Normalize(effective, effects, [], end, includeUnitLines: true, detectExisting: false);
        return result with { Points = reds.Concat(result.Points.Where(p => !p.Uninherited)).ToArray() };
    }

    public static TimingPoint[] FromVisible(IEnumerable<TimingPoint> greens, IReadOnlyList<TimingPoint> reds, double end)
    {
        var effective = BeatGrid.Effective(reds);
        if (effective.Select(r => OsuPrecision.Bpm(r.Bpm)).Distinct().Count() < 2) return greens.ToArray();
        double main = ScrollNormalization.MainBeatLength(effective, end);
        return greens.Select(g => g with
        {
            BeatLength = g.BeatLength * ScrollNormalization.ExactUnit(At(effective, g.Time).BeatLength / main)
        }).ToArray();
    }
}
