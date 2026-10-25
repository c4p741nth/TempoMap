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
                if (ToolTip.GetTip(control) is string tip && tip.Length > 170) ToolTip.SetTip(control, ShortTip(tip));
                if (control is Button or ComboBox or Slider or TextBox && string.IsNullOrEmpty(AutomationProperties.GetName(control)))
                    AutomationProperties.SetName(control, ToolTip.GetTip(control) as string ?? control.Name ?? (control as Button)?.Content?.ToString() ?? "Editor control");
            }
        };
    }
    /// <summary>
    /// A long tip cut after its last whole sentence that fits in 160 characters, else at a word. Most tips open with their key
    /// ("Ctrl+T. Analyze…"), so a cut at the first sentence would leave only the key; a cut that short counts as no sentence.
    /// </summary>
    public static string ShortTip(string tip)
    {
        int end = tip.LastIndexOf(". ", 160, StringComparison.Ordinal);
        if (end >= 40) return tip[..(end + 1)];
        int space = tip.LastIndexOf(' ', 150);
        return tip[..(space > 0 ? space : 150)] + "… (F2: guide)";
    }
    private void CancelAnalysis(object? sender, RoutedEventArgs e) => model.CancelAnalysis();
}
