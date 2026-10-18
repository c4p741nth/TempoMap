using TimingAnalyzer.Editor;

namespace TimingAnalyzer.Editor.Tests;

public class SectionTimingTests
{
    private sealed class FakeDetector
    {
        public List<TimingRequest> Requests { get; } = [];
        public Func<TimingRequest, TimingDetection> Result { get; set; } = r => new(r.Bpm ?? 150, r.StartMs + 13.4, null, 0, null);
        public Task<TimingDetection> Detect(AudioContent audio, TimingRequest request)
        {
            Requests.Add(request);
            return Task.FromResult(Result(request));
        }
    }

    private static async Task<(EditorViewModel Vm, string Path)> Loaded(params TimingPoint[] reds)
    {
        string path = Path.GetTempFileName();
        var vm = new EditorViewModel(_ => Task.FromResult(AudioContent.Create(new double[20000], 1000)), _ => new ViewModelTests.FakePlayback());
        await vm.LoadAsync(path);
        vm.Edits.Replace(reds);
        return (vm, path);
    }

    [Theory]
    [InlineData(1000, 1003)]
    [InlineData(900, 1003)]
    [InlineData(1240, 1003)]
    [InlineData(1260, 1503)]
    [InlineData(700, 503)]
    public void PlaceSnapsToTheNearestDetectedBeatInWholeMs(double current, double expected) =>
        Assert.Equal(expected, SectionFit.Place(current, 1003.2, 120));

    [Theory]
    [InlineData(46, 375, false)]
    [InlineData(-47, 375, true)]
    [InlineData(94, 375, true)]
    public void MovesBeyondAnEighthBeatAreFlagged(double moved, double beatLength, bool expected) =>
        Assert.Equal(expected, SectionFit.IsSuspicious(moved, beatLength));

    [Fact]
    public void RetimeKeepsOrSetsBeatLengthAndNeverCrossesANeighbour()
    {
        var edits = new TimingEdits([new(0, 500), new(5000, 400.0123), new(9000, 500)]);
        edits.Retime(1, 5013.4);
        Assert.Equal(new TimingPoint(5013, 400.0123), edits.RedLines[1]); // Beat length untouched.
        edits.Retime(1, 4805.2, 60000 / 149.997);
        Assert.Equal(new TimingPoint(4805, 60000 / 149.997), edits.RedLines[1]);
        foreach (double bad in new[] { 0.0, -10, 9000, 9500 })
            Assert.Contains("neighbouring", Assert.Throws<InvalidOperationException>(() => edits.Retime(1, bad)).Message, StringComparison.Ordinal);
        Assert.Equal(new TimingPoint(4805, 60000 / 149.997), edits.RedLines[1]);
        edits.Undo(); edits.Undo();
        Assert.Equal(new TimingPoint(5000, 400.0123), edits.RedLines[1]);
    }

    [Fact]
    public async Task WholeSongAutoTimeUsesTheBpmBoxAndRejectsBadInput()
    {
        var (vm, path) = await Loaded(new TimingPoint(0, 500));
        using (vm)
            try
            {
                var detector = new FakeDetector { Result = r => new(r.Bpm ?? 159.9968637393969, 352, null, r.Bpm is null ? 0 : null, null) };
                await vm.AnalyzeAsync(detector.Detect);
                Assert.Equal(new TimingRequest(0, 20000, null), detector.Requests[^1]);
                Assert.False(vm.Proposal!.Locked);
                Assert.StartsWith("Exact BPM: 159.997", vm.ProposalDescription, StringComparison.Ordinal);
                vm.LockedBpmText = " 160 ";
                await vm.AnalyzeAsync(detector.Detect);
                Assert.Equal(new TimingRequest(0, 20000, 160), detector.Requests[^1]);
                Assert.True(vm.Proposal!.Locked);
                Assert.StartsWith("Locked BPM: 160; osu! offset: 352 ms", vm.ProposalDescription, StringComparison.Ordinal);
                vm.ApplyProposal(true);
                Assert.Equal(new TimingPoint(352, 375), Assert.Single(vm.Edits.RedLines));
                vm.LockedBpmText = "159.99964";
                await vm.AnalyzeAsync(detector.Detect);
                Assert.Equal(160, detector.Requests[^1].Bpm); // Rounded to osu! precision before analysis.
                int calls = detector.Requests.Count;
                foreach (string bad in new[] { "abc", "0", "-5", "1001", "NaN" })
                {
                    vm.LockedBpmText = bad;
                    await vm.AnalyzeAsync(detector.Detect);
                    Assert.Contains("BPM box", vm.Status, StringComparison.Ordinal);
                }
                Assert.Equal(calls, detector.Requests.Count); // Bad input never reaches the detector.
            }
            finally { File.Delete(path); }
    }

