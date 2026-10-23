using TimingAnalyzer.Editor;

namespace TimingAnalyzer.Editor.Tests;

/// <summary>Held ←/→ run on like Audacity's cursor keys, and ❮ BPM ❯ tunes a red line without typing (docs/PHASE1-STATUS.md E42).</summary>
public class ScrubAndNudgeTests
{
    [Fact]
    public void ScrubSpeedWaitsForTheDelayThenStartsSlowDoublesAndStops()
    {
        Assert.Equal(0, ScrubSpeed.MsPerSecond(0, 10000));
        Assert.Equal(0, ScrubSpeed.MsPerSecond(ScrubSpeed.DelayMs - 1, 10000));
        double start = ScrubSpeed.MsPerSecond(ScrubSpeed.DelayMs, 10000);
        Assert.Equal(ScrubSpeed.StartSpansPerSecond * 10000, start, 6);
        Assert.Equal(2 * start, ScrubSpeed.MsPerSecond(ScrubSpeed.DelayMs + ScrubSpeed.DoublingMs, 10000), 6);
        Assert.Equal(ScrubSpeed.MaxSpansPerSecond * 10000, ScrubSpeed.MsPerSecond(60000, 10000), 6); // The cap.
        // A zoomed-in view stays fine and a very zoomed-out one stays bounded.
        Assert.Equal(ScrubSpeed.MinMsPerSecond, ScrubSpeed.MsPerSecond(400, 100));
        Assert.Equal(ScrubSpeed.MaxMsPerSecond, ScrubSpeed.MsPerSecond(60000, 1_000_000));
        Assert.Equal(0, ScrubSpeed.MsPerSecond(double.NaN, 10000));
    }

    [Fact]
    public async Task HoldingAnArrowStepsOnceThenRunsOnFasterAndSeeksOnlyWhenItGoesUp()
    {
        using var s = await Session.Open();
        var vm = s.Model;
        vm.ArrowDown(1); // One 1/4 beat of a 500 ms beat.
        Assert.Equal(125, vm.PositionMs);
        int seeks = s.Engine.Seeks;
        for (int t = 30; t < 300; t += 30) { s.Now = t; vm.ArrowDown(1); vm.UpdateFrame(); } // OS key repeats before the delay.
        Assert.Equal(125, vm.PositionMs);
        Assert.Equal(seeks, s.Engine.Seeks);

        double previous = vm.PositionMs, previousStep = 0;
        for (int t = 300; t <= 3000; t += 16)
        {
            s.Now = t; vm.ArrowDown(1); vm.UpdateFrame();
            double step = vm.PositionMs - previous;
            Assert.True(step >= 0);
            if (t > 400 && t < 2600) Assert.True(step >= previousStep - 1e-9, "the run-on never slows down"); // t = 300 itself moves 0.
            previous = vm.PositionMs; previousStep = step;
        }
        Assert.True(vm.PositionMs > 125 + 5000, "three seconds of holding cover far more than the key repeats did");
        Assert.Equal(seeks, s.Engine.Seeks); // The audio is not seeked per frame.
        double at = vm.PositionMs;
        vm.ArrowUp(1);
        Assert.Equal(seeks + 1, s.Engine.Seeks);
        Assert.Equal(at, s.Engine.Position, 6);
        Assert.Equal(at, vm.PositionMs, 6);
        s.Now = 3100; vm.UpdateFrame();
        Assert.Equal(at, vm.PositionMs, 6); // Nothing runs on after the key went up.
    }

    [Fact]
    public async Task AReleaseBeforeTheDelayIsJustAStepAndTheOtherArrowsReleaseDoesNotStopTheRun()
    {
        using var s = await Session.Open();
        var vm = s.Model;
        vm.ArrowDown(1); s.Now = 100; vm.UpdateFrame(); vm.ArrowUp(1);
        Assert.Equal(125, vm.PositionMs);
        Assert.Equal(1, s.Engine.Seeks); // The one step's seek; the release before the delay adds none.
        s.Now = 1000; vm.UpdateFrame();
        Assert.Equal(125, vm.PositionMs);

        s.Now = 2000; vm.ArrowDown(1);
        s.Now = 2400; vm.UpdateFrame(); s.Now = 2800; vm.UpdateFrame();
        double moving = vm.PositionMs;
        Assert.True(moving > 125 + 125);
        vm.ArrowUp(-1); // Left went up, but right is the one held.
        s.Now = 2900; vm.ArrowDown(1); vm.UpdateFrame();
        Assert.True(vm.PositionMs > moving);
        vm.ArrowUp(1);
    }

