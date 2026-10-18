using TimingAnalyzer.Editor;

namespace TimingAnalyzer.Editor.Tests;

public class SelectionTests
{
    private static async Task<(EditorViewModel Vm, ViewModelTests.FakePlayback Engine, string Path)> Loaded(params TimingPoint[] reds)
    {
        string path = Path.GetTempFileName();
        var engine = new ViewModelTests.FakePlayback();
        var vm = new EditorViewModel(_ => Task.FromResult(AudioContent.Create(new double[20000], 1000)), _ => engine);
        await vm.LoadAsync(path);
        vm.Edits.Replace(reds);
        return (vm, engine, path);
    }

    [Fact]
    public async Task BoxSelectAddSelectAllClearAndEscape()
    {
        var (vm, _, path) = await Loaded(new(0, 500), new(1000, 500), new(2000, 500), new(3000, 500), new(4000, 500));
        using (vm)
            try
            {
                vm.SelectRange(3100, 900, false); // Either drag direction.
                Assert.Equal(new[] { 1, 2, 3 }, vm.SelectedIndices);
                Assert.Equal(1, vm.SelectedRow!.Index);
                Assert.Equal("3 red lines selected", vm.SelectionLabel);
                Assert.Equal(new[] { false, true, true, true, false }, vm.Rows.Select(r => r.InSelection));
                Assert.StartsWith("● 1000 ms", vm.Rows[1].Label, StringComparison.Ordinal);
                Assert.StartsWith("0 ms", vm.Rows[0].Label, StringComparison.Ordinal);
                vm.SelectRange(3900, 4100, true);
                Assert.Equal(new[] { 1, 2, 3, 4 }, vm.SelectedIndices);
                Assert.Equal(1, vm.SelectedRow!.Index); // Adding keeps the primary row.
                vm.SelectedRow = vm.Rows[1]; // The list echoing the primary row keeps the multi-selection.
                Assert.Equal(4, vm.SelectedIndices.Count);
                vm.SelectRange(1500, 1600, false); // Empty band clears.
                Assert.Empty(vm.SelectedIndices); Assert.Null(vm.SelectedRow);
                vm.SelectAll();
                Assert.Equal(new[] { 0, 1, 2, 3, 4 }, vm.SelectedIndices);
                vm.SelectedRow = vm.Rows[3]; // A list click on another row selects just that row.
                Assert.Equal(new[] { 3 }, vm.SelectedIndices);
                vm.SelectAll();
                Assert.True(vm.BeginDrag(2000, 5, false));
                vm.UpdateDrag(2100);
                vm.Escape(); // Cancels the drag first.
                Assert.False(vm.IsDragging); Assert.Equal(2000, vm.Edits.RedLines[2].Time);
                vm.SelectAll();
                vm.Escape(); // Then clears the selection.
                Assert.Empty(vm.SelectedIndices); Assert.Null(vm.SelectedRow);
                Assert.Equal("No red line selected", vm.SelectionLabel);
            }
            finally { File.Delete(path); }
    }

    [Fact]
    public async Task MultiShiftMovesEachSelectedLineByItsOwnBeatInOneUndoStep()
    {
        using var vm = new EditorViewModel(_ => throw new InvalidOperationException(), _ => new ViewModelTests.FakePlayback());
        await vm.LoadAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "my-dearest-tv.osu"));
        var original = vm.Edits.RedLines.ToArray();
        Assert.NotEqual(original[2].BeatLength, original[3].BeatLength);
        vm.SelectRange(original[2].Time, original[3].Time, false);
        Assert.Equal(new[] { 2, 3 }, vm.SelectedIndices);
        vm.ShiftScope = RedLineScope.Selected;
        vm.ShiftBeatsCommand.Execute("0.25");
        var expected = original.Select((p, i) => i is 2 or 3 ? p with { Time = Math.Floor(p.Time + p.BeatLength / 4 + 0.5) } : p).ToArray();
        Assert.Equal(expected, vm.Edits.RedLines);
        Assert.Equal(new[] { 2, 3 }, vm.SelectedIndices); // Selection follows the moved lines.
        Assert.Contains("2 selected red lines", vm.Status, StringComparison.Ordinal);
        vm.Undo();
        Assert.Equal(original, vm.Edits.RedLines); // One step restores both.
        vm.Redo();
        Assert.Equal(expected, vm.Edits.RedLines);
    }

    [Fact]
    public async Task MultiShiftIsRejectedWhenItWouldReachAnUnselectedLineButMayPassOverSelectedOnes()
    {
        var (vm, engine, path) = await Loaded(new(0, 500), new(1000, 500), new(1500, 500), new(3000, 500));
        using (vm)
            try
            {
                vm.SelectRange(900, 1600, false);
                vm.ShiftScope = RedLineScope.Selected;
                vm.ShiftBeatsCommand.Execute("1"); // Line 1 lands where line 2 was; line 2 moves too.
                Assert.Equal(new double[] { 0, 1500, 2000, 3000 }, vm.Edits.RedLines.Select(p => p.Time));
                vm.ShiftBeatsCommand.Execute("1");
                Assert.Equal(new double[] { 0, 2000, 2500, 3000 }, engine.Timing.Select(p => p.Time));
                var before = vm.Edits.RedLines.ToArray();
                vm.ShiftBeatsCommand.Execute("1"); // 2500 -> 3000 meets the unselected line.
                Assert.Contains("neighbouring", vm.Status, StringComparison.Ordinal);
                Assert.Equal(before, vm.Edits.RedLines);
                Assert.Equal(before, engine.Timing);
                vm.ShiftMsCommand.Execute("-5");
                Assert.Equal(new double[] { 0, 1995, 2495, 3000 }, vm.Edits.RedLines.Select(p => p.Time));
                vm.Undo(); vm.Undo(); vm.Undo(); // Three applied shifts; the rejection added no step.
                Assert.Equal(new double[] { 0, 1000, 1500, 3000 }, vm.Edits.RedLines.Select(p => p.Time));
            }
            finally { File.Delete(path); }
    }

    [Fact]
    public async Task MultiDeleteIsOneUndoStepAndInsertSelectsTheNewLine()
    {
        var (vm, engine, path) = await Loaded(new(0, 500), new(1000, 500), new(1500, 500), new(3000, 500));
        using (vm)
            try
            {
                vm.SelectRange(900, 1600, false);
                vm.Delete();
                Assert.Equal(new double[] { 0, 3000 }, vm.Edits.RedLines.Select(p => p.Time));
                Assert.Equal(vm.Edits.RedLines, engine.Timing);
                Assert.Contains("2 red lines deleted", vm.Status, StringComparison.Ordinal);
                vm.Undo();
                Assert.Equal(new double[] { 0, 1000, 1500, 3000 }, vm.Edits.RedLines.Select(p => p.Time));
                vm.Redo();
                Assert.Equal(new double[] { 0, 3000 }, vm.Edits.RedLines.Select(p => p.Time));
                vm.ClearSelection(); vm.Delete(); // Nothing selected: nothing deleted.
                Assert.Equal(2, vm.Edits.RedLines.Count);
                vm.Seek(2222.5); vm.Insert();
                Assert.Equal(new[] { 1 }, vm.SelectedIndices);
                Assert.Equal(2223, vm.SelectedRow!.Point.Time);
            }
            finally { File.Delete(path); }
    }
}
