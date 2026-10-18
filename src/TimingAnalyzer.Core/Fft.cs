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
        for (int size = 2; size <= n; size <<= 1)
        {
            int half = size / 2;
            // Compute each twiddle directly: avoid recurrence drift on long songs.
            var twiddles = new Complex[half];
            for (int k = 0; k < half; k++)
                twiddles[k] = Complex.FromPolarCoordinates(1, (inverse ? 2 : -2) * Math.PI * k / size);
            for (int start = 0; start < n; start += size)
                for (int k = 0; k < half; k++)
                {
                    var even = values[start + k];
                    var odd = values[start + k + half] * twiddles[k];
                    values[start + k] = even + odd;
                    values[start + k + half] = even - odd;
                }
            if (size == n) break;
        }
        if (inverse)
            for (int i = 0; i < n; i++) values[i] /= n;
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
