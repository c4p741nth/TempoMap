using TimingAnalyzer.Editor;

namespace TimingAnalyzer.Editor.Tests;

/// <summary>The highlighted range (Shift+←/→, Shift+drag), Auto-time range, and pasting timing points.</summary>
public class RangeTests
{
    private static EditorViewModel NewModel() =>
        new(_ => Task.FromResult(AudioContent.Create(new double[60000], 1000)), _ => new ViewModelTests.FakePlayback());

    // The user's timing for a crowd of rebellion - Ill, with Auto-time's 189.3 BPM third section.
    private static readonly TimingPoint[] Ill =
    [
        new(455, 60000 / 284.0), new(57496, 60000 / 220.0), new(131888, 60000 / 189.322), new(154707, 60000 / 202.0),
        new(156489, 60000 / 209.0), new(158798, 60000 / 220.0),
    ];

    [Fact]
    public void ARangeReplacesTheLinesInsideAndTheMusicAfterKeepsItsTiming()
    {
        // Range 131 000-169 000 re-analyzed as one 142 BPM line: the four lines inside go, and the 220 BPM line in
        // effect at the end continues from its first beat at or after 169 000 (158 798 + 38 beats of 272.727 ms = 169 161.6).
        var (lines, continuation) = RangeEdit.Replace(Ill, 131000, 169000, [new TimingPoint(131678.4, 60000 / 142.0)]);
        Assert.Equal([455.0, 57496, 131678, 169162], lines.Select(p => p.Time));
        Assert.Equal(169162, continuation!.Time);
        Assert.Equal(220, Math.Round(lines[3].Bpm, 6));
        // A line the analysis puts outside the range is not used.
        var (only, _) = RangeEdit.Replace(Ill, 131000, 169000, [new TimingPoint(125000, 500), new TimingPoint(140000, 60000 / 142.0)]);
        Assert.DoesNotContain(only, p => p.Time == 125000);
        // No copy when it would hold less than a beat (209 BPM from 158 786, the next line at 158 798), or the new
        // line already has the same grid.
        var (beforeNext, none) = RangeEdit.Replace(Ill, 131000, 158500, [new TimingPoint(131678, 60000 / 142.0)]);
        Assert.Null(none);
        Assert.Equal([455.0, 57496, 131678, 158798], beforeNext.Select(p => p.Time));
        var (sameGrid, noCopy) = RangeEdit.Replace(Ill, 100000, 120000, [new TimingPoint(100314, 60000 / 220.0)]);
        Assert.Null(noCopy);
        Assert.Equal([455.0, 57496, 100314, 131888, 154707, 156489, 158798], sameGrid.Select(p => p.Time));
        Assert.Throws<InvalidOperationException>(() => RangeEdit.Replace(Ill, 131000, 169000, []));
    }

