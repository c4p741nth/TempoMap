namespace TimingAnalyzer.Core;

/// <summary>
/// Red lines for a stretch of the song the user highlighted (the editor's "Auto-time range"): the multi-BPM analyzer
/// on that slice of the onset envelope, with absolute times, so a tempo change inside the range gets its own red line.
/// The slice keeps the whole-song tracker's mistakes elsewhere from leaking in. a crowd of rebellion - Ill
/// (2026-10-18): the whole song gives 189.3 BPM (4/3 of the real tempo) and a false ramp over 131.9-158.8 s, while
/// the slice 131.7-169.7 s gives 142.02 BPM on the drums and 120-180 s gives 146.7 → 142 at 131 678 → 220.18 at
/// 169 710. On reference sections where whole-song timing hits under half the beats (timing-eval range-section,
/// held-out and tuning halves), the slice brings aligned 10 ms recall from 0.09-0.28 to 0.30-0.66, slightly ahead of
/// the single-BPM detector on the same slice (0.26-0.64). One red line also gets the whole-BPM pass (the whole song
/// keeps the original's single answer instead): Ill's 142.02 becomes 142; the scores above are the same either way.
/// </summary>
public static class RangeTiming
{
    /// <summary>Shorter ranges lock to quarter or half beats too often (MAMBO COMBO, docs/PHASE1-STATUS.md E17).</summary>
    public const double MinimumMs = 4000;

    /// <summary>
    /// Experiment for <see cref="MultiBpmOptions.AnchorBpm"/> (timing-eval range ranchor=1): the original detector on the
    /// whole song, exact or approximate; null when it finds nothing. The editor does not use it.
    /// </summary>
    public static double? WholeSongAnchor(double[] envelope)
    {
        var whole = Analyzer.AnalyzeEnvelope(envelope);
        return whole.Bpm ?? whole.ApproximateBpm;
    }

    /// <summary>
    /// Experiment, off in the editor (2026-10-21, timing-eval range / range-section rpad=ms): with <paramref
    /// name="contextMs"/> &gt; 0 the analyzer also sees that much of the song on each side, and only the red lines inside
    /// the range are kept (the one in effect at its start moves to its first beat in the range). Moonlit Occultation's
    /// 206 → 240 ramp highlighted from its first beat (01:48:327-02:01) then gets the whole song's steps (max 6 ms on
    /// the clearly heard beats) instead of 214.5 / 230.669 / 245, together with the whole-song octave anchor. 10 s on
    /// the evaluation sets: ranges around reference tempo changes better on 6 of 8 (A hold 0.368 → 0.538, C hold
    /// 0.462 → 0.615), D hold 0.517 → 0.476; one-tempo sections worse on 4 (C tune 0.813 → 0.747): the context brings
    /// the neighbour's tempo back in, which the slice keeps out. Context only when the range alone has no exact BPM
    /// (measured, not kept) still lowered 7 sets by 0.001-0.042, and a synthetic ramp gets an exact (wrong) BPM on
    /// its own. docs/PHASE1-STATUS.md E41.
    /// </summary>
    public static IReadOnlyList<TimingSection> Analyze(double[] envelope, double startMs, double endMs, MultiBpmOptions? options = null,
        double contextMs = 0)
    {
        int a = Math.Clamp((int)Math.Floor(startMs), 0, envelope.Length);
        int b = Math.Clamp((int)Math.Ceiling(endMs), a, envelope.Length);
        if (b - a < MinimumMs) return [];
        var o = options ?? new MultiBpmOptions();
        int pad = (int)Math.Max(0, contextMs), ca = Math.Max(0, a - pad), cb = Math.Min(envelope.Length, b + pad);
        var sections = MultiBpmAnalyzer.Analyze(envelope[ca..cb], o);
        if (sections.Count == 1 && o.WholeBpmDriftMs > 0) sections = MultiBpmAnalyzer.WholeBpm(sections, cb - ca, o.WholeBpmDriftMs);
        var lines = sections.Select(s => s with { TimeMs = s.TimeMs + ca }).ToList();
        return pad > 0 ? Inside(lines, a, b) : lines;
    }

    private static List<TimingSection> Inside(List<TimingSection> lines, double a, double b)
    {
        var inside = lines.Where(s => s.TimeMs >= a && s.TimeMs < b).ToList();
        if (lines.LastOrDefault(s => s.TimeMs < a) is { } before)
        {
            double length = 60000 / before.Bpm, first = before.TimeMs + Math.Ceiling((a - before.TimeMs) / length) * length;
            if (first < b && (inside.Count == 0 || first < inside[0].TimeMs - 1)) inside.Insert(0, before with { TimeMs = first });
        }
        return inside;
    }
}
