namespace TimingAnalyzer.Core;

/// <summary>Tuning knobs for <see cref="MultiBpmAnalyzer"/> (docs/PHASE2-PLAN.md). Defaults are the tuned values.</summary>
public sealed record MultiBpmOptions
{
    /// <summary>Frame size of the tempo and beat tracking (the onset envelope is 1 ms; beats are refined back to it).</summary>
    public int FrameMs { get; init; } = 5;
    /// <summary>Tuned on Set A's tuning half (2026-10-16): 4 s / 0.25 s beat 8 s / 0.5 s on held-out aligned F10 (0.768 vs 0.717).</summary>
    public double WindowMs { get; init; } = 4000;
    public double HopMs { get; init; } = 250;
    public double MinBpm { get; init; } = 60;
    public double MaxBpm { get; init; } = 400;
    /// <summary>Local tempo stays within the whole-song period divided or multiplied by this (octave anchor).</summary>
    public double OctaveRange { get; init; } = 1.6;
    /// <summary>Viterbi cost of changing tempo between windows, per (ln ratio)².</summary>
    public double TempoChangeCost { get; init; } = 400;
    /// <summary>Beat DP weight of the inter-beat interval deviating from the local period (Ellis's α).</summary>
    public double BeatTightness { get; init; } = 2000;
    public double RefineMs { get; init; } = 25;
    /// <summary>A beat counts as supported when its onset peak reaches this share of the median peak.</summary>
    public double SupportRatio { get; init; } = 0.3;
    /// <summary>Segmentation cost of one more tempo section, in ms² of inter-beat-interval residual.</summary>
    public double SectionPenalty { get; init; } = 1000;
    public int MinSectionBeats { get; init; } = 4;
    /// <summary>Intervals further than this share from their section's mean are ignored (phase flips, missed beats).</summary>
    public double IntervalOutlier { get; init; } = 0.12;
    /// <summary>Sections at least this long get BPM and phase by epoch folding, like the original offset.</summary>
    public double LongSectionMs { get; init; } = 8000;
    /// <summary>Folding searches the period within this share of the tracked one.</summary>
    public double FoldSearch { get; init; } = 0.015;
    /// <summary>Snap BPM to a whole or half value when that drifts less than this over the section.</summary>
    public double SnapDriftMs { get; init; } = 4;
    /// <summary>A short section starts on the previous grid's beat when its own fit is within this share of a beat.</summary>
    public double ContinuityShare { get; init; } = 1.0 / 6;
    /// <summary>Sections with fewer supported beats than this share keep the previous grid (breaks, intros).</summary>
    public double WeakSupport { get; init; } = 0.5;
    /// <summary>Neighbouring sections with the same BPM whose grids line up within this merge into one.</summary>
    public double MergeMs { get; init; } = 5;
    /// <summary>A neighbour's grid that reaches this share of a section's own folded peak replaces its red line.</summary>
    public double InheritRatio { get; init; } = 0.85;

    /// <summary>Track beats in a joint (time, interval) space instead of along a fixed tempogram period (Phase 3 P1).</summary>
    public bool JointTracker { get; init; }
    /// <summary>Joint tracker: cost of the inter-beat interval changing between consecutive beats, per (ln ratio)².</summary>
    public double TempoContinuity { get; init; } = 100;
    /// <summary>Joint tracker: cost of an interval straying from the tempogram's local period, per (ln ratio)² (keeps the metre).</summary>
    public double TempoPrior { get; init; } = 20;
    /// <summary>Joint tracker: the largest interval change between consecutive beats that is a step rather than a jump.</summary>
    public double MaxStepChange { get; init; } = 0.15;
    /// <summary>Joint tracker: flat cost of any larger tempo change (a sudden new section).</summary>
    public double JumpCost { get; init; } = 8;
    /// <summary>Joint tracker: intervals searched span the tempogram's periods divided/multiplied by this.</summary>
    public double JointRange { get; init; } = 1.4;

