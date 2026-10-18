namespace TimingAnalyzer.Editor;

/// <summary>Stateless float PCM renderer, suitable for arbitrary buffers and seeking.</summary>
public sealed class ClickTrack
{
    private readonly ScheduledClick[] clicks;
    private readonly float[] normal, accent;
    public ClickTrack(IReadOnlyList<TimingPoint> reds, int rate, long endSample)
    {
        clicks = ClickSchedule.Query(reds, rate, 0, endSample);
        normal = Tick(rate, 1200);
        accent = Tick(rate, 1800);
    }

    private static float[] Tick(int rate, double frequency) => Enumerable.Range(0, Math.Max(1, rate * 15 / 1000))
        .Select(i => (float)(0.35 * Math.Cos(2 * Math.PI * frequency * i / rate) * Math.Exp(-7.0 * i / (rate * 0.015))))
        .ToArray();

    public void Render(long startSample, Span<float> interleaved, int channels, bool enabled, double gain = 1)
    {
        if (channels <= 0 || interleaved.Length % channels != 0) throw new ArgumentOutOfRangeException(nameof(channels));
        float volume = (float)PlaybackMath.ClampVolume(gain);
        interleaved.Clear();
        if (!enabled || volume == 0) return;
        long end = startSample + interleaved.Length / channels;
        int low = 0, high = clicks.Length;
        while (low < high)
        {
            int mid = low + (high - low) / 2;
            if (clicks[mid].Sample + normal.Length <= startSample) low = mid + 1; else high = mid;
        }
        for (int n = low; n < clicks.Length && clicks[n].Sample < end; n++)
        {
            var click = clicks[n];
            var tick = click.Downbeat ? accent : normal;
            long from = Math.Max(startSample, click.Sample), to = Math.Min(end, click.Sample + tick.Length);
            for (long frame = from; frame < to; frame++)
                for (int ch = 0; ch < channels; ch++)
                    interleaved[checked((int)((frame - startSample) * channels + ch))] += tick[frame - click.Sample] * volume;
        }
    }
}
