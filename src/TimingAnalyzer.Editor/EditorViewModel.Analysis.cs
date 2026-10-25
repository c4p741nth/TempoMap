namespace TimingAnalyzer.Editor;

public sealed record AnalysisProgress(string Stage, double? Percent = null);
public sealed record BpmCandidate(double Bpm, double RelativeScore)
{
    public override string ToString() => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{Bpm:0.###} BPM · {RelativeScore:P0} relative strength");
}

public sealed partial class EditorViewModel
{
    private CancellationTokenSource? operationCancellation;
    private string analysisStage = "";
    private double? analysisPercent;
    private IReadOnlyList<BpmCandidate> bpmCandidates = [];
    public CancellationToken AnalysisToken => operationCancellation?.Token ?? lifetime.Token;
    public string AnalysisStage { get => analysisStage; private set => SetProperty(ref analysisStage, value); }
    public double AnalysisPercent => analysisPercent ?? 0;
    public bool AnalysisIndeterminate => analysisPercent is null;
    public IReadOnlyList<BpmCandidate> BpmCandidates { get => bpmCandidates; private set => SetProperty(ref bpmCandidates, value); }
    public void SetBpmCandidates(IReadOnlyList<BpmCandidate> values) => BpmCandidates = values;
    public void ChooseCandidate(BpmCandidate candidate) { if (IsBusy || disposed) return; AnalysisBpmText = OsuPrecision.FormatBpm(candidate.Bpm); Status = "BPM candidate selected; run Auto-time to fit its offset, then review and Apply."; }
    public void ReportProgress(AnalysisProgress value)
    {
        if (!IsBusy || AnalysisToken.IsCancellationRequested) return;
        AnalysisStage = value.Stage; analysisPercent = value.Percent;
        OnPropertyChanged(nameof(AnalysisPercent)); OnPropertyChanged(nameof(AnalysisIndeterminate));
    }
    public void CancelAnalysis()
    {
        if (!IsBusy) return;
        operationCancellation?.Cancel(); AnalysisStage = "Cancelling…";
    }
    private void BeginOperation()
    {
        operationCancellation?.Dispose(); operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        rangeAuditionActive = false; playback?.Pause(); playing = false;
        if (playback is not null) ReadPlayback(wallMilliseconds(), snap: true);
        analysisPercent = null; AnalysisStage = "Preparing…";
        OnPropertyChanged(nameof(AnalysisIndeterminate));
    }
    private void EndOperation()
    {
        if (operationCancellation?.IsCancellationRequested == true) Status = "Analysis cancelled. Timing points were kept.";
        AnalysisStage = "";
    }
    private void Transport(Action action)
    {
        if (disposed) return;
        try { action(); } catch (Exception ex) { Status = ex.Message; }
    }
}