    [Fact]
    public async Task ARunOnPausesTheSoundAndPlaysOnFromTheNewPlaceWhenTheKeyGoesUp()
    {
        using var s = await Session.Open();
        var vm = s.Model;
        vm.Seek(10000); vm.TogglePlay();
        Assert.True(s.Engine.IsPlaying);
        s.Now = 1000; vm.ArrowDown(-1);
        s.Now = 1300; vm.UpdateFrame();
        Assert.False(s.Engine.IsPlaying); // The sound would otherwise play on from the old place.
        s.Now = 1316; vm.UpdateFrame(); s.Now = 2000; vm.UpdateFrame();
        double head = vm.PositionMs;
        Assert.True(head < 10000 - 125);
        vm.ArrowUp(-1);
        Assert.True(s.Engine.IsPlaying);
        Assert.Equal(head, s.Engine.Position, 6);
    }

    [Fact]
    public async Task TheRunOnStopsAtBothEndsAndAKeyThatNeverComesUpIsReleasedAfterTwoSeconds()
    {
        using var s = await Session.Open();
        var vm = s.Model;
        vm.ArrowDown(-1);
        for (int t = 300; t < 4000; t += 16) { s.Now = t; vm.ArrowDown(-1); vm.UpdateFrame(); }
        Assert.Equal(0, vm.PositionMs);
        vm.ArrowUp(-1);
        vm.Seek(Session.Duration - 1000);
        s.Now = 5000; vm.ArrowDown(1);
        for (int t = 5300; t < 9000; t += 16) { s.Now = t; vm.ArrowDown(1); vm.UpdateFrame(); }
        Assert.Equal(Session.Duration, vm.PositionMs);
        vm.ArrowUp(1);

        vm.Seek(1000); s.Now = 10000; vm.ArrowDown(1);
        s.Now = 10400; vm.UpdateFrame(); s.Now = 10800; vm.UpdateFrame();
        Assert.True(vm.PositionMs > 1125);
        s.Now = 10800 + EditorViewModel.StuckKeyMs + 100; vm.UpdateFrame(); // No key repeat for too long: its key-up was lost.
        double stopped = vm.PositionMs;
        s.Now += 500; vm.UpdateFrame();
        Assert.Equal(stopped, vm.PositionMs, 6);
        Assert.Equal(stopped, s.Engine.Position, 6);
    }

    [Fact]
    public async Task ShiftArrowKeepsExtendingTheHighlightedRangeWhileHeld()
    {
        using var s = await Session.Open();
        var vm = s.Model;
        vm.Seek(1000);
        vm.ArrowDown(1, extend: true);
        Assert.Equal((1000.0, 1125.0), vm.Range!.Value);
        for (int t = 300; t <= 1400; t += 16) { s.Now = t; vm.ArrowDown(1, extend: true); vm.UpdateFrame(); }
        var range = vm.Range!.Value;
        Assert.Equal(1000, range.Start);
        Assert.Equal(vm.PositionMs, range.End, 6);
        Assert.True(range.End > 2000);
        vm.ArrowUp();
        Assert.Equal(range, vm.Range!.Value);
    }

    [Fact]
    public async Task TheViewFollowsTheRunOnInsteadOfLosingTheHead()
    {
        using var s = await Session.Open();
        var vm = s.Model;
        vm.Seek(100);
        s.Now = 0; vm.ArrowDown(1);
        for (int t = 300; t < 3000; t += 16) { s.Now = t; vm.ArrowDown(1); vm.UpdateFrame(); }
        Assert.True(vm.PositionMs > vm.SpanMs);
        Assert.InRange(vm.PositionMs, vm.StartMs, vm.StartMs + vm.SpanMs);
        vm.ArrowUp();
    }

