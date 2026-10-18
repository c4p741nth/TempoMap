using System.Globalization;

namespace TimingAnalyzer.Editor;

public sealed record TimingPoint(double Time, double BeatLength, int Meter = 4, int SampleSet = 1,
    int SampleIndex = 0, int Volume = 100, bool Uninherited = true, int Effects = 0)
{
    /// <summary>osu! effect bits: kiai time, and "omit first barline" (no bar line at the red line in taiko and mania).</summary>
    public const int Kiai = 1, OmitFirstBarline = 8;
    public double Bpm => 60000 / BeatLength;
    public double SerializedTime => Uninherited ? Math.Floor(Time + 0.5) : Time;
    public bool OmitsFirstBarline => Uninherited && (Effects & OmitFirstBarline) != 0;
    /// <summary>Slider velocity of a green line (osu! writes it as beat length −100 / SV).</summary>
    public double Sv => Uninherited ? 1 : -100 / BeatLength;
    public TimingPoint Validate()
    {
        if (!double.IsFinite(Time) || !double.IsFinite(BeatLength) ||
            (Uninherited ? BeatLength <= 0 : BeatLength >= 0) || Meter <= 0)
            throw new ArgumentException("Invalid timing point.");
        return this;
    }

    public static TimingPoint Parse(string text)
    {
        var f = text.Split(',');
        if (f.Length < 2) throw new FormatException("A timing point needs time and beat length.");
        int Field(int i, int fallback) => f.Length > i ? int.Parse(f[i], CultureInfo.InvariantCulture) : fallback;
        return new TimingPoint(double.Parse(f[0], CultureInfo.InvariantCulture),
            double.Parse(f[1], CultureInfo.InvariantCulture), Field(2, 4), Field(3, 1), Field(4, 0),
            Field(5, 100), Field(6, 1) != 0, Field(7, 0)).Validate();
    }

    public string ToOsuLine() => string.Join(",", new[] {
        SerializedTime.ToString(Uninherited ? "0" : "R", CultureInfo.InvariantCulture),
        BeatLength.ToString("R", CultureInfo.InvariantCulture), Meter.ToString(CultureInfo.InvariantCulture),
        SampleSet.ToString(CultureInfo.InvariantCulture), SampleIndex.ToString(CultureInfo.InvariantCulture),
        Volume.ToString(CultureInfo.InvariantCulture), Uninherited ? "1" : "0", Effects.ToString(CultureInfo.InvariantCulture) });
}

public static class TimingClipboard
{
    /// <summary>Timing point rows only, no [TimingPoints] header: ready to paste into the osu! editor.</summary>
    public static string Write(IEnumerable<TimingPoint> points) =>
        string.Join("\r\n", points.OrderBy(p => p.SerializedTime).ThenBy(p => !p.Uninherited).Select(p => p.ToOsuLine())) + "\r\n";

    public static TimingPoint[] Parse(string text) => text.Replace("\r", "").Split('\n')
        .Select(s => s.Trim()).Where(s => s.Length > 0 && !s.StartsWith('[') && !s.StartsWith("//", StringComparison.Ordinal))
        .Select(TimingPoint.Parse).ToArray();
}
