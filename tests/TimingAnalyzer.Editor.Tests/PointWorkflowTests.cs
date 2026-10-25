using System.Globalization;
using TimingAnalyzer.Editor;

namespace TimingAnalyzer.Editor.Tests;

public class PointWorkflowTests
{
    private static EditorViewModel Create() => new(_ => throw new InvalidOperationException(), _ => new ViewModelTests.FakePlayback());

    [Fact]
    public void InsertSelectsTheNewDuplicateAndFieldsPreserveUntouchedPrecision()
    {
        using var vm = Create();
        var original = new TimingPoint(1000, 60000 / 142.9971234, 3, 2, 4, 60, Effects: 1);
        vm.Edits.Replace([original]);
        vm.Seek(1000); vm.Insert();
        Assert.Equal(1, vm.SelectedRow!.Index);
        vm.BpmText = "150.25"; vm.ApplyFields();
        Assert.Same(original, vm.Edits.RedLines[0]);
        Assert.Equal(150.25, vm.Edits.RedLines[1].Bpm, 8);
        vm.Undo(); vm.Undo();
        vm.SelectRows([0], 0);
        int version = vm.Edits.History.Version;
        vm.ApplyFields();
        Assert.Equal(version, vm.Edits.History.Version);
        Assert.Same(original, vm.Edits.RedLines[0]);
        vm.MeterText = "7"; vm.ApplyFields();
        Assert.Equal(original.Time, vm.Edits.RedLines[0].Time);
        Assert.Equal(original.BeatLength, vm.Edits.RedLines[0].BeatLength);
    }

