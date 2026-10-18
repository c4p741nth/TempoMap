using TimingAnalyzer.Editor;

namespace TimingAnalyzer.Editor.Tests;

public class StepTimingTests
{
    /// <summary>The official Yooh - RPG timing (osu! Featured Artist; the Zelq, Alvearia and [Crz]xz1z1z sets agree).</summary>
    private static readonly (double Time, double Bpm)[] Official =
    [
        (8, 190), (116218, 170), (116747, 165), (117292, 155), (117679, 155), (174195, 150), (174995, 140), (175852, 130),
        (176775, 120), (178775, 110), (218047, 115), (220133, 120), (222133, 125), (224053, 130), (225899, 140), (227613, 150),
        (229213, 160), (252463, 150), (253263, 140), (254120, 120), (254620, 110), (255165, 210), (299736, 200), (300336, 190),
        (300967, 190), (302230, 200), (303430, 210), (304572, 220), (354753, 210), (355895, 200), (357095, 180), (357761, 150),
    ];

    private static TimingPoint[] Lines(params (double Time, double Bpm)[] lines) => lines.Select(l => new TimingPoint(l.Time, 60000 / l.Bpm)).ToArray();

    /// <summary>Each ramp of the official timing: its first step line to the next section's first line.</summary>
    public static TheoryData<double, double> Ramps() => new()
    {
        { 116218, 117679 }, // 190 → 170, 165 for 1.5 beats, 155 for 1 → 155
        { 174195, 178775 }, // 155 → 150, 140, 130 for 2 beats, 120 for 4 → 110
        { 218047, 229213 }, // 110 → 115 … 150 for 4 beats each → 160
        { 252463, 255165 }, // 160 → 150, 140 for 2 beats, 120, 110 for 1 → a jump to 210
        { 299736, 304572 }, // 210 → 200, 190 for 2 beats, 190, 200, 210 for 4 → 220 (a V)
        { 354753, 357761 }, // 220 → 210, 200 for 4 beats, 180 for 2 → 150 (the end)
    };

    [Theory]
    [MemberData(nameof(Ramps))]
    public void EveryOfficialRpgRampComesBackFromItsLinesAlone(double from, double to)
    {
        var official = Lines(Official);
        int first = Array.FindIndex(official, p => p.Time == from), last = Array.FindIndex(official, p => p.Time == to);
        // Placed as Ctrl+P places them (each with the BPM of the section before) and snapped (each line after the
        // first off by up to 3 ms); the next section's first line keeps its BPM.
        double[] noise = [0, 3, -2, 2, -3, 1, -1];
        var placed = official.ToArray();
        for (int i = first; i <= last; i++)
            placed[i] = new TimingPoint(official[i].Time + noise[(i - first) % noise.Length], i < last ? official[first - 1].BeatLength : official[i].BeatLength);
        var result = StepTiming.FromSpacing(placed, Enumerable.Range(first, last - first + 1));
        Assert.Equal(official.Select(p => p.Time), result.Lines.Select(p => p.Time));
        Assert.Equal(official.Select(p => Math.Round(p.Bpm, 9)), result.Lines.Select(p => Math.Round(p.Bpm, 9)));
        Assert.Equal(last - first, result.Steps.Count);
    }

    [Fact]
    public void OnlySelectedLinesWithANextLineChangeAndRunsRestartFromTheLineBefore()
    {
        var reds = Lines((0, 160), (252463, 160), (253263, 160), (254120, 160), (300000, 999));
        // Index 4 has no next line; index 2 alone counts against index 1's 160 BPM: 857 ms = 2 beats → 140.
        var result = StepTiming.FromSpacing(reds, [4, 2]);
        var step = Assert.Single(result.Steps);
        Assert.Equal((2, 140.0, 2.0), (step.Index, step.Bpm, step.Beats));
        Assert.Equal(0, result.Moved); // index 3 is not selected, so it stays where it is
        Assert.Equal(reds.Select(p => p.Time), result.Lines.Select(p => p.Time));
        Assert.Equal("140 (2 beats), 120 (1 beat)", StepTiming.Describe([step, new StepTiming.Step(3, 120, 1)]));
    }

    [Fact]
    public void ALineMoreThanSixteenBeatsFromTheNextIsASectionAndKeepsItsBpm()
    {
        // RPG 174 s with the next section's first line (178775@110, 72 beats to 218047) caught by the selection.
        var reds = Lines((0, 155), (174195, 155), (174995, 155), (175852, 155), (176775, 155), (178775, 110), (218047, 115));
        var result = StepTiming.FromSpacing(reds, Enumerable.Range(1, 5));
        Assert.Equal(new[] { 1, 2, 3, 4 }, result.Steps.Select(s => s.Index));
        Assert.Equal(new double[] { 150, 140, 130, 120 }, result.Steps.Select(s => s.Bpm));
        Assert.Equal(110, Math.Round(result.Lines[5].Bpm, 9));
        // 16 beats is still a step: 8000 ms at 120.
        Assert.Equal(120, Assert.Single(StepTiming.FromSpacing(Lines((0, 120), (1000, 120), (9000, 120)), [1]).Steps).Bpm);
    }

