using TimingAnalyzer.Editor;

namespace TimingAnalyzer.Editor.Tests;

/// <summary>Remembered settings, moving red lines onto the playhead, and files dropped on the window.</summary>
public class SettingsTests
{
    private static EditorViewModel NewModel() =>
        new(_ => Task.FromResult(AudioContent.Create(new double[20000], 1000)), _ => new ViewModelTests.FakePlayback());

    [Fact]
    public void SettingsRoundTripThroughAFileAndBadValuesAreIgnored()
    {
        var vm = NewModel();
        vm.MusicVolumePercent = 40; vm.ClickVolumePercent = 25; vm.ClickEnabled = false; vm.FollowPlayhead = false;
        vm.SelectedSpeed = "0.75"; vm.SelectedSnap = vm.SnapOptions.First(o => o.Divisor == 8);
        vm.SelectedAutoMode = "Drift 1 beat"; vm.TableTab = 2; vm.SelectedTheme = "Light";
        vm.AutoNormalizeSv = true;
        vm.SelectedShiftBeatStep = vm.ShiftBeatSteps.First(o => o.Divisor == 32); vm.SelectedShiftMsStep = 5;
        string path = Path.Combine(Directory.CreateTempSubdirectory().FullName, "TempoMap", "settings.json");
        vm.CaptureSettings(new EditorSettings { WindowWidth = 1500, WindowHeight = 900, WindowMaximized = true }).Save(path);

        var loaded = EditorSettings.Load(path);
        Assert.Equal("Light", loaded.Theme);
        Assert.True(loaded.AutoNormalizeSv);
        Assert.Equal((1500.0, 900.0, true), (loaded.WindowWidth, loaded.WindowHeight, loaded.WindowMaximized));
        var other = NewModel();
        other.ApplySettings(loaded);
        Assert.Equal("Light", other.SelectedTheme);
        Assert.True(other.AutoNormalizeSv);
        Assert.Equal((40.0, 25.0, false, false), (other.MusicVolumePercent, other.ClickVolumePercent, other.ClickEnabled, other.FollowPlayhead));
        Assert.Equal(("0.75", 8, "Drift 1 beat", 2), (other.SelectedSpeed, other.SnapDivisor, other.SelectedAutoMode, other.TableTab));
        Assert.Equal((32, 5), (other.SelectedShiftBeatStep.Divisor, other.SelectedShiftMsStep));

        // Defaults: a fresh record, a missing file, a broken file; values the window would not accept are skipped.
        Assert.Equivalent(new EditorSettings(), EditorSettings.Load(path + ".missing"));
        File.WriteAllText(path, "{ not json");
        Assert.Equivalent(new EditorSettings(), EditorSettings.Load(path));
        other.ApplySettings(new EditorSettings { Speed = "9.9", SnapDivisor = 10, AutoMode = "Drift 7 beats", MusicVolumePercent = 400, TableTab = 9,
            ShiftBeatDivisor = 3, ShiftMs = 7, Theme = "invalid-theme" });
        Assert.Equal("Light", other.SelectedTheme);
        Assert.Equal(("0.75", 8, "Drift 1 beat", 100.0, 2), (other.SelectedSpeed, other.SnapDivisor, other.SelectedAutoMode, other.MusicVolumePercent, other.TableTab));
        Assert.Equal((32, 5), (other.SelectedShiftBeatStep.Divisor, other.SelectedShiftMsStep));
        other.ApplySettings(new EditorSettings());
        Assert.Equal("Dark", other.SelectedTheme);
        Assert.False(other.AutoNormalizeSv);
        File.WriteAllText(path, "{\"MusicVolumePercent\":35}");
        Assert.Equal("Dark", EditorSettings.Load(path).Theme); // Existing settings predate appearance choices.
        Assert.Equal((100.0, 70.0, true, true, "1.0", 4, "Normal", 0),
            (other.MusicVolumePercent, other.ClickVolumePercent, other.ClickEnabled, other.FollowPlayhead, other.SelectedSpeed, other.SnapDivisor, other.SelectedAutoMode, other.TableTab));
        Assert.Equal((4, 1), (other.SelectedShiftBeatStep.Divisor, other.SelectedShiftMsStep));
    }

    [Fact]
    public async Task StartingWithRememberedNormalizeSvAsksNothingButEditingAnOpenSongDoes()
    {
        // The remembered Auto normalize SV is applied before any file is open: with nothing open there is nothing to save,
        // so closing must not ask (it did, because the saved baseline was captured with the choice off).
        var vm = NewModel();
        vm.ApplySettings(new EditorSettings { AutoNormalizeSv = true });
        Assert.False(vm.IsDirty);
        await vm.LoadAsync(Path.GetTempFileName());
        Assert.False(vm.IsDirty);
        vm.AutoNormalizeSv = false; // With a song open the choice changes what Save writes, so it still counts as an edit.
        Assert.True(vm.IsDirty);
    }

