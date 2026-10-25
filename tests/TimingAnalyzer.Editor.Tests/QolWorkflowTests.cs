using TimingAnalyzer.Editor;

namespace TimingAnalyzer.Editor.Tests;

public class QolWorkflowTests
{
    private static EditorViewModel NewModel() => new(_ => Task.FromResult(AudioContent.Create(new double[20000], 1000)), _ => new ViewModelTests.FakePlayback());

    [Fact]
    public void ManualSvIsTheDefaultAndRedEditsDoNotChangeEnteredNumbers()
    {
        using var vm = NewModel();
        Assert.False(vm.AutoNormalizeSv);
        vm.Edits.Replace([new(0, 500), new(4000, 250)]);
        Assert.Empty(vm.InheritedRows);
        vm.Seek(2000); vm.InsertInherited(); vm.BpmText = "1.75"; vm.ApplyFields();
        vm.SelectRows([0], 0); vm.ScaleTempo(0.5);
        Assert.Equal(1.75, Assert.Single(vm.InheritedRows).Point.Sv, 10);
        Assert.Equal(60, vm.Edits.RedLines[0].Bpm);
        Assert.False(vm.CaptureSettings().AutoNormalizeSv);
    }

    [Fact]
    public void SwitchingNormalizationIsUndoableTogetherWithTheCorrespondingGreenRepresentation()
    {
        using var vm = NewModel();
        vm.Edits.Replace([new(0, 500), new(4000, 250)]);
        vm.Seek(2000); vm.InsertInherited(); vm.BpmText = "1.75"; vm.ApplyFields();
        string manual = vm.ClipboardText();
        vm.AutoNormalizeSv = true;
        Assert.Equal(1.75, vm.InheritedRows.Single(r => r.Point.Time == 2000).Point.Sv, 10);
        Assert.Equal(3, vm.InheritedRows.Count);
        vm.SelectRows([0], 0); vm.ScaleTempo(0.5);
        Assert.Equal(3.5, vm.InheritedRows.Single(r => r.Point.Time == 2000).Point.Sv, 10);
        string automatic = vm.ClipboardText();
        vm.AutoNormalizeSv = false;
        Assert.False(vm.AutoNormalizeSv);
        Assert.Equal(automatic, vm.ClipboardText());
        vm.SelectRows([0], 0); vm.ScaleTempo(0.5);
        Assert.Equal(3.5, vm.InheritedRows.Single(r => r.Point.Time == 2000).Point.Sv, 10);
        vm.Undo(); vm.Undo();
        Assert.True(vm.AutoNormalizeSv);
        Assert.Equal(automatic, vm.ClipboardText());
        vm.Undo(); vm.Undo();
        Assert.False(vm.AutoNormalizeSv);
        Assert.Equal(manual, vm.ClipboardText());
        vm.Redo(); Assert.True(vm.AutoNormalizeSv);
    }

