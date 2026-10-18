using System.Globalization;

namespace TimingAnalyzer.Editor;

/// <summary>osu! takes red-line offsets as integer ms and shows BPM with three decimals.</summary>
public static class OsuPrecision
{
    public static double Time(double ms) => Math.Floor(ms + 0.5);
    public static double Bpm(double bpm) => Math.Round(bpm, 3, MidpointRounding.AwayFromZero);
    public static double BeatLength(double beatLength) => 60000 / Bpm(60000 / beatLength);
    public static string FormatTime(double ms) => Time(ms).ToString("0", CultureInfo.InvariantCulture);
    public static string FormatBpm(double bpm) => Bpm(bpm).ToString("0.###", CultureInfo.InvariantCulture);
}

public static class TimeFormat
{
    /// <summary>osu! editor time <c>mm:ss:fff</c> of the whole ms (as offsets are rounded), so 1531 ms reads <c>00:01:531</c>.</summary>
    public static string Osu(double ms)
    {
        long total = double.IsFinite(ms) && ms > 0 ? (long)OsuPrecision.Time(ms) : 0;
        return (total / 60000).ToString("00", CultureInfo.InvariantCulture) + ":" +
            (total / 1000 % 60).ToString("00", CultureInfo.InvariantCulture) + ":" + (total % 1000).ToString("000", CultureInfo.InvariantCulture);
    }
}
