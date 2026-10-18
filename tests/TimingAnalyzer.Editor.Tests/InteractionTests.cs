using TimingAnalyzer.Editor;

namespace TimingAnalyzer.Editor.Tests;

public class InteractionTests
{
    [Fact]
    public void RedHitMovesWithGrabOffsetAndBeatHitStretchesFromFixedStart()
    {
        var edits = new TimingEdits([new(100, 500, 3), new(2500, 250)]);
        Assert.Null(TimingInteraction.Hit(edits.RedLines, 400, 10, false));
        var move = TimingInteraction.Hit(edits.RedLines, 104, 6, false)!;
        move.Apply(edits, 204);
        Assert.Equal(new TimingPoint(200, 500, 3), edits.RedLines[0]);
        edits.Undo(); edits.Redo();
        var stretch = TimingInteraction.Hit(edits.RedLines, 1202, 6, true)!;
        Assert.Equal(2, stretch.Beat);
        stretch.Apply(edits, 1402);
        Assert.Equal(200, edits.RedLines[0].Time);
        Assert.Equal(1400, edits.RedLines[0].Time + 2 * edits.RedLines[0].BeatLength);
        Assert.Null(TimingInteraction.Hit(edits.RedLines, 200, 6, true));
    }
    [Fact]
    public void ViewModelPreviewsWithoutMutationAndCommitsOneUndoStep()
    {
        using var vm = new EditorViewModel(_ => throw new InvalidOperationException(), _ => new ViewModelTests.FakePlayback());
        vm.Edits.Replace([new(100, 500), new(3000, 250, 3)]);
        Assert.True(vm.BeginDrag(100, 6, false));
        vm.UpdateDrag(150); vm.UpdateDrag(200);
        Assert.Equal(100, vm.Edits.RedLines[0].Time);
        Assert.Equal(200, vm.DisplayRedLines[0].Time);
        vm.CommitDrag(); Assert.Equal(200, vm.Edits.RedLines[0].Time);
        vm.Undo(); Assert.Equal(100, vm.Edits.RedLines[0].Time);
        vm.Redo(); Assert.Equal(200, vm.Edits.RedLines[0].Time);
        vm.BeginDrag(200, 6, false); vm.UpdateDrag(400); vm.CancelDrag();
        Assert.Equal(200, vm.Edits.RedLines[0].Time);
        vm.Seek(4000); vm.Insert();
        Assert.Equal(250, vm.SelectedRow!.Point.BeatLength);
        Assert.Equal(3, vm.SelectedRow.Point.Meter);
        vm.Delete(); Assert.DoesNotContain(vm.Edits.RedLines, p => p.Time == 4000);
        vm.Undo(); Assert.Contains(vm.Edits.RedLines, p => p.Time == 4000);
        vm.Redo(); Assert.DoesNotContain(vm.Edits.RedLines, p => p.Time == 4000);
    }
    [Fact]
    public void InvalidDragDoesNotCommit()
    {
        using var vm = new EditorViewModel(_ => throw new InvalidOperationException(), _ => new ViewModelTests.FakePlayback());
        vm.Edits.Replace([new(0, 500), new(2000, 500)]);
        vm.BeginDrag(500, 5, true); vm.UpdateDrag(-500); vm.CommitDrag();
        Assert.Equal(500, vm.Edits.RedLines[0].BeatLength);
        vm.BeginDrag(0, 5, false); vm.UpdateDrag(2000); vm.CommitDrag();
        Assert.Equal(0, vm.Edits.RedLines[0].Time);
    }
}
