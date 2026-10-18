using TimingAnalyzer.Editor;

namespace TimingAnalyzer.Editor.Tests;

public class GridAgreementTests
{
    private static readonly TimingPoint[] TwoSections = [new(100, 500), new(5100, 400)];

    [Fact]
    public void ATimingAgainstItselfIsPerfect()
    {
        var score = GridAgreement.Score(TwoSections, TwoSections, 20000);
        Assert.Equal(0, score.MedianMs);
        Assert.Equal(1, score.Within5);
        Assert.Equal(1, score.Precision10);
        Assert.Equal(1, score.F10);
        Assert.Equal(10 + 38, score.ReferenceBeats); // 100..4600 every 500, then 5100..19900 every 400.
    }

    [Fact]
    public void AShiftShowsAsItsSizeInEveryBeat()
    {
        TimingPoint[] shifted = [new(107, 500), new(5107, 400)];
        var score = GridAgreement.Score(TwoSections, shifted, 20000);
        Assert.Equal(7, score.MedianMs, 9);
        Assert.Equal(7, score.P90Ms, 9);
        Assert.Equal(0, score.Within5);
        Assert.Equal(1, score.Within10);
        TimingPoint[] late = [new(130, 500), new(5130, 400)];
        var far = GridAgreement.Score(TwoSections, late, 20000);
        Assert.Equal(0, far.F10);           // 30 ms off everywhere...
        Assert.Equal(30, far.ShiftMs, 9);
        Assert.Equal(1, far.AlignedF10);    // ...but the right shape once one constant shift is removed.
        TimingPoint[] wrongTempo = [new(100, 500), new(5100, 430)];
        Assert.True(GridAgreement.Score(TwoSections, wrongTempo, 20000).AlignedF10 < 0.7);
    }

    [Fact]
    public void HalfAndDoubleTempoAreToldApart()
    {
        TimingPoint[] reference = [new(0, 500)];
        var half = GridAgreement.Score(reference, [new TimingPoint(0, 1000)], 10000);
        Assert.Equal(0.5, half.Within10, 9);   // Every other reference beat is half a beat away.
        Assert.Equal(1, half.Precision10);      // But every predicted beat is a real beat.
        Assert.Equal(1, half.HalfTempoWithin10);
        var twice = GridAgreement.Score(reference, [new TimingPoint(0, 250)], 10000);
        Assert.Equal(1, twice.Within10);
        Assert.Equal(0.5, twice.Precision10, 2);
        Assert.True(twice.F10 < 0.7);
    }

    [Fact]
    public void GridsExtendBackwardsAndSectionEdgesUseTheNeighbouringSection()
    {
        // Predicted timing starts later but is on the same grid: still a match before its first red line.
        var late = GridAgreement.Score([new TimingPoint(0, 500)], [new TimingPoint(2000, 500)], 6000);
        Assert.Equal(1, late.Within5);
        // Just after a red line, the previous section's last beat counts.
        Assert.Equal(3, GridAgreement.Nearest(TwoSections, 5100 + 3, 1), 9);
        Assert.Equal(2, GridAgreement.Nearest(TwoSections, 5098, 1), 9);
        Assert.Equal(4, GridAgreement.Nearest(TwoSections, 4604, 1), 9);
    }
}
