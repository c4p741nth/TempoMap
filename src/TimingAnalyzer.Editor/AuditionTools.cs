namespace TimingAnalyzer.Editor;

public sealed class TapTempo
{
    private readonly Queue<double> taps = new();
    public double? Tap(double wallMs)
    {
        if (!double.IsFinite(wallMs)) throw new ArgumentOutOfRangeException(nameof(wallMs));
        if (taps.Count > 0 && (wallMs - taps.Last() > 2500 || wallMs <= taps.Last())) taps.Clear();
        if (taps.Count > 0 && wallMs - taps.Last() < 100) return null;
        taps.Enqueue(wallMs); while (taps.Count > 9) taps.Dequeue();
        if (taps.Count < 3) return null;
        var times = taps.ToArray(); var gaps = times.Skip(1).Select((t, i) => t - times[i]).Order().ToArray();
        return 60000 / (gaps.Length % 2 == 0 ? (gaps[gaps.Length / 2 - 1] + gaps[gaps.Length / 2]) / 2 : gaps[gaps.Length / 2]);
    }
    public void Reset() => taps.Clear();
}

public sealed partial class EditorViewModel
{
    private bool loopEnabled, momentaryMute, looping, rangeAuditionActive;
    private int preRollBeats;
    private double? anchorA, anchorB;
    private string anchorBeats = "16", comparisonSpeed = "0.75";
    private readonly TapTempo tapTempo = new();
    public bool LoopEnabled { get => loopEnabled; set { SetProperty(ref loopEnabled, value); if (!value) rangeAuditionActive = false; } }
    public int PreRollBeats { get => preRollBeats; set => SetProperty(ref preRollBeats, Math.Clamp(value, 0, 16)); }
    public string ComparisonSpeed { get => comparisonSpeed; set { if (Speeds.Contains(value)) SetProperty(ref comparisonSpeed, value); } }
    public double? AnchorA { get => anchorA; private set { SetProperty(ref anchorA, value); OnPropertyChanged(nameof(AnchorSummary)); } }
    public double? AnchorB { get => anchorB; private set { SetProperty(ref anchorB, value); OnPropertyChanged(nameof(AnchorSummary)); } }
    public string AnchorBeats { get => anchorBeats; set { SetProperty(ref anchorBeats, value); OnPropertyChanged(nameof(AnchorSummary)); } }
    public string AnchorSummary => AnchorA is { } a && AnchorB is { } b && b > a && double.TryParse(AnchorBeats, System.Globalization.NumberStyles.Float, Invariant, out double beats) && beats > 0
        ? string.Create(Invariant, $"A {a:0.###} → B {b:0.###} ms · {beats:0.###} beats = {60000 * beats / (b - a):0.###} BPM") : "Mark A and B on two known beats, then enter their beat count.";
    public void MarkAnchorA() { AnchorA = PositionMs; }
    public void MarkAnchorB() { AnchorB = PositionMs; }
    public void ApplyAnchors() => Run(() =>
    {
        if (AnchorA is not { } a || AnchorB is not { } b || b <= a) throw new ArgumentException("B must be after A.");
        double beats = double.Parse(AnchorBeats, Invariant);
        if (!double.IsFinite(beats) || beats <= 0 || beats > 100000) throw new ArgumentException("Enter a positive beat count.");
        var point = new TimingPoint(OsuPrecision.Time(a), OsuPrecision.BeatLength((b - a) / beats));
        var (lines, _) = RangeEdit.Replace(Edits.RedLines, a, b, InheritSamples([point]));
        Edits.Replace(lines); Select(Edits.RedLines.ToList().FindIndex(p => p.Time == point.Time));
        Status = AnchorSummary + ". Applied to A–B; timing after B is preserved. Ctrl+Z undoes.";
    });
    public void PlayRange() => Transport(() =>
    {
        if (range is not { } r) throw new InvalidOperationException("Highlight a range first (Shift+drag).");
        double beat = Edits.RedLines.LastOrDefault(p => p.Time <= r.Start)?.BeatLength ?? 500;
        if (beat < 50) beat = 500;
        rangeAuditionActive = false;
        Seek(Math.Max(0, r.Start - PreRollBeats * beat)); rangeAuditionActive = LoopEnabled; playback?.Play();
        if (playback is not null) ReadPlayback(wallMilliseconds(), snap: true);
    });
    private void LoopRangeIfNeeded()
    {
        if (looping || !rangeAuditionActive || !LoopEnabled || range is not { } r || playback is null || PositionMs < r.End || r.End <= r.Start) return;
        looping = true;
        try { PlayRange(); } finally { looping = false; }
    }
    public void SwapComparisonSpeed() => Transport(() => { string previous = SelectedSpeed; SelectedSpeed = ComparisonSpeed; ComparisonSpeed = previous; });
    public void MuteMetronomeWhileHeld(bool held)
    {
        momentaryMute = held;
        if (playback is not null) playback.ClickEnabled = ClickEnabled && !held;
    }
    public void TapBpm()
    {
        if (tapTempo.Tap(wallMilliseconds()) is { } bpm)
        { AnalysisBpmText = OsuPrecision.FormatBpm(bpm); Status = "Tap estimate: " + AnalysisBpmText + " BPM. Auto-time fits its offset; clear BPM for automatic detection."; }
        else Status = "Tap at least three beats at a steady pace…";
    }
    public void ResetTaps() { tapTempo.Reset(); }
}
