using System.Text.Json;
using TimingAnalyzer.Core;

namespace TimingAnalyzer.Core.Tests;

/// <summary>The C# mel front end must reproduce tools/beatnet/data.py, or the beat model sees different input.</summary>
public class BeatMelTests
{
    [Fact]
    public void LogMelMatchesTheTrainingFrontEnd()
    {
        string dir = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        var bytes = File.ReadAllBytes(Path.Combine(dir, "beatmel-signal.f32"));
        var x = new float[bytes.Length / 4];
        Buffer.BlockCopy(bytes, 0, x, 0, bytes.Length);
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "beatmel-reference.json")));
        var mel = BeatMel.Compute(x, out int frames);
        Assert.Equal(json.RootElement.GetProperty("frames").GetInt32(), frames);
        var rows = json.RootElement.GetProperty("rows").EnumerateArray().Select(e => e.GetInt32()).ToArray();
        var expected = json.RootElement.GetProperty("mel_rows").EnumerateArray().ToArray();
        double worst = 0;
        for (int r = 0; r < rows.Length; r++)
        {
            var want = expected[r].EnumerateArray().Select(e => e.GetDouble()).ToArray();
            for (int m = 0; m < BeatMel.Mels; m++) worst = Math.Max(worst, Math.Abs(mel[rows[r] * BeatMel.Mels + m] - want[m]));
        }
        // float32 in both, rounded to 5 decimals in the reference: agreement to 1e-3 of a log value up to ~13.
        Assert.True(worst < 1e-3, $"largest difference {worst}");
    }

    [Fact]
    public void ResamplingKeepsDurationAtTheModelRate()
    {
        var mono = new double[44100 * 3];
        for (int i = 0; i < mono.Length; i++) mono[i] = Math.Sin(2 * Math.PI * 440 * i / 44100.0);
        var y = BeatMel.Resample(mono, 44100);
        Assert.Equal(22050 * 3, y.Length);
        Assert.InRange(y[1000], (float)Math.Sin(2 * Math.PI * 440 * 2000 / 44100.0) - 1e-4f, (float)Math.Sin(2 * Math.PI * 440 * 2000 / 44100.0) + 1e-4f);
    }
}
