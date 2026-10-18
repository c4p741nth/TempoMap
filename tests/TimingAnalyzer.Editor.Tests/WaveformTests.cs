using TimingAnalyzer.Editor;

namespace TimingAnalyzer.Editor.Tests;

public class WaveformTests
{
    [Theory]
    [InlineData(0, 10001, 1)]
    [InlineData(7, 9987, 137)]
    [InlineData(53, 9000, 1920)]
    [InlineData(350, 370, 100)]
    public void PixelsEqualNaiveExtrema(int start, int end, int width)
    {
        var rng = new Random(17);
        var samples = Enumerable.Range(0, 10001).Select(_ => (float)(rng.NextDouble() * 2 - 1)).ToArray();
        var peaks = new WaveformPeaks(samples);
        var actual = peaks.Query(start, end, width);
        for (int x = 0; x < width; x++)
        {
            var slice = samples[(start + (end - start) * x / width)..(start + (end - start) * (x + 1) / width)];
            Assert.Equal(slice.Length == 0 ? default : new Peak(slice.Min(), slice.Max()), actual[x]);
        }
        Assert.True(peaks.StoredPeakCount <= 2L * samples.Length + peaks.LevelCount);
        Assert.Equal((int)Math.Ceiling(Math.Log2(samples.Length)) + 1, peaks.LevelCount);
    }

    [Fact]
    public void EmptyAndSingletonAreBounded()
    {
        var empty = new WaveformPeaks([]);
        Assert.Equal(0, empty.StoredPeakCount);
        Assert.All(empty.Query(0, 0, 3), p => Assert.Equal(default, p));
        var one = new WaveformPeaks([0.3f]);
        Assert.Equal(new Peak(0.3f, 0.3f), one.Range(0, 1));
        Assert.Equal(1, one.StoredPeakCount);
    }
}
