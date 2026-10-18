namespace TimingAnalyzer.Editor;

/// <summary>
/// An audio range to analyze. A null BPM lets the detector find it; a BPM locks it so only the offset is fitted.
/// The detection's offset is absolute: the first detected beat at or after <see cref="StartMs"/>.
/// </summary>
public sealed record TimingRequest(double StartMs, double EndMs, double? Bpm);

public static class SectionFit
{
    /// <summary>
    /// On MAMBO COMBO, 3 s windows at 135 BPM usually locked a quarter beat off; from 4 s the median error was 1 ms.
    /// Some passages lock a half or quarter beat off at any length, hence <see cref="IsSuspicious"/>.
    /// </summary>
    public const double MinimumSectionMs = 4000;

    /// <summary>A move of more than an eighth of a beat may be an off-beat lock rather than a correction.</summary>
    public static bool IsSuspicious(double movedMs, double beatLength) => Math.Abs(movedMs) > beatLength / 8;

    /// <summary>The detected beat (grid through <paramref name="phaseMs"/>) nearest the line's current time, in whole ms.</summary>
    public static double Place(double currentTime, double phaseMs, double bpm)
    {
        double period = 60000 / bpm;
        return OsuPrecision.Time(phaseMs + Math.Round((currentTime - phaseMs) / period, MidpointRounding.AwayFromZero) * period);
    }
}
