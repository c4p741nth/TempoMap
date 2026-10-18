using System.Globalization;
using TimingAnalyzer.Editor;

namespace TimingAnalyzer.Editor.Tests;

public class PrecisionTests
{
    private static string FixturePath => Path.Combine(AppContext.BaseDirectory, "Fixtures", "my-dearest-tv.osu");

    [Theory]
    [InlineData(0, "00:00:000")]
    [InlineData(1531, "00:01:531")] // Same instant as osu! offset 1531.
    [InlineData(1530.6, "00:01:531")] // Whole ms, rounded like offsets.
    [InlineData(59999.4, "00:59:999")]
    [InlineData(211750, "03:31:750")]
    [InlineData(6000000, "100:00:000")]
    [InlineData(-5, "00:00:000")]
    [InlineData(double.NaN, "00:00:000")]
    public void OsuTimeIsMinutesSecondsMilliseconds(double ms, string expected) => Assert.Equal(expected, TimeFormat.Osu(ms));

    [Fact]
    public void RoundsToOsuPrecisionInvariantly()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE"); // Decimal comma must not leak in.
            Assert.Equal(1235, OsuPrecision.Time(1234.5));
            Assert.Equal(0, OsuPrecision.Time(-0.5));
            Assert.Equal(-1, OsuPrecision.Time(-0.6));
            Assert.Equal(159.997, OsuPrecision.Bpm(159.9968637393969));
            Assert.Equal("159.997", OsuPrecision.FormatBpm(159.9968637393969));
            Assert.Equal("160", OsuPrecision.FormatBpm(159.99964));
            Assert.Equal("170", OsuPrecision.FormatBpm(60000 / 352.941176470589));
            Assert.Equal("351", OsuPrecision.FormatTime(351.4));
            Assert.Equal(60000 / 159.997, OsuPrecision.BeatLength(60000 / 159.9968637393969));
            double once = OsuPrecision.BeatLength(375.00703);
            Assert.Equal(once, OsuPrecision.BeatLength(once)); // Idempotent.
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Fact]
    public async Task AutoTimeProposalShowsAndAppliesThreeDecimalBpmAndWholeMsOffset()
    {
        string song = Path.GetTempFileName();
        try
        {
            using var vm = new EditorViewModel(_ => Task.FromResult(AudioContent.Create(new double[200], 1000)), _ => new ViewModelTests.FakePlayback());
            await vm.LoadAsync(song);
            await vm.AnalyzeAsync(_ => Task.FromResult(new TimingDetection(159.9968637393969, 351.2, null, 0, null)));
            Assert.Equal(159.997, vm.Proposal!.Bpm);
            Assert.Contains("159.997", vm.ProposalDescription, StringComparison.Ordinal);
            Assert.Contains("351 ms", vm.ProposalDescription, StringComparison.Ordinal);
            Assert.DoesNotContain("159.9968", vm.ProposalDescription, StringComparison.Ordinal);
            Assert.Equal("351", vm.ProposalOffsetText);
            vm.ApplyProposal(true);
            var line = Assert.Single(vm.Edits.RedLines);
            Assert.Equal(new TimingPoint(351, 60000 / 159.997), line);
            Assert.Equal("351", vm.TimeText);
            Assert.Equal("159.997", vm.BpmText);
            Assert.StartsWith("● 351 ms   159.997 BPM", vm.Rows[0].Label, StringComparison.Ordinal); // Selected after applying.
            Assert.StartsWith("351," + (60000 / 159.997).ToString("R", CultureInfo.InvariantCulture) + ",4,", line.ToOsuLine(), StringComparison.Ordinal);
        }
        finally { File.Delete(song); }
    }

    [Fact]
    public void EveryEditQuantizesOnlyTheFieldItChanges()
    {
        var edits = new TimingEdits([new(200, 500, 3), new(5000, 375.007)]);
        edits.StretchRedLine(0, 3, 1400.123); // 400.041 ms -> 149.98463 BPM -> 149.985 BPM.
        Assert.Equal(60000 / 149.985, edits.RedLines[0].BeatLength);
        Assert.Equal(200, edits.RedLines[0].Time);
        edits.MoveRedLine(0, 250.5);
        Assert.Equal(251, edits.RedLines[0].Time);
        Assert.Equal(60000 / 149.985, edits.RedLines[0].BeatLength);
        edits.ShiftRedLines(RedLineScope.Selected, 0.5, 1); // 5000 + 187.5035
        Assert.Equal(new TimingPoint(5188, 375.007), edits.RedLines[1]); // Beat length untouched by a shift.
        edits.AddRedLine(3000.6);
        Assert.Equal(3001, edits.RedLines[1].Time);
        var drag = TimingInteraction.Hit(edits.RedLines, 251, 2, false)!;
        Assert.Equal(301, drag.Preview(300.7).Time);
        var stretch = TimingInteraction.Hit(edits.RedLines, 251 + 60000 / 149.985, 2, true)!;
        double bpm = stretch.Preview(251 + 60000 / 149.985 + 1.234).Bpm;
        Assert.Equal(Math.Round(bpm, 3), bpm, 9);
    }

    [Fact]
    public async Task FieldsInsertAndDragCommitAtOsuPrecision()
    {
        using var vm = new EditorViewModel(_ => throw new InvalidOperationException(), _ => new ViewModelTests.FakePlayback());
        await vm.LoadAsync(FixturePath); // Timing only: the fixture's audio is not shipped.
        vm.Select(0);
        var first = vm.Edits.RedLines[0];
        vm.TimeText = "100.4"; vm.BpmText = "159.99964"; vm.ApplyFields();
        Assert.Equal(first with { Time = 100, BeatLength = 375 }, vm.Edits.RedLines[0]); // Samples and effects kept.
        Assert.Equal("160", vm.BpmText);
        vm.Seek(1234.6); vm.Insert();
        Assert.Equal(1235, vm.SelectedRow!.Point.Time);
        Assert.Contains("1235 ms", vm.Status, StringComparison.Ordinal);
        Assert.True(vm.BeginDrag(1235, 2, false));
        vm.UpdateDrag(1300.7);
        Assert.Contains(vm.DisplayRedLines, p => p.Time == 1301);
        vm.CommitDrag();
        Assert.Equal(1301, vm.SelectedRow!.Point.Time);
    }

    [Fact]
    public void UntouchedLinesKeepExactValuesAndTheFixtureRoundTripsByteForByte()
    {
        byte[] bytes = File.ReadAllBytes(FixturePath);
        var doc = OsuFile.Parse(bytes);
        var edits = new TimingEdits(doc.TimingPoints.Where(p => p.Uninherited));
        Assert.Equal(bytes, doc.WithRedLines(edits.RedLines));
        var original = edits.RedLines.ToArray();
        edits.ShiftRedLinesMs(RedLineScope.Selected, 1, 2);
        Assert.Equal(original.Where((_, i) => i != 2), edits.RedLines.Where((_, i) => i != 2)); // Exact, unrounded beat lengths.
        Assert.Equal(original[2] with { Time = original[2].Time + 1 }, edits.RedLines[2]);
        string text = System.Text.Encoding.UTF8.GetString(doc.WithRedLines(edits.RedLines));
        Assert.Contains("\n" + (original[2].Time + 1).ToString(CultureInfo.InvariantCulture) + "," +
            original[2].BeatLength.ToString("R", CultureInfo.InvariantCulture) + ",", text, StringComparison.Ordinal);
        edits.Undo();
        Assert.Equal(bytes, doc.WithRedLines(edits.RedLines));
    }
}
