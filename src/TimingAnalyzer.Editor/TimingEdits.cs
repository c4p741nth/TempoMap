namespace TimingAnalyzer.Editor;

public enum RedLineScope { All, Selected }

public sealed class UndoStack
{
    private readonly Stack<(TimingPoint[] Before, TimingPoint[] After)> undo = new(), redo = new();
    public bool CanUndo => undo.Count > 0;
    public bool CanRedo => redo.Count > 0;
    public void Record(TimingPoint[] before, TimingPoint[] after)
    {
        undo.Push((before.ToArray(), after.ToArray()));
        redo.Clear();
    }
    public TimingPoint[] Undo() { var edit = undo.Pop(); redo.Push(edit); return edit.Before.ToArray(); }
    public TimingPoint[] Redo() { var edit = redo.Pop(); undo.Push(edit); return edit.After.ToArray(); }
}

public sealed class TimingEdits
{
    private TimingPoint[] points;
    public IReadOnlyList<TimingPoint> RedLines => Array.AsReadOnly(points);
    public UndoStack History { get; } = new();
    public TimingEdits(IEnumerable<TimingPoint> redLines) => points = BeatGrid.Sort(redLines);
    public event Action? Changed;

    public void Replace(IEnumerable<TimingPoint> redLines)
    {
        var after = BeatGrid.Sort(redLines);
        if (points.SequenceEqual(after)) return;
        History.Record(points, after);
        points = after;
        Changed?.Invoke();
    }
    public void Update(int index, TimingPoint value) => Replace(points.Select((p, i) => i == index ? value : p));
    // Edited fields are rounded to osu! precision (integer ms, BPM to 3 decimals); untouched fields stay exact.
    public void MoveRedLine(int index, double time) => Update(index, points[index] with { Time = OsuPrecision.Time(time) });
    public void StretchRedLine(int index, double beat, double targetTime)
    {
        if (!double.IsFinite(beat) || beat <= 0 || targetTime <= points[index].Time)
            throw new ArgumentOutOfRangeException(nameof(beat));
        Update(index, points[index] with { BeatLength = OsuPrecision.BeatLength((targetTime - points[index].Time) / beat) });
    }
    public void AddRedLine(double time)
    {
        time = OsuPrecision.Time(time);
        var source = points.LastOrDefault(p => p.Time <= time) ?? points.FirstOrDefault() ?? new TimingPoint(0, 500);
        Replace(points.Append(source with { Time = time }));
    }
    /// <summary>Moves a red line to a fitted beat, optionally setting its beat length; it may not cross a neighbour.</summary>
    public void Retime(int index, double time, double? beatLength = null)
    {
        time = OsuPrecision.Time(time);
        if (index > 0 && time <= points[index - 1].Time || index + 1 < points.Length && time >= points[index + 1].Time)
            throw new InvalidOperationException("The fitted red line would cross or touch a neighbouring red line.");
        Update(index, points[index] with
        {
            Time = time, BeatLength = beatLength is { } length ? OsuPrecision.BeatLength(length) : points[index].BeatLength
        });
    }
    public void Delete(int index) => Replace(points.Where((_, i) => i != index));
    public void Delete(IEnumerable<int> indices)
    {
        var remove = indices.ToHashSet();
        Replace(points.Where((_, i) => !remove.Contains(i)));
    }
    public void ShiftRedLines(RedLineScope scope, double beats, int selectedIndex = -1) =>
        ShiftRedLines(scope, beats, selectedIndex < 0 ? [] : [selectedIndex]);
    public void ShiftRedLines(RedLineScope scope, double beats, IReadOnlyCollection<int> selected)
    {
        if (beats is not (-1 or -0.5 or -0.25 or -0.125 or 0.125 or 0.25 or 0.5 or 1))
            throw new ArgumentOutOfRangeException(nameof(beats), "Choose an eighth, quarter, half or whole beat shift.");
        Shift(scope, point => beats * point.BeatLength, selected);
    }
    public void ShiftRedLinesMs(RedLineScope scope, double ms, int selectedIndex = -1) =>
        ShiftRedLinesMs(scope, ms, selectedIndex < 0 ? [] : [selectedIndex]);
    public void ShiftRedLinesMs(RedLineScope scope, double ms, IReadOnlyCollection<int> selected)
    {
        if (!double.IsFinite(ms)) throw new ArgumentOutOfRangeException(nameof(ms));
        Shift(scope, _ => ms, selected);
    }
    private void Shift(RedLineScope scope, Func<TimingPoint, double> delta, IReadOnlyCollection<int> selected)
    {
        if (!Enum.IsDefined(scope)) throw new ArgumentOutOfRangeException(nameof(scope));
        if (scope == RedLineScope.Selected && (selected.Count == 0 || selected.Any(i => i < 0 || i >= points.Length)))
            throw new InvalidOperationException("Select a red line before shifting it.");
        var moved = scope == RedLineScope.All ? Enumerable.Range(0, points.Length).ToHashSet() : selected.ToHashSet();
        var after = points.Select((point, index) => moved.Contains(index)
            ? point with { Time = OsuPrecision.Time(point.Time + delta(point)) } : point).ToArray();
        // Order must be kept, so indices (and the selection) stay valid. Green lines are never included.
        for (int i = 1; i < after.Length; i++)
            if (after[i].Time <= after[i - 1].Time)
                throw new InvalidOperationException((scope == RedLineScope.All ? "Shifted" : "Selected") +
                    " red lines cannot cross or overlap a neighbouring red line.");
        Replace(after);
    }
    public void Undo() { if (History.CanUndo) { points = History.Undo(); Changed?.Invoke(); } }
    public void Redo() { if (History.CanRedo) { points = History.Redo(); Changed?.Invoke(); } }
}
