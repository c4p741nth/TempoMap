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

/// <summary>
/// Phase 4 variant "model alone": the beat model's beats turned straight into red lines, no onset refinement and no
/// Core pipeline. A red line starts where the beat spacing changes by more than 2 % from the running mean of the
/// current line's intervals (its BPM = 60 000 / that mean), so a steady song becomes one line and a tempo change a new one.
/// </summary>
public sealed class ModelOnlyAlgorithm(Func<double[]?> beats) : ITimingAlgorithm
{
    public string Name => "model alone";
    public IReadOnlyList<TimingPoint> Run(double[] envelope, double durationMs)
    {
        var b = beats();
        if (b is null || b.Length < 4) return [];
        var t = b.Order().ToArray(); // EnvelopeCache.ExternalBeats already gives ms
        var lines = new List<TimingPoint>();
        int start = 0;
        double sum = 0;
        int count = 0;
        for (int k = 1; k < t.Length; k++)
        {
            double gap = t[k] - t[k - 1];
            if (count >= 4 && Math.Abs(gap / (sum / count) - 1) > 0.02)
            {
                lines.Add(new TimingPoint(OsuPrecision.Time(t[start]), 60000 / OsuPrecision.Bpm(60000 / ((t[k - 1] - t[start]) / count))));
                start = k - 1; sum = 0; count = 0;
            }
            sum += gap; count++;
        }
        if (count > 0) lines.Add(new TimingPoint(OsuPrecision.Time(t[start]), 60000 / OsuPrecision.Bpm(60000 / ((t[^1] - t[start]) / count))));
        return lines.GroupBy(p => p.Time).Select(g => g.First()).ToList();
    }
}

/// <summary>
/// Phase 4 N4: the editor's choice per song (Core <see cref="BeatModelTiming.PreferModel"/>) between the multi-BPM
/// analyzer and the beat model's own red lines (<see cref="BeatModelTiming.Lines"/>), scored as the editor runs it.
/// </summary>
public sealed class SelectedAlgorithm(MultiBpmOptions options, Func<double[]?> beats) : ITimingAlgorithm
{
    public string Name => "selected";
    public IReadOnlyList<TimingPoint> Run(double[] envelope, double durationMs)
    {
        var analyzer = MultiBpmAnalyzer.Analyze(envelope, options);
        var b = beats();
        var model = b is null ? [] : BeatModelTiming.Lines(b);
        // Both at osu! precision before comparing, as the evaluation that tuned the thresholds compared them.
        static IReadOnlyList<TimingSection> Osu(IReadOnlyList<TimingSection> s) => s
            .Select(x => new TimingSection(OsuPrecision.Time(x.TimeMs), OsuPrecision.Bpm(x.Bpm))).ToList();
        var chosen = BeatModelTiming.PreferModel(envelope, Osu(analyzer), Osu(model), durationMs) ? model : analyzer;
        return chosen.Select(s => new TimingPoint(OsuPrecision.Time(s.TimeMs), 60000 / OsuPrecision.Bpm(s.Bpm)))
            .GroupBy(p => p.Time).Select(g => g.First()).ToList();
    }
}

/// <summary>
/// Phase 4 variant "hybrid": the algorithm's steady sections (at least <paramref name="steadyMs"/> long) kept as they
/// are; the gaps between them (ramps and short steps, where the probe model scored ramp beats 0.448 against the
/// algorithm's 0.190) filled from the model's own red lines (<see cref="ModelOnlyAlgorithm"/>) that start inside them.
/// </summary>
public sealed class HybridAlgorithm(MultiBpmOptions options, Func<double[]?> beats, double steadyMs) : ITimingAlgorithm
{
    public string Name => $"hybrid steady>={steadyMs:0}";
    public IReadOnlyList<TimingPoint> Run(double[] envelope, double durationMs)
    {
        var alg = new MultiAlgorithm(options).Run(envelope, durationMs);
        var model = new ModelOnlyAlgorithm(beats).Run(envelope, durationMs);
        if (model.Count == 0) return alg;
        var steady = alg.Select((p, i) => (p, End: i + 1 < alg.Count ? alg[i + 1].Time : durationMs))
            .Where(x => x.End - x.p.Time >= steadyMs).ToList();
        bool InSteady(double t) => steady.Any(s => t >= s.p.Time - 1 && t < s.End - 1);
        var result = steady.Select(s => s.p).Concat(model.Where(m => !InSteady(m.Time)));
        // A steady section that is not the first: the model's line before it would run into it; fine, the next red line ends it.
        return result.GroupBy(p => p.Time).Select(g => g.First()).OrderBy(p => p.Time).ToList();
    }
}

/// <summary>
/// Phase 4 variant "algorithm + model octave": the algorithm's red lines, each section's BPM moved to the octave
/// (×½, ×2, ×1.5, ×⅔) whose beat count matches the beat model's beats in that section best, when that ratio is clearly
/// closer than keeping (within 15 % of the model's count, and at least 25 % closer than ×1).
/// </summary>
public sealed class ModelOctaveAlgorithm(MultiBpmOptions options, Func<double[]?> beats) : ITimingAlgorithm
{
    public string Name => "multi+model octave";
    public IReadOnlyList<TimingPoint> Run(double[] envelope, double durationMs)
    {
        var sections = MultiBpmAnalyzer.Analyze(envelope, options).ToList();
        var model = beats();
        if (model is not null)
            for (int i = 0; i < sections.Count; i++)
            {
                double a = sections[i].TimeMs, b = i + 1 < sections.Count ? sections[i + 1].TimeMs : durationMs;
                if (b - a < 10000) continue;
                int count = model.Count(t => t >= a && t < b);
                double expected = (b - a) * sections[i].Bpm / 60000;
                if (count < 8 || expected < 8) continue;
                double Off(double r) => Math.Abs(expected * r / count - 1);
                double best = new[] { 0.5, 2.0 / 3, 1.5, 2 }.MinBy(Off);
                if (Off(best) < 0.15 && Off(best) < 0.75 * Off(1)) sections[i] = sections[i] with { Bpm = sections[i].Bpm * best };
            }
        return sections.Select(s => new TimingPoint(OsuPrecision.Time(s.TimeMs), 60000 / OsuPrecision.Bpm(s.Bpm)))
            .GroupBy(p => p.Time).Select(g => g.First()).ToList();
    }
}

/// <summary>Multi-BPM, then the octave selector on each section of 10 s or more (docs/PHASE1-STATUS.md E36).</summary>
public sealed class OctaveAlgorithm(MultiBpmOptions options, double margin) : ITimingAlgorithm
{
    public string Name => "multi+octave margin " + margin.ToString(System.Globalization.CultureInfo.InvariantCulture);
    public IReadOnlyList<TimingPoint> Run(double[] envelope, double durationMs) =>
        OctaveSelector.Apply(envelope, MultiBpmAnalyzer.Analyze(envelope, options), durationMs, margin)
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
