using TimingAnalyzer.Editor;

namespace TimingAnalyzer.Editor.Tests;

public class SessionTests
{
    [Fact]
    public void CorruptRecoveryProposalIsSkippedAndNullSettingsCollectionsUseSafeDefaults()
    {
        string dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            File.WriteAllText(Path.Combine(dir, "broken.tmap"), "{\"MultiProposal\":{\"Sections\":null}}");
            Assert.Empty(new RecoveryStore(dir).List());
            string settingsPath = Path.Combine(dir, "settings.json");
            File.WriteAllText(settingsPath, "{\"RecentFiles\":null,\"Shortcuts\":null,\"CopyProfiles\":null}");
            var settings = EditorSettings.Load(settingsPath);
            Assert.Empty(settings.RecentFiles); Assert.Empty(settings.Shortcuts); Assert.Equal(2, settings.CopyProfiles.Length);
        }
        finally { Directory.Delete(dir, true); }
    }
    [Fact]
    public async Task ProjectRestoresManualSvBookmarksAndTypedBpmWithoutChangingSourceMap()
    {
        string dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string song = Path.Combine(dir, "song.wav"), map = Path.Combine(dir, "map.osu"), project = Path.Combine(dir, "edit.tmap");
            File.WriteAllBytes(song, []);
            const string original = "[General]\r\nAudioFilename: song.wav\r\n[TimingPoints]\r\n0,500,4,1,0,100,1,0\r\n";
            File.WriteAllText(map, original);
            using var vm = Create(); await vm.LoadAsync(map);
            Assert.False(vm.IsDirty);
            vm.Edits.ReplaceTiming(vm.Edits.RedLines, [new(2000, -50, Uninherited: false, Effects: 1)]);
            vm.JumpTo(3000); vm.AddBookmark("Chorus"); vm.AnalysisBpmText = "220";
            Assert.True(vm.IsDirty); vm.SaveProject(project); Assert.False(vm.IsDirty);
            Assert.Equal(original, File.ReadAllText(map));
            Assert.DoesNotContain(dir.Replace("\\", "\\\\"), File.ReadAllText(project));
            using var restored = Create(); await restored.LoadProjectAsync(project);
            Assert.False(restored.IsDirty); Assert.Equal(vm.Edits.GreenLines, restored.Edits.GreenLines);
            Assert.Equal("Chorus", Assert.Single(restored.Bookmarks).Name);
            Assert.Equal("220", restored.LockedBpmText); Assert.Equal(song, restored.OriginalAudioPath);
            Assert.Equal(3000, restored.PositionMs);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task UndoToSavedStateClearsDirtyAndRecoveryNeverWritesTheMap()
    {
        string dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string song = Path.Combine(dir, "song.wav"); File.WriteAllBytes(song, []);
            using var vm = Create(); await vm.LoadAsync(song);
            vm.JumpTo(2500); vm.Insert(); Assert.True(vm.IsDirty);
            var store = new RecoveryStore(Path.Combine(dir, "recovery")); store.Save(vm.CaptureProject());
            vm.Undo(); Assert.False(vm.IsDirty);
            var entry = Assert.Single(store.List()); Assert.Equal(2, entry.Project.Timing.Points.Length);
            using var recovered = Create(); await recovered.RestoreProjectAsync(entry.Project);
            Assert.True(recovered.IsDirty); Assert.Equal(2, recovered.Edits.RedLines.Count);
            store.Clear(); Assert.Empty(store.List()); Assert.Single(Directory.GetFiles(dir));
        }
        finally { Directory.Delete(dir, true); }
    }
    private static EditorViewModel Create() => new(_ => Task.FromResult(AudioContent.Create(new double[10000], 1000)), _ => new ViewModelTests.FakePlayback());
}