    // ---- BPM nudge ----

    [Theory]
    [InlineData(143, 0.01, 1, 143.01)]
    [InlineData(143, 0.01, -1, 142.99)]
    [InlineData(142.997, 0.01, 1, 143)]
    [InlineData(142.997, 0.01, -1, 142.99)]
    [InlineData(142.997, 0.001, 1, 142.998)]
    [InlineData(142.9996, 0.001, -1, 142.999)]
    [InlineData(142.5, 1, 1, 143)]
    [InlineData(143, 1, -1, 142)]
    [InlineData(120.0000000001, 0.1, 1, 120.1)]
    [InlineData(1.005, 1, -1, 1)]
    [InlineData(999.5, 1, 1, 1000)]
    [InlineData(1000, 1, 1, 1000)]
    public void NextBpmMovesToTheNextMultipleOfTheStepAtOsuPrecision(double bpm, double step, int direction, double expected) =>
        Assert.Equal(expected, EditorViewModel.NextBpm(bpm, step, direction), 9);

    [Fact]
    public async Task NudgingKeepsTheTimeUpdatesTheFieldAndAHeldRunIsOneUndoStep()
    {
        using var s = await Session.Open();
        var vm = s.Model;
        vm.TimeText = "437"; vm.BpmText = "143"; vm.ApplyFields();
        Assert.Equal(143, vm.Edits.RedLines[0].Bpm, 9);
        vm.SelectedBpmStep = 0.01;
        for (int i = 0; i < 5; i++) { s.Now = 100 * i; vm.NudgeBpmCommand.Execute("1"); }
        Assert.Equal(437, vm.Edits.RedLines[0].Time);
        Assert.Equal(143.05, vm.Edits.RedLines[0].Bpm, 9);
        Assert.Equal("143.05", vm.BpmText);
        s.Now = 450; vm.NudgeBpmCommand.Execute("-1");
        Assert.Equal(143.04, vm.Edits.RedLines[0].Bpm, 9);
        vm.Undo(); // The whole run, not the last tick.
        Assert.Equal(143, vm.Edits.RedLines[0].Bpm, 9);
        Assert.Equal(437, vm.Edits.RedLines[0].Time);
        vm.Redo();
        Assert.Equal(143.04, vm.Edits.RedLines[0].Bpm, 9);
    }

    [Fact]
    public async Task ANudgeAfterAPauseAnotherEditOrAnotherSelectionStartsANewUndoStep()
    {
        using var s = await Session.Open();
        var vm = s.Model;
        vm.TimeText = "1000"; vm.BpmText = "150"; vm.ApplyFields();
        vm.SelectedBpmStep = 0.1;
        s.Now = 0; vm.NudgeBpm(1); s.Now = 100; vm.NudgeBpm(1);
        s.Now = 100 + EditorViewModel.NudgeMergeMs + 1; vm.NudgeBpm(1); // A pause.
        Assert.Equal(150.3, vm.Edits.RedLines[0].Bpm, 9);
        vm.Undo(); Assert.Equal(150.2, vm.Edits.RedLines[0].Bpm, 9);
        vm.Undo(); Assert.Equal(150, vm.Edits.RedLines[0].Bpm, 9);

        vm.Redo(); vm.Redo();
        s.Now = 5000; vm.NudgeBpm(1);
        vm.ShiftMsCommand.Execute("3"); // Another edit in between.
        s.Now = 5100; vm.NudgeBpm(1);
        Assert.Equal(150.5, vm.Edits.RedLines[0].Bpm, 9);
        Assert.Equal(1003, vm.Edits.RedLines[0].Time);
        vm.Undo(); Assert.Equal(150.4, vm.Edits.RedLines[0].Bpm, 9);
        Assert.Equal(1003, vm.Edits.RedLines[0].Time);
    }

