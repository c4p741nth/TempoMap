namespace TimingAnalyzer.Core;

public static class OnsetEnvelope
{
    public static double[] Compute(double[] mono, double rate, Coefficients? coefficients = null)
    {
        var c = coefficients ?? Coefficients.Default;
        var acc = new double[mono.Length];
        Array.Fill(acc, 1);
        foreach (var band in c.Bands)
        {
            var y = Biquad.Filter(mono, band);
            for (int i = 0; i < y.Length; i++) y[i] *= y[i];
            Biquad.FilterInPlace(y, c.Smooth);
            for (int i = 0; i < y.Length; i++) acc[i] *= 1 + y[i];
        }
        for (int i = 0; i < acc.Length; i++) acc[i] = Math.Log(acc[i]);
        Biquad.FilterInPlace(acc, c.Bands[0]);
        double ratio = 1000.0 / rate;
        int m = (int)Math.Ceiling(acc.Length * ratio);
        // Very short inputs cannot contain enough samples for the two-sided derivative.
        if (m < 2) return new double[m];
        var x = new double[m];
        for (int i = 0; i < m; i++)
        {
            int src = (int)Math.Floor(i * (1.0 / ratio) + 0.5);
            x[i] = src < acc.Length ? acc[src] : 0;
        }
        var fwd = Biquad.Filter(x, c.Post);
        Array.Reverse(x);
        Biquad.FilterInPlace(x, c.Post);
        var d = new double[m];
        d[m - 1] = -fwd[m - 2];
        for (int i = 1; i < m - 1; i++) d[i] = x[m - i - 2] - fwd[i - 1];
        d[0] = x[m - 2];
        return d;
    }
}
