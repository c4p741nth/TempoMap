using TimingAnalyzer.Editor;

namespace TimingAnalyzer.Editor.Tests;

/// <summary>M3: the editor side of multi-BPM auto-timing, with a fake detector (no audio decoding, no Core).</summary>
public class MultiTimingTests
{
    private sealed class FakeMultiDetector
    {
        public int Calls { get; private set; }
        public Func<MultiTimingDetection> Result { get; set; } = () => new([new TimingPoint(351, 60000 / 160.0)]);
        public Task<MultiTimingDetection> Detect(AudioContent audio)
        {
            Calls++;
            return Task.FromResult(Result());
        }
    }

    private static async Task<EditorViewModel> Loaded(params TimingPoint[] reds)
    {
        string path = Path.GetTempFileName();
        var vm = new EditorViewModel(_ => Task.FromResult(AudioContent.Create(new double[20000], 1000)), _ => new ViewModelTests.FakePlayback());
        await vm.LoadAsync(path);
        if (reds.Length > 0) vm.Edits.Replace(reds);
        return vm;
    }

    private static MultiTimingDetection Mambo() => new([
        new TimingPoint(726, 60000 / 159.997),
        new TimingPoint(102274, 60000 / 135.0),
        new TimingPoint(140445, 60000 / 140.0),
    ]);

    [Fact]
    public async Task ProposesEverySectionAndAppliesThemInOneUndoStep()
    {
        var vm = await Loaded(new TimingPoint(0, 500));
        var detector = new FakeMultiDetector { Result = Mambo };
        var before = vm.Edits.RedLines.ToArray();

        await vm.AnalyzeMultiAsync(detector.Detect);
        Assert.Equal(1, detector.Calls);
        // Nothing changes until the proposal is applied.
        Assert.Equal(before, vm.Edits.RedLines);
        Assert.Equal(3, vm.MultiProposal!.Sections.Count);
        Assert.Equal(3, vm.ProposalSections.Count);
        Assert.Contains("135", vm.ProposalDescription);

        vm.ApplyMultiProposal(false);
        Assert.Equal(before, vm.Edits.RedLines);

        vm.ApplyMultiProposal(true);
        var after = vm.Edits.RedLines;
        Assert.Equal(3, after.Count);
        Assert.Equal(726, after[0].Time);
        Assert.Equal(102274, after[1].Time);
        Assert.Equal(160, 60000 / after[0].BeatLength, 2);
        Assert.Equal(135, 60000 / after[1].BeatLength, 2);

        // One undo step restores all of them at once.
        vm.Undo();
        Assert.Equal(before, vm.Edits.RedLines);
    }

    [Fact]
    public async Task AppliedSectionsAreAtOsuPrecision()
    {
        var vm = await Loaded();
        await vm.AnalyzeMultiAsync(_ => Task.FromResult(new MultiTimingDetection([
            new TimingPoint(726.47, 60000 / 159.9968637393969),
            new TimingPoint(102274.2, 60000 / 134.99991),
        ])));
        vm.ApplyMultiProposal(true);
        var reds = vm.Edits.RedLines;
        Assert.All(reds, p => Assert.Equal(p.Time, Math.Round(p.Time)));
        Assert.Equal(726, reds[0].Time);
        Assert.Equal(159.997, 60000 / reds[0].BeatLength, 3);
        Assert.Equal(135, 60000 / reds[1].BeatLength, 3);
    }

    [Fact]
    public async Task GreenLinesSurviveApply()
    {
        string path = Path.GetTempFileName();
        var vm = new EditorViewModel(_ => Task.FromResult(AudioContent.Create(new double[20000], 1000)), _ => new ViewModelTests.FakePlayback());
        await vm.LoadAsync(path);
        vm.Edits.Replace([new TimingPoint(0, 500)]);
        await vm.AnalyzeMultiAsync(new FakeMultiDetector { Result = Mambo }.Detect);
        vm.ApplyMultiProposal(true);
        // Apply only ever touches red lines; the edit buffer holds nothing else.
        Assert.All(vm.Edits.RedLines, p => Assert.True(p.Uninherited));
        Assert.Equal(3, vm.Edits.RedLines.Count);
    }

    [Fact]
    public async Task TheBpmBoxStillForcesOneBpm()
    {
        var vm = await Loaded(new TimingPoint(0, 500));
        vm.LockedBpmText = "160";
        var detector = new FakeMultiDetector { Result = Mambo };
        await vm.AnalyzeMultiAsync(detector.Detect);
        Assert.Equal(0, detector.Calls);
        Assert.Null(vm.MultiProposal);
        Assert.Contains("BPM box", vm.Status);
    }

