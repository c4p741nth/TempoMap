using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using TimingAnalyzer.Editor;

namespace TimingAnalyzer.App;

/// <summary>
/// F3: Listening, Display / bookmarks and Proposal / review. Each tab is a set of cards 318 px wide that flow into one
/// or two columns with the window, so no row stretches across the whole window and nothing needs a long scroll.
/// </summary>
public partial class MainWindow
{
    // Two cards and the gap (648) fit the 664 px left beside the scrollbar of the default 720 px window.
    private const double CardWidth = 318;
    private static Border Sized(Border card) { card.Width = CardWidth; return card; }
    private static WrapPanel Row(params Control[] items)
    {
        var row = new WrapPanel { ItemSpacing = 8, LineSpacing = 6 };
        foreach (var item in items) { item.VerticalAlignment = VerticalAlignment.Center; row.Children.Add(item); }
        return row;
    }
    private static TextBlock Caption(string text) => new() { Text = text, VerticalAlignment = VerticalAlignment.Center };
    /// <summary>Label, slider and value on one line.</summary>
    private static Grid SliderRow(string label, string value, string text)
    {
        var row = new Grid { ColumnDefinitions = new("86,*,46"), ColumnSpacing = 8 };
        row.Children.Add(Caption(label));
        var slider = Bound(new Slider { Minimum = 0, Maximum = 100, VerticalAlignment = VerticalAlignment.Center }, Slider.ValueProperty, value, true);
        Grid.SetColumn(slider, 1); row.Children.Add(slider);
        var read = Bound(new TextBlock { TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center }, TextBlock.TextProperty, text);
        Grid.SetColumn(read, 2); row.Children.Add(read);
        return row;
    }
    private static ScrollViewer CardPage(string? note, params Border[] cards)
    {
        var flow = new WrapPanel { ItemSpacing = 12, LineSpacing = 12 };
        foreach (var card in cards) flow.Children.Add(Sized(card));
        var page = new StackPanel { Margin = new Thickness(16), Spacing = 12 };
        if (note is not null) page.Children.Add(Hint(note));
        page.Children.Add(flow);
        return new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = page };
    }

    private void ShowWorkspaceTools(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => OpenWorkspaceTools(0);
    private void ShowProposalReview(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => OpenWorkspaceTools(2);
    private void OpenWorkspaceTools(int tab)
    {
        if (workspaceTools is not null) { workspaceTabs!.SelectedIndex = tab; workspaceTools.Activate(); return; }
        workspaceTools = NewDialog("TempoMap workspace tools", 720, resizable: true, height: 680); workspaceTools.DataContext = model;

        var tempoFactor = Field(nameof(model.TempoFactorText), "BPM factor"); tempoFactor.Name = "WorkspaceTempoFactor"; tempoFactor.Width = 100;
        var beats = Field(nameof(model.AnchorBeats), "Beat count"); beats.Width = 100;
        var preRoll = Bound(new NumericUpDown { Minimum = 0, Maximum = 16, Increment = 1, Width = 110 }, NumericUpDown.ValueProperty, nameof(model.PreRollBeats), true);
        var speed = Choice(nameof(model.Speeds), nameof(model.ComparisonSpeed), 96);
        var listening = CardPage("Shift (beat, ms, BPM), Loop and Metronome are in the main window.",
            Card("Click and volume",
                Check("Metronome", nameof(model.ClickEnabled)),
                SliderRow("Music", nameof(model.MusicVolumePercent), nameof(model.MusicVolumeLabel)),
                SliderRow("Metronome", nameof(model.ClickVolumePercent), nameof(model.ClickVolumeLabel)),
                Hint("Hold Alt+M to mute the metronome for a moment; release to restore it.")),
            Card("Range loop",
                Hint("Highlight a range with Shift+drag on the waveform."),
                Check("Loop range", nameof(model.LoopEnabled)),
                Row(Caption("Pre-roll beats (0–16)"), preRoll),
                ActionButton("Play range", model.PlayRange, "PlayRangeButton")),
            Card("Listening speed",
                Row(Caption("Alternate speed"), speed),
                ActionButton("Swap listening speed", model.SwapComparisonSpeed, "SwapSpeedButton"),
                Hint("Backspace swaps too.")),
            Card("Tap tempo",
                Row(ActionButton("Tap BPM", model.TapBpm, "TapBpmButton"), ActionButton("Reset taps", model.ResetTaps)),
                Hint("Tap along with the music (T). The estimate goes into the Auto-time BPM box; it is a start, not the answer.")),
            Card("A–B anchors",
                Row(ActionButton("Mark A here", model.MarkAnchorA, "MarkAnchorAButton"), ActionButton("Mark B here", model.MarkAnchorB, "MarkAnchorBButton")),
                Row(Caption("Beats from A to B"), beats),
                Hint("16 = four bars of 4/4."),
                Bound(new TextBlock { TextWrapping = TextWrapping.Wrap }, TextBlock.TextProperty, nameof(model.AnchorSummary)),
                ActionButton("Apply A–B timing", model.ApplyAnchors, "ApplyAnchorsButton")),
            Card("BPM factor",
                Hint("Multiply the BPM of the selected red lines: 0.5 halves. Each Apply is one undo step."),
                Row(tempoFactor, ActionButton("Apply BPM factor", model.ApplyTempoFactor))));

        var layerChoice = Choice(nameof(model.WaveformLayers), nameof(model.WaveformLayer), 200); layerChoice.Name = "WaveformLayerChoice";
        var bookmarkName = new TextBox { Name = "BookmarkName", PlaceholderText = "Bookmark name", Width = 180 };
        var bookmarks = Bound(new ListBox { Name = "BookmarkList", Height = 150 }, ItemsControl.ItemsSourceProperty, nameof(model.Bookmarks));
        var display = CardPage(null,
            Card("Waveform",
                Hint("A visual aid: analysis always uses the original audio."), layerChoice,
                Check("Overlay onset strength", nameof(model.ShowOnsets)), Check("Show green SV lane", nameof(model.ShowSv)),
                Check("Show Kiai lane", nameof(model.ShowKiai)), Check("Four rows of measures", nameof(model.MeasureView))),
            Card("Bookmarks",
                Hint("Named bookmarks are stored in .tmap projects and recovery snapshots."),
                Row(bookmarkName, ActionButton("Bookmark playhead", () => model.AddBookmark(bookmarkName.Text ?? ""), "AddBookmarkButton")), bookmarks,
                Row(ActionButton("Jump to bookmark", () => { if (bookmarks.SelectedItem is Bookmark marker) model.JumpTo(marker.TimeMs); }),
                    ActionButton("Delete bookmark", () => { if (bookmarks.SelectedItem is Bookmark marker) model.RemoveBookmark(marker.Id); }))));

        var proposals = Bound(new ListBox { Height = 150, Name = "ProposalList" }, ItemsControl.ItemsSourceProperty, nameof(model.ProposalSections));
        proposals.SelectionChanged += (_, _) => model.JumpProposal(proposals.SelectedIndex);
        var candidates = Bound(new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, Name = "BpmCandidates", PlaceholderText = "No alternatives yet" }, ItemsControl.ItemsSourceProperty, nameof(model.BpmCandidates));
        var issues = Bound(new ListBox { Height = 150 }, ItemsControl.ItemsSourceProperty, nameof(model.ReviewQueue));
        issues.SelectionChanged += (_, _) => { if (issues.SelectedItem is ReviewSection issue) { model.Select(issue.Index); model.JumpTo(issue.TimeMs); } };
        var review = CardPage(null,
            Card("Proposal",
                Check("Ghost proposed grid on waveform", nameof(model.GhostProposal)),
                Row(ActionButton("A · Current grid", model.AuditionCurrent, "AuditionCurrentButton"), ActionButton("B · Proposed grid", model.AuditionPending, "AuditionProposalButton")),
                Hint("Audition changes only the metronome. Apply in the main window commits the proposal."), proposals),
            Card("BPM alternatives",
                Hint("Relative autocorrelation strength, not probability. Choosing one locks it; Auto-time then fits the offset."), candidates,
                ActionButton("Use selected BPM", () => { if (candidates.SelectedItem is BpmCandidate candidate) model.ChooseCandidate(candidate); }, "UseCandidateButton")),
            Card("Sections to check",
                Hint("Run Check sections (Ctrl+K), then pick one to jump to it."), issues,
                Row(ActionButton("Previous issue", () => model.ReviewNext(-1)), ActionButton("Next issue", () => model.ReviewNext(), "NextIssueButton"),
                    ActionButton("Mark reviewed", model.MarkReviewed))));

        workspaceTabs = new TabControl { SelectedIndex = tab, ItemsSource = new[]
        {
            new TabItem { Header = "Listening", Content = listening },
            new TabItem { Header = "Display / bookmarks", Content = display },
            new TabItem { Header = "Proposal / review", Content = review }
        } };
        workspaceTools.Content = workspaceTabs;
        workspaceTools.Closed += (_, _) => { model.AuditionCurrent(); workspaceTools = null; workspaceTabs = null; };
        CloseOnEscape(workspaceTools); workspaceTools.Show(this);
    }
}
