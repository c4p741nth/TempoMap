using System.Globalization;
using TimingAnalyzer.Editor;

namespace TimingAnalyzer.Editor.Tests;

public class ViewModelTests
{
    internal sealed class FakePlayback : IPlaybackEngine
    {
        public double DurationMs => 20000;
        public double PositionMs { get; private set; }
        public bool IsPlaying { get; private set; }
        public bool ClickEnabled { get; set; }
        public double Speed { get; set; } = 1;
        public double MusicVolume { get; set; } = 1;
        public double ClickVolume { get; set; } = 0.7;
        public TimingPoint[] Timing { get; private set; } = [];
        public bool Disposed { get; private set; }
        public void SetTiming(IEnumerable<TimingPoint> lines) => Timing = lines.ToArray();
        public void Play() => IsPlaying = true;
        public void Pause() => IsPlaying = false;
        public void Seek(double ms) => PositionMs = ms;
        public void Dispose() => Disposed = true;
    }
    [Fact]
    public async Task VolumeSlidersPassClampedValuesImmediatelyAndSurviveReload()
    {
        string path = Path.GetTempFileName();
        var engines = new List<FakePlayback>();
        try
        {
            using var vm = new EditorViewModel(_ => Task.FromResult(AudioContent.Create(new double[100], 1000)), _ =>
            {
                var engine = new FakePlayback(); engines.Add(engine); return engine;
            });
            Assert.Equal(100, vm.MusicVolumePercent);
            Assert.Equal(70, vm.ClickVolumePercent);
            await vm.LoadAsync(path);
            Assert.Equal(1, engines[0].MusicVolume);
            Assert.Equal(0.7, engines[0].ClickVolume);
            vm.TogglePlay(); vm.Seek(25);
            vm.MusicVolumePercent = 25; vm.ClickVolumePercent = 60;
            Assert.Equal(0.25, engines[0].MusicVolume);
            Assert.Equal(0.6, engines[0].ClickVolume);
            Assert.True(engines[0].IsPlaying);
            Assert.Equal(25, engines[0].PositionMs);
            await vm.LoadAsync(path);
            Assert.True(engines[0].Disposed);
            Assert.Equal(2, engines.Count);
            Assert.Equal(0.25, engines[1].MusicVolume);
            Assert.Equal(0.6, engines[1].ClickVolume);
            Assert.Equal("25 %", vm.MusicVolumeLabel);
            Assert.Equal("60 %", vm.ClickVolumeLabel);
            vm.MusicVolumePercent = -10; vm.ClickVolumePercent = 200;
            Assert.Equal(0, vm.MusicVolumePercent); Assert.Equal(0, engines[1].MusicVolume);
            Assert.Equal(100, vm.ClickVolumePercent); Assert.Equal(1, engines[1].ClickVolume);
            vm.ClickVolumePercent = 0;
            Assert.Equal(0, engines[1].ClickVolume);
            vm.MusicVolumePercent = 200;
            Assert.Equal(1, engines[1].MusicVolume);
            vm.MusicVolumePercent = double.NaN;
            Assert.Equal(100, vm.MusicVolumePercent);
            Assert.Contains("number", vm.Status, StringComparison.Ordinal);
        }
        finally { File.Delete(path); }
    }
    [Fact]
    public async Task LoadTransportViewportAndInvariantFields()
    {
        var old = CultureInfo.CurrentCulture;
        string path = Path.GetTempFileName();
        var playback = new FakePlayback();
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("th-TH");
            using var vm = new EditorViewModel(_ => Task.FromResult(AudioContent.Create(new double[20000], 1000)), _ => playback);
            await vm.LoadAsync(path);
            Assert.NotNull(vm.Audio);
            vm.TogglePlay(); Assert.True(playback.IsPlaying);
            vm.TogglePlay(); Assert.False(playback.IsPlaying);
            vm.Seek(1234.5); Assert.Equal(1234.5, vm.PositionMs);
            Assert.Equal("00:01:235 / 00:20:000", vm.PositionLabel); // osu! editor time, no decimals.
            vm.SelectedSpeed = "0.75"; vm.ClickEnabled = false;
            Assert.Equal(0.75, playback.Speed); Assert.False(playback.ClickEnabled);
            vm.Zoom(0.5, 0.25); Assert.Equal(5000, vm.SpanMs); Assert.Equal(1250, vm.StartMs);
            vm.Scroll(100000); Assert.Equal(15000, vm.StartMs);
            vm.TimeText = "100.5"; vm.BpmText = "150.25"; vm.MeterText = "3"; vm.ApplyFields();
            Assert.Equal(101, vm.Edits.RedLines[0].Time); // E15: whole ms.
            Assert.Equal(150.25, vm.Edits.RedLines[0].Bpm, 8);
            Assert.Equal(3, playback.Timing[0].Meter);
            vm.BpmText = "NaN"; vm.ApplyFields(); Assert.Equal(150.25, vm.Edits.RedLines[0].Bpm, 8);
        }
        finally { CultureInfo.CurrentCulture = old; File.Delete(path); }
        Assert.True(playback.Disposed);
    }
    [Fact]
    public async Task BeatmapWithoutAudioStillLoadsTimingAndResolvesRelativeFilename()
    {
        using var vm = new EditorViewModel(_ => throw new InvalidOperationException("Must not decode missing audio"), _ => new FakePlayback());
        string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "my-dearest-tv.osu");
        await vm.LoadAsync(path);
        Assert.NotNull(vm.Document);
        Assert.Equal(vm.Document.TimingPoints.Where(p => p.Uninherited), vm.Edits.RedLines);
        Assert.Equal(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, vm.Document.AudioFilename!)), vm.AudioPath);
        Assert.Contains("missing", vm.Status, StringComparison.Ordinal);
        Assert.False(vm.IsBusy);
    }
    [Fact]
    public async Task BeatmapWithDuplicateRedLinesOpensAndSaysSo()
    {
        string path = Path.Combine(Path.GetTempPath(), "dup-" + Guid.NewGuid().ToString("N") + ".osu");
        try
        {
            File.WriteAllText(path, "[General]\r\nAudioFilename: missing.mp3\r\n[TimingPoints]\r\n0,500,4,2,0,100,1,0\r\n" +
                "1000,400,4,2,0,100,1,0\r\n1000,400,4,2,0,100,1,0\r\n[HitObjects]\r\n");
            using var vm = new EditorViewModel(_ => throw new InvalidOperationException("Must not decode missing audio"), _ => new FakePlayback());
            await vm.LoadAsync(path);
            Assert.Equal([0, 1000], vm.Edits.RedLines.Select(p => p.Time));
            Assert.Contains("1 duplicate red line", vm.Status, StringComparison.Ordinal);
            Assert.DoesNotContain("Open failed", vm.Status, StringComparison.Ordinal);
        }
        finally { File.Delete(path); }
    }
    [Fact]
    public async Task NoDeviceRetainsWaveformAndBusyPreventsConcurrentLoad()
    {
        string path = Path.GetTempFileName();
        try
        {
            var pending = new TaskCompletionSource<AudioContent>();
            using var vm = new EditorViewModel(_ => pending.Task, _ => throw new IOException("No output device"));
            var load = vm.LoadAsync(path); Assert.True(vm.IsBusy);
            await vm.LoadAsync("ignored");
            pending.SetResult(AudioContent.Create(new double[100], 1000)); await load;
            Assert.NotNull(vm.Audio);
            Assert.Contains("No output device", vm.Status, StringComparison.Ordinal);
            Assert.True(vm.CanInteract);
        }
        finally { File.Delete(path); }
    }
}
