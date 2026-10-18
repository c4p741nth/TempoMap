using TimingAnalyzer.Core;
using TimingAnalyzer.Editor;

namespace TimingAnalyzer.App;

/// <summary>
/// Maps editor timing requests onto the parity-locked Core. The onset envelope (1 sample per ms) is computed once
/// per song; a section analyzes a slice of it, so sections have no filter start-up transient. The whole song runs
/// exactly what <see cref="Analyzer.Analyze"/> runs.
/// </summary>
public sealed class CoreTiming
{
    private AudioContent? source;
    private double[] envelope = [];

    public TimingDetection Detect(AudioContent audio, TimingRequest request)
    {
        if (!ReferenceEquals(audio, source)) { envelope = OnsetEnvelope.Compute(audio.Mono, audio.Rate); source = audio; }
        bool whole = request.StartMs <= 0 && request.EndMs >= audio.DurationMs;
        if (whole && audio.Mono.Length / (double)audio.Rate > TimingConstants.MaximumSeconds)
            return new(null, null, null, null, "longer than 10 minutes");
        int a = whole ? 0 : Math.Clamp((int)Math.Floor(request.StartMs), 0, envelope.Length);
        int b = whole ? envelope.Length : Math.Clamp((int)Math.Ceiling(request.EndMs), a, envelope.Length);
        var result = Analyzer.AnalyzeEnvelope(whole ? envelope : envelope[a..b], request.Bpm ?? 0);
        // Core offsets are relative to the analyzed slice.
        return new(result.Bpm, result.Offset + a, result.ApproximateBpm, result.Status, result.Error);
    }

    /// <summary>Whole-song multi-BPM timing (docs/PHASE2-PLAN.md M1-M2), reusing the cached onset envelope.</summary>
    public MultiTimingDetection DetectMulti(AudioContent audio)
    {
        if (audio.Mono.Length / (double)audio.Rate > TimingConstants.MaximumSeconds)
            return new([], "longer than 10 minutes");
        if (!ReferenceEquals(audio, source)) { envelope = OnsetEnvelope.Compute(audio.Mono, audio.Rate); source = audio; }
        var (sections, beats) = MultiBpmAnalyzer.AnalyzeWithBeats(envelope);
        if (sections.Count == 0) return new([], "cannot determine timing");
        var wobble = DrumWobble.Measure(audio.Mono, audio.Rate, beats);
        return new(sections.Select(s => new TimingPoint(s.TimeMs, 60000 / s.Bpm)).ToList(), null, Analyzer.AnalyzeEnvelope(envelope).Status,
            wobble is { SuggestsLiveDrums: true } ? new LiveDrumHint(wobble.MedianMs, wobble.Triples) : null);
    }

    /// <summary>Drift mode: one red line every <paramref name="beatsPerLine"/> tracked beats on a continuous grid.</summary>
    public MultiTimingDetection DetectDrift(AudioContent audio, int beatsPerLine)
    {
        if (audio.Mono.Length / (double)audio.Rate > TimingConstants.MaximumSeconds)
            return new([], "longer than 10 minutes");
        if (!ReferenceEquals(audio, source)) { envelope = OnsetEnvelope.Compute(audio.Mono, audio.Rate); source = audio; }
        var lines = DriftLines.Fit(envelope, beatsPerLine);
        if (lines.Count == 0) return new([], "cannot track beats");
        return new(lines.Select(s => new TimingPoint(s.TimeMs, 60000 / s.Bpm)).ToList());
    }

    /// <summary>An onset this far (ms) from where the line was placed counts half as much as one right under it.</summary>
    public const double SnapHalfWeightMs = 20;

    /// <summary>
    /// For each query, the osu! time of the onset peak within its window (envelope peak − 28.7 ms, the calibration the
    /// detectors use) with the most strength / (1 + (distance / <see cref="SnapHalfWeightMs"/>)²), parabolically
    /// interpolated; null when the window holds no peak. The distance weight keeps a stronger hit farther off from
    /// beating the accent the line was placed on (Yooh - RPG 178775: an unweighted search took a peak 58 ms away).
    /// On the official RPG ramps with simulated ear errors up to 30 ms, 20 ms gave 132 of 150 step BPMs, against 127
    /// for 1 − (distance / window)² and 126-127 for 10-15 ms.
    /// </summary>
    public IReadOnlyList<double?> FindOnsets(AudioContent audio, IReadOnlyList<OnsetQuery> queries)
    {
        if (!ReferenceEquals(audio, source)) { envelope = OnsetEnvelope.Compute(audio.Mono, audio.Rate); source = audio; }
        var d = envelope;
        return queries.Select(q =>
        {
            double centre = q.TimeMs + MultiBpmAnalyzer.PeakToOsuMs;
            int lo = Math.Max(1, (int)Math.Floor(centre - q.WindowMs)), hi = Math.Min(d.Length - 2, (int)Math.Ceiling(centre + q.WindowMs));
            int best = -1;
            double bestScore = 0;
            for (int j = lo; j <= hi; j++)
            {
                if (!(d[j] > 0 && d[j] >= d[j - 1] && d[j] >= d[j + 1])) continue;
                double x = (j - centre) / SnapHalfWeightMs, score = d[j] / (1 + x * x);
                if (score > bestScore) { best = j; bestScore = score; }
            }
            if (best < 0) return (double?)null;
            double den = (d[best] - d[best + 1]) + (d[best] - d[best - 1]);
            double at = den > 0 ? best + 0.5 * (d[best + 1] - d[best - 1]) / den : best;
            return at - MultiBpmAnalyzer.PeakToOsuMs;
        }).ToList();
    }
}
