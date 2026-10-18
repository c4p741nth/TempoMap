using System.Text.Json;

namespace TimingAnalyzer.Core;

public sealed class Coefficients
{
    public static Coefficients Default { get; } = Load();
    public IReadOnlyList<BiquadSection[]> Bands { get; }
    public BiquadSection[] Smooth { get; }
    public BiquadSection[] Post { get; }

    private Coefficients(BiquadSection[][] bands, BiquadSection[] smooth, BiquadSection[] post)
        => (Bands, Smooth, Post) = (bands, smooth, post);

    private static Coefficients Load()
    {
        using var stream = typeof(Coefficients).Assembly.GetManifestResourceStream("TimingAnalyzer.Core.coefficients.json")
            ?? throw new InvalidOperationException("Missing embedded coefficients.");
        using var doc = JsonDocument.Parse(stream);
        var root = doc.RootElement;
        var bands = root.GetProperty("bands").EnumerateArray()
            .Select(b => b.GetProperty("sections").EnumerateArray().Select(ReadSection).ToArray()).ToArray();
        return new(bands, [ReadSection(root.GetProperty("envelope_smoother_44100").GetProperty("section"))],
            [ReadSection(root.GetProperty("post_filter_1000").GetProperty("section"))]);
    }

    private static BiquadSection ReadSection(JsonElement s)
    {
        double g = s.GetProperty("gain").GetDouble();
        return new(g, g * s.GetProperty("n1").GetDouble(), g * s.GetProperty("n2").GetDouble(),
            s.GetProperty("d1").GetDouble(), s.GetProperty("d2").GetDouble());
    }
}
