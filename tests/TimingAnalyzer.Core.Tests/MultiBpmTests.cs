using TimingAnalyzer.Core;

namespace TimingAnalyzer.Core.Tests;

public class MultiBpmTests
{
    /// <summary>A synthetic onset envelope: a sharp bump per onset at envelope index = osu! time + 28.7 ms, plus noise.</summary>
    private static double[] Envelope(IEnumerable<(double Time, double Strength)> onsets, double lengthMs, int seed = 7)
    {
        var d = new double[(int)lengthMs];
        var random = new Random(seed);
        for (int i = 0; i < d.Length; i++) d[i] = (random.NextDouble() - 0.5) * 0.05;
        foreach (var (time, strength) in onsets)
        {
            double centre = time + MultiBpmAnalyzer.PeakToOsuMs;
            for (int i = (int)centre - 12; i <= (int)centre + 12; i++)
                if (i >= 0 && i < d.Length) d[i] += strength * (Math.Exp(-0.5 * Math.Pow((i - centre) / 2.0, 2)) - 0.3 * Math.Exp(-0.5 * Math.Pow((i - centre - 6) / 4.0, 2)));
        }
        return d;
    }

    private static IEnumerable<(double, double)> Beats(double first, double bpm, double end, double offBeat = 0)
    {
        double length = 60000 / bpm;
        for (double t = first; t < end; t += length)
        {
            yield return (t, 1.0);
            if (offBeat > 0) yield return (t + length / 2, offBeat);
        }
    }

    /// <summary>Distance from a true beat to the nearest beat of the predicted red lines.</summary>
    private static double Error(IReadOnlyList<TimingSection> sections, double time)
    {
        int s = 0;
        while (s + 1 < sections.Count && sections[s + 1].TimeMs <= time + 1) s++;
        double length = 60000 / sections[s].Bpm, k = Math.Round((time - sections[s].TimeMs) / length);
        return Math.Abs(sections[s].TimeMs + k * length - time);
    }

    [Fact]
    public void SteadyTempoGivesOneRedLineOnTheBeatsNotTheOffBeats()
    {
        var truth = Beats(1000, 150, 60000).ToList();
        var d = Envelope(Beats(1000, 150, 60000, offBeat: 0.45), 62000);
        var sections = MultiBpmAnalyzer.Analyze(d);
        var only = Assert.Single(sections);
        Assert.Equal(150, only.Bpm, 2);
        Assert.All(truth, beat => Assert.True(Error(sections, beat.Item1) < 2, $"beat {beat.Item1}: {Error(sections, beat.Item1):0.0} ms"));
    }

    [Fact]
    public void ATempoChangeGetsItsOwnRedLineAtTheChange()
    {
        double change = 1000 + 85 * 60000 / 128.0; // 85 beats at 128 BPM, then 160 BPM on the next beat.
        var truth = Beats(1000, 128, change).Concat(Beats(change, 160, 80000)).ToList();
        var d = Envelope(truth.Select(b => (b.Item1, b.Item2)), 82000);
        var sections = MultiBpmAnalyzer.Analyze(d);
        Assert.Equal(2, sections.Count);
        Assert.Equal(128, sections[0].Bpm, 2);
        Assert.Equal(160, sections[1].Bpm, 2);
        Assert.InRange(sections[1].TimeMs, change - 60000 / 128.0, change + 60000 / 160.0);
        var errors = truth.Select(b => Error(sections, b.Item1)).Order().ToArray();
        Assert.True(errors[errors.Length * 95 / 100] < 3, $"95th percentile error {errors[errors.Length * 95 / 100]:0.0} ms");
    }

    /// <summary>Beats of a ramp that changes tempo every <paramref name="beatsPerStep"/> beats.</summary>
    private static List<(double, double)> Ramp(double first, IEnumerable<double> bpms, int beatsPerStep)
    {
        var beats = new List<(double, double)>();
        double t = first;
        foreach (double bpm in bpms)
            for (int k = 0; k < beatsPerStep; k++) { beats.Add((t, 1.0)); t += 60000 / bpm; }
        return beats;
    }

