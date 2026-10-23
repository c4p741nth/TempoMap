namespace TimingAnalyzer.Editor;

/// <summary>
/// One click for the commonest auto-time error: a section at the wrong octave or ratio of the map's tempo. On the 30
/// single-BPM evaluation songs, 5 came out at exactly double or half the map's BPM (Dusk 128 → 256, Tsukinami 240 →
/// 120, ...), and on Camellia - Parallel Universe Shifter range analysis gave ½, ⅔ or 4/3 of the map's BPM on 8 of
/// 11 steady sections (docs/PHASE1-STATUS.md E35).
/// </summary>
public static class TempoScale
{
    /// <summary>The factors offered: ×2, ÷2, ×1.5 (dotted), ×⅔, ×4/3, ×¾.</summary>
    public static IReadOnlyList<double> Factors { get; } = [2, 0.5, 1.5, 2.0 / 3, 4.0 / 3, 0.75];

    /// <summary>
    /// Multiplies the BPM of the given red lines by <paramref name="factor"/>, rounded to osu! precision; their times
    /// stay, so a halved section keeps every second beat and a doubled one adds the beats between.
    /// </summary>
    public static TimingPoint[] Apply(IReadOnlyList<TimingPoint> reds, IReadOnlyCollection<int> indices, double factor)
    {
        if (!(factor > 0) || !double.IsFinite(factor)) throw new ArgumentOutOfRangeException(nameof(factor));
        if (indices.Count == 0 || indices.Any(i => i < 0 || i >= reds.Count)) throw new InvalidOperationException("Select the red lines to change first.");
        var chosen = indices.ToHashSet();
        return reds.Select((p, i) => chosen.Contains(i) ? p with { BeatLength = 60000 / OsuPrecision.Bpm(p.Bpm * factor) } : p).ToArray();
    }

    public static string Label(double factor) => factor switch
    {
        2 => "×2", 0.5 => "÷2", 1.5 => "×1.5", 0.75 => "×¾",
        _ when Math.Abs(factor - 2.0 / 3) < 1e-9 => "×⅔",
        _ when Math.Abs(factor - 4.0 / 3) < 1e-9 => "×4/3",
        _ => "×" + factor.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture),
    };
}

/// <summary>
/// A section worth a listen (Check sections): the line's grid sits on few onsets (<paramref name="Weak"/>), the same
/// tempo fits clearly better <paramref name="ShiftMs"/> ms away, another tempo ratio fits the onsets clearly better
/// (<paramref name="Ratio"/>), or maps of songs like it usually write this BPM × <paramref name="MapRatio"/> (the
/// octave model: a suggestion only, never applied by itself). App maps Core onto this, so Editor stays independent
/// of Core.
/// </summary>
public sealed record SectionMark(int Index, bool Weak, double? ShiftMs, double? Ratio, double? MapRatio = null)
{
    public string Describe(TimingPoint line) => OsuPrecision.FormatTime(line.Time) + " ms" +
        (ShiftMs is { } s ? ", fits better " + s.ToString("+0;-0", System.Globalization.CultureInfo.InvariantCulture) + " ms" : "") +
        (Ratio is { } r ? ", " + TempoScale.Label(r) + " fits better" : "") +
        (MapRatio is { } m ? ", maps usually write " + TempoScale.Label(m) + " (" + OsuPrecision.FormatBpm(line.Bpm * m) + " BPM)" : "") +
        (ShiftMs is null && Ratio is null && MapRatio is null && Weak ? ", few onsets on its beats" : "");
}