    [Fact]
    public async Task NudgesActOnEverySelectedLineEachOnItsOwnStepGridAndCtrlShiftTakesTenSteps()
    {
        using var s = await Session.Open();
        var vm = s.Model;
        vm.Paste("1000,420.0,4,1,0,100,1,0\n5000,401.2345,4,1,0,100,1,0\n9000,500,4,1,0,100,1,0", replaceAll: true);
        vm.SelectAll();
        Assert.Equal(3, vm.SelectedIndices.Count);
        var before = vm.Edits.RedLines.Select(p => OsuPrecision.Bpm(p.Bpm)).ToArray();
        vm.SelectedBpmStep = 0.1;
        vm.NudgeBpm(1);
        for (int i = 0; i < 3; i++)
            Assert.Equal(EditorViewModel.NextBpm(before[i], 0.1, 1), OsuPrecision.Bpm(vm.Edits.RedLines[i].Bpm), 9);
        Assert.All(vm.Edits.RedLines.Select((p, i) => p.Time), time => Assert.True(time % 1 == 0));
        Assert.Equal(new[] { 1000.0, 5000.0, 9000.0 }, vm.Edits.RedLines.Select(p => p.Time));
        s.Now = 10; vm.NudgeBpm(-1, 10); // Ctrl+Shift: ten steps = the whole BPM below.
        for (int i = 0; i < 3; i++)
            Assert.Equal(EditorViewModel.NextBpm(EditorViewModel.NextBpm(before[i], 0.1, 1), 1, -1), vm.Edits.RedLines[i].Bpm, 6);
        Assert.Equal(3, vm.SelectedIndices.Count);
    }

    [Fact]
    public async Task WithNothingSelectedANudgeSaysSoAndChangesNothing()
    {
        using var s = await Session.Open();
        var vm = s.Model;
        vm.ClearSelection();
        double bpm = vm.Edits.RedLines[0].Bpm;
        vm.NudgeBpm(1);
        Assert.Equal(bpm, vm.Edits.RedLines[0].Bpm);
        Assert.Contains("Select", vm.Status, StringComparison.Ordinal);
        Assert.False(vm.Edits.History.CanUndo);
    }

    [Fact]
    public void TheBpmStepIsRemembered()
    {
        Assert.Equal(0.01, new EditorSettings().BpmStep);
        using var vm = new EditorViewModel(_ => Task.FromResult(AudioContent.Create(new double[100], 1000)), _ => new FakePlayback());
        vm.ApplySettings(new EditorSettings { BpmStep = 0.1 });
        Assert.Equal(0.1, vm.SelectedBpmStep);
        Assert.Equal(0.1, vm.CaptureSettings().BpmStep);
        vm.ApplySettings(new EditorSettings { BpmStep = 0.37 }); // Not one of the choices: ignored.
        Assert.Equal(0.1, vm.SelectedBpmStep);
    }

    private sealed class Session : IDisposable
    {
        public const int Duration = 120000;
        private readonly string path = Path.GetTempFileName();
        public double Now { get; set; }
        public FakePlayback Engine { get; } = new();
        public EditorViewModel Model { get; }
        private Session() => Model = new(_ => Task.FromResult(AudioContent.Create(new double[Duration], 1000)), _ => Engine, () => Now);
        public static async Task<Session> Open()
        {
            var session = new Session();
            await session.Model.LoadAsync(session.path);
            session.Model.Select(0);
            return session;
        }
        public void Dispose() { Model.Dispose(); File.Delete(path); }
    }

    private sealed class FakePlayback : IPlaybackEngine
    {
        public double DurationMs => Session.Duration;
        public double Position { get; set; }
        public int Seeks { get; private set; }
        public double PositionMs => Position;
        public bool IsPlaying { get; set; }
        public bool ClickEnabled { get; set; }
        public double MusicVolume { get; set; }
        public double ClickVolume { get; set; }
        public double Speed { get; set; } = 1;
        public void Play() { if (Position >= DurationMs) Position = 0; IsPlaying = true; }
        public void Pause() => IsPlaying = false;
        public void Seek(double timeMs) { Seeks++; Position = Math.Clamp(timeMs, 0, DurationMs); }
        public void SetTiming(IEnumerable<TimingPoint> lines) { }
        public void Dispose() { }
    }
}