    /// <summary>
    /// Free tempo (Phase 3, option A): after the sections are built, track beats with the joint tracker and add a red
    /// line wherever a run of them drifts steadily off the section grid (live or old recordings). Off by default.
    /// </summary>
    public bool FreeTempo { get; init; }
    /// <summary>Free tempo: a beat must sit this far from the current grid (ms) before a new red line is considered.</summary>
    public double DriftMs { get; init; } = 8;
    /// <summary>Free tempo: consecutive supported beats that must all drift the same way before a red line is added.</summary>
    public int DriftBeats { get; init; } = 4;
    /// <summary>Free tempo: each beat may move off the grid by at most this share of a beat more than the previous one
    /// (a drift is gradual; an off-beat or quarter-beat flip jumps at once).</summary>
    public double DriftMaxChange { get; init; } = 0.03;
    /// <summary>Free tempo: a new red line is kept only if its local grid folds at least this share as strongly as the
    /// current one. 1.25 is the lowest value that leaves Set B at one red line on every song (measured 2026-10-16).</summary>
    public double DriftFoldRatio { get; init; } = 1.25;
    /// <summary>Free tempo: joint tracker continuity used for the drift beats.</summary>
    public double DriftContinuity { get; init; } = 1000;

    /// <summary>
    /// Tempo ramps (Phase 3 P2, <see cref="RampBridge"/>): fill the gap between two sections with whole-BPM steps.
    /// On (2026-10-17): ramp-beat recall on the held-out halves C 0.061 → 0.102, A 0.113 → 0.161, D 0.267 → 0.295,
    /// aligned F10 equal or better on every set, Set B unchanged.
    /// </summary>
    public bool Ramps { get; init; } = true;
    /// <summary>Ramps: only tempo changes of at least this share are examined.</summary>
    public double RampMinChange { get; init; } = 0.02;
    /// <summary>Ramps: a 4-beat window fits a grid when its onset strength reaches this share of the grid's median window.</summary>
    public double RampGoodRatio { get; init; } = 0.7;
    /// <summary>Ramps: a gap shorter than this many beats is a plain switch.</summary>
    public double RampMinGapBeats { get; init; } = 4;
    /// <summary>Ramps: a gap longer than this (ms) is not treated as a ramp.</summary>
    public double RampMaxMs { get; init; } = 30000;
    /// <summary>Ramps: step BPMs may overshoot the range between the two sections by this share.</summary>
    public double RampSlack { get; init; } = 0.03;
    /// <summary>Ramps: allowed step lengths in beats.</summary>
    public IReadOnlyList<int> RampStepBeats { get; init; } = [4, 6, 8, 12, 16];
    /// <summary>Ramps: the last step must end this close (ms) to the next section's grid beat.</summary>
    public double RampLandMs { get; init; } = 20;
    /// <summary>Ramps: cost of each red line, in typical beats of onset evidence.</summary>
    public double RampStepPenalty { get; init; } = 0.5;
    /// <summary>Ramps: the ramp must beat the best plain switch by this many typical beats.</summary>
    public double RampMargin { get; init; } = 1;
    /// <summary>Ramps: beats per red line of a continuous (linear-BPM) ramp.</summary>
    public IReadOnlyList<int> RampSmoothBeats { get; init; } = [1, 2, 4];
    /// <summary>Ramps: cost of each red line of a continuous ramp, in typical beats of onset evidence.</summary>
    public double RampLinePenalty { get; init; } = 0.3;

    /// <summary>
    /// Experiment (2026-10-17): beat times (ms of the decoded audio) from an outside beat tracker, used instead of
    /// <see cref="MultiBpmAnalyzer.TrackBeats"/>; each is refined to the onset envelope like a tracked beat. Null =
    /// track beats here.
    /// </summary>
    public IReadOnlyList<double>? ExternalBeats { get; init; }
    /// <summary>Envelope index of an outside beat = its audio time + this (ms): the envelope's filter delay.</summary>
    public double ExternalLagMs { get; init; }
    /// <summary>
    /// Outside beats are brought to the whole-song estimate's beat level first: n evenly spaced beats per interval of
    /// about n periods, and beats closer than ¾ period to the last kept one dropped (Beat This! is at the reference's
    /// level on 83 of 179 eval songs, half tempo on 29).
    /// </summary>
    public bool ExternalToOctave { get; init; }

