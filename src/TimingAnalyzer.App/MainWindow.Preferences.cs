using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using TimingAnalyzer.Editor;

namespace TimingAnalyzer.App;

public partial class MainWindow
{
    private bool HandleConfiguredShortcut(KeyEventArgs e)
    {
        bool input = InInputControl(e);
        bool GlobalInInput(string command) => command is "Add red point" or "Add green point" or "Move point here" or
            "Apply proposal" or "Auto-time" or "Open beatmap" or "Open audio" or "Save beatmap" or "Save project" or "Keys" or "Thai guide" or "Workspace tools";
        static bool Matches(string value, KeyEventArgs key)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            try { var gesture = KeyGesture.Parse(value); return gesture.Key == key.Key && gesture.KeyModifiers == key.KeyModifiers; }
            catch (FormatException) { return false; } catch (ArgumentException) { return false; }
        }
        foreach (var command in EditorShortcuts.Defaults)
        {
            string configured = fileSettings.Shortcuts.GetValueOrDefault(command.Key, command.Value);
            if (!Matches(configured, e)) continue;
            if (input && (!GlobalInInput(command.Key) || e.KeyModifiers == KeyModifiers.None && e.Key is not (Key.F1 or Key.F2 or Key.F3))) return false;
            if (!model.IsBusy || command.Key == "Play / Pause") ExecuteShortcut(command.Key);
            e.Handled = true; return true;
        }
        // A remapped default must stop activating its old hard-coded handler.
        if (!input && EditorShortcuts.Defaults.Any(c => fileSettings.Shortcuts.ContainsKey(c.Key) && Matches(c.Value, e)))
        { e.Handled = true; return true; }
        return false;
    }
    private void ExecuteShortcut(string command)
    {
        var e = new RoutedEventArgs();
        switch (command)
        {
            case "Play / Pause": model.TogglePlay(); break;
            case "Add red point": AddTimingPoint(this, e); break;
            case "Add green point": AddInheritedPoint(this, e); break;
            case "Move point here": if (SavePointFields()) model.MoveToPlayhead(); break;
            case "Auto-time": AutoTime(this, e); break;
            case "Apply proposal": if (SavePointFields()) ApplyAutoTime(this, e); break;
            case "Auto-time range": AutoTimeRange(this, e); break;
            case "Check sections": CheckSections(this, e); break;
            case "Fill ramp": model.FillRamp(); break;
            case "Fit offset": FitOffset(this, e); break;
            case "Snap to onsets": SnapToOnsets(this, e); break;
            case "BPM from spacing": model.BpmFromSpacing(); break;
            case "Bar line here": model.StartBarHere(); break;
            case "Undo": model.Undo(); break;
            case "Redo": model.Redo(); break;
            case "Delete points": model.Delete(); break;
            case "Select all rows": TimingTable.SelectAll(); break;
            case "Copy rows": _ = CopySelectedRows(); break;
            case "Copy all timing": CopyTiming(this, e); break;
            case "Paste points": _ = PasteFromClipboard(false); break;
            case "Paste replacing": _ = PasteFromClipboard(true); break;
            case "Open beatmap": _ = Open(true); break;
            case "Open audio": _ = Open(false); break;
            case "Save beatmap": SaveOsu(this, e); break;
            case "Save project": SaveProject(this, e); break;
            case "Metronome": model.ClickEnabled = !model.ClickEnabled; break;
            case "Follow playhead": model.FollowPlayhead = !model.FollowPlayhead; break;
            case "Workspace tools": OpenWorkspaceTools(0); break;
            case "Keys": ShowKeys(this, e); break;
            case "Thai guide": ShowToolGuide(this, e); break;
            case "Play range": model.PlayRange(); break;
            case "Swap listening speed": model.SwapComparisonSpeed(); break;
            case "Tap BPM": model.TapBpm(); break;
            case "Mark anchor A": model.MarkAnchorA(); break;
            case "Mark anchor B": model.MarkAnchorB(); break;
            case "Previous issue": model.ReviewNext(-1); break;
            case "Next issue": model.ReviewNext(); break;
        }
    }
    private async void ShowPreferences(object? sender, RoutedEventArgs e)
    {
        var dialog = new Window { Title = "Keyboard shortcuts and copy profiles", Width = 650, Height = 620, MinHeight = 360,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Icon = Icon, DataContext = model };
        var keys = ToolPanel(); var fields = new Dictionary<string, TextBox>();
        Label(keys, "Change a shortcut using names such as Ctrl+P or Alt+R. Leave it empty to disable. Input fields retain their own navigation keys.");
        foreach (var command in EditorShortcuts.Defaults)
        {
            var row = new Grid { ColumnDefinitions = new("*,160"), ColumnSpacing = 12 };
            row.Children.Add(new TextBlock { Text = command.Key, VerticalAlignment = VerticalAlignment.Center });
            var field = new TextBox { Text = fileSettings.Shortcuts.GetValueOrDefault(command.Key, command.Value) };
            Grid.SetColumn(field, 1); row.Children.Add(field); keys.Children.Add(row); fields[command.Key] = field;
        }
        var copy = ToolPanel(); Label(copy, "Copy compensation in milliseconds. Default osu! profile: -33. Applies to exported clipboard rows only; internal paste and saved files keep their original times.");
        copy.Children.Add(Bound(new NumericUpDown { Minimum = -10000, Maximum = 10000, Increment = 1 }, NumericUpDown.ValueProperty, nameof(model.OsuCopyShiftMs), true));
        var profiles = Bound(new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch }, ItemsControl.ItemsSourceProperty, nameof(model.CopyProfiles)); copy.Children.Add(profiles);
        copy.Children.Add(ActionButton("Use profile", () => { if (profiles.SelectedItem is CopyProfile profile) model.OsuCopyShiftMs = profile.ShiftMs; }));
        var name = new TextBox { PlaceholderText = "New profile name" }; copy.Children.Add(name);
        copy.Children.Add(ActionButton("Save current value as profile", () => model.AddCopyProfile(name.Text ?? "")));
        var tabs = new TabControl { ItemsSource = new[] { new TabItem { Header = "Keyboard", Content = new ScrollViewer { Content = keys } }, new TabItem { Header = "Copy profiles", Content = new ScrollViewer { Content = copy } } } };
        var error = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap, Foreground = Avalonia.Media.Brushes.IndianRed };
        var footer = new StackPanel { Margin = new Thickness(16), Spacing = 8 }; footer.Children.Add(error);
        var buttons = new WrapPanel { ItemSpacing = 8 };
        buttons.Children.Add(ActionButton("Save preferences", () =>
        {
            var parsed = new HashSet<(Key, KeyModifiers)>(); var values = new Dictionary<string, string>();
            try
            {
                foreach (var field in fields)
                {
                    string value = field.Value.Text?.Trim() ?? "";
                    if (value.Length > 0)
                    {
                        var gesture = KeyGesture.Parse(value);
                        if (!parsed.Add((gesture.Key, gesture.KeyModifiers))) throw new ArgumentException("Duplicate shortcut: " + value);
                        if (gesture.Key is Key.Left or Key.Right or Key.Up or Key.Down or Key.Home or Key.End && gesture.KeyModifiers == KeyModifiers.None) throw new ArgumentException("Arrow/Home/End keys are reserved for transport navigation.");
                    }
                    values[field.Key] = value;
                }
                fileSettings = fileSettings with { Shortcuts = values }; SaveSettings(); dialog.Close();
            }
            catch (Exception ex) when (ex is FormatException or ArgumentException) { error.Text = ex.Message; }
        }));
        buttons.Children.Add(ActionButton("Default keys", () => { foreach (var field in fields) field.Value.Text = EditorShortcuts.Defaults[field.Key]; }));
        buttons.Children.Add(ActionButton("Close", () => { SaveSettings(); dialog.Close(); }));
        footer.Children.Add(buttons); var dock = new DockPanel(); DockPanel.SetDock(footer, Dock.Bottom); dock.Children.Add(footer); dock.Children.Add(tabs); dialog.Content = dock;
        CloseOnEscape(dialog); await dialog.ShowDialog(this);
    }
}
