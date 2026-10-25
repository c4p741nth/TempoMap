namespace TimingAnalyzer.Editor;

public sealed partial class EditorViewModel
{
    private string waveformLayer = "Full";
    private bool showOnsets, showSv = true, showKiai = true, measureView, ghostProposal = true, auditionProposal;
    private readonly HashSet<double> reviewedSections = [];
    public IReadOnlyList<string> WaveformLayers { get; } = ["Full", "Low (<200 Hz)", "Mid (200–4000 Hz)", "High (>4000 Hz)", "Spectrogram"];
    public string WaveformLayer { get => waveformLayer; set { if (WaveformLayers.Contains(value)) SetProperty(ref waveformLayer, value); } }
    public bool ShowOnsets { get => showOnsets; set => SetProperty(ref showOnsets, value); }
    public bool ShowSv { get => showSv; set => SetProperty(ref showSv, value); }
    public bool ShowKiai { get => showKiai; set => SetProperty(ref showKiai, value); }
    public bool MeasureView { get => measureView; set => SetProperty(ref measureView, value); }
    public bool GhostProposal { get => ghostProposal; set => SetProperty(ref ghostProposal, value); }
    public bool AuditionProposal
    {
        get => auditionProposal;
        set
        {
            if (!SetProperty(ref auditionProposal, value)) return;
            playback?.SetTiming(value && ProposedTiming.Count > 0 ? ProposedTiming : Edits.RedLines);
            Status = value ? "Audition B: proposed metronome grid. Timing is unchanged until Apply." : "Audition A: current metronome grid.";
        }
    }
    public IReadOnlyList<TimingPoint> ProposedTiming
    {
        get
        {
            if (MultiProposal is { } multi) return multi.Sections;
            try { return ProposalPoint() is { } point ? [point] : []; } catch (FormatException) { return []; } catch (ArgumentException) { return []; }
        }
    }
    private void RefreshProposalPreview()
    {
        OnPropertyChanged(nameof(ProposedTiming)); OnPropertyChanged(nameof(HasProposal));
        if (AuditionProposal) playback?.SetTiming(ProposedTiming.Count > 0 ? ProposedTiming : Edits.RedLines);
    }
    public void AuditionCurrent() { AuditionProposal = false; }
    public void AuditionPending() { if (HasProposal) AuditionProposal = true; }
    public void JumpProposal(int index)
    {
        if (index >= 0 && index < ProposedTiming.Count) JumpTo(ProposedTiming[index].Time);
    }
    public IReadOnlyList<ReviewSection> ReviewQueue => Marks.Where(m => m.Index < Edits.RedLines.Count)
        .Select(m => new ReviewSection(m.Index, Edits.RedLines[m.Index].Time, m.Describe(Edits.RedLines[m.Index]), reviewedSections.Contains(Edits.RedLines[m.Index].Time))).ToArray();
    public void ReviewNext(int direction = 1)
    {
        var queue = ReviewQueue.Where(x => !x.Reviewed).OrderBy(x => x.TimeMs).ToArray();
        if (queue.Length == 0) { Status = "No unreviewed section issues. Run Check sections to refresh."; return; }
        var issue = direction >= 0 ? queue.FirstOrDefault(x => x.TimeMs > PositionMs + .5) ?? queue[0]
            : queue.LastOrDefault(x => x.TimeMs < PositionMs - .5) ?? queue[^1];
        Select(issue.Index); JumpTo(issue.TimeMs); Status = issue.Reason;
    }
    public void MarkReviewed()
    {
        if (SelectedRow is { } row) reviewedSections.Add(row.Point.Time);
        OnPropertyChanged(nameof(ReviewQueue));
    }
}
public sealed record ReviewSection(int Index, double TimeMs, string Reason, bool Reviewed)
{
    public override string ToString() => (Reviewed ? "✓ " : "• ") + TimeFormat.Osu(TimeMs) + " · " + Reason;
}
