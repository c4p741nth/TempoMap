using TimingAnalyzer.Core;

namespace TimingAnalyzer.Core.Tests;

/// <summary>The beat head folded at a known BPM (<see cref="BeatPhase"/>), docs/PHASE1-STATUS.md E44.</summary>
public class BeatPhaseTests
{
    /// <summary>A beat activation at 100 fps: a peak of <paramref name="height"/> on every <paramref name="every"/>-th beat of the grid from <paramref name="firstMs"/>.</summary>
    private static float[] Beats(double firstMs, double lengthMs, double endMs, int every = 1, float height = 0.9f, double jitterMs = 0)
    {
        var f = new float[(int)(endMs / 10)];
        var random = new Random(5);
        int index = 0;
        for (double t = firstMs; t < endMs - 50; t += lengthMs, index++)
        {
            if (index % every != 0) continue;
            double at = t + (random.NextDouble() - 0.5) * 2 * jitterMs;
            int frame = (int)Math.Round(at / 10);
            for (int d = -2; d <= 2; d++)
                if (frame + d >= 0 && frame + d < f.Length) f[frame + d] = Math.Max(f[frame + d], (float)(height * Math.Exp(-0.5 * d * d / 1.5)));
        }
        return f;
    }

    [Theory]
    [InlineData(437, 419.58)]
    [InlineData(95, 500)]
    [InlineData(1203, 333.333)]
    public void TheFoldFindsThePhaseOfTheGridWhereverTheSectionStarts(double first, double length)
    {
        var beat = Beats(first, length, 60000, jitterMs: 4);
        double start = 5000;
        double found = BeatPhase.FirstBeat(beat, length, start, 60000)!.Value;
        Assert.InRange(found, start, start + length);
        double error = (found - first) % length;
        if (error > length / 2) error -= length;
        if (error < -length / 2) error += length;
        Assert.InRange(error, -4, 4);
    }

    [Fact]
    public void OffBeatEnergyDoesNotMoveThePhaseButTooFewBeatsGiveNothing()
    {
        // Strong activations on the beats, a weaker run half a beat later (the 8th notes).
        var beat = Beats(437, 419.58, 60000, height: 0.9f);
        var off = Beats(437 + 209.79, 419.58, 60000, height: 0.5f);
        for (int i = 0; i < beat.Length; i++) beat[i] = Math.Max(beat[i], off[i]);
        double found = BeatPhase.FirstBeat(beat, 419.58, 0, 60000)!.Value;
        Assert.InRange(found % 419.58 - 437 % 419.58, -4, 4);
        Assert.Null(BeatPhase.FirstBeat(beat, 419.58, 0, 7 * 419.58));
        Assert.Null(BeatPhase.FirstBeat(new float[100], 419.58, 0, 60000));
    }

    [Fact]
    public void ASectionLockedHalfABeatOffMovesToTheHeadsPhaseAndASmallDifferenceStays()
    {
        double length = 500;
        var beat = Beats(1000, length, 120000);   // the grid the head hears: beats at 1000 + k * 500
        // The analyzer put the first section's line half a beat late (1250: 250 ms from the head's beats), the second one 7 ms late.
        var sections = new[] { new TimingSection(1250, 120), new TimingSection(60007, 120) };
        var aligned = BeatPhase.AlignSections(sections, beat, 120000);
        Assert.InRange(Math.Abs(aligned[0].TimeMs - 1250), 200, 300);                   // moved by about half a beat ...
        Assert.InRange(((aligned[0].TimeMs - 1000) % length + length) % length, 0, 4);  // ... onto a beat of the head's grid (1000 or 1500)
        Assert.Equal(sections[1].TimeMs, aligned[1].TimeMs);                           // under 0.2 beat: left as the analyzer fitted it
        Assert.Equal(120, aligned[0].Bpm);
        Assert.Equal(120, aligned[1].Bpm);
    }

    [Fact]
    public void ShortSectionsAndMovesOntoAnEarlierLineAreLeftAlone()
    {
        var beat = Beats(1000, 500, 120000);
        // 20 beats: under the 24 a section needs.
        var shortFirst = new[] { new TimingSection(1250, 120), new TimingSection(1250 + 20 * 500, 120) };
        Assert.Equal(1250, BeatPhase.AlignSections(shortFirst, beat, 120000)[0].TimeMs);
        // Half a beat after the previous line the move would land on it (1000): refused.
        var neighbours = new[] { new TimingSection(1000, 120), new TimingSection(1250, 120) };
        Assert.Equal(1250, BeatPhase.AlignSections(neighbours, beat, 120000)[1].TimeMs);
    }

    [Fact]
    public void TheAlternateBeatRatioIsOneForEveryBeatAndNearZeroForEverySecond()
    {
        double length = 209.79;
        Assert.InRange(BeatPhase.AlternateBeatRatio(Beats(100, length, 60000), length, 0, 60000)!.Value, 0.9, 1.0001);
        Assert.InRange(BeatPhase.AlternateBeatRatio(Beats(100, length, 60000, every: 2), length, 0, 60000)!.Value, 0, 0.1);
        // A weaker alternate beat sits in between.
        var beat = Beats(100, length, 60000, every: 2);
        var other = Beats(100 + length, length, 60000, every: 2, height: 0.45f);
        for (int i = 0; i < beat.Length; i++) beat[i] = Math.Max(beat[i], other[i]);
        Assert.InRange(BeatPhase.AlternateBeatRatio(beat, length, 0, 60000)!.Value, 0.4, 0.6);
        Assert.Null(BeatPhase.AlternateBeatRatio(beat, length, 0, 5 * length));
    }
}
