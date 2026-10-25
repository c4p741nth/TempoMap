using System.IO.Compression;

namespace TimingAnalyzer.Editor;

public static class BeatmapArchive
{
    private static readonly HashSet<string> AssetExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp", ".mp4", ".avi", ".flv", ".wmv", ".mkv", ".osb", ".wav", ".ogg", ".mp3", ".flac" };

    public static async Task WriteAsync(string destination, byte[] beatmap, string beatmapName, string audioPath, string audioEntry,
        string? assetDirectory, CancellationToken cancellation = default)
    {
        destination = Path.GetFullPath(destination); audioPath = Path.GetFullPath(audioPath);
        if (!string.Equals(Path.GetExtension(destination), ".osz", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Choose an .osz destination.");
        if (!File.Exists(audioPath)) throw new FileNotFoundException("The original audio is missing.", audioPath);
        string temporary = FileSaveSession.TemporaryPath(destination);
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            {
                using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
                {
                    var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    string SafeName(string name)
                    {
                        name = name.Replace('\\', '/');
                        if (name.StartsWith('/') || name.Split('/').Any(p => p is ".." or "." || p.Contains(':'))) throw new InvalidDataException("Unsafe archive path.");
                        if (!names.Add(name)) throw new InvalidDataException("Archive contains duplicate names: " + name);
                        return name;
                    }
                    var map = archive.CreateEntry(SafeName(beatmapName), CompressionLevel.Optimal);
                    using (var output = map.Open()) await output.WriteAsync(beatmap, cancellation).ConfigureAwait(false);
                    async Task AddFile(string path, string name)
                    {
                        cancellation.ThrowIfCancellationRequested();
                        var entry = archive.CreateEntry(SafeName(name), CompressionLevel.Fastest);
                        using var input = File.OpenRead(path); using var output = entry.Open();
                        await input.CopyToAsync(output, cancellation).ConfigureAwait(false);
                    }
                    await AddFile(audioPath, audioEntry).ConfigureAwait(false);
                    if (assetDirectory is not null)
                    {
                        var options = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = false };
                        foreach (string path in Directory.EnumerateFiles(assetDirectory, "*", options))
                        {
                            if (!AssetExtensions.Contains(Path.GetExtension(path)) || string.Equals(Path.GetFullPath(path), audioPath, StringComparison.OrdinalIgnoreCase)) continue;
                            await AddFile(path, Path.GetRelativePath(assetDirectory, path)).ConfigureAwait(false);
                        }
                    }
                }
                stream.Flush(flushToDisk: true);
            }
            cancellation.ThrowIfCancellationRequested(); new FileSaveSession().CommitPrepared(temporary, destination);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}

public sealed partial class EditorViewModel
{
    public async Task ExportArchiveAsync(string destination)
    {
        if (IsBusy || disposed) return;
        if (sourceAudioPath is not { } audio || !File.Exists(audio)) { Status = "Open the original audio before exporting a beatmap archive."; return; }
        string? root = BeatmapPath is { } mapPath ? Path.GetDirectoryName(mapPath) : null;
        string audioEntry = root is not null ? Path.GetRelativePath(root, audio) : Path.GetFileName(audio);
        if (audioEntry.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(audioEntry)) audioEntry = Path.GetFileName(audio);
        var doc = Document ?? OsuFile.Create(audioEntry);
        byte[] bytes = doc.WithTimingPoints(NormalizedTiming().Points, audioEntry.Replace('\\', '/'));
        string name = Path.GetFileName(BeatmapPath ?? "Timing.osu");
        IsBusy = true; ReportProgress(new("Exporting current difficulty and audio"));
        try
        {
            await BeatmapArchive.WriteAsync(destination, bytes, name, audio, audioEntry, root, AnalysisToken);
            Status = "Exported current difficulty, original audio and local media: " + Path.GetFileName(destination) + ".";
        }
        catch (Exception ex) { Status = "Export failed: " + ex.Message; }
        finally { IsBusy = false; }
    }
}
