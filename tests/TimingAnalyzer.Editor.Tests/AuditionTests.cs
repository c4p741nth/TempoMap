using TimingAnalyzer.Editor;

namespace TimingAnalyzer.Editor.Tests;

public class AuditionTests
{
    [Fact]
    public async Task LoopUsesPreRollAndManualPauseStopsLooping()
    {
        string song = Path.GetTempFileName();
        try
        {
            var player = new ViewModelTests.FakePlayback();
            using var vm = new EditorViewModel(_ => Task.FromResult(AudioContent.Create(new double[20000], 1000)), _ => player);
            await vm.LoadAsync(song); vm.SetRange(3000, 5000); vm.PreRollBeats = 2; vm.LoopEnabled = true;
            vm.PlayRange(); Assert.Equal(2000, player.PositionMs); Assert.True(player.IsPlaying);
            player.Seek(5000); vm.RefreshPosition(); Assert.Equal(2000, player.PositionMs); Assert.True(player.IsPlaying);
            vm.TogglePlay(); player.Seek(7000); vm.RefreshPosition(); Assert.Equal(7000, player.PositionMs); Assert.False(player.IsPlaying);
            vm.ClickEnabled = true; vm.MuteMetronomeWhileHeld(true); Assert.False(player.ClickEnabled);
            vm.MuteMetronomeWhileHeld(false); Assert.True(player.ClickEnabled);
            vm.MuteMetronomeWhileHeld(true); vm.ClickEnabled = false; vm.MuteMetronomeWhileHeld(false); Assert.False(player.ClickEnabled);
        }
        finally { File.Delete(song); }
    }
    [Fact]
    public void TapTempoUsesRecentMedianAndResetsAfterIdle()
    {
        var tap = new TapTempo();
        Assert.Null(tap.Tap(0)); Assert.Null(tap.Tap(500)); Assert.Equal(120, tap.Tap(1000));
        Assert.Equal(120, tap.Tap(1520)); Assert.Null(tap.Tap(1600));
        Assert.Null(tap.Tap(5000)); Assert.Null(tap.Tap(5250)); Assert.Equal(240, tap.Tap(5500));
    }
    [Fact]
    public async Task ExplicitAnchorsUseSpecifiedBeatCountAndPreserveTimingBeyondB()
    {
        string song = Path.GetTempFileName();
        try
        {
            using var vm = new EditorViewModel(_ => Task.FromResult(AudioContent.Create(new double[20000], 1000)), _ => new ViewModelTests.FakePlayback());
            await vm.LoadAsync(song); var before = vm.Edits.RedLines.ToArray();
            vm.JumpTo(1000); vm.MarkAnchorA(); vm.JumpTo(5000); vm.MarkAnchorB(); vm.AnchorBeats = "16"; vm.ApplyAnchors();
            Assert.Equal(240, vm.Edits.RedLines.Single(p => p.Time == 1000).Bpm);
            Assert.Equal(before[0].BeatLength, vm.Edits.RedLines.Last(p => p.Time <= 6000).BeatLength);
            vm.Undo(); Assert.Equal(before, vm.Edits.RedLines);
        }
        finally { File.Delete(song); }
    }
}
