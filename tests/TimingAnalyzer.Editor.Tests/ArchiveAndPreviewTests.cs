using System.IO.Compression;
using TimingAnalyzer.Editor;

namespace TimingAnalyzer.Editor.Tests;

public class ArchiveAndPreviewTests
{
    [Fact]
    public async Task ArchiveContainsEditedMapOriginalAudioAndLocalAssetsWithoutSavingSource()
    {
        string dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string audio = Path.Combine(dir, "song.mp3"), map = Path.Combine(dir, "difficulty.osu"), destination = Path.Combine(dir, "export.osz");
            byte[] originalAudio = [1, 2, 3, 4]; File.WriteAllBytes(audio, originalAudio); File.WriteAllBytes(Path.Combine(dir, "bg.jpg"), [5, 6]);
            const string original = "[General]\r\nAudioFilename: song.mp3\r\n[TimingPoints]\r\n0,500,4,1,0,100,1,0\r\n"; File.WriteAllText(map, original);
            using var vm = new EditorViewModel(_ => Task.FromResult(AudioContent.Create(new double[10000], 1000)), _ => new ViewModelTests.FakePlayback());
            await vm.LoadAsync(map); vm.Edits.Update(0, new(123, 250)); await vm.ExportArchiveAsync(destination);
            Assert.Contains("Exported", vm.Status); Assert.True(vm.IsDirty); Assert.Equal(original, File.ReadAllText(map));
            using var zip = ZipFile.OpenRead(destination); Assert.Equal(new[] { "bg.jpg", "difficulty.osu", "song.mp3" }, zip.Entries.Select(e => e.FullName).Order());
            using var read = new StreamReader(zip.GetEntry("difficulty.osu")!.Open()); Assert.Contains("123,250", await read.ReadToEndAsync());
            using var bytes = new MemoryStream(); await zip.GetEntry("song.mp3")!.Open().CopyToAsync(bytes); Assert.Equal(originalAudio, bytes.ToArray());
        }
        finally { Directory.Delete(dir, true); }
    }
    [Fact]
    public async Task ProposalAuditionNeverEditsTimingAndUndoLabelsTrackChanges()
    {
        string song = Path.GetTempFileName();
        try
        {
            var player = new ViewModelTests.FakePlayback();
            using var vm = new EditorViewModel(_ => Task.FromResult(AudioContent.Create(new double[10000], 1000)), _ => player);
            await vm.LoadAsync(song); var before = vm.Edits.RedLines.ToArray(); Assert.False(vm.UndoCommand.CanExecute(null));
            await vm.AnalyzeMultiAsync(_ => Task.FromResult(new MultiTimingDetection([new(1000, 250), new(5000, 400)])));
            vm.AuditionPending(); Assert.True(vm.AuditionProposal); Assert.Equal(before, vm.Edits.RedLines); Assert.False(vm.IsDirty);
            vm.AuditionCurrent(); Assert.False(vm.AuditionProposal);
            vm.ApplyMultiProposal(true); Assert.True(vm.CanUndo); Assert.Contains("add", vm.UndoLabel); Assert.True(vm.UndoCommand.CanExecute(null));
            vm.Undo(); Assert.False(vm.CanUndo); Assert.True(vm.CanRedo); Assert.Equal(before, vm.Edits.RedLines);
        }
        finally { File.Delete(song); }
    }
    [Fact]
    public void CopyProfilesChangeOnlyExportedTimesAndRoundTrip()
    {
        using var vm = new EditorViewModel(_ => throw new Exception(), _ => new ViewModelTests.FakePlayback());
        vm.Edits.Replace([new(1000, 500)]); vm.OsuCopyShiftMs = -17; vm.AddCopyProfile("My setup");
        Assert.StartsWith("983,", vm.OsuClipboardText()); Assert.StartsWith("1000,", vm.ClipboardText());
        using var other = new EditorViewModel(_ => throw new Exception(), _ => new ViewModelTests.FakePlayback());
        other.ApplySettings(vm.CaptureSettings()); Assert.Equal(-17, other.OsuCopyShiftMs); Assert.Contains(other.CopyProfiles, p => p.Name == "My setup" && p.ShiftMs == -17);
    }
}
