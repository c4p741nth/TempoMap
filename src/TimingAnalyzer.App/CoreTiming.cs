using TimingAnalyzer.Core;
using TimingAnalyzer.Editor;

namespace TimingAnalyzer.App;

/// <summary>
/// Maps editor timing requests onto the parity-locked Core. The onset envelope (1 sample per ms) is computed once
/// per song; a section analyzes a slice of it, so sections have no filter start-up transient. The whole song runs
/// exactly what <see cref="Analyzer.Analyze"/> runs, without its 10-minute cap: the original exe's FFT table held
/// 2²⁰ points, while Core's <see cref="Fft"/> sizes itself to the song.
/// </summary>
public sealed class CoreTiming
{
    private AudioContent? source;
    private double[] envelope = [];
    private double[]? rise;
    private double? lag;
    private (float[] Beat, float[] Downbeat)? heads;

    /// <summary>
    /// Lines on the waveform's leading edge instead of the original detector's onset-peak convention (<see cref="EdgeAlign"/>,
    /// docs/PHASE1-STATUS.md E43): every time Auto-time and Snap to onsets produce is later by <see cref="LagMs"/>. The
    /// original detector's own numbers (the CLI, the parity tests, the evaluation harness) are not touched.
    /// </summary>
    public bool AlignToEdge { get; set; } = true;
    /// <summary>Auto-time's first red line goes to the first bar's first beat when the beat model can tell (<see cref="BarStart"/>).</summary>
    public bool StartOnBar { get; set; } = true;
    /// <summary>
    /// A one-tempo answer of at least <see cref="HalveFromBpm"/> is halved when the beat model hears a beat only on every
    /// second beat of it (<see cref="BeatPhase.AlternateBeatRatio"/> at most <see cref="HalveRatio"/>): the original detector's
    /// octave is the louder 8th notes there. Evaluation (docs/PHASE1-STATUS.md E44): fires on Rebellious exorcist, Dusk and
    /// Ragnarok, all three at half the answer in the map, and on none of the 75 other one-tempo answers.
    /// </summary>
    public bool HalveByModel { get; set; } = true;
    public const double HalveFromBpm = 200, HalveRatio = 0.2, HintRatio = 0.6, HintFromBpm = 240;
    /// <summary>
    /// Sections of 24 beats or more that the analyzer's fold put half a beat or so off move to the phase the beat model's beat
    /// head folds highest at their BPM (<see cref="BeatPhase.AlignSections"/>; docs/PHASE1-STATUS.md E44).
    /// </summary>
    public bool PhaseByModel { get; set; } = true;

    private void Load(AudioContent audio)
    {
        if (ReferenceEquals(audio, source)) return;
        envelope = OnsetEnvelope.Compute(audio.Mono, audio.Rate);
        source = audio; rise = null; lag = null; heads = null;
    }

    /// <summary>The beat model's beat and downbeat activations for this song, run once; null without the model file.</summary>
    private (float[] Beat, float[] Downbeat)? Heads(AudioContent audio)
    {
        Load(audio);
        if (heads is null && Model() is { } model) heads = model.Activations(audio.Mono, audio.Rate);
        return heads;
    }

    /// <summary>
    /// The first beat at or after <paramref name="from"/> of the grid at <paramref name="bpm"/> whose phase the beat model
    /// puts highest over [from, to) (<see cref="BeatPhase"/>), or <paramref name="fallback"/> without the model or with too
    /// few beats. Better than the original's fold of the onset function: within 25 ms of the map's grid in 94 % of 606
    /// reference sections against 80 %.
    /// </summary>
    private double? ModelPhase(AudioContent audio, double bpm, double from, double to, double? fallback) =>
        Heads(audio) is { } h && BeatPhase.FirstBeat(h.Beat, 60000 / bpm, from, to) is { } phase ? phase : fallback;

    /// <summary>
    /// The ms Auto-time adds to the detector's times for this song: how much later than the onset peak's convention the
    /// hits' leading edges sit (measured on the strongest hits, 18 ms when the song has too few), 0 with
    /// <see cref="AlignToEdge"/> off.
    /// </summary>
    public double LagMs(AudioContent audio) => AlignToEdge ? MeasuredLagMs(audio) : 0;