    [Fact]
    public void ABpmRoundsToAMultipleOfFiveWithinTenMsThenAWholeBpmWithinFour()
    {
        // Four separate steps, each counted against a 120 BPM line before it.
        var reds = Lines((0, 120), (1000, 120), (2009, 120), (5000, 120), (6012, 120), (10000, 120), (18030, 120),
            (20000, 120), (21008, 120), (40000, 120));
        var steps = StepTiming.FromSpacing(reds, [1, 3, 5, 7]).Steps;
        // 2 beats in 1009 ms = 118.93: 120 ends 9 ms early. 2 in 1012 = 118.58: 120 is 12 ms off, 119 is 3.6.
        // 16 in 8030 = 119.552: 120 is 30 ms off, so the decimals stay. 2 in 1008 = 119.05: 120 is 8 ms off.
        Assert.Equal(new double[] { 120, 119, 119.552, 120 }, steps.Select(s => s.Bpm));
    }

    [Fact]
    public void ChainedLinesAreTruncatedToWholeMsAsInTheOfficialTiming()
    {
        // 218047 + 4 × 521.739 = 220133.96: the official line is 220133 (the placed line is 3 ms late).
        var result = StepTiming.FromSpacing(Lines((0, 110), (218047, 110), (220136, 110)), [1, 2]);
        Assert.Equal(115, result.Steps[0].Bpm);
        Assert.Equal(220133, result.Lines[2].Time);
        Assert.Equal(1, result.Moved);
    }

    private static async Task<(EditorViewModel Vm, string Path)> Loaded(params TimingPoint[] reds)
    {
        string path = Path.GetTempFileName();
        var vm = new EditorViewModel(_ => Task.FromResult(AudioContent.Create(new double[400000], 1000)), _ => new ViewModelTests.FakePlayback());
        await vm.LoadAsync(path);
        vm.Edits.Replace(reds);
        return (vm, path);
    }

    [Fact]
    public async Task ViewModelSetsStepBpmsAndChainsInOneUndoStep()
    {
        // Snapped lines of the 252 s ramp: 6.5 ms early on average, ±1.5 ms apart.
        var reds = Lines((0, 160), (252456, 160), (253258, 160), (254112, 160), (254615, 160), (255160, 210));
        var (vm, path) = await Loaded(reds);
        using (vm)
            try
            {
                // The selection also catches 255160@210, the last red line: it is chained but keeps its BPM.
                vm.SelectRange(252000, 255200, false);
                vm.BpmFromSpacingCommand.Execute(null);
                Assert.Equal(new double[] { 160, 150, 140, 120, 110, 210 }, vm.Edits.RedLines.Select(p => Math.Round(p.Bpm, 3)));
                Assert.Equal(new double[] { 0, 252456, 253256, 254113, 254613, 255158 }, vm.Edits.RedLines.Select(p => p.Time));
                Assert.Equal(new[] { 1, 2, 3, 4, 5 }, vm.SelectedIndices);
                Assert.Contains("150 (2 beats), 140 (2 beats), 120 (1 beat), 110 (1 beat); chained 4 lines onto the end of the step before (up to 2 ms); 1 line keeps its BPM",
                    vm.Status, StringComparison.Ordinal);
                vm.Undo();
                Assert.Equal(reds, vm.Edits.RedLines);
                vm.ClearSelection();
                vm.BpmFromSpacingCommand.Execute(null);
                Assert.Contains("Select the red lines", vm.Status, StringComparison.Ordinal);
            }
            finally { File.Delete(path); }
    }

    [Fact]
    public async Task SnapToOnsetsMovesSelectedLinesOntoFoundOnsetsInOneUndoStep()
    {
        var reds = Lines((0, 160), (252470, 160), (253250, 160), (254100, 160), (254640, 160), (255165, 210));
        var (vm, path) = await Loaded(reds);
        using (vm)
            try
            {
                List<OnsetQuery>? asked = null;
                // Onsets near three lines; none near the fourth.
                Task<IReadOnlyList<double?>> Find(AudioContent _, IReadOnlyList<OnsetQuery> q)
                {
                    asked = q.ToList();
                    return Task.FromResult<IReadOnlyList<double?>>([252455.6, 253262.8, 254119.6, null]);
                }
                vm.SelectRange(252000, 254700, false);
                await vm.SnapToOnsetsAsync(Find);
                Assert.Equal(new double[] { 252470, 253250, 254100, 254640 }, asked!.Select(q => q.TimeMs));
                Assert.All(asked!, q => Assert.Equal(60, q.WindowMs)); // ¼ of a 375 ms beat is over 60 ms.
                Assert.Equal(new double[] { 0, 252456, 253263, 254120, 254640, 255165 }, vm.Edits.RedLines.Select(p => p.Time));
                Assert.Contains("Snapped 3 of 4", vm.Status, StringComparison.Ordinal);
                Assert.Contains("1 found no onset", vm.Status, StringComparison.Ordinal);
                Assert.Equal(new[] { 1, 2, 3, 4 }, vm.SelectedIndices);
                vm.Undo();
                Assert.Equal(reds, vm.Edits.RedLines);
                // An onset past the next red line is not taken.
                vm.SelectRange(252000, 252500, false);
                await vm.SnapToOnsetsAsync((_, _) => Task.FromResult<IReadOnlyList<double?>>([253300.0]));
                Assert.Equal(252470, vm.Edits.RedLines[1].Time);
                Assert.Contains("would cross a neighbour", vm.Status, StringComparison.Ordinal);
            }
            finally { File.Delete(path); }
    }
}