    [Fact]
    public void TheJointTrackerFollowsACleanTempoRamp()
    {
        // 30 s at 160, a ramp down to 135 in 8-beat steps (the MAMBO COMBO shape), then 30 s at 135.
        var steady = Beats(1000, 160, 31000).ToList();
        double rampStart = steady[^1].Item1 + 60000 / 160.0;
        var ramp = Ramp(rampStart, [155, 150, 145, 140], 8);
        double tail = ramp[^1].Item1 + 60000 / 140.0;
        var truth = steady.Concat(ramp).Concat(Beats(tail, 135, tail + 30000)).ToList();
        var d = Envelope(truth, truth[^1].Item1 + 2000);
        var rampTimes = ramp.Select(b => b.Item1).ToArray();
        double Recall(MultiBpmOptions o)
        {
            var tracked = MultiBpmAnalyzer.TrackBeats(d, o, 160).Select(b => b.TimeMs).ToArray();
            return rampTimes.Count(t => tracked.Any(b => Math.Abs(b - t) <= 10)) / (double)rampTimes.Length;
        }
        // Clean synthetic onsets: both trackers follow this ramp (measured 100 %). The joint tracker's advantage is
        // on real, syncopated music (docs/PHASE3-STATUS.md); here it must simply not lose the ramp.
        double joint = Recall(new MultiBpmOptions { JointTracker = true, TempoContinuity = 1000 });
        Assert.True(joint >= 0.9, $"joint tracker found {joint:P0} of the ramp beats");
    }

    [Fact]
    public void AStepRampBetweenTwoSectionsGetsItsSteps()
    {
        // MAMBO COMBO's shape: 160, then 8-beat steps 150 / 145 / 140, then 135. Weaker onsets in the ramp.
        var steady = Beats(1000, 160, 41000).ToList();
        double rampStart = steady[^1].Item1 + 60000 / 160.0;
        var ramp = Ramp(rampStart, [150, 145, 140], 8).Select(b => (b.Item1, 0.6)).ToList();
        double tail = ramp[^1].Item1 + 60000 / 140.0;
        var truth = steady.Concat(ramp).Concat(Beats(tail, 135, tail + 40000)).ToList();
        var d = Envelope(truth, truth[^1].Item1 + 2000);
        var sections = MultiBpmAnalyzer.Analyze(d);
        // Every ramp beat is on the predicted grid; the ramp got whole-BPM lines between 160 and 135.
        var rampErrors = ramp.Select(b => Error(sections, b.Item1)).Order().ToArray();
        Assert.True(rampErrors[rampErrors.Length * 9 / 10] < 6, $"90th percentile ramp error {rampErrors[rampErrors.Length * 9 / 10]:0.0} ms; " +
            string.Join(", ", sections.Select(s => $"{s.TimeMs:0}@{s.Bpm:0.###}")));
        Assert.Contains(sections, s => s.Bpm is > 136 and < 159);
        Assert.Equal(160, sections[0].Bpm, 1);
        Assert.Equal(135, sections[^1].Bpm, 1);
        // Without the ramp model the same song loses the ramp beats.
        var plain = MultiBpmAnalyzer.Analyze(d, new MultiBpmOptions { Ramps = false });
        var plainErrors = ramp.Select(b => Error(plain, b.Item1)).Order().ToArray();
        Assert.True(plainErrors[plainErrors.Length * 9 / 10] > rampErrors[rampErrors.Length * 9 / 10]);
    }

