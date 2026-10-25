using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.VisualTree;
using TimingAnalyzer.Editor;

namespace TimingAnalyzer.App;

public partial class MainWindow
{
    private Window? workspaceTools;
    private TabControl? workspaceTabs;
    private static T Bound<T>(T control, AvaloniaProperty property, string name, bool twoWay = false) where T : Control
    {
        if (string.IsNullOrEmpty(AutomationProperties.GetName(control))) AutomationProperties.SetName(control, System.Text.RegularExpressions.Regex.Replace(name, "([a-z])([A-Z])", "$1 $2"));
        control.Bind(property, new Binding(name) { Mode = twoWay ? BindingMode.TwoWay : BindingMode.OneWay }); return control;
    }
    private static Button ActionButton(string label, Action action, string? name = null)
    {
        var button = new Button { Content = label, Name = name }; button.Click += (_, _) => action();
        AutomationProperties.SetName(button, label); return button;
    }
    private static StackPanel ToolPanel() => new() { Margin = new Thickness(16), Spacing = 10 };
    private static void Label(StackPanel panel, string text) => panel.Children.Add(new TextBlock { Text = text, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
    private static CheckBox Check(string text, string property) => Bound(new CheckBox { Content = text }, CheckBox.IsCheckedProperty, property, true);
    private static ComboBox Choice(string items, string selected, double width = 180) => Bound(Bound(new ComboBox { MinWidth = width }, ItemsControl.ItemsSourceProperty, items), ComboBox.SelectedItemProperty, selected, true);
    private static TextBox Field(string property, string label) => Bound(new TextBox { PlaceholderText = label }, TextBox.TextProperty, property, true);

    private void ShowWorkspaceTools(object? sender, RoutedEventArgs e) => OpenWorkspaceTools(0);
    private void ShowProposalReview(object? sender, RoutedEventArgs e) => OpenWorkspaceTools(2);
    private void OpenWorkspaceTools(int tab)
    {
        if (workspaceTools is not null) { workspaceTabs!.SelectedIndex = tab; workspaceTools.Activate(); return; }
        workspaceTools = new Window { Title = "TempoMap workspace tools", Width = 620, Height = 580, MinWidth = 480, MinHeight = 360,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Icon = Icon, DataContext = model };
        var listening = ToolPanel(); Label(listening, "Loop a highlighted range (Shift+drag). Pre-roll counts beats before each pass.");
        listening.Children.Add(Check("Metronome", nameof(model.ClickEnabled)));
        Label(listening, "Music volume"); listening.Children.Add(Bound(new Slider { Minimum = 0, Maximum = 100 }, Slider.ValueProperty, nameof(model.MusicVolumePercent), true));
        listening.Children.Add(Bound(new TextBlock(), TextBlock.TextProperty, nameof(model.MusicVolumeLabel)));
        Label(listening, "Metronome volume"); listening.Children.Add(Bound(new Slider { Minimum = 0, Maximum = 100 }, Slider.ValueProperty, nameof(model.ClickVolumePercent), true));
        listening.Children.Add(Bound(new TextBlock(), TextBlock.TextProperty, nameof(model.ClickVolumeLabel)));
        listening.Children.Add(Check("Loop range", nameof(model.LoopEnabled)));
        Label(listening, "Pre-roll beats (0–16)"); listening.Children.Add(Bound(new NumericUpDown { Minimum = 0, Maximum = 16, Increment = 1 }, NumericUpDown.ValueProperty, nameof(model.PreRollBeats), true));
        listening.Children.Add(ActionButton("Play range", model.PlayRange, "PlayRangeButton"));
        Label(listening, "Alternate listening speed"); listening.Children.Add(Choice(nameof(model.Speeds), nameof(model.ComparisonSpeed)));
        listening.Children.Add(ActionButton("Swap listening speed", model.SwapComparisonSpeed, "SwapSpeedButton"));
        Label(listening, "Hold Alt+M to mute the metronome temporarily. Release to restore it.");
        listening.Children.Add(ActionButton("Tap BPM", model.TapBpm, "TapBpmButton")); listening.Children.Add(ActionButton("Reset taps", model.ResetTaps));
        var anchors = new WrapPanel { ItemSpacing = 8 }; anchors.Children.Add(ActionButton("Mark A here", model.MarkAnchorA, "MarkAnchorAButton")); anchors.Children.Add(ActionButton("Mark B here", model.MarkAnchorB, "MarkAnchorBButton")); listening.Children.Add(anchors);
        Label(listening, "Beats from A to B (e.g. 16 = four bars of 4/4)"); listening.Children.Add(Field(nameof(model.AnchorBeats), "Beat count"));
        listening.Children.Add(Bound(new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap }, TextBlock.TextProperty, nameof(model.AnchorSummary)));
        listening.Children.Add(ActionButton("Apply A–B timing", model.ApplyAnchors, "ApplyAnchorsButton"));
        Label(listening, "Multiply selected BPM (0.5 halves; each Apply is one undo step)");
        var factorField = Field(nameof(model.TempoFactorText), "BPM factor"); factorField.Name = "WorkspaceTempoFactor"; listening.Children.Add(factorField);
        listening.Children.Add(ActionButton("Apply BPM factor", model.ApplyTempoFactor));
        Label(listening, "Shift selected lines");
        listening.Children.Add(Choice(nameof(model.ShiftBeatSteps), nameof(model.SelectedShiftBeatStep)));
        var beatButtons = new WrapPanel { ItemSpacing = 8 };
        beatButtons.Children.Add(ActionButton("Earlier by beat step", () => model.ShiftBeatStepCommand.Execute("-1")));
        beatButtons.Children.Add(ActionButton("Later by beat step", () => model.ShiftBeatStepCommand.Execute("1"))); listening.Children.Add(beatButtons);
        listening.Children.Add(Choice(nameof(model.ShiftMsSteps), nameof(model.SelectedShiftMsStep)));
        var msButtons = new WrapPanel { ItemSpacing = 8 };
        msButtons.Children.Add(ActionButton("Earlier by ms step", () => model.ShiftMsStepCommand.Execute("-1")));
        msButtons.Children.Add(ActionButton("Later by ms step", () => model.ShiftMsStepCommand.Execute("1"))); listening.Children.Add(msButtons);
        Label(listening, "BPM nudge step"); listening.Children.Add(Choice(nameof(model.BpmSteps), nameof(model.SelectedBpmStep)));
        var bpmButtons = new WrapPanel { ItemSpacing = 8 };
        bpmButtons.Children.Add(ActionButton("Lower BPM one step", () => model.NudgeBpm(-1)));
        bpmButtons.Children.Add(ActionButton("Raise BPM one step", () => model.NudgeBpm(1))); listening.Children.Add(bpmButtons);

        var display = ToolPanel(); Label(display, "Waveform layer (visual aid; analysis always uses the original audio)");
        var layerChoice = Choice(nameof(model.WaveformLayers), nameof(model.WaveformLayer)); layerChoice.Name = "WaveformLayerChoice"; display.Children.Add(layerChoice);
        display.Children.Add(Check("Overlay onset strength", nameof(model.ShowOnsets))); display.Children.Add(Check("Show green SV lane", nameof(model.ShowSv)));
        display.Children.Add(Check("Show Kiai lane", nameof(model.ShowKiai))); display.Children.Add(Check("Four rows of measures", nameof(model.MeasureView)));
        Label(display, "Named bookmarks are stored in .tmap projects and recovery snapshots.");
        var bookmarkName = new TextBox { Name = "BookmarkName", PlaceholderText = "Bookmark name" }; display.Children.Add(bookmarkName);
        display.Children.Add(ActionButton("Bookmark playhead", () => model.AddBookmark(bookmarkName.Text ?? ""), "AddBookmarkButton"));
        var bookmarks = Bound(new ListBox { Name = "BookmarkList", Height = 150 }, ItemsControl.ItemsSourceProperty, nameof(model.Bookmarks)); display.Children.Add(bookmarks);
        display.Children.Add(ActionButton("Jump to bookmark", () => { if (bookmarks.SelectedItem is Bookmark marker) model.JumpTo(marker.TimeMs); }));
        display.Children.Add(ActionButton("Delete bookmark", () => { if (bookmarks.SelectedItem is Bookmark marker) model.RemoveBookmark(marker.Id); }));

        var review = ToolPanel(); review.Children.Add(Check("Ghost proposed grid on waveform", nameof(model.GhostProposal)));
        var ab = new WrapPanel { ItemSpacing = 8 }; ab.Children.Add(ActionButton("A · Current grid", model.AuditionCurrent, "AuditionCurrentButton")); ab.Children.Add(ActionButton("B · Proposed grid", model.AuditionPending, "AuditionProposalButton")); review.Children.Add(ab);
        Label(review, "Audition changes only the metronome. Apply in the main window commits the proposal.");
        var proposals = Bound(new ListBox { Height = 180, Name = "ProposalList" }, ItemsControl.ItemsSourceProperty, nameof(model.ProposalSections));
        proposals.SelectionChanged += (_, _) => model.JumpProposal(proposals.SelectedIndex); review.Children.Add(proposals);
        Label(review, "Approximate BPM alternatives · relative autocorrelation strength, not probability. Choosing one locks it; Auto-time then fits the offset.");
        var candidates = Bound(new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, Name = "BpmCandidates" }, ItemsControl.ItemsSourceProperty, nameof(model.BpmCandidates)); review.Children.Add(candidates);
        review.Children.Add(ActionButton("Use selected BPM", () => { if (candidates.SelectedItem is BpmCandidate candidate) model.ChooseCandidate(candidate); }, "UseCandidateButton"));
        var issues = Bound(new ListBox { Height = 150 }, ItemsControl.ItemsSourceProperty, nameof(model.ReviewQueue)); review.Children.Add(issues);
        issues.SelectionChanged += (_, _) => { if (issues.SelectedItem is ReviewSection issue) { model.Select(issue.Index); model.JumpTo(issue.TimeMs); } };
        var navigation = new WrapPanel { ItemSpacing = 8 };
        navigation.Children.Add(ActionButton("Previous issue", () => model.ReviewNext(-1))); navigation.Children.Add(ActionButton("Next issue", () => model.ReviewNext(), "NextIssueButton"));
        navigation.Children.Add(ActionButton("Mark reviewed", model.MarkReviewed)); review.Children.Add(navigation);
        workspaceTabs = new TabControl { SelectedIndex = tab, ItemsSource = new[]
        {
            new TabItem { Header = "Listening", Content = new ScrollViewer { Content = listening } },
            new TabItem { Header = "Display / bookmarks", Content = new ScrollViewer { Content = display } },
            new TabItem { Header = "Proposal / review", Content = new ScrollViewer { Content = review } }
        } };
        workspaceTools.Content = workspaceTabs;
        workspaceTools.Closed += (_, _) => { model.AuditionCurrent(); workspaceTools = null; workspaceTabs = null; };
        CloseOnEscape(workspaceTools); workspaceTools.Show(this);
    }

