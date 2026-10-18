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
    public MainWindow()
    {
        InitializeComponent();
        model = new EditorViewModel(path => Task.Run(() =>
        {
            var decoded = AudioDecoder.Read(path);
            return AudioContent.Create(decoded.Mono, checked((int)decoded.Rate));
        }), path => new PlaybackEngine(path));
        DataContext = model;
        // Avalonia 12 TopLevel.RequestAnimationFrame(Action<TimeSpan>), verified in cached XML.
        Opened += (_, _) => RequestAnimationFrame(UpdateFrame);
        Closed += (_, _) => { closed = true; model.Dispose(); };
        AddHandler(KeyDownEvent, HandleKey, RoutingStrategies.Bubble);
        // Tunnel, so Space and ←/→ reach the transport before a focused button, slider, combo box or list uses
        // them: otherwise Space would press the last clicked button again instead of playing.
        AddHandler(KeyDownEvent, HandleTransportKey, RoutingStrategies.Tunnel);
        AddHandler(KeyUpEvent, SwallowSpaceUp, RoutingStrategies.Tunnel);
        // After every layout pass, not on SizeChanged: there the slider and status heights could still be the old ones
        // (at 900x500 the toolbars got 117 px instead of 67, leaving the waveform 110 px).
        LayoutUpdated += (_, _) => FitPanels();
        // A new proposal shows its apply buttons even when the toolbars are scrolled or taller than their room.
        model.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(EditorViewModel.SelectedIndices))
            {
                // A selection made elsewhere (waveform box, Esc, follow) replaces the table's green rows too.
                if (!syncingTable) selectedGreens.Clear();
                QueueTableSync();
            }
            else if (e.PropertyName == nameof(EditorViewModel.TableRows)) QueueTableSync();
            if (e.PropertyName == nameof(EditorViewModel.HasProposal) && model.HasProposal)
                Avalonia.Threading.Dispatcher.UIThread.Post(() => ApplyRow.BringIntoView(), Avalonia.Threading.DispatcherPriority.Background);
        };
    }
    // The table selects red and green rows; the red ones are the model's selection (what the editing tools act on),
    // the green ones live here and only matter for Copy selected.
    private bool syncingTable, syncQueued;
    private HashSet<TimingPoint> selectedGreens = [];
    private void TableSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (syncingTable) return;
        var rows = TimingTable.SelectedItems?.OfType<TimingRow>().ToList() ?? [];
        selectedGreens = rows.Where(r => r.IsGreen).Select(r => r.Point).ToHashSet();
        var reds = rows.Where(r => r.IsRed).Select(r => r.RedIndex).ToList();
        int primary = e.AddedItems.OfType<TimingRow>().LastOrDefault(r => r.IsRed)?.RedIndex ??
            (model.SelectedRow is { } current && reds.Contains(current.Index) ? current.Index : reds.FirstOrDefault(-1));
        syncingTable = true;
        try { model.SelectRows(reds, primary); }
        finally { syncingTable = false; }
        // A plain click (one row) jumps there, as osu!'s list does; Ctrl / Shift clicks only select.
        if (rows.Count == 1 && e.AddedItems.Count == 1) model.JumpTo(rows[0].Point.Time);
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
        syncingTable = true;
        try
        {
            var selection = TimingTable.Selection;
            selection.BeginBatchUpdate();
            try
            {
                selection.Clear();
                for (int i = 0; i < items.Count; i++)
                    if (items[i].IsRed ? reds.Contains(items[i].RedIndex) : selectedGreens.Contains(items[i].Point)) selection.Select(i);
            }
            finally { selection.EndBatchUpdate(); }
        }
        finally { syncingTable = false; }
        if (model.SelectedRow is { } row)
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
            await Clipboard.SetTextAsync(EditorViewModel.RowsClipboardText(rows));
            int red = rows.Count(r => r.IsRed);
            model.ReportError("Copied " + red + " red and " + (rows.Count - red) + " green timing point rows - paste them into the osu! editor.");
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
            TopScroll.Margin.Bottom - ScrollSlider.Bounds.Height - MinWaveformHeight));
        Set(DetailsScroll, Math.Max(48, RightPanel.Bounds.Height - RedLinesTitle.Bounds.Height - RedLinesTitle.Margin.Bottom -
            TableHeader.Bounds.Height - TableHeader.Margin.Bottom - DetailsScroll.Margin.Top - MinListHeight));
    }
    private static bool InTextBox(KeyEventArgs e) =>
        e.Source is TextBox || e.Source is Visual v && v.GetVisualAncestors().OfType<TextBox>().Any();
    private void HandleTransportKey(object? sender, KeyEventArgs e)
    {
        if (InTextBox(e) || model.IsBusy) return;
        if (e.Key == Key.Space) { model.TogglePlay(); e.Handled = true; }
        else if (e.Key is Key.Left or Key.Right && e.KeyModifiers == KeyModifiers.None)
        {
            model.StepSnap(e.Key == Key.Right ? 1 : -1);
            e.Handled = true;
        }
    }
    // A button clicks on Space release; the press was taken by the transport, so the release must not click.
    private void SwallowSpaceUp(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space && !InTextBox(e)) e.Handled = true;
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
                { Patterns = osu ? ["*.osu"] : ["*.mp3", "*.ogg", "*.wav", "*.flac", "*.mp4"] }]
            });
            string? path = files.FirstOrDefault()?.TryGetLocalPath();
            if (path is not null) await model.LoadAsync(path);
        }
        catch (Exception ex) { model.ReportError(ex.Message); }
    }
    private readonly CoreTiming timing = new();
    private Task<TimingDetection> Detect(AudioContent audio, TimingRequest request) => Task.Run(() => timing.Detect(audio, request));
    private async void AutoTime(object? sender, RoutedEventArgs e) => await model.AutoTimeAsync(Detect,
        audio => Task.Run(() => timing.DetectMulti(audio)), (audio, beats) => Task.Run(() => timing.DetectDrift(audio, beats)));
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
    private async void FitOffset(object? sender, RoutedEventArgs e) => await model.FitOffsetAsync(Detect);
    private async void SnapToOnsets(object? sender, RoutedEventArgs e) =>
        await model.SnapToOnsetsAsync((audio, queries) => Task.Run(() => timing.FindOnsets(audio, queries)));
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
    /// <summary>The release this build is ("20261018.0"), from the App project's InformationalVersion.</summary>
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
        return await dialog.ShowDialog<bool>(this);
    }
    private async void CopyTiming(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (Clipboard is null) throw new IOException("Clipboard unavailable.");
            string text = model.ClipboardText();
            await Clipboard.SetTextAsync(text);
            int rows = text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
            model.ReportError(rows + " timing point rows copied (no [TimingPoints] header) - paste them into the osu! editor.");
        }
        catch (Exception ex) { model.ReportError(ex.Message); }
    }
    private async void CopyNormalized(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (Clipboard is null) throw new IOException("Clipboard unavailable.");
            var (text, summary) = model.NormalizedClipboardText();
            await Clipboard.SetTextAsync(text);
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
        bool ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        if (e.Key == Key.Insert || e.Key == Key.P && ctrl) { model.Insert(); e.Handled = true; }
        else if (e.Key == Key.Delete) { model.Delete(); e.Handled = true; }
        else if (e.Key == Key.A && ctrl) { TimingTable.SelectAll(); e.Handled = true; }
        else if (e.Key == Key.C && ctrl) { _ = CopySelectedRows(); e.Handled = true; }
        else if (e.Key == Key.Z && ctrl) { model.Undo(); e.Handled = true; }
        else if (e.Key == Key.Y && ctrl) { model.Redo(); e.Handled = true; }
        else if (e.Key == Key.Escape) { model.Escape(); e.Handled = true; }
    }
}
