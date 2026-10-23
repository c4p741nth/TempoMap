using System.Numerics;

namespace TimingAnalyzer.Core;

public static class Fft
{
    // Forward is unscaled; inverse divides by N, as in NumPy.
    public static void Transform(Complex[] values, bool inverse = false)
    {
        int n = values.Length;
        if (n == 0 || (n & (n - 1)) != 0)
            throw new ArgumentException("FFT length must be a positive power of two.", nameof(values));
        for (int i = 1, j = 0; i < n; i++)
        {
            int bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j) (values[i], values[j]) = (values[j], values[i]);
        }
        var twiddles = Twiddles(n, inverse);
        for (int size = 2; size <= n; size <<= 1)
        {
            int half = size / 2, stride = n / size;
            for (int start = 0; start < n; start += size)
                for (int k = 0; k < half; k++)
                {
                    var even = values[start + k];
                    var odd = values[start + k + half] * twiddles[k * stride];
                    values[start + k] = even + odd;
                    values[start + k + half] = even - odd;
                }
            if (size == n) break;
        }
        if (inverse)
            for (int i = 0; i < n; i++) values[i] /= n;
    }

    private const int CachedLog2 = 16;
    private static readonly Complex[]?[] Forward = new Complex[CachedLog2 + 1][], Inverse = new Complex[CachedLog2 + 1][];

    /// <summary>
    /// e^(∓2πik/n) for k &lt; n/2, each computed directly (no recurrence drift on long songs). A stage of size s reads
    /// every (n/s)-th: (±2π·k·n/s)/n rounds exactly as (±2π·k)/s, both factors being powers of two, so every stage
    /// sees the values a table of its own would hold. Tables up to 2^16 are kept (the beat model's mel runs one 2 048
    /// FFT per 10 ms frame); a fresh table per call was 1 GB of garbage on a 5-minute song.
    /// </summary>
    private static Complex[] Twiddles(int n, bool inverse)
    {
        int log2 = System.Numerics.BitOperations.Log2((uint)n);
        var cache = inverse ? Inverse : Forward;
        if (log2 <= CachedLog2 && cache[log2] is { } cached) return cached;
        var table = new Complex[Math.Max(1, n / 2)];
        for (int k = 0; k < n / 2; k++)
            table[k] = Complex.FromPolarCoordinates(1, (inverse ? 2 : -2) * Math.PI * k / n);
        if (log2 <= CachedLog2) cache[log2] = table;
        return table;
    }

    public static double[] Autocorrelate(double[] input, int lags)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(lags);
        if (lags == 0) return [];
        int nfft = 1 << (int)Math.Ceiling(Math.Log2(input.Length + lags - 0.5));
        var spectrum = new Complex[nfft];
        for (int i = 0; i < input.Length; i++) spectrum[i] = new(input[i], 0);
        Transform(spectrum);
        for (int i = 0; i < nfft; i++)
        {
            var v = spectrum[i];
            spectrum[i] = new(v.Real * v.Real + v.Imaginary * v.Imaginary, 0);
        }
        Transform(spectrum, inverse: true);
        return spectrum.Take(lags).Select(v => v.Real).ToArray();
    }
}