    /// <summary>The song's edge lag whatever the setting is: Check sections judges lines from either convention.</summary>
    private double MeasuredLagMs(AudioContent audio)
    {
        Load(audio);
        return lag ??= EdgeAlign.Clamp(EdgeAlign.SongLag(envelope, rise ??= EdgeAlign.LogRise(audio.Mono, audio.Rate)));
    }

    private static TimingPoint Shifted(TimingSection s, double lagMs) => new(s.TimeMs + lagMs, 60000 / s.Bpm);

    private static string EdgeNote(double lagMs) => lagMs >= 1
        ? " Lines sit " + lagMs.ToString("0", System.Globalization.CultureInfo.InvariantCulture) +
          " ms after the onset peak, on the waveform's leading edge (Settings turns this off)."
        : "";

    public TimingDetection Detect(AudioContent audio, TimingRequest request)
    {
        Load(audio);
        bool whole = request.StartMs <= 0 && request.EndMs >= audio.DurationMs;
        int a = whole ? 0 : Math.Clamp((int)Math.Floor(request.StartMs), 0, envelope.Length);
        int b = whole ? envelope.Length : Math.Clamp((int)Math.Ceiling(request.EndMs), a, envelope.Length);
        var result = Analyzer.AnalyzeEnvelope(whole ? envelope : envelope[a..b], request.Bpm ?? 0);
        // Core offsets are relative to the analyzed slice. With a BPM (locked, or detected exactly) the beat model gives the phase.
        double? offset = result.Offset + a;
        if (offset is not null && result.Bpm is { } bpm) offset = ModelPhase(audio, bpm, a, whole ? audio.DurationMs : b, offset);
        return new(result.Bpm, offset + (offset is null ? 0 : LagMs(audio)), result.ApproximateBpm, result.Status, result.Error);
    }

