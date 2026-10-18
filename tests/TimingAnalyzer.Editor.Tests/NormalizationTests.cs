using TimingAnalyzer.Editor;

namespace TimingAnalyzer.Editor.Tests;

/// <summary>Bar lines on a red line per beat, and osu!mania scroll-speed normalisation for the copied timing.</summary>
public class NormalizationTests
{
    private static TimingPoint Red(double time, double bpm, int effects = 0) => new(time, 60000 / bpm, Effects: effects);
    private static TimingPoint Green(double time, double sv, int volume = 100, int effects = 0) =>
        new(time, -100 / sv, Volume: volume, Uninherited: false, Effects: effects);

    [Fact]
    public void MainBpmIsTheBeatLengthHeldLongestUpToTheLastNote()
    {
        // 150 BPM 0-14000 (the first line counts from 0, not from 5000: 9000 would lose) and 46000-66000; 200 BPM 32000.
        TimingPoint[] reds = [Red(5000, 150), Red(14000, 200), Red(46000, 150)];
        Assert.Equal(400, ScrollNormalization.MainBeatLength(reds, 66000));
        // With the last note at 60000 the 150 BPM tail is shorter and 200 BPM wins.
        Assert.Equal(300, ScrollNormalization.MainBeatLength(reds, 60000));
        // A red line after the last note counts nothing (the line before it still runs up to it, as in osu!).
        Assert.Equal(300, ScrollNormalization.MainBeatLength([Red(0, 150), Red(10000, 200), Red(30000, 300)], 25000));
        // A tie goes to the first beat length in time.
        Assert.Equal(400, ScrollNormalization.MainBeatLength([Red(0, 150), Red(10000, 200)], 20000));
    }

    [Fact]
    public void EveryRedLineGetsAGreenLineSoTheScrollSpeedStaysAtTheMainBpm()
    {
        TimingPoint[] reds = [Red(0, 150) with { SampleSet = 2, Volume = 40 }, Red(10000, 200) with { SampleSet = 2, Volume = 40, Effects = TimingPoint.Kiai | TimingPoint.OmitFirstBarline }, Red(20000, 150)];
        // A volume line inside the 200 BPM section and an SV effect (1.5x) inside the last 150 one.
        TimingPoint[] greens = [Green(15000, 1, volume: 70), Green(25000, 1.5)];
        var result = ScrollNormalization.Normalize(reds, greens, reds, 40000);
        Assert.Equal(150, result.MainBpm, 9);
        Assert.Null(result.Reference);
        var added = Assert.Single(result.Points, p => !p.Uninherited && p.Time == 10000);
        Assert.Equal(0.75, added.Sv, 9);
        Assert.Equal((2, 40, TimingPoint.Kiai), (added.SampleSet, added.Volume, added.Effects));
        Assert.Equal(0.75, result.Points.Single(p => p.Time == 15000).Sv, 9);
        Assert.Equal(70, result.Points.Single(p => p.Time == 15000).Volume);
        Assert.Equal(1.5, result.Points.Single(p => p.Time == 25000).Sv, 9);
        // Red lines at the main BPM need no green line; the red lines are copied unchanged.
        Assert.DoesNotContain(result.Points, p => !p.Uninherited && p.Time is 0 or 20000);
        // Nor does one whose 3-decimal BPM is the main beat length to a hair (116.959 BPM = 513.0002 ms against 513).
        var hair = ScrollNormalization.Normalize([new TimingPoint(0, 513), Red(60000, 116.959), Red(61000, 120)], [], [], 62000);
        Assert.Equal([61000.0], hair.Points.Where(p => !p.Uninherited).Select(p => p.Time));
        Assert.Equal(reds, result.Points.Where(p => p.Uninherited));
        Assert.Equal((1, 1, 0), (result.Added, result.Scaled, result.Clamped));
        // The clipboard order puts each green line after the red line at its time.
        var rows = TimingClipboard.Parse(TimingClipboard.Write(result.Points));
        Assert.True(rows[1].Uninherited && rows[1].Time == 10000 && !rows[2].Uninherited && rows[2].Time == 10000);
    }