    [Fact]
    public void ARangeThatStartsOnARampGetsItsStepsFromTheContext()
    {
        // As AStepRampBetweenTwoSectionsGetsItsSteps, with the range highlighted from the ramp's first beat to 1.5 s
        // after it (Moonlit Occultation, 2026-10-21: 01:48:327 to 02:01 over a 206 → 240 ramp). The slice alone has no
        // steady part before the ramp to bridge from; the context on each side gives it one. The context is an
        // experiment the editor does not use (RangeTiming.Analyze, docs/PHASE1-STATUS.md E41).
        var steady = Beats(1000, 160, 41000).ToList();
        double rampStart = steady[^1].Item1 + 60000 / 160.0;
        var ramp = Ramp(rampStart, [150, 145, 140], 8).Select(b => (b.Item1, 0.6)).ToList();
        double tail = ramp[^1].Item1 + 60000 / 140.0;
        var truth = steady.Concat(ramp).Concat(Beats(tail, 135, tail + 40000)).ToList();
        var d = Envelope(truth, truth[^1].Item1 + 2000);
        double a = rampStart, b = tail + 1500;
        var lines = RangeTiming.Analyze(d, a, b, null, contextMs: 10000);
        Assert.All(lines, s => Assert.InRange(s.TimeMs, a, b));
        var inRange = truth.Select(t => t.Item1).Where(t => t >= lines[0].TimeMs && t < b).ToArray();
        var errors = inRange.Select(t => Error(lines, t)).Order().ToArray();
        Assert.True(errors[errors.Length * 9 / 10] < 6, $"90th percentile error {errors[errors.Length * 9 / 10]:0.0} ms; " +
            string.Join(", ", lines.Select(s => $"{s.TimeMs:0}@{s.Bpm:0.###}")));
        Assert.True(lines[0].TimeMs < a + 60000 / 150.0, $"first red line at {lines[0].TimeMs:0}, the range starts at {a:0}");
        // The slice alone loses the ramp beats.
        var alone = RangeTiming.Analyze(d, a, b);
        var aloneErrors = inRange.Select(t => Error(alone, t)).Order().ToArray();
        Assert.True(aloneErrors[aloneErrors.Length * 9 / 10] > errors[errors.Length * 9 / 10], $"slice alone: 90th percentile error " +
            $"{aloneErrors[aloneErrors.Length * 9 / 10]:0.0} ms; " + string.Join(", ", alone.Select(s => $"{s.TimeMs:0}@{s.Bpm:0.###}")));
    }

    [Fact]
    public void AStraightTempoSwitchGetsNoRampLines()
    {
        double change = 1000 + 96 * 60000 / 160.0;
        var truth = Beats(1000, 160, change).Concat(Beats(change, 135, change + 40000)).ToList();
        var sections = MultiBpmAnalyzer.Analyze(Envelope(truth, change + 42000));
        Assert.Equal(2, sections.Count);
    }

    [Fact]
    public void NearWholeBpmsSnapAroundTheSectionMiddle()
    {
        // 32 beats at 159.995 BPM: 160 changes the section by 32 × 0.012 ms = 0.4 ms, so it snaps; the middle beat stays.
        var sections = new List<TimingSection> { new(1000, 159.995), new(1000 + 32 * 60000 / 159.995, 135.4) };
        double end = sections[1].TimeMs + 32 * 60000 / 135.4;
        var snapped = MultiBpmAnalyzer.WholeBpm(sections, end, 8);
        Assert.Equal(160, snapped[0].Bpm);
        Assert.Equal(1000 + 16 * 60000 / 159.995, snapped[0].TimeMs + 16 * 60000 / 160.0, 6);
        // 135.4 → 135 is 32 × 1.31 ms = 42 ms off: kept.
        Assert.Equal(sections[1], snapped[1]);
        // A two-line song is the only case changed; the option is the default.
        Assert.Equal(8, new MultiBpmOptions().WholeBpmDriftMs);
    }