    [Fact]
    public async Task FitOffsetKeepsTheLinesExactBpmAndAnalyzesOnlyItsSection()
    {
        var (vm, path) = await Loaded(new(0, 500), new(5000, 400.0123), new(12000, 500));
        using (vm)
            try
            {
                var detector = new FakeDetector();
                vm.LockedBpmText = "170"; // Ignored: Fit offset keeps the line's own BPM.
                vm.Select(1);
                await vm.FitOffsetAsync(detector.Detect);
                var request = Assert.Single(detector.Requests);
                Assert.Equal(new TimingRequest(5000, 12000, 60000 / 400.0123), request);
                Assert.Equal(new TimingPoint(5013, 400.0123), vm.Edits.RedLines[1]);
                Assert.Equal(1, vm.SelectedRow!.Index);
                Assert.Contains("(kept)", vm.Status, StringComparison.Ordinal);
                Assert.Contains("5000 → 5013 ms", vm.Status, StringComparison.Ordinal);
                Assert.DoesNotContain("check by ear", vm.Status, StringComparison.Ordinal); // 13 ms < ⅛ beat.
                vm.Undo();
                Assert.Equal(new TimingPoint(5000, 400.0123), vm.Edits.RedLines[1]);
                vm.Select(2);
                await vm.FitOffsetAsync(detector.Detect);
                Assert.Equal(new TimingRequest(12000, 20000, 120), detector.Requests[^1]); // Last section runs to the song end.
            }
            finally { File.Delete(path); }
    }

    [Fact]
    public async Task AutoTimeSectionDetectsOrLocksBpmAndSnapsTheLineToTheNearestBeat()
    {
        var (vm, path) = await Loaded(new(0, 500), new(5000, 400), new(12000, 500));
        using (vm)
            try
            {
                var detector = new FakeDetector { Result = r => new(r.Bpm ?? 149.9968, 5205.1, null, 0, null) };
                vm.Select(1);
                await vm.AutoTimeSectionAsync(detector.Detect);
                Assert.Equal(new TimingRequest(5000, 12000, null), detector.Requests[^1]);
                // Beats at 5205.1 + k * 400.0085; the nearest to 5000 is k = -1.
                Assert.Equal(new TimingPoint(4805, 60000 / 149.997), vm.Edits.RedLines[1]);
                Assert.Contains("(detected)", vm.Status, StringComparison.Ordinal);
                Assert.Contains("check by ear", vm.Status, StringComparison.Ordinal); // Moved 195 ms, about ½ beat.
                vm.Undo();
                vm.Select(1);
                vm.LockedBpmText = "150";
                await vm.AutoTimeSectionAsync(detector.Detect);
                Assert.Equal(150, detector.Requests[^1].Bpm);
                Assert.Equal(new TimingPoint(4805, 400), vm.Edits.RedLines[1]);
                Assert.Contains("(BPM box)", vm.Status, StringComparison.Ordinal);
                vm.Undo();
                Assert.Equal(new TimingPoint(5000, 400), vm.Edits.RedLines[1]); // One undo step.
            }
            finally { File.Delete(path); }
    }

    [Fact]
    public async Task SectionFitReportsProblemsWithoutEditing()
    {
        var (vm, path) = await Loaded(new(0, 500), new(5000, 400), new(6500, 500), new(12000, 500));
        using (vm)
            try
            {
                var detector = new FakeDetector();
                var before = vm.Edits.RedLines.ToArray();
                vm.ClearSelection();
                await vm.FitOffsetAsync(detector.Detect);
                Assert.Contains("Select the red line", vm.Status, StringComparison.Ordinal);
                vm.Select(1); // 5000-6500: shorter than 4 s.
                await vm.AutoTimeSectionAsync(detector.Detect);
                Assert.Contains("too short", vm.Status, StringComparison.Ordinal);
                Assert.Contains("Ctrl+P", vm.Status, StringComparison.Ordinal);
                Assert.Empty(detector.Requests);
                vm.Select(2);
                detector.Result = _ => new(null, null, 140, 16, null);
                await vm.AutoTimeSectionAsync(detector.Detect);
                Assert.Contains("approximate BPM (140)", vm.Status, StringComparison.Ordinal);
                detector.Result = _ => new(null, null, null, -1, "cannot determine timing");
                await vm.AutoTimeSectionAsync(detector.Detect);
                Assert.Contains("cannot determine timing", vm.Status, StringComparison.Ordinal);
                detector.Result = r => new(r.Bpm ?? 15, 4800.2, null, 0, null); // 4000 ms beats: the nearest to 6500 is 4800, before 5000.
                await vm.AutoTimeSectionAsync(detector.Detect);
                Assert.Contains("neighbouring", vm.Status, StringComparison.Ordinal);
                Assert.Equal(before, vm.Edits.RedLines);
                Assert.False(vm.IsBusy);
            }
            finally { File.Delete(path); }
    }
}