    [Fact]
    public async Task ADetectorErrorIsReportedAndChangesNothing()
    {
        var vm = await Loaded(new TimingPoint(0, 500));
        var before = vm.Edits.RedLines.ToArray();
        await vm.AnalyzeMultiAsync(_ => Task.FromResult(new MultiTimingDetection([], "cannot determine timing")));
        Assert.Null(vm.MultiProposal);
        Assert.Contains("cannot determine timing", vm.Status);
        Assert.Equal(before, vm.Edits.RedLines);

        await vm.AnalyzeMultiAsync(_ => Task.FromResult(new MultiTimingDetection([])));
        Assert.Null(vm.MultiProposal);
        Assert.Contains("No timing detected", vm.Status);
        Assert.Equal(before, vm.Edits.RedLines);
    }

    [Fact]
    public async Task TheTwoProposalKindsAreMutuallyExclusive()
    {
        var vm = await Loaded(new TimingPoint(0, 500));
        await vm.AnalyzeMultiAsync(new FakeMultiDetector { Result = Mambo }.Detect);
        Assert.NotNull(vm.MultiProposal);

        // A single-line auto-time replaces the pending multi proposal, so Apply cannot use a stale one.
        await vm.AnalyzeAsync(_ => Task.FromResult(new TimingDetection(180, 123, null, 0, null)));
        Assert.Null(vm.MultiProposal);
        Assert.Empty(vm.ProposalSections);
        Assert.NotNull(vm.Proposal);

        vm.ApplyMultiProposal(true);
        Assert.Equal(new TimingPoint(0, 500), Assert.Single(vm.Edits.RedLines));
    }

    [Fact]
    public async Task DriftModePassesTheChosenBeatsAndAppliesLikeMultiBpm()
    {
        var vm = await Loaded(new TimingPoint(0, 500));
        Assert.Equal(0, vm.DriftBeatsPerLine); // Normal mode
        vm.SelectedAutoMode = "Drift 8 beats";
        vm.SelectedAutoMode = "Drift 7 beats"; // not a choice: ignored
        int asked = 0;
        await vm.AnalyzeDriftAsync((_, beats) =>
        {
            asked = beats;
            return Task.FromResult(new MultiTimingDetection([new TimingPoint(103, 60000 / 117.1), new TimingPoint(4203.4, 60000 / 117.2349)]));
        });
        Assert.Equal(8, asked);
        Assert.Contains("every 8 beats", vm.Status);
        Assert.Equal(new TimingPoint(0, 500), Assert.Single(vm.Edits.RedLines));
        vm.ApplyMultiProposal(true);
        Assert.Equal([103, 4203], vm.Edits.RedLines.Select(p => p.Time));
        Assert.Equal(117.235, 60000 / vm.Edits.RedLines[1].BeatLength, 3);
        vm.Undo();
        Assert.Equal(new TimingPoint(0, 500), Assert.Single(vm.Edits.RedLines));
        // The BPM box forces one BPM here too.
        vm.LockedBpmText = "117";
        asked = 0;
        await vm.AnalyzeDriftAsync((_, beats) => { asked = beats; return Task.FromResult(new MultiTimingDetection([])); });
        Assert.Equal(0, asked);
    }

    [Fact]
    public async Task DriftOnEveryBeatAsksForOneBeatAndSummarisesHundredsOfLines()
    {
        var vm = await Loaded(new TimingPoint(0, 500));
        vm.SelectedAutoMode = "Drift 1 beat";
        Assert.Equal(1, vm.DriftBeatsPerLine);
        int asked = 0;
        var lines = Enumerable.Range(0, 574).Select(k => new TimingPoint(391 + 512.0 * k + (k % 2 == 0 ? 0 : -3), k % 2 == 0 ? 509 : 515)).ToList();
        await vm.AnalyzeDriftAsync((_, beats) => { asked = beats; return Task.FromResult(new MultiTimingDetection(lines)); });
        Assert.Equal(1, asked);
        Assert.Contains("on every beat", vm.Status);
        Assert.Contains("574 sections: 116.505-117.878 BPM from 391 to", vm.ProposalDescription);
        Assert.True(vm.ProposalDescription.Length < 300, vm.ProposalDescription);
        Assert.Equal(574, vm.ProposalSections.Count);
        vm.ApplyMultiProposal(true);
        Assert.Equal(574, vm.Edits.RedLines.Count);
    }

