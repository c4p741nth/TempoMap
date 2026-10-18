using TimingAnalyzer.Editor;

namespace TimingAnalyzer.Editor.Tests;

public class TimingEditTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    public void GridResetsMeasuresAndClipsSections(int division)
    {
        TimingPoint[] points = [new(100, 500, 3), new(1250, 250, 2)];
        var grid = BeatGrid.Query(points, 0, 2500, division).ToArray();
        Assert.Equal(new double[] { 100, 600, 1100, 1250, 1500, 1750, 2000, 2250 },
            grid.Where(p => p.Kind != GridKind.Subdivision).Select(p => p.Time));
        Assert.Equal(new double[] { 1750, 2250 }, grid.Where(p => p.Kind == GridKind.Measure).Select(p => p.Time));
        Assert.Equal(grid, BeatGrid.Query(points, 0, 1250, division).Concat(BeatGrid.Query(points, 1250, 2500, division)));
        Assert.All(grid, p => Assert.Equal(points[p.Section].Time + p.Step * points[p.Section].BeatLength / division, p.Time));
    }

    [Fact]
    public void EveryEditUndoesAndRedoesAndNewEditClearsRedo()
    {
        var edits = new TimingEdits([new(100, 500, 3), new(2000, 250)]);
        void Check(Action action)
        {
            var before = edits.RedLines.ToArray();
            action();
            var after = edits.RedLines.ToArray();
            Assert.NotEqual(before, after);
            edits.Undo(); Assert.Equal(before, edits.RedLines);
            edits.Redo(); Assert.Equal(after, edits.RedLines);
        }
        Check(() => edits.MoveRedLine(0, 200));
        Assert.Equal(500, edits.RedLines[0].BeatLength);
        Check(() => edits.StretchRedLine(0, 3, 1400));
        Assert.Equal(200, edits.RedLines[0].Time);
        Assert.Equal(1400, edits.RedLines[0].Time + 3 * edits.RedLines[0].BeatLength);
        Check(() => edits.AddRedLine(1000));
        Assert.Equal(400, edits.RedLines[1].BeatLength);
        Assert.Equal(3, edits.RedLines[1].Meter);
        Check(() => edits.Delete(1));
        Check(() => edits.Update(0, edits.RedLines[0] with { Meter = 7 }));
        edits.Undo(); edits.MoveRedLine(0, 250);
        Assert.False(edits.History.CanRedo);
        Assert.Throws<ArgumentException>(() => edits.MoveRedLine(0, 2000));
        Assert.Throws<ArgumentOutOfRangeException>(() => edits.StretchRedLine(0, 0, 1000));
    }
}