    [Fact]
    public async Task KiaiAndManualSvSurviveRedChangesCopySaveAndReopen()
    {
        string dir = Directory.CreateTempSubdirectory().FullName, map = Path.Combine(dir, "map.osu");
        File.WriteAllText(map, "[TimingPoints]\r\n0,500,4,2,3,70,1,0\r\n");
        try
        {
            using var vm = NewModel(); await vm.LoadAsync(map);
            vm.Seek(1000); vm.InsertInherited(); vm.BpmText = "1.75"; vm.ApplyFields(); vm.KiaiEnabled = true;
            var first = Assert.Single(vm.InheritedRows).Point;
            Assert.Equal((1.75, 2, 3, 70, 1), (first.Sv, first.SampleSet, first.SampleIndex, first.Volume, first.Effects));
            vm.Seek(2000); vm.InsertInherited(); Assert.True(vm.KiaiEnabled); vm.KiaiEnabled = false;
            Assert.Equal(1, vm.InheritedRows[0].Point.Effects); Assert.Equal(0, vm.InheritedRows[1].Point.Effects);
            vm.SelectRows([0], 0); vm.ScaleTempo(2);
            vm.Save(map);
            var greens = OsuFile.Load(map).TimingPoints.Where(p => !p.Uninherited).ToArray();
            Assert.Equal([1.75, 1.75], greens.Select(p => p.Sv));
            Assert.Equal([1, 0], greens.Select(p => p.Effects));
            using var reopened = NewModel(); await reopened.LoadAsync(map);
            Assert.False(reopened.AutoNormalizeSv);
            Assert.Equal(vm.ClipboardText(), reopened.ClipboardText());
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void RepeatedCustomMultiplierAndUndoAreSeparateActions()
    {
        using var vm = NewModel(); vm.Edits.Replace([new(1000, 250)]);
        vm.TempoFactorText = "0.5";
        vm.ApplyTempoFactor(); vm.ApplyTempoFactor(); vm.ApplyTempoFactor();
        Assert.Equal(30, vm.Edits.RedLines[0].Bpm); Assert.Equal(1000, vm.Edits.RedLines[0].Time);
        vm.Undo(); Assert.Equal(60, vm.Edits.RedLines[0].Bpm);
        vm.Undo(); Assert.Equal(120, vm.Edits.RedLines[0].Bpm);
        int version = vm.Edits.History.Version;
        vm.TempoFactorText = "NaN"; vm.ApplyTempoFactor();
        Assert.Equal(version, vm.Edits.History.Version); Assert.Equal(120, vm.Edits.RedLines[0].Bpm);
    }

    [Fact]
    public async Task DetectedBpmStaysAutomaticButTypingAPendingBpmLocksIt()
    {
        string song = Path.GetTempFileName();
        try
        {
            using var vm = NewModel(); await vm.LoadAsync(song);
            await vm.AnalyzeAsync((_, _) => Task.FromResult(new TimingDetection(120, 1000, null, 0, null)));
            Assert.Equal("120", vm.AnalysisBpmText); Assert.Equal("", vm.LockedBpmText);
            vm.AnalysisBpmText = "150.25"; vm.ProposalOffsetText = "1111.4";
            vm.ApplyProposal(true);
            Assert.Equal(150.25, Assert.Single(vm.Edits.RedLines).Bpm, 8);
            Assert.Equal(1111, vm.Edits.RedLines[0].Time); Assert.Equal("150.25", vm.LockedBpmText);
            vm.AnalysisBpmText = "NaN";
            Assert.Throws<ArgumentException>(() => vm.ProposalPoint());
        }
        finally { File.Delete(song); }
    }

    [Theory]
    [InlineData("220", 220)]
    [InlineData("143", 143)]
    [InlineData("150.25", 150.25)]
    [InlineData("97.125", 97.125)]
    public async Task RerunningAutoTimeAfterTypingOverAProposalFitsOnlyOffset(string text, double bpm)
    {
        string song = Path.GetTempFileName();
        try
        {
            using var vm = NewModel(); await vm.LoadAsync(song);
            await vm.AnalyzeAsync((_, _) => Task.FromResult(new TimingDetection(219.997, 1000, null, 0, null)));
            vm.AnalysisBpmText = text;
            TimingRequest? request = null;
            await vm.AutoTimeAsync((_, r) =>
            {
                request = r;
                return Task.FromResult(new TimingDetection(r.Bpm, 1234, null, 0, null));
            }, _ => throw new InvalidOperationException("Unexpected unlocked multi-BPM analysis"),
                (_, _) => throw new InvalidOperationException("Unexpected drift analysis"));
            Assert.NotNull(request); Assert.Equal(bpm, request.Bpm);
            Assert.True(vm.Proposal!.Locked); Assert.Equal(text, vm.AnalysisBpmText);
            Assert.Equal("1234", vm.ProposalOffsetText);
            vm.ApplyProposal(true);
            Assert.Equal(bpm, Assert.Single(vm.Edits.RedLines).Bpm, 8);
            Assert.Equal(1234, vm.Edits.RedLines[0].Time);
        }
        finally { File.Delete(song); }
    }

    [Fact]
    public async Task ClearingThePendingBpmExplicitlyReturnsToAutomaticDetection()
    {
        string song = Path.GetTempFileName();
        try
        {
            using var vm = NewModel(); await vm.LoadAsync(song);
            await vm.AnalyzeAsync((_, _) => Task.FromResult(new TimingDetection(219.997, 1000, null, 0, null)));
            vm.AnalysisBpmText = "220"; vm.AnalysisBpmText = "";
            bool automatic = false;
            await vm.AutoTimeAsync((_, _) => throw new InvalidOperationException("Unexpected locked analysis"), _ =>
            {
                automatic = true;
                return Task.FromResult(new MultiTimingDetection([new TimingPoint(1500, 500)], null));
            }, (_, _) => throw new InvalidOperationException("Unexpected drift analysis"));
            Assert.True(automatic); Assert.False(vm.Proposal!.Locked);
            Assert.Equal(120, vm.ProposalPoint()!.Bpm); Assert.Equal("", vm.LockedBpmText);
        }
        finally { File.Delete(song); }
    }
}
