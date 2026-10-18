namespace TimingAnalyzer.Editor;

/// <summary>Interpolates source milliseconds using a caller-supplied monotonic wall clock.</summary>
public sealed class PlayheadClock
{
    // Small device/read jitter decays over a few frames; discontinuities snap immediately.
    public const double CorrectionTimeMs = 120;
    public const double SnapThresholdMs = 250;
    private double enginePosition, sampledAt, speed, correction, lastPosition;
    private bool initialized, playing;

    public void Synchronize(double positionMs, double wallMs, double playbackSpeed, bool isPlaying, bool snap = false)
    {
        double current = initialized ? PositionAt(wallMs) : positionMs;
        bool discontinuity = snap || !initialized || !isPlaying || playing != isPlaying ||
            Math.Abs(positionMs - current) >= SnapThresholdMs;
        correction = discontinuity ? 0 : current - positionMs;
        enginePosition = positionMs;
        sampledAt = wallMs;
        speed = playbackSpeed;
        playing = isPlaying;
        initialized = true;
        lastPosition = discontinuity ? positionMs : current;
    }

    public double PositionAt(double wallMs)
    {
        if (!initialized) return 0;
        if (!playing) return enginePosition;
        double elapsed = Math.Max(0, wallMs - sampledAt);
        double predicted = enginePosition + elapsed * speed + correction * Math.Exp(-elapsed / CorrectionTimeMs);
        // A negative engine correction may slow or briefly hold the head, but never reverse it.
        lastPosition = Math.Max(lastPosition, predicted);
        return lastPosition;
    }
}

public static class FollowViewport
{
    /// <summary>Visible width is in source milliseconds, so the current zoom sets the anchor.</summary>
    public static double Start(double positionMs, double visibleWidthMs, double durationMs) =>
        Math.Clamp(positionMs - visibleWidthMs / 3, 0, Math.Max(0, durationMs - visibleWidthMs));
}
