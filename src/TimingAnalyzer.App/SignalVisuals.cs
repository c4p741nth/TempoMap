using System.Numerics;
using TimingAnalyzer.Core;
using TimingAnalyzer.Editor;

namespace TimingAnalyzer.App;

internal sealed record VisualSignal(WaveformPeaks Peaks, int Rate);
internal static class SignalVisuals
{
    public static VisualSignal Filter(AudioContent audio, string layer, CancellationToken token)
    {
        const int visualRate = 2000;
        var values = new double[checked((int)Math.Ceiling(audio.DurationMs * visualRate / 1000))];
        double low = 0, midLow = 0, lowAlpha = 1 - Math.Exp(-2 * Math.PI * 200 / audio.Rate), highAlpha = 1 - Math.Exp(-2 * Math.PI * 4000 / audio.Rate);
        double previousEnergy = 0, energy = 0;
        int last = -1;
        for (int i = 0; i < audio.Mono.Length; i++)
        {
            if ((i & 32767) == 0) token.ThrowIfCancellationRequested();
            double sample = audio.Mono[i]; low += lowAlpha * (sample - low); midLow += highAlpha * (sample - midLow);
            double v = layer.StartsWith("Low", StringComparison.Ordinal) ? low : layer.StartsWith("Mid", StringComparison.Ordinal) ? midLow - low
                : layer == "Onsets" ? sample * sample : sample - midLow;
            int bin = Math.Min(values.Length - 1, (int)((long)i * visualRate / audio.Rate));
            if (layer == "Onsets")
            {
                if (bin != last) { values[bin] = Math.Max(0, energy - previousEnergy); previousEnergy = energy; energy = 0; }
                energy += v; last = bin;
            }
            else if (Math.Abs(v) > Math.Abs(values[bin])) values[bin] = v;
        }
        return new(new WaveformPeaks(values), visualRate);
    }

    public static byte[,] Spectrogram(AudioContent audio, double start, double span, int columns, int rows, CancellationToken token)
    {
        const int fftSize = 2048;
        var data = new byte[columns, rows]; var fft = new Complex[fftSize];
        double maximumHz = Math.Min(16000, audio.Rate / 2.0);
        for (int x = 0; x < columns; x++)
        {
            token.ThrowIfCancellationRequested();
            int centre = (int)((start + span * x / columns) * audio.Rate / 1000);
            for (int i = 0; i < fftSize; i++)
            {
                int at = centre + i - fftSize / 2;
                fft[i] = new((at >= 0 && at < audio.Mono.Length ? audio.Mono[at] : 0) * (.5 - .5 * Math.Cos(2 * Math.PI * i / (fftSize - 1))), 0);
            }
            Fft.Transform(fft);
            for (int y = 0; y < rows; y++)
            {
                double hz = 30 * Math.Pow(maximumHz / 30, (double)y / (rows - 1));
                int bin = Math.Clamp((int)Math.Round(hz * fftSize / audio.Rate), 0, fftSize / 2);
                double db = 20 * Math.Log10(Math.Max(1e-12, fft[bin].Magnitude / fftSize));
                data[x, y] = (byte)Math.Clamp((db + 90) * 255 / 80, 0, 255);
            }
        }
        return data;
    }
}
