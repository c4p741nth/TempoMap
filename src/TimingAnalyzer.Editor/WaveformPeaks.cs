namespace TimingAnalyzer.Editor;

public readonly record struct Peak(float Min, float Max);

/// <summary>Dyadic min/max pyramid. Each query uses aligned blocks plus exact edge blocks.</summary>
public sealed class WaveformPeaks
{
    private readonly Peak[][] levels;
    public int SampleCount { get; }
    public int LevelCount => levels.Length;
    public long StoredPeakCount => levels.Sum(l => (long)l.Length);

    public WaveformPeaks(ReadOnlySpan<float> samples)
    {
        SampleCount = samples.Length;
        if (samples.Length == 0) { levels = []; return; }
        var all = new List<Peak[]>();
        var current = new Peak[samples.Length];
        for (int i = 0; i < samples.Length; i++)
        {
            float value = float.IsFinite(samples[i]) ? samples[i] : 0;
            current[i] = new(value, value);
        }
        all.Add(current);
        while (current.Length > 1)
        {
            var next = new Peak[(current.Length + 1) / 2];
            for (int i = 0; i < next.Length; i++)
                next[i] = i * 2 + 1 < current.Length ? Merge(current[i * 2], current[i * 2 + 1]) : current[i * 2];
            all.Add(next);
            current = next;
        }
        levels = all.ToArray();
    }

    private static Peak Merge(Peak a, Peak b) => new(Math.Min(a.Min, b.Min), Math.Max(a.Max, b.Max));

    public Peak Range(int start, int end)
    {
        if (start < 0 || end > SampleCount || end < start) throw new ArgumentOutOfRangeException(nameof(start));
        if (start == end) return default;
        var result = new Peak(float.PositiveInfinity, float.NegativeInfinity);
        while (start < end)
        {
            int level = 0;
            while (level + 1 < levels.Length && (start % (1L << (level + 1))) == 0 &&
                start + (1L << (level + 1)) <= end) level++;
            result = Merge(result, levels[level][start >> level]);
            start += 1 << level;
        }
        return result;
    }

    public Peak[] Query(int startSample, int endSample, int pixelWidth)
    {
        if (pixelWidth <= 0) throw new ArgumentOutOfRangeException(nameof(pixelWidth));
        if (startSample < 0 || endSample > SampleCount || endSample < startSample)
            throw new ArgumentOutOfRangeException(nameof(startSample));
        var result = new Peak[pixelWidth];
        long length = endSample - startSample;
        for (int x = 0; x < pixelWidth; x++)
        {
            int a = startSample + (int)(length * x / pixelWidth);
            int b = startSample + (int)(length * (x + 1) / pixelWidth);
            result[x] = Range(a, b);
        }
        return result;
    }
}