    [Fact]
    public void ViewListeningAndLayoutChoicesAreRememberedAndBadOnesIgnored()
    {
        var vm = NewModel();
        vm.WaveformLayer = "Spectrogram"; vm.ShowOnsets = true; vm.ShowSv = false; vm.ShowKiai = false; vm.MeasureView = true; vm.GhostProposal = false;
        vm.LoopEnabled = true; vm.PreRollBeats = 4; vm.ComparisonSpeed = "0.5";
        string path = Path.Combine(Directory.CreateTempSubdirectory().FullName, "TempoMap", "settings.json");
        vm.CaptureSettings(new EditorSettings { TablePanelWidth = 420, TimingToolsExpanded = false }).Save(path);

        var loaded = EditorSettings.Load(path);
        Assert.Equal((420.0, false), (loaded.TablePanelWidth, loaded.TimingToolsExpanded));
        var other = NewModel();
        other.ApplySettings(loaded);
        Assert.Equal(("Spectrogram", true, false, false, true, false), (other.WaveformLayer, other.ShowOnsets, other.ShowSv, other.ShowKiai, other.MeasureView, other.GhostProposal));
        Assert.Equal((true, 4, "0.5"), (other.LoopEnabled, other.PreRollBeats, other.ComparisonSpeed));

        // A layer or speed the window does not offer is skipped (the earlier choice stays); the pre-roll is clamped like the box clamps it.
        other.ApplySettings(new EditorSettings { WaveformLayer = "Bogus", ComparisonSpeed = "9.9", PreRollBeats = 99 });
        Assert.Equal(("Spectrogram", "0.5", 16), (other.WaveformLayer, other.ComparisonSpeed, other.PreRollBeats));
        // The defaults of a fresh record are what a first run shows.
        var fresh = new EditorSettings();
        Assert.Equal(("Full", false, true, true, false, true), (fresh.WaveformLayer, fresh.ShowOnsets, fresh.ShowSv, fresh.ShowKiai, fresh.MeasureView, fresh.GhostProposal));
        Assert.Equal((350.0, true), (fresh.TablePanelWidth, fresh.TimingToolsExpanded));
    }

    [Fact]
    public async Task MoveHereMovesTheSelectedLinesSoTheLastPickedLandsOnThePlayhead()
    {
        var vm = NewModel();
        await vm.LoadAsync(Path.GetTempFileName());
        vm.Edits.Replace([new TimingPoint(1000, 500), new TimingPoint(3000, 400), new TimingPoint(6000, 300)]);
        vm.SelectRows([1, 2], 1);
        vm.Seek(3012.4);
        vm.MoveToPlayheadCommand.Execute(null);
        Assert.Equal([1000.0, 3012, 6012], vm.Edits.RedLines.Select(p => p.Time));
        Assert.Equal([1, 2], vm.SelectedIndices);
        Assert.Contains("2 red lines moved by +12 ms onto the playhead", vm.Status);
        vm.MoveToPlayheadCommand.Execute(null);
        Assert.Equal("The red line is already on the playhead.", vm.Status);
        vm.Undo();
        Assert.Equal([1000.0, 3000, 6000], vm.Edits.RedLines.Select(p => p.Time));
        // One line, back onto an earlier spot; BPM and meter stay.
        vm.SelectRows([1], 1);
        vm.Seek(2950);
        vm.MoveToPlayheadCommand.Execute(null);
        Assert.Equal(new TimingPoint(2950, 400), vm.Edits.RedLines[1]);
        Assert.Contains("Red line moved by -50 ms", vm.Status);
        // Past a neighbour it is rejected, as a shift is, and adds no undo step.
        vm.Seek(6500);
        vm.MoveToPlayheadCommand.Execute(null);
        Assert.Contains("neighbouring", vm.Status);
        Assert.Equal([1000.0, 2950, 6000], vm.Edits.RedLines.Select(p => p.Time));
        vm.Undo();
        Assert.Equal([1000.0, 3000, 6000], vm.Edits.RedLines.Select(p => p.Time));
        vm.ClearSelection();
        vm.MoveToPlayheadCommand.Execute(null);
        Assert.Contains("Select the red line to move", vm.Status);
    }

    [Fact]
    public async Task DroppedFilesOpenABeatmapFirstThenAudioAndExplainAnythingElse()
    {
        string dir = Directory.CreateTempSubdirectory().FullName;
        string map = Path.Combine(dir, "map.osu"), song = Path.Combine(dir, "song.MP3"), notes = Path.Combine(dir, "notes.txt");
        File.WriteAllText(map, "osu file format v14\r\n\r\n[General]\r\nAudioFilename: song.MP3\r\n\r\n[TimingPoints]\r\n120,500,4,2,0,100,1,0\r\n");
        File.WriteAllBytes(song, []); File.WriteAllText(notes, "");
        var vm = NewModel();
        await vm.OpenDroppedAsync([notes, song, map]);
        Assert.Equal((map, song), (vm.BeatmapPath, vm.AudioPath));
        Assert.Equal(120, vm.Edits.RedLines[0].Time);
        await vm.OpenDroppedAsync([notes, song]);
        Assert.Equal(((string?)null, song), (vm.BeatmapPath, vm.AudioPath));
        await vm.OpenDroppedAsync([notes]);
        Assert.Equal(song, vm.AudioPath);
        Assert.Equal("Drop an osu! beatmap (.osu) or an audio file (" + string.Join(", ", EditorViewModel.AudioExtensions) + ") to open it.", vm.Status);
        Assert.True(EditorViewModel.CanOpen("A.OSU") && EditorViewModel.CanOpen("b.flac") && !EditorViewModel.CanOpen("c.osz") && !EditorViewModel.CanOpen("d"));
    }
}
