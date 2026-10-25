namespace TimingAnalyzer.Editor;

public sealed record CopyProfile(string Name, int ShiftMs)
{
    public override string ToString() => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{Name} ({ShiftMs:+0;-0;0} ms)");
}
public static class EditorShortcuts
{
    public static IReadOnlyDictionary<string, string> Defaults { get; } = new Dictionary<string, string>
    {
        ["Play / Pause"] = "Space", ["Add red point"] = "Ctrl+P", ["Add green point"] = "Ctrl+G", ["Move point here"] = "Ctrl+Shift+P",
        ["Auto-time"] = "Ctrl+T", ["Apply proposal"] = "Ctrl+Enter", ["Auto-time range"] = "Ctrl+R", ["Check sections"] = "Ctrl+K",
        ["Fill ramp"] = "Ctrl+L", ["Fit offset"] = "Ctrl+Alt+F", ["Snap to onsets"] = "Ctrl+Alt+S", ["BPM from spacing"] = "Ctrl+Alt+B", ["Bar line here"] = "Ctrl+Alt+L",
        ["Undo"] = "Ctrl+Z", ["Redo"] = "Ctrl+Y", ["Delete points"] = "Delete", ["Select all rows"] = "Ctrl+A",
        ["Copy rows"] = "Ctrl+C", ["Copy all timing"] = "Ctrl+Shift+C", ["Paste points"] = "Ctrl+V", ["Paste replacing"] = "Ctrl+Shift+V",
        ["Open beatmap"] = "Ctrl+O", ["Open audio"] = "Ctrl+Shift+O", ["Save beatmap"] = "Ctrl+S", ["Save project"] = "Ctrl+Shift+S",
        ["Metronome"] = "M", ["Follow playhead"] = "F", ["Workspace tools"] = "F3", ["Keys"] = "F1", ["Thai guide"] = "F2",
        ["Play range"] = "Ctrl+Space", ["Swap listening speed"] = "Back", ["Tap BPM"] = "T", ["Mark anchor A"] = "Alt+A", ["Mark anchor B"] = "Alt+B",
        ["Previous issue"] = "Alt+Left", ["Next issue"] = "Alt+Right",
        ["Shift earlier by beat step"] = "Ctrl+Alt+Left", ["Shift later by beat step"] = "Ctrl+Alt+Right"
    };
}

public sealed partial class EditorViewModel
{
    private int osuCopyShiftMs = OsuTimingExport.ShiftMs;
    private CopyProfile[] copyProfiles = [new("osu!", -33), new("Unshifted", 0)];
    public int OsuCopyShiftMs { get => osuCopyShiftMs; set { if (value is >= -10000 and <= 10000) SetProperty(ref osuCopyShiftMs, value); } }
    public IReadOnlyList<CopyProfile> CopyProfiles => copyProfiles;
    public void AddCopyProfile(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 40) { Status = "Enter a profile name (1–40 characters)."; return; }
        copyProfiles = copyProfiles.Where(p => !string.Equals(p.Name, name.Trim(), StringComparison.OrdinalIgnoreCase)).Append(new(name.Trim(), OsuCopyShiftMs)).TakeLast(20).ToArray();
        OnPropertyChanged(nameof(CopyProfiles));
    }
    public bool CanUndo => Edits.History.CanUndo;
    public bool CanRedo => Edits.History.CanRedo;
    public string UndoLabel => "_Undo" + (CanUndo ? " " + Edits.History.UndoLabel : "");
    public string RedoLabel => "_Redo" + (CanRedo ? " " + Edits.History.RedoLabel : "");
    private void NotifyHistory()
    {
        OnPropertyChanged(nameof(CanUndo)); OnPropertyChanged(nameof(CanRedo)); OnPropertyChanged(nameof(UndoLabel)); OnPropertyChanged(nameof(RedoLabel));
        UndoCommand.NotifyCanExecuteChanged(); RedoCommand.NotifyCanExecuteChanged();
    }
}
