using TimingAnalyzer.Core;
using TimingAnalyzer.Editor;

namespace TimingAnalyzer.Eval;

/// <summary>A timing algorithm under evaluation: onset envelope in, red lines (osu! precision) out.</summary>
public interface ITimingAlgorithm
{
    string Name { get; }
    IReadOnlyList<TimingPoint> Run(double[] envelope, double durationMs);
}

/// <summary>Phase 2 multi-BPM analyzer, at osu! precision.</summary>
public sealed class MultiAlgorithm(MultiBpmOptions options, string name = "multi") : ITimingAlgorithm
{
    public string Name => name;
    public IReadOnlyList<TimingPoint> Run(double[] envelope, double durationMs) =>
        MultiBpmAnalyzer.Analyze(envelope, options)
            .Select(s => new TimingPoint(OsuPrecision.Time(s.TimeMs), 60000 / OsuPrecision.Bpm(s.Bpm)))
            .GroupBy(p => p.Time).Select(g => g.First()).ToList();
}

/// <summary>Drift mode (opt-in in the editor): a continuous grid fitted to the tracked beats, one red line per N beats.</summary>
public sealed class DriftAlgorithm(MultiBpmOptions options, int beatsPerLine) : ITimingAlgorithm
{
    public string Name => "drift" + beatsPerLine.ToString(System.Globalization.CultureInfo.InvariantCulture);
    public IReadOnlyList<TimingPoint> Run(double[] envelope, double durationMs) =>
        DriftLines.Fit(envelope, beatsPerLine, options)
            .Select(s => new TimingPoint(OsuPrecision.Time(s.TimeMs), 60000 / OsuPrecision.Bpm(s.Bpm)))
            .GroupBy(p => p.Time).Select(g => g.First()).ToList();
}

/// <summary>Diagnostic: the multi-BPM red lines before the merge passes, to measure what merging gains or loses.</summary>
public sealed class UnmergedAlgorithm(MultiBpmOptions options) : ITimingAlgorithm
{
    public string Name => "unmerged";
    public IReadOnlyList<TimingPoint> Run(double[] envelope, double durationMs) =>
        MultiBpmAnalyzer.Diagnose(envelope, options).Unmerged
            .Select(s => new TimingPoint(OsuPrecision.Time(s.TimeMs), 60000 / OsuPrecision.Bpm(s.Bpm)))
            .GroupBy(p => p.Time).Select(g => g.First()).ToList();
}

/// <summary>Diagnostic ceiling for segmentation: every tracked beat becomes a red line, so the grid is exactly the
/// tracked beats. Unrounded, so it measures the tracker alone, not osu! precision.</summary>
public sealed class TrackedAlgorithm(MultiBpmOptions options) : ITimingAlgorithm
{
    public string Name => "tracked";
    public IReadOnlyList<TimingPoint> Run(double[] envelope, double durationMs)
    {
        var beats = MultiBpmAnalyzer.Diagnose(envelope, options).Beats;
        var result = new List<TimingPoint>();
        for (int k = 0; k + 1 < beats.Count; k++)
        {
            double length = beats[k + 1].TimeMs - beats[k].TimeMs;
            if (length > 0) result.Add(new TimingPoint(beats[k].TimeMs, length));
        }
        return result;
    }
}

/// <summary>
/// Diagnostic for a "free tempo" mode: tracked beats grouped into red lines of <paramref name="group"/> beats each,
/// with a least-squares grid per group, then osu! precision. group = 1 is "a red line per beat".
/// </summary>
public sealed class GroupedAlgorithm(MultiBpmOptions options, int group) : ITimingAlgorithm
{
    public string Name => "grouped" + group.ToString(System.Globalization.CultureInfo.InvariantCulture);
    public IReadOnlyList<TimingPoint> Run(double[] envelope, double durationMs)
    {
        var beats = MultiBpmAnalyzer.Diagnose(envelope, options).Beats.Where(b => b.Weight > 0).Select(b => b.TimeMs).ToArray();
        var result = new List<TimingPoint>();
        for (int i = 0; i + 1 < beats.Length; i += group)
        {
            int j = Math.Min(beats.Length - 1, i + group);
            // Beats i..j, assumed one beat apart: least-squares slope = beat length.
            int n = j - i + 1;
            double sx = 0, sy = 0, sxx = 0, sxy = 0;
            for (int k = 0; k < n; k++) { sx += k; sy += beats[i + k]; sxx += k * k; sxy += k * beats[i + k]; }
            double length = (n * sxy - sx * sy) / (n * sxx - sx * sx), first = (sy - length * sx) / n;
            if (!(length > 100 && length < 1000)) continue;
            var point = new TimingPoint(OsuPrecision.Time(first), 60000 / OsuPrecision.Bpm(60000 / length));
            if (result.Count == 0 || point.Time > result[^1].Time) result.Add(point);
        }
        return result;
    }
}

/// <summary>Today's best single-BPM result: the original detector, with an approximate BPM's offset fitted too.</summary>
public sealed class BaselineAlgorithm : ITimingAlgorithm
{
    public string Name => "baseline";
    public IReadOnlyList<TimingPoint> Run(double[] envelope, double durationMs)
    {
        var result = Analyzer.AnalyzeEnvelope(envelope);
        if (result.Bpm is null && result.ApproximateBpm is { } approximate)
            result = Analyzer.AnalyzeEnvelope(envelope, approximate);
        if (result.Bpm is not { } bpm || result.Offset is not { } offset) return [];
        return [new TimingPoint(OsuPrecision.Time(offset), 60000 / OsuPrecision.Bpm(bpm))];
    }
}