    [Fact]
    public void AHalfTempoSectionIsDoubledAndARealSlowOneIsNot()
    {
        // 220 BPM played as onsets on every beat, but the section was found at 110: the half beats fold as strongly.
        var fast = Beats(1000, 220, 31000).ToList();
        double change = 31000;
        // Then a genuine 110 BPM section with nothing between its beats.
        var slow = Beats(change, 110, change + 30000).ToList();
        var d = Envelope(fast.Concat(slow).ToList(), change + 32000);
        var found = new List<TimingSection> { new(1000, 110), new(change, 110) };
        var fixedUp = MultiBpmAnalyzer.DoubleHalfTempo(d, found, change + 30000, new MultiBpmOptions());
        Assert.Equal(220, fixedUp[0].Bpm);
        Assert.Equal(110, fixedUp[1].Bpm);
        Assert.Equal(found.Select(s => s.TimeMs), fixedUp.Select(s => s.TimeMs));
        // Above the cap nothing is doubled.
        Assert.Equal(found, MultiBpmAnalyzer.DoubleHalfTempo(d, found, change + 30000, new MultiBpmOptions { HalfTempoMaxBpm = 100 }));
    }

    [Fact]
    public void ATripletLevelAnswerIsDividedByThreeAndAnEvenOneIsNot()
    {
        // 95 BPM with triplets: onsets at 285 BPM, every third (the beat) strong. The line was found at 285 on a triplet.
        double length = 60000 / 285.0, end = 41000;
        var triplets = Beats(1000, 285, end).Select((b, i) => (b.Item1, i % 3 == 1 ? 1.0 : 0.3)).ToList();
        var d = Envelope(triplets, end + 2000);
        var fast = new TimingSection(1000, 285);
        var slow = MultiBpmAnalyzer.Triplet(d, fast, end, new MultiBpmOptions());
        Assert.Equal(95, slow.Bpm);
        Assert.Equal(1000 + length, slow.TimeMs, 6);
        // Even onsets: every beat is a beat.
        var even = Envelope(Beats(1000, 285, end), end + 2000);
        Assert.Equal(fast, MultiBpmAnalyzer.Triplet(even, fast, end, new MultiBpmOptions()));
        // Below 240 BPM the measure is not trusted (Veil Of Maya - Mikasa, a 220 map, folds like this).
        var at220 = Envelope(Beats(1000, 220, end).Select((b, i) => (b.Item1, i % 3 == 1 ? 1.0 : 0.3)), end + 2000);
        Assert.Equal(new TimingSection(1000, 220), MultiBpmAnalyzer.Triplet(at220, new TimingSection(1000, 220), end, new MultiBpmOptions()));
    }

    [Fact]
    public void DrumWobbleTellsPlayedHitsFromSequencedOnes()
    {
        // Clicks (a 2.5 kHz burst that starts at once and decays) 22 ms after each tracked beat, 120 BPM for 40 s.
        const int rate = 44100;
        static double[] Clicks(Func<int, double> push)
        {
            var mono = new double[rate * 42];
            var random = new Random(7);
            for (int i = 0; i < mono.Length; i++) mono[i] = 0.002 * (random.NextDouble() - 0.5);
            for (int k = 0; k < 80; k++)
            {
                int start = (int)((1000 + 500 * k + 22 + push(k)) * rate / 1000);
                for (int i = 0; i < rate / 50; i++) mono[start + i] += 0.5 * Math.Exp(-i / (rate * 0.01)) * Math.Sin(2 * Math.PI * 2500 * i / rate);
            }
            return mono;
        }
        var beats = Enumerable.Range(0, 80).Select(k => new TrackedBeat(1000 + 500 * k, 1)).ToList();
        var steady = DrumWobble.Measure(Clicks(_ => 0), rate, beats)!;
        Assert.InRange(steady.MedianMs, 0, 0.5);
        Assert.False(steady.SuggestsLiveDrums);
        // A loose drummer: each hit up to ±12 ms off the grid (deterministic). ±8 ms measures 3.65 ms, under the hint:
        // the 4 ms threshold sits above the steady songs' 2.5 ms, so only clearly played drums (Billie Jean 4.9) get it.
        var played = DrumWobble.Measure(Clicks(k => 12 * Math.Sin(k * 2.399) * Math.Cos(k * 0.731)), rate, beats)!;
        Assert.True(played.Triples >= DrumWobble.MinTriples, $"{played.Triples} triples");
        Assert.True(played.MedianMs >= DrumWobble.HintMs, $"median {played.MedianMs:0.00} ms");
        Assert.True(played.SuggestsLiveDrums);
    }

