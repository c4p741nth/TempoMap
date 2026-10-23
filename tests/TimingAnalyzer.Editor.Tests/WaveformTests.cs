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
        var samples = Enumerable.Range(0, 10001).Select(_ => rng.NextDouble() * 2 - 1).ToArray();
        var peaks = new WaveformPeaks(samples);
        var actual = peaks.Query(start, end, width);
        for (int x = 0; x < width; x++)
        {
            var slice = samples[(start + (end - start) * x / width)..(start + (end - start) * (x + 1) / width)];
            Assert.Equal(slice.Length == 0 ? default : new Peak((float)slice.Min(), (float)slice.Max()), actual[x]);
        }
        // Blocks of 32 samples and up: about one stored peak per 16 samples, not two per sample.
        Assert.True(peaks.StoredPeakCount <= samples.Length / 16 + peaks.LevelCount);
        Assert.Equal((int)Math.Ceiling(Math.Log2(samples.Length / 32)) + 1, peaks.LevelCount);
    }

    [Fact]
    public void EveryRangeOfAShortSongEqualsNaiveExtrema()
    {
        // Every start/end pair over 3 blocks and a partial one, so each edge case of the block split is covered.
        var rng = new Random(5);
        var samples = Enumerable.Range(0, 3 * 32 + 7).Select(_ => rng.NextDouble() * 2 - 1).ToArray();
        var peaks = new WaveformPeaks(samples);
        for (int a = 0; a < samples.Length; a++)
            for (int b = a + 1; b <= samples.Length; b++)
                Assert.Equal(new Peak((float)samples[a..b].Min(), (float)samples[a..b].Max()), peaks.Range(a, b));
    }

    [Fact]
    public void EmptyAndSingletonAreBounded()
    {
        var empty = new WaveformPeaks([]);
        Assert.Equal(0, empty.StoredPeakCount);
        Assert.All(empty.Query(0, 0, 3), p => Assert.Equal(default, p));
        var one = new WaveformPeaks([0.3]);
        Assert.Equal(new Peak(0.3f, 0.3f), one.Range(0, 1));
        Assert.Equal(0, one.StoredPeakCount);
    }
}
