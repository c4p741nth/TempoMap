using System.Globalization;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace TimingAnalyzer.Editor;

public sealed record AudioContent(double[] Mono, int Rate, WaveformPeaks Peaks)
{
    public double DurationMs => Mono.Length * 1000.0 / Rate;
    public static AudioContent Create(double[] mono, int rate) => new(mono, rate,
        new WaveformPeaks(mono.Select(v => (float)v).ToArray()));
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
    private bool updatingSelection;
    private SnapOption snap = new(4);
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
    public AutoTimingProposal? Proposal { get => proposal; private set { SetProperty(ref proposal, value); OnPropertyChanged(nameof(ProposalDescription)); OnPropertyChanged(nameof(HasProposal)); OnPropertyChanged(nameof(HasSingleProposal)); } }
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
    public string LockedBpmText { get => lockedBpmText; set => SetProperty(ref lockedBpmText, value ?? ""); }
    /// <summary>Drift mode choices: beats per red line (4 = one bar in 4/4; 1 = a red line on every drum hit).</summary>
    /// <summary>
    /// Auto-time modes beside the one Auto-time button: Normal (multi-BPM, which gives the original single red line when
    /// the song keeps one tempo) or drift with N beats per red line (1 = a red line on every drum hit).
    /// </summary>
    public IReadOnlyList<string> AutoModes { get; } = [NormalMode, "Drift 1 beat", "Drift 4 beats", "Drift 8 beats", "Drift 16 beats", "Drift 32 beats"];
    public const string NormalMode = "Normal";
    private string autoMode = NormalMode;
    public string SelectedAutoMode { get => autoMode; set => SetProperty(ref autoMode, AutoModes.Contains(value) ? value : autoMode); }
    /// <summary>Beats per drift red line; 0 in Normal mode.</summary>
    public int DriftBeatsPerLine => autoMode == NormalMode ? 0 : int.Parse(autoMode.Split(' ')[1], Invariant);
    public IReadOnlyList<TimingPoint> DisplayRedLines => preview ?? Edits.RedLines;
    public bool IsDragging => drag is not null;
    public TimingEdits Edits { get; private set; } = new([]);
    public OsuFile? Document { get; private set; }
    public string? BeatmapPath { get; private set; }
    public string? AudioPath { get; private set; }
    public AudioContent? Audio { get; private set; }
    public bool IsBusy { get => busy; private set { SetProperty(ref busy, value); OnPropertyChanged(nameof(CanInteract)); } }
    public bool CanInteract => !IsBusy;
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
    public IReadOnlyList<TimingRow> TimingRows => Edits.RedLines.Select((p, i) => new TimingRow(i, p)).ToArray();
    /// <summary>The "Inherited" tab: the map's green lines, read-only here (Copy normalized rewrites their SV).</summary>
    public IReadOnlyList<TimingRow> InheritedRows => Greens.OrderBy(p => p.Time).Select(p => new TimingRow(-1, p)).ToArray();
    /// <summary>The "All" tab: red and green lines in time order, a green line after the red line at its time.</summary>
    public IReadOnlyList<TimingRow> AllRows => TimingRows.Concat(InheritedRows)
        .OrderBy(r => r.Point.SerializedTime).ThenBy(r => r.IsGreen).ToArray();
    /// <summary>The timing table's tabs, as osu!'s Timing and Control Points window.</summary>
    public IReadOnlyList<string> TableTabs { get; } = ["All", "Timing", "Inherited"];
    private int tableTab;
    public int TableTab { get => tableTab; set { if (SetProperty(ref tableTab, Math.Clamp(value, 0, 2))) OnPropertyChanged(nameof(TableRows)); } }
    public IReadOnlyList<TimingRow> TableRows => tableTab switch { 1 => TimingRows, 2 => InheritedRows, _ => AllRows };
    /// <summary>Selects red lines picked in the timing table (Ctrl / Shift click) without moving the playhead.</summary>
    public void SelectRows(IEnumerable<int> redIndices, int primary) => SetSelection(redIndices, primary);
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
    /// <summary>Moves the playhead to a time and, when paused, scrolls it to one third of the view.</summary>
    public void JumpTo(double timeMs) => Run(() =>
    {
        Seek(timeMs);
        if (!playing) StartMs = FollowViewport.Start(PositionMs, SpanMs, DurationMs);
    });
    /// <summary>The loaded map's "Artist - Title [Version] (Creator)", or the audio file name.</summary>
    public string SongTitle => Document?.SongTitle ?? (AudioPath is not null ? Path.GetFileName(AudioPath) :
        BeatmapPath is not null ? Path.GetFileName(BeatmapPath) : "No song loaded");
    public string WindowTitle => Document is null && AudioPath is null ? AppInfo.Title : SongTitle + " - " + AppInfo.ShortTitle;
    public IReadOnlyList<int> SelectedIndices => selection;
    public string SelectionLabel => selection.Length switch
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
    public string TimeText { get => timeText; set => SetProperty(ref timeText, value); }
    public string BpmText { get => bpmText; set => SetProperty(ref bpmText, value); }
    public string MeterText { get => meterText; set => SetProperty(ref meterText, value); }
    /// <summary>The settings to remember (the window adds its size).</summary>
    public EditorSettings CaptureSettings(EditorSettings? window = null) => (window ?? new EditorSettings()) with
    {
        MusicVolumePercent = MusicVolumePercent, ClickVolumePercent = ClickVolumePercent, ClickEnabled = ClickEnabled,
        FollowPlayhead = FollowPlayhead, Speed = SelectedSpeed, SnapDivisor = SnapDivisor, AutoMode = SelectedAutoMode,
        TableTab = TableTab,
    };
    /// <summary>Applies remembered settings through the same setters as the window, so out-of-range values are ignored.</summary>
    public void ApplySettings(EditorSettings s)
    {
        MusicVolumePercent = s.MusicVolumePercent; ClickVolumePercent = s.ClickVolumePercent;
        ClickEnabled = s.ClickEnabled; FollowPlayhead = s.FollowPlayhead;
        if (Speeds.Contains(s.Speed)) SelectedSpeed = s.Speed;
        if (SnapOptions.FirstOrDefault(o => o.Divisor == s.SnapDivisor) is { } snapOption) SelectedSnap = snapOption;
        SelectedAutoMode = s.AutoMode; TableTab = s.TableTab;
    }
    public IReadOnlyList<string> Speeds { get; } = ["0.5", "0.75", "1.0"];
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
    public IRelayCommand TogglePlayCommand { get; }
    public IRelayCommand ApplyFieldsCommand { get; }
    public IRelayCommand InsertCommand { get; }
    public IRelayCommand DeleteCommand { get; }
    public IRelayCommand UndoCommand { get; }
    public IRelayCommand RedoCommand { get; }
    public IRelayCommand BpmFromSpacingCommand { get; }
    public IRelayCommand StartBarHereCommand { get; }
    public IRelayCommand MoveToPlayheadCommand { get; }
    public EditorViewModel(Func<string, Task<AudioContent>> loadAudio, Func<string, IPlaybackEngine> createPlayback,
        Func<double>? wallMilliseconds = null)
    {
        this.loadAudio = loadAudio; this.createPlayback = createPlayback;
        this.wallMilliseconds = wallMilliseconds ?? (() => Stopwatch.GetTimestamp() * 1000.0 / Stopwatch.Frequency);
        TogglePlayCommand = new RelayCommand(TogglePlay);
        ApplyFieldsCommand = new RelayCommand(ApplyFields);
        InsertCommand = new RelayCommand(Insert);
        DeleteCommand = new RelayCommand(Delete);
        UndoCommand = new RelayCommand(Undo);
        RedoCommand = new RelayCommand(Redo);
        BpmFromSpacingCommand = new RelayCommand(BpmFromSpacing);
        StartBarHereCommand = new RelayCommand(StartBarHere);
        MoveToPlayheadCommand = new RelayCommand(MoveToPlayhead);
        ShiftBeatsCommand = new RelayCommand<string>(value => Run(() => Shift(false, double.Parse(value!, Invariant))));
        ShiftMsCommand = new RelayCommand<string>(value => Run(() => Shift(true, double.Parse(value!, Invariant))));
        Edits.Changed += TimingChanged;
    }

