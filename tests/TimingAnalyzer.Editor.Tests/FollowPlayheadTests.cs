using TimingAnalyzer.Editor;

namespace TimingAnalyzer.Editor.Tests;

public class FollowPlayheadTests
{
    [Theory]
    [InlineData(6000, 9000, 30000, 3000)]
    [InlineData(6000, 3000, 30000, 5000)]
    [InlineData(0, 9000, 30000, 0)]
    [InlineData(2000, 9000, 30000, 0)]
    [InlineData(29000, 9000, 30000, 21000)]
    [InlineData(30000, 9000, 30000, 21000)]
    [InlineData(500, 1000, 500, 0)]
    [InlineData(0, 1000, 0, 0)]
    public void ViewportUsesOneThirdOfZoomWidthAndClamps(double position, double width, double duration, double expected)
        => Assert.Equal(expected, FollowViewport.Start(position, width, duration), 8);

    [Theory]
    [InlineData(0.75)]
    [InlineData(1.0)]
    public void ClockExtrapolatesAtSourceSpeedAndStopsWhenPaused(double speed)
    {
        var clock = new PlayheadClock();
        Assert.Equal(0, clock.PositionAt(0));
        clock.Synchronize(1000, 100, speed, true);
        Assert.Equal(1000 + 16 * speed, clock.PositionAt(116), 8);
        Assert.Equal(1000 + 100 * speed, clock.PositionAt(200), 8);
        clock.Synchronize(1070, 200, speed, false);
        Assert.Equal(1070, clock.PositionAt(10000));
    }

    [Theory]
    [InlineData(-20)]
    [InlineData(20)]
    public void SmallDriftIsContinuousAndEasesTowardEngine(double drift)
    {
        var clock = new PlayheadClock();
        clock.Synchronize(1000, 0, 1, true);
        clock.Synchronize(1100 + drift, 100, 1, true);
        Assert.Equal(1100, clock.PositionAt(100), 8);
        double after = clock.PositionAt(220);
        Assert.Equal(1220 + drift - drift / Math.E, after, 8);
        Assert.InRange(Math.Abs(clock.PositionAt(700) - (1700 + drift)), 0, 0.2);
    }

    [Theory]
    [InlineData(0.75)]
    [InlineData(1.0)]
    public void NegativeCorrectionsNeverMoveBackwardsEvenWhenCorrectionExceedsFrameAdvance(double speed)
    {
        var clock = new PlayheadClock();
        clock.Synchronize(1000, 0, speed, true);
        // Less than the snap threshold, but larger than the easing time times speed.
        clock.Synchronize(800, 0, speed, true);
        double previous = 1000;
        for (int frame = 0; frame <= 100; frame++)
        {
            double position = clock.PositionAt(frame * 16);
            Assert.True(position >= previous);
            previous = position;
        }
        Assert.Equal(800 + 1600 * speed, previous, 3);
    }

    [Fact]
    public void ExplicitSmallSeeksAndLargeDiscontinuitiesSnapInBothDirections()
    {
        var clock = new PlayheadClock();
        clock.Synchronize(1000, 0, 1, true);
        clock.Synchronize(990, 10, 1, true, snap: true);
        Assert.Equal(990, clock.PositionAt(10));
        clock.Synchronize(1000, 10, 1, true, snap: true);
        Assert.Equal(1000, clock.PositionAt(10));
        clock.Synchronize(10000, 20, 1, true);
        Assert.Equal(10000, clock.PositionAt(20));
        clock.Synchronize(100, 30, 1, true);
        Assert.Equal(100, clock.PositionAt(30));
    }

    [Fact]
    public void SpeedChangeKeepsContinuityAndUsesNewSpeed()
    {
        var clock = new PlayheadClock();
        clock.Synchronize(1000, 0, 1, true);
        clock.Synchronize(1100, 100, 0.75, true);
        Assert.Equal(1100, clock.PositionAt(100));
        Assert.Equal(1175, clock.PositionAt(200));
    }

    [Fact]
    public async Task FramesInterpolateBetweenThrottledReadsAndAutomaticSliderEchoDoesNotSuspend()
    {
        using var session = await Session.Open();
        var vm = session.Model;
        Assert.True(vm.FollowPlayhead);
        vm.Seek(6000); vm.TogglePlay();
        int reads = session.Engine.Reads;
        Assert.Equal(6000 - vm.SpanMs / 3, vm.StartMs, 8);
        vm.ManualStartMs = vm.StartMs;
        session.Now = 16; vm.UpdateFrame();
        Assert.Equal(reads, session.Engine.Reads);
        Assert.Equal(6016, vm.PositionMs, 8);
        Assert.Equal(6016 - vm.SpanMs / 3, vm.StartMs, 8);
        session.Engine.Position = 6050; session.Now = 50; vm.UpdateFrame();
        Assert.Equal(reads + 1, session.Engine.Reads);
        session.Now = 66; vm.UpdateFrame();
        Assert.Equal(6066, vm.PositionMs, 8);
        Assert.Equal(6066 - vm.SpanMs / 3, vm.StartMs, 8);
        vm.SelectedSpeed = "0.75";
        session.Now = 82; vm.UpdateFrame();
        Assert.InRange(vm.PositionMs, 6070, 6078);
    }

