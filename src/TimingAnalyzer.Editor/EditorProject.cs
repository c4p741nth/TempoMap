using System.Text.Json;

namespace TimingAnalyzer.Editor;

public sealed record Bookmark(Guid Id, string Name, double TimeMs)
{
    public Bookmark Validate()
    {
        if (string.IsNullOrWhiteSpace(Name) || Name.Length > 80 || !double.IsFinite(TimeMs) || TimeMs < 0)
            throw new ArgumentException("A bookmark needs a name (1–80 characters) and a valid song time.");
        return this;
    }
    public override string ToString() => TimeFormat.Osu(TimeMs) + " · " + Name;
}

/// <summary>Editable session data, independent of the game's exported beatmap.</summary>
public sealed record EditorProject
{
    public int Version { get; init; } = 1;
    public string? AudioPath { get; init; }
    public string? BeatmapPath { get; init; }
    public byte[]? BeatmapBytes { get; init; }
    public TimingState Timing { get; init; } = new([], false);
    public Bookmark[] Bookmarks { get; init; } = [];
    public AutoTimingProposal? Proposal { get; init; }
    public MultiTimingProposal? MultiProposal { get; init; }
    public string LockedBpm { get; init; } = "";
    public string PendingBpm { get; init; } = "";
    public string PendingOffset { get; init; } = "";
    public string AutoMode { get; init; } = "Normal";
    public double PositionMs { get; init; }
    public double StartMs { get; init; }
    public double SpanMs { get; init; } = 10000;
    public double? RangeStart { get; init; }
    public double? RangeEnd { get; init; }
    public int TableTab { get; init; }
    public bool LoopEnabled { get; init; }
    public int PreRollBeats { get; init; }
    public string ComparisonSpeed { get; init; } = "0.75";
    public double? AnchorA { get; init; }
    public double? AnchorB { get; init; }
    public string AnchorBeats { get; init; } = "16";
    public string WaveformLayer { get; init; } = "Full";
    public bool ShowOnsets { get; init; }
    public bool ShowSv { get; init; } = true;
    public bool ShowKiai { get; init; } = true;
    public bool MeasureView { get; init; }
    public double[] ReviewedTimes { get; init; } = [];
    public SectionMark[] SectionMarks { get; init; } = [];
    public DateTimeOffset SavedUtc { get; init; } = DateTimeOffset.UtcNow;

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, IgnoreReadOnlyProperties = true };

    public EditorProject Validate()
    {
        if (Version != 1) throw new InvalidDataException("Unsupported TempoMap project version.");
        if (Timing?.Points is null || Bookmarks is null || ReviewedTimes is null || SectionMarks is null || !double.IsFinite(PositionMs) || !double.IsFinite(StartMs) ||
            !double.IsFinite(SpanMs) || SpanMs <= 0 || RangeStart is { } a && !double.IsFinite(a) ||
            RangeEnd is { } b && !double.IsFinite(b) || Proposal is not null && MultiProposal is not null ||
            AnchorA is { } anchorA && !double.IsFinite(anchorA) || AnchorB is { } anchorB && !double.IsFinite(anchorB) ||
            Timing.Points.Any(p => p is null) || Bookmarks.Any(p => p is null) || SectionMarks.Any(p => p is null) || ReviewedTimes.Any(t => !double.IsFinite(t)))
            throw new InvalidDataException("Invalid project data.");
        foreach (var point in Timing.Points) point.Validate();
        foreach (var marker in Bookmarks) marker.Validate();
        if (Proposal is { } single && (!double.IsFinite(single.Bpm) || single.Bpm <= 0 || single.OffsetMs is { } offset && !double.IsFinite(offset)))
            throw new InvalidDataException("Invalid pending timing proposal.");
        if (MultiProposal is { } multi)
        {
            if (multi.Sections is null || multi.Sections.Count == 0 || multi.Sections.Any(p => p is null)) throw new InvalidDataException("Invalid proposed sections.");
            foreach (var point in multi.Sections) point.Validate();
        }
        if (SectionMarks.Any(m => m.Index < 0 || m.Index >= Timing.Points.Count(p => p.Uninherited))) throw new InvalidDataException("Invalid section review index.");
        return this;
    }

    public void Save(string path, FileSaveSession? session = null, bool backup = true)
    {
        Validate(); path = Path.GetFullPath(path);
        string directory = Path.GetDirectoryName(path)!;
        string? Relative(string? value) => value is null ? null : Path.GetRelativePath(directory, Path.GetFullPath(value));
        var portable = this with { AudioPath = Relative(AudioPath), BeatmapPath = Relative(BeatmapPath), SavedUtc = DateTimeOffset.UtcNow };
        (session ?? new FileSaveSession()).Write(path, JsonSerializer.SerializeToUtf8Bytes(portable, Json), backup);
    }

    public static EditorProject Load(string path)
    {
        path = Path.GetFullPath(path);
        if (new FileInfo(path).Length > 64 * 1024 * 1024) throw new InvalidDataException("Project exceeds 64 MB.");
        var project = JsonSerializer.Deserialize<EditorProject>(File.ReadAllBytes(path), Json)?.Validate()
            ?? throw new InvalidDataException("Empty project.");
        string directory = Path.GetDirectoryName(path)!;
        string? Absolute(string? value) => value is null ? null : Path.GetFullPath(Path.Combine(directory, value));
        return project with { AudioPath = Absolute(project.AudioPath), BeatmapPath = Absolute(project.BeatmapPath) };
    }
}

public sealed record RecoveryEntry(string Path, EditorProject Project)
{
    public override string ToString() => System.IO.Path.GetFileName(Project.AudioPath ?? Project.BeatmapPath ?? "Untitled") +
        " · " + Project.SavedUtc.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture) + " UTC";
}

public sealed class RecoveryStore(string directory)
{
    private readonly FileSaveSession writes = new();
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetFullPath(directory), Guid.NewGuid().ToString("N") + ".tmap");
    public void Save(EditorProject project)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        project.Save(Path, writes, backup: false);
    }
    public void Clear() { if (File.Exists(Path)) File.Delete(Path); }
    public IReadOnlyList<RecoveryEntry> List()
    {
        if (!Directory.Exists(directory)) return [];
        var result = new List<RecoveryEntry>();
        foreach (string path in Directory.EnumerateFiles(directory, "*.tmap"))
        {
            try { result.Add(new(path, EditorProject.Load(path))); }
            catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or ArgumentException or UnauthorizedAccessException) { }
        }
        return result.OrderByDescending(x => x.Project.SavedUtc).ToArray();
    }
}
