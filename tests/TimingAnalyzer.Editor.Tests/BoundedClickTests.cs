using TimingAnalyzer.Editor;

namespace TimingAnalyzer.Editor.Tests;

public class BoundedClickTests
{
    [Fact]
    public void GimmickTimingIsBoundedAndSurvivesUnchanged()
    {
        TimingPoint[] reds = [new(0, 6e-96), new(3000, 500)];
        Assert.Equal(5, BeatGrid.Query(reds, 0, 5000).Count());
        var track = new ClickTrack(reds, 48000, 48000L * 36000);
        var buffer = new float[4800]; track.Render(0, buffer, 1, true);
        Assert.Contains(buffer, x => x != 0);
        Assert.Equal(6e-96, reds[0].BeatLength);
        Assert.Equal(32, BeatGrid.Query([new(0, 500, 268435456)], 0, 1000, 16).Count());
    }

    [Fact]
    public void BufferedRenderingMatchesOneWholeBufferIncludingClickTails()
    {
        var track = new ClickTrack([new(0.011, 60000.0 / 137), new(2500.003, 333.333)], 48000, 240000);
        var full = new float[240000]; track.Render(0, full, 1, true);
        var buffered = new float[240000];
        for (int start = 0; start < buffered.Length; start += 127)
            track.Render(start, buffered.AsSpan(start, Math.Min(127, buffered.Length - start)), 1, true);
        Assert.Equal(full, buffered);
    }
}