    [Fact]
    public async Task PlayingIntoARedLineSelectsItButPausedSeeksDoNot()
    {
        using var session = await Session.Open();
        var vm = session.Model;
        vm.Edits.Replace([new(0, 500), new(5000, 400), new(10000, 375)]);
        vm.Select(2);
        vm.Seek(4000); // Paused: the user's selection stays (Delete and the fields act on it).
        Assert.Equal(2, vm.SelectedRow!.Index);
        vm.TogglePlay(); // Playing from 4000: the section under the playhead is selected at once.
        Assert.Equal(0, vm.SelectedRow!.Index);
        session.Engine.Position = 5100; session.Now = 1100; vm.UpdateFrame();
        Assert.Equal(1, vm.SelectedRow!.Index); // Passed the red line at 5000.
        vm.Select(2); // The user picks another line inside the same section: kept until the next red line.
        session.Engine.Position = 5300; session.Now = 1300; vm.UpdateFrame();
        Assert.Equal(2, vm.SelectedRow!.Index);
        vm.Select(0);
        vm.FollowPlayhead = false; // Follow off: neither the view nor the list follows.
        session.Engine.Position = 10100; session.Now = 6100; vm.UpdateFrame();
        Assert.Equal(0, vm.SelectedRow!.Index);
    }

    [Fact]
    public async Task ZoomKeepsFollowingAtOneThirdOfTheNewWidth()
    {
        using var session = await Session.Open();
        var vm = session.Model;
        vm.Seek(6000); vm.TogglePlay();
        vm.Zoom(0.5, 0.9); // User request: zooming must not stop follow.
        Assert.Equal(6000 - vm.SpanMs / 3, vm.StartMs, 8);
        vm.Seek(12000); session.Now = 16; vm.UpdateFrame(); // The playhead advances 16 ms while playing.
        Assert.Equal(vm.PositionMs - vm.SpanMs / 3, vm.StartMs, 8);
        vm.Zoom(4, 0.1);
        Assert.Equal(FollowViewport.Start(vm.PositionMs, vm.SpanMs, vm.DurationMs), vm.StartMs, 8);
        vm.TogglePlay();
        double paused = vm.StartMs, width = vm.SpanMs;
        vm.Zoom(0.5, 0.5); // Paused: an ordinary zoom about the view centre, no follow jump.
        Assert.Equal(Math.Clamp(paused + width / 4, 0, vm.ScrollMaximum), vm.StartMs, 8);
    }

    [Theory]
    [InlineData("wheel")]
    [InlineData("slider")]
    public async Task ScrollingAheadWaitsForThePlayheadThenResumesWithoutAJump(string navigation)
    {
        using var session = await Session.Open();
        var vm = session.Model;
        vm.Seek(6000); vm.TogglePlay();
        if (navigation == "wheel") vm.Scroll(1500);
        else vm.ManualStartMs += 1500;
        double manualStart = vm.StartMs;
        Assert.Equal(6000 - vm.SpanMs / 3 + 1500, manualStart, 8);
        session.Engine.Position = 7000; session.Now = 1000; vm.UpdateFrame();
        Assert.True(vm.FollowPlayhead);
        Assert.Equal(manualStart, vm.StartMs); // Not yet at the anchor of the scrolled view.
        session.Engine.Position = 7600; session.Now = 1600; vm.UpdateFrame();
        Assert.Equal(7600 - vm.SpanMs / 3, vm.StartMs, 8); // Reached it: following again, 100 ms on from the view.
        session.Engine.Position = 8000; session.Now = 2000; vm.UpdateFrame();
        Assert.Equal(8000 - vm.SpanMs / 3, vm.StartMs, 8);
    }

