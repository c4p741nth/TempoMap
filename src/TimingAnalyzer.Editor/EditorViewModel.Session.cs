namespace TimingAnalyzer.Editor;

public sealed partial class EditorViewModel
{
    private TimingState savedTiming = new([], false);
    private Bookmark[] bookmarks = [], savedBookmarks = [];
    private FileSaveSession projectSaveSession = new();
    public string? ProjectPath { get; private set; }
    public string? OriginalAudioPath => sourceAudioPath;
    public IReadOnlyList<Bookmark> Bookmarks => bookmarks;
    /// <summary>Edits not yet saved. With nothing open there is nothing to save, so a fresh window is never dirty.</summary>
    public bool IsDirty => HasWorkspace && (savedTiming.AutoNormalizeSv != Edits.AutoNormalizeSv ||
        !savedTiming.Points.SequenceEqual(Edits.RedLines.Concat(Edits.GreenLines)) || !savedBookmarks.SequenceEqual(bookmarks));
    public bool HasWorkspace => AudioPath is not null || BeatmapPath is not null || ProjectPath is not null;
    private TimingState CaptureTiming() => new(Edits.RedLines.Concat(Edits.GreenLines).ToArray(), Edits.AutoNormalizeSv);

    private void NotifyDirty()
    {
        OnPropertyChanged(nameof(IsDirty)); OnPropertyChanged(nameof(WindowTitle));
        OnPropertyChanged(nameof(HasWorkspace)); OnPropertyChanged(nameof(ProjectPath));
        OnPropertyChanged(nameof(Bookmarks));
    }
    private void MarkSaved(bool includeWorkspace)
    {
        savedTiming = CaptureTiming();
        if (includeWorkspace) savedBookmarks = bookmarks.ToArray();
        NotifyDirty();
    }

    public EditorProject CaptureProject() => new()
    {
        AudioPath = sourceAudioPath, BeatmapPath = BeatmapPath,
        BeatmapBytes = Document?.WithRedLines(Document.RedLines), Timing = CaptureTiming(),
        Bookmarks = bookmarks.ToArray(), Proposal = Proposal, MultiProposal = MultiProposal,
        LockedBpm = LockedBpmText, PendingBpm = AnalysisBpmText, PendingOffset = ProposalOffsetText,
        AutoMode = SelectedAutoMode, PositionMs = PositionMs, StartMs = StartMs, SpanMs = SpanMs,
        RangeStart = range?.Start, RangeEnd = range?.End, TableTab = TableTab,
        LoopEnabled = LoopEnabled, PreRollBeats = PreRollBeats, ComparisonSpeed = ComparisonSpeed,
        AnchorA = AnchorA, AnchorB = AnchorB, AnchorBeats = AnchorBeats,
        WaveformLayer = WaveformLayer, ShowOnsets = ShowOnsets, ShowSv = ShowSv, ShowKiai = ShowKiai,
        MeasureView = MeasureView, ReviewedTimes = reviewedSections.ToArray(), SectionMarks = Marks.ToArray(),
    };

    public void SaveProject(string path) => Run(() =>
    {
        if (!HasWorkspace) throw new InvalidOperationException("Open audio or a beatmap first.");
        CaptureProject().Save(path, projectSaveSession);
        ProjectPath = Path.GetFullPath(path); MarkSaved(includeWorkspace: true);
        Status = "Project saved: " + Path.GetFileName(path) + ".";
    });

    public async Task LoadProjectAsync(string path)
    {
        if (IsBusy || disposed) return;
        EditorProject project;
        try { project = EditorProject.Load(path); }
        catch (Exception ex) { Status = "Open project failed: " + ex.Message; return; }
        await RestoreProjectAsync(project);
        ProjectPath = Path.GetFullPath(path); projectSaveSession = new();
        MarkSaved(includeWorkspace: true);
    }

    public async Task RestoreProjectAsync(EditorProject project)
    {
        if (IsBusy || disposed) return;
        project.Validate();
        if (project.AudioPath is { } audioPath && File.Exists(audioPath)) await LoadAsync(audioPath);
        else
        {
            playback?.Dispose(); playback = null; Audio = null; AudioPath = project.AudioPath;
            sourceAudioPath = project.AudioPath; CancelDrag();
        }
        Document = project.BeatmapBytes is { } bytes ? OsuFile.Parse(bytes) : null;
        BeatmapPath = project.BeatmapPath;
        Edits.Changed -= TimingChanged;
        Edits = new(project.Timing.Points.Where(p => p.Uninherited), project.Timing.Points.Where(p => !p.Uninherited), project.Timing.AutoNormalizeSv);
        Edits.Changed += TimingChanged;
        bookmarks = project.Bookmarks.OrderBy(b => b.TimeMs).ToArray();
        SelectedAutoMode = project.AutoMode;
        Proposal = project.Proposal; MultiProposal = project.MultiProposal;
        proposalBpmText = project.PendingBpm; LockedBpmText = project.LockedBpm;
        ProposalOffsetText = project.PendingOffset; TableTab = project.TableTab;
        span = Math.Clamp(project.SpanMs, 1, Math.Max(1, DurationMs));
        StartMs = project.StartMs;
        range = project.RangeStart is { } a && project.RangeEnd is { } b ? (a, b) : null;
        LoopEnabled = project.LoopEnabled; PreRollBeats = project.PreRollBeats; ComparisonSpeed = project.ComparisonSpeed;
        AnchorA = project.AnchorA; AnchorB = project.AnchorB; AnchorBeats = project.AnchorBeats;
        WaveformLayer = project.WaveformLayer; ShowOnsets = project.ShowOnsets; ShowSv = project.ShowSv; ShowKiai = project.ShowKiai;
        MeasureView = project.MeasureView;
        RangeChanged(); TimingChanged(); Seek(project.PositionMs);
        Marks = project.SectionMarks; reviewedSections.UnionWith(project.ReviewedTimes); RefreshProposalPreview(); OnPropertyChanged(nameof(ReviewQueue));
        OnPropertyChanged(nameof(AnalysisBpmText)); NotifyView();
        Status = "Session restored" + (Audio is null ? "; source audio is missing." : ".");
    }

    public void AddBookmark(string name) => Run(() =>
    {
        var marker = new Bookmark(Guid.NewGuid(), name.Trim(), PositionMs).Validate();
        bookmarks = bookmarks.Append(marker).OrderBy(b => b.TimeMs).ToArray(); NotifyDirty();
    });
    public void RemoveBookmark(Guid id) => Run(() => { bookmarks = bookmarks.Where(b => b.Id != id).ToArray(); NotifyDirty(); });
}
