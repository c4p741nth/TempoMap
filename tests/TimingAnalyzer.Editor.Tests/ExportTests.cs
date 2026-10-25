using TimingAnalyzer.Editor;

namespace TimingAnalyzer.Editor.Tests;

public class ExportTests
{
    [Fact]
    public async Task ClosingDuringLoadDoesNotOpenAnOutputDeviceAfterwards()
    {
        string path = Path.GetTempFileName();
        try
        {
            var pending = new TaskCompletionSource<AudioContent>();
            bool deviceOpened = false;
            var vm = new EditorViewModel(_ => pending.Task, _ => { deviceOpened = true; return new ViewModelTests.FakePlayback(); });
            var loading = vm.LoadAsync(path);
            vm.Dispose();
            pending.SetResult(AudioContent.Create(new double[100], 1000));
            await loading;
            Assert.False(deviceOpened);
        }
        finally { File.Delete(path); }
    }
    [Fact]
    public void ProposalUsesOsuOffsetAndApproximateNeedsManualOffset()
    {
        var exact = AutoTimingProposal.FromDetection(new(150.25, -23.4, null, 0, null));
        Assert.False(exact.Approximate);
        Assert.Equal(-23, exact.OffsetMs); // osu! offsets are whole ms.
        Assert.Equal(new TimingPoint(-23, 60000 / 150.25), exact.AtOffset("-23.4"));
        var approximate = AutoTimingProposal.FromDetection(new(null, null, 125, 16, null));
        Assert.True(approximate.Approximate); Assert.Null(approximate.OffsetMs);
        Assert.Throws<FormatException>(() => approximate.AtOffset(""));
        Assert.Equal(42, approximate.AtOffset("42").Time);
        Assert.Throws<InvalidOperationException>(() => AutoTimingProposal.FromDetection(new(null, null, null, -1, "cannot determine timing")));
    }
    [Fact]
    public async Task ProposalRequiresConfirmationAndIsUndoableWithoutTouchingGreens()
    {
        string song = Path.GetTempFileName();
        try
        {
            using var vm = new EditorViewModel(_ => Task.FromResult(AudioContent.Create(new double[200], 1000)), _ => new ViewModelTests.FakePlayback());
            await vm.LoadAsync(song);
            vm.Edits.Replace([new(100, 500), new(5000, 250)]);
            var before = vm.Edits.RedLines.ToArray();
            await vm.AnalyzeAsync(_ => Task.FromResult(new TimingDetection(180, 123, null, 0, null)));
            Assert.Equal(before, vm.Edits.RedLines);
            vm.ApplyProposal(false); Assert.Equal(before, vm.Edits.RedLines);
            vm.ApplyProposal(true); Assert.Equal(new TimingPoint(123, 60000.0 / 180), Assert.Single(vm.Edits.RedLines));
            vm.Undo(); Assert.Equal(before, vm.Edits.RedLines);
            await vm.AnalyzeAsync(_ => Task.FromResult(new TimingDetection(null, null, 125, 16, null)));
            vm.ApplyProposal(true); Assert.Equal(before, vm.Edits.RedLines);
            vm.ProposalOffsetText = "23.5"; vm.ApplyProposal(true);
            Assert.Equal(24, Assert.Single(vm.Edits.RedLines).Time);
        }
        finally { File.Delete(song); }
    }
    [Fact]
    public async Task SaveFixtureCreatesExactBackupAndSavesVisibleNormalizedGreenRows()
    {
        string dir = Path.Combine(Path.GetTempPath(), "timing-editor-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "copy.osu");
        try
        {
            byte[] original = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "steady-multi.osu"));
            File.WriteAllBytes(path, original);
            using var vm = new EditorViewModel(_ => throw new InvalidOperationException(), _ => new ViewModelTests.FakePlayback());
            await vm.LoadAsync(path);
            vm.Edits.MoveRedLine(0, vm.Edits.RedLines[0].Time + 1);
            var greens = TimingClipboard.Parse(vm.ClipboardText()).Where(p => !p.Uninherited).ToArray();
            vm.Save(path);
            Assert.Equal(original, File.ReadAllBytes(path + ".bak"));
            Assert.Equal(greens, OsuFile.Load(path).TimingPoints.Where(p => !p.Uninherited));
            Assert.Equal(greens, TimingClipboard.Parse(vm.ClipboardText()).Where(p => !p.Uninherited));
            byte[] edited = File.ReadAllBytes(path);
            vm.Save(path);
            Assert.Equal(edited, File.ReadAllBytes(path));
            Assert.Equal(edited, File.ReadAllBytes(path + ".bak"));
        }
        finally { File.Delete(path); File.Delete(path + ".bak"); Directory.Delete(dir); }
    }
    [Fact]
    public async Task AudioOnlySaveCreatesBeatmapWithRelativeAudioPath()
    {
        string song = Path.GetTempFileName(), output = song + ".osu";
        try
        {
            using var vm = new EditorViewModel(_ => Task.FromResult(AudioContent.Create(new double[100], 1000)), _ => new ViewModelTests.FakePlayback());
            await vm.LoadAsync(song); vm.Save(output);
            var doc = OsuFile.Load(output);
            Assert.Equal(Path.GetFileName(song), doc.AudioFilename);
            Assert.Equal(vm.Edits.RedLines, doc.TimingPoints);
            Assert.Equal(Path.GetFullPath(output), vm.BeatmapPath);
        }
        finally { File.Delete(song); File.Delete(output); }
    }
}