    /// <summary>
    /// Final pass: a red line's BPM becomes the nearest whole BPM when that changes its section's length by at most
    /// this many ms over all its beats; the grid stays centred on the section's middle beat. 0 = off. Ranked maps
    /// write 73 % of red lines as whole BPMs (docs/PHASE3-STATUS.md, Set F). Only songs with more than one red line,
    /// so Set B keeps the original's single-BPM answer. 8 ms (2026-10-18): lines with exactly the map's BPM
    /// 128 → 161 of 219 on the mania sets; aligned F10 equal or better on every set but C hold (−0.001).
    /// </summary>
    public double WholeBpmDriftMs { get; init; } = 8;

    /// <summary>
    /// Final pass against half tempo: a section of at least 8 beats at or below this BPM is doubled when its onsets,
    /// folded at its beat length, peak at the half beat at least <see cref="HalfTempoRatio"/> as high as on the beat.
    /// 0 = off. The original's > 210 BPM halving anchors the tracker, so most octave errors are half tempo (80 % of
    /// the octave-wrong reference beats on the tuning halves, at a median map BPM of 224). Only songs with more
    /// than one red line (Set B keeps the original's answer). 130 / 0.5 chosen on the tuning halves (2026-10-18):
    /// aligned F10 C tune 0.683 → 0.740, D 0.691 → 0.733, A 0.665 → 0.737; held out C 0.558 → 0.647,
    /// D 0.715 → 0.727, E 0.409 → 0.489, A 0.773 → 0.787, F 0.545 → 0.557; B identical.
    /// </summary>
    public double HalfTempoMaxBpm { get; init; } = 130;
    /// <summary>See <see cref="HalfTempoMaxBpm"/>: half-beat peak / beat peak of the folded onsets.</summary>
    public double HalfTempoRatio { get; init; } = 0.5;

    /// <summary>
    /// Experiment, off (2026-10-18): a section of at least 8 beats whose BPM × 1.5 is at or below this is sped up by
    /// 1.5 when its onsets, folded at its beat length, peak at both ⅓ and ⅔ of the beat at least
    /// <see cref="ThirdBeatRatio"/> as high as on the beat. Section features looked clean (C/D/A tune 4 fixed, 1
    /// broken), but end to end it lost: C tune aligned 0.740 → 0.725 (M1LLI0N PP −0.175, DiSANiMATE −0.143: triplet
    /// fills inside right sections), only XNOR XNOR XNOR gained (+0.058). 0 = off.
    /// </summary>
    public double ThirdBeatMaxBpm { get; init; }
    /// <summary>See <see cref="ThirdBeatMaxBpm"/>: the weaker of the ⅓ and ⅔ beat peaks / the beat peak.</summary>
    public double ThirdBeatRatio { get; init; } = 0.6;
}

/// <summary>One red line: osu! time of its first beat (ms, unrounded) and its BPM (unrounded unless snapped).</summary>
public sealed record TimingSection(double TimeMs, double Bpm);

/// <summary>A tracked beat in osu! time with its onset support (0 = no onset near it).</summary>
public readonly record struct TrackedBeat(double TimeMs, double Weight);

/// <summary>Intermediate results of <see cref="MultiBpmAnalyzer.Analyze"/>: tracked beats, the number of
/// constant-tempo spans, red lines before the merge passes, and the final red lines.</summary>
internal sealed record MultiBpmStages(List<TrackedBeat> Beats, int Spans, List<TimingSection> Unmerged, IReadOnlyList<TimingSection> Final);

/// <summary>
/// Multi-BPM auto-timing: local tempo (windowed autocorrelation + Viterbi), dynamic-programming beat tracking that
/// follows that tempo, beats refined to onset peaks, then an optimal piecewise-linear segmentation of beat times
/// into red lines. New code beside the parity-locked original; it reuses only the onset envelope and BPM detector.
/// </summary>
public static class MultiBpmAnalyzer
{
    /// <summary>An onset peak at envelope index j is osu! time j − 28.7 ms: the original offset's calibration.</summary>
    public const double PeakToOsuMs = TimingConstants.OffsetLatency + TimingConstants.OffsetDisplay;

    public static IReadOnlyList<TimingSection> Analyze(double[] d, MultiBpmOptions? options = null) => Diagnose(d, options).Final;

