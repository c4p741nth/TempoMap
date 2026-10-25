using TimingAnalyzer.Editor;

namespace TimingAnalyzer.Editor.Tests;

public class AnalysisCancellationTests
{
    [Fact]
    public async Task CancellingAnalysisPausesAudioKeepsTransportUsableAndNeverAppliesResult()
    {
        string song = Path.GetTempFileName();
        try
        {
            var playback = new ViewModelTests.FakePlayback();
            using var vm = new EditorViewModel(_ => Task.FromResult(AudioContent.Create(new double[10000], 1000)), _ => playback);
            await vm.LoadAsync(song); vm.TogglePlay(); Assert.True(playback.IsPlaying);
            var finish = new TaskCompletionSource<TimingDetection>();
            var before = vm.Edits.RedLines.ToArray(); var task = vm.AnalyzeAsync(_ => finish.Task);
            Assert.True(vm.IsBusy); Assert.False(playback.IsPlaying);
            vm.MusicVolumePercent = 40; vm.ClickVolumePercent = 20; vm.SelectedSpeed = "0.5"; vm.JumpTo(765);
            Assert.Equal(.4, playback.MusicVolume); Assert.Equal(.2, playback.ClickVolume); Assert.Equal(.5, playback.Speed); Assert.Equal(765, playback.PositionMs);
            vm.TogglePlay(); Assert.True(playback.IsPlaying); vm.TogglePlay(); Assert.False(playback.IsPlaying);
            vm.CancelAnalysis(); finish.SetResult(new(220, 432, null, 0, null)); await task;
            Assert.False(vm.IsBusy); Assert.Null(vm.Proposal); Assert.Equal(before, vm.Edits.RedLines);
            Assert.Contains("cancelled", vm.Status);
        }
        finally { File.Delete(song); }
    }
}