    [Fact]
    public async Task ScrollingBackStaysForLookingUntilSeekPlayOrCheckbox()
    {
        using var session = await Session.Open();
        var vm = session.Model;
        vm.Seek(12000); vm.TogglePlay();
        vm.Scroll(-3000); double manualStart = vm.StartMs;
        session.Engine.Position = 14000; session.Now = 2000; vm.UpdateFrame();
        Assert.Equal(manualStart, vm.StartMs); // Looking back while the song plays on.
        vm.Seek(13000); // A click-seek (or ←/→) resumes following at once.
        Assert.Equal(13000 - vm.SpanMs / 3, vm.StartMs, 8);
        vm.ManualStartMs -= 2000; manualStart = vm.StartMs;
        session.Engine.Position = 13500; session.Now = 2500; vm.UpdateFrame();
        Assert.Equal(manualStart, vm.StartMs);
        vm.FollowPlayhead = false; vm.FollowPlayhead = true; // The checkbox resumes.
        Assert.Equal(13500 - vm.SpanMs / 3, vm.StartMs, 8);
        vm.Scroll(-2000);
        vm.TogglePlay(); vm.TogglePlay(); // So does Play.
        Assert.Equal(13500 - vm.SpanMs / 3, vm.StartMs, 8);
        vm.Scroll(1000); manualStart = vm.StartMs;
        vm.FollowPlayhead = false;
        vm.Seek(15000); // Follow off: seeking never moves the view.
        Assert.Equal(manualStart, vm.StartMs);
        vm.FollowPlayhead = true;
        Assert.Equal(15000 - vm.SpanMs / 3, vm.StartMs, 8);
    }

    [Fact]
    public async Task SeekingKeepsFollowAndSnapsSmallSeeksWhilePauseKeepsViewStill()
    {
        using var session = await Session.Open();
        var vm = session.Model;
        vm.TogglePlay(); vm.Seek(12000);
        Assert.Equal(12000 - vm.SpanMs / 3, vm.StartMs, 8);
        vm.Seek(11990);
        Assert.Equal(11990, vm.PositionMs);
        Assert.Equal(11990 - vm.SpanMs / 3, vm.StartMs, 8);
        vm.TogglePlay();
        double pausedStart = vm.StartMs;
        vm.Seek(15000); session.Now = 500; vm.UpdateFrame();
        Assert.Equal(15000, vm.PositionMs);
        Assert.Equal(pausedStart, vm.StartMs);
        vm.FollowPlayhead = false; vm.FollowPlayhead = true;
        Assert.Equal(pausedStart, vm.StartMs);
        vm.Zoom(0.5, 0.5); vm.TogglePlay();
        Assert.Equal(15000 - vm.SpanMs / 3, vm.StartMs, 8);
    }

    [Fact]
    public async Task FollowOffSurvivesPlayAndEndClampsThenReplayRestarts()
    {
        using var session = await Session.Open();
        var vm = session.Model;
        vm.FollowPlayhead = false; vm.Seek(12000); vm.TogglePlay();
        Assert.Equal(0, vm.StartMs);
        vm.TogglePlay(); vm.TogglePlay();
        Assert.Equal(0, vm.StartMs);
        vm.FollowPlayhead = true;
        vm.Seek(29990);
        Assert.Equal(vm.ScrollMaximum, vm.StartMs);
        session.Now = 16; vm.UpdateFrame();
        Assert.Equal(30000, vm.PositionMs);
        Assert.Equal(vm.ScrollMaximum, vm.StartMs);
        session.Engine.Position = 30000; session.Engine.IsPlaying = false;
        session.Now = 50; vm.UpdateFrame();
        Assert.Equal(30000, vm.PositionMs);
        vm.TogglePlay();
        Assert.Equal(0, vm.PositionMs);
        Assert.Equal(0, vm.StartMs);
    }

    private sealed class Session : IDisposable
    {
        private readonly string path = Path.GetTempFileName();
        public double Now { get; set; }
        public FakePlayback Engine { get; } = new();
        public EditorViewModel Model { get; }
        private Session() => Model = new(_ => Task.FromResult(AudioContent.Create(new double[30000], 1000)),
            _ => Engine, () => Now);
        public static async Task<Session> Open()
        {
            var session = new Session();
            await session.Model.LoadAsync(session.path);
            return session;
        }
        public void Dispose() { Model.Dispose(); File.Delete(path); }
    }

    private sealed class FakePlayback : IPlaybackEngine
    {
        public double DurationMs => 30000;
        public double Position { get; set; }
        public int Reads { get; private set; }
        public double PositionMs { get { Reads++; return Position; } }
        public bool IsPlaying { get; set; }
        public bool ClickEnabled { get; set; }
        public double MusicVolume { get; set; }
        public double ClickVolume { get; set; }
        public double Speed { get; set; } = 1;
        public void Play() { if (Position >= DurationMs) Position = 0; IsPlaying = true; }
        public void Pause() => IsPlaying = false;
        public void Seek(double timeMs) => Position = Math.Clamp(timeMs, 0, DurationMs);
        public void SetTiming(IEnumerable<TimingPoint> lines) { }
        public void Dispose() { }
    }
}
