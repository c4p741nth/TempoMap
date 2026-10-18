namespace TimingAnalyzer.Core;

public sealed record AnalysisResult
{
    public double Rate { get; init; }
    public double Seconds { get; init; }
    public int? Status { get; init; }
    public int EnvelopeLength { get; init; }
    public string? Error { get; init; }
    public double? ApproximateBpm { get; init; }
    public double? Bpm { get; init; }
    public double? OffsetInternal { get; init; }
    public double? Offset => OffsetInternal - TimingConstants.OffsetDisplay;
    public int Flags { get; init; }
    public string? Timeline { get; init; }
}

public static class Analyzer
{
    public static AnalysisResult Analyze(double[] mono, double rate, double bpm = 0, double? offset = null,
        Coefficients? coefficients = null)
    {
        var result = new AnalysisResult { Rate = rate, Seconds = mono.Length / rate };
        if (result.Seconds > TimingConstants.MaximumSeconds) return result with { Error = "longer than 10 minutes" };
        var d = OnsetEnvelope.Compute(mono, rate, coefficients);
        return AnalyzeEnvelope(d, bpm, offset) with { Rate = rate, Seconds = result.Seconds };
    }

    public static AnalysisResult AnalyzeEnvelope(double[] d, double bpm = 0, double? offset = null)
    {
        var result = new AnalysisResult { EnvelopeLength = d.Length };
        if (bpm < 1e-30)
        {
            var detected = BpmDetector.Detect(d);
            result = result with { Status = detected.Status };
            if (detected.Status < 0) return result with { Error = "cannot determine timing" };
            bpm = detected.Bpm!.Value;
            if (detected.Status >= 16) return result with { ApproximateBpm = bpm };
            offset = null;
        }
        double offsetInternal = offset is null ? OffsetDetector.Detect(d, bpm) : OffsetDetector.FoldUserOffset(offset.Value, bpm);
        var timeline = Core.Timeline.Build(d, bpm, offsetInternal);
        return result with { Bpm = bpm, OffsetInternal = offsetInternal, Flags = timeline.Flags, Timeline = timeline.Marks };
    }
}
