using System.Globalization;
using TimingAnalyzer.Editor;

namespace TimingAnalyzer.Editor.Tests;

public class OsuTimingExportTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-36)]
    [InlineData(15)]
    public void SavedShiftCanCustomizeClipboardCompensation(int legacyShift)
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            string path = Path.Combine(directory.FullName, "settings.json");
            File.WriteAllText(path, "{\"OsuCopyShiftMs\":" + legacyShift.ToString(CultureInfo.InvariantCulture) + "}");
            var settings = EditorSettings.Load(path);
            using var vm = new EditorViewModel(_ => throw new InvalidOperationException(), _ => throw new InvalidOperationException());
            vm.ApplySettings(settings);
            Assert.Equal(legacyShift, vm.OsuCopyShiftMs);
            Assert.Equal(legacyShift, vm.CaptureSettings().OsuCopyShiftMs);
        }
        finally { directory.Delete(true); }
    }

    [Fact]
    public void CopyMovesEveryRowBy33AndPreservesNegativeTimesFractionsAndSampleSettings()
    {
        TimingPoint[] points = [new(10, 500, 3, 2, 4, 60, true, 8), new(10, -50, 3, 3, 7, 30, false, 1),
            new(19.75, -100, 4, 1, 0, 80, false), new(2000, 400)];
        var original = points.ToArray();
        var before = TimingClipboard.Parse(TimingClipboard.Write(points));
        var copied = TimingClipboard.Parse(OsuTimingExport.Write(points));
        Assert.Equal(before.Select(p => p with { Time = p.Time - 33 }), copied);
        Assert.Equal(original, points);
        Assert.StartsWith("967,500,", OsuTimingExport.Write([new(1000, 500)]));
    }

    [Fact]
    public async Task CopyUsesEditedTimingAndNormalizesBeforeShiftingWithoutChangingPreviewOrSave()
    {
        string fixture = Path.Combine(AppContext.BaseDirectory, "Fixtures", "steady-multi.osu");
        using var vm = new EditorViewModel(_ => Task.FromResult(AudioContent.Create(new double[100], 1000)),
            _ => new ViewModelTests.FakePlayback());
        await vm.LoadAsync(fixture);
        vm.Edits.MoveRedLine(0, vm.Edits.RedLines[0].Time + 1);
        var reds = vm.Edits.RedLines.ToArray();
        var original = vm.ClipboardText();
        var save = vm.Document!.WithRedLines(reds);
        var expected = TimingClipboard.Parse(original);
        var copied = vm.OsuClipboardText();
        Assert.Equal(expected.Select(p => p with { Time = p.Time - 33 }), TimingClipboard.Parse(copied));
        Assert.Equal(copied, vm.OsuClipboardText()); // A second copy cannot accumulate the correction.
        Assert.Equal(reds, vm.Edits.RedLines);
        Assert.Equal(original, vm.ClipboardText());
        Assert.Equal(save, vm.Document.WithRedLines(vm.Edits.RedLines));
        Assert.DoesNotContain("[TimingPoints]", copied);
    }

    [Fact]
    public void CopySettingsRoundTripAndUseInvariantCulture()
    {
        var culture = CultureInfo.CurrentCulture;
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("th-TH");
            using var vm = new EditorViewModel(_ => throw new InvalidOperationException(), _ => throw new InvalidOperationException());
            string path = Path.Combine(directory.FullName, "settings.json");
            vm.CaptureSettings().Save(path);
            using var other = new EditorViewModel(_ => throw new InvalidOperationException(), _ => throw new InvalidOperationException());
            other.ApplySettings(EditorSettings.Load(path));
            Assert.Equal(-33, other.OsuCopyShiftMs);
            Assert.StartsWith("-23,500,", OsuTimingExport.Write([new(10, 500)]));
            other.ApplySettings(new EditorSettings());
            Assert.Equal(-33, other.OsuCopyShiftMs);
        }
        finally { CultureInfo.CurrentCulture = culture; directory.Delete(true); }
    }
}
