using TimingAnalyzer.Editor;

namespace TimingAnalyzer.Editor.Tests;

/// <summary>Tempo ratios, ramp fill and section marks: the one-click fixes after auto-time.</summary>
public class AssistTests
{
    private static EditorViewModel NewModel() =>
        new(_ => Task.FromResult(AudioContent.Create(new double[120000], 1000)), _ => new ViewModelTests.FakePlayback());

    [Fact]
    public async Task TempoRatiosChangeTheBpmOfTheSelectedLinesAndKeepTheirTimes()
    {
        var vm = NewModel();
        await vm.LoadAsync(Path.GetTempFileName());
        vm.Edits.Replace([new TimingPoint(1000, 60000 / 128.0), new TimingPoint(30000, 60000 / 120.01), new TimingPoint(60000, 500)]);
        vm.SelectRows([0, 1], 0);
        vm.ScaleTempoCommand.Execute("2");
        Assert.Equal([256.0, 240.02, 120.0], vm.Edits.RedLines.Select(p => Math.Round(p.Bpm, 6)));
        Assert.Equal([1000.0, 30000, 60000], vm.Edits.RedLines.Select(p => p.Time));
        Assert.Equal([0, 1], vm.SelectedIndices);
        Assert.Equal("×2: 2 red lines rescaled. Ctrl+Z undoes.", vm.Status);
        vm.SelectRows([2], 2);
        vm.ScaleTempoCommand.Execute("0.6666666666666666");
        Assert.Equal(80, vm.Edits.RedLines[2].Bpm, 6);
        Assert.Contains("×⅔: red line at 60000 ms now 80 BPM", vm.Status);
        vm.Undo(); vm.Undo();
        Assert.Equal([128.0, 120.01, 120.0], vm.Edits.RedLines.Select(p => Math.Round(p.Bpm, 6)));
        vm.ClearSelection();
        vm.ScaleTempoCommand.Execute("2");
        Assert.Equal("Select the red lines to change first.", vm.Status);
    }

    [Fact]
    public void ARampHasARedLineOnEveryBeatMovingInAStraightLineAndLandsOnTheEnd()
    {
        // Parallel Universe Shifter's 111 -> 175 accelerando (68 748 to 95 771): the map writes 63 beats, one red line
        // each, 111 to 174 BPM at +1 per beat.
        var a = new TimingPoint(68748, 60000 / 111.0, Effects: 0);
        var b = new TimingPoint(95771, 60000 / 175.0);
        var ramp = RampFill.Between(a, b);
        Assert.Equal(63, ramp.Length); // The start line and 62 more, as the map.
        Assert.Equal(174, ramp[^1].Bpm, 0);
        Assert.Equal(68748, ramp[0].Time);
        Assert.All(ramp.Zip(ramp.Skip(1)), p => Assert.InRange(p.Second.Bpm - p.First.Bpm, 0.9, 1.1));
        Assert.All(ramp.Skip(1), p => Assert.True(p.OmitsFirstBarline));
        // Each line ends where the next starts, and the last beat ends on B (whole ms).
        for (int k = 0; k + 1 < ramp.Length; k++) Assert.InRange(ramp[k + 1].Time - (ramp[k].Time + ramp[k].BeatLength), -1, 1);
        Assert.InRange(b.Time - (ramp[^1].Time + ramp[^1].BeatLength), -1, 1);
        Assert.Throws<InvalidOperationException>(() => RampFill.Between(a, a with { Time = a.Time + 500 }));
    }

    [Fact]
    public async Task FillRampReplacesTheLinesBetweenTheSelectedOnesInOneUndoStep()
    {
        var vm = NewModel();
        await vm.LoadAsync(Path.GetTempFileName());
        vm.Edits.Replace([new TimingPoint(1000, 500), new TimingPoint(5000, 450), new TimingPoint(9000, 60000 / 150.0), new TimingPoint(20000, 300)]);
        vm.SelectRows([0, 2], 0);
        vm.FillRamp();
        var reds = vm.Edits.RedLines;
        Assert.Equal(1000, reds[0].Time);
        Assert.DoesNotContain(reds, p => p.Time == 5000 && Math.Abs(p.BeatLength - 450) < 1e-9); // Replaced by the ramp.
        Assert.Contains(reds, p => p.Time == 9000 && Math.Abs(p.Bpm - 150) < 1e-9);
        Assert.Equal(20000, reds[^1].Time);
        Assert.InRange(reds.Count(p => p.Time > 1000 && p.Time < 9000), 15, 20); // 8 s from 120 to 150 BPM: about 18 beats.
        Assert.StartsWith("Ramp 120 → 150 BPM:", vm.Status);
        vm.Undo();
        Assert.Equal([1000.0, 5000, 9000, 20000], vm.Edits.RedLines.Select(p => p.Time));
        vm.SelectRows([0], 0);
        vm.FillRamp();
        Assert.StartsWith("Select the red line where the ramp starts", vm.Status);
    }

    [Fact]
    public async Task CheckSectionsMarksAndSelectsTheSectionsWorthAListenAndForgetsThemOnAnEdit()
    {
        var vm = NewModel();
        await vm.LoadAsync(Path.GetTempFileName());
        vm.Edits.Replace([new TimingPoint(1000, 500), new TimingPoint(30000, 400), new TimingPoint(60000, 300)]);
        await vm.CheckSectionsAsync((_, reds) => Task.FromResult<IReadOnlyList<SectionMark?>>([null, new SectionMark(0, false, -45, null), new SectionMark(0, true, null, null)]));
        Assert.Equal([1, 2], vm.Marks.Select(m => m.Index));
        Assert.Equal([1, 2], vm.SelectedIndices);
        Assert.Equal("Check: 2 sections worth a listen (marked on the waveform, selected in the table): 30000 ms, fits better -45 ms; " +
            "60000 ms, few onsets on its beats.", vm.Status);
        vm.ShiftMsCommand.Execute("1");
        Assert.False(vm.HasMarks); // The timing changed: the marks no longer describe it.
        await vm.CheckSectionsAsync((_, reds) => Task.FromResult<IReadOnlyList<SectionMark?>>([null, null, null]));
        Assert.Equal("Check: nothing stands out; every section's grid sits on the onsets.", vm.Status);
        // The octave model's hint: a suggestion with the BPM it would give; nothing is changed by itself.
        var before = vm.Edits.RedLines.ToArray();
        await vm.CheckSectionsAsync((_, reds) => Task.FromResult<IReadOnlyList<SectionMark?>>([new SectionMark(0, false, null, null, 2), null, null]));
        Assert.Equal("Check: 1 section worth a listen (marked on the waveform, selected in the table): 1000 ms, maps usually write ×2 (240 BPM).", vm.Status);
        Assert.Equal(before, vm.Edits.RedLines);
    }
}
