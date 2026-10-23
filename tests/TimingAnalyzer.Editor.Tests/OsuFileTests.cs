using System.Globalization;
using System.Text;
using TimingAnalyzer.Editor;
using Xunit;

namespace TimingAnalyzer.Editor.Tests;

public class OsuFileTests
{
    [Theory]
    [InlineData("steady-multi.osu")]
    [InlineData("ramp-mania.osu")]
    [InlineData("one-red.osu")]
    public void FixturesRoundTripAndOnlyTimingRowsChange(string name)
    {
        byte[] bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));
        var doc = OsuFile.Parse(bytes);
        var reds = doc.TimingPoints.Where(p => p.Uninherited).ToArray();
        Assert.Equal(bytes, doc.WithRedLines(reds));
        byte[] edited = doc.WithRedLines(reds.Select(p => p with { BeatLength = p.BeatLength + 1 }));
        var parsed = OsuFile.Parse(edited);
        Assert.Equal(doc.TimingPoints.Where(p => !p.Uninherited), parsed.TimingPoints.Where(p => !p.Uninherited));
        static string WithoutReds(byte[] b) => string.Join("\n", Encoding.UTF8.GetString(b).Split('\n')
            .Where(s => !s.Contains(',') || !double.TryParse(s.Split(',')[0], CultureInfo.InvariantCulture, out _) ||
                s.Split(',').Length < 7 || s.Split(',')[6] != "1"));
        Assert.Equal(WithoutReds(bytes), WithoutReds(edited));
        Assert.False(string.IsNullOrWhiteSpace(doc.AudioFilename));
    }

    [Fact]
    public void ClipboardPreservesFractionalGreenTimesAndOrdersSerializedRedTimes()
    {
        TimingPoint[] points = [new(0.4, 500), new(-0.4, -100, Uninherited: false)];
        string text = TimingClipboard.Write(points);
        // Rows only, ready to paste into the osu! editor: no [TimingPoints] header.
        Assert.DoesNotContain("[", text, StringComparison.Ordinal);
        Assert.Equal(["-0.4,-100,4,1,0,100,0,0", "0,500,4,1,0,100,1,0"], text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries));
        var parsed = TimingClipboard.Parse(text);
        Assert.Equal(new double[] { -0.4, 0 }, parsed.Select(p => p.Time));
        Assert.Equal(points[1], parsed[0]);
    }

    [Fact]
    public void EditedRowsSortByTheirSerializedTimesIncludingFractionalGreens()
    {
        var doc = OsuFile.Parse(Encoding.UTF8.GetBytes("[TimingPoints]\n-0.4,-100,4,1,0,50,0,0\n1,500\n[HitObjects]\n"));
        var output = doc.WithRedLines([new TimingPoint(0.4, 600)]);
        Assert.Equal(new double[] { -0.4, 0 }, OsuFile.Parse(output).TimingPoints.Select(p => p.Time));
    }

    [Fact]
    public void RedLinesSharingATimeKeepTheFirstAsOsuDoes()
    {
        // Shape of ReeK - Multigenre Smackdown (Arrival), a ranked taiko map: one red line written twice, and (as in
        // LeaF - Mopemope) a pair at one time with different beat lengths. A green line at the same time is not a duplicate.
        var bytes = Encoding.UTF8.GetBytes("[TimingPoints]\r\n0,500,4,2,0,100,1,0\r\n200911,352.941176470588,4,1,0,100,1,0\r\n" +
            "200911,352.941176470588,4,1,0,100,1,0\r\n300000,300,4,2,0,0,1,0\r\n300000,100,4,1,0,15,1,0\r\n300000,-50,4,2,0,0,0,0\r\n[HitObjects]\r\n");
        var doc = OsuFile.Parse(bytes);
        Assert.Equal(2, doc.DuplicateRedLines);
        Assert.Equal([0, 200911, 300000], doc.RedLines.Select(p => p.Time));
        Assert.Equal(300, doc.RedLines[2].BeatLength);
        Assert.Equal(bytes, doc.WithRedLines(doc.RedLines));
        var edited = OsuFile.Parse(doc.WithRedLines(doc.RedLines.Select(p => p with { Volume = 90 })));
        Assert.Equal(0, edited.DuplicateRedLines);
        Assert.Equal(3, edited.RedLines.Count);
        Assert.Equal(-50, Assert.Single(edited.TimingPoints, p => !p.Uninherited).BeatLength);
    }

    [Fact]
    public void CanInsertIntoTimingHeaderWithoutFinalNewline()
    {
        var doc = OsuFile.Parse(Encoding.UTF8.GetBytes("[TimingPoints]"));
        Assert.Equal(new TimingPoint(100, 500), Assert.Single(OsuFile.Parse(doc.WithRedLines([new(100, 500)])).TimingPoints));
    }

    [Fact]
    public void LegacyBomCultureRoundingAndClipboard()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("th-TH");
            var bytes = Encoding.UTF8.Preamble.ToArray().Concat(Encoding.UTF8.GetBytes(
                "[General]\nAudioFilename: song.mp3\n[TimingPoints]\n-1.5,500\n0,-100,4,1,0,50,0,0\n[HitObjects]\nx\n")).ToArray();
            var doc = OsuFile.Parse(bytes);
            Assert.True(doc.TimingPoints[0].Uninherited);
            Assert.Equal(bytes, doc.WithRedLines(doc.TimingPoints.Where(p => p.Uninherited)));
            var edited = doc.WithRedLines([new(0.4, 60000 / 123.456)]);
            Assert.True(edited.AsSpan().StartsWith(Encoding.UTF8.Preamble));
            Assert.DoesNotContain("\r", Encoding.UTF8.GetString(edited), StringComparison.Ordinal);
            var points = OsuFile.Parse(edited).TimingPoints;
            Assert.True(points[0].Uninherited);
            Assert.False(points[1].Uninherited);
            Assert.Equal(0, points[0].Time);
            Assert.StartsWith("-1,", new TimingPoint(-1.5, 500).ToOsuLine());
            Assert.Equal(points, TimingClipboard.Parse(TimingClipboard.Write(points)));
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }
}
