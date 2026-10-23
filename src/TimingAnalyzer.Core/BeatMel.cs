using System.Numerics;

namespace TimingAnalyzer.Core;

/// <summary>
/// The beat model's input (docs/PHASE4-PLAN.md): log-mel frames at 100 fps from 22 050 Hz mono, exactly as
/// tools/beatnet/data.py computes them for training: Hann window 2048 (numpy.hanning, symmetric), frame t centred on
/// sample round(t · 220.5) of the signal padded by 1 024 zeros each side, power spectrum, 128 triangular mel bands
/// 30-11 000 Hz (HTK mel scale, no normalisation), log(1 + 1000 · band power / 2048).
/// </summary>
public static class BeatMel
{
    public const int Rate = 22050, Fps = 100, NFft = 2048, Mels = 128;
    private const double FMin = 30, FMax = 11000;
    private static readonly float[] Window = MakeWindow();
    private static readonly (int Lo, float[] Weights)[] Filters = MakeFilters();

    /// <summary>Linear resampling to 22 050 Hz, as `timing-eval dump-set` does for training.</summary>
    public static float[] Resample(double[] mono, double rate)
    {
        int n = (int)(mono.LongLength * Rate / (long)rate);
        var y = new float[n];
        double step = rate / Rate;
        for (int i = 0; i < n; i++)
        {
            double x = i * step;
            int j = (int)x;
            double f = x - j, a = mono[Math.Min(j, mono.Length - 1)], b = mono[Math.Min(j + 1, mono.Length - 1)];
            y[i] = (float)(a + (b - a) * f);
        }
        return y;
    }

    /// <summary>Frames × 128 log-mel values (row-major), frames = samples · 100 / 22 050.</summary>
    public static float[] Compute(float[] x, out int frames)
    {
        frames = (int)((long)x.Length * Fps / Rate);
        var mel = new float[(long)frames * Mels];
        var buffer = new Complex[NFft];
        var power = new double[NFft / 2 + 1];
        for (int t = 0; t < frames; t++)
        {
            // Centre in the padded signal is round(t * RATE / FPS) + NFFT/2, so the window starts at round(t * 220.5).
            long start = (long)Math.Round(t * (double)Rate / Fps, MidpointRounding.ToEven) - NFft / 2;
            for (int i = 0; i < NFft; i++)
            {
                long s = start + i;
                buffer[i] = new Complex(s >= 0 && s < x.Length ? x[s] * Window[i] : 0, 0);
            }
            Fft.Transform(buffer);
            for (int k = 0; k < power.Length; k++) power[k] = buffer[k].Real * buffer[k].Real + buffer[k].Imaginary * buffer[k].Imaginary;
            for (int m = 0; m < Mels; m++)
            {
                var (lo, w) = Filters[m];
                double sum = 0;
                for (int k = 0; k < w.Length; k++) sum += w[k] * power[lo + k];
                mel[(long)t * Mels + m] = (float)Math.Log(1 + 1000 * sum / NFft);
            }
        }
        return mel;
    }

    private static float[] MakeWindow()
    {
        // numpy.hanning(M): 0.5 - 0.5 cos(2πn / (M - 1)), symmetric.
        var w = new float[NFft];
        for (int n = 0; n < NFft; n++) w[n] = (float)(0.5 - 0.5 * Math.Cos(2 * Math.PI * n / (NFft - 1)));
        return w;
    }

    private static (int, float[])[] MakeFilters()
    {
        static double HzToMel(double f) => 2595 * Math.Log10(1 + f / 700);
        static double MelToHz(double m) => 700 * (Math.Pow(10, m / 2595) - 1);
        var edges = new double[Mels + 2];
        double lo = HzToMel(FMin), hi = HzToMel(FMax);
        for (int i = 0; i < edges.Length; i++) edges[i] = MelToHz(lo + (hi - lo) * i / (Mels + 1));
        int bins = NFft / 2 + 1;
        var filters = new (int, float[])[Mels];
        for (int m = 0; m < Mels; m++)
        {
            var weights = new float[bins];
            int first = -1, last = -1;
            for (int k = 0; k < bins; k++)
            {
                double f = k * (double)Rate / NFft;
                double up = (f - edges[m]) / (edges[m + 1] - edges[m]), down = (edges[m + 2] - f) / (edges[m + 2] - edges[m + 1]);
                weights[k] = (float)Math.Max(0, Math.Min(up, down));
                if (weights[k] > 0) { if (first < 0) first = k; last = k; }
            }
            filters[m] = first < 0 ? (0, []) : (first, weights[first..(last + 1)]);
        }
        return filters;
    }
}