    /// <summary>Whole-song multi-BPM timing (docs/PHASE2-PLAN.md M1-M2), reusing the cached onset envelope.</summary>
    public MultiTimingDetection DetectMulti(AudioContent audio)
    {
        Load(audio);
        var (sections, beats) = MultiBpmAnalyzer.AnalyzeWithBeats(envelope);
        double lagMs = LagMs(audio);
        float[]? downbeat = null;
        // The beat model's red lines replace the analyzer's when the per-song rule prefers them (docs/PHASE4-STATUS.md
        // N4: no evaluation set worse, 7 of 10 better). Without the model file the analyzer's answer stands.
        if (Heads(audio) is { } activations)
        {
            var (beat, down) = activations;
            downbeat = down;
            var modelLines = BeatModelTiming.Lines(BeatModelTiming.Beats(beat));
            if (BeatModelTiming.PreferModel(envelope, OsuSections(sections), OsuSections(modelLines), audio.DurationMs))
                return new(modelLines.Select(s => Shifted(s, lagMs)).ToList(), null, Analyzer.AnalyzeEnvelope(envelope).Status,
                    null, BeatModelUsed: true, Note: EdgeNote(lagMs));
        }
        if (sections.Count == 0) return new([], "cannot determine timing");
        var lines = sections.ToList();
        string barNote = "";
        if (Heads(audio) is { } model2)
        {
            // One high tempo the beat model hears at every second beat: the map's BPM is half of it.
            if (lines.Count == 1 && lines[0].Bpm >= HalveFromBpm
                && BeatPhase.AlternateBeatRatio(model2.Beat, 60000 / lines[0].Bpm, lines[0].TimeMs, audio.DurationMs) is { } alternate)
            {
                if (HalveByModel && alternate <= HalveRatio)
                {
                    double before = lines[0].Bpm, length = 120000 / before;
                    lines[0] = new TimingSection(BeatPhase.FirstBeat(model2.Beat, length, lines[0].TimeMs, audio.DurationMs) ?? lines[0].TimeMs, before / 2);
                    barNote += " Halved from " + before.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) +
                        " BPM: the beat model hears a beat only on every second beat of that tempo (Timing → Tempo of selected lines → ×2 doubles it back).";
                }
                else if (alternate < HintRatio && lines[0].Bpm >= HintFromBpm)
                    barNote += " The beat model hears the beat mostly on every second beat of " +
                        lines[0].Bpm.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + " BPM (alternate beat " +
                        alternate.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + " of the strongest): the map may be at half of it; ÷2, then Fit offset.";
            }
            if (PhaseByModel) lines = BeatPhase.AlignSections(lines, model2.Beat, audio.DurationMs).ToList();
        }
        // The first red line on a bar's first beat, by the beat model's downbeat head; the later sections keep their times.
        if (StartOnBar && downbeat is not null)
        {
            int moved = BarStart.BeatsToBarStart(envelope, downbeat, lines[0], lines.Count > 1 ? lines[1].TimeMs : audio.DurationMs);
            if (moved != 0)
            {
                lines[0] = BarStart.Move(lines[0], moved);
                barNote += " The first red line moved " + moved.ToString("+0;-0", System.Globalization.CultureInfo.InvariantCulture) +
                    (Math.Abs(moved) == 1 ? " beat" : " beats") + " to start a bar (the beat model's bar starts).";
            }
        }
        var wobble = DrumWobble.Measure(audio.Mono, audio.Rate, beats);
        return new(lines.Select(s => Shifted(s, lagMs)).ToList(), null, Analyzer.AnalyzeEnvelope(envelope).Status,
            wobble is { SuggestsLiveDrums: true } ? new LiveDrumHint(wobble.MedianMs, wobble.Triples) : null, Note: barNote + EdgeNote(lagMs));
    }

    /// <summary>Beat model mode: the model's red lines for the whole song; an error when the model file is missing.</summary>
    public MultiTimingDetection DetectBeatModel(AudioContent audio)
    {
        if (Model() is not { } model) return new([], "the beat model file (Models\\beatnet.onnx) is missing beside the exe");
        var lines = BeatModelTiming.Lines(BeatModelTiming.Beats(model.BeatProbability(audio.Mono, audio.Rate)));
        double lagMs = LagMs(audio);
        return lines.Count == 0 ? new([], "the beat model found no beats") :
            new(lines.Select(s => Shifted(s, lagMs)).ToList(), BeatModelUsed: true, Note: EdgeNote(lagMs));
    }

    private BeatModel? beatModel;
    private bool modelTried;
    private BeatModel? Model()
    {
        if (!modelTried) { beatModel = BeatModel.TryLoad(); modelTried = true; }
        return beatModel;
    }
    // The selector's thresholds were tuned on red lines at osu! precision.
    private static IReadOnlyList<TimingSection> OsuSections(IReadOnlyList<TimingSection> s) =>
        s.Select(x => new TimingSection(OsuPrecision.Time(x.TimeMs), OsuPrecision.Bpm(x.Bpm))).ToList();

    /// <summary>Drift mode: one red line every <paramref name="beatsPerLine"/> tracked beats on a continuous grid.</summary>
    public MultiTimingDetection DetectDrift(AudioContent audio, int beatsPerLine)
    {
        Load(audio);
        var lines = DriftLines.Fit(envelope, beatsPerLine);
        if (lines.Count == 0) return new([], "cannot track beats");
        double lagMs = LagMs(audio);
        return new(lines.Select(s => Shifted(s, lagMs)).ToList(), Note: EdgeNote(lagMs));
    }

    /// <summary>
    /// Auto-time range: the multi-BPM analyzer on the range (<see cref="RangeTiming"/>), or one red line at the BPM box's
    /// BPM with its offset fitted on the range, or drift lines over the range.
    /// </summary>
    public MultiTimingDetection DetectRange(AudioContent audio, RangeRequest request)
    {
        Load(audio);
        int a = Math.Clamp((int)Math.Floor(request.StartMs), 0, envelope.Length);
        int b = Math.Clamp((int)Math.Ceiling(request.EndMs), a, envelope.Length);
        if (b - a < RangeTiming.MinimumMs) return new([], "the range is shorter than 4 s");
        double lagMs = LagMs(audio);
        if (request.Bpm is { } bpm)
        {
            var single = Analyzer.AnalyzeEnvelope(envelope[a..b], bpm);
            return single.Offset is { } offset ? new([new TimingPoint(ModelPhase(audio, bpm, a, b, offset + a)!.Value + lagMs, 60000 / bpm)])
                : new([], single.Error ?? "no beat found in the range");
        }
        IReadOnlyList<TimingSection> lines = request.DriftBeats > 0
            ? DriftLines.Fit(envelope[a..b], request.DriftBeats).Select(s => s with { TimeMs = s.TimeMs + a }).ToList()
            : request.BeatModel && Model() is { } model ? ModelRange(model, audio, a, b)
            : RangeTiming.Analyze(envelope, a, b);
        return lines.Count == 0 ? new([], "cannot determine timing in the range")
            : new(lines.Select(s => Shifted(s, lagMs)).ToList());
    }

    // The model runs on the range plus 10 s each side (its context), then keeps the red lines that start inside.
    private static IReadOnlyList<TimingSection> ModelRange(BeatModel model, AudioContent audio, int a, int b)
    {
        int from = Math.Max(0, a - 10000), to = Math.Min((int)audio.DurationMs, b + 10000);
        int s0 = (int)((long)from * audio.Rate / 1000), s1 = (int)Math.Min(audio.Mono.Length, (long)to * audio.Rate / 1000);
        var beats = BeatModelTiming.Beats(model.BeatProbability(audio.Mono[s0..s1], audio.Rate)).Select(t => t + from).Where(t => t >= a && t < b).ToList();
        return BeatModelTiming.Lines(beats);
    }

    /// <summary>Check sections: Core <see cref="SectionCheck"/> on the current red lines, one mark per line or null.</summary>
    public IReadOnlyList<SectionMark?> CheckSections(AudioContent audio, IReadOnlyList<TimingPoint> reds)
    {
        Load(audio);
        // The check judges a grid against the onset peaks, so a line on the leading edge (Auto-time with the edge setting)
        // is judged 'lag' ms earlier; each line is judged in whichever convention its beats fit better, so a map from
        // elsewhere (or the user's own lines, with the setting off) is not marked for sitting on the other one. The
        // user's three recreate.ogg lines, which sit on the edge, were weak at 437 and 55 108 judged on the peaks alone.
        double lagMs = MeasuredLagMs(audio);
        var plain = SectionCheck.Check(envelope, reds.Select(p => new TimingSection(p.Time, p.Bpm)).ToList(), audio.DurationMs);
        var early = lagMs >= 1 ? SectionCheck.Check(envelope, reds.Select(p => new TimingSection(p.Time - lagMs, p.Bpm)).ToList(), audio.DurationMs) : plain;
        var onEdge = plain.Select((r, i) => lagMs >= 1 && early[i] is { } e && (r is null || e.Fit > r.Fit)).ToList();
        var reports = plain.Select((r, i) => onEdge[i] ? early[i] : r).ToList();
        return reports.Select((r, i) =>
        {
            double end = i + 1 < reds.Count ? reds[i + 1].Time : audio.DurationMs;
            double map = OctaveSelector.Ratio(envelope, reds[i].Bpm, reds[i].Time - (onEdge[i] ? lagMs : 0), end, OctaveSelector.HintMargin);
            double? mapRatio = map == 1 ? null : map;
            return r is { } x && (x.Weak || x.ShiftMs is not null || x.BestRatio is not null) || mapRatio is not null
                ? new SectionMark(i, r?.Weak ?? false, r?.ShiftMs, r?.BestRatio, mapRatio) : null;
        }).ToList();
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
        Load(audio);
        var d = envelope;
        // Lines are on the leading edge (AlignToEdge): search around the onset peak that edge belongs to, answer on the edge.
        double lagMs = LagMs(audio);
        return queries.Select(q =>
        {
            double centre = q.TimeMs - lagMs + MultiBpmAnalyzer.PeakToOsuMs;
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
            return at - MultiBpmAnalyzer.PeakToOsuMs + lagMs;
        }).ToList();
    }
}
