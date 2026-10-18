using TimingAnalyzer.Editor;

namespace TimingAnalyzer.Editor.Tests;

public class ClickTests
{
    [Theory]
    [InlineData(44100)]
    [InlineData(48000)]
    public void RoundingAndAdjacentBuffersAreExact(int rate)
    {
        TimingPoint[] reds = [new(0.011, 60000.0 / 137, 3), new(2500.003, 333.333, 2)];
        long end = rate * 5;
        var expected = BeatGrid.Query(reds, 0, 5000).Select(g =>
            new ScheduledClick((long)Math.Floor(g.Time * rate / 1000 + 0.5), g.Kind is GridKind.RedLine or GridKind.Measure)).ToArray();
        Assert.Equal(expected, ClickSchedule.Query(reds, rate, 0, end));
        var buffered = new List<ScheduledClick>();
        for (long start = 0; start < end; start += 127)
            buffered.AddRange(ClickSchedule.Query(reds, rate, start, Math.Min(start + 127, end)));
        Assert.Equal(expected, buffered);
        Assert.All(ClickSchedule.Query(reds, rate, 0, end, false), c => Assert.False(c.Downbeat));
    }

    [Fact]
    public void RoundedBoundaryBelongsToExactlyOneRange()
    {
        TimingPoint[] reds = [new(0.49, 1), new(2.49, 1)];
        var all = ClickSchedule.Query(reds, 1000, 0, 5);
        Assert.Equal(new long[] { 0, 1, 2, 3, 4 }, all.Select(c => c.Sample));
        Assert.Equal(new long[] { 0, 2 }, all.Where(c => c.Downbeat).Select(c => c.Sample));
        Assert.Equal(all, ClickSchedule.Query(reds, 1000, 0, 2).Concat(ClickSchedule.Query(reds, 1000, 2, 5)));
        Assert.Equal(-1, ClickSchedule.SampleAt(-1.5, 1000));
        Assert.Empty(ClickSchedule.Query(reds, 1000, 2, 2));
    }
}