    public async Task LoadAsync(string path)
    {
        if (IsBusy || disposed) return;
        IsBusy = true;
        try
        {
            var doc = Path.GetExtension(path).Equals(".osu", StringComparison.OrdinalIgnoreCase) ? OsuFile.Load(path) : null;
            string? song = doc is null ? path : doc.AudioFilename is null ? null :
                Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, doc.AudioFilename));
            playback?.Dispose(); playback = null;
            CancelDrag();
            Edits.Changed -= TimingChanged;
            Edits = new(doc?.RedLines ?? [new TimingPoint(0, 500)]);
            Edits.Changed += TimingChanged;
            Document = doc; BeatmapPath = doc is null ? null : Path.GetFullPath(path); AudioPath = song; Audio = null;
            Proposal = null; MultiProposal = null; ProposalOffsetText = "";
            PositionMs = 0; start = 0; span = 10000;
            playing = false; followSuspended = false; nextEngineReadMs = 0;
            playheadClock.Synchronize(0, wallMilliseconds(), 1, false, snap: true);
            TimingChanged(); NotifyView();
            if (song is null || !File.Exists(song)) { Status = "Timing loaded; audio file is missing. Open the map beside its AudioFilename." + DuplicateNote(doc); return; }
            Status = "Loading audio...";
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
                Status = "Loaded " + Path.GetFileName(path) + DuplicateNote(doc);
            }
            catch (Exception ex)
            {
                playback?.Dispose(); playback = null;
                Status = "Waveform loaded; playback unavailable: " + ex.Message;
            }
        }
        catch (Exception ex) { Status = "Open failed: " + ex.Message; }
        finally { IsBusy = false; }
    }
    private static string DuplicateNote(OsuFile? doc) => doc is { DuplicateRedLines: > 0 and var n }
        ? string.Create(Invariant, $" ({n} duplicate red line{(n == 1 ? "" : "s")} at the same time ignored, as osu! does)") : "";
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
        if (playback is null) return;
        double now = wallMilliseconds();
        if (now >= nextEngineReadMs) ReadPlayback(now);
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
        if (!playing || !FollowPlayhead || IsDragging) return;
        var reds = Edits.RedLines;
        int section = reds.Count == 0 ? -1 : 0;
        for (int i = 1; i < reds.Count && reds[i].Time <= PositionMs; i++) section = i;
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
    public void Select(int index) => SetSelection([index], index);
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
    /// <summary>Esc cancels a drag first, then clears the selection.</summary>
    public void Escape() { if (IsDragging) CancelDrag(); else ClearSelection(); }
    private void SetSelection(IEnumerable<int> indices, int primary)
    {
        var reds = Edits.RedLines;
        selection = indices.Where(i => i >= 0 && i < reds.Count).Distinct().Order().ToArray();
        if (!selection.Contains(primary)) primary = selection.Length > 0 ? selection[0] : -1;
        selectionPoints = selection.Select(i => reds[i]).ToArray();
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
        if (row is not null)
        {
            TimeText = OsuPrecision.FormatTime(row.Point.Time);
            BpmText = OsuPrecision.FormatBpm(row.Point.Bpm);
            MeterText = row.Point.Meter.ToString(Invariant);
        }
        OnPropertyChanged(nameof(SelectedIndices)); OnPropertyChanged(nameof(SelectionLabel));
    }
    public void ApplyFields() => Run(() =>
    {
        if (SelectedRow is null) return;
        double time = double.Parse(TimeText, NumberStyles.Float, Invariant);
        double bpm = double.Parse(BpmText, NumberStyles.Float, Invariant);
        int meter = int.Parse(MeterText, NumberStyles.Integer, Invariant);
        if (!double.IsFinite(bpm) || bpm <= 0) throw new ArgumentException("BPM must be positive.");
        var updated = SelectedRow.Point with { Time = OsuPrecision.Time(time), BeatLength = 60000 / OsuPrecision.Bpm(bpm), Meter = meter };
        Edits.Update(SelectedRow.Index, updated);
        Select(Edits.RedLines.ToList().IndexOf(updated));
        Status = "Red line updated.";
    });
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
        Select(Edits.RedLines.ToList().FindIndex(p => p.Time == time));
        Status = "Red line added at " + OsuPrecision.FormatTime(time) + " ms.";
    });
    public void Delete() => Run(() =>
    {
        CancelDrag();
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
            chosen.Length.ToString(Invariant) + " selected red lines") + " by " +
            amount.ToString("0.##", Invariant) + (milliseconds ? " ms." : " beats.") + " Green lines stay fixed.";
    }
    /// <summary>
    /// Moves the selected red lines by the distance from the line clicked last to the playhead, so that line lands on
    /// the playhead (put the playhead on the hit by ear or with ←/→, pick the line in the table, then this). One undo step.
    /// </summary>
    public void MoveToPlayhead() => Run(() =>
    {
        CancelDrag();
        if (selectedRow is not { } row) throw new InvalidOperationException("Select the red line to move in the timing table first.");
        double delta = OsuPrecision.Time(PositionMs) - row.Point.Time;
        if (delta == 0) { Status = "The red line is already on the playhead."; return; }
        int[] chosen = selection;
        Edits.ShiftRedLinesMs(RedLineScope.Selected, delta, chosen);
        SetSelection(chosen, row.Index);
        Status = (chosen.Length == 1 ? "Red line" : chosen.Length.ToString(Invariant) + " red lines") + " moved by " +
            delta.ToString("+0;-0", Invariant) + " ms onto the playhead at " + TimeFormat.Osu(PositionMs) + ". Ctrl+Z undoes.";
    });
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
    public TimingPoint? ProposalPoint() => Proposal?.AtOffset(ProposalOffsetText);
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
    /// as a list. A drift mode: drift lines. Nothing changes until Apply.
    /// </summary>
    public async Task AutoTimeAsync(Func<AudioContent, TimingRequest, Task<TimingDetection>> single,
        Func<AudioContent, Task<MultiTimingDetection>> multi, Func<AudioContent, int, Task<MultiTimingDetection>> drift)
    {
        if (LockedBpmText.Trim().Length > 0) await AnalyzeAsync(single);
        else if (DriftBeatsPerLine > 0) await AnalyzeDriftAsync(drift);
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
    private IEnumerable<TimingPoint> Greens => Document?.TimingPoints.Where(p => !p.Uninherited) ?? [];

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

    /// <summary>
    /// Red and green lines with the scroll speed normalised to osu!'s main BPM (<see cref="ScrollNormalization"/>),
    /// measured up to the last note, else the end of the audio. The edited timing itself is not changed.
    /// </summary>
    public (string Text, string Summary) NormalizedClipboardText()
    {
        double last = Document?.LastObjectTime ?? Audio?.DurationMs ?? Edits.RedLines[^1].Time;
        string until = Document?.LastObjectTime is not null ? "the last note at " : Audio is not null ? "the end of the audio at " : "the last red line at ";
        var result = ScrollNormalization.Normalize(Edits.RedLines, Greens.ToList(), Document?.RedLines ?? [], last);
        int greens = result.Points.Count(p => !p.Uninherited);
        string summary = "Copied " + Edits.RedLines.Count.ToString(Invariant) + " red and " + greens.ToString(Invariant) +
            " green lines with the scroll speed normalised to " + OsuPrecision.FormatBpm(result.MainBpm) +
            " BPM (osu!'s main BPM: held longest up to " + until + TimeFormat.Osu(last) + "): " +
            result.Added.ToString(Invariant) + " added on red lines, " + result.Scaled.ToString(Invariant) + " existing scaled" +
            (result.Reference is { } r ? "; the old green lines were normalised to " + OsuPrecision.FormatBpm(r) + " BPM, so only their own SV is kept" : "") +
            (result.Clamped > 0 ? "; " + result.Clamped.ToString(Invariant) + " SVs held at the 0.01-10x limit" : "") +
            ". Paste into the osu! editor (no [TimingPoints] header); the timing here is unchanged.";
        return (TimingClipboard.Write(result.Points), summary);
    }
    public void Save(string path) => Run(() =>
    {
        CancelDrag();
        var doc = Document ?? (AudioPath is not null ? OsuFile.Create(Path.GetRelativePath(
            Path.GetDirectoryName(Path.GetFullPath(path))!, AudioPath)) : throw new InvalidOperationException("Open audio or a beatmap first."));
        doc.Save(path, Edits.RedLines);
        Document = OsuFile.Load(path); BeatmapPath = Path.GetFullPath(path);
        Status = "Saved " + Path.GetFileName(path) + ". Existing files are backed up as .osu.bak.";
        NotifyView();
    });
    private void TimingChanged()
    {
        followedSection = -1;
        Run(() => playback?.SetTiming(Edits.RedLines));
        // Keep the selected lines that still exist; otherwise fall back to the first red line.
        var reds = Edits.RedLines.ToList();
        int[] kept = selectionPoints.Select(p => reds.IndexOf(p)).Where(i => i >= 0).ToArray();
        int primary = selectedRow is null ? -1 : reds.IndexOf(selectedRow.Point);
        if (kept.Length == 0) { kept = [0]; primary = 0; }
        SetSelection(kept, primary);
        NotifyView();
        OnPropertyChanged(nameof(DisplayRedLines));
    }
    private void NotifyView()
    {
        OnPropertyChanged(nameof(Edits)); OnPropertyChanged(nameof(Audio)); OnPropertyChanged(nameof(Document));
        OnPropertyChanged(nameof(SongTitle)); OnPropertyChanged(nameof(WindowTitle));
        OnPropertyChanged(nameof(DurationMs)); OnPropertyChanged(nameof(ScrollMaximum));
        OnPropertyChanged(nameof(StartMs)); OnPropertyChanged(nameof(ManualStartMs));
        OnPropertyChanged(nameof(SpanMs)); OnPropertyChanged(nameof(PositionLabel));
        OnPropertyChanged(nameof(TimingRows)); OnPropertyChanged(nameof(InheritedRows)); OnPropertyChanged(nameof(AllRows));
        OnPropertyChanged(nameof(TableRows));
    }
    public void ReportError(string message) => Status = message;
    public void Run(Action action)
    {
        if (IsBusy || disposed) return;
        try { action(); } catch (Exception ex) { Status = ex.Message; }
    }
    public void Dispose() { disposed = true; playback?.Dispose(); playback = null; Edits.Changed -= TimingChanged; }
}