    private void InitializeWorkspaceUi()
    {
        var waveformMenu = new ContextMenu();
        void Add(ContextMenu menu, string label, Action action) { var item = new MenuItem { Header = label }; item.Click += (_, _) => action(); menu.Items.Add(item); }
        Add(waveformMenu, "Add red point here", () => AddTimingPoint(this, new RoutedEventArgs()));
        Add(waveformMenu, "Add green point here", () => AddInheritedPoint(this, new RoutedEventArgs()));
        Add(waveformMenu, "Auto-time highlighted range", () => AutoTimeRange(this, new RoutedEventArgs()));
        Add(waveformMenu, "Play highlighted range", model.PlayRange);
        Add(waveformMenu, "Mark anchor A", model.MarkAnchorA); Add(waveformMenu, "Mark anchor B", model.MarkAnchorB);
        Waveform.ContextMenu = waveformMenu;
        var rows = new ContextMenu();
        Add(rows, "Delete selected", model.Delete); Add(rows, "Copy selected", () => _ = CopySelectedRows());
        Add(rows, "Jump to selected", () => { if (model.SelectedRow is { } row) model.JumpTo(row.Point.Time); else if (model.SelectedGreenIndices.LastOrDefault(-1) is >= 0 and var i) model.JumpTo(model.InheritedRows[i].Point.Time); });
        Add(rows, "Double BPM", () => model.ScaleTempoCommand.Execute("2")); Add(rows, "Halve BPM", () => model.ScaleTempoCommand.Execute("0.5"));
        TimingTable.ContextMenu = rows;
        TimingTable.AddHandler(PointerPressedEvent, (_, e) =>
        {
            if (!e.GetCurrentPoint(TimingTable).Properties.IsRightButtonPressed) return;
            var row = (e.Source as Visual)?.GetSelfAndVisualAncestors().OfType<ListBoxItem>().FirstOrDefault()?.DataContext as TimingRow;
            if (row is not null && !(row.IsRed ? model.SelectedIndices.Contains(row.RedIndex) : model.SelectedGreenIndices.Contains(row.GreenIndex))) model.SelectTableRows([row], row);
        }, RoutingStrategies.Tunnel);
        Opened += (_, _) =>
        {
            foreach (var control in this.GetVisualDescendants().OfType<Control>())
            {
                if (ToolTip.GetTip(control) is string tip && tip.Length > 170)
                {
                    int end = tip.IndexOf(". ", StringComparison.Ordinal);
                    ToolTip.SetTip(control, end is > 0 and < 165 ? tip[..(end + 1)] : tip[..160] + "… (F2: guide)");
                }
                if (control is Button or ComboBox or Slider or TextBox && string.IsNullOrEmpty(AutomationProperties.GetName(control)))
                    AutomationProperties.SetName(control, ToolTip.GetTip(control) as string ?? control.Name ?? (control as Button)?.Content?.ToString() ?? "Editor control");
            }
        };
    }
    private void CancelAnalysis(object? sender, RoutedEventArgs e) => model.CancelAnalysis();
    private static StackPanel BuildThaiGuide()
    {
        var panel = new StackPanel { Spacing = 16 };
        foreach (string paragraph in ToolGuide.Thai.Replace("\r", "").Split("\n\n", StringSplitOptions.RemoveEmptyEntries))
        {
            var lines = paragraph.Split('\n', 2);
            var section = new StackPanel { Spacing = 5 };
            section.Children.Add(new TextBlock { Text = lines[0], FontSize = 16, FontWeight = Avalonia.Media.FontWeight.SemiBold, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
            if (lines.Length > 1) section.Children.Add(new TextBlock { Text = lines[1], FontSize = 14, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
            panel.Children.Add(section);
        }
        return panel;
    }
}
