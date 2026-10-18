using TimingAnalyzer.Editor;

namespace TimingAnalyzer.Editor.Tests;

public class BeatSnapTests
{
    public static TheoryData<int> AllDivisors()
    {
        var data = new TheoryData<int>();
        foreach (int divisor in BeatSnap.Divisors) data.Add(divisor);
        return data;
    }

    [Theory]
    [MemberData(nameof(AllDivisors))]
    public void StepsToTheNeighbouringTickForEveryDivisorIncludingNonIntegerTicks(int divisor)
    {
        TimingPoint[] reds = [new(1000, 500)];
        double step = 500.0 / divisor, tick = 1000 + 3 * step;
        // On the tick, or within the 0.5 ms tolerance of it, both directions reach the neighbour.
        foreach (double position in new[] { tick, tick + 0.4, tick - 0.4 })
        {
            Assert.Equal(tick + step, BeatSnap.Step(reds, position, divisor, 1), 9);
            Assert.Equal(tick - step, BeatSnap.Step(reds, position, divisor, -1), 9);
        }
        // Beyond the tolerance the tick itself is the nearest one in that direction.
        Assert.Equal(tick, BeatSnap.Step(reds, tick + 0.6, divisor, -1), 9);
        Assert.Equal(tick, BeatSnap.Step(reds, tick - 0.6, divisor, 1), 9);
    }

    [Fact]
    public void CrossingARedLineLandsOnItAndUsesTheNextSectionsTicks()
    {
        TimingPoint[] reds = [new(0, 600), new(1000, 400)];
        Assert.Equal(1000, BeatSnap.Step(reds, 700, 1, 1));   // Section 0 has no tick before the red line.
        Assert.Equal(1000, BeatSnap.Step(reds, 600, 1, 1));
        Assert.Equal(1400, BeatSnap.Step(reds, 1000, 1, 1));
        Assert.Equal(600, BeatSnap.Step(reds, 1000, 1, -1));  // Back into the truncated section.
        Assert.Equal(1000, BeatSnap.Step(reds, 1200, 1, -1));
        Assert.Equal(1000, BeatSnap.Step(reds, 900, 4, 1));   // 1050 would be past the red line.
        Assert.Equal(900, BeatSnap.Step(reds, 1000, 4, -1));
        Assert.Equal(1000, BeatSnap.Step(reds, 1100, 4, -1));
    }

    [Fact]
    public void TicksExtendBackwardsBeforeTheFirstRedLine()
    {
        TimingPoint[] reds = [new(1000, 500)];
        Assert.Equal(500, BeatSnap.Step(reds, 0, 1, 1));
        Assert.Equal(-500, BeatSnap.Step(reds, 0, 1, -1));
        Assert.Equal(0, BeatSnap.Step(reds, 200, 1, -1));
        Assert.Equal(1000, BeatSnap.Step(reds, 600, 1, 1));
        Assert.Equal(123, BeatSnap.Step([], 123, 4, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => BeatSnap.Step(reds, 0, 10, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => BeatSnap.Step(reds, double.NaN, 4, 1));
    }

    [Fact]
    public async Task ViewModelStepsWithTheChosenDivisorClampsToTheSongAndKeepsThePlayheadVisible()
    {
        string path = Path.GetTempFileName();
        try
        {
            var engine = new ViewModelTests.FakePlayback();
            using var vm = new EditorViewModel(_ => Task.FromResult(AudioContent.Create(new double[20000], 1000)), _ => engine);
            await vm.LoadAsync(path);
            vm.Edits.Replace([new(0, 500)]);
            Assert.Equal(4, vm.SnapDivisor);
            vm.SelectedSnap = new SnapOption(10); // Not an osu! divisor: ignored.
            Assert.Equal(4, vm.SnapDivisor);
            vm.SelectedSnap = vm.SnapOptions.Single(o => o.Divisor == 2);
            Assert.Equal("1/2", vm.SelectedSnap.ToString());
            Assert.Equal(new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 12, 16 }, vm.SnapOptions.Select(o => o.Divisor));
            vm.StepSnap(1); Assert.Equal(250, engine.PositionMs); Assert.Equal(250, vm.PositionMs);
            vm.StepSnap(-1); Assert.Equal(0, vm.PositionMs);
            vm.StepSnap(-1); Assert.Equal(0, vm.PositionMs); // Clamped at the start.
            Assert.Equal(0, vm.StartMs);
            vm.Seek(19900); vm.StepSnap(1);
            Assert.Equal(20000, vm.PositionMs);
            Assert.Equal(10000, vm.StartMs); // Scrolled so the playhead is on screen.
            vm.StepSnap(1); Assert.Equal(20000, vm.PositionMs); // Clamped at the end.
            vm.TogglePlay(); vm.Seek(1000); vm.StepSnap(1);
            Assert.True(engine.IsPlaying); Assert.Equal(1250, engine.PositionMs); // Seeks while playing.
        }
        finally { File.Delete(path); }
    }
}
