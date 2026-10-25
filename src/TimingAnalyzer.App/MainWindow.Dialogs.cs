using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using TimingAnalyzer.Editor;

namespace TimingAnalyzer.App;

/// <summary>
/// The secondary windows (dialogs and tool windows) are built in code. They share these pieces so they look like one
/// family with the main window: the same 24 px margin, cards on the canvas colour, right-aligned buttons with the
/// main action in the accent colour, and muted hints.
/// </summary>
public partial class MainWindow
{
    private Window NewDialog(string title, double width, bool resizable = false, double? height = null)
    {
        var dialog = new Window { Title = title, Width = width, MinWidth = Math.Min(width, 360), CanResize = resizable,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Icon = Icon };
        if (height is { } h) { dialog.Height = h; dialog.MinHeight = Math.Min(h, 320); } else dialog.SizeToContent = SizeToContent.Height;
        return dialog;
    }
    private static StackPanel DialogBody(params Control[] content)
    {
        var body = new StackPanel { Margin = new Thickness(24, 20), Spacing = 16 };
        body.Children.AddRange(content); return body;
    }
    private static Button Primary(Button button) { button.Classes.Add("primary"); return button; }
    private static Button Danger(Button button) { button.Classes.Add("danger"); return button; }
    private static WrapPanel ButtonRow(IEnumerable<Button> buttons)
    {
        var row = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, ItemSpacing = 8, LineSpacing = 8 };
        row.Children.AddRange(buttons); return row;
    }
    private static TextBlock Hint(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, Classes = { "hint" } };
    private static TextBlock Heading(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, Classes = { "title" } };
    private static Border Card(string? caption, params Control[] content)
    {
        var body = new StackPanel { Spacing = 8 };
        if (caption is not null) body.Children.Add(new TextBlock { Text = caption.ToUpperInvariant(), Classes = { "caption" } });
        body.Children.AddRange(content);
        return new Border { Classes = { "surface" }, Padding = new Thickness(14, 12), Child = body };
    }
    private static Grid LabeledRow(string label, Control control, double labelWidth = 190)
    {
        var row = new Grid { ColumnDefinitions = new($"{labelWidth.ToString(System.Globalization.CultureInfo.InvariantCulture)},*"), ColumnSpacing = 10 };
        row.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap });
        control.HorizontalAlignment = HorizontalAlignment.Left; control.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(control, 1); row.Children.Add(control); return row;
    }

    /// <summary>A message and its choices; the result is the chosen index (-1 for Esc). The first choice is the main action.</summary>
    private async Task<int> Choose(string title, string message, params string[] choices)
    {
        var dialog = NewDialog(title, 540); dialog.MaxHeight = 560;
        var buttons = new List<Button>();
        for (int i = 0; i < choices.Length; i++)
        {
            int result = i; var button = new Button { Content = choices[i] };
            if (i == 0) Primary(button);
            if (choices[i].StartsWith("Discard", StringComparison.Ordinal)) Danger(button);
            button.Click += (_, _) => dialog.Close(result); buttons.Add(button);
        }
        dialog.Content = DialogBody(new ScrollViewer { MaxHeight = 340, Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, FontSize = 14, LineHeight = 21 } }, ButtonRow(buttons));
        CloseOnEscape(dialog, -1);
        return await dialog.ShowDialog<int>(this);
    }
    private async Task<bool> Confirm(string title, string message)
    {
        var yes = Primary(new Button { Content = "Replace red lines" }); var no = new Button { Content = "Cancel" };
        var dialog = NewDialog(title, 520);
        // A long message scrolls; the buttons below it stay on screen.
        dialog.Content = DialogBody(new ScrollViewer { MaxHeight = 360, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, FontSize = 14, LineHeight = 21 } }, ButtonRow([yes, no]));
        yes.Click += (_, _) => dialog.Close(true); no.Click += (_, _) => dialog.Close(false);
        CloseOnEscape(dialog, false); // Esc is Cancel
        return await dialog.ShowDialog<bool>(this);
    }
    private async Task OfferRecovery()
    {
        var entries = recovery.List().Where(x => x.Path != recovery.Path).ToArray();
        if (entries.Length == 0) return;
        var list = new ListBox { ItemsSource = entries, SelectedIndex = 0, MaxHeight = 220 };
        var restore = Primary(new Button { Content = "Recover selected" }); var later = new Button { Content = "Later" };
        var dialog = NewDialog("Recover editing session", 560);
        dialog.Content = DialogBody(Heading("A previous editing session did not close normally."),
            Hint("Pick a snapshot to review it. Later keeps the snapshots for the next start."), list, ButtonRow([restore, later]));
        restore.Click += (_, _) => dialog.Close(list.SelectedItem as RecoveryEntry); later.Click += (_, _) => dialog.Close();
        CloseOnEscape(dialog);
        if (await dialog.ShowDialog<RecoveryEntry?>(this) is { } entry)
        {
            await model.RestoreProjectAsync(entry.Project);
            recovery.Save(model.CaptureProject());
            File.Delete(entry.Path);
        }
    }

    private async void ShowAbout(object? sender, RoutedEventArgs e)
    {
        var github = new Button { Content = "Open on GitHub" }; var close = Primary(new Button { Content = "Close" });
        var dialog = NewDialog("About " + AppInfo.Name, 500);
        var head = new Grid { ColumnDefinitions = new("Auto,*"), ColumnSpacing = 16 };
        head.Children.Add(new Image { Source = BrandIcon.Source, Width = 56, Height = 56, VerticalAlignment = VerticalAlignment.Top });
        var names = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        names.Children.Add(new TextBlock { Text = AppInfo.Name, FontSize = 22, FontWeight = FontWeight.Bold });
        names.Children.Add(Hint("Version " + Version));
        names.Children.Add(new TextBlock { Text = AppInfo.Tagline + ", by " + AppInfo.Author, TextWrapping = TextWrapping.Wrap });
        Grid.SetColumn(names, 1); head.Children.Add(names);
        dialog.Content = DialogBody(head,
            new TextBlock { TextWrapping = TextWrapping.Wrap, LineHeight = 20, Text =
                "Auto-timing for steady, multi-BPM and live-played songs (a red line on every drum hit), bar lines, normalised " +
                "slider velocity for osu!mania. It started as a rebuild of the TimingAnlyz 0.32.4 BPM/offset detector." },
            Card(null, new TextBlock { Text = AppInfo.Repository, TextWrapping = TextWrapping.Wrap },
                Hint("Audio: BASS by un4seen (free for non-commercial use) through ManagedBass. UI: Avalonia. MVVM: CommunityToolkit.Mvvm.")),
            ButtonRow([github, close]));
        github.Click += async (_, _) =>
        {
            try { await dialog.Launcher.LaunchUriAsync(new Uri(AppInfo.Repository)); }
            catch (Exception ex) { model.ReportError(ex.Message); }
        };
        close.Click += (_, _) => dialog.Close();
        CloseOnEscape(dialog);
        await dialog.ShowDialog(this);
    }

    /// <summary>F1: every command key as configured, the fixed keys and mouse moves, and the colours of the waveform.</summary>
    private async void ShowKeys(object? sender, RoutedEventArgs e)
    {
        var dialog = NewDialog("Keyboard shortcuts", 800, resizable: true, height: 620);
        static Border Chip(string keys) => new() { Classes = { "chip" }, VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = keys.Length == 0 ? "—" : keys.Replace("Back", "Backspace", StringComparison.Ordinal), FontFamily = new FontFamily("Consolas,monospace"), FontSize = 12 } };
        var commands = new WrapPanel { ItemSpacing = 24, LineSpacing = 6 };
        foreach (var command in EditorShortcuts.Defaults)
        {
            var row = new Grid { Width = 340, ColumnDefinitions = new("*,Auto"), ColumnSpacing = 10 };
            row.Children.Add(new TextBlock { Text = command.Key, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
            var chip = Chip(fileSettings.Shortcuts.GetValueOrDefault(command.Key, command.Value)); Grid.SetColumn(chip, 1); row.Children.Add(chip);
            commands.Children.Add(row);
        }
        var fixedKeys = new StackPanel { Spacing = 6 };
        foreach (var (keys, meaning) in new[]
        {
            ("← →", "Step the playhead by the beat snap; hold to scrub"), ("Shift+← →", "Highlight a range from where the steps began"),
            ("Ctrl+← →", "Shift the selected red lines 1 ms (Ctrl+Shift: 5 ms)"), ("Ctrl+↑ ↓", "Nudge their BPM by its step (Ctrl+Shift: ten steps)"),
            ("Home  End", "Go to the song start / end"), ("Esc", "Clear the range or selection; cancel a field edit"),
            ("Shift+drag", "Highlight a range on the waveform"), ("Middle drag", "Pan the waveform"), ("Ctrl+wheel", "Zoom the waveform"),
            ("Alt+M (hold)", "Mute the metronome for a moment"), ("Ctrl+click  Shift+click", "Select several table rows")
        })
        {
            var row = new Grid { ColumnDefinitions = new("170,*"), ColumnSpacing = 10 };
            var chip = Chip(keys); chip.HorizontalAlignment = HorizontalAlignment.Left; row.Children.Add(chip);
            var text = new TextBlock { Text = meaning, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center }; Grid.SetColumn(text, 1); row.Children.Add(text);
            fixedKeys.Children.Add(row);
        }
        var colours = new WrapPanel { ItemSpacing = 24, LineSpacing = 8 };
        foreach (var (key, meaning) in new[]
        {
            ("WaveformRedBrush", "red: timing point"), ("TimingGreenBrush", "green: SV / inherited point"), ("WaveformPlayheadBrush", "gold: playhead"),
            ("WaveformMeasureBrush", "purple: bar line"), ("OverlayGhostBrush", "cyan: proposed grid"), ("OverlayRangeBrush", "blue: highlighted range"),
            ("OverlayOnsetBrush", "amber: Kiai / onset strength")
        })
        {
            var item = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Width = 200 };
            var swatch = new Border { Width = 14, Height = 14, CornerRadius = new CornerRadius(4), VerticalAlignment = VerticalAlignment.Center };
            swatch.Bind(Border.BackgroundProperty, dialog.GetResourceObservable(key));
            item.Children.Add(swatch); item.Children.Add(new TextBlock { Text = meaning, VerticalAlignment = VerticalAlignment.Center });
            colours.Children.Add(item);
        }
        var change = new Button { Content = "Change shortcuts…" }; var close = Primary(new Button { Content = "Close" });
        change.Click += (_, _) => { dialog.Close(); ShowPreferences(this, new RoutedEventArgs()); }; close.Click += (_, _) => dialog.Close();
        var buttons = ButtonRow([change, close]); buttons.Margin = new Thickness(24, 8, 24, 16);
        var cards = new StackPanel { Margin = new Thickness(24, 20, 24, 8), Spacing = 12 };
        cards.Children.Add(Card("Commands", Hint("As currently set; empty means off."), commands));
        cards.Children.Add(Card("Fixed keys and mouse", fixedKeys));
        cards.Children.Add(Card("Colours", colours));
        var dock = new DockPanel(); DockPanel.SetDock(buttons, Dock.Bottom); dock.Children.Add(buttons);
        dock.Children.Add(new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = cards });
        dialog.Content = dock; CloseOnEscape(dialog);
        await dialog.ShowDialog(this);
    }

    private async void ShowToolGuide(object? sender, RoutedEventArgs e)
    {
        var dialog = NewDialog("คู่มือเครื่องมือ TempoMap", Math.Min(760, Width - 40), resizable: true, height: Math.Min(660, Height - 40));
        var close = Primary(new Button { Content = "ปิด" });
        var buttons = ButtonRow([close]); buttons.Margin = new Thickness(24, 8, 24, 16);
        var dock = new DockPanel(); DockPanel.SetDock(buttons, Dock.Bottom); dock.Children.Add(buttons);
        var guide = BuildThaiGuide(); guide.Margin = new Thickness(24, 20, 24, 8);
        dock.Children.Add(new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = guide });
        dialog.Content = dock; close.Click += (_, _) => dialog.Close(); CloseOnEscape(dialog);
        await dialog.ShowDialog(this);
    }
    private static StackPanel BuildThaiGuide()
    {
        var panel = new StackPanel { Spacing = 12 };
        foreach (string paragraph in ToolGuide.Thai.Replace("\r", "").Split("\n\n", StringSplitOptions.RemoveEmptyEntries))
        {
            var lines = paragraph.Split('\n', 2);
            var section = new StackPanel { Spacing = 6 };
            section.Children.Add(Heading(lines[0]));
            if (lines.Length > 1) section.Children.Add(new TextBlock { Text = lines[1], FontSize = 14, LineHeight = 22, TextWrapping = TextWrapping.Wrap });
            panel.Children.Add(new Border { Classes = { "surface" }, Padding = new Thickness(16, 12), Child = section });
        }
        return panel;
    }
}
