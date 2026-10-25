namespace TimingAnalyzer.Core;

public static class OnsetEnvelope
{
    /// <summary>
    /// Every filter before the 1 ms resampling is causal, so the song runs through all of them one sample at a time
    /// and only the samples the resampling picks are kept: the same operations in the same order as filtering whole
    /// arrays band by band, without two song-length buffers (~220 MB on a 5-minute song, ~900 MB at 20 minutes).
    /// </summary>
    public static double[] Compute(double[] mono, double rate, Coefficients? coefficients = null, CancellationToken cancellationToken = default, Action<double>? progress = null)
    {
        var c = coefficients ?? Coefficients.Default;
        double ratio = 1000.0 / rate;
        int m = (int)Math.Ceiling(mono.Length * ratio);
        // Very short inputs cannot contain enough samples for the two-sided derivative.
        if (m < 2) return new double[m];
        var bands = c.Bands.Select(b => new Cascade(b)).ToArray();
        var smooth = c.Bands.Select(_ => new Cascade(c.Smooth)).ToArray();
        var low = new Cascade(c.Bands[0]);
        var x = new double[m];
        int next = 0, src = Source(0, ratio);
        for (int i = 0; i < mono.Length && next < m; i++)
        {
            if ((i & 32767) == 0) { cancellationToken.ThrowIfCancellationRequested(); progress?.Invoke(100.0 * i / mono.Length); }
            double acc = 1;
            for (int b = 0; b < bands.Length; b++)
            {
                double y = bands[b].Step(mono[i]);
                acc *= 1 + smooth[b].Step(y * y);
            }
            double v = low.Step(Math.Log(acc));
            while (next < m && src == i) { x[next++] = v; src = Source(next, ratio); }
        }
        var fwd = Biquad.Filter(x, c.Post);
        Array.Reverse(x);
        Biquad.FilterInPlace(x, c.Post);
        var d = new double[m];
        d[m - 1] = -fwd[m - 2];
        for (int i = 1; i < m - 1; i++) d[i] = x[m - i - 2] - fwd[i - 1];
        d[0] = x[m - 2];
        cancellationToken.ThrowIfCancellationRequested(); progress?.Invoke(100);
        return d;
    }

    /// <summary>The audio sample the 1 ms resampling takes for envelope index <paramref name="i"/> (past the end: 0).</summary>
    private static int Source(int i, double ratio) => (int)Math.Floor(i * (1.0 / ratio) + 0.5);

    /// <summary><see cref="Biquad.FilterInPlace"/> one sample at a time: the same arithmetic per section, in order.</summary>
    private sealed class Cascade(BiquadSection[] sections)
    {
        private readonly double[] z1 = new double[sections.Length], z2 = new double[sections.Length];

        public double Step(double x)
        {
            for (int k = 0; k < sections.Length; k++)
            {
                var s = sections[k];
                double y = s.B0 * x + z1[k];
                z1[k] = s.B1 * x - s.A1 * y + z2[k];
                z2[k] = s.B2 * x - s.A2 * y;
                x = y;
            }
            return x;
        }
    }
}