    /// <summary><see cref="Analyze"/> and the tracked beats behind it (the editor measures <see cref="DrumWobble"/> on them).</summary>
    public static (IReadOnlyList<TimingSection> Sections, IReadOnlyList<TrackedBeat> Beats) AnalyzeWithBeats(double[] d, MultiBpmOptions? options = null)
    {
        var stages = Diagnose(d, options);
        return (stages.Final, stages.Beats);
    }

    /// <summary>Every stage of <see cref="Analyze"/>, for the eval harness: tracked beats, the red lines before the
    /// merge passes, and the final answer.</summary>
    internal static MultiBpmStages Diagnose(double[] d, MultiBpmOptions? options = null)
    {
        var o = options ?? new MultiBpmOptions();
        // The original whole-song result: octave anchor, and the answer whenever only one tempo is found.
        var original = Analyzer.AnalyzeEnvelope(d);
        if (original.Bpm is null && original.ApproximateBpm is { } approximate) original = Analyzer.AnalyzeEnvelope(d, approximate);
        TimingSection? single = original.Bpm is { } bpm && original.Offset is { } offset ? new TimingSection(offset, bpm) : null;
        var beats = o.ExternalBeats is { } external
            ? RefineBeats(d, o.ExternalToOctave && single is not null ? ToBeatLevel(external, 60000 / single.Bpm) : external, o)
            : TrackBeats(d, o, single?.Bpm);
        if (beats.Count < o.MinSectionBeats) return new(beats, 0, [], single is null ? [] : [single]);
        var (spans, unmerged) = SectionBuilder.Sections(d, beats, o);
        var sections = SectionBuilder.Merge(d, beats, unmerged, o, single);
        // One tempo: the original's estimate over the whole song is the better one (Set B regression guard).
        IReadOnlyList<TimingSection> final = sections.Count <= 1 && single is not null ? [single] : sections;
        if (o.Ramps && final.Count > 1)
            final = RampBridge.Fill(d, final, beats[^1].TimeMs + 60000 / final[^1].Bpm, o);
        if (o.FreeTempo && final.Count > 0)
        {
            var drift = TrackBeats(d, o with { JointTracker = true, TempoContinuity = o.DriftContinuity }, single?.Bpm);
            final = FreeTempoLines.Add(d, final, drift, o);
        }
        if (o.WholeBpmDriftMs > 0 && final.Count > 1)
            final = WholeBpm(final, beats[^1].TimeMs + 60000 / final[^1].Bpm, o.WholeBpmDriftMs);
        if (o.HalfTempoMaxBpm > 0 && final.Count > 1)
            final = DoubleHalfTempo(d, final, beats[^1].TimeMs + 60000 / final[^1].Bpm, o);
        if (o.ThirdBeatMaxBpm > 0 && final.Count > 1)
            final = SpeedUpDottedBeat(d, final, beats[^1].TimeMs + 60000 / final[^1].Bpm, o);
        return new(beats, spans, unmerged, final);
    }

    /// <summary>See <see cref="MultiBpmOptions.HalfTempoMaxBpm"/>. The red line keeps its time; its beats stay beats.</summary>
    internal static IReadOnlyList<TimingSection> DoubleHalfTempo(double[] d, IReadOnlyList<TimingSection> sections, double songEnd, MultiBpmOptions o) =>
        Rescale(sections, songEnd, 2, (s, length, count) =>
            s.Bpm <= o.HalfTempoMaxBpm && SubBeatRatio(d, s.TimeMs, length, count, [0.5]) >= o.HalfTempoRatio);

    /// <summary>See <see cref="MultiBpmOptions.ThirdBeatMaxBpm"/>. The red line keeps its time.</summary>
    internal static IReadOnlyList<TimingSection> SpeedUpDottedBeat(double[] d, IReadOnlyList<TimingSection> sections, double songEnd, MultiBpmOptions o) =>
        Rescale(sections, songEnd, 1.5, (s, length, count) =>
            1.5 * s.Bpm <= o.ThirdBeatMaxBpm && SubBeatRatio(d, s.TimeMs, length, count, [1.0 / 3, 2.0 / 3]) >= o.ThirdBeatRatio);

