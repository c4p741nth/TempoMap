using System.Globalization;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace TimingAnalyzer.Editor;

public sealed record AudioContent(double[] Mono, int Rate, WaveformPeaks Peaks)
{
    public double DurationMs => Mono.Length * 1000.0 / Rate;
    public static AudioContent Create(double[] mono, int rate) => new(mono, rate, new WaveformPeaks(mono));
}
public sealed record RedLineRow(int Index, TimingPoint Point, bool InSelection = false)
{
    public string Label => (InSelection ? "● " : "") + OsuPrecision.FormatTime(Point.Time) + " ms   " +
        OsuPrecision.FormatBpm(Point.Bpm) + " BPM   " + Point.Meter.ToString(CultureInfo.InvariantCulture) + "/4" +
        (Point.OmitsFirstBarline ? "   no bar line" : "");
    // The selection marker is display only: a marked and an unmarked row are the same list item.
    public bool Equals(RedLineRow? other) => other is not null && Index == other.Index && Point == other.Point;
    public override int GetHashCode() => HashCode.Combine(Index, Point);
}

public sealed class EditorViewModel : ObservableObject, IDisposable
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
    private readonly Func<string, Task<AudioContent>> loadAudio;
    private readonly Func<string, IPlaybackEngine> createPlayback;
    private readonly Func<string, CancellationToken, Task<string>> prepareAudio;
    private readonly CancellationTokenSource lifetime = new();
    private IPlaybackEngine? playback;
    private readonly PlayheadClock playheadClock = new();
    private readonly Func<double> wallMilliseconds;
    private double nextEngineReadMs;
    private bool playing, followPlayhead = true, followSuspended;
    private double suspendedTarget; // Follow target at the last suspended frame (see ApplyFollow).
    private string status = "Open audio or an osu! beatmap to begin.", timeText = "0", bpmText = "120", meterText = "4";
    private bool busy, clickEnabled = true, disposed;
    private string selectedSpeed = "1.0";
    private RedLineRow? selectedRow;
    // Sorted red-line indices. SelectedRow is the primary one, which the fields edit.
    private int[] selection = [];
    private TimingPoint[] selectionPoints = [];
    private int[] greenSelection = [];
    private TimingRow? selectedGreenRow;
    /// <summary>Keep playback from changing the point while its fields have keyboard focus.</summary>
    public bool IsEditingPoint { get; set; }
    private bool updatingSelection;
    private SnapOption snap = new(4), shiftBeatStep = new(4);
    private int shiftMsStep = 1;
    private double bpmStep = 0.01;
    private bool alignToEdge = true, startOnBar = true;
    private double position, start, span = 10000;
    private double musicVolumePercent = 100, clickVolumePercent = 70;
    // The window has no scope box: lines are picked in the timing table (Ctrl+A for all), so shifts act on them.
    private RedLineScope shiftScope = RedLineScope.Selected;
    private TimingDrag? drag;
    private TimingPoint[]? preview;
    private double dragTime;
    private AutoTimingProposal? proposal;
    private MultiTimingProposal? multiProposal;
    private string proposalOffsetText = "";
    private string proposalBpmText = "";
    public AutoTimingProposal? Proposal { get => proposal; private set { SetProperty(ref proposal, value); proposalBpmText = value is null ? "" : OsuPrecision.FormatBpm(value.Bpm); OnPropertyChanged(nameof(AnalysisBpmText)); OnPropertyChanged(nameof(ProposalDescription)); OnPropertyChanged(nameof(HasProposal)); OnPropertyChanged(nameof(HasSingleProposal)); } }
    /// <summary>A one-line proposal waits: its offset can be edited before Apply.</summary>
    public bool HasSingleProposal => Proposal is not null;
    /// <summary>The multi-BPM proposal, when one was made. Mutually exclusive with <see cref="Proposal"/>.</summary>
    public MultiTimingProposal? MultiProposal { get => multiProposal; private set { SetProperty(ref multiProposal, value); OnPropertyChanged(nameof(ProposalDescription)); OnPropertyChanged(nameof(ProposalSections)); OnPropertyChanged(nameof(HasProposal)); } }
    /// <summary>A proposal waits to be applied: the window shows its description, offset box and apply buttons.</summary>
    public bool HasProposal => Proposal is not null || MultiProposal is not null;
    /// <summary>One readable row per proposed red line; empty unless a multi-BPM proposal is pending.</summary>
    public IReadOnlyList<string> ProposalSections => MultiProposal?.Lines ?? [];
    public string ProposalDescription => Proposal?.Description ?? MultiProposal?.Description ?? "Auto-time proposes one red line for the whole song; review it before replacing red lines. " +
        "For several BPMs, place a red line at each section start and use Auto-time section or Fit offset.";
    public string ProposalOffsetText { get => proposalOffsetText; set => SetProperty(ref proposalOffsetText, value); }
    private string lockedBpmText = "";
    /// <summary>Empty: detect the BPM. A number: lock it, so auto-time fits only the offset.</summary>
    public string LockedBpmText { get => lockedBpmText; set { if (SetProperty(ref lockedBpmText, value ?? "")) OnPropertyChanged(nameof(AnalysisBpmText)); } }
    /// <summary>Typing locks the next analysis as well as editing a pending result; detector-filled values stay automatic.</summary>
    public string AnalysisBpmText
    {
        get => HasSingleProposal ? proposalBpmText : LockedBpmText;
        set
        {
            if (HasSingleProposal) SetProperty(ref proposalBpmText, value ?? "");
            LockedBpmText = value ?? "";
        }
    }
    private string tempoFactorText = "0.5";
    public string TempoFactorText { get => tempoFactorText; set => SetProperty(ref tempoFactorText, value); }
    public bool AutoNormalizeSv
    {
        get => Edits.AutoNormalizeSv;
        set => Run(() =>
        {
            if (value == Edits.AutoNormalizeSv) return;
            double? greenTime = selectedGreenRow?.Point.Time;
            var visible = Greens.ToArray();
            Edits.ReplaceTiming(Edits.RedLines, value ? LiveNormalization.FromVisible(visible, Edits.RedLines, NormalizationEnd) : visible, value);
            if (greenTime is { } time) SelectGreenAt(time);
            Status = value ? "Auto normalize SV on: green values follow the red grid. Undo restores the previous mode." :
                "Manual SV: current green values stay fixed when BPM or timing changes. Undo restores the previous mode.";
        });
    }
    /// <summary>Drift mode choices: beats per red line (4 = one bar in 4/4; 1 = a red line on every drum hit).</summary>
    /// <summary>
    /// Auto-time modes beside the one Auto-time button: Normal (multi-BPM, which gives the original single red line when
    /// the song keeps one tempo, and the beat model's red lines where the per-song rule prefers them), Beat model (the
    /// model's red lines for the whole song, whatever the rule says: for hard songs such as Camellia - Parallel Universe
    /// Shifter, where the model scored 0.585 against the analyzer's 0.296 but Normal kept the analyzer), or drift with N
    /// beats per red line (1 = a red line on every drum hit).
    /// </summary>
    public IReadOnlyList<string> AutoModes { get; } = [NormalMode, BeatModelMode, "Drift 1 beat", "Drift 4 beats", "Drift 8 beats", "Drift 16 beats", "Drift 32 beats"];
    public const string NormalMode = "Normal";
    public const string BeatModelMode = "Beat model";
    public bool BeatModelSelected => autoMode == BeatModelMode;
    private string autoMode = NormalMode;
    public string SelectedAutoMode { get => autoMode; set => SetProperty(ref autoMode, AutoModes.Contains(value) ? value : autoMode); }
    /// <summary>Beats per drift red line; 0 in Normal and Beat model modes.</summary>
    public int DriftBeatsPerLine => autoMode.StartsWith("Drift ", StringComparison.Ordinal) ? int.Parse(autoMode.Split(' ')[1], Invariant) : 0;
    public IReadOnlyList<TimingPoint> DisplayRedLines => preview ?? Edits.RedLines;
    public bool IsDragging => drag is not null;
    public TimingEdits Edits { get; private set; } = new([]);
    public OsuFile? Document { get; private set; }
    public string? BeatmapPath { get; private set; }
    public string? AudioPath { get; private set; }
    public AudioContent? Audio { get; private set; }
    public bool IsBusy { get => busy; private set { SetProperty(ref busy, value); OnPropertyChanged(nameof(CanInteract)); } }
    public bool CanInteract => !IsBusy;
    public bool HasAudio => Audio is not null;
    public IReadOnlyList<string> ThemeOptions { get; } = ["Dark", "Light", "System"];
    private string selectedTheme = "Dark";
    public string SelectedTheme
    {
        get => selectedTheme;
        set { if (ThemeOptions.Contains(value)) SetProperty(ref selectedTheme, value); }
    }
    public string Status { get => status; private set => SetProperty(ref status, value); }
    public double DurationMs => Audio?.DurationMs ?? Math.Max(10000, (Edits.RedLines.LastOrDefault()?.Time ?? 0) + 5000);
    public double PositionMs
    {
        get => position;
        private set { SetProperty(ref position, value); OnPropertyChanged(nameof(PositionLabel)); }
    }
    /// <summary>osu! editor style: <c>00:01:531</c> is the same instant as offset 1531.</summary>
    public string PositionLabel => TimeFormat.Osu(PositionMs) + " / " + TimeFormat.Osu(DurationMs);
    public double StartMs
    {
        get => start;
        private set
        {
            if (SetProperty(ref start, Math.Clamp(value, 0, ScrollMaximum))) OnPropertyChanged(nameof(ManualStartMs));
        }
    }
    // Only user navigation writes this binding. A binding echo of an automatic update is a no-op.
    public double ManualStartMs
    {
        get => StartMs;
        set
        {
            double clamped = Math.Clamp(value, 0, ScrollMaximum);
            if (clamped == StartMs) return;
            SuspendFollow();
            StartMs = clamped;
        }
    }
    public bool FollowPlayhead
    {
        get => followPlayhead;
        set
        {
            if (!SetProperty(ref followPlayhead, value)) return;
            followSuspended = false;
            ApplyFollow();
        }
    }
    public double SpanMs => span;
    public double ScrollMaximum => Math.Max(0, DurationMs - SpanMs);
    public IReadOnlyList<RedLineRow> Rows => Edits.RedLines.Select((p, i) => new RedLineRow(i, p, selection.Contains(i))).ToArray();
    /// <summary>The timing table's "Timing" tab: every red line.</summary>
    public IReadOnlyList<TimingRow> TimingRows => TablePoints(Edits.RedLines, red: true);
    /// <summary>The "Inherited" tab includes automatic scroll normalization and the map's own effects.</summary>
    public IReadOnlyList<TimingRow> InheritedRows => TablePoints(Greens, red: false);
    private static TimingRow[] TablePoints(IEnumerable<TimingPoint> points, bool red)
    {
        var rows = points.ToArray();
        var overlaps = rows.GroupBy(p => p.SerializedTime).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet();
        return rows.Select((p, i) => new TimingRow(red ? i : -1, p, red ? -1 : i, overlaps.Contains(p.SerializedTime))).ToArray();
    }
    /// <summary>The "All" tab: red and green lines in time order, a green line after the red line at its time.</summary>
    public IReadOnlyList<TimingRow> AllRows => TimingRows.Concat(InheritedRows)
        .OrderBy(r => r.Point.SerializedTime).ThenBy(r => r.IsGreen).ToArray();
    /// <summary>The timing table's tabs, as osu!'s Timing and Control Points window.</summary>
    public IReadOnlyList<string> TableTabs { get; } = ["All", "Timing", "Inherited"];
    private int tableTab;
    public int TableTab { get => tableTab; set { if (SetProperty(ref tableTab, Math.Clamp(value, 0, 2))) { OnPropertyChanged(nameof(TableRows)); OnPropertyChanged(nameof(AddPointTip)); } } }
    public string AddPointTip => TableTab == 2 ? "Add an inherited point at the yellow playhead (Ctrl+G)." :
        "Add a timing point at the yellow playhead (Ctrl+P). Ctrl+G adds an inherited point from any tab.";
    public IReadOnlyList<TimingRow> TableRows => tableTab switch { 1 => TimingRows, 2 => InheritedRows, _ => AllRows };
    /// <summary>Selects red lines picked in the timing table (Ctrl / Shift click) without moving the playhead.</summary>
    public void SelectRows(IEnumerable<int> redIndices, int primary) => SetSelection(redIndices, primary);
    public IReadOnlyList<int> SelectedGreenIndices => greenSelection;
    public bool HasPointSelection => SelectedRow is not null || selectedGreenRow is not null;
    public bool IsRedPointSelected => selectedGreenRow is null && SelectedRow is not null;
    public string SelectedPointLabel => selectedGreenRow is not null ? "Selected inherited point" : "Selected timing point";
    public string PointValueLabel => selectedGreenRow is not null ? "SV (x)" : "BPM";
    private TimingPoint? FieldPoint => selectedGreenRow?.Point ?? SelectedRow?.Point;
    public bool KiaiEnabled
    {
        get => FieldPoint is { } point && (point.Effects & TimingPoint.Kiai) != 0;
        set => Run(() =>
        {
            if (FieldPoint is not { } point || value == ((point.Effects & TimingPoint.Kiai) != 0)) return;
            var updated = point with { Effects = value ? point.Effects | TimingPoint.Kiai : point.Effects & ~TimingPoint.Kiai };
            if (selectedGreenRow is { } green)
            {
                var visible = InheritedRows.Select(r => r.Point).ToArray(); visible[green.GreenIndex] = updated;
                ReplaceVisibleGreens(visible); SelectGreenAt(updated.Time);
            }
            else { Edits.Update(SelectedRow!.Index, updated); Select(Edits.RedLines.ToList().FindIndex(p => ReferenceEquals(p, updated))); }
            Status = value ? "Kiai on from this point until the next effect change." : "Kiai off from this point.";
        });
    }
    public void SelectTableRows(IEnumerable<TimingRow> rows, TimingRow? primary)
    {
        var chosen = rows.ToArray();
        SetSelection(chosen.Where(r => r.IsRed).Select(r => r.RedIndex), primary?.RedIndex ?? -1,
            chosen.Where(r => r.IsGreen).Select(r => r.GreenIndex), primary is { IsGreen: true } ? primary.GreenIndex : -1);
    }
    /// <summary>The picked table rows, red and green, as osu! timing point rows for the clipboard.</summary>
    public static string RowsClipboardText(IEnumerable<TimingRow> rows) => TimingClipboard.Write(rows.Select(r => r.Point));
    public RedLineRow? SelectedRow
    {
        get => selectedRow;
        set
        {
            // A list click selects one row. The list echoing the current row, or clearing itself while
            // the rows are rebuilt, must not collapse a multi-selection.
            if (updatingSelection || Equals(value, selectedRow)) return;
            SetSelection(value is null ? [] : [value.Index], value?.Index ?? -1);
            // Picking a row in the list jumps there, as in the osu! timing panel.
            if (value is not null) JumpTo(value.Point.Time);
        }
    }
    /// <summary>Moves the playhead to a time and brings it into view, including when playback follow is off.</summary>
    public void JumpTo(double timeMs) => Run(() =>
    {
        Seek(timeMs);
        StartMs = FollowViewport.Start(PositionMs, SpanMs, DurationMs);
    });
    /// <summary>The loaded map's "Artist - Title [Version] (Creator)", or the audio file name.</summary>
    public string SongTitle => Document?.SongTitle ?? (AudioPath is not null ? Path.GetFileName(AudioPath) :
        BeatmapPath is not null ? Path.GetFileName(BeatmapPath) : "No song loaded");
    public string WindowTitle => Document is null && AudioPath is null ? AppInfo.Title : SongTitle + " - " + AppInfo.ShortTitle;
    public IReadOnlyList<int> SelectedIndices => selection;
    public string SelectionLabel => greenSelection.Length > 0 && selection.Length == 0 ? Count(greenSelection.Length, "green line") + " selected"
        : greenSelection.Length > 0
        ? Count(selection.Length, "red line") + ", " + Count(greenSelection.Length, "green line")
        : selection.Length switch
    {
        0 => "No red line selected", 1 => "1 red line selected",
        var n => n.ToString(Invariant) + " red lines selected"
    };
    public IReadOnlyList<SnapOption> SnapOptions { get; } = BeatSnap.Divisors.Select(d => new SnapOption(d)).ToArray();
    public SnapOption SelectedSnap
    {
        get => snap;
        set { if (value is not null && BeatSnap.Divisors.Contains(value.Divisor) && SetProperty(ref snap, value)) OnPropertyChanged(nameof(SnapDivisor)); }
    }
    public int SnapDivisor => snap.Divisor;
    /// <summary>The Shift toolbar's step sizes: ❮ and ❯ move the selected red lines one step earlier or later.</summary>
    public IReadOnlyList<SnapOption> ShiftBeatSteps { get; } = TimingEdits.ShiftDivisors.Select(d => new SnapOption(d)).ToArray();
    public SnapOption SelectedShiftBeatStep
    {
        get => shiftBeatStep;
        set { if (value is not null && TimingEdits.ShiftDivisors.Contains(value.Divisor)) SetProperty(ref shiftBeatStep, value); }
    }
    public IReadOnlyList<int> ShiftMsSteps { get; } = [1, 2, 5, 10, 20, 50];
    public int SelectedShiftMsStep { get => shiftMsStep; set { if (ShiftMsSteps.Contains(value)) SetProperty(ref shiftMsStep, value); } }
    /// <summary>The BPM nudge steps: osu! shows three decimals, so 0.001 is the finest.</summary>
    public IReadOnlyList<double> BpmSteps { get; } = [0.001, 0.01, 0.1, 1];
    public double SelectedBpmStep { get => bpmStep; set { if (BpmSteps.Contains(value)) SetProperty(ref bpmStep, value); } }
    /// <summary>Auto-time puts its lines on the waveform's leading edge, not the onset peak's convention (Core EdgeAlign).</summary>
    public bool AlignToEdge { get => alignToEdge; set => SetProperty(ref alignToEdge, value); }
    /// <summary>Auto-time starts its first red line on a bar's first beat, by the beat model's bar starts (Core BarStart).</summary>
    public bool StartOnBar { get => startOnBar; set => SetProperty(ref startOnBar, value); }
    public int OsuCopyShiftMs => OsuTimingExport.ShiftMs;
    public string TimeText { get => timeText; set => SetProperty(ref timeText, value); }
    public string BpmText { get => bpmText; set => SetProperty(ref bpmText, value); }
    public string MeterText { get => meterText; set => SetProperty(ref meterText, value); }
    /// <summary>The settings to remember (the window adds its size).</summary>
    public EditorSettings CaptureSettings(EditorSettings? window = null) => (window ?? new EditorSettings()) with
    {
        MusicVolumePercent = MusicVolumePercent, ClickVolumePercent = ClickVolumePercent, ClickEnabled = ClickEnabled,
        FollowPlayhead = FollowPlayhead, Speed = SelectedSpeed, SnapDivisor = SnapDivisor, AutoMode = SelectedAutoMode,
        TableTab = TableTab, ShiftBeatDivisor = SelectedShiftBeatStep.Divisor, ShiftMs = SelectedShiftMsStep,
        BpmStep = SelectedBpmStep, AlignToEdge = AlignToEdge, StartOnBar = StartOnBar, Theme = SelectedTheme, AutoNormalizeSv = AutoNormalizeSv,
    };
    /// <summary>Applies remembered settings through the same setters as the window, so out-of-range values are ignored.</summary>
    public void ApplySettings(EditorSettings s)
    {
        SelectedTheme = s.Theme;
        AutoNormalizeSv = s.AutoNormalizeSv;
        MusicVolumePercent = s.MusicVolumePercent; ClickVolumePercent = s.ClickVolumePercent;
        ClickEnabled = s.ClickEnabled; FollowPlayhead = s.FollowPlayhead;
        if (Speeds.Contains(s.Speed)) SelectedSpeed = s.Speed;
        if (SnapOptions.FirstOrDefault(o => o.Divisor == s.SnapDivisor) is { } snapOption) SelectedSnap = snapOption;
        SelectedAutoMode = s.AutoMode; TableTab = s.TableTab;
        if (ShiftBeatSteps.FirstOrDefault(o => o.Divisor == s.ShiftBeatDivisor) is { } beatStep) SelectedShiftBeatStep = beatStep;
        SelectedShiftMsStep = s.ShiftMs;
        SelectedBpmStep = s.BpmStep;
        AlignToEdge = s.AlignToEdge; StartOnBar = s.StartOnBar;
    }
    public IReadOnlyList<string> Speeds { get; } = ["0.25", "0.5", "0.75", "1.0"];
    public string SelectedSpeed
    {
        get => selectedSpeed;
        set => Run(() =>
        {
            double speed = PlaybackMath.ValidateSpeed(double.Parse(value, Invariant));
            if (playback is not null) playback.Speed = speed;
            SetProperty(ref selectedSpeed, value);
            RefreshPosition();
        });
    }
    public bool ClickEnabled
    {
        get => clickEnabled;
        set => Run(() => { if (playback is not null) playback.ClickEnabled = value; SetProperty(ref clickEnabled, value); });
    }
    public double MusicVolumePercent
    {
        get => musicVolumePercent;
        set => Run(() =>
        {
            double volume = PlaybackMath.ClampVolume(value / 100);
            if (playback is not null) playback.MusicVolume = volume;
            SetProperty(ref musicVolumePercent, volume * 100);
            OnPropertyChanged(nameof(MusicVolumeLabel));
        });
    }
    public double ClickVolumePercent
    {
        get => clickVolumePercent;
        set => Run(() =>
        {
            double volume = PlaybackMath.ClampVolume(value / 100);
            if (playback is not null) playback.ClickVolume = volume;
            SetProperty(ref clickVolumePercent, volume * 100);
            OnPropertyChanged(nameof(ClickVolumeLabel));
        });
    }
    public string MusicVolumeLabel => MusicVolumePercent.ToString("0", Invariant) + " %";
    public string ClickVolumeLabel => ClickVolumePercent.ToString("0", Invariant) + " %";
    public IReadOnlyList<RedLineScope> ShiftScopes { get; } = [RedLineScope.All, RedLineScope.Selected];
    public RedLineScope ShiftScope { get => shiftScope; set => SetProperty(ref shiftScope, value); }
    public IRelayCommand<string> ShiftBeatsCommand { get; }
    public IRelayCommand<string> ShiftMsCommand { get; }
    /// <summary>"-1" or "1": one step of the chosen size earlier or later.</summary>
    public IRelayCommand<string> ShiftBeatStepCommand { get; }
    public IRelayCommand<string> ShiftMsStepCommand { get; }
    /// <summary>"-1" or "1": the BPM of the selected red lines one step lower or higher (<see cref="NudgeBpm"/>).</summary>
    public IRelayCommand<string> NudgeBpmCommand { get; }
    public IRelayCommand MoveToPlayheadCommand { get; }
    public IRelayCommand TogglePlayCommand { get; }
    public IRelayCommand ApplyFieldsCommand { get; }
    public IRelayCommand InsertCommand { get; }
    public IRelayCommand InsertInheritedCommand { get; }
    public IRelayCommand JumpToStartCommand { get; }
    public IRelayCommand JumpToEndCommand { get; }
    public IRelayCommand DeleteCommand { get; }
    public IRelayCommand UndoCommand { get; }
    public IRelayCommand RedoCommand { get; }
    public IRelayCommand BpmFromSpacingCommand { get; }
    public IRelayCommand<string> ScaleTempoCommand { get; }
    public IRelayCommand FillRampCommand { get; }
    public IRelayCommand StartBarHereCommand { get; }
    public IRelayCommand ClearRangeCommand { get; }
    public EditorViewModel(Func<string, Task<AudioContent>> loadAudio, Func<string, IPlaybackEngine> createPlayback,
        Func<double>? wallMilliseconds = null, Func<string, CancellationToken, Task<string>>? prepareAudio = null)
    {
        this.loadAudio = loadAudio; this.createPlayback = createPlayback;
        this.prepareAudio = prepareAudio ?? ((path, _) => Task.FromResult(path));
        this.wallMilliseconds = wallMilliseconds ?? (() => Stopwatch.GetTimestamp() * 1000.0 / Stopwatch.Frequency);
        TogglePlayCommand = new RelayCommand(TogglePlay);
        ApplyFieldsCommand = new RelayCommand(ApplyFields);
        InsertCommand = new RelayCommand(Insert);
        InsertInheritedCommand = new RelayCommand(InsertInherited);
        JumpToStartCommand = new RelayCommand(() => JumpTo(0));
        JumpToEndCommand = new RelayCommand(() => JumpTo(DurationMs));
        DeleteCommand = new RelayCommand(Delete);
        UndoCommand = new RelayCommand(Undo);
        RedoCommand = new RelayCommand(Redo);
        BpmFromSpacingCommand = new RelayCommand(BpmFromSpacing);
        ScaleTempoCommand = new RelayCommand<string>(value => ScaleTempo(double.Parse(value!, Invariant)));
        FillRampCommand = new RelayCommand(FillRamp);
        StartBarHereCommand = new RelayCommand(StartBarHere);
        ClearRangeCommand = new RelayCommand(ClearRange);
        ShiftBeatsCommand = new RelayCommand<string>(value => Run(() => Shift(false, double.Parse(value!, Invariant))));
        ShiftMsCommand = new RelayCommand<string>(value => Run(() => Shift(true, double.Parse(value!, Invariant))));
        ShiftBeatStepCommand = new RelayCommand<string>(direction => Run(() => Shift(false, int.Parse(direction!, Invariant) / (double)shiftBeatStep.Divisor)));
        ShiftMsStepCommand = new RelayCommand<string>(direction => Run(() => Shift(true, int.Parse(direction!, Invariant) * shiftMsStep)));
        NudgeBpmCommand = new RelayCommand<string>(direction => NudgeBpm(int.Parse(direction!, Invariant)));
        MoveToPlayheadCommand = new RelayCommand(MoveToPlayhead);
        Edits.Changed += TimingChanged;
    }

    public async Task LoadAsync(string path)
    {
        if (IsBusy || disposed) return;
        IsBusy = true;
        LockedBpmText = ""; ProposalOffsetText = "";
        try
        {
            var doc = Path.GetExtension(path).Equals(".osu", StringComparison.OrdinalIgnoreCase) ? OsuFile.Load(path) : null;
            string? song = doc is null ? path : doc.AudioFilename is null ? null :
                Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, doc.AudioFilename));
            playback?.Dispose(); playback = null;
            CancelDrag();
            Edits.Changed -= TimingChanged;
            var reds = doc?.TimingPoints.Where(p => p.Uninherited).ToArray() ?? [new TimingPoint(0, 500)];
            bool normalizeSv = AutoNormalizeSv;
            var loadedGreens = doc?.TimingPoints.Where(p => !p.Uninherited) ?? [];
            Edits = new(reds, normalizeSv ? LiveNormalization.FromDocument(reds, loadedGreens) : loadedGreens, normalizeSv);
            Edits.Changed += TimingChanged;
            Document = doc; BeatmapPath = doc is null ? null : Path.GetFullPath(path); AudioPath = song; Audio = null;
            Proposal = null; MultiProposal = null; ProposalOffsetText = ""; range = null; rangeAnchor = null; RangeChanged();
            PositionMs = 0; start = 0; span = 10000;
            playing = false; followSuspended = false; nextEngineReadMs = 0;
            playheadClock.Synchronize(0, wallMilliseconds(), 1, false, snap: true);
            TimingChanged(); NotifyView();
            if (song is null || !File.Exists(song)) { Status = "Timing loaded; audio file is missing. Open the map beside its AudioFilename." + DuplicateNote(doc); return; }
            Status = "Preparing Ogg Vorbis quality 6 audio...";
            song = await prepareAudio(song, lifetime.Token);
            if (disposed) return;
            AudioPath = song;
            Status = "Loading waveform from " + Path.GetFileName(song) + "...";
            var content = await loadAudio(song);
            if (disposed) return;
            Audio = content;
            span = Math.Min(10000, Math.Max(1, DurationMs));
            NotifyView();
            try
            {
                playback = createPlayback(song);
                playback.SetTiming(Edits.RedLines);
                playback.Speed = double.Parse(selectedSpeed, Invariant);
                playback.ClickEnabled = clickEnabled;
                playback.MusicVolume = musicVolumePercent / 100;
                playback.ClickVolume = clickVolumePercent / 100;
                Status = "Loaded " + Path.GetFileName(song) + DuplicateNote(doc);
            }
            catch (Exception ex)
            {
                playback?.Dispose(); playback = null;
                Status = "Waveform loaded; playback unavailable: " + ex.Message;
            }
        }
        catch (OperationCanceledException) when (disposed) { }
        catch (Exception ex) { Status = "Open failed: " + ex.Message; }
        finally { IsBusy = false; }
    }
    private static string DuplicateNote(OsuFile? doc) => doc is { DuplicateRedLines: > 0 and var n }
        ? string.Create(Invariant, $" ({n} overlapping red line{(n == 1 ? "" : "s")} retained; the first controls playback)") : "";
    public void TogglePlay() => Run(() =>
    {
        if (playback is null) return;
        if (playback.IsPlaying) playback.Pause();
        else { playback.Play(); followSuspended = false; followedSection = -1; }
        ReadPlayback(wallMilliseconds(), snap: true);
    });
    /// <summary>Click-to-seek and ←/→ also end a scroll suspension: the user is back at the playhead.</summary>
    public void Seek(double ms) => Run(() =>
    {
        followSuspended = false;
        playback?.Seek(ms);
        if (playback is not null) ReadPlayback(wallMilliseconds(), snap: true);
        else PositionMs = Math.Clamp(ms, 0, DurationMs);
    });
    public void RefreshPosition() => Run(() => ReadPlayback(wallMilliseconds()));
    public void UpdateFrame() => Run(() =>
    {
        double now = wallMilliseconds();
        if (heldDirection != 0 && Scrub(now)) return;
        if (playback is null) return;
        // Until the sound starts after a seek or Play the engine is read every frame, so the head leaves on time.
        if (now >= nextEngineReadMs || playheadClock.WaitingForStart) ReadPlayback(now);
        else UpdateDisplay(now);
    });
    private void ReadPlayback(double now, bool snap = false)
    {
        if (playback is null) return;
        // Read position first: the engine finalizes its end-of-song state in this getter.
        double enginePosition = playback.PositionMs;
        playing = playback.IsPlaying;
        playheadClock.Synchronize(enginePosition, now, playback.Speed, playing, snap);
        nextEngineReadMs = now + 50;
        UpdateDisplay(now);
    }
    private void UpdateDisplay(double now)
    {
        PositionMs = Math.Clamp(playheadClock.PositionAt(now), 0, DurationMs);
        ApplyFollow();
        FollowSection();
    }
    private int followedSection = -1;
    /// <summary>
    /// While playing with Follow on, the red line the playhead is in becomes the selection (the list scrolls to it)
    /// each time the playhead passes into another section. Paused, the selection is left alone so a seek cannot
    /// redirect Delete or the fields.
    /// </summary>
    private void FollowSection()
    {
        if (!playing || !FollowPlayhead || IsDragging || IsEditingPoint || TableTab == 2 || selectedGreenRow is not null) return;
        var reds = Edits.RedLines;
        int section = reds.Count == 0 ? -1 : 0;
        for (int i = 1; i < reds.Count && reds[i].Time <= PositionMs; i++)
            if (reds[i].Time != reds[i - 1].Time) section = i;
        if (section == followedSection) return;
        followedSection = section;
        if (section >= 0 && !(selection.Length == 1 && selection[0] == section)) SetSelection([section], section);
    }
    private void ApplyFollow()
    {
        if (!playing || !FollowPlayhead) return;
        double target = FollowViewport.Start(PositionMs, SpanMs, DurationMs);
        if (followSuspended)
        {
            // A view scrolled ahead of the playhead resumes when the playhead reaches its one-third anchor, so the
            // hand-over has no jump. A view scrolled back stays put (to look back) until seek, Play or the checkbox.
            bool caughtUp = suspendedTarget < StartMs && target >= StartMs;
            suspendedTarget = target;
            if (!caughtUp) return;
            followSuspended = false;
        }
        StartMs = target;
    }
    private void SuspendFollow()
    {
        if (playback?.IsPlaying != true) return;
        followSuspended = true;
        suspendedTarget = FollowViewport.Start(PositionMs, SpanMs, DurationMs);
    }
    public void Zoom(double factor, double anchorFraction)
    {
        if (!double.IsFinite(factor) || factor <= 0) return;
        anchorFraction = Math.Clamp(anchorFraction, 0, 1);
        double anchor = StartMs + SpanMs * anchorFraction;
        span = Math.Clamp(SpanMs * factor, Math.Min(10, DurationMs), DurationMs);
        StartMs = anchor - SpanMs * anchorFraction;
        OnPropertyChanged(nameof(SpanMs)); OnPropertyChanged(nameof(ScrollMaximum));
        ApplyFollow(); // Zoom keeps following (user request); only scrolling suspends it.
    }
    public void Scroll(double deltaMs) { SuspendFollow(); StartMs += deltaMs; }
    /// <summary>Moves the playhead to the previous/next 1/<see cref="SnapDivisor"/> tick and keeps it on screen.</summary>
    public void StepSnap(int direction) => Run(() =>
    {
        if (Edits.RedLines.Count == 0 || direction == 0) return;
        Seek(Math.Clamp(BeatSnap.Step(Edits.RedLines, PositionMs, SnapDivisor, direction), 0, DurationMs));
        if (PositionMs < StartMs || PositionMs > StartMs + SpanMs) StartMs = FollowViewport.Start(PositionMs, SpanMs, DurationMs);
    });
    // Held ←/→ (docs/PHASE1-STATUS.md E42). The first press steps by the beat snap; once the key has been down for
    // ScrubSpeed.DelayMs the playhead runs on every frame, faster the longer it is held, and the audio is seeked once,
    // when the key goes up (a seek rebuilds the audio graph: doing it per key repeat made a held key crawl).
    private int heldDirection;
    private bool heldExtend, scrubbing, resumeAfterScrub;
    private double heldSince, lastHeldEvent, lastScrubAt;
    /// <summary>A held key whose key-up never came (focus lost) stops after this long without an OS key repeat.</summary>
    public const double StuckKeyMs = 2000;
    /// <summary>
    /// ←/→ went down; <paramref name="extend"/> is Shift+←/→ (highlight a range). OS key repeats of the same key come
    /// here too and only show that it is still down. Pair with <see cref="ArrowUp"/>.
    /// </summary>
    public void ArrowDown(int direction, bool extend = false)
    {
        if (direction == 0) return;
        double now = wallMilliseconds();
        if (heldDirection == direction && heldExtend == extend) { lastHeldEvent = now; return; }
        ArrowUp();
        heldDirection = direction; heldExtend = extend; heldSince = lastHeldEvent = lastScrubAt = now;
        if (extend) ExtendRange(direction); else StepSnap(direction);
    }
    /// <summary>The held arrow went up (0: whichever is held). Ends a run-on: the audio seeks to the head, and plays on if it was playing.</summary>
    public void ArrowUp(int direction = 0)
    {
        if (heldDirection == 0 || direction != 0 && direction != heldDirection) return;
        heldDirection = 0;
        if (!scrubbing) return;
        scrubbing = false;
        bool resume = resumeAfterScrub;
        resumeAfterScrub = false;
        Run(() =>
        {
            Seek(PositionMs);
            if (!resume || playback is null) return;
            playback.Play();
            followSuspended = false; followedSection = -1;
            ReadPlayback(wallMilliseconds(), snap: true);
        });
    }
    // One frame of a held arrow; true while the head is running on (the engine is not read then: it holds the old place).
    private bool Scrub(double now)
    {
        if (now - lastHeldEvent > StuckKeyMs) { ArrowUp(); return false; }
        double held = now - heldSince;
        if (held < ScrubSpeed.DelayMs) return false;
        if (!scrubbing)
        {
            scrubbing = true; lastScrubAt = now;
            resumeAfterScrub = playback?.IsPlaying == true;
            // The sound would play on from the old place while the head races ahead of it.
            if (resumeAfterScrub) playback!.Pause();
        }
        double dt = Math.Min(Math.Max(0, now - lastScrubAt), 50); // a slow frame must not make the head leap
        lastScrubAt = now;
        PositionMs = Math.Clamp(PositionMs + heldDirection * ScrubSpeed.MsPerSecond(held, SpanMs) * dt / 1000, 0, DurationMs);
        // The head glides to the middle third of the view and drags the view along from there.
        if (heldDirection > 0 && PositionMs > StartMs + 2 * SpanMs / 3) StartMs = PositionMs - 2 * SpanMs / 3;
        else if (heldDirection < 0 && PositionMs < StartMs + SpanMs / 3) StartMs = PositionMs - SpanMs / 3;
        if (heldExtend && rangeAnchor is { } anchor)
        {
            double lo = Math.Min(anchor, PositionMs), hi = Math.Max(anchor, PositionMs);
            range = hi - lo >= 1 ? (lo, hi) : null;
            RangeChanged();
        }
        return true;
    }
    public void Select(int index) => SetSelection([index], index);

    // The highlighted range of the waveform (Shift+drag, Shift+←/→): what Auto-time range analyzes. The anchor is
    // where Shift+←/→ started, so the range grows and shrinks around it as the playhead moves.
    private (double Start, double End)? range;
    private double? rangeAnchor;
    public (double Start, double End)? Range => range;
    public bool HasRange => range is not null;
    public string RangeLabel => range is { } r
        ? TimeFormat.Osu(r.Start) + " – " + TimeFormat.Osu(r.End) + " (" + ((r.End - r.Start) / 1000).ToString("0.0", Invariant) + " s)" : "";
    /// <summary>Highlights [from, to] (either order); a range of under 1 ms clears it.</summary>
    public void SetRange(double fromMs, double toMs)
    {
        double lo = Math.Clamp(Math.Min(fromMs, toMs), 0, DurationMs), hi = Math.Clamp(Math.Max(fromMs, toMs), 0, DurationMs);
        range = hi - lo >= 1 ? (lo, hi) : null;
        rangeAnchor = null;
        RangeChanged();
    }
    public void ClearRange() { range = null; rangeAnchor = null; RangeChanged(); }
    private void RangeChanged() { OnPropertyChanged(nameof(Range)); OnPropertyChanged(nameof(HasRange)); OnPropertyChanged(nameof(RangeLabel)); }
    /// <summary>
    /// Shift+←/→: steps the playhead by the beat snap, as ←/→ do, and highlights from where the Shift steps began to the
    /// playhead. Starting from either end of an existing range keeps its other end.
    /// </summary>
    public void ExtendRange(int direction) => Run(() =>
    {
        if (Edits.RedLines.Count == 0 || direction == 0) return;
        double anchor = rangeAnchor ?? (range is { } r && Math.Abs(PositionMs - r.End) < 1 ? r.Start
            : range is { } q && Math.Abs(PositionMs - q.Start) < 1 ? q.End : PositionMs);
        StepSnap(direction);
        double lo = Math.Min(anchor, PositionMs), hi = Math.Max(anchor, PositionMs);
        range = hi - lo >= 1 ? (lo, hi) : null;
        rangeAnchor = anchor;
        RangeChanged();
    });
    /// <summary>Selects the red lines inside a time band (box select); <paramref name="add"/> keeps the current selection.</summary>
    public void SelectRange(double fromMs, double toMs, bool add) => Run(() =>
    {
        CancelDrag();
        double lo = Math.Min(fromMs, toMs), hi = Math.Max(fromMs, toMs);
        var inside = Edits.RedLines.Select((p, i) => (p.Time, i)).Where(p => p.Time >= lo && p.Time <= hi).Select(p => p.i).ToArray();
        SetSelection(add ? selection.Concat(inside) : inside, add && selectedRow is not null ? selectedRow.Index : inside.FirstOrDefault(-1));
        Status = SelectionLabel + ".";
    });
    public void SelectAll() => Run(() =>
    {
        CancelDrag();
        SetSelection(Enumerable.Range(0, Edits.RedLines.Count), selectedRow?.Index ?? 0);
        Status = SelectionLabel + ".";
    });
    public void ClearSelection() => SetSelection([], -1);
    /// <summary>Esc cancels a drag first, then clears the range, then the selection.</summary>
    public void Escape() { if (IsDragging) CancelDrag(); else if (HasRange) ClearRange(); else ClearSelection(); }
    private void SetSelection(IEnumerable<int> indices, int primary, IEnumerable<int>? greenIndices = null, int primaryGreen = -1)
    {
        var reds = Edits.RedLines;
        selection = indices.Where(i => i >= 0 && i < reds.Count).Distinct().Order().ToArray();
        if (!selection.Contains(primary)) primary = selection.Length > 0 ? selection[0] : -1;
        selectionPoints = selection.Select(i => reds[i]).ToArray();
        var greens = InheritedRows;
        greenSelection = (greenIndices ?? []).Where(i => i >= 0 && i < greens.Count).Distinct().Order().ToArray();
        if (!greenSelection.Contains(primaryGreen)) primaryGreen = primary < 0 ? greenSelection.FirstOrDefault(-1) : -1;
        selectedGreenRow = primaryGreen < 0 ? null : greens[primaryGreen];
        var row = primary < 0 ? null : new RedLineRow(primary, reds[primary], true);
        updatingSelection = true;
        try
        {
            OnPropertyChanged(nameof(Rows)); // Markers changed; the list rebuilds and may clear its own selection.
            OnPropertyChanged(nameof(SelectedIndices));
            selectedRow = row;
            OnPropertyChanged(nameof(SelectedRow));
        }
        finally { updatingSelection = false; }
        if (FieldPoint is { } point)
        {
            TimeText = FieldTime(point);
            BpmText = FieldValue(point);
            MeterText = point.Meter.ToString(Invariant);
        }
        OnPropertyChanged(nameof(SelectedGreenIndices)); OnPropertyChanged(nameof(HasPointSelection));
        OnPropertyChanged(nameof(IsRedPointSelected)); OnPropertyChanged(nameof(SelectedPointLabel)); OnPropertyChanged(nameof(PointValueLabel));
        OnPropertyChanged(nameof(KiaiEnabled));
        OnPropertyChanged(nameof(SelectedIndices)); OnPropertyChanged(nameof(SelectionLabel));
    }
    private static string FieldTime(TimingPoint point) => point.Uninherited ? OsuPrecision.FormatTime(point.Time) : point.Time.ToString("R", Invariant);
    private static string FieldValue(TimingPoint point) => point.Uninherited ? OsuPrecision.FormatBpm(point.Bpm) : point.Sv.ToString("0.########", Invariant);
    public void RestorePointFields() => SetSelection(selection, selectedRow?.Index ?? -1, greenSelection, selectedGreenRow?.GreenIndex ?? -1);
    public void ApplyFields() => TryApplyFields();
    public bool TryApplyFields()
    {
        bool saved = false;
        Run(() =>
        {
            if (FieldPoint is not { } point) { saved = true; return; }
            bool timeChanged = TimeText != FieldTime(point), valueChanged = BpmText != FieldValue(point);
            bool meterChanged = point.Uninherited && MeterText != point.Meter.ToString(Invariant);
            if (!timeChanged && !valueChanged && !meterChanged) { saved = true; return; }
            double time = timeChanged ? OsuPrecision.Time(double.Parse(TimeText, NumberStyles.Float, Invariant)) : point.Time;
            double value = valueChanged ? double.Parse(BpmText, NumberStyles.Float, Invariant) : point.Uninherited ? point.Bpm : point.Sv;
            int meter = meterChanged ? int.Parse(MeterText, NumberStyles.Integer, Invariant) : point.Meter;
            if (!double.IsFinite(value) || value <= 0) throw new ArgumentException(point.Uninherited ? "BPM must be positive." : "SV must be positive.");
            if (!point.Uninherited && valueChanged && (value < 0.01 || value > 10)) throw new ArgumentException("SV must be between 0.01 and 10.");
            double length = valueChanged ? point.Uninherited ? 60000 / OsuPrecision.Bpm(value) : -100 / value : point.BeatLength;
            var updated = (point with { Time = time, BeatLength = length, Meter = meter }).Validate();
            if (selectedGreenRow is { } green)
            {
                var visible = InheritedRows.Select(r => r.Point).ToArray();
                visible[green.GreenIndex] = updated;
                ReplaceVisibleGreens(visible);
                SelectGreenAt(updated.Time);
            }
            else
            {
                Edits.Update(SelectedRow!.Index, updated);
                Select(Edits.RedLines.ToList().FindIndex(p => ReferenceEquals(p, updated)));
            }
            Status = point.Uninherited ? "Timing point saved." : "Inherited point saved.";
            saved = true;
        });
        return saved;
    }
    public bool BeginDrag(double time, double tolerance, bool stretch)
    {
        if (IsBusy) return false;
        drag = TimingInteraction.Hit(Edits.RedLines, time, tolerance, stretch);
        if (drag is null) return false;
        Select(drag.Index); dragTime = time;
        return true;
    }
    public void UpdateDrag(double time)
    {
        if (drag is null) return;
        try
        {
            var point = drag.Preview(time);
            if (point.Time != drag.Original.Time && Edits.RedLines.Where((_, i) => i != drag.Index).Any(p => p.Time == point.Time))
                throw new ArgumentException("A dragged red line cannot overlap a neighbour. Paste to create a duplicate.");
            preview = BeatGrid.Sort(Edits.RedLines.Select((p, i) => i == drag.Index ? point : p));
            dragTime = time;
        }
        catch (ArgumentException ex) { preview = null; Status = ex.Message; }
        OnPropertyChanged(nameof(DisplayRedLines));
    }
    public void CommitDrag() => Run(() =>
    {
        var active = drag;
        bool valid = preview is not null;
        var updated = active?.Preview(dragTime);
        CancelDrag();
        if (active is null || !valid) return;
        active.Apply(Edits, dragTime);
        Select(Edits.RedLines.ToList().IndexOf(updated!));
    });
    public void CancelDrag() { drag = null; preview = null; OnPropertyChanged(nameof(DisplayRedLines)); }
    public void Insert() => Run(() =>
    {
        CancelDrag(); Edits.AddRedLine(PositionMs);
        double time = OsuPrecision.Time(PositionMs);
        if (TableTab == 2) TableTab = 0;
        Select(Edits.RedLines.ToList().FindLastIndex(p => p.Time == time));
        Status = "Red line added at " + OsuPrecision.FormatTime(time) + " ms.";
    });
    private void ReplaceVisibleGreens(IEnumerable<TimingPoint> visible) =>
        Edits.ReplaceTiming(Edits.RedLines, StoredGreens(visible, Edits.RedLines));
    private IEnumerable<TimingPoint> StoredGreens(IEnumerable<TimingPoint> visible, IReadOnlyList<TimingPoint> reds) =>
        AutoNormalizeSv ? LiveNormalization.FromVisible(visible, reds, NormalizationEnd) : visible;
    private void SelectGreenAt(double time)
    {
        int index = InheritedRows.ToList().FindLastIndex(r => r.Point.Time == time);
        SetSelection([], -1, index < 0 ? [] : [index], index);
    }
    public void InsertInherited() => Run(() =>
    {
        CancelDrag();
        double time = OsuPrecision.Time(PositionMs);
        var visible = InheritedRows.Select(r => r.Point).ToArray();
        var source = Edits.RedLines.Concat(visible).Where(p => p.Time <= time)
            .OrderBy(p => p.Time).ThenBy(p => !p.Uninherited).LastOrDefault() ?? new TimingPoint(0, 500);
        var added = source with { Time = time, BeatLength = source.Uninherited ? -100 : source.BeatLength, Uninherited = false, Effects = source.Effects & ~TimingPoint.OmitFirstBarline };
        ReplaceVisibleGreens(visible.Append(added));
        if (TableTab == 1) TableTab = 0;
        SelectGreenAt(time);
        Status = "Inherited point added at " + OsuPrecision.FormatTime(time) + " ms.";
    });
    public void Delete() => Run(() =>
    {
        CancelDrag();
        if (greenSelection.Length > 0)
        {
            var remove = greenSelection.ToHashSet();
            var visible = InheritedRows.Where(r => !remove.Contains(r.GreenIndex)).Select(r => r.Point).ToArray();
            var reds = Edits.RedLines.Where((_, i) => !selection.Contains(i)).ToArray();
            Edits.ReplaceTiming(reds, StoredGreens(visible, reds));
            Status = "Selected timing points deleted. Automatic normalization rows remain where needed.";
            return;
        }
        if (selection.Length == 0) return;
        int count = selection.Length;
        Edits.Delete(selection); // One undo step for the whole selection.
        Status = count == 1 ? "Red line deleted." : count.ToString(Invariant) + " red lines deleted.";
    });
    public void Undo() => Run(() => { CancelDrag(); Edits.Undo(); });
    public void Redo() => Run(() => { CancelDrag(); Edits.Redo(); });
    private void Shift(bool milliseconds, double amount)
    {
        CancelDrag();
        int[] chosen = selection;
        int primary = selectedRow?.Index ?? -1;
        if (milliseconds) Edits.ShiftRedLinesMs(ShiftScope, amount, chosen);
        else Edits.ShiftRedLines(ShiftScope, amount, chosen);
        SetSelection(chosen, primary); // Shifts keep the order, so the same indices are the moved lines.
        Status = "Shifted " + (ShiftScope == RedLineScope.All ? "all red lines" : chosen.Length == 1 ? "selected red line" :
            chosen.Length.ToString(Invariant) + " selected red lines") + " by " + (milliseconds
            ? amount.ToString("+0.##;-0.##", Invariant) + " ms."
            // As the step box shows it: +1/4 beat, not 0.25 beats.
            : (amount < 0 ? "-" : "+") + (Math.Abs(amount) == 1 ? "1" : "1/" + (1 / Math.Abs(amount)).ToString("0", Invariant)) + " beat.") +
            " Green lines stay fixed.";
    }
    // A run of BPM nudges is one undo step: the step it extends is the last one recorded, for the same lines, a moment ago.
    private int nudgeVersion = -1;
    private int[] nudgeLines = [];
    private double nudgeAt = double.NegativeInfinity;
    /// <summary>A nudge within this long of the last one, on the same lines, extends its undo step.</summary>
    public const double NudgeMergeMs = 1500;
    /// <summary>The BPM bounds of a nudge (osu! timing points).</summary>
    public const double MinNudgeBpm = 1, MaxNudgeBpm = 1000;
    /// <summary>
    /// ❮ ❯ BPM: moves the BPM of every selected red line to the next multiple of <see cref="SelectedBpmStep"/> (times
    /// <paramref name="scale"/>) below or above it, keeping its time, so a BPM that is a little off is tuned against the
    /// waveform without typing it. A run of nudges is one undo step. 142.997 + 0.01 → 143, not 143.007.
    /// </summary>
    public void NudgeBpm(int direction, double scale = 1) => Run(() =>
    {
        CancelDrag();
        if (direction == 0 || !(scale > 0)) return;
        if (selection.Length == 0) throw new InvalidOperationException("Select the red line to tune in the timing table first.");
        int[] chosen = selection.ToArray();
        int primary = selectedRow?.Index ?? chosen[0];
        double step = SelectedBpmStep * scale;
        var after = Edits.RedLines.Select((p, i) => chosen.Contains(i)
            ? p with { BeatLength = 60000 / NextBpm(p.Bpm, step, direction) } : p).ToArray();
        double now = wallMilliseconds();
        bool continuing = Edits.History.Version == nudgeVersion && now - nudgeAt < NudgeMergeMs && chosen.SequenceEqual(nudgeLines);
        Edits.ReplaceMerging(after, continuing);
        nudgeVersion = Edits.History.Version; nudgeLines = chosen; nudgeAt = now;
        SetSelection(chosen, primary);
        var line = Edits.RedLines[primary];
        Status = (chosen.Length == 1 ? "Red line at " + OsuPrecision.FormatTime(line.Time) + " ms" : Count(chosen.Length, "red line")) +
            " tuned to " + OsuPrecision.FormatBpm(line.Bpm) + " BPM. Ctrl+Z undoes the whole run.";
    });
    /// <summary>The next multiple of <paramref name="step"/> above (direction &gt; 0) or below the BPM, at osu! precision.</summary>
    public static double NextBpm(double bpm, double step, int direction)
    {
        double q = OsuPrecision.Bpm(bpm) / step;
        double k = direction > 0 ? Math.Floor(q + 1e-6) + 1 : Math.Ceiling(q - 1e-6) - 1;
        return OsuPrecision.Bpm(Math.Clamp(k * step, MinNudgeBpm, MaxNudgeBpm));
    }
    /// <summary>
    /// Moves the selected red lines so the one the fields edit lands on the playhead (yellow line), keeping their spacing:
    /// for a red line already placed, where + would add a second one. One undo step; a move that would cross or reach an
    /// unselected red line is rejected, as a shift is.
    /// </summary>
    public void MoveToPlayhead() => Run(() =>
    {
        CancelDrag();
        if (selectedGreenRow is { } green)
        {
            double target = OsuPrecision.Time(PositionMs), shift = target - green.Point.Time;
            if (shift == 0) return;
            var chosenGreens = greenSelection.ToHashSet();
            ReplaceVisibleGreens(InheritedRows.Select(r => chosenGreens.Contains(r.GreenIndex) ? r.Point with { Time = OsuPrecision.Time(r.Point.Time + shift) } : r.Point));
            SelectGreenAt(target);
            Status = "Inherited points moved to the playhead.";
            return;
        }
        if (selectedRow is not { } row) throw new InvalidOperationException("Select the red line to move in the timing table first.");
        double delta = OsuPrecision.Time(PositionMs) - row.Point.Time;
        if (delta == 0) { Status = "The red line is already on the playhead."; return; }
        int[] chosen = selection;
        Edits.ShiftRedLinesMs(RedLineScope.Selected, delta, chosen);
        SetSelection(chosen, row.Index);
        Status = (chosen.Length == 1 ? "Red line" : chosen.Length.ToString(Invariant) + " red lines") + " moved by " +
            delta.ToString("+0;-0", Invariant) + " ms onto the playhead at " + TimeFormat.Osu(PositionMs) + ". Ctrl+Z undoes.";
    });
    /// <summary>The audio files Open and drag-and-drop take (what the decoder reads); beatmaps are .osu.</summary>
    public static IReadOnlyList<string> AudioExtensions { get; } =
        [".mp3", ".ogg", ".wav", ".flac", ".mp4", ".m4a", ".aac", ".opus", ".wma", ".aif", ".aiff", ".oga"];
    public static bool CanOpen(string path) => IsBeatmap(path) || IsAudio(path);
    private static bool IsBeatmap(string path) => Path.GetExtension(path).Equals(".osu", StringComparison.OrdinalIgnoreCase);
    private static bool IsAudio(string path) => AudioExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
    /// <summary>
    /// Opens what was dropped on the window: a beatmap first (it brings its audio), else the first audio file. Anything
    /// else only explains what can be dropped.
    /// </summary>
    public async Task OpenDroppedAsync(IEnumerable<string> paths)
    {
        var files = paths.ToList();
        if ((files.FirstOrDefault(IsBeatmap) ?? files.FirstOrDefault(IsAudio)) is { } path) await LoadAsync(path);
        else Status = "Drop an osu! beatmap (.osu) or an audio file (" + string.Join(", ", AudioExtensions) + ") to open it.";
    }
    /// <summary>
    /// Pastes red and green rows, retaining duplicates. One undo step; pasted red lines are selected.
    /// </summary>
    public void Paste(string text, bool replaceAll = false) => Run(() =>
    {
        CancelDrag();
        var (lines, pasted, replaced, greens) = RangeEdit.Paste(Edits.RedLines, text, replaceAll);
        var visible = NormalizedTiming().Points.Where(p => !p.Uninherited).ToArray();
        // Materialise automatic rows when pasting, so a pasted duplicate remains a distinct row.
        var existing = AutoNormalizeSv ? Edits.GreenLines.Concat(LiveNormalization.FromVisible(visible.Skip(Edits.GreenLines.Count),
            Edits.RedLines, NormalizationEnd)) : Edits.GreenLines;
        var incoming = StoredGreens(TimingClipboard.Parse(text).Where(p => !p.Uninherited), lines);
        Edits.ReplaceTiming(lines, existing.Concat(incoming));
        var indices = Enumerable.Range(0, Edits.RedLines.Count)
            .Where(i => pasted.Any(p => ReferenceEquals(p, Edits.RedLines[i]))).ToArray();
        SetSelection(indices, indices.FirstOrDefault(-1));
        Status = "Pasted " + Count(pasted.Length, "red line") + (replaceAll ? ", replacing all " + Count(replaced, "red line")
            : " (overlaps retained)") + (greens > 0 ? " and " + Count(greens, "green line") : "") + ". Ctrl+Z undoes.";
    });
    private static string Count(int n, string noun) => n.ToString(Invariant) + " " + noun + (n == 1 ? "" : "s");

    /// <summary>
    /// Auto-time range: re-times the highlighted range. The red lines inside it are replaced by what the analysis finds
    /// there (a tempo change inside gets its own line); a BPM in the box locks one line at that BPM; a drift mode puts
    /// drift lines in the range. The music after the range keeps its timing (<see cref="RangeEdit.Replace"/>). One undo
    /// step; the new lines are selected.
    /// </summary>
    public async Task AutoTimeRangeAsync(Func<AudioContent, RangeRequest, Task<MultiTimingDetection>> analyze)
    {
        if (IsBusy || disposed) return;
        if (Audio is null) { Status = "Load audio before auto-timing."; return; }
        if (range is not { } r) { Status = "Highlight a range first: Shift+drag across the waveform, or Shift+←/→."; return; }
        if (r.End - r.Start < SectionFit.MinimumSectionMs)
        {
            Status = "The range is too short to analyze reliably: it needs at least " + OsuPrecision.FormatTime(SectionFit.MinimumSectionMs) +
                " ms. Highlight more of the section (a tempo change may sit inside it).";
            return;
        }
        if (!TryLockedBpm(out double? bpm)) return;
        CancelDrag(); IsBusy = true;
        var before = Edits.RedLines.ToArray();
        string label = RangeLabel;
        Status = "Analyzing " + label + "...";
        MultiTimingDetection detection;
        try { detection = await analyze(Audio, new RangeRequest(r.Start, r.End, bpm, bpm is null ? DriftBeatsPerLine : 0, bpm is null && BeatModelSelected)); }
        catch (Exception ex) { Status = "Auto-time range failed: " + ex.Message; return; }
        finally { IsBusy = false; }
        if (disposed) return;
        Run(() =>
        {
            if (!Edits.RedLines.SequenceEqual(before)) throw new InvalidOperationException("Timing changed during analysis; try again.");
            if (detection.Error is not null) throw new InvalidOperationException("Auto-time range failed: " + detection.Error);
            var found = MultiTimingProposal.FromDetection(detection).Sections;
            var (lines, continuation) = RangeEdit.Replace(before, r.Start, r.End, InheritSamples(found));
            int removed = before.Count(p => p.Time >= r.Start && p.Time < r.End);
            Edits.Replace(lines);
            var added = found.Select(p => OsuPrecision.Time(p.Time)).Where(t => t >= r.Start && t < r.End).ToHashSet();
            var indices = Enumerable.Range(0, Edits.RedLines.Count).Where(i => added.Contains(Edits.RedLines[i].Time)).ToArray();
            SetSelection(indices, indices.FirstOrDefault(-1));
            var inside = indices.Select(i => Edits.RedLines[i]).ToArray();
            Status = "Range " + label + ": " + (inside.Length == 1
                    ? OsuPrecision.FormatBpm(inside[0].Bpm) + " BPM from " + OsuPrecision.FormatTime(inside[0].Time) + " ms"
                    : Count(inside.Length, "red line") + ", " + string.Join(" → ", inside.Take(6).Select(p => OsuPrecision.FormatBpm(p.Bpm))) +
                      (inside.Length > 6 ? " …" : "") + " BPM") +
                (bpm is not null ? " (BPM box)" : "") + (removed > 0 ? "; replaced " + Count(removed, "red line") : "") +
                (continuation is { } c ? "; the timing after the range continues from " + OsuPrecision.FormatTime(c.Time) + " ms" : "") +
                ". Ctrl+Z undoes.";
        });
    }

    public Task AnalyzeAsync(Func<AudioContent, Task<TimingDetection>> analyze) => AnalyzeAsync((audio, _) => analyze(audio));
    /// <summary>Whole-song auto-time, honouring the BPM box. Nothing changes until the proposal is applied.</summary>
    public async Task AnalyzeAsync(Func<AudioContent, TimingRequest, Task<TimingDetection>> analyze)
    {
        if (IsBusy || Audio is null) { if (Audio is null) Status = "Load audio before auto-timing."; return; }
        if (!TryLockedBpm(out double? bpm)) return;
        CancelDrag(); IsBusy = true; Proposal = null; MultiProposal = null;
        Status = "Analyzing audio...";
        try
        {
            Proposal = AutoTimingProposal.FromDetection(await analyze(Audio, new TimingRequest(0, DurationMs, bpm)), locked: bpm is not null);
            ProposalOffsetText = Proposal.OffsetMs is { } offset ? OsuPrecision.FormatTime(offset) : "";
            Status = Proposal.Description;
        }
        catch (Exception ex) { Status = "Auto-time failed: " + ex.Message; }
        finally { IsBusy = false; }
    }
    /// <summary>Detects BPM (or uses the BPM box) and offset from the selected red line to the next one, then retimes that line.</summary>
    public Task AutoTimeSectionAsync(Func<AudioContent, TimingRequest, Task<TimingDetection>> analyze) => FitSectionAsync(analyze, keepBpm: false);
    /// <summary>Keeps the selected red line's BPM and moves it to the nearest detected beat of its section.</summary>
    public Task FitOffsetAsync(Func<AudioContent, TimingRequest, Task<TimingDetection>> analyze) => FitSectionAsync(analyze, keepBpm: true);
    private async Task FitSectionAsync(Func<AudioContent, TimingRequest, Task<TimingDetection>> analyze, bool keepBpm)
    {
        if (IsBusy || disposed) return;
        if (Audio is null) { Status = "Load audio before auto-timing."; return; }
        if (selectedRow is not { } row) { Status = "Select the red line that starts the section."; return; }
        var reds = Edits.RedLines;
        double start = Math.Max(0, row.Point.Time);
        double end = row.Index + 1 < reds.Count ? reds[row.Index + 1].Time : DurationMs;
        if (end - start < SectionFit.MinimumSectionMs)
        {
            Status = "Section too short to analyze reliably: it needs at least " + OsuPrecision.FormatTime(SectionFit.MinimumSectionMs) +
                " ms up to the next red line. For a short tempo step, put its red line on a beat of the previous section: ←/→ to the beat, then Ctrl+P.";
            return;
        }
        double? bpm = row.Point.Bpm;
        if (!keepBpm && !TryLockedBpm(out bpm)) return;
        CancelDrag(); IsBusy = true;
        Status = "Analyzing section " + OsuPrecision.FormatTime(start) + "–" + OsuPrecision.FormatTime(end) + " ms...";
        TimingDetection detection;
        try { detection = await analyze(Audio, new TimingRequest(start, end, bpm)); }
        catch (Exception ex) { Status = "Auto-time failed: " + ex.Message; return; }
        finally { IsBusy = false; }
        if (disposed) return;
        Run(() =>
        {
            if (row.Index >= Edits.RedLines.Count || Edits.RedLines[row.Index] != row.Point)
                throw new InvalidOperationException("Timing changed during analysis; try again.");
            if (detection.Error is not null) throw new InvalidOperationException("Auto-time failed: " + detection.Error);
            if (detection.Bpm is not { } found || detection.OsuOffsetMs is not { } phase)
                throw new InvalidOperationException(detection.ApproximateBpm is { } approximate
                    ? "Only an approximate BPM (" + OsuPrecision.FormatBpm(approximate) + ") was found for this section. Type a BPM in the BPM box and try again."
                    : "No beat found in this section.");
            double fitted = keepBpm ? row.Point.Bpm : OsuPrecision.Bpm(found);
            Edits.Retime(row.Index, SectionFit.Place(row.Point.Time, phase, fitted), keepBpm ? null : 60000 / fitted);
            Select(row.Index);
            var line = Edits.RedLines[row.Index];
            Status = "Section " + OsuPrecision.FormatTime(start) + "–" + OsuPrecision.FormatTime(end) + " ms: " +
                OsuPrecision.FormatBpm(line.Bpm) + " BPM (" + (keepBpm ? "kept" : bpm is null ? "detected" : "BPM box") + "); red line " +
                OsuPrecision.FormatTime(row.Point.Time) + " → " + OsuPrecision.FormatTime(line.Time) + " ms. Ctrl+Z undoes." +
                (SectionFit.IsSuspicious(line.Time - row.Point.Time, line.BeatLength)
                    ? " Moved more than ⅛ beat: it may have locked onto an off-beat, so check by ear." : "");
        });
    }
    /// <summary>
    /// Moves each selected red line onto the strongest onset within ±¼ of its beat (at most 60 ms): put lines on the
    /// accents of a short tempo step by ear, then snap them. One undo step; a move that would cross a neighbour or find
    /// no onset leaves that line where it is.
    /// </summary>
    public async Task SnapToOnsetsAsync(Func<AudioContent, IReadOnlyList<OnsetQuery>, Task<IReadOnlyList<double?>>> find)
    {
        if (IsBusy || disposed) return;
        if (Audio is null) { Status = "Load audio before snapping red lines to onsets."; return; }
        if (selection.Length == 0) { Status = "Select the red lines to snap (box-select several), then Snap to onsets."; return; }
        var reds = Edits.RedLines.ToArray();
        int[] chosen = selection.ToArray();
        int primary = selectedRow?.Index ?? chosen[0];
        var queries = chosen.Select(i => new OnsetQuery(reds[i].Time, Math.Min(60, reds[i].BeatLength / 4))).ToList();
        CancelDrag(); IsBusy = true;
        Status = "Looking for onsets...";
        IReadOnlyList<double?> found;
        try { found = await find(Audio, queries); }
        catch (Exception ex) { Status = "Snap to onsets failed: " + ex.Message; return; }
        finally { IsBusy = false; }
        if (disposed) return;
        Run(() =>
        {
            if (!Edits.RedLines.SequenceEqual(reds)) throw new InvalidOperationException("Timing changed while looking for onsets; try again.");
            var after = reds.ToArray();
            int missing = 0, blocked = 0;
            var moves = new List<string>();
            for (int q = 0; q < chosen.Length; q++)
            {
                int i = chosen[q];
                if (found[q] is not { } onset) { missing++; continue; }
                double time = OsuPrecision.Time(onset);
                if (i > 0 && time <= after[i - 1].Time || i + 1 < after.Length && time >= after[i + 1].Time) { blocked++; continue; }
                if (time != after[i].Time) moves.Add((time - after[i].Time).ToString("+0;-0", Invariant));
                after[i] = after[i] with { Time = time };
            }
            if (moves.Count > 0) { Edits.Replace(after); SetSelection(chosen, primary); }
            Status = "Snapped " + moves.Count.ToString(Invariant) + " of " + chosen.Length.ToString(Invariant) + " red lines to onsets" +
                (moves.Count > 0 ? " (ms: " + string.Join(", ", moves) + ")" : "") +
                (missing > 0 ? "; " + missing.ToString(Invariant) + " found no onset nearby" : "") +
                (blocked > 0 ? "; " + blocked.ToString(Invariant) + " would cross a neighbour" : "") +
                (moves.Count > 0 ? ". Ctrl+Z undoes." : ".");
        });
    }
    /// <summary>
    /// Sets each selected red line's BPM from the gap to the next red line and chains the selected lines after the
    /// first onto the end of the step before (<see cref="StepTiming.FromSpacing"/>): after placing a line on every
    /// step of a short ramp. One undo step.
    /// </summary>
    /// <summary>
    /// ×2, ÷2, ×1.5, ×⅔ ... on the selected red lines: their BPM is multiplied, their times stay. For a section
    /// auto-timed at the wrong octave or ratio of the map's tempo. One undo step.
    /// </summary>
    public void ScaleTempo(double factor) => Run(() =>
    {
        CancelDrag();
        int[] chosen = selection.ToArray();
        int primary = selectedRow?.Index ?? (chosen.Length > 0 ? chosen[0] : -1);
        Edits.Replace(TempoScale.Apply(Edits.RedLines, chosen, factor));
        SetSelection(chosen, primary);
        var lines = chosen.Select(i => Edits.RedLines[i]).ToArray();
        Status = TempoScale.Label(factor) + ": " + (lines.Length == 1 ? "red line at " + OsuPrecision.FormatTime(lines[0].Time) + " ms now " +
            OsuPrecision.FormatBpm(lines[0].Bpm) + " BPM" : Count(lines.Length, "red line") + " rescaled") + ". Ctrl+Z undoes.";
    });
    public void ApplyTempoFactor() => Run(() => ScaleTempo(double.Parse(TempoFactorText, NumberStyles.Float, Invariant)));

    /// <summary>
    /// Fill ramp: a red line on every beat between the selected red lines, the BPM moving in a straight line from one
    /// to the next (<see cref="RampFill"/>). Select where the ramp starts and where it ends (and any holds between).
    /// Red lines between them are replaced. One undo step; the ramp is selected.
    /// </summary>
    public void FillRamp() => Run(() =>
    {
        CancelDrag();
        var before = Edits.RedLines.ToArray();
        int[] chosen = selection.ToArray();
        var (lines, added) = RampFill.Fill(before, chosen);
        double from = before[chosen.Min()].Time, to = before[chosen.Max()].Time;
        int replaced = before.Count(p => p.Time > from && p.Time < to && !chosen.Any(i => before[i].Time == p.Time));
        Edits.Replace(lines);
        var inside = Enumerable.Range(0, Edits.RedLines.Count).Where(i => Edits.RedLines[i].Time >= from && Edits.RedLines[i].Time <= to).ToArray();
        SetSelection(inside, inside.FirstOrDefault(-1));
        Status = "Ramp " + OsuPrecision.FormatBpm(before[chosen.Min()].Bpm) + " → " + OsuPrecision.FormatBpm(before[chosen.Max()].Bpm) + " BPM: " +
            Count(added, "red line") + ", one per beat" + (replaced > 0 ? "; replaced " + Count(replaced, "red line") : "") +
            ". Listen to it; where the music holds a tempo midway, put a red line there and fill each part. Ctrl+Z undoes.";
    });

    // Section check (C): the sections whose grid the onsets do not support, worth a listen.
    private IReadOnlyList<SectionMark> marks = [];
    public IReadOnlyList<SectionMark> Marks { get => marks; private set { marks = value; OnPropertyChanged(nameof(Marks)); OnPropertyChanged(nameof(HasMarks)); } }
    public bool HasMarks => marks.Count > 0;
    /// <summary>
    /// Check sections: scores every red line's section against the onsets and lists the ones worth a listen (the
    /// waveform marks them and the table selects them). <paramref name="check"/> returns one mark per red line, or
    /// null for a section that looks right or is too short to judge.
    /// </summary>
    public async Task CheckSectionsAsync(Func<AudioContent, IReadOnlyList<TimingPoint>, Task<IReadOnlyList<SectionMark?>>> check)
    {
        if (IsBusy || disposed) return;
        if (Audio is null) { Status = "Load audio before checking sections."; return; }
        CancelDrag(); IsBusy = true;
        var before = Edits.RedLines.ToArray();
        Status = "Checking sections...";
        IReadOnlyList<SectionMark?> found;
        try { found = await check(Audio, before); }
        catch (Exception ex) { Status = "Check failed: " + ex.Message; return; }
        finally { IsBusy = false; }
        if (disposed) return;
        Run(() =>
        {
            if (!Edits.RedLines.SequenceEqual(before)) throw new InvalidOperationException("Timing changed during the check; try again.");
            var list = found.Select((m, i) => m is null ? null : m with { Index = i }).OfType<SectionMark>().ToList();
            Marks = list;
            if (list.Count == 0) { Status = "Check: nothing stands out; every section's grid sits on the onsets."; return; }
            SetSelection(list.Select(m => m.Index), list[0].Index);
            Status = "Check: " + Count(list.Count, "section") + " worth a listen (marked on the waveform, selected in the table): " +
                string.Join("; ", list.Take(4).Select(m => m.Describe(Edits.RedLines[m.Index]))) + (list.Count > 4 ? "; …" : "") + ".";
        });
    }

    public void BpmFromSpacing() => Run(() =>
    {
        CancelDrag();
        if (selection.Length == 0)
            throw new InvalidOperationException("Select the red lines of the tempo steps (box-select them), then BPM from spacing.");
        int[] chosen = selection.ToArray();
        int primary = selectedRow?.Index ?? chosen[0];
        var before = Edits.RedLines.ToArray();
        var result = StepTiming.FromSpacing(before, chosen);
        if (result.Steps.Count == 0)
            throw new InvalidOperationException("Each selected red line needs a next red line at most " +
                StepTiming.MaxBeats.ToString(Invariant) + " beats away to measure its step.");
        int left = chosen.Length - result.Steps.Count;
        double largest = before.Zip(result.Lines, (a, b) => Math.Abs(a.Time - b.Time)).Max();
        Edits.Replace(result.Lines);
        SetSelection(chosen, primary);
        Status = "BPM from spacing: " + StepTiming.Describe(result.Steps) +
            (result.Moved > 0 ? "; chained " + result.Moved.ToString(Invariant) + (result.Moved == 1 ? " line" : " lines") +
                " onto the end of the step before (up to " + largest.ToString("0", Invariant) + " ms)" : "") +
            (left > 0 ? "; " + left.ToString(Invariant) + (left == 1 ? " line keeps its" : " lines keep their") + " BPM (the last red line, or more than " +
                StepTiming.MaxBeats.ToString(Invariant) + " beats to the next: a section, not a step)" : "") + ". Ctrl+Z undoes.";
    });
    private bool TryLockedBpm(out double? bpm)
    {
        bpm = null;
        string text = LockedBpmText.Trim();
        if (text.Length == 0) return true;
        if (!double.TryParse(text, NumberStyles.Float, Invariant, out double value) || !double.IsFinite(value) || value < 1 || value > 1000)
        {
            Status = "BPM box: enter a BPM from 1 to 1000 (for example 160), or leave it empty to detect.";
            return false;
        }
        bpm = OsuPrecision.Bpm(value);
        return true;
    }
    public TimingPoint? ProposalPoint()
    {
        if (Proposal is not { } pending) return null;
        double bpm = double.Parse(AnalysisBpmText, NumberStyles.Float, Invariant);
        if (!double.IsFinite(bpm) || bpm < 1 || bpm > 1000) throw new ArgumentException("Enter a BPM from 1 to 1000.");
        return (pending with { Bpm = OsuPrecision.Bpm(bpm) }).AtOffset(ProposalOffsetText);
    }
    public void ApplyProposal(bool confirmed) => Run(() =>
    {
        if (!confirmed || ProposalPoint() is not { } point) return;
        CancelDrag(); Edits.Replace(InheritSamples([point])); Select(0);
        Status = "Auto-time proposal applied. Undo restores the previous red lines.";
    });
    /// <summary>
    /// Whole-song multi-BPM auto-time: proposes every red line the detector finds. The BPM box still forces one
    /// locked BPM, so it falls back to the single-line path when the box is filled. Nothing changes until applied.
    /// </summary>
    public async Task AnalyzeMultiAsync(Func<AudioContent, Task<MultiTimingDetection>> analyze) =>
        await AnalyzeLinesAsync(analyze, "Analyzing audio for tempo changes...", null);

    /// <summary>
    /// The one Auto-time button. A BPM in the box: one red line at that BPM with its offset fitted (the original
    /// detector). Normal mode: multi-BPM; a song with one tempo comes back as the original detector's red line with an
    /// editable offset, as the former "Auto-time song" gave (multi-BPM returns exactly that line then), several tempos
    /// as a list. Beat model mode: the beat model's red lines for the whole song. A drift mode: drift lines. Nothing
    /// changes until Apply.
    /// </summary>
    public async Task AutoTimeAsync(Func<AudioContent, TimingRequest, Task<TimingDetection>> single,
        Func<AudioContent, Task<MultiTimingDetection>> multi, Func<AudioContent, int, Task<MultiTimingDetection>> drift,
        Func<AudioContent, Task<MultiTimingDetection>>? beatModel = null)
    {
        if (LockedBpmText.Trim().Length > 0) await AnalyzeAsync(single);
        else if (DriftBeatsPerLine > 0) await AnalyzeDriftAsync(drift);
        else if (BeatModelSelected)
        {
            if (beatModel is null) { Status = "The beat model is not available in this build; choose Normal."; return; }
            await AnalyzeLinesAsync(beatModel, "Running the beat model...",
                " Beat model mode: the model's red lines for the whole song (Normal uses them only where it is sure). Listen to the result.");
        }
        else await AnalyzeLinesAsync(multi, "Analyzing audio...", null, oneLineAsSingle: true);
    }

    /// <summary>
    /// Drift mode: a red line every <see cref="DriftBeatsPerLine"/> beats following a slowly drifting tempo (live
    /// or old recordings). Opt-in: on steady songs one red line is better (Set B aligned 0.93 vs 0.73 for 8 beats).
    /// Reviewed and applied like the multi-BPM proposal.
    /// </summary>
    public async Task AnalyzeDriftAsync(Func<AudioContent, int, Task<MultiTimingDetection>> analyze)
    {
        int beats = DriftBeatsPerLine;
        // On every beat, every red line would also be a bar line: count 4/4 bars from the first line instead.
        await AnalyzeLinesAsync(audio => analyze(audio, beats), "Tracking beats for a drifting tempo...",
            beats == 1 ? " Drift mode: a red line on every beat (fewer where the spacing repeats); bar lines every 4 beats from the first line, Bar line here moves them."
                : " Drift mode: one red line every " + beats.ToString(Invariant) + " beats.",
            beats == 1 ? p => p with { Sections = BarLines.FromDownbeat(p.Sections, 0).Lines } : null);
    }

    private async Task AnalyzeLinesAsync(Func<AudioContent, Task<MultiTimingDetection>> analyze, string working, string? note,
        Func<MultiTimingProposal, MultiTimingProposal>? arrange = null, bool oneLineAsSingle = false)
    {
        if (IsBusy || Audio is null) { if (Audio is null) Status = "Load audio before auto-timing."; return; }
        if (!TryLockedBpm(out double? bpm)) return;
        if (bpm is not null)
        {
            Status = "The BPM box forces one BPM, so multi-BPM auto-time is not used. Clear it to detect tempo changes.";
            return;
        }
        CancelDrag(); IsBusy = true; Proposal = null; MultiProposal = null; ProposalOffsetText = "";
        Status = working;
        try
        {
            var detection = await analyze(Audio);
            var found = MultiTimingProposal.FromDetection(detection);
            // Played drums: a hint only, the 2026-10-18 probe could not measure most live songs (DrumWobble).
            string hint = detection.LiveDrums is { } live
                ? " The drums push and drag " + live.WobbleMs.ToString("0.0", Invariant) + " ms from beat to beat (" +
                  live.Beats.ToString(Invariant) + " beats measured): played live? For a red line on every drum hit, choose Drift 1 beat and Auto-time again."
                : "";
            // The beat model's red lines were chosen over the analyzer's for this song (docs/PHASE4-STATUS.md N4).
            if (detection.BeatModelUsed) hint = " Timed by the beat model trained on ranked maps (the analyzer was less sure here); listen to the result." + hint;
            // What the detection moved after the analysis: the first line onto a bar start, the lines onto the waveform's edge.
            hint = detection.Note + hint;
            if (oneLineAsSingle && found.Sections.Count == 1)
            {
                var line = found.Sections[0];
                // Status 16 and up: the original detector itself only had an approximate BPM (Billie Jean).
                Proposal = new AutoTimingProposal(OsuPrecision.Bpm(line.Bpm), line.Time, detection.Status >= 16, detection.Status);
                ProposalOffsetText = OsuPrecision.FormatTime(line.Time);
                Status = Proposal.Description + (hint.Length > 0 ? "." + hint : "");
                return;
            }
            MultiProposal = arrange is null ? found : arrange(found);
            Status = MultiProposal.Summary + note + hint;
        }
        catch (Exception ex) { Status = "Auto-time failed: " + ex.Message; }
        finally { IsBusy = false; }
    }

    /// <summary>Replaces every red line with the multi-BPM proposal in one undo step. Green lines are untouched.</summary>
    public void ApplyMultiProposal(bool confirmed) => Run(() =>
    {
        if (!confirmed || MultiProposal is not { } pending) return;
        CancelDrag(); Edits.Replace(InheritSamples(pending.Sections)); Select(0);
        Status = "Applied " + pending.Sections.Count.ToString(Invariant) + (pending.Sections.Count == 1 ? " red line." : " red lines.") +
            " Undo restores the previous red lines.";
    });

    /// <summary>
    /// Proposed red lines take the sample set, custom index, volume and kiai in effect at their time (the red or green
    /// line before them, as osu!'s editor does for a new line), so replacing hundreds of red lines does not reset the
    /// hitsound volume to 100 % on every one.
    /// </summary>
    private TimingPoint[] InheritSamples(IEnumerable<TimingPoint> lines)
    {
        var current = Edits.RedLines.Concat(Greens).OrderBy(p => p.SerializedTime).ThenBy(p => !p.Uninherited).ToArray();
        return lines.Select(line => (current.LastOrDefault(p => p.SerializedTime <= line.Time) ?? current.FirstOrDefault()) is { } source
            ? line with
            {
                SampleSet = source.SampleSet, SampleIndex = source.SampleIndex, Volume = source.Volume,
                Effects = (line.Effects & ~TimingPoint.Kiai) | (source.Effects & TimingPoint.Kiai)
            }
            : line).ToArray();
    }
    private ScrollNormalization.Result? normalization;
    private double NormalizationEnd => Audio?.DurationMs ?? Document?.LastObjectTime ?? DurationMs;
    private IEnumerable<TimingPoint> Greens => NormalizedTiming().Points.Where(p => !p.Uninherited).OrderBy(p => p.Time);
    public double MainBpm => NormalizedTiming().MainBpm;
    public string NormalizationLabel => AutoNormalizeSv ? "Main BPM " + OsuPrecision.FormatBpm(MainBpm) + " · auto-normalized SV" :
        "Manual SV · green values stay fixed when red lines change";

    /// <summary>
    /// Puts a bar line on the selected red line and counts bars of its meter from there to the end: each later red line
    /// that does not start a bar omits its bar line (<see cref="BarLines"/>). For a red line on every beat. One undo step.
    /// </summary>
    public void StartBarHere() => Run(() =>
    {
        CancelDrag();
        if (selectedRow is not { } row) throw new InvalidOperationException("Select the red line that starts a bar, then Bar line here.");
        var result = BarLines.FromDownbeat(Edits.RedLines, row.Index);
        Edits.Replace(result.Lines);
        SetSelection([row.Index], row.Index);
        int after = Edits.RedLines.Count - row.Index - 1;
        Status = "Bar line at " + TimeFormat.Osu(row.Point.Time) + ", then every " + row.Point.Meter.ToString(Invariant) + " beats: " +
            result.Omitted.ToString(Invariant) + " of the " + after.ToString(Invariant) + " red lines after it omit their bar line" +
            (result.OffBar > 0 ? "; " + result.OffBar.ToString(Invariant) + (result.OffBar == 1 ? " of them is" : " of them are") +
                " longer than a bar, so osu! draws a bar line inside off the bar" : "") + ". Ctrl+Z undoes.";
    });

    public string ClipboardText() => TimingClipboard.Write(Edits.RedLines.Concat(Greens));

    /// <summary>Copy for a calibrated osu! setup. Normalize before shifting, so the main BPM uses original song times.</summary>
    public string OsuClipboardText() => OsuTimingExport.Write(NormalizedTiming().Points);

    private ScrollNormalization.Result NormalizedTiming() => normalization ??= AutoNormalizeSv
        ? LiveNormalization.Build(Edits.RedLines, Edits.GreenLines, NormalizationEnd)
        : new(Edits.RedLines.Concat(Edits.GreenLines).ToArray(), Edits.RedLines.Count == 0 ? 120 :
            60000 / ScrollNormalization.MainBeatLength(Edits.RedLines, NormalizationEnd), null, 0, 0, 0);

    /// <summary>
    /// Copy the visible red and green lines, normalized to the BPM held longest over the whole audio.
    /// </summary>
    public (string Text, string Summary) NormalizedClipboardText()
    {
        double last = NormalizationEnd;
        string until = "the end of the song at ";
        var result = NormalizedTiming();
        int greens = result.Points.Count(p => !p.Uninherited);
        string summary = "Copied " + Edits.RedLines.Count.ToString(Invariant) + " red and " + greens.ToString(Invariant) +
            " green lines with the scroll speed normalised to " + OsuPrecision.FormatBpm(result.MainBpm) +
            " BPM (held longest up to " + until + TimeFormat.Osu(last) + "): " +
            result.Added.ToString(Invariant) + " added on red lines, " + result.Scaled.ToString(Invariant) + " existing scaled" +
            (result.Reference is { } r ? "; the old green lines were normalised to " + OsuPrecision.FormatBpm(r) + " BPM, so only their own SV is kept" : "") +
            (result.Clamped > 0 ? "; " + result.Clamped.ToString(Invariant) + " SVs held at the 0.01-10x limit" : "") +
            ". The osu! clipboard subtracts 33 ms; pasting here keeps original times.";
        return (TimingClipboard.Write(result.Points), summary);
    }
    public void Save(string path) => Run(() =>
    {
        CancelDrag();
        var doc = Document ?? (AudioPath is not null ? OsuFile.Create(Path.GetRelativePath(
            Path.GetDirectoryName(Path.GetFullPath(path))!, AudioPath)) : throw new InvalidOperationException("Open audio or a beatmap first."));
        doc.SaveTimingPoints(path, NormalizedTiming().Points, AudioPath is null ? null : Path.GetRelativePath(
            Path.GetDirectoryName(Path.GetFullPath(path))!, AudioPath));
        Document = OsuFile.Load(path); BeatmapPath = Path.GetFullPath(path);
        Status = "Saved " + Path.GetFileName(path) + ". Existing files are backed up as .osu.bak.";
        NotifyView();
    });
    private void TimingChanged()
    {
        normalization = null;
        if (marks.Count > 0) Marks = [];
        followedSection = -1;
        Run(() => playback?.SetTiming(Edits.RedLines));
        // Keep the selected lines that still exist; otherwise fall back to the first red line.
        var reds = Edits.RedLines.ToList();
        int Find(TimingPoint p) => reds.FindIndex(r => ReferenceEquals(r, p));
        int[] kept = selectionPoints.Select(Find).Where(i => i >= 0).ToArray();
        int primary = selectedRow is null ? -1 : Find(selectedRow.Point);
        if (kept.Length == 0) { kept = [0]; primary = 0; }
        SetSelection(kept, primary);
        NotifyView();
        OnPropertyChanged(nameof(DisplayRedLines));
    }
    private void NotifyView()
    {
        normalization = null;
        OnPropertyChanged(nameof(Edits)); OnPropertyChanged(nameof(Audio)); OnPropertyChanged(nameof(Document));
        OnPropertyChanged(nameof(HasAudio));
        OnPropertyChanged(nameof(SongTitle)); OnPropertyChanged(nameof(WindowTitle));
        OnPropertyChanged(nameof(DurationMs)); OnPropertyChanged(nameof(ScrollMaximum));
        OnPropertyChanged(nameof(StartMs)); OnPropertyChanged(nameof(ManualStartMs));
        OnPropertyChanged(nameof(SpanMs)); OnPropertyChanged(nameof(PositionLabel));
        OnPropertyChanged(nameof(TimingRows)); OnPropertyChanged(nameof(InheritedRows)); OnPropertyChanged(nameof(AllRows));
        OnPropertyChanged(nameof(TableRows));
        OnPropertyChanged(nameof(MainBpm)); OnPropertyChanged(nameof(NormalizationLabel));
        OnPropertyChanged(nameof(AutoNormalizeSv));
    }
    public void ReportError(string message) => Status = message;
    public void Run(Action action)
    {
        if (IsBusy || disposed) return;
        try { action(); } catch (Exception ex) { Status = ex.Message; }
    }
    public void Dispose() { disposed = true; lifetime.Cancel(); playback?.Dispose(); playback = null; Edits.Changed -= TimingChanged; }
}