    [Fact]
    public void NormalisingTwiceChangesNothingAndAnotherReferenceIsUndone()
    {
        TimingPoint[] reds = [Red(0, 150), Red(30000, 200), Red(40000, 120)];
        var once = ScrollNormalization.Normalize(reds, [Green(35000, 1, volume: 70)], reds, 50000);
        var greens = once.Points.Where(p => !p.Uninherited).OrderBy(p => p.Time).ToArray();
        Assert.Equal([0.75, 0.75, 1.25], greens.Select(p => Math.Round(p.Sv, 9)));
        var twice = ScrollNormalization.Normalize(reds, greens, reds, 50000);
        Assert.Equal(150, twice.Reference!.Value, 6);
        Assert.Equal(greens.Select(p => (p.Time, Math.Round(p.Sv, 9))),
            twice.Points.Where(p => !p.Uninherited).OrderBy(p => p.Time).Select(p => (p.Time, Math.Round(p.Sv, 9))));
        Assert.Equal(0, twice.Added);
        // A map normalised to 143 (as JAPANESE SOUL BROTHERS is, its main BPM being 142.627): the green lines' own SV is 1.
        TimingPoint[] live = [Red(0, 142.627), Red(500, 146), Red(910, 140), Red(1340, 142.627)];
        TimingPoint[] at143 = live.Select(r => Green(r.Time, 143 / r.Bpm)).ToArray();
        var renormalised = ScrollNormalization.Normalize(live, at143, live, 2000);
        Assert.Equal(143, renormalised.Reference!.Value, 6);
        Assert.All(renormalised.Points.Where(p => !p.Uninherited), g =>
            Assert.Equal(142.627, g.Sv * renormalised.Points.Last(r => r.Uninherited && r.Time <= g.Time).Bpm, 3));
        // New red lines under green lines written for the old ones: the old line's normalisation goes, the new one comes.
        TimingPoint[] edited = [Red(0, 150), Red(5000, 300), Red(10000, 150), Red(30000, 200), Red(40000, 120)];
        var moved = ScrollNormalization.Normalize(edited, greens, reds, 50000);
        Assert.Equal(0.5, moved.Points.Single(p => !p.Uninherited && p.Time == 5000).Sv, 9);
        Assert.Equal(0.75, moved.Points.Single(p => p.Time == 35000).Sv, 9);
    }

    [Fact]
    public void SlowAndFastSectionsAreHeldAtTheSvLimits()
    {
        var result = ScrollNormalization.Normalize([Red(0, 100), Red(50000, 20000), Red(50003, 0.5)], [], [], 50010);
        Assert.Equal(2, result.Clamped);
        Assert.Equal([0.01, 10], result.Points.Where(p => !p.Uninherited).Select(p => Math.Round(p.Sv, 9)));
    }

    [Fact]
    public void BarLinesEveryFourBeatsFromTheDownbeat()
    {
        // A line on every beat, one line spanning two beats (equal spacing merged), kiai on one line.
        var reds = new List<TimingPoint>();
        double t = 1000;
        for (int k = 0; k < 10; k++) { reds.Add(Red(t, 120, k == 5 ? TimingPoint.Kiai : 0)); t += k == 2 ? 1000 : 500; }
        var result = BarLines.FromDownbeat(reds, 0);
        // Beats: 0 1 2 (3) 4 5 6 7 8 9 10 → bar lines on beats 0, 4, 8: lines 0, 3, 7.
        Assert.Equal([0, 3, 7], Enumerable.Range(0, 10).Where(i => !result.Lines[i].OmitsFirstBarline));
        Assert.Equal(TimingPoint.Kiai | TimingPoint.OmitFirstBarline, result.Lines[5].Effects);
        Assert.Equal((7, 0), (result.Omitted, result.OffBar));
        // Moving the bar start to line 1 leaves line 0 alone and counts from line 1.
        var moved = BarLines.FromDownbeat(result.Lines, 1);
        Assert.Equal([0, 1, 4, 8], Enumerable.Range(0, 10).Where(i => !moved.Lines[i].OmitsFirstBarline));
        // A line longer than a bar that starts mid-bar is reported: osu! draws a bar line inside it.
        var longOne = BarLines.FromDownbeat([Red(0, 120), Red(500, 120), Red(3500, 120)], 0);
        Assert.Equal(1, longOne.OffBar);
        Assert.True(longOne.Lines[2].OmitsFirstBarline); // beat 1 + 6 = 7, mid-bar
    }

    [Fact]
    public void AnOmittedBarLineIsNotAMetronomeDownbeat()
    {
        TimingPoint[] reds = [Red(0, 120), Red(500, 120, TimingPoint.OmitFirstBarline) with { Meter = 1 }];
        var clicks = ClickSchedule.Query(reds, 1000, 0, 1600);
        Assert.Equal([(0L, true), (500L, false), (1000L, true), (1500L, true)], clicks.Select(c => (c.Sample, c.Downbeat)));
    }