    /// <summary>Multiplies the BPM of each section of at least 8 beats for which <paramref name="test"/> holds.</summary>
    private static IReadOnlyList<TimingSection> Rescale(IReadOnlyList<TimingSection> sections, double songEnd, double factor,
        Func<TimingSection, double, int, bool> test)
    {
        var result = new List<TimingSection>();
        for (int i = 0; i < sections.Count; i++)
        {
            var s = sections[i];
            double length = 60000 / s.Bpm, end = i + 1 < sections.Count ? sections[i + 1].TimeMs : songEnd;
            int count = (int)((end - s.TimeMs) / length);
            result.Add(count >= 8 && test(s, length, count) ? s with { Bpm = factor * s.Bpm } : s);
        }
        return result;
    }

    /// <summary>Onsets folded at <paramref name="length"/> from <paramref name="start"/>: peak near the half beat / peak near the beat (±12 ms).</summary>
    internal static double HalfBeatRatio(double[] d, double start, double length, int count) => SubBeatRatio(d, start, length, count, [0.5]);

    /// <summary>
    /// Onsets folded at <paramref name="length"/> from <paramref name="start"/>: the weakest peak near the given
    /// fractions of the beat / the peak near the beat (each ±12 ms).
    /// </summary>
    internal static double SubBeatRatio(double[] d, double start, double length, int count, IReadOnlyList<double> fractions)
    {
        int c = (int)Math.Ceiling(length), folds = 0;
        var h = new double[c];
        for (int k = 0; k < count; k++)
        {
            int a = (int)Math.Round(start + k * length + PeakToOsuMs);
            if (a < 0 || a + c > d.Length) continue;
            for (int j = 0; j < c; j++) h[j] += Math.Max(0, d[a + j]);
            folds++;
        }
        if (folds == 0) return 0;
        double Peak(double at)
        {
            double best = 0;
            for (int j = -12; j <= 12; j++) best = Math.Max(best, h[(((int)Math.Round(at) + j) % c + c) % c]);
            return best;
        }
        double onBeat = Peak(0);
        return onBeat > 0 ? fractions.Min(f => Peak(f * length)) / onBeat : 0;
    }

    /// <summary>
    /// See <see cref="MultiBpmOptions.WholeBpmDriftMs"/>: drift = beats in the section × change of beat length. The
    /// snapped grid keeps the section's middle beat in place, so the error splits between both ends instead of piling
    /// up at the last beat; a line that would move onto or past its predecessor keeps its own BPM.
    /// </summary>
    internal static IReadOnlyList<TimingSection> WholeBpm(IReadOnlyList<TimingSection> sections, double songEnd, double driftMs)
    {
        var result = new List<TimingSection>();
        for (int i = 0; i < sections.Count; i++)
        {
            var s = sections[i];
            double length = 60000 / s.Bpm, whole = Math.Round(s.Bpm), snapped = whole > 0 ? 60000 / whole : length;
            double end = i + 1 < sections.Count ? sections[i + 1].TimeMs : songEnd;
            double beats = Math.Max(1, (end - s.TimeMs) / length), middle = Math.Floor(beats / 2);
            double time = s.TimeMs + middle * (length - snapped);
            bool fits = beats * Math.Abs(snapped - length) <= driftMs && (result.Count == 0 || time > result[^1].TimeMs);
            result.Add(fits ? new TimingSection(time, whole) : s);
        }
        return result;
    }

    public static List<TrackedBeat> TrackBeats(double[] d, MultiBpmOptions o, double? anchorBpm = null)
    {
        int f = o.FrameMs;
        int frames = d.Length / f;
        if (frames < 4) return [];
        // Half-wave rectified onset strength, decimated by max so peaks survive, normalized by its spread.
        var onset = new double[frames];
        for (int i = 0; i < frames; i++)
        {
            double m = 0;
            for (int j = i * f; j < (i + 1) * f; j++) m = Math.Max(m, d[j]);
            onset[i] = m;
        }
        double mean = onset.Average(), sd = Math.Sqrt(onset.Select(v => (v - mean) * (v - mean)).Average());
        if (!(sd > 0)) return [];
        for (int i = 0; i < frames; i++) onset[i] /= sd;

        double? globalMs = anchorBpm is { } bpm ? 60000 / bpm : null;
        double[] period = TempoTracker.Track(onset, globalMs / f, o);
        int[] beatFrames = o.JointTracker ? JointBeatDp(onset, period, o) : BeatDp(onset, period, o.BeatTightness);

        var beats = new List<TrackedBeat>(beatFrames.Length);
        foreach (int b in beatFrames)
        {
            double centre = b * f + f / 2.0;
            double radius = Math.Min(o.RefineMs, period[b] * f / 4);
            int lo = Math.Max(1, (int)Math.Floor(centre - radius)), hi = Math.Min(d.Length - 2, (int)Math.Ceiling(centre + radius));
            int peak = lo;
            for (int j = lo; j <= hi; j++) if (d[j] > d[peak]) peak = j;
            bool supported = d[peak] > 0 && d[peak] >= d[peak - 1] && d[peak] >= d[peak + 1];
            double at = supported ? Interpolate(d, peak) : centre;
            beats.Add(new TrackedBeat(at - PeakToOsuMs, supported ? d[peak] : 0));
        }
        return beats;
    }

