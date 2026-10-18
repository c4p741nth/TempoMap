namespace TimingAnalyzer.Editor;

public interface IPlaybackEngine : IDisposable
{
    double DurationMs { get; }
    double PositionMs { get; }
    bool IsPlaying { get; }
    bool ClickEnabled { get; set; }
    double MusicVolume { get; set; }
    double ClickVolume { get; set; }
    double Speed { get; set; }
    void SetTiming(IEnumerable<TimingPoint> redLines);
    void Play();
    void Pause();
    void Seek(double timeMs);
}

public static class PlaybackMath
{
    public static double ClampVolume(double value) => double.IsNaN(value)
        ? throw new ArgumentOutOfRangeException(nameof(value), "Volume must be a number.")
        : Math.Clamp(value, 0, 1);

    public static double ValidateSpeed(double value) => value is 0.5 or 0.75 or 1.0 ? value :
        throw new ArgumentOutOfRangeException(nameof(value), "Speed must be 0.5, 0.75 or 1.0.");
    public static double AudiblePosition(double sourceMs, double deviceLatencyMs, double speed,
        double startMs, double durationMs) => Math.Clamp(sourceMs - deviceLatencyMs * speed, startMs, durationMs);
}
