namespace TimingAnalyzer.Editor;

/// <summary>
/// Holding ←/→ past a short delay runs the playhead on, faster the longer the key stays down, as Audacity's cursor keys
/// do (docs/PHASE1-STATUS.md E42). Before this each key repeat stepped by the beat snap and seeked the audio, so a held
/// key crept along one tick at a time. The speed is a share of the visible span per second, so a zoomed-in view stays
/// fine enough to place a line and a zoomed-out one still crosses the song in seconds.
/// </summary>
public static class ScrubSpeed
{
    /// <summary>The first press steps by the beat snap; the run-on starts once the key has been down this long.</summary>
    public const double DelayMs = 300;
    /// <summary>Spans of the view crossed per second when the run-on starts; it doubles every <see cref="DoublingMs"/>.</summary>
    public const double StartSpansPerSecond = 0.3;
    /// <summary>The fastest run-on, in spans per second (reached about 2.6 s after the delay).</summary>
    public const double MaxSpansPerSecond = 4;
    public const double DoublingMs = 700;
    /// <summary>Bounds on the speed in song ms per second, for a very zoomed-in or zoomed-out view.</summary>
    public const double MinMsPerSecond = 300, MaxMsPerSecond = 120_000;

    /// <summary>Song ms per second after the key has been held <paramref name="heldMs"/>; 0 before the delay.</summary>
    public static double MsPerSecond(double heldMs, double spanMs)
    {
        if (!(heldMs >= DelayMs)) return 0;
        double spans = Math.Min(MaxSpansPerSecond, StartSpansPerSecond * Math.Pow(2, (heldMs - DelayMs) / DoublingMs));
        return Math.Clamp(spans * spanMs, MinMsPerSecond, MaxMsPerSecond);
    }
}