    /// <summary>
    /// Outside beats (audio ms) onto the envelope: each moves to the strongest peak within <see
    /// cref="MultiBpmOptions.RefineMs"/> or a quarter of its shorter neighbouring interval, as <see cref="TrackBeats"/>
    /// refines its own.
    /// </summary>
    public static List<TrackedBeat> RefineBeats(double[] d, IReadOnlyList<double> audioMs, MultiBpmOptions o)
    {
        var times = audioMs.Order().ToArray();
        var beats = new List<TrackedBeat>(times.Length);
        for (int k = 0; k < times.Length; k++)
        {
            double gap = Math.Min(k > 0 ? times[k] - times[k - 1] : double.PositiveInfinity, k + 1 < times.Length ? times[k + 1] - times[k] : double.PositiveInfinity);
            double centre = times[k] + o.ExternalLagMs;
            double radius = Math.Min(o.RefineMs, double.IsInfinity(gap) ? o.RefineMs : gap / 4);
            int lo = Math.Max(1, (int)Math.Floor(centre - radius)), hi = Math.Min(d.Length - 2, (int)Math.Ceiling(centre + radius));
            if (lo > hi) continue;
            int peak = lo;
            for (int j = lo; j <= hi; j++) if (d[j] > d[peak]) peak = j;
            bool supported = d[peak] > 0 && d[peak] >= d[peak - 1] && d[peak] >= d[peak + 1];
            double at = supported ? Interpolate(d, peak) : centre;
            beats.Add(new TrackedBeat(at - PeakToOsuMs, supported ? d[peak] : 0));
        }
        return beats;
    }

    /// <summary>Beats (ms) brought to a beat period (<see cref="MultiBpmOptions.ExternalToOctave"/>).</summary>
    public static List<double> ToBeatLevel(IReadOnlyList<double> times, double period)
    {
        var sorted = times.Order().ToArray();
        var level = new List<double>(sorted.Length * 2);
        foreach (double t in sorted)
        {
            if (level.Count == 0) { level.Add(t); continue; }
            double last = level[^1], gap = t - last;
            if (gap < 0.75 * period) continue;
            int n = Math.Max(1, (int)Math.Round(gap / period));
            for (int k = 1; k < n; k++) level.Add(last + gap * k / n);
            level.Add(t);
        }
        return level;
    }

    /// <summary>Ellis (2007) dynamic-programming beat tracker with a time-varying target period (frames).</summary>
    private static int[] BeatDp(double[] onset, double[] period, double tightness)
    {
        int n = onset.Length;
        var score = new double[n];
        var back = new int[n];
        for (int t = 0; t < n; t++)
        {
            double tau = period[t], best = 0;
            int arg = -1;
            int pMin = Math.Max(1, (int)Math.Round(0.7 * tau)), pMax = (int)Math.Round(1.4 * tau);
            for (int p = pMin; p <= pMax && t - p >= 0; p++)
            {
                double ratio = Math.Log(p / tau);
                double v = score[t - p] - tightness * ratio * ratio;
                if (v > best) { best = v; arg = t - p; }
            }
            score[t] = onset[t] + best;
            back[t] = arg;
        }
        int last = (int)Math.Round(period[n - 1]);
        int end = n - 1;
        for (int t = Math.Max(0, n - 1 - last); t < n; t++) if (score[t] > score[end]) end = t;
        var beats = new List<int>();
        for (int t = end; t >= 0; t = back[t]) beats.Add(t);
        beats.Reverse();
        return beats.ToArray();
    }

