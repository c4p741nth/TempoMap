using System.Globalization;

namespace TimingAnalyzer.Editor;

/// <summary>
/// One row of the timing table, laid out as osu!'s Timing and Control Points list (All / Timing Points / Inherited
/// Points): a red line (<see cref="RedIndex"/> its index among the red lines) or a green line (−1).
/// </summary>
public sealed record TimingRow(int RedIndex, TimingPoint Point)
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
    public bool IsRed => RedIndex >= 0;
    public bool IsGreen => RedIndex < 0;
    public string Offset => TimeFormat.Osu(Point.Time);
    /// <summary>BPM of a red line, "x1.90" slider velocity of a green line.</summary>
    public string Value => IsRed ? OsuPrecision.FormatBpm(Point.Bpm) : "x" + Point.Sv.ToString("0.00", Invariant);
    /// <summary>Beats per bar of a red line; "nb" when it omits its first bar line.</summary>
    public string Meter => IsRed ? Point.Meter.ToString(Invariant) + "/4" + (Point.OmitsFirstBarline ? " nb" : "") : "";
    /// <summary>Sample set (A auto, N normal, S soft, D drum) and custom index, as osu! shows "N:C1".</summary>
    public string Sample => (Point.SampleSet switch { 1 => "N", 2 => "S", 3 => "D", _ => "A" }) + ":C" + Point.SampleIndex.ToString(Invariant);
    public string Volume => Point.Volume.ToString(Invariant) + "%";
    public string Kiai => (Point.Effects & TimingPoint.Kiai) != 0 ? "✓" : "";
}
