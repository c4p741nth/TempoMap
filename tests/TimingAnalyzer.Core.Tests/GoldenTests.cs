using System.Globalization;
using System.Text.Json;
using TimingAnalyzer.Core;
using Xunit.Abstractions;

namespace TimingAnalyzer.Core.Tests;

public class GoldenTests(ITestOutputHelper output)
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    [Theory]
    [InlineData("Judgement")]
    [InlineData("Hayakore")]
    public void StagesMatchPython(string song)
    {
        string path = Path.Combine(GoldenRoot(), song);
        Assert.True(File.Exists(Path.Combine(path, "mono.f64")), "Generate tests/golden/" + song + " with tools/dump_golden.py first.");
        var mono = ReadDoubles(Path.Combine(path, "mono.f64"));
        double rate = double.Parse(File.ReadAllText(Path.Combine(path, "rate.txt")), Invariant);
        var d = OnsetEnvelope.Compute(mono, rate);
        CompareArray("envelope", d, ReadDoubles(Path.Combine(path, "envelope.f64")), 1e-9);
        CompareArray("acf", Fft.Autocorrelate(d, 2000), ReadDoubles(Path.Combine(path, "acf.f64")), 1e-9);
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(path, "result.json")));
        var expected = doc.RootElement;
        CompareResult(Analyzer.Analyze(mono, rate), expected);
        foreach (var mode in expected.GetProperty("modes").EnumerateArray())
        {
            double bpm = mode.GetProperty("input_bpm").GetDouble();
            var o = mode.GetProperty("input_offset");
            CompareResult(Analyzer.AnalyzeEnvelope(d, bpm, o.ValueKind == JsonValueKind.Null ? null : o.GetDouble()), mode.GetProperty("result"));
        }
    }

    private void CompareArray(string stage, double[] actual, double[] expected, double relativeTolerance)
    {
        Assert.Equal(expected.Length, actual.Length);
        double error = actual.Zip(expected, (a, b) => Math.Abs(a - b)).Max();
        double scale = expected.Max(v => Math.Abs(v));
        output.WriteLine(stage + " max abs diff=" + error.ToString("G17", Invariant) + ", scale=" + scale.ToString("G17", Invariant));
        Assert.True(error <= relativeTolerance * scale, stage + " exceeds tolerance: " + error.ToString("G17", Invariant));
    }

    private static void CompareResult(AnalysisResult actual, JsonElement expected)
    {
        var status = expected.GetProperty("status");
        Assert.Equal(status.ValueKind == JsonValueKind.Null ? (int?)null : status.GetInt32(), actual.Status);
        Assert.Equal(expected.GetProperty("envelope_len").GetInt32(), actual.EnvelopeLength);
        if (expected.TryGetProperty("error", out var error)) { Assert.Equal(error.GetString(), actual.Error); return; }
        Assert.Null(actual.Error);
        if (expected.TryGetProperty("approx_bpm", out var approximate))
        {
            Assert.True(Math.Abs(approximate.GetDouble() - actual.ApproximateBpm!.Value) < 1e-6);
            return;
        }
        Assert.Null(actual.ApproximateBpm);
        Assert.True(Math.Abs(expected.GetProperty("bpm").GetDouble() - actual.Bpm!.Value) < 1e-6);
        Assert.True(Math.Abs(expected.GetProperty("offset_int").GetDouble() - actual.OffsetInternal!.Value) < 1e-6);
        Assert.Equal(expected.GetProperty("flags").GetInt32(), actual.Flags);
        Assert.Equal(expected.GetProperty("timeline").GetString(), actual.Timeline);
    }

    private static string GoldenRoot()
    {
        var configured = Environment.GetEnvironmentVariable("TIMING_ANALYZER_GOLDEN");
        if (configured is not null) return configured;
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TimingAnalyzer.sln"))) dir = dir.Parent;
        return Path.Combine(dir?.FullName ?? throw new InvalidOperationException("Cannot locate repository; set TIMING_ANALYZER_GOLDEN."), "tests", "golden");
    }

    private static double[] ReadDoubles(string path)
    {
        using var reader = new BinaryReader(File.OpenRead(path));
        var result = new double[reader.BaseStream.Length / sizeof(double)];
        for (int i = 0; i < result.Length; i++) result[i] = reader.ReadDouble();
        return result;
    }

    [Fact]
    public void SilenceFailsTimingAndLongSongsStopBeforeDsp()
    {
        Assert.Equal("cannot determine timing", Analyzer.Analyze(new double[44100], 44100).Error);
        Assert.Equal("longer than 10 minutes", Analyzer.Analyze(new double[601], 1).Error);
    }
}