    /// <summary>
    /// Beat tracking over (time, inter-beat interval) states: a beat scores its onset strength plus its best
    /// predecessor, minus a cost on the interval changing from the previous one (so a ramp is a path of small steps,
    /// not a jump the tempogram must foresee) and a weak cost on straying from the tempogram's local period (keeps the
    /// metre, so off-beats and double tempo do not pay). A change beyond <see cref="MultiBpmOptions.MaxStepChange"/>
    /// costs a flat <see cref="MultiBpmOptions.JumpCost"/>, which lets a song jump to a new section tempo.
    /// </summary>
    private static int[] JointBeatDp(double[] onset, double[] period, MultiBpmOptions o)
    {
        int n = onset.Length;
        double low = double.PositiveInfinity, high = 0;
        foreach (double p in period) { low = Math.Min(low, p); high = Math.Max(high, p); }
        int pMin = Math.Max(2, (int)Math.Floor(low / o.JointRange)), pMax = Math.Max(pMin + 1, (int)Math.Ceiling(high * o.JointRange));
        int states = pMax - pMin + 1;
        var cost = new double[states, states];
        for (int s = 0; s < states; s++)
            for (int q = 0; q < states; q++)
            {
                double r = Math.Log((pMin + s) / (double)(pMin + q));
                cost[s, q] = Math.Abs(r) <= Math.Log(1 + o.MaxStepChange) ? o.TempoContinuity * r * r : double.PositiveInfinity;
            }
        var score = new float[(long)n * states];
        var back = new short[(long)n * states]; // Interval state of the previous beat; -1 = the chain starts here.
        var rowMax = new double[n];
        var rowArg = new short[n];
        for (int t = 0; t < n; t++)
        {
            double tau = period[t], best = double.NegativeInfinity;
            int arg = -1;
            for (int s = 0; s < states; s++)
            {
                int p = pMin + s, prev = t - p;
                double r = Math.Log(p / tau), value = 0; // Starting a chain here is free.
                short from = -1;
                if (prev >= 0)
                {
                    long row = (long)prev * states;
                    if (rowMax[prev] - o.JumpCost > value) { value = rowMax[prev] - o.JumpCost; from = rowArg[prev]; }
                    int qLo = Math.Max(0, (int)Math.Floor(p / (1 + o.MaxStepChange)) - pMin), qHi = Math.Min(states - 1, (int)Math.Ceiling(p * (1 + o.MaxStepChange)) - pMin);
                    for (int q = qLo; q <= qHi; q++)
                    {
                        double v = score[row + q] - cost[s, q];
                        if (v > value) { value = v; from = (short)q; }
                    }
                }
                double total = onset[t] + value - o.TempoPrior * r * r;
                score[(long)t * states + s] = (float)total;
                back[(long)t * states + s] = from;
                if (total > best) { best = total; arg = s; }
            }
            rowMax[t] = best;
            rowArg[t] = (short)arg;
        }
        // End on the best beat within one longest interval of the end, then walk back.
        int end = n - 1;
        for (int t = Math.Max(0, n - 1 - pMax); t < n; t++) if (rowMax[t] > rowMax[end]) end = t;
        var beats = new List<int>();
        int state = rowArg[end];
        for (int t = end; t >= 0 && state >= 0;)
        {
            beats.Add(t);
            int previousState = back[(long)t * states + state];
            t -= pMin + state;
            state = previousState;
        }
        beats.Reverse();
        return beats.ToArray();
    }

    internal static double Interpolate(double[] y, int i)
    {
        double den = (y[i] - y[i + 1]) + (y[i] - y[i - 1]);
        return den > 0 ? i + 0.5 * (y[i + 1] - y[i - 1]) / den : i;
    }
}

