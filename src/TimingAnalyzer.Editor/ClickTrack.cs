namespace TimingAnalyzer.Editor;

/// <summary>Stateless float PCM renderer, suitable for arbitrary buffers and seeking.</summary>
public sealed class ClickTrack
{
    private readonly TimingPoint[] reds;
    private readonly int rate;
    private readonly long endSample;
    private readonly float[] normal, accent;
    public ClickTrack(IReadOnlyList<TimingPoint> reds, int rate, long endSample)
    {
        this.reds = BeatGrid.Effective(reds);
        this.rate = rate;
        this.endSample = endSample;
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
        long end = Math.Min(endSample, startSample + interleaved.Length / channels);
        if (end <= startSample) return;
        // Include the tail of a click started in the preceding buffer. Cost depends on this buffer, not song length.
        var clicks = ClickSchedule.Query(reds, rate, Math.Max(0, startSample - normal.Length + 1), end, minimumBeatLength: 50);
        for (int n = 0; n < clicks.Length; n++)
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