    [Fact]
    public void InheritedCanBeAddedEditedMovedDeletedAndUndoneWithoutChangingReds()
    {
        var oldCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("th-TH");
            using var vm = Create();
            var red = new TimingPoint(0, 500, 3, 2, 5, 65, Effects: TimingPoint.Kiai | TimingPoint.OmitFirstBarline);
            vm.Edits.Replace([red]);
            vm.TableTab = 1; vm.Seek(1500); vm.InsertInherited();
            Assert.Equal(0, vm.TableTab);
            Assert.Single(vm.SelectedGreenIndices);
            Assert.False(vm.IsRedPointSelected);
            var green = Assert.Single(vm.InheritedRows).Point;
            Assert.Equal((1500.0, 1.0, 2, 5, 65, 1), (green.Time, green.Sv, green.SampleSet, green.SampleIndex, green.Volume, green.Effects));
            vm.TimeText = "1250.4"; vm.BpmText = "1.75"; vm.ApplyFields();
            Assert.Equal((1250.0, 1.75), (vm.InheritedRows[0].Point.Time, vm.InheritedRows[0].Point.Sv));
            vm.Seek(3000); vm.MoveToPlayhead();
            Assert.Equal(3000, vm.InheritedRows[0].Point.Time);
            vm.Delete(); Assert.Empty(vm.InheritedRows);
            vm.Undo(); Assert.Equal(3000, Assert.Single(vm.InheritedRows).Point.Time);
            vm.Undo(); Assert.Equal(1250, Assert.Single(vm.InheritedRows).Point.Time);
            vm.Undo(); Assert.Equal(1500, Assert.Single(vm.InheritedRows).Point.Time);
            vm.Undo(); Assert.Empty(vm.InheritedRows);
            Assert.Same(red, Assert.Single(vm.Edits.RedLines));
        }
        finally { CultureInfo.CurrentCulture = oldCulture; }
    }

    [Fact]
    public void EditingNormalizedGreenKeepsItsVisibleSvAndDoesNotMultiplyOtherGreens()
    {
        using var vm = Create();
        vm.AutoNormalizeSv = true;
        vm.Edits.Replace([new(0, 500), new(4000, 250)]);
        var original = vm.ClipboardText();
        vm.SelectTableRows([vm.InheritedRows[0]], vm.InheritedRows[0]);
        vm.BpmText = "1.5"; vm.ApplyFields();
        Assert.Equal(1.5, vm.InheritedRows[0].Point.Sv, 8);
        Assert.Equal(1, vm.InheritedRows[1].Point.Sv, 8);
        vm.Undo(); Assert.Equal(original, vm.ClipboardText());
        vm.TableTab = 2; vm.Seek(2000); vm.InsertInherited();
        Assert.Equal(2, vm.TableTab);
        Assert.Equal(2, vm.InheritedRows.Single(r => r.Point.Time == 2000).Point.Sv, 8);
    }

    [Fact]
    public void DecimalMainBpmDoesNotChangeTypedSvOrAccumulateRoundingOnBlur()
    {
        using var vm = Create();
        vm.AutoNormalizeSv = true;
        vm.Edits.Replace([new(0, 60000.0 / 130), new(1000, 60000.0 / 155), new(8000, 500)]);
        vm.Seek(2000); vm.InsertInherited();
        vm.BpmText = "1.75"; vm.ApplyFields();
        Assert.Equal(1.75, vm.InheritedRows.Single(r => r.Point.Time == 2000).Point.Sv, 10);
        string before = vm.ClipboardText();
        int version = vm.Edits.History.Version;
        vm.ApplyFields(); vm.ApplyFields();
        Assert.Equal(before, vm.ClipboardText());
        Assert.Equal(version, vm.Edits.History.Version);
    }

    [Fact]
    public void InvalidFieldsDoNotRecordAnUndoStepAndEscapeRestoresThePoint()
    {
        using var vm = Create();
        vm.Edits.Replace([new(0, 500)]);
        int version = vm.Edits.History.Version;
        vm.BpmText = "NaN"; vm.ApplyFields();
        Assert.Contains("positive", vm.Status);
        Assert.Equal(version, vm.Edits.History.Version);
        vm.RestorePointFields(); Assert.Equal("120", vm.BpmText);
        vm.Seek(1000); vm.InsertInherited();
        version = vm.Edits.History.Version;
        vm.BpmText = "20"; vm.ApplyFields();
        Assert.Contains("0.01 and 10", vm.Status);
        Assert.Equal(version, vm.Edits.History.Version);
        vm.RestorePointFields(); Assert.Equal("1", vm.BpmText);
    }

    [Fact]
    public async Task PlaybackDoesNotRedirectFieldsWhileTypingOrUsingInheritedTab()
    {
        string song = Path.GetTempFileName();
        var engine = new ViewModelTests.FakePlayback();
        try
        {
            using var vm = new EditorViewModel(_ => Task.FromResult(AudioContent.Create(new double[20000], 1000)), _ => engine);
            vm.AutoNormalizeSv = true;
            await vm.LoadAsync(song);
            vm.Edits.Replace([new(0, 500), new(4000, 250)]);
            vm.SelectRows([0], 0); vm.TogglePlay();
            vm.IsEditingPoint = true; vm.BpmText = "135";
            engine.Seek(6000); vm.RefreshPosition();
            Assert.Equal(0, vm.SelectedRow!.Index); Assert.Equal("135", vm.BpmText);
            vm.ApplyFields(); Assert.Equal(135, vm.Edits.RedLines[0].Bpm, 8);
            vm.IsEditingPoint = false;
            vm.TableTab = 2; vm.SelectTableRows([vm.InheritedRows[0]], vm.InheritedRows[0]);
            engine.Seek(8000); vm.RefreshPosition();
            Assert.Single(vm.SelectedGreenIndices); Assert.Null(vm.SelectedRow);
            vm.JumpToStartCommand.Execute(null); Assert.Equal(0, engine.PositionMs); Assert.Equal(0, vm.StartMs);
            vm.JumpToEndCommand.Execute(null); Assert.Equal(20000, engine.PositionMs); Assert.Equal(vm.ScrollMaximum, vm.StartMs);
        }
        finally { File.Delete(song); }
    }
}
