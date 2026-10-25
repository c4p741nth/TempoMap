using TimingAnalyzer.Editor;

namespace TimingAnalyzer.Editor.Tests;

public class WorkflowFollowupTests
{
    [Fact]
    public async Task PreparedAudioIsUsedForWaveformPlaybackAndSavedAudioFilename()
    {
        string dir = Directory.CreateTempSubdirectory().FullName;
        string source = Path.Combine(dir, "source.wav"), prepared = Path.Combine(dir, "source.ogg"), map = Path.Combine(dir, "map.osu");
        File.WriteAllBytes(source, []);
        File.WriteAllText(map, "[General]\r\nAudioFilename: source.wav\r\n[TimingPoints]\r\n1000,500,4,1,0,100,1,0\r\n");
        try
        {
            string? decoded = null, played = null;
            using var vm = new EditorViewModel(path => { decoded = path; return Task.FromResult(AudioContent.Create(new double[20000], 1000)); },
                path => { played = path; return new ViewModelTests.FakePlayback(); }, prepareAudio: (_, _) => Task.FromResult(prepared));
            await vm.LoadAsync(map);
            Assert.Equal(prepared, decoded); Assert.Equal(prepared, played); Assert.Equal(prepared, vm.AudioPath);
            vm.Save(map);
            Assert.Equal("source.ogg", OsuFile.Load(map).AudioFilename);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task AudioPreparationMustSucceedBeforeDecoderOrPlaybackRuns()
    {
        string song = Path.GetTempFileName();
        try
        {
            int decoded = 0;
            using var vm = new EditorViewModel(_ => { decoded++; return Task.FromResult(AudioContent.Create([], 1000)); },
                _ => new ViewModelTests.FakePlayback(), prepareAudio: (_, _) => throw new IOException("conversion control failure"));
            await vm.LoadAsync(song);
            Assert.Equal(0, decoded);
            Assert.Contains("conversion control failure", vm.Status);
            Assert.Null(vm.Audio);
        }
        finally { File.Delete(song); }
    }
    [Fact]
    public async Task MultiBpmShowsNormalizedGreensIncludingMainTempoUnitLine()
    {
        string song = Path.GetTempFileName();
        try
        {
            using var vm = new EditorViewModel(_ => Task.FromResult(AudioContent.Create(new double[20000], 1000)),
                _ => new ViewModelTests.FakePlayback());
            vm.AutoNormalizeSv = true;
            await vm.LoadAsync(song);
            vm.Edits.Replace([new(0, 500), new(4000, 250)]);
            Assert.Equal(2, vm.InheritedRows.Count);
            Assert.Equal(2, vm.InheritedRows[0].Point.Sv, 8);
            Assert.Equal(1, vm.InheritedRows[1].Point.Sv, 8); // 240 BPM holds 16 of 20 seconds.
        }
        finally { File.Delete(song); }
    }

    [Fact]
    public void PasteDuplicatesInsteadOfReplacingAndUndoRestoresTheOriginalRows()
    {
        using var vm = new EditorViewModel(_ => throw new InvalidOperationException(), _ => new ViewModelTests.FakePlayback());
        vm.AutoNormalizeSv = true;
        vm.Edits.Replace([new(1000, 500), new(6000, 250)]);
        var before = vm.ClipboardText();
        vm.Paste("1000,500,4,1,0,100,1,0\r\n1000,-100,4,1,0,100,0,0\r\n");
        Assert.Equal(3, vm.Edits.RedLines.Count);
        Assert.Equal(2, vm.InheritedRows.Count(row => row.Point.Time == 1000));
        vm.Undo(); Assert.Equal(before, vm.ClipboardText());
        vm.Redo(); Assert.Equal(3, vm.Edits.RedLines.Count);
    }

    [Fact]
    public void DuplicateRedLinesUseTheFirstForPlayback()
    {
        TimingPoint[] points = [new(0, 500), new(0, 250), new(1500, 500)];
        Assert.Equal(new[] { 0.0, 500, 1000 }, BeatGrid.Query(points, 0, 1500).Select(p => p.Time));
        Assert.Equal(500, BeatSnap.Step(points, 0, 1, 1));
    }

    [Fact]
    public async Task OpeningOrDroppingAnotherSongClearsAnalysisInputs()
    {
        string first = Path.GetTempFileName(), second = first + ".wav";
        File.WriteAllBytes(second, []);
        try
        {
            using var vm = new EditorViewModel(_ => Task.FromResult(AudioContent.Create(new double[10000], 1000)),
                _ => new ViewModelTests.FakePlayback());
            await vm.LoadAsync(first);
            vm.LockedBpmText = "143"; vm.ProposalOffsetText = "437";
            await vm.LoadAsync(second);
            Assert.Equal("", vm.LockedBpmText); Assert.Equal("", vm.ProposalOffsetText);
            vm.LockedBpmText = "180"; vm.ProposalOffsetText = "49108";
            await vm.OpenDroppedAsync([second]);
            Assert.Equal("", vm.LockedBpmText); Assert.Equal("", vm.ProposalOffsetText);
        }
        finally { File.Delete(first); File.Delete(second); }
    }

    [Theory]
    [InlineData(1, 1008)]
    [InlineData(-1, 992)]
    public void SixtyFourthBeatShiftIsSupported(int direction, double expected)
    {
        var edits = new TimingEdits([new TimingPoint(1000, 500)]);
        edits.ShiftRedLines(RedLineScope.All, direction / 64.0);
        Assert.Equal(expected, edits.RedLines[0].Time);
        edits.Undo(); Assert.Equal(1000, edits.RedLines[0].Time);
    }
}
