using System.Text.Json;

namespace TimingAnalyzer.Editor;

/// <summary>
/// What the editor remembers between runs (volumes, metronome, follow, speed, beat snap, auto-time mode, table tab,
/// window size), as JSON in the user's application data. A missing or broken file gives the defaults.
/// </summary>
public sealed record EditorSettings
{
    public double MusicVolumePercent { get; init; } = 100;
    public double ClickVolumePercent { get; init; } = 70;
    public bool ClickEnabled { get; init; } = true;
    public bool FollowPlayhead { get; init; } = true;
    public string Speed { get; init; } = "1.0";
    public int SnapDivisor { get; init; } = 4;
    public string AutoMode { get; init; } = EditorViewModel.NormalMode;
    public int TableTab { get; init; }
    public double WindowWidth { get; init; } = 1280;
    public double WindowHeight { get; init; } = 720;
    public bool WindowMaximized { get; init; }

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    /// <summary>%APPDATA%\Redline\settings.json.</summary>
    public static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        AppInfo.Name, "settings.json");

    public static EditorSettings Load(string path)
    {
        try { return File.Exists(path) ? JsonSerializer.Deserialize<EditorSettings>(File.ReadAllText(path), Json) ?? new() : new(); }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return new(); }
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, Json));
    }
}
