namespace TimingAnalyzer.Editor;

public readonly record struct Peak(float Min, float Max);

/// <summary>
/// Dyadic min/max pyramid over blocks of <see cref="BlockSize"/> samples and up; a query reads the song's own samples
/// for the part not covered by whole blocks. Min and max are exact, so any split of a range gives the same peak. The
/// samples are the decoded song the editor keeps anyway, so the pyramid holds about one peak per 16 samples (a per-
/// sample bottom level was ~230 MB on a 5-minute song).
/// </summary>
public sealed class WaveformPeaks
{
    private const int BlockLog2 = 5, BlockSize = 1 << BlockLog2;
    private readonly double[] samples;
    private readonly Peak[][] levels;
    public int SampleCount { get; }
    public int LevelCount => levels.Length;
    public long StoredPeakCount => levels.Sum(l => (long)l.Length);

    /// <summary>Peaks of <paramref name="samples"/>, which are kept by reference and must not change.</summary>
    public WaveformPeaks(double[] samples)
    {
        this.samples = samples;
        SampleCount = samples.Length;
        if (samples.Length < BlockSize) { levels = []; return; }
        var all = new List<Peak[]>();
        var current = new Peak[samples.Length / BlockSize];
        for (int i = 0; i < current.Length; i++)
        {
            var peak = new Peak(float.PositiveInfinity, float.NegativeInfinity);
            for (int j = i * BlockSize; j < (i + 1) * BlockSize; j++) peak = Merge(peak, Sample(j));
            current[i] = peak;
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

    private Peak Sample(int i)
    {
        float value = (float)samples[i];
        if (!float.IsFinite(value)) value = 0;
        return new(value, value);
    }

    private static Peak Merge(Peak a, Peak b) => new(Math.Min(a.Min, b.Min), Math.Max(a.Max, b.Max));

    public Peak Range(int start, int end)
    {
        if (start < 0 || end > SampleCount || end < start) throw new ArgumentOutOfRangeException(nameof(start));
        if (start == end) return default;
        var result = new Peak(float.PositiveInfinity, float.NegativeInfinity);
        // Samples up to the first block boundary, whole blocks (the largest aligned ones that fit), then the rest.
        while (start < end && (start % BlockSize != 0 || start + BlockSize > end)) result = Merge(result, Sample(start++));
        while (start + BlockSize <= end)
        {
            int level = 0;
            while (level + 1 < levels.Length && (start % (BlockSize << (level + 1))) == 0 &&
                start + (BlockSize << (level + 1)) <= end) level++;
            result = Merge(result, levels[level][start >> (BlockLog2 + level)]);
            start += BlockSize << level;
        }
        while (start < end) result = Merge(result, Sample(start++));
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
