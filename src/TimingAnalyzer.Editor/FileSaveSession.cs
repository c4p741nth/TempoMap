using System.Globalization;

namespace TimingAnalyzer.Editor;

/// <summary>Atomic same-directory replacement and an immutable first backup for each edited file.</summary>
public sealed class FileSaveSession
{
    private readonly Dictionary<string, string> backups = new(StringComparer.OrdinalIgnoreCase);
    private readonly string sessionId = DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff", CultureInfo.InvariantCulture) +
        "-" + Guid.NewGuid().ToString("N")[..8];
    public string? LastBackupPath { get; private set; }

    public void Write(string path, ReadOnlySpan<byte> bytes, bool backup = true)
    {
        path = Path.GetFullPath(path);
        string temporary = TemporaryPath(path);
        try
        {
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                       65536, FileOptions.WriteThrough))
            {
                output.Write(bytes);
                output.Flush(flushToDisk: true);
            }
            CommitPrepared(temporary, path, backup);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    /// <summary>Installs an already-completed conversion without buffering the audio file in managed memory.</summary>
    public void CommitPrepared(string temporary, string path, bool backup = true)
    {
        temporary = Path.GetFullPath(temporary); path = Path.GetFullPath(path);
        if (temporary.Equals(path, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Path.GetDirectoryName(temporary), Path.GetDirectoryName(path), StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Atomic replacement requires a separate temporary file in the destination directory.");
        LastBackupPath = null;
        if (File.Exists(path))
        {
            if (backup)
            {
                if (!backups.TryGetValue(path, out string? backupPath))
                {
                    backupPath = File.Exists(path + ".bak") ? path + "." + sessionId + ".bak" : path + ".bak";
                    // Publish a complete backup before replacing the target. Never overwrite an earlier backup.
                    string stagedBackup = TemporaryPath(backupPath);
                    try
                    {
                        using (var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                        using (var output = new FileStream(stagedBackup, FileMode.CreateNew, FileAccess.Write,
                                   FileShare.None, 65536, FileOptions.WriteThrough))
                        {
                            input.CopyTo(output);
                            output.Flush(flushToDisk: true);
                        }
                        File.Move(stagedBackup, backupPath, overwrite: false);
                        backups.Add(path, backupPath);
                    }
                    finally { if (File.Exists(stagedBackup)) File.Delete(stagedBackup); }
                }
                LastBackupPath = backupPath;
            }
            File.Replace(temporary, path, destinationBackupFileName: null);
        }
        else File.Move(temporary, path, overwrite: false);
    }

    public static string TemporaryPath(string destination) => Path.Combine(Path.GetDirectoryName(Path.GetFullPath(destination))!,
        "." + Path.GetFileName(destination) + "." + Guid.NewGuid().ToString("N") + ".tmp");
}