/// <summary>Local beat period per frame: windowed autocorrelation (tempogram) and a Viterbi path through it.</summary>
internal static class TempoTracker
{
    public static double[] Track(double[] onset, double? globalPeriod, MultiBpmOptions o)
    {
        int n = onset.Length, f = o.FrameMs;
        int win = Math.Max(4, (int)(o.WindowMs / f)), hop = Math.Max(1, (int)(o.HopMs / f));
        int lMin = Math.Max(2, (int)Math.Floor(60000 / o.MaxBpm / f)), lMax = (int)Math.Ceiling(60000 / o.MinBpm / f);
        int windows = Math.Max(1, (n + hop - 1) / hop);
        var acf = new double[windows][];
        for (int w = 0; w < windows; w++)
        {
            int b = Math.Min(n, w * hop + win / 2), a = Math.Max(0, b - win);
            b = Math.Min(n, a + win);
            acf[w] = Autocorrelation(onset, a, b, lMin, lMax);
        }
        double anchor = globalPeriod ?? GlobalPeriod(acf, lMin, f);
        int sMin = Math.Max(lMin, (int)Math.Floor(anchor / o.OctaveRange)), sMax = Math.Min(lMax, (int)Math.Ceiling(anchor * o.OctaveRange));
        if (sMax < sMin) (sMin, sMax) = (lMin, lMax);
        int states = sMax - sMin + 1;

        var score = new double[states];
        var back = new int[windows, states];
        var next = new double[states];
        for (int s = 0; s < states; s++) score[s] = Emission(acf[0], sMin + s - lMin);
        for (int w = 1; w < windows; w++)
        {
            for (int s = 0; s < states; s++)
            {
                double best = double.NegativeInfinity; int arg = 0;
                for (int q = 0; q < states; q++)
                {
                    double r = Math.Log((sMin + s) / (double)(sMin + q));
                    double v = score[q] - o.TempoChangeCost * r * r;
                    if (v > best) { best = v; arg = q; }
                }
                next[s] = best + Emission(acf[w], sMin + s - lMin);
                back[w, s] = arg;
            }
            (score, next) = (next, score);
        }
        var path = new int[windows];
        path[^1] = Array.IndexOf(score, score.Max());
        for (int w = windows - 1; w > 0; w--) path[w - 1] = back[w, path[w]];

        // Per frame: interpolate the window periods (window w is centred at frame w·hop), refined by parabolic ACF peak.
        var centres = new double[windows];
        for (int w = 0; w < windows; w++)
        {
            int lag = sMin + path[w] - lMin;
            var a = acf[w];
            double refined = sMin + path[w];
            if (lag > 0 && lag < a.Length - 1 && a[lag] >= a[lag - 1] && a[lag] >= a[lag + 1])
            {
                double den = (a[lag] - a[lag + 1]) + (a[lag] - a[lag - 1]);
                if (den > 0) refined += 0.5 * (a[lag + 1] - a[lag - 1]) / den;
            }
            centres[w] = refined;
        }
        var period = new double[n];
        for (int t = 0; t < n; t++)
        {
            double pos = (double)t / hop;
            int w = Math.Min(windows - 1, (int)Math.Floor(pos));
            double frac = pos - w;
            period[t] = w + 1 < windows ? centres[w] * (1 - frac) + centres[w + 1] * frac : centres[w];
        }
        return period;
    }

    private static double Emission(double[] acf, int index)
    {
        double max = 0;
        foreach (double v in acf) max = Math.Max(max, v);
        return max > 0 ? Math.Max(0, acf[index]) / max : 0;
    }

    private static double[] Autocorrelation(double[] x, int a, int b, int lMin, int lMax)
    {
        double mean = 0;
        for (int i = a; i < b; i++) mean += x[i];
        mean /= Math.Max(1, b - a);
        var r = new double[lMax - lMin + 1];
        for (int l = lMin; l <= lMax; l++)
        {
            double s = 0; int count = 0;
            for (int i = a; i + l < b; i++) { s += (x[i] - mean) * (x[i + l] - mean); count++; }
            r[l - lMin] = count > 0 ? s / count : 0;
        }
        return r;
    }

    /// <summary>Fallback octave anchor when the original detector finds nothing: mean tempogram, log-normal prior at 160 BPM.</summary>
    private static double GlobalPeriod(double[][] acf, int lMin, int frameMs)
    {
        int len = acf[0].Length;
        double best = double.NegativeInfinity; int arg = 0;
        for (int i = 0; i < len; i++)
        {
            double mean = acf.Average(a => a[i]);
            double octaves = Math.Log2((lMin + i) * frameMs / 375.0);
            double v = mean * Math.Exp(-0.5 * octaves * octaves / 0.25);
            if (v > best) { best = v; arg = i; }
        }
        return lMin + arg;
    }
}

