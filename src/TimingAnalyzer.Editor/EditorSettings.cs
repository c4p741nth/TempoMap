using System.Text.Json;

namespace TimingAnalyzer.Editor;

/// <summary>
/// What the editor remembers between runs (volumes, metronome, follow, speed, beat snap, shift steps, auto-time mode,
/// table tab, window size and appearance), as JSON in the user's application data. A missing or broken file gives the defaults.
/// </summary>
public sealed record EditorSettings
{
    public double MusicVolumePercent { get; init; } = 100;
    public double ClickVolumePercent { get; init; } = 70;
    public bool ClickEnabled { get; init; } = true;
    public bool FollowPlayhead { get; init; } = true;
    public string Speed { get; init; } = "1.0";
    public int SnapDivisor { get; init; } = 4;
    /// <summary>The Shift toolbar's steps: 1/n beat and ms.</summary>
    public int ShiftBeatDivisor { get; init; } = 4;
    public int ShiftMs { get; init; } = 1;
    /// <summary>The Shift toolbar's BPM nudge step (❮ BPM ❯).</summary>
    public double BpmStep { get; init; } = 0.01;
    /// <summary>Auto-time's lines on the waveform's leading edge; its first red line on a bar's first beat (docs E43).</summary>
    public bool AlignToEdge { get; init; } = true;
    public bool StartOnBar { get; init; } = true;
    public int OsuCopyShiftMs { get; init; } = OsuTimingExport.ShiftMs;
    public CopyProfile[] CopyProfiles { get; init; } = [new("osu!", -33), new("Unshifted", 0)];
    public Dictionary<string, string> Shortcuts { get; init; } = [];
    public string AutoMode { get; init; } = EditorViewModel.NormalMode;
    public int TableTab { get; init; }
    public double WindowWidth { get; init; } = 1280;
    public double WindowHeight { get; init; } = 720;
    public bool WindowMaximized { get; init; }
    public string Theme { get; init; } = "Dark";
    public bool AutoNormalizeSv { get; init; }
    public string[] RecentFiles { get; init; } = [];
    public string? LastAudioDirectory { get; init; }
    public string? LastMapDirectory { get; init; }

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    /// <summary>%APPDATA%\TempoMap\settings.json.</summary>
    public static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        AppInfo.Name, "settings.json");

    public static EditorSettings Load(string path)
    {
        try
        {
            var settings = File.Exists(path) ? JsonSerializer.Deserialize<EditorSettings>(File.ReadAllText(path), Json) ?? new() : new();
            return settings with { RecentFiles = settings.RecentFiles?.Where(p => !string.IsNullOrWhiteSpace(p)).Take(12).ToArray() ?? [],
                Shortcuts = settings.Shortcuts ?? [], CopyProfiles = settings.CopyProfiles ?? [new("osu!", -33), new("Unshifted", 0)] };
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return new(); }
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        new FileSaveSession().Write(path, JsonSerializer.SerializeToUtf8Bytes(this, Json), backup: false);
    }
}
