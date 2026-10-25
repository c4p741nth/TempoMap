using System.Text;
using TimingAnalyzer.Editor;

namespace TimingAnalyzer.Editor.Tests;

public class FileSafetyTests
{
    [Fact]
    public void ExistingBackupIsNeverOverwrittenAndANewSessionCapturesItsStartingFile()
    {
        string directory = Directory.CreateTempSubdirectory().FullName, path = Path.Combine(directory, "map.osu");
        try
        {
            File.WriteAllText(path, "session start"); File.WriteAllText(path + ".bak", "original");
            var session = new FileSaveSession();
            session.Write(path, Encoding.UTF8.GetBytes("first edit"));
            string backup = session.LastBackupPath!;
            session.Write(path, Encoding.UTF8.GetBytes("second edit"));
            Assert.Equal(backup, session.LastBackupPath);
            Assert.Equal("session start", File.ReadAllText(backup));
            Assert.Equal("original", File.ReadAllText(path + ".bak"));
            Assert.Equal("second edit", File.ReadAllText(path));
            Assert.Equal(2, Directory.GetFiles(directory, "*.bak").Length);
            Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void FailedAtomicReplacementLeavesTheTargetAndBackupIntact()
    {
        string directory = Directory.CreateTempSubdirectory().FullName, path = Path.Combine(directory, "map.osu");
        try
        {
            File.WriteAllText(path, "original");
            using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                Assert.ThrowsAny<IOException>(() => new FileSaveSession().Write(path, "replacement"u8));
            Assert.Equal("original", File.ReadAllText(path));
            Assert.Equal("original", File.ReadAllText(path + ".bak"));
            Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task OpeningAnyAudioDoesNotLaunchFfmpegOrCreateConvertedSiblings()
    {
        string directory = Directory.CreateTempSubdirectory().FullName;
        try
        {
            foreach (string extension in new[] { ".mp3", ".wav", ".flac", ".ogg" })
            {
                string path = Path.Combine(directory, "song" + extension); File.WriteAllBytes(path, [1, 2, 3]);
                var import = new AudioImport("missing-encoder-control", "missing-probe-control");
                Assert.Equal(path, await import.PrepareAsync(path));
                Assert.False(import.Diagnose().EncoderAvailable);
            }
            Assert.Equal(4, Directory.GetFiles(directory).Length);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task ConversionRejectsOverwritingItsSourceAndInvalidSettingsBeforeLaunchingTools()
    {
        var import = new AudioImport("missing-encoder-control");
        await Assert.ThrowsAsync<ArgumentException>(() => import.ConvertAsync("source.ogg", "source.ogg"));
        await Assert.ThrowsAsync<ArgumentException>(() => import.ConvertAsync("source.wav", "out.ogg", new(0, 6)));
        await Assert.ThrowsAsync<ArgumentException>(() => import.ConvertAsync("source.wav", "out.ogg", new(44100, double.NaN)));
    }
}
