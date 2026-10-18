using TimingAnalyzer.Editor;

namespace TimingAnalyzer.Editor.Tests;

/// <summary>Remembered settings, and moving red lines onto the playhead.</summary>
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
        vm.SelectedAutoMode = "Drift 1 beat"; vm.TableTab = 2;
        string path = Path.Combine(Directory.CreateTempSubdirectory().FullName, "Redline", "settings.json");
        vm.CaptureSettings(new EditorSettings { WindowWidth = 1500, WindowHeight = 900, WindowMaximized = true }).Save(path);

        var loaded = EditorSettings.Load(path);
        Assert.Equal((1500.0, 900.0, true), (loaded.WindowWidth, loaded.WindowHeight, loaded.WindowMaximized));
        var other = NewModel();
        other.ApplySettings(loaded);
        Assert.Equal((40.0, 25.0, false, false), (other.MusicVolumePercent, other.ClickVolumePercent, other.ClickEnabled, other.FollowPlayhead));
        Assert.Equal(("0.75", 8, "Drift 1 beat", 2), (other.SelectedSpeed, other.SnapDivisor, other.SelectedAutoMode, other.TableTab));

        // Defaults: a fresh record, a missing file, a broken file; values the window would not accept are skipped.
        Assert.Equal(new EditorSettings(), EditorSettings.Load(path + ".missing"));
        File.WriteAllText(path, "{ not json");
        Assert.Equal(new EditorSettings(), EditorSettings.Load(path));
        other.ApplySettings(new EditorSettings { Speed = "9.9", SnapDivisor = 10, AutoMode = "Drift 7 beats", MusicVolumePercent = 400, TableTab = 9 });
        Assert.Equal(("0.75", 8, "Drift 1 beat", 100.0, 2), (other.SelectedSpeed, other.SnapDivisor, other.SelectedAutoMode, other.MusicVolumePercent, other.TableTab));
        other.ApplySettings(new EditorSettings());
        Assert.Equal((100.0, 70.0, true, true, "1.0", 4, "Normal", 0),
            (other.MusicVolumePercent, other.ClickVolumePercent, other.ClickEnabled, other.FollowPlayhead, other.SelectedSpeed, other.SnapDivisor, other.SelectedAutoMode, other.TableTab));
    }

    [Fact]
    public async Task ToPlayheadMovesTheSelectedLinesSoTheLastPickedLandsOnIt()
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
        // Shifts act on the selection: the window has no All / Selected box any more.
        Assert.Equal(RedLineScope.Selected, vm.ShiftScope);
        vm.ClearSelection();
        vm.MoveToPlayheadCommand.Execute(null);
        Assert.Contains("Select the red line to move", vm.Status);
    }
}
