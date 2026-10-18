using System.Numerics;
using System.Text.Json;
using TimingAnalyzer.Core;

namespace TimingAnalyzer.Core.Tests;

public class DspTests
{
    [Fact]
    public void FftMatchesNaiveDftAndInverts()
    {
        const int n = 1024;
        var random = new Random(1234);
        var input = Enumerable.Range(0, n).Select(_ => new Complex(random.NextDouble() - 0.5, random.NextDouble() - 0.5)).ToArray();
        var actual = (Complex[])input.Clone();
        Fft.Transform(actual);
        for (int k = 0; k < n; k++)
        {
            Complex expected = Complex.Zero;
            for (int j = 0; j < n; j++)
                expected += input[j] * Complex.FromPolarCoordinates(1, -2 * Math.PI * k * j / n);
            Assert.True(Complex.Abs(actual[k] - expected) < 1e-9);
        }
        Fft.Transform(actual, inverse: true);
        for (int i = 0; i < n; i++) Assert.True(Complex.Abs(actual[i] - input[i]) < 1e-12);
    }

    [Theory]
    [InlineData(257, 128)]
    [InlineData(31, 40)]
    public void AutocorrelationMatchesNaive(int n, int lags)
    {
        var random = new Random(4321);
        var input = Enumerable.Range(0, n).Select(_ => random.NextDouble() - 0.5).ToArray();
        var actual = Fft.Autocorrelate(input, lags);
        for (int lag = 0; lag < lags; lag++)
        {
            double expected = 0;
            for (int j = 0; j < n - lag; j++) expected += input[j] * input[j + lag];
            Assert.True(Math.Abs(expected - actual[lag]) < 1e-11);
        }
    }

    [Fact]
    public void EveryBiquadCascadeMatchesScipy()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "biquad.json")));
        var input = doc.RootElement.GetProperty("input").EnumerateArray().Select(v => v.GetDouble()).ToArray();
        var c = Coefficients.Default;
        var filters = c.Bands.Concat(new[] { c.Smooth, c.Post }).ToArray();
        var outputs = doc.RootElement.GetProperty("outputs");
        for (int f = 0; f < filters.Length; f++)
        {
            var actual = Biquad.Filter(input, filters[f]);
            var expected = outputs[f].EnumerateArray().Select(v => v.GetDouble()).ToArray();
            double scale = expected.Max(v => Math.Abs(v));
            for (int i = 0; i < input.Length; i++) Assert.True(Math.Abs(actual[i] - expected[i]) <= 1e-12 * scale);
            Assert.Equal(actual, Biquad.Filter(input, filters[f])); // each invocation resets state
        }
    }
}