    [Fact]
    public async Task OneAutoTimeButtonPicksThePathFromTheBpmBoxAndTheMode()
    {
        var vm = await Loaded(new TimingPoint(0, 500));
        var calls = new List<string>();
        Task<TimingDetection> Single(AudioContent a, TimingRequest r) { calls.Add("single " + r.Bpm); return Task.FromResult(new TimingDetection(160, 352, null, 0, null)); }
        MultiTimingDetection result = new([new TimingPoint(351, 60000 / 159.997)], null, 3);
        Task<MultiTimingDetection> Multi(AudioContent a) { calls.Add("multi"); return Task.FromResult(result); }
        Task<MultiTimingDetection> Drift(AudioContent a, int beats) { calls.Add("drift " + beats); return Task.FromResult(new MultiTimingDetection([new TimingPoint(351, 375), new TimingPoint(5000, 380)])); }

        // Normal, one tempo: the line comes as the one-line proposal with its offset editable, as Auto-time song gave.
        await vm.AutoTimeAsync(Single, Multi, Drift);
        Assert.Equal(["multi"], calls);
        Assert.True(vm.HasSingleProposal);
        Assert.Null(vm.MultiProposal);
        Assert.Equal("351", vm.ProposalOffsetText);
        Assert.StartsWith("Exact BPM: 159.997; osu! offset: 351 ms; detector status: 3", vm.Status, StringComparison.Ordinal);
        vm.ProposalOffsetText = "360";
        vm.ApplyProposal(true);
        Assert.Equal(new TimingPoint(360, 60000 / 159.997), Assert.Single(vm.Edits.RedLines));

        // Normal, several tempos: a list; played drums add the drift hint.
        result = new([new TimingPoint(351, 375), new TimingPoint(40000, 400)], null, 0, new LiveDrumHint(4.87, 125));
        await vm.AutoTimeAsync(Single, Multi, Drift);
        Assert.False(vm.HasSingleProposal);
        Assert.Equal(2, vm.MultiProposal!.Sections.Count);
        Assert.Contains("4.9 ms from beat to beat (125 beats measured)", vm.Status);
        Assert.Contains("choose Drift 1 beat", vm.Status);

        // A drift mode runs drift; a BPM in the box runs the locked one-line path whatever the mode.
        vm.SelectedAutoMode = "Drift 4 beats";
        await vm.AutoTimeAsync(Single, Multi, Drift);
        vm.LockedBpmText = "160";
        await vm.AutoTimeAsync(Single, Multi, Drift);
        Assert.Equal(["multi", "multi", "drift 4", "single 160"], calls);
        Assert.StartsWith("Locked BPM: 160", vm.Status, StringComparison.Ordinal);

        // Beat model mode: the model's red lines for the whole song, as a list (never the one-line proposal), and it
        // says so; the BPM box still wins; without a model the mode explains itself instead of failing.
        vm.LockedBpmText = "";
        vm.SelectedAutoMode = EditorViewModel.BeatModelMode;
        Assert.Equal(0, vm.DriftBeatsPerLine);
        Task<MultiTimingDetection> Model(AudioContent a)
        {
            calls.Add("model");
            return Task.FromResult(new MultiTimingDetection([new TimingPoint(351, 60000 / 150.0)], BeatModelUsed: true));
        }
        await vm.AutoTimeAsync(Single, Multi, Drift, Model);
        Assert.Equal("model", calls[^1]);
        Assert.False(vm.HasSingleProposal);
        Assert.Single(vm.MultiProposal!.Sections);
        Assert.Contains("Beat model mode: the model's red lines for the whole song", vm.Status);
        await vm.AutoTimeAsync(Single, Multi, Drift);
        Assert.Equal("The beat model is not available in this build; choose Normal.", vm.Status);
    }

    [Fact]
    public void DuplicateAndUnsortedSectionsAreNormalized()
    {
        var proposal = MultiTimingProposal.FromDetection(new MultiTimingDetection([
            new TimingPoint(5000, 60000 / 140.0),
            new TimingPoint(726, 60000 / 160.0),
            new TimingPoint(5000.4, 60000 / 150.0),
        ]));
        Assert.Equal(2, proposal.Sections.Count);
        Assert.Equal(726, proposal.Sections[0].Time);
        Assert.Equal(5000, proposal.Sections[1].Time);
    }
}
