using TimingAnalyzer.Core;

namespace TimingAnalyzer.Core.Tests;

public class EnvelopeCancellationTests
{
    [Fact]
    public void CancellationInterruptsTheActualSampleLoop()
    {
        using var cancellation = new CancellationTokenSource(); int callbacks = 0;
        Assert.Throws<OperationCanceledException>(() => OnsetEnvelope.Compute(new double[441000], 44100,
            cancellationToken: cancellation.Token, progress: percent => { callbacks++; if (percent > 5) cancellation.Cancel(); }));
        Assert.InRange(callbacks, 2, 4);
    }
}
