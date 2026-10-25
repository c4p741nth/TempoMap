using TimingAnalyzer.Editor;

namespace TimingAnalyzer.Editor.Tests;

public class MeasureLayoutTests
{
    [Fact]
    public void RowsCountBarsAcrossTempoAndMeterChanges()
    {
        var rows = MeasureLayout.Rows([new(0, 500), new(4000, 250, 3)], 0);
        Assert.Equal(new MeasureRow(0, 5500), rows[0]); Assert.Equal(new MeasureRow(5500, 8500), rows[1]);
    }
    [Fact]
    public void OmittedFirstBarlinesDoNotCountAsNewBars()
    {
        var points = Enumerable.Range(0, 32).Select(i => new TimingPoint(i * 500, 500, Effects: i % 4 == 0 ? 0 : TimingPoint.OmitFirstBarline));
        Assert.Equal(new MeasureRow(0, 8000), MeasureLayout.Rows(points, 0)[0]);
        Assert.All(MeasureLayout.Rows([new(0, 6e-96)], 0), row => Assert.True(double.IsFinite(row.EndMs) && row.EndMs > row.StartMs));
    }
}
