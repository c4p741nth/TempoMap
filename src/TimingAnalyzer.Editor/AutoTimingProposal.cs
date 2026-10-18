using System.Globalization;

namespace TimingAnalyzer.Editor;

// App maps the parity-locked Core result to this boundary type; Editor remains independent of Core.
public sealed record TimingDetection(double? Bpm, double? OsuOffsetMs, double? ApproximateBpm, int? Status, string? Error);
public sealed record AutoTimingProposal(double Bpm, double? OffsetMs, bool Approximate, int? DetectorStatus, bool Locked = false)
{
    public static AutoTimingProposal FromDetection(TimingDetection result, bool locked = false) => FromDetectionCore(result) with { Locked = locked };
    private static AutoTimingProposal FromDetectionCore(TimingDetection result)
    {
        if (result.Error is not null) throw new InvalidOperationException(result.Error);
        bool approximate = result.ApproximateBpm is not null;
        double bpm = (approximate ? result.ApproximateBpm : result.Bpm) ?? throw new InvalidOperationException("No BPM detected.");
        if (!double.IsFinite(bpm) || bpm <= 0) throw new InvalidOperationException("Invalid detected BPM.");
        if (!approximate && (result.OsuOffsetMs is not { } offset || !double.IsFinite(offset)))
            throw new InvalidOperationException("No offset detected.");
        // osu! precision: BPM to 3 decimals, offset to integer ms.
        return new(OsuPrecision.Bpm(bpm), approximate ? null : OsuPrecision.Time(result.OsuOffsetMs!.Value), approximate, result.Status);
    }
    public string Description => (Locked ? "Locked" : Approximate ? "Approximate" : "Exact") + " BPM: " + OsuPrecision.FormatBpm(Bpm) +
        "; osu! offset: " + (OffsetMs is { } offset ? OsuPrecision.FormatTime(offset) + " ms" : "unavailable; enter an offset") +
        (Locked ? "; offset fitted for the locked BPM" :
            "; detector status: " + (DetectorStatus?.ToString(CultureInfo.InvariantCulture) ?? "unknown"));
    public TimingPoint AtOffset(string offsetText) => new TimingPoint(OsuPrecision.Time(
        double.Parse(offsetText, NumberStyles.Float, CultureInfo.InvariantCulture)), 60000 / Bpm).Validate();
}
