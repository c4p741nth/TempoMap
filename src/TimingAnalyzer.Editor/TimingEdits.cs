namespace TimingAnalyzer.Editor;

public enum RedLineScope { All, Selected }

public sealed class UndoStack
{
    private readonly Stack<(TimingPoint[] Before, TimingPoint[] After)> undo = new(), redo = new();
    public bool CanUndo => undo.Count > 0;
    public bool CanRedo => redo.Count > 0;
    /// <summary>Changes whenever a step is recorded, undone or redone (not when one is extended), so a caller can tell that
    /// nothing else was edited since the step it extends.</summary>
    public int Version { get; private set; }
    public void Record(TimingPoint[] before, TimingPoint[] after)
    {
        undo.Push((before.ToArray(), after.ToArray()));
        redo.Clear();
        Version++;
    }
    /// <summary>Makes the last recorded step end at <paramref name="after"/> instead: one undo reverts a run of small edits.</summary>
    public void ExtendLast(TimingPoint[] after)
    {
        var last = undo.Pop();
        undo.Push((last.Before, after.ToArray()));
        redo.Clear();
    }
    public TimingPoint[] Undo() { var edit = undo.Pop(); redo.Push(edit); Version++; return edit.Before.ToArray(); }
    public TimingPoint[] Redo() { var edit = redo.Pop(); undo.Push(edit); Version++; return edit.After.ToArray(); }
}

public sealed class TimingEdits
{
    private TimingPoint[] points;
    private TimingPoint[] greens;
    public IReadOnlyList<TimingPoint> RedLines => Array.AsReadOnly(points);
    public IReadOnlyList<TimingPoint> GreenLines => Array.AsReadOnly(greens);
    public UndoStack History { get; } = new();
    public TimingEdits(IEnumerable<TimingPoint> redLines, IEnumerable<TimingPoint>? greenLines = null)
    {
        points = BeatGrid.Sort(redLines);
        greens = SortGreens(greenLines ?? []);
    }
    private static TimingPoint[] SortGreens(IEnumerable<TimingPoint> rows) => rows.Select(p => p.Validate())
        .Where(p => !p.Uninherited).OrderBy(p => p.Time).ToArray();
    private TimingPoint[] State() => points.Concat(greens).ToArray();
    public event Action? Changed;

    public void Replace(IEnumerable<TimingPoint> redLines)
    {
        var after = BeatGrid.Sort(redLines);
        if (points.SequenceEqual(after)) return;
        History.Record(State(), after.Concat(greens).ToArray());
        points = after;
        Changed?.Invoke();
    }
    /// <summary>
    /// <see cref="Replace"/>, except that when <paramref name="continuing"/> is true (the last undo step is the one this
    /// call carries on) that step is extended, so a held BPM nudge is one undo step, not hundreds.
    /// </summary>
    public void ReplaceMerging(IEnumerable<TimingPoint> redLines, bool continuing)
    {
        var after = BeatGrid.Sort(redLines);
        if (points.SequenceEqual(after)) return;
        if (continuing && History.CanUndo) History.ExtendLast(after.Concat(greens).ToArray());
        else History.Record(State(), after.Concat(greens).ToArray());
        points = after;
        Changed?.Invoke();
    }
    public void Update(int index, TimingPoint value) => Replace(points.Select((p, i) => i == index ? value : p));
    public void ReplaceTiming(IEnumerable<TimingPoint> redLines, IEnumerable<TimingPoint> greenLines)
    {
        var after = BeatGrid.Sort(redLines);
        var afterGreens = SortGreens(greenLines);
        if (points.SequenceEqual(after) && greens.SequenceEqual(afterGreens)) return;
        History.Record(State(), after.Concat(afterGreens).ToArray());
        points = after; greens = afterGreens;
        Changed?.Invoke();
    }
    // Edited fields are rounded to osu! precision (integer ms, BPM to 3 decimals); untouched fields stay exact.
    public void MoveRedLine(int index, double time)
    {
        time = OsuPrecision.Time(time);
        if (time != points[index].Time && points.Where((_, i) => i != index).Any(p => p.Time == time))
            throw new ArgumentException("Moving a red line cannot overlap a neighbour. Paste to create a duplicate.");
        Update(index, points[index] with { Time = time });
    }
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
    /// <summary>Beat shifts are 1/n of each line's own beat for these n (powers of two, so the fractions are exact).</summary>
    public static IReadOnlyList<int> ShiftDivisors { get; } = [1, 2, 4, 8, 16, 32, 64];
    public void ShiftRedLines(RedLineScope scope, double beats, int selectedIndex = -1) =>
        ShiftRedLines(scope, beats, selectedIndex < 0 ? [] : [selectedIndex]);
    public void ShiftRedLines(RedLineScope scope, double beats, IReadOnlyCollection<int> selected)
    {
        if (!ShiftDivisors.Any(divisor => divisor == 1 / Math.Abs(beats)))
            throw new ArgumentOutOfRangeException(nameof(beats), "Choose a 1, 1/2, 1/4, 1/8, 1/16, 1/32 or 1/64 beat shift.");
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
            if (after[i].Time < after[i - 1].Time ||
                after[i].Time == after[i - 1].Time && points[i].Time != points[i - 1].Time)
                throw new InvalidOperationException((scope == RedLineScope.All ? "Shifted" : "Selected") +
                    " red lines cannot cross or overlap a neighbouring red line.");
        Replace(after);
    }
    private void Restore(TimingPoint[] state)
    {
        points = BeatGrid.Sort(state.Where(p => p.Uninherited));
        greens = SortGreens(state.Where(p => !p.Uninherited));
        Changed?.Invoke();
    }
    public void Undo() { if (History.CanUndo) Restore(History.Undo()); }
    public void Redo() { if (History.CanRedo) Restore(History.Redo()); }
}
