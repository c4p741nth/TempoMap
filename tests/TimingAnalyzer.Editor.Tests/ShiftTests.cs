using System.Globalization;
using System.Text;
using TimingAnalyzer.Editor;

namespace TimingAnalyzer.Editor.Tests;

public class ShiftTests
{
    private static byte[] Fixture() => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "steady-multi.osu"));

    [Theory]
    [InlineData(-1)]
    [InlineData(-0.5)]
    [InlineData(-0.25)]
    [InlineData(-0.125)]
    [InlineData(-0.0625)]
    [InlineData(-0.03125)]
    [InlineData(0.03125)]
    [InlineData(0.0625)]
    [InlineData(0.125)]
    [InlineData(0.25)]
    [InlineData(0.5)]
    [InlineData(1)]
    public void AllBeatShiftsUseEachLinesOwnBeatLengthAndUndoRestoresExactBytes(double beats)
    {
        byte[] bytes = Fixture();
        var doc = OsuFile.Parse(bytes);
        var original = doc.TimingPoints.Where(point => point.Uninherited).ToArray();
        Assert.True(original.Select(point => point.BeatLength).Distinct().Count() > 1);
        var edits = new TimingEdits(original);
        int changes = 0;
        edits.Changed += () => changes++;
        edits.ShiftRedLines(RedLineScope.All, beats);
        // E15: each moved line lands on a whole ms; its beat length stays exact.
        var expected = original.Select(point => point with { Time = Math.Floor(point.Time + beats * point.BeatLength + 0.5) })
            .OrderBy(point => point.Time).ToArray();
        Assert.Equal(expected, edits.RedLines); // Whole records: BPM, meter, samples and effects stay unchanged.
        Assert.Contains(original, point => beats * point.BeatLength != Math.Floor(beats * point.BeatLength)); // Rounding happened.
        Assert.All(edits.RedLines, point => Assert.Equal(Math.Floor(point.Time), point.Time));
        Assert.Equal(1, changes);
        byte[] shifted = doc.WithRedLines(edits.RedLines);
        var exported = OsuFile.Parse(shifted);
        Assert.Equal(doc.TimingPoints.Where(point => !point.Uninherited), exported.TimingPoints.Where(point => !point.Uninherited));
        foreach (string green in Encoding.UTF8.GetString(bytes).Split('\n')
            .Where(line => line.Split(',') is { Length: >= 7 } fields && fields[6] == "0"))
            Assert.Contains(green, Encoding.UTF8.GetString(shifted), StringComparison.Ordinal);
        Assert.Equal(expected.Select(point => point.SerializedTime), exported.TimingPoints.Where(point => point.Uninherited).Select(point => point.Time));
        edits.Undo();
        Assert.Equal(original, edits.RedLines);
        Assert.Equal(bytes, doc.WithRedLines(edits.RedLines));
        Assert.False(edits.History.CanUndo);
        edits.Redo(); Assert.Equal(expected, edits.RedLines);
    }

    [Theory]
    [InlineData(-5)]
    [InlineData(-1)]
    [InlineData(1)]
    [InlineData(5)]
    public void MillisecondNudgeMovesAllLinesByTheSameAmountInOneUndo(double ms)
    {
        var doc = OsuFile.Parse(Fixture());
        var original = doc.TimingPoints.Where(point => point.Uninherited).ToArray();
        var edits = new TimingEdits(original);
        edits.ShiftRedLinesMs(RedLineScope.All, ms);
        Assert.Equal(original.Select(point => point with { Time = point.Time + ms }), edits.RedLines);
        edits.Undo(); Assert.Equal(original, edits.RedLines); Assert.False(edits.History.CanUndo);
    }

    [Fact]
    public void SelectedScopeMovesOnlyThatLineAndRejectsBothNeighboursWithoutChangingHistory()
    {
        byte[] bytes = Fixture();
        var doc = OsuFile.Parse(bytes);
        var original = doc.TimingPoints.Where(point => point.Uninherited).ToArray();
        var edits = new TimingEdits(original);
        const int selected = 1;
        edits.ShiftRedLines(RedLineScope.Selected, 0.25, selected);
        Assert.Equal(original.Select((point, index) => index == selected
            ? point with { Time = Math.Floor(point.Time + point.BeatLength / 4 + 0.5) } : point), edits.RedLines);
        edits.Undo(); Assert.Equal(bytes, doc.WithRedLines(edits.RedLines));
        edits.ShiftRedLinesMs(RedLineScope.Selected, -5, selected);
        Assert.Equal(original.Select((point, index) => index == selected ? point with { Time = point.Time - 5 } : point), edits.RedLines);
        edits.Undo();
        int changes = 0;
        edits.Changed += () => changes++;
        foreach (double destination in new[] { original[0].Time - 1, original[0].Time, original[2].Time, original[2].Time + 1 })
        {
            var error = Assert.Throws<InvalidOperationException>(() => edits.ShiftRedLinesMs(
                RedLineScope.Selected, destination - original[selected].Time, selected));
            Assert.Contains("neighbouring", error.Message, StringComparison.Ordinal);
            Assert.Equal(original, edits.RedLines);
            Assert.False(edits.History.CanUndo);
            Assert.True(edits.History.CanRedo);
        }
        Assert.Equal(0, changes);
        edits.Redo(); Assert.Equal(original[selected].Time - 5, edits.RedLines[selected].Time);
    }

    [Fact]
    public void NegativeFractionalTimesAreAllowedAndInvalidShiftsAreAtomic()
    {
        var edits = new TimingEdits([new(0, 501.5), new(10, 500)]);
        edits.ShiftRedLines(RedLineScope.Selected, -0.5, 0);
        Assert.Equal(-251, edits.RedLines[0].Time); // -250.75 rounded to a whole ms.
        Assert.StartsWith("-251,", edits.RedLines[0].ToOsuLine(), StringComparison.Ordinal);
        edits.Undo();
        Assert.Throws<InvalidOperationException>(() => edits.ShiftRedLines(RedLineScope.Selected, 0.25, 0));
        Assert.Throws<InvalidOperationException>(() => edits.ShiftRedLines(RedLineScope.Selected, -0.25, 1));
        Assert.Throws<InvalidOperationException>(() => edits.ShiftRedLines(RedLineScope.Selected, 1));
        Assert.Throws<InvalidOperationException>(() => edits.ShiftRedLinesMs(RedLineScope.Selected, 1, 99));
        Assert.Throws<ArgumentOutOfRangeException>(() => edits.ShiftRedLines(RedLineScope.All, 0.3));
        Assert.Throws<ArgumentOutOfRangeException>(() => edits.ShiftRedLines(RedLineScope.All, 1.0 / 64));
        Assert.Throws<ArgumentOutOfRangeException>(() => edits.ShiftRedLines(RedLineScope.All, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => edits.ShiftRedLines(RedLineScope.All, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => edits.ShiftRedLines(RedLineScope.All, double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => edits.ShiftRedLinesMs(RedLineScope.All, double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => edits.ShiftRedLines((RedLineScope)99, 1));
        Assert.False(edits.History.CanUndo);
        var collision = new TimingEdits([new(0, 600), new(100, 400)]);
        Assert.Throws<InvalidOperationException>(() => collision.ShiftRedLines(RedLineScope.All, 0.5));
        var reorder = new TimingEdits([new(0, 1000), new(100, 100)]); // +1 beat would swap the two lines.
        Assert.Contains("neighbouring", Assert.Throws<InvalidOperationException>(() => reorder.ShiftRedLines(RedLineScope.All, 1)).Message, StringComparison.Ordinal);
        Assert.False(reorder.History.CanUndo);
        Assert.Equal(new double[] { 0, 100 }, collision.RedLines.Select(point => point.Time));
        Assert.False(collision.History.CanUndo);
    }

    [Fact]
    public async Task ShiftCommandsUpdatePlaybackKeepSelectionAndReportRejectionInInvariantCulture()
    {
        string path = Path.GetTempFileName();
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("th-TH");
            var engine = new ViewModelTests.FakePlayback();
            using var vm = new EditorViewModel(_ => Task.FromResult(AudioContent.Create(new double[100], 1000)), _ => engine);
            await vm.LoadAsync(path);
            vm.Edits.Replace([new(0, 500), new(2000, 600, 3), new(2200, 250)]);
            vm.SelectedRow = vm.Rows[1];
            vm.ShiftScope = RedLineScope.Selected;
            vm.ShiftBeatsCommand.Execute("0.25");
            Assert.Equal(2150, vm.SelectedRow!.Point.Time);
            Assert.Equal(1, vm.SelectedRow.Index);
            Assert.Equal(vm.Edits.RedLines, engine.Timing);
            Assert.Contains("by +1/4 beat.", vm.Status, StringComparison.Ordinal);
            var before = vm.Edits.RedLines.ToArray();
            vm.ShiftBeatsCommand.Execute("0.25");
            Assert.Contains("neighbouring", vm.Status, StringComparison.Ordinal);
            Assert.Equal(before, vm.Edits.RedLines); Assert.Equal(before, engine.Timing);
            vm.Undo(); Assert.Equal(2000, vm.Edits.RedLines[1].Time); // Rejection adds no undo step.
            vm.SelectedRow = vm.Rows[1];
            vm.ShiftScope = RedLineScope.All;
            vm.ShiftMsCommand.Execute("-5");
            Assert.Equal(new double[] { -5, 1995, 2195 }, engine.Timing.Select(point => point.Time));
            Assert.Equal(1995, vm.SelectedRow!.Point.Time);
            vm.ShiftScope = RedLineScope.Selected; vm.SelectedRow = null;
            vm.ShiftMsCommand.Execute("1");
            Assert.Contains("Select a red line", vm.Status, StringComparison.Ordinal);
            Assert.Equal(new double[] { -5, 1995, 2195 }, engine.Timing.Select(point => point.Time));
        }
        finally { CultureInfo.CurrentCulture = previous; File.Delete(path); }
    }
    [Fact]
    public async Task StepButtonsShiftBySizeChosenInTheirBoxes()
    {
        using var vm = new EditorViewModel(_ => Task.FromResult(AudioContent.Create(new double[100], 1000)), _ => new ViewModelTests.FakePlayback());
        await vm.LoadAsync(Path.GetTempFileName());
        vm.Edits.Replace([new(0, 500), new(4000, 480), new(9000, 400)]);
        vm.SelectRows([1], 1);
        Assert.Equal(["1/1", "1/2", "1/4", "1/8", "1/16", "1/32"], vm.ShiftBeatSteps.Select(o => o.ToString()));
        Assert.Equal((4, 1), (vm.SelectedShiftBeatStep.Divisor, vm.SelectedShiftMsStep));
        vm.SelectedShiftBeatStep = vm.ShiftBeatSteps.First(o => o.Divisor == 32);
        vm.ShiftBeatStepCommand.Execute("1");
        Assert.Equal(4015, vm.Edits.RedLines[1].Time); // 480 / 32 = 15 ms of its own beat.
        Assert.Contains("by +1/32 beat.", vm.Status, StringComparison.Ordinal);
        vm.SelectedShiftBeatStep = vm.ShiftBeatSteps.First(o => o.Divisor == 16);
        vm.ShiftBeatStepCommand.Execute("-1");
        Assert.Equal(3985, vm.Edits.RedLines[1].Time);
        Assert.Contains("by -1/16 beat.", vm.Status, StringComparison.Ordinal);
        vm.SelectedShiftBeatStep = vm.ShiftBeatSteps[0];
        vm.ShiftBeatStepCommand.Execute("1");
        Assert.Equal(4465, vm.Edits.RedLines[1].Time);
        Assert.Contains("by +1 beat.", vm.Status, StringComparison.Ordinal);
        vm.SelectedShiftMsStep = 20;
        vm.ShiftMsStepCommand.Execute("-1");
        Assert.Equal(4445, vm.Edits.RedLines[1].Time);
        Assert.Contains("by -20 ms.", vm.Status, StringComparison.Ordinal);
        vm.ShiftMsStepCommand.Execute("1");
        Assert.Equal(4465, vm.Edits.RedLines[1].Time);
        Assert.Equal([0.0, 9000], new[] { vm.Edits.RedLines[0].Time, vm.Edits.RedLines[2].Time }); // Unselected lines stay.
        // Choices outside the boxes are ignored.
        vm.SelectedShiftMsStep = 7; vm.SelectedShiftBeatStep = new SnapOption(3); vm.SelectedShiftBeatStep = null!;
        Assert.Equal((1, 20), (vm.SelectedShiftBeatStep.Divisor, vm.SelectedShiftMsStep));
        for (int i = 0; i < 5; i++) vm.Undo();
        Assert.Equal(4000, vm.Edits.RedLines[1].Time); // One undo step per press.
    }
}
