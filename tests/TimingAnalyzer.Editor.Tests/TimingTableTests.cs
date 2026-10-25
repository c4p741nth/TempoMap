using TimingAnalyzer.Editor;

namespace TimingAnalyzer.Editor.Tests;

/// <summary>The timing table (All / Timing / Inherited), laid out as osu!'s Timing and Control Points list.</summary>
public class TimingTableTests
{
    [Fact]
    public async Task TabsListRedAndGreenLinesInOsuOrderAndColumns()
    {
        string dir = Directory.CreateTempSubdirectory().FullName, path = Path.Combine(dir, "map.osu");
        File.WriteAllText(path, "osu file format v14\r\n\r\n[General]\r\nAudioFilename: missing.ogg\r\n\r\n[TimingPoints]\r\n" +
            "498,428.571428571429,4,1,1,40,1,0\r\n21069,-52.6315789473684,4,1,1,40,0,0\r\n21122,500,4,1,1,40,1,0\r\n" +
            "21122,-1000,4,1,1,40,0,0\r\n30000,400,4,2,1,60,1,8\r\n40000,-100,4,3,2,70,0,1\r\n50000,375,3,0,0,60,1,0\r\n");
        var vm = new EditorViewModel(_ => Task.FromResult(AudioContent.Create(new double[1000], 1000)), _ => new ViewModelTests.FakePlayback());
        vm.AutoNormalizeSv = true; // This table fixture exercises generated normalization rows.
        await vm.LoadAsync(path);
        Assert.Equal("All", vm.TableTabs[vm.TableTab]);
        // The red line first where a green one shares its time, as osu! orders them.
        Assert.Equal(["R00:00:498", "G00:00:498", "G00:21:069", "R00:21:122", "G00:21:122", "R00:30:000", "G00:30:000", "G00:40:000", "R00:50:000", "G00:50:000"],
            vm.TableRows.Select(r => (r.IsRed ? "R" : "G") + r.Offset));
        Assert.Equal(["140", "x1.00", "x1.90", "120", "x0.12", "150", "x0.93", "x0.93", "160", "x0.88"], vm.TableRows.Select(r => r.Value));
        Assert.Equal(["4/4", "", "", "4/4", "", "4/4 nb", "", "", "3/4", ""], vm.TableRows.Select(r => r.Meter));
        Assert.Equal(["N:C1", "N:C1", "N:C1", "N:C1", "N:C1", "S:C1", "S:C1", "D:C2", "A:C0", "A:C0"], vm.TableRows.Select(r => r.Sample));
        Assert.Equal(["40%", "40%", "40%", "40%", "40%", "60%", "60%", "70%", "60%", "60%"], vm.TableRows.Select(r => r.Volume));
        Assert.Equal(["", "", "", "", "", "", "", "✓", "", ""], vm.TableRows.Select(r => r.Kiai));
        Assert.Equal([0, -1, -1, 1, -1, 2, -1, -1, 3, -1], vm.TableRows.Select(r => r.RedIndex));
        vm.TableTab = 1;
        Assert.All(vm.TableRows, r => Assert.True(r.IsRed));
        Assert.Equal(4, vm.TableRows.Count);
        vm.TableTab = 2;
        Assert.Equal(6, vm.TableRows.Count(r => r.IsGreen));
        Assert.Equal(vm.TableRows.Select(r => r.Point.Time).Order(), vm.TableRows.Select(r => r.Point.Time));
        vm.TableTab = 7; // clamped
        Assert.Equal(2, vm.TableTab);
        // Picking rows selects their red lines without moving the playhead; their rows copy as osu! rows.
        vm.TableTab = 0;
        vm.SelectRows([1, 3], 3);
        Assert.Equal([1, 3], vm.SelectedIndices);
        Assert.Equal(3, vm.SelectedRow!.Index);
        Assert.Equal(0, vm.PositionMs);
        var text = EditorViewModel.RowsClipboardText(vm.TableRows.Where(r => r.RedIndex is 1 or 3 || r.Point.Time == 40000));
        var copied = TimingClipboard.Parse(text);
        Assert.Equal([21122.0, 40000, 50000], copied.Select(p => p.Time));
        Assert.Equal(140.0 / 150, copied[1].Sv, 5);
        Assert.Equal((3, 2, 70, 1), (copied[1].SampleSet, copied[1].SampleIndex, copied[1].Volume, copied[1].Effects));
    }
}