    [Fact]
    public async Task ShiftArrowsHighlightARangeByTheBeatSnapAndAutoTimeRangeRetimesIt()
    {
        var vm = NewModel();
        await vm.LoadAsync(Path.GetTempFileName());
        vm.Edits.Replace([new TimingPoint(1000, 500), new TimingPoint(20000, 400)]);
        vm.SelectedSnap = vm.SnapOptions.First(o => o.Divisor == 1);
        vm.Seek(3000);
        for (int i = 0; i < 12; i++) vm.ExtendRange(1);
        Assert.Equal((3000.0, 9000.0), vm.Range);
        vm.ExtendRange(-1); vm.ExtendRange(-1);
        Assert.Equal((3000.0, 8000.0), vm.Range); // Shrinks back toward where it started.
        Assert.Equal(8000, vm.PositionMs);
        for (int i = 0; i < 12; i++) vm.ExtendRange(-1);
        Assert.Equal((2000.0, 3000.0), vm.Range); // Past the start it grows the other way.

        vm.SetRange(15000, 5000);
        Assert.Equal("00:05:000 – 00:15:000 (10.0 s)", vm.RangeLabel);
        RangeRequest? asked = null;
        await vm.AutoTimeRangeAsync((_, request) =>
        {
            asked = request;
            return Task.FromResult(new MultiTimingDetection([new TimingPoint(5210.4, 60000 / 142.0), new TimingPoint(11000, 60000 / 150.0)]));
        });
        Assert.Equal(new RangeRequest(5000, 15000, null, 0), asked);
        Assert.Equal([1000.0, 5210, 11000, 15000, 20000], vm.Edits.RedLines.Select(p => p.Time));
        Assert.Equal(120, vm.Edits.RedLines[3].Bpm, 6); // The 1000 ms line's grid continues after the range.
        Assert.Equal([1, 2], vm.SelectedIndices);
        Assert.Contains("2 red lines, 142 → 150 BPM", vm.Status);
        Assert.Contains("the timing after the range continues from 15000 ms", vm.Status);
        vm.Undo();
        Assert.Equal([1000.0, 20000], vm.Edits.RedLines.Select(p => p.Time));

        // The BPM box and drift mode go with the request; a short range or no range is refused.
        vm.LockedBpmText = "142";
        await vm.AutoTimeRangeAsync((_, request) => { asked = request; return Task.FromResult(new MultiTimingDetection([new TimingPoint(5200, 60000 / 142.0)])); });
        Assert.Equal(142.0, asked!.Bpm);
        vm.LockedBpmText = ""; vm.SelectedAutoMode = "Drift 4 beats";
        await vm.AutoTimeRangeAsync((_, request) => { asked = request; return Task.FromResult(new MultiTimingDetection([new TimingPoint(5200, 500)])); });
        Assert.Equal(4, asked!.DriftBeats);
        vm.SetRange(5000, 8000);
        await vm.AutoTimeRangeAsync((_, _) => throw new InvalidOperationException("not called"));
        Assert.Contains("too short", vm.Status);
        vm.Escape();
        Assert.False(vm.HasRange);
        await vm.AutoTimeRangeAsync((_, _) => throw new InvalidOperationException("not called"));
        Assert.StartsWith("Highlight a range first", vm.Status);
    }

    [Fact]
    public async Task PastedRedAndGreenRowsJoinTheTimingIncludingOverlaps()
    {
        var vm = NewModel();
        await vm.LoadAsync(Path.GetTempFileName());
        vm.Edits.Replace([new TimingPoint(1000, 500), new TimingPoint(5000, 400)]);
        // The user's Ill rows, plus a green row and a header.
        string text = "[TimingPoints]\r\n455,211.26760563380282,4,1,0,100,1,0\r\n5000,272.72727272727275,4,1,0,100,1,0\r\n" +
            "6000,-50,4,1,0,100,0,0\r\n";
        vm.Paste(text);
        Assert.Equal([455.0, 1000, 5000, 5000], vm.Edits.RedLines.Select(p => p.Time));
        Assert.Equal(150, vm.Edits.RedLines[2].Bpm, 6); // Original first, pasted duplicate second.
        Assert.Equal(220, vm.Edits.RedLines[3].Bpm, 6);
        Assert.Equal([0, 3], vm.SelectedIndices);
        Assert.Contains("overlaps retained", vm.Status);
        Assert.Contains("and 1 green line", vm.Status);
        Assert.Contains(vm.InheritedRows, r => r.Point.Time == 6000 && Math.Abs(r.Point.Sv - 2) < 1e-8);
        vm.Undo();
        Assert.Equal([1000.0, 5000], vm.Edits.RedLines.Select(p => p.Time));
        vm.Paste(text, replaceAll: true);
        Assert.Equal([455.0, 5000], vm.Edits.RedLines.Select(p => p.Time));
        Assert.Contains("replacing all 2 red lines", vm.Status);
        vm.Paste("hello");
        Assert.Contains("A timing point needs time and beat length", vm.Status);
        vm.Paste("6000,-50,4,1,0,100,0,0");
        Assert.Contains("and 1 green line", vm.Status);
        Assert.Equal(2, vm.InheritedRows.Count(r => r.Point.Time == 6000));
    }
}
