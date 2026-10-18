namespace TimingAnalyzer.Core;

public readonly record struct BiquadSection(double B0, double B1, double B2, double A1, double A2);

public static class Biquad
{
    // SciPy sosfilt: transposed direct form II, zero state for each section.
    public static double[] Filter(double[] input, IReadOnlyList<BiquadSection> sections)
    {
        var output = (double[])input.Clone();
        FilterInPlace(output, sections);
        return output;
    }

    public static void FilterInPlace(double[] samples, IReadOnlyList<BiquadSection> sections)
    {
        foreach (var s in sections)
        {
            double z1 = 0, z2 = 0;
            for (int i = 0; i < samples.Length; i++)
            {
                double x = samples[i];
                double y = s.B0 * x + z1;
                z1 = s.B1 * x - s.A1 * y + z2;
                z2 = s.B2 * x - s.A2 * y;
                samples[i] = y;
            }
        }
    }
}
