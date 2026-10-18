using System.Globalization;

namespace TimingAnalyzer.Editor;

/// <summary>
/// A multi-BPM auto-timing result from the boundary: one entry per proposed red line, already at osu! precision.
/// App maps the Core multi-BPM analyzer onto this, so Editor stays independent of Core (as with
/// <see cref="TimingDetection"/>).
/// </summary>
public sealed record MultiTimingDetection(IReadOnlyList<TimingPoint> Sections, string? Error = null, int? Status = null,
    LiveDrumHint? LiveDrums = null);

/// <summary>The drums wobble enough from beat to beat to suggest drift mode's red line on every beat (Core DrumWobble).</summary>
public sealed record LiveDrumHint(double WobbleMs, int Beats);

/// <summary>
/// A reviewable proposal of several red lines. Unlike <see cref="AutoTimingProposal"/> the offset is not editable:
/// each section carries its own phase, so a single offset box has nothing to apply to. Applying replaces every red
/// line in one undo step and keeps green lines, like the single-line proposal.
/// </summary>
public sealed record MultiTimingProposal(IReadOnlyList<TimingPoint> Sections)
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    public static MultiTimingProposal FromDetection(MultiTimingDetection detection)
    {
        if (detection.Error is not null) throw new InvalidOperationException(detection.Error);
        if (detection.Sections.Count == 0) throw new InvalidOperationException("No timing detected.");
        // osu! precision, sorted, and one red line per time (a duplicate time would be unreachable anyway).
        var points = BeatGrid.Sort(detection.Sections
            .Select(p => (p with { Time = OsuPrecision.Time(p.Time), BeatLength = 60000 / OsuPrecision.Bpm(p.Bpm) }).Validate())
            .GroupBy(p => p.Time).Select(g => g.First()));
        return new MultiTimingProposal(points);
    }

    /// <summary>One line per section, so the user can read the proposal before replacing their timing.</summary>
    public IReadOnlyList<string> Lines => Sections
        .Select((p, i) => (i + 1).ToString(Invariant) + ". " + OsuPrecision.FormatTime(p.Time) + " ms — " +
            OsuPrecision.FormatBpm(p.Bpm) + " BPM")
        .ToList();

    /// <summary>Above this many sections the description gives the BPM range; the list below has every line.</summary>
    public const int ListedSections = 12;

    public string Description => Sections.Count == 1
        ? "Auto-time (multi-BPM) found one tempo: " + OsuPrecision.FormatBpm(Sections[0].Bpm) + " BPM at " +
          OsuPrecision.FormatTime(Sections[0].Time) + " ms. Review it before replacing red lines."
        : "Auto-time (multi-BPM) found " + Sections.Count.ToString(Invariant) + " sections: " +
          (Sections.Count <= ListedSections
              ? string.Join(", ", Sections.Select(p => OsuPrecision.FormatBpm(p.Bpm) + " BPM at " + OsuPrecision.FormatTime(p.Time) + " ms"))
              : OsuPrecision.FormatBpm(Sections.Min(p => p.Bpm)) + "-" + OsuPrecision.FormatBpm(Sections.Max(p => p.Bpm)) + " BPM from " +
                OsuPrecision.FormatTime(Sections[0].Time) + " to " + OsuPrecision.FormatTime(Sections[^1].Time) + " ms, listed below") +
          ". Review them before replacing red lines; Apply is one undo step and keeps green lines.";
}
