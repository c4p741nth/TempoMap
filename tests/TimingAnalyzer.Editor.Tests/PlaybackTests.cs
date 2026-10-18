using TimingAnalyzer.Editor;

namespace TimingAnalyzer.Editor.Tests;

public class PlaybackTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(0.25)]
    [InlineData(0.7)]
    [InlineData(1)]
    [InlineData(-1)]
    [InlineData(2)]
    public void ClickGainScalesSamplesAndChangesOnTheNextRender(double gain)
    {
        var track = new ClickTrack([new(0, 500, 3)], 44100, 44100);
        var full = new float[44100 * 2];
        track.Render(0, full, 2, true, 1);
        Assert.Contains(full, sample => sample != 0);
        var scaled = new float[full.Length];
        Array.Fill(scaled, 99f);
        track.Render(0, scaled, 2, true, gain);
        Assert.Equal(full.Select(sample => sample * (float)Math.Clamp(gain, 0, 1)), scaled);
        track.Render(0, scaled, 2, true, 0);
        Assert.All(scaled, sample => Assert.Equal(0, sample));
        track.Render(0, scaled, 2, true, 1);
        Assert.Equal(full, scaled);
    }

    [Theory]
    [InlineData(-0.5, 0)]
    [InlineData(0, 0)]
    [InlineData(0.7, 0.7)]
    [InlineData(1, 1)]
    [InlineData(1.5, 1)]
    [InlineData(double.NegativeInfinity, 0)]
    [InlineData(double.PositiveInfinity, 1)]
    public void VolumeClampsToTheUnitRange(double value, double expected)
    {
        Assert.Equal(expected, PlaybackMath.ClampVolume(value));
        Assert.Throws<ArgumentOutOfRangeException>(() => PlaybackMath.ClampVolume(double.NaN));
    }

    [Fact]
    public void RenderingIsIdenticalAcrossBufferSizesAndSeeks()
    {
        var track = new ClickTrack([new(2.3, 500, 3)], 44100, 44100);
        var whole = new float[44100 * 2];
        track.Render(0, whole, 2, true);
        var parts = new float[whole.Length];
        for (int start = 0; start < 44100; start += 127)
        {
            int frames = Math.Min(127, 44100 - start);
            track.Render(start, parts.AsSpan(start * 2, frames * 2), 2, true);
        }
        Assert.Equal(whole, parts);
        Assert.All(whole.Take((int)ClickSchedule.SampleAt(2.3, 44100) * 2), s => Assert.Equal(0, s));
        Assert.NotEqual(0, whole[(int)ClickSchedule.SampleAt(2.3, 44100) * 2]);
        var seek = new float[2048];
        track.Render(200, seek, 2, true);
        Assert.Equal(whole[400..(400 + seek.Length)], seek);
        track.Render(200, seek, 2, false);
        Assert.All(seek, s => Assert.Equal(0, s));
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(0.75)]
    [InlineData(0.5)]
    public void DeviceLatencyUsesSourceSpeedAndClampsToSeek(double speed)
    {
        Assert.Equal(speed, PlaybackMath.ValidateSpeed(speed));
        Assert.Equal(1000 - 40 * speed, PlaybackMath.AudiblePosition(1000, 40, speed, 500, 2000));
        Assert.Equal(500, PlaybackMath.AudiblePosition(500, 40, speed, 500, 2000));
        Assert.Equal(2000, PlaybackMath.AudiblePosition(3000, 40, speed, 500, 2000));
        Assert.Throws<ArgumentOutOfRangeException>(() => PlaybackMath.ValidateSpeed(0.9));
    }
}
