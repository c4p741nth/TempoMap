using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia;
using Avalonia.VisualTree;
using TimingAnalyzer.Audio;
using TimingAnalyzer.Editor;
using TimingAnalyzer.Core;
using System.Globalization;

namespace TimingAnalyzer.App;

public partial class MainWindow : Window
{
    private readonly EditorViewModel model;
    private bool closed;
    /// <summary>Where settings are kept; a test harness points it elsewhere before opening a window.</summary>
    public static string SettingsPath { get; set; } = EditorSettings.DefaultPath;
    private Size normalSize = new(1280, 720);
    public MainWindow()
    {
        InitializeComponent();
        model = new EditorViewModel(path => Task.Run(() =>
        {
            var decoded = AudioDecoder.Read(path);
            return AudioContent.Create(decoded.Mono, checked((int)decoded.Rate));
        }), path => new PlaybackEngine(path), prepareAudio: new AudioImport().PrepareAsync);
        DataContext = model;
        ApplySettings(EditorSettings.Load(SettingsPath));
        // The size to remember is the last one the window had before it was maximised.
        SizeChanged += (_, e) => { if (WindowState == WindowState.Normal) normalSize = e.NewSize; };
        Closing += (_, _) => SaveSettings();
        // A key released while another window had the focus never reaches this one.
        Deactivated += (_, _) => model.ArrowUp();
        // Avalonia 12 TopLevel.RequestAnimationFrame(Action<TimeSpan>), verified in cached XML.
        Opened += (_, _) => RequestAnimationFrame(UpdateFrame);
        Closed += (_, _) => { closed = true; model.Dispose(); };
        AddHandler(KeyDownEvent, HandleKey, RoutingStrategies.Bubble);
        // Tunnel, so Space and ←/→ reach the transport before a focused button, slider, combo box or list uses
        // them: otherwise Space would press the last clicked button again instead of playing.
        AddHandler(KeyDownEvent, HandleTransportKey, RoutingStrategies.Tunnel);
        AddHandler(KeyUpEvent, SwallowSpaceUp, RoutingStrategies.Tunnel);
        // Save before a clicked row changes the selection, including controls that do not take keyboard focus.
        AddHandler(PointerPressedEvent, SavePointBeforeClick, RoutingStrategies.Tunnel);
        // A .osu or an audio file dropped anywhere on the window opens it, as File → Open does.
        AddHandler(DragDrop.DragEnterEvent, DragOverFiles);
        AddHandler(DragDrop.DragOverEvent, DragOverFiles);
        AddHandler(DragDrop.DropEvent, DropFiles);
        // After every layout pass, not on SizeChanged: there the slider and status heights could still be the old ones
        // (at 900x500 the toolbars got 117 px instead of 67, leaving the waveform 110 px).
        LayoutUpdated += (_, _) => FitPanels();
        // A new proposal shows its apply buttons even when the toolbars are scrolled or taller than their room.
        model.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(EditorViewModel.SelectedIndices))
            {
                // A selection made elsewhere (waveform box, Esc, follow) replaces the table's green rows too.
                QueueTableSync();
            }
            else if (e.PropertyName == nameof(EditorViewModel.TableRows)) QueueTableSync();
            if (e.PropertyName == nameof(EditorViewModel.HasProposal) && model.HasProposal)
                Avalonia.Threading.Dispatcher.UIThread.Post(() => ApplyRow.BringIntoView(), Avalonia.Threading.DispatcherPriority.Background);
        };
    }
    // Both kinds of point can be selected and edited; tempo tools still use the red selection.
    private bool syncingTable, syncQueued, savingPointFields;
    private static readonly DataFormat<string> InternalTimingFormat = DataFormat.CreateStringApplicationFormat("Tempomap.TimingPoints.v1");
    private void TableSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (syncingTable || savingPointFields) return;
        var rows = TimingTable.SelectedItems?.OfType<TimingRow>().ToList() ?? [];
        var primary = e.AddedItems.OfType<TimingRow>().LastOrDefault() ?? rows.LastOrDefault();
        syncingTable = true;
        try { model.SelectTableRows(rows, primary); }
        finally { syncingTable = false; }
        // A click only selects; a double-click jumps there (TableDoubleTapped).
    }
    /// <summary>A double-click on a row jumps there.</summary>
    private void TableDoubleTapped(object? sender, TappedEventArgs e)
    {
        if ((e.Source as Visual)?.GetSelfAndVisualAncestors().OfType<ListBoxItem>().FirstOrDefault()?.DataContext is TimingRow row)
            model.JumpTo(row.Point.Time);
    }
    private void SelectAllRows(object? sender, RoutedEventArgs e) => TimingTable.SelectAll();
    private void AddPointForTab(object? sender, RoutedEventArgs e)
    {
        if (model.TableTab == 2) AddInheritedPoint(sender, e);
        else AddTimingPoint(sender, e);
    }
    private void AddTimingPoint(object? sender, RoutedEventArgs e) { if (SavePointFields()) { model.Insert(); FocusPointValue(); } }
    private void AddInheritedPoint(object? sender, RoutedEventArgs e) { if (SavePointFields()) { model.InsertInherited(); FocusPointValue(); } }
    private void FocusPointValue() => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
    {
        PointValueBox.BringIntoView(); PointValueBox.Focus(); PointValueBox.SelectAll();
    }, Avalonia.Threading.DispatcherPriority.Background);
    private void PointFieldFocused(object? sender, FocusChangedEventArgs e) => model.IsEditingPoint = true;
    private void PointFieldBlurred(object? sender, RoutedEventArgs e)
    {
        if (!SavePointFields()) model.RestorePointFields();
        model.IsEditingPoint = PointTimeBox.IsFocused || PointValueBox.IsFocused || PointMeterBox.IsFocused;
    }
    private bool SavePointFields()
    {
        if (savingPointFields) return true;
        savingPointFields = true;
        try { return model.TryApplyFields(); }
        finally { savingPointFields = false; }
    }
    private void SavePointBeforeClick(object? sender, PointerPressedEventArgs e)
    {
        if (!model.IsEditingPoint) return;
        var focused = PointTimeBox.IsFocused ? PointTimeBox : PointValueBox.IsFocused ? PointValueBox : PointMeterBox.IsFocused ? PointMeterBox : null;
        if (focused is null || (e.Source as Visual)?.GetSelfAndVisualAncestors().Contains(focused) == true) return;
        var clicked = (e.Source as Visual)?.GetSelfAndVisualAncestors().OfType<ListBoxItem>().FirstOrDefault()?.DataContext as TimingRow;
        if (!SavePointFields()) model.RestorePointFields();
        if (clicked is not null && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            // Saving Time can reorder/rebuild the list under this pointer event. Resolve the actual clicked point
            // again instead of letting the detached ListBoxItem select its old index.
            var rows = model.TableRows;
            var target = rows.FirstOrDefault(r => ReferenceEquals(r.Point, clicked.Point)) ?? rows.FirstOrDefault(r => r.Point == clicked.Point)
                ?? rows.FirstOrDefault(r => r.IsRed == clicked.IsRed && (r.IsRed ? r.RedIndex == model.SelectedRow?.Index : model.SelectedGreenIndices.Contains(r.GreenIndex)));
            if (target is not null)
            {
                var chosen = rows.Where(r => r.IsRed ? model.SelectedIndices.Contains(r.RedIndex) : model.SelectedGreenIndices.Contains(r.GreenIndex)).ToList();
                if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
                {
                    int anchor = rows.ToList().FindIndex(r => r.IsRed ? r.RedIndex == model.SelectedRow?.Index : model.SelectedGreenIndices.Contains(r.GreenIndex));
                    int end = rows.ToList().IndexOf(target);
                    if (anchor < 0) anchor = end;
                    chosen = rows.Skip(Math.Min(anchor, end)).Take(Math.Abs(end - anchor) + 1).ToList();
                }
                else if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
                {
                    if (!chosen.Remove(target)) chosen.Add(target);
                }
                else chosen = [target];
                model.SelectTableRows(chosen, chosen.Contains(target) ? target : chosen.LastOrDefault());
                SyncTable();
            }
            TimingTable.Focus();
            e.Handled = true;
            return;
        }
        // A click on the waveform or a label may leave keyboard focus in the field.
        if ((e.Source as Visual)?.GetSelfAndVisualAncestors().Contains(PointFields) != true) TimingTable.Focus();
    }
    private void PointFieldKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers != KeyModifiers.None || e.Key is not (Key.Enter or Key.Escape)) return;
        if (e.Key == Key.Enter && !SavePointFields()) { e.Handled = true; return; }
        if (e.Key == Key.Escape) model.RestorePointFields();
        TimingTable.Focus();
        e.Handled = true;
    }
    private void ExitApp(object? sender, RoutedEventArgs e) => Close();
    /// <summary>
    /// Esc closes a dialog as its cancel/close button does. On the tunnel, so a focused button or text does not take the
    /// key first; <paramref name="result"/> is what ShowDialog returns (false for the confirm dialog's Cancel).
    /// </summary>
    internal static void CloseOnEscape(Window dialog, object? result = null) =>
        dialog.AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key != Key.Escape || e.KeyModifiers != KeyModifiers.None) return;
            e.Handled = true;
            dialog.Close(result);
        }, RoutingStrategies.Tunnel);
    private async void ShowKeys(object? sender, RoutedEventArgs e)
    {
        var close = new Button { Content = "Close" };
        var dialog = new Window { Title = "Keys and colours", Width = 560, SizeToContent = SizeToContent.Height,
            CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner, Icon = Icon };
        var panel = new StackPanel { Margin = new Thickness(20), Spacing = 10 };
        panel.Children.Add(new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap, Text =
            "Space: play / pause\n←/→: step the playhead by the beat snap · hold past 0.3 s and it runs on, faster the longer you hold (like Audacity)\n" +
            "Wheel: scroll · Ctrl+wheel: zoom · Click: seek\n" +
            "Drag a red line: move it · Ctrl+drag a beat: stretch the BPM\nDrag an empty area: box-select red lines\n" +
            "Ctrl+P: add a red timing point and type its BPM · Ctrl+G: add a green inherited point and type its SV\n" +
            "Insert / +: add the tab's point type (All/Timing: red; Inherited: green) · Enter / leave field: save · Esc in field: cancel\n" +
            "Delete: delete selected points · Ctrl+Shift+P: move the selected point onto the playhead\n" +
            "Home / End: go to song start / end · Drop a .osu or an audio file on the window: open it\n" +
            "Shift+←/→ or Shift+drag: highlight a range · Ctrl+R: auto-time the range · Esc: clear the range, then the selection\n" +
            "Ctrl+V: paste timing points (Ctrl+Shift+V replaces all red lines)\n" +
            "Ctrl+K: check sections (marks the ones worth a listen) · Ctrl+L: fill a ramp between the selected red lines\n" +
            "Timing table: click selects, double-click jumps, Ctrl+click adds, Shift+click selects a range\n" +
            "Ctrl+A: select every row · Ctrl+C: copy the selected rows · Esc: clear the selection\n" +
            "Ctrl+Z / Ctrl+Y: undo / redo · Ctrl+O: open .osu · Ctrl+Shift+O: open audio · Ctrl+S: save\n" +
            "Ctrl+T: auto-time · Ctrl+Enter: apply the proposal\n" +
            "Ctrl+←/→: shift the selected lines 1 ms (Ctrl+Shift: 5 ms) · Ctrl+↑/↓: tune their BPM by the step in the Shift row (Ctrl+Shift: ten steps)\n" +
            "M: metronome · F: follow · Esc: close this window and the other dialogs\n" +
            "Ctrl+Shift+C: copy all timing points · Ctrl+Shift+N: copy normalized (osu!mania) · F1: this list" });
        panel.Children.Add(new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap, Opacity = 0.8, Text =
            "Waveform: red = red line, purple = bar line, gray = beat, dim = beat-snap tick, orange = selected, yellow = playhead, blue band = range, violet band = section worth a listen." });
        panel.Children.Add(close); dialog.Content = panel;
        close.Click += (_, _) => dialog.Close();
        CloseOnEscape(dialog);
        await dialog.ShowDialog(this);
    }
    private void QueueTableSync()
    {
        if (syncQueued) return;
        syncQueued = true;
        Avalonia.Threading.Dispatcher.UIThread.Post(SyncTable, Avalonia.Threading.DispatcherPriority.Background);
    }
    /// <summary>Shows the model's selection (and the green rows picked here) in the table, scrolled to the current line.</summary>
    private void SyncTable()
    {
        syncQueued = false;
        if (TimingTable.ItemsSource is not IReadOnlyList<TimingRow> items) return;
        var reds = model.SelectedIndices.ToHashSet();
        var greens = model.SelectedGreenIndices.ToHashSet();
        syncingTable = true;
        try
        {
            var selection = TimingTable.Selection;
            selection.BeginBatchUpdate();
            try
            {
                selection.Clear();
                for (int i = 0; i < items.Count; i++)
                    if (items[i].IsRed ? reds.Contains(items[i].RedIndex) : greens.Contains(items[i].GreenIndex)) selection.Select(i);
            }
            finally { selection.EndBatchUpdate(); }
        }
        finally { syncingTable = false; }
        if (model.SelectedGreenIndices.LastOrDefault(-1) is >= 0 and var green)
        {
            for (int i = 0; i < items.Count; i++)
                if (items[i].IsGreen && items[i].GreenIndex == green) { TimingTable.ScrollIntoView(i); break; }
        }
        else if (model.SelectedRow is { } row)
            for (int i = 0; i < items.Count; i++)
                if (items[i].RedIndex == row.Index) { TimingTable.ScrollIntoView(i); break; }
    }
    private async void CopySelected(object? sender, RoutedEventArgs e) => await CopySelectedRows();
    private async Task CopySelectedRows()
    {
        try
        {
            var rows = TimingTable.SelectedItems?.OfType<TimingRow>().OrderBy(r => r.Point.SerializedTime).ThenBy(r => r.IsGreen).ToList() ?? [];
            if (rows.Count == 0) { model.ReportError("Select rows in the timing table first (Ctrl+A selects every row of the tab)."); return; }
            if (Clipboard is null) throw new IOException("Clipboard unavailable.");
            await WriteTimingClipboard(EditorViewModel.RowsClipboardText(rows));
            int red = rows.Count(r => r.IsRed);
            model.ReportError(string.Create(CultureInfo.InvariantCulture, $"Copied {red} red and {rows.Count - red} green rows for osu! (-33 ms). Pasting here keeps their original times."));
        }
        catch (Exception ex) { model.ReportError(ex.Message); }
    }
    /// <summary>Least waveform and red-line list heights; the toolbars and the selected-line panel scroll instead.</summary>
    public const double MinWaveformHeight = 160, MinListHeight = 100;
    private void FitPanels()
    {
        // Written only when it moves, so the layout pass it causes settles instead of looping.
        static void Set(Control c, double value) { if (Math.Abs(c.MaxHeight - value) > 0.5) c.MaxHeight = value; }
        Set(TopScroll, Math.Max(48, MainArea.Bounds.Height - BottomPanel.Bounds.Height - BottomPanel.Margin.Top -
            TopScroll.Margin.Bottom - SeekBar.Bounds.Height - MinWaveformHeight));
        Set(DetailsScroll, Math.Max(48, RightPanel.Bounds.Height - RedLinesTitle.Bounds.Height - RedLinesTitle.Margin.Bottom -
            TableHeader.Bounds.Height - TableHeader.Margin.Bottom - DetailsScroll.Margin.Top - MinListHeight));
    }
    private void ApplySettings(EditorSettings settings)
    {
        model.ApplySettings(settings);
        WindowState = WindowState.Normal;
        Width = Math.Max(MinWidth, settings.WindowWidth); Height = Math.Max(MinHeight, settings.WindowHeight);
        normalSize = new Size(Width, Height);
        if (settings.WindowMaximized) WindowState = WindowState.Maximized;
    }
    private void SaveSettings()
    {
        try
        {
            model.CaptureSettings(new EditorSettings
            {
                WindowWidth = normalSize.Width, WindowHeight = normalSize.Height, WindowMaximized = WindowState == WindowState.Maximized
            }).Save(SettingsPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* settings are a convenience */ }
    }
    private void RestoreDefaults(object? sender, RoutedEventArgs e)
    {
        ApplySettings(new EditorSettings());
        SaveSettings();
        model.ReportError("Settings restored to their defaults (volumes, metronome, follow, speed, beat snap, shift steps, BPM step, auto-time mode and choices, table tab, window size).");
    }
    private static bool InTextBox(KeyEventArgs e) =>
        e.Source is TextBox || e.Source is Visual v && v.GetVisualAncestors().OfType<TextBox>().Any();
    private void HandleTransportKey(object? sender, KeyEventArgs e)
    {
        if (model.IsBusy) return;
        if (e.Key == Key.P && e.KeyModifiers == KeyModifiers.Control)
        { AddTimingPoint(this, new RoutedEventArgs()); e.Handled = true; return; }
        if (e.Key == Key.G && e.KeyModifiers == KeyModifiers.Control)
        { AddInheritedPoint(this, new RoutedEventArgs()); e.Handled = true; return; }
        if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.Control)
        { if (SavePointFields()) ApplyAutoTime(this, new RoutedEventArgs()); e.Handled = true; return; }
        if (e.Key == Key.P && e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift))
        { if (SavePointFields()) model.MoveToPlayhead(); e.Handled = true; return; }
        if (InTextBox(e)) return;
        if (e.Key == Key.Space) { model.TogglePlay(); e.Handled = true; }
        // ←/→: one beat-snap step, and held past 300 ms the playhead runs on, faster the longer (EditorViewModel.ArrowDown);
        // the OS key repeats only tell it the key is still down.
        else if (e.Key is Key.Left or Key.Right && e.KeyModifiers == KeyModifiers.None)
        {
            model.ArrowDown(e.Key == Key.Right ? 1 : -1);
            e.Handled = true;
        }
        // Shift+←/→: the same, highlighting from where the Shift steps began (Auto-time range).
        else if (e.Key is Key.Left or Key.Right && e.KeyModifiers == KeyModifiers.Shift)
        {
            model.ArrowDown(e.Key == Key.Right ? 1 : -1, extend: true);
            e.Handled = true;
        }
        // Ctrl+↑/↓: tune the selected red lines' BPM by the step in the Shift row (Ctrl+Shift: ten steps).
        else if (e.Key is Key.Up or Key.Down && e.KeyModifiers is KeyModifiers.Control or (KeyModifiers.Control | KeyModifiers.Shift))
        {
            model.NudgeBpm(e.Key == Key.Up ? 1 : -1, e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 10 : 1);
            e.Handled = true;
        }
        // Before the table or a button takes them: Ctrl(+Shift)+←/→ shift the selected red lines 1 (5) ms.
        else if (e.Key is Key.Left or Key.Right && e.KeyModifiers is KeyModifiers.Control or (KeyModifiers.Control | KeyModifiers.Shift))
        {
            int ms = (e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 5 : 1) * (e.Key == Key.Right ? 1 : -1);
            model.ShiftMsCommand.Execute(ms.ToString(CultureInfo.InvariantCulture));
            e.Handled = true;
        }
        else if (e.Key == Key.Home && e.KeyModifiers == KeyModifiers.None) { model.JumpTo(0); e.Handled = true; }
        else if (e.Key == Key.End && e.KeyModifiers == KeyModifiers.None) { model.JumpTo(model.DurationMs); e.Handled = true; }
    }
    // A button clicks on Space release; the press was taken by the transport, so the release must not click.
    private void SwallowSpaceUp(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space && !InTextBox(e)) e.Handled = true;
        // The held ←/→ ends with its own key going up, whatever modifiers are down by then.
        else if (e.Key is Key.Left or Key.Right) model.ArrowUp(e.Key == Key.Right ? 1 : -1);
    }
    private void UpdateFrame(TimeSpan timestamp)
    {
        if (closed) return;
        model.UpdateFrame();
        RequestAnimationFrame(UpdateFrame);
    }
    private async void OpenAudio(object? sender, RoutedEventArgs e) => await Open(false);
    private async void OpenOsu(object? sender, RoutedEventArgs e) => await Open(true);
    private async Task Open(bool osu)
    {
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = osu ? "Open osu! beatmap" : "Open audio", AllowMultiple = false,
                FileTypeFilter = [new FilePickerFileType(osu ? "osu! beatmap" : "Audio")
                { Patterns = osu ? ["*.osu"] : [.. EditorViewModel.AudioExtensions.Select(extension => "*" + extension)] }]
            });
            string? path = files.FirstOrDefault()?.TryGetLocalPath();
            if (path is not null) await model.LoadAsync(path);
        }
        catch (Exception ex) { model.ReportError(ex.Message); }
    }
    private static List<string> DroppedPaths(DragEventArgs e) =>
        e.DataTransfer.TryGetFiles()?.Select(file => file.TryGetLocalPath()).OfType<string>().ToList() ?? [];
    private void DragOverFiles(object? sender, DragEventArgs e)
    {
        e.DragEffects = !model.IsBusy && DroppedPaths(e).Any(EditorViewModel.CanOpen) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }
    private async void DropFiles(object? sender, DragEventArgs e)
    {
        e.Handled = true;
        try { await model.OpenDroppedAsync(DroppedPaths(e)); }
        catch (Exception ex) { model.ReportError(ex.Message); }
    }
    private readonly CoreTiming coreTiming = new();
    /// <summary>The analysis with the Settings menu's choices (read here, on the UI thread, before the work moves to a thread).</summary>
    private CoreTiming Timing
    {
        get { coreTiming.AlignToEdge = model.AlignToEdge; coreTiming.StartOnBar = model.StartOnBar; return coreTiming; }
    }
    private Task<TimingDetection> Detect(AudioContent audio, TimingRequest request) { var t = Timing; return Task.Run(() => t.Detect(audio, request)); }
    private async void AutoTime(object? sender, RoutedEventArgs e)
    {
        var t = Timing;
        await model.AutoTimeAsync(Detect, audio => Task.Run(() => t.DetectMulti(audio)),
            (audio, beats) => Task.Run(() => t.DetectDrift(audio, beats)), audio => Task.Run(() => t.DetectBeatModel(audio)));
    }
    private async Task ApplyMulti()
    {
        if (model.MultiProposal is not { } pending) { model.ReportError("Run Auto-time first."); return; }
        // Yes/no only: the lines are listed in the main window. Listing them here made a 553-line drift proposal's
        // dialog 8 600 px tall, with its buttons off the screen.
        var reds = pending.Sections;
        int n = reds.Count;
        string range = n == 1 ? OsuPrecision.FormatBpm(reds[0].Bpm) + " BPM" :
            OsuPrecision.FormatBpm(reds.Min(p => p.Bpm)) + "-" + OsuPrecision.FormatBpm(reds.Max(p => p.Bpm)) + " BPM";
        bool confirmed = await Confirm("Replace all red lines?", "Replace all red lines with the " + n.ToString(CultureInfo.InvariantCulture) +
            (n == 1 ? " proposed red line (" : " proposed red lines (") + range + ")? Green lines are kept, and one undo restores the old timing.");
        model.ApplyMultiProposal(confirmed);
    }
    /// <summary>The one Apply button: the one-line proposal (with its edited offset) or the list of red lines.</summary>
    private async void AutoTimeSection(object? sender, RoutedEventArgs e) => await model.AutoTimeSectionAsync(Detect);
    private async void AutoTimeRange(object? sender, RoutedEventArgs e) =>
        await model.AutoTimeRangeAsync((audio, request) => { var t = Timing; return Task.Run(() => t.DetectRange(audio, request)); });
    private async void PasteRows(object? sender, RoutedEventArgs e) => await PasteFromClipboard(false);
    private async void PasteReplacing(object? sender, RoutedEventArgs e) => await PasteFromClipboard(true);
    private async Task PasteFromClipboard(bool replaceAll)
    {
        try
        {
            if (Clipboard is null) throw new IOException("Clipboard unavailable.");
            string? text = await Clipboard.TryGetValueAsync(InternalTimingFormat) ?? await Clipboard.TryGetTextAsync();
            if (string.IsNullOrWhiteSpace(text)) { model.ReportError("The clipboard holds no text to paste."); return; }
            if (replaceAll && !await Confirm("Replace all red lines?", "Replace every red line with the pasted ones? Green lines are kept, and one undo restores the old timing.")) return;
            model.Paste(text, replaceAll);
        }
        catch (Exception ex) { model.ReportError("Paste failed: " + ex.Message); }
    }
    private async void FitOffset(object? sender, RoutedEventArgs e) => await model.FitOffsetAsync(Detect);
    private async void CheckSections(object? sender, RoutedEventArgs e) =>
        await model.CheckSectionsAsync((audio, reds) => { var t = Timing; return Task.Run(() => t.CheckSections(audio, reds)); });
    private async void SnapToOnsets(object? sender, RoutedEventArgs e) =>
        await model.SnapToOnsetsAsync((audio, queries) => { var t = Timing; return Task.Run(() => t.FindOnsets(audio, queries)); });
    private async void ApplyAutoTime(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (model.Proposal is null && model.MultiProposal is not null) { await ApplyMulti(); return; }
            if (model.ProposalPoint() is not { } point) { model.ReportError("Run Auto-time first."); return; }
            bool confirmed = await Confirm("Replace all red lines?", model.ProposalDescription + "\n\nApply one red line at " +
                OsuPrecision.FormatTime(point.Time) + " ms, " + OsuPrecision.FormatBpm(point.Bpm) + " BPM? Green lines are preserved.");
            model.ApplyProposal(confirmed);
        }
        catch (Exception ex) { model.ReportError("Enter a valid proposal offset: " + ex.Message); }
    }
    /// <summary>The release this build is ("20261023.0"), from the App project's InformationalVersion.</summary>
    public static string Version => typeof(MainWindow).Assembly
        .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
        .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion ?? "dev";
    private async void ShowAbout(object? sender, RoutedEventArgs e)
    {
        var github = new Button { Content = "Open on GitHub" };
        var close = new Button { Content = "Close" };
        var dialog = new Window { Title = "About " + AppInfo.Name, Width = 480, SizeToContent = SizeToContent.Height,
            CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner, Icon = Icon };
        var panel = new StackPanel { Margin = new Thickness(20), Spacing = 10 };
        panel.Children.Add(new TextBlock { Text = AppInfo.Name + " v" + Version, FontSize = 20, FontWeight = Avalonia.Media.FontWeight.Bold });
        panel.Children.Add(new TextBlock { Text = AppInfo.Tagline + ", by " + AppInfo.Author, FontSize = 15 });
        panel.Children.Add(new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap, Text =
            "Auto-timing for steady, multi-BPM and live-played songs (a red line on every drum hit), bar lines, normalised " +
            "slider velocity for osu!mania. It started as a rebuild of the TimingAnlyz 0.32.4 BPM/offset detector." });
        panel.Children.Add(new TextBlock { Text = AppInfo.Repository, Opacity = 0.8 });
        panel.Children.Add(new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap, Opacity = 0.7, FontSize = 12, Text =
            "Audio: BASS by un4seen (free for non-commercial use) through ManagedBass. UI: Avalonia. MVVM: CommunityToolkit.Mvvm." });
        var buttons = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 12 };
        buttons.Children.Add(github); buttons.Children.Add(close);
        panel.Children.Add(buttons); dialog.Content = panel;
        github.Click += async (_, _) =>
        {
            try { await dialog.Launcher.LaunchUriAsync(new Uri(AppInfo.Repository)); }
            catch (Exception ex) { model.ReportError(ex.Message); }
        };
        close.Click += (_, _) => dialog.Close();
        CloseOnEscape(dialog);
        await dialog.ShowDialog(this);
    }
    private async Task<bool> Confirm(string title, string message)
    {
        var yes = new Button { Content = "Replace red lines" };
        var no = new Button { Content = "Cancel" };
        var dialog = new Window { Title = title, Width = 520, SizeToContent = SizeToContent.Height,
            CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var buttons = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 12 };
        buttons.Children.Add(yes); buttons.Children.Add(no);
        var panel = new StackPanel { Margin = new Thickness(20), Spacing = 16 };
        // A long message scrolls; the buttons below it stay on screen.
        panel.Children.Add(new ScrollViewer
        {
            MaxHeight = 360, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            Content = new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap }
        });
        panel.Children.Add(buttons); dialog.Content = panel;
        yes.Click += (_, _) => dialog.Close(true); no.Click += (_, _) => dialog.Close(false);
        CloseOnEscape(dialog, false); // Esc is Cancel
        return await dialog.ShowDialog<bool>(this);
    }
    private async void CopyTiming(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (Clipboard is null) throw new IOException("Clipboard unavailable.");
            string text = model.ClipboardText();
            await WriteTimingClipboard(text);
            int rows = text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
            model.ReportError(string.Create(CultureInfo.InvariantCulture, $"Copied {rows} timing point rows for osu! (-33 ms). Pasting here keeps their original times."));
        }
        catch (Exception ex) { model.ReportError(ex.Message); }
    }
    private async Task WriteTimingClipboard(string original)
    {
        if (Clipboard is null) throw new IOException("Clipboard unavailable.");
        var item = DataTransferItem.CreateText(OsuTimingExport.Write(TimingClipboard.Parse(original)));
        item.Set(InternalTimingFormat, original);
        var data = new DataTransfer();
        data.Add(item);
        await Clipboard.SetDataAsync(data);
        await Clipboard.FlushAsync();
    }
    private async void CopyNormalized(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (Clipboard is null) throw new IOException("Clipboard unavailable.");
            var (text, summary) = model.NormalizedClipboardText();
            await WriteTimingClipboard(text);
            model.ReportError(summary);
        }
        catch (Exception ex) { model.ReportError(ex.Message); }
    }
    private async void SaveOsu(object? sender, RoutedEventArgs e)
    {
        try
        {
            string? path = model.BeatmapPath;
            if (path is null)
            {
                var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = "Save timing beatmap", SuggestedFileName = "timing.osu", DefaultExtension = "osu",
                    FileTypeChoices = [new FilePickerFileType("osu! beatmap") { Patterns = ["*.osu"] }]
                });
                path = file?.TryGetLocalPath();
            }
            if (path is not null) model.Save(path);
        }
        catch (Exception ex) { model.ReportError(ex.Message); }
    }
    private void HandleKey(object? sender, KeyEventArgs e)
    {
        if (InTextBox(e) || e.Handled || model.IsBusy) return;
        bool ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control), shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        bool plain = e.KeyModifiers == KeyModifiers.None;
        if (e.Key == Key.O && ctrl) { _ = Open(!shift); e.Handled = true; }
        else if (e.Key == Key.T && ctrl) { AutoTime(this, new RoutedEventArgs()); e.Handled = true; }
        else if (e.Key == Key.C && ctrl && shift) { CopyTiming(this, new RoutedEventArgs()); e.Handled = true; }
        else if (e.Key == Key.N && ctrl && shift) { CopyNormalized(this, new RoutedEventArgs()); e.Handled = true; }
        else if (e.Key == Key.F1) { ShowKeys(this, new RoutedEventArgs()); e.Handled = true; }
        else if (e.Key == Key.M && plain) { model.ClickEnabled = !model.ClickEnabled; e.Handled = true; }
        else if (e.Key == Key.F && plain) { model.FollowPlayhead = !model.FollowPlayhead; e.Handled = true; }
        else if (e.Key == Key.S && ctrl) { SaveOsu(this, new RoutedEventArgs()); e.Handled = true; }
        else if (e.Key == Key.Insert && plain) { AddPointForTab(this, new RoutedEventArgs()); e.Handled = true; }
        else if (e.Key == Key.Delete) { model.Delete(); e.Handled = true; }
        else if (e.Key == Key.A && ctrl) { TimingTable.SelectAll(); e.Handled = true; }
        else if (e.Key == Key.C && ctrl) { _ = CopySelectedRows(); e.Handled = true; }
        else if (e.Key == Key.V && ctrl) { _ = PasteFromClipboard(shift); e.Handled = true; }
        else if (e.Key == Key.R && ctrl) { AutoTimeRange(this, new RoutedEventArgs()); e.Handled = true; }
        else if (e.Key == Key.K && ctrl) { CheckSections(this, new RoutedEventArgs()); e.Handled = true; }
        else if (e.Key == Key.L && ctrl) { model.FillRamp(); e.Handled = true; }
        else if (e.Key == Key.Z && ctrl) { model.Undo(); e.Handled = true; }
        else if (e.Key == Key.Y && ctrl) { model.Redo(); e.Handled = true; }
        else if (e.Key == Key.Escape) { model.Escape(); e.Handled = true; }
    }
}