    [Fact]
    public void TheLastNoteIncludesAHoldsEnd()
    {
        var doc = OsuFile.Parse(System.Text.Encoding.UTF8.GetBytes(
            "osu file format v14\r\n\r\n[TimingPoints]\r\n0,500,4,1,0,100,1,0\r\n\r\n[HitObjects]\r\n64,192,1000,1,0,0:0:0:0:\r\n" +
            "192,192,1500,128,0,4200:0:0:0:0:\r\n320,192,3000,1,0,0:0:0:0:\r\n"));
        Assert.Equal(4200, doc.LastObjectTime);
        Assert.Null(OsuFile.Parse(System.Text.Encoding.UTF8.GetBytes("osu file format v14\r\n\r\n[HitObjects]\r\n")).LastObjectTime);
    }

    private static async Task<EditorViewModel> WithBeatmap(string timing, string hitObjects = "")
    {
        string dir = Directory.CreateTempSubdirectory().FullName;
        File.WriteAllBytes(Path.Combine(dir, "audio.ogg"), [0]);
        string path = Path.Combine(dir, "map.osu");
        File.WriteAllText(path, "osu file format v14\r\n\r\n[General]\r\nAudioFilename: audio.ogg\r\nMode: 3\r\n\r\n[TimingPoints]\r\n" +
            timing + "\r\n[HitObjects]\r\n" + hitObjects);
        var vm = new EditorViewModel(_ => Task.FromResult(AudioContent.Create(new double[300000], 1000)), _ => new ViewModelTests.FakePlayback());
        await vm.LoadAsync(path);
        return vm;
    }

    [Fact]
    public async Task EveryBeatDriftHasBarLinesAndKeepsTheHitsoundVolume()
    {
        // The map's hitsounds: soft at 40 % from 0, a kiai at 60 % from 3000 (a green line).
        var vm = await WithBeatmap("0,500,4,2,0,40,1,0\r\n3000,-100,4,2,0,60,0,1\r\n");
        vm.SelectedAutoMode = "Drift 1 beat";
        var lines = Enumerable.Range(0, 12).Select(k => new TimingPoint(1000 + 500 * k + (k % 2 == 0 ? 0 : -4), k % 2 == 0 ? 496 : 504)).ToList();
        await vm.AnalyzeDriftAsync((_, _) => Task.FromResult(new MultiTimingDetection(lines)));
        Assert.Equal([0, 4, 8], Enumerable.Range(0, 12).Where(i => !vm.MultiProposal!.Sections[i].OmitsFirstBarline));
        Assert.Contains("bar lines every 4 beats", vm.Status);
        vm.ApplyMultiProposal(true);
        var reds = vm.Edits.RedLines;
        Assert.All(reds, r => Assert.Equal(2, r.SampleSet));
        Assert.Equal([40, 40, 40, 40, 60, 60], reds.Take(6).Select(r => r.Volume));
        Assert.Equal([0, 0, 0, 0, 1, 1], reds.Take(6).Select(r => r.Effects & TimingPoint.Kiai));
        Assert.Contains("no bar line", vm.Rows[1].Label);
        // Bar line here on line 2 moves the bars; one undo restores them.
        vm.SelectedRow = vm.Rows[2];
        vm.StartBarHereCommand.Execute(null);
        Assert.Equal([0, 2, 6, 10], Enumerable.Range(0, 12).Where(i => !vm.Edits.RedLines[i].OmitsFirstBarline));
        Assert.Contains("7 of the 9 red lines after it omit their bar line", vm.Status);
        vm.Undo();
        Assert.Equal([0, 4, 8], Enumerable.Range(0, 12).Where(i => !vm.Edits.RedLines[i].OmitsFirstBarline));
    }

    [Fact]
    public async Task NormalizedCopyMeasuresTheMainBpmToTheLastNoteAndLeavesTheTimingAlone()
    {
        var vm = await WithBeatmap("0,400,4,1,0,50,1,0\r\n10000,300,4,1,0,50,1,0\r\n20000,400,4,1,0,50,1,0\r\n",
            "64,192,1000,1,0,0:0:0:0:\r\n64,192,24000,128,0,26000:0:0:0:0:\r\n");
        var before = vm.Edits.RedLines.ToArray();
        var (text, summary) = vm.NormalizedClipboardText();
        var rows = TimingClipboard.Parse(text);
        Assert.Equal(4, rows.Length);
        Assert.Equal(0.75, rows.Single(r => !r.Uninherited).Sv, 9);
        Assert.Contains("normalised to 150 BPM", summary);
        Assert.Contains("the last note at 00:26:000", summary);
        Assert.Contains("1 added on red lines, 0 existing scaled", summary);
        Assert.Equal(before, vm.Edits.RedLines);
    }
}
