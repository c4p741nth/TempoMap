using TimingAnalyzer.Editor;

namespace TimingAnalyzer.Editor.Tests;

public class SongInfoTests
{
    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    [Fact]
    public void MetadataNamesTheSong()
    {
        var doc = OsuFile.Load(Fixture("m1ll10n-pp.osu"));
        Assert.Equal("Camellia", doc.Metadata["Artist"]);
        Assert.Equal("かめりあ", doc.Metadata["ArtistUnicode"]);
        Assert.Equal("Camellia - M1LLI0N PP [DOM1NATI0N] (Hydria)", doc.SongTitle);
        Assert.Null(OsuFile.Parse("[General]\nAudioFilename: a.mp3\n"u8.ToArray()).SongTitle);
        Assert.Equal("Unknown - Untitled [Timing] (Timing Analyzer)", OsuFile.Create("song.ogg").SongTitle);
    }

    [Fact]
    public async Task TheWindowShowsWhatIsLoaded()
    {
        using var vm = new EditorViewModel(_ => throw new InvalidOperationException(), _ => new ViewModelTests.FakePlayback());
        Assert.Equal("No song loaded", vm.SongTitle);
        Assert.Equal("Redline - timing editor for osu! by C4P741N", vm.WindowTitle);
        await vm.LoadAsync(Fixture("my-dearest-tv.osu"));
        Assert.Equal("supercell - My Dearest (TV Size) [Bandusty's Insane] (-[ Peachy ]-)", vm.SongTitle);
        Assert.Equal(vm.SongTitle + " - Redline by C4P741N", vm.WindowTitle);

        string audio = Path.Combine(Path.GetTempPath(), "timing-" + Guid.NewGuid().ToString("N") + ".ogg");
        File.WriteAllBytes(audio, []);
        try
        {
            using var audioOnly = new EditorViewModel(_ => Task.FromResult(AudioContent.Create(new double[1000], 1000)), _ => new ViewModelTests.FakePlayback());
            await audioOnly.LoadAsync(audio);
            Assert.Equal(Path.GetFileName(audio), audioOnly.SongTitle);
        }
        finally { File.Delete(audio); }
    }

    [Fact]
    public async Task PickingARowInTheListJumpsToItsLine()
    {
        string path = Path.GetTempFileName();
        try
        {
            var engine = new ViewModelTests.FakePlayback();
            using var vm = new EditorViewModel(_ => Task.FromResult(AudioContent.Create(new double[60000], 1000)), _ => engine);
            await vm.LoadAsync(path);
            vm.Edits.Replace([new(0, 500), new(30000, 400), new(45000, 375)]);
            vm.SelectedRow = vm.Rows[1];
            Assert.Equal(30000, engine.PositionMs);
            Assert.Equal(30000, vm.PositionMs);
            Assert.Equal(30000 - vm.SpanMs / 3, vm.StartMs, 6); // On screen, one third from the left.
            vm.Seek(5000);
            vm.SelectedRow = vm.Rows[1]; // The list echoing the same row is not a new pick.
            Assert.Equal(5000, vm.PositionMs);
            vm.SelectedRow = vm.Rows[2];
            Assert.Equal(45000, vm.PositionMs);
            vm.SelectRange(-1, 1, false); // Box selection does not move the playhead.
            Assert.Equal(45000, vm.PositionMs);
        }
        finally { File.Delete(path); }
    }
}