    [Fact]
    public void DriftLinesFollowASlowlyDriftingTempo()
    {
        // Billie Jean's shape: 117 BPM drifting ±0.3 BPM along a slow arc, so one grid ends up tens of ms off.
        var truth = new List<(double, double)>();
        double t = 1000;
        for (int k = 0; t < 241000; k++)
        {
            truth.Add((t, 1.0));
            t += 60000 / (117 + 0.3 * Math.Sin(2 * Math.PI * k / 480));
        }
        var tracked = truth.Select(b => new TrackedBeat(b.Item1, 1)).ToList();
        var lines = DriftLines.Fit(tracked, 16);
        Assert.InRange(lines.Count, truth.Count / 16 - 1, truth.Count / 16 + 1);
        var errors = truth.Select(b => Error(lines, b.Item1)).Order().ToArray();
        Assert.True(errors[^1] < 3, $"worst beat {errors[^1]:0.0} ms off the drift lines");
        // One straight line through the same beats is far off somewhere.
        var straight = DriftLines.Fit(tracked, truth.Count);
        Assert.True(truth.Max(b => Error(straight, b.Item1)) > 20);
        // Off-beat tracker flips do not pull the grid: every eighth beat moved by half a beat.
        var flipped = tracked.Select((b, k) => k % 8 == 3 ? b with { TimeMs = b.TimeMs + 256 } : b).ToList();
        var robust = DriftLines.Fit(flipped, 16);
        Assert.True(truth.Where((_, k) => k % 8 != 3).Max(b => Error(robust, b.Item1)) < 4);
    }

    [Fact]
    public void PerBeatDriftLinesLandOnEveryPlayedBeat()
    {
        // Billie Jean's shape: the slow arc, each hit pushed or dragged by up to 10 ms as a drummer plays it.
        var truth = new List<double>();
        double grid = 1000;
        for (int k = 0; grid < 121000; k++)
        {
            truth.Add(grid + 10 * Math.Sin(k * 2.399) * Math.Cos(k * 0.731));
            grid += 60000 / (117 + 0.3 * Math.Sin(2 * Math.PI * k / 480));
        }
        // Every 16 beats cannot follow the hits; every beat lands on each one within the whole-ms rounding.
        var tracked = truth.Select(b => new TrackedBeat(b, 1)).ToList();
        var bars = DriftLines.Fit(tracked, 16);
        Assert.True(truth.Max(b => Error(bars, b)) > 8);
        var lines = DriftLines.Fit(tracked, DriftLines.EveryBeat);
        Assert.InRange(lines.Count, truth.Count * 3 / 4, truth.Count - 1);
        Assert.All(lines, l => Assert.Equal(Math.Floor(l.TimeMs), l.TimeMs));
        Assert.True(truth.Max(b => Error(lines, b)) <= 0.5 + 1e-9, $"worst beat {truth.Max(b => Error(lines, b)):0.00} ms off");
        // An off-beat flip and a silent stretch take their place from the kept beats around them.
        var holes = tracked.Select((b, k) => k == 40 ? b with { TimeMs = b.TimeMs + 256 } : k is >= 60 and < 70 ? b with { Weight = 0 } : b).ToList();
        var repaired = DriftLines.Fit(holes, DriftLines.EveryBeat);
        Assert.True(Error(repaired, (truth[39] + truth[41]) / 2) <= 1);
        Assert.DoesNotContain(repaired, l => l.TimeMs > truth[59] + 1 && l.TimeMs < truth[70] - 1);
        Assert.True(truth.Where((_, k) => k != 40 && k is < 60 or >= 70).Max(b => Error(repaired, b)) <= 0.5 + 1e-9);
    }

    [Fact]
    public void TheJointTrackerKeepsASteadySongOnOneRedLine()
    {
        var d = Envelope(Beats(1000, 150, 60000, offBeat: 0.45), 62000);
        var only = Assert.Single(MultiBpmAnalyzer.Analyze(d, new MultiBpmOptions { JointTracker = true, TempoContinuity = 1000 }));
        Assert.Equal(150, only.Bpm, 2);
    }

    [Fact]
    public void ParityPathsAreUntouched()
    {
        // The analyzer is additive: the original whole-song result for the same envelope is unchanged by running it.
        var d = Envelope(Beats(1000, 150, 60000), 62000);
        var before = Analyzer.AnalyzeEnvelope(d);
        MultiBpmAnalyzer.Analyze(d);
        var after = Analyzer.AnalyzeEnvelope(d);
        Assert.Equal(before.Bpm, after.Bpm);
        Assert.Equal(before.OffsetInternal, after.OffsetInternal);
    }

    [Fact]
    public void SectionCheckMarksAMisplacedLineAndLeavesARightOneAlone()
    {
        // 150 BPM from 1 000 ms, a quieter off-beat between: the right line gets no mark, one 60 ms late a shift back.
        var d = Envelope(Beats(1000, 150, 60000, offBeat: 0.4), 62000);
        var right = Assert.Single(SectionCheck.Check(d, [new TimingSection(1000, 150)], 60000))!;
        Assert.False(right.Weak);
        Assert.Null(right.ShiftMs);
        Assert.Null(right.BestRatio);
        var late = Assert.Single(SectionCheck.Check(d, [new TimingSection(1060, 150)], 60000))!;
        // Each beat takes the strongest onset within ±3 ms (SectionCheck.ToleranceMs), so the move is exact to that.
        Assert.InRange(late.ShiftMs!.Value, -60 - SectionCheck.ToleranceMs, -60 + SectionCheck.ToleranceMs);
        // On the off-beats (half a beat late) the line is weak and the move is half a beat.
        var off = Assert.Single(SectionCheck.Check(d, [new TimingSection(1200, 150)], 60000))!;
        Assert.InRange(Math.Abs(off.ShiftMs!.Value), 200 - SectionCheck.ToleranceMs, 200 + SectionCheck.ToleranceMs);
        // A section shorter than 8 beats is a ramp step: not judged.
        Assert.Null(SectionCheck.Check(d, [new TimingSection(1000, 150), new TimingSection(3000, 150)], 60000)[0]);
    }

    [Fact]
    public void ARangeIsAnalyzedOnItsOwnWithAbsoluteTimes()
    {
        // As ATempoChangeGetsItsOwnRedLineAtTheChange: 128 BPM, then 160 BPM from 40.8 s; the range 45-75 s sees only 160.
        double change = 1000 + 85 * 60000 / 128.0;
        var d = Envelope(Beats(1000, 128, change).Concat(Beats(change, 160, 80000)), 82000);
        var only = Assert.Single(RangeTiming.Analyze(d, 45000, 75000));
        Assert.Equal(160, only.Bpm); // Whole BPM.
        double k = Math.Round((only.TimeMs - change) / 375);
        Assert.True(Math.Abs(only.TimeMs - (change + 375 * k)) < 2, $"red line at {only.TimeMs:0.0} is not on a 160 BPM beat");
        Assert.InRange(only.TimeMs, 45000, 45000 + 375);
        // A range across the change gets a red line for each tempo.
        var both = RangeTiming.Analyze(d, 20000, 75000);
        Assert.Equal([128.0, 160.0], both.Select(s => Math.Round(s.Bpm, 1)));
        Assert.InRange(both[1].TimeMs, change - 60000 / 128.0, change + 375);
        Assert.Empty(RangeTiming.Analyze(d, 10000, 10000 + RangeTiming.MinimumMs - 1));
    }
}
