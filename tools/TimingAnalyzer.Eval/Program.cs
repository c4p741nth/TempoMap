// timing-eval: evaluation harness for multi-BPM auto-timing (docs/PHASE2-PLAN.md).
//   timing-eval select <songsDir> <out.tsv> [multiCount] [singleCount]
//   timing-eval select-mania <songsDir> <out.tsv>
//   timing-eval run <songs.tsv> [algorithm] [set=A|B|M|C|D] [split=tune|hold]
//   timing-eval edge-lag|phase|bar-start|octave-beat|normal-variants|auto-time <songs.tsv>...   (docs/PHASE1-STATUS.md E43-E44)
using System.Collections.Concurrent;
using System.Globalization;
using TimingAnalyzer.Editor;
using TimingAnalyzer.Eval;

var inv = CultureInfo.InvariantCulture;
CultureInfo.DefaultThreadCurrentCulture = inv;
CultureInfo.CurrentCulture = inv;
string repo = FindRepo();
string cache = Path.Combine(repo, ".eval-cache");
const double RampSectionMs = 6000; // A reference section shorter than this counts as a ramp step (docs/PHASE2-STATUS.md M4).
// Option overrides for "multi": key=value after the algorithm (penalty, tight, change, window, hop, octave,
// refine, minbeats, outlier, snap, merge, ...; joint tracker: joint=1, cont, prior, step, jump, jrange).
var overrides = args.Skip(args.Length > 0 && args[0] == "show" ? 4 : 3).Where(a => a.Contains('=') && !a.StartsWith("set=", StringComparison.Ordinal) && !a.StartsWith("split=", StringComparison.Ordinal))
    .Select(a => a.Split('=', 2)).ToDictionary(p => p[0], p => double.Parse(p[1], inv), StringComparer.OrdinalIgnoreCase);
double Opt(string key, double fallback) => overrides.TryGetValue(key, out double v) ? v : fallback;
var defaults = new TimingAnalyzer.Core.MultiBpmOptions();
var multiOptions = defaults with
{
    SectionPenalty = Opt("penalty", defaults.SectionPenalty), BeatTightness = Opt("tight", defaults.BeatTightness),
    TempoChangeCost = Opt("change", defaults.TempoChangeCost), WindowMs = Opt("window", defaults.WindowMs),
    HopMs = Opt("hop", defaults.HopMs), OctaveRange = Opt("octave", defaults.OctaveRange), RefineMs = Opt("refine", defaults.RefineMs),
    MinSectionBeats = (int)Opt("minbeats", defaults.MinSectionBeats), IntervalOutlier = Opt("outlier", defaults.IntervalOutlier),
    SnapDriftMs = Opt("snap", defaults.SnapDriftMs), MergeMs = Opt("merge", defaults.MergeMs),
    SupportRatio = Opt("support", defaults.SupportRatio), LongSectionMs = Opt("long", defaults.LongSectionMs),
    FoldSearch = Opt("fold", defaults.FoldSearch), ContinuityShare = Opt("continuity", defaults.ContinuityShare),
    WeakSupport = Opt("weak", defaults.WeakSupport), InheritRatio = Opt("inherit", defaults.InheritRatio),
    JointTracker = Opt("joint", defaults.JointTracker ? 1 : 0) != 0, TempoContinuity = Opt("cont", defaults.TempoContinuity),
    TempoPrior = Opt("prior", defaults.TempoPrior), MaxStepChange = Opt("step", defaults.MaxStepChange),
    JumpCost = Opt("jump", defaults.JumpCost), JointRange = Opt("jrange", defaults.JointRange),
    FreeTempo = Opt("free", defaults.FreeTempo ? 1 : 0) != 0, DriftMs = Opt("drift", defaults.DriftMs),
    DriftBeats = (int)Opt("dbeats", defaults.DriftBeats), DriftMaxChange = Opt("dchange", defaults.DriftMaxChange),
    DriftFoldRatio = Opt("dfold", defaults.DriftFoldRatio), DriftContinuity = Opt("dcont", defaults.DriftContinuity),
    Ramps = Opt("ramps", defaults.Ramps ? 1 : 0) != 0, RampMinChange = Opt("rchange", defaults.RampMinChange),
    RampGoodRatio = Opt("rgood", defaults.RampGoodRatio), RampMinGapBeats = Opt("rgap", defaults.RampMinGapBeats),
    RampMaxMs = Opt("rmax", defaults.RampMaxMs), RampSlack = Opt("rslack", defaults.RampSlack), RampLandMs = Opt("rland", defaults.RampLandMs),
    RampStepPenalty = Opt("rpen", defaults.RampStepPenalty), RampMargin = Opt("rmargin", defaults.RampMargin),
    RampLinePenalty = Opt("rline", defaults.RampLinePenalty), WholeBpmDriftMs = Opt("whole", defaults.WholeBpmDriftMs),
    HalfTempoMaxBpm = Opt("hmax", defaults.HalfTempoMaxBpm), HalfTempoRatio = Opt("hratio", defaults.HalfTempoRatio),
    ThirdBeatMaxBpm = Opt("tmax", defaults.ThirdBeatMaxBpm), ThirdBeatRatio = Opt("tratio", defaults.ThirdBeatRatio),
    TripletRatio = Opt("trip", defaults.TripletRatio), TripletMinBpm = Opt("tripmin", defaults.TripletMinBpm),
    // rshort=2 allows 2-beat ramp steps, rshort=1 also 1-beat ones (default: the library's list).
    RampStepBeats = Opt("rshort", 0) switch { 2 => [2, 4, 6, 8, 12, 16], 1 => [1, 2, 3, 4, 6, 8, 12, 16], _ => defaults.RampStepBeats },
};
// ext=1: beats from an outside tracker (tools/beat_probe.py, .eval-cache/<key>.beats) instead of our own; lag=ms.
bool external = Opt("ext", 0) != 0;
IReadOnlyList<TimingPoint> RunAlgorithm(ITimingAlgorithm algorithm, EvalSong song, double[] envelope, double duration) =>
    algorithm is ModelOctaveAlgorithm ? new ModelOctaveAlgorithm(multiOptions, () => EnvelopeCache.ExternalBeats(song.Audio, cache)).Run(envelope, duration)
    : algorithm is ModelOnlyAlgorithm ? new ModelOnlyAlgorithm(() => EnvelopeCache.ExternalBeats(song.Audio, cache)).Run(envelope, duration)
    : algorithm is SelectedAlgorithm ? new SelectedAlgorithm(multiOptions, () => EnvelopeCache.ExternalBeats(song.Audio, cache)).Run(envelope, duration)
    : algorithm is HybridAlgorithm ? new HybridAlgorithm(multiOptions, () => EnvelopeCache.ExternalBeats(song.Audio, cache), Opt("steady", 8000)).Run(envelope, duration)
    : !external ? algorithm.Run(envelope, duration)
    : new MultiAlgorithm(multiOptions with
    {
        ExternalBeats = EnvelopeCache.ExternalBeats(song.Audio, cache) ?? throw new FileNotFoundException("no outside beats for " + song.Name),
        ExternalLagMs = Opt("lag", 0), ExternalToOctave = Opt("extoct", 0) != 0,
    }, algorithm.Name).Run(envelope, duration);
var algorithms = new Dictionary<string, Func<ITimingAlgorithm>>(StringComparer.OrdinalIgnoreCase)
{
    ["baseline"] = () => new BaselineAlgorithm(),
    ["unmerged"] = () => new UnmergedAlgorithm(multiOptions),
    ["tracked"] = () => new TrackedAlgorithm(multiOptions),
    ["grouped1"] = () => new GroupedAlgorithm(multiOptions, 1),
    ["grouped2"] = () => new GroupedAlgorithm(multiOptions, 2),
    ["grouped4"] = () => new GroupedAlgorithm(multiOptions, 4),
    ["grouped8"] = () => new GroupedAlgorithm(multiOptions, 8),
    ["drift1"] = () => new DriftAlgorithm(multiOptions, 1),
    ["drift4"] = () => new DriftAlgorithm(multiOptions, 4), ["drift8"] = () => new DriftAlgorithm(multiOptions, 8),
    ["drift16"] = () => new DriftAlgorithm(multiOptions, 16), ["drift32"] = () => new DriftAlgorithm(multiOptions, 32),
    ["multi"] = () => new MultiAlgorithm(multiOptions, "multi " + string.Join(" ", overrides.Select(p => p.Key + "=" + p.Value.ToString(inv)))),
    ["octave"] = () => new OctaveAlgorithm(multiOptions, Opt("omargin", TimingAnalyzer.Core.OctaveSelector.Margin)),
    ["modeloct"] = () => new ModelOctaveAlgorithm(multiOptions, () => null),
    ["modelonly"] = () => new ModelOnlyAlgorithm(() => null),
    ["selected"] = () => new SelectedAlgorithm(multiOptions, () => null),
    ["hybrid"] = () => new HybridAlgorithm(multiOptions, () => null, Opt("steady", 8000)),
};

if (args.Length >= 3 && args[0] == "select")
{
    int multi = args.Length > 3 ? int.Parse(args[3], inv) : 40, single = args.Length > 4 ? int.Parse(args[4], inv) : 30;
    // MAMBO COMBO (set M) was dropped on 2026-10-18 at the user's request: its reference map was re-saved in osu! from
    // our own output, so the original timing is gone and it no longer checks anything independent.
    var extra = Array.Empty<EvalSong>();
    var songs = SongSelection.Select(args[1], multi, single, extra);
    SongSelection.Write(args[2], songs);
    foreach (var g in songs.GroupBy(s => s.Set)) Console.WriteLine($"set {g.Key}: {g.Count()} songs ({g.Count(s => s.Split == "tune")} tune / {g.Count(s => s.Split == "hold")} hold)");
    return 0;
}
if (args.Length >= 3 && args[0] == "select-mania")
{
    // timing-eval select-mania <songsDir> <out.tsv>: Set C (osu!mania tournament / Camellia / Ludicin, multi-BPM) and
    // Set D (every other plausible multi-BPM mania set), split tune/hold by song.
    var songs = SongSelection.SelectMania(args[1]);
    SongSelection.Write(args[2], songs);
    foreach (var g in songs.GroupBy(s => s.Set)) Console.WriteLine($"set {g.Key}: {g.Count()} sets ({g.Count(s => s.Split == "tune")} tune / {g.Count(s => s.Split == "hold")} hold)");
    return 0;
}
if (args.Length >= 2 && args[0] == "audio-keys")
{
    // timing-eval audio-keys <songs.tsv>...: cache key and audio path of every song, for tools/beat_probe.py.
    foreach (string audio in args.Skip(1).Where(a => !a.Contains('=')).SelectMany(SongSelection.Read).Select(s => s.Audio).Distinct(StringComparer.OrdinalIgnoreCase))
        Console.WriteLine(EnvelopeCache.Key(audio) + "	" + audio);
    return 0;
}
if (args.Length >= 3 && args[0] == "dump-set")
{
    // timing-eval dump-set <songs.tsv> <outDir>: for the beat model (docs/PHASE4-PLAN.md): per song, our decoder's mono
    // samples resampled to 22 050 Hz (float32 .pcm) and the map's red lines and last note (.json), named by the
    // envelope cache key, so the model sees the audio the editor sees. Songs already dumped are skipped.
    var songs = SongSelection.Read(args[1]);
    Directory.CreateDirectory(args[2]);
    int done = 0, skipped = 0, failed = 0;
    Parallel.ForEach(songs, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2) }, song =>
    {
        try
        {
            string key = EnvelopeCache.Key(song.Audio), pcm = Path.Combine(args[2], key + ".pcm"), meta = Path.Combine(args[2], key + ".json");
            if (File.Exists(pcm) && File.Exists(meta)) { Interlocked.Increment(ref skipped); return; }
            var doc = OsuFile.Load(song.Reference);
            var reds = BeatGrid.Sort(doc.RedLines);
            var decoded = TimingAnalyzer.Audio.AudioDecoder.Read(song.Audio);
            if (decoded.Mono.Length / decoded.Rate > 600) return;
            // Linear resampling to 22 050 Hz (the mel front end low-passes at 11 kHz anyway).
            const int rate = 22050;
            int n = (int)(decoded.Mono.LongLength * rate / (long)decoded.Rate);
            using (var w = new BinaryWriter(File.Create(pcm + ".tmp")))
            {
                double step = decoded.Rate / rate;
                for (int i = 0; i < n; i++)
                {
                    double x = i * step;
                    int j = (int)x;
                    double f = x - j, a = decoded.Mono[Math.Min(j, decoded.Mono.Length - 1)], b = decoded.Mono[Math.Min(j + 1, decoded.Mono.Length - 1)];
                    w.Write((float)(a + (b - a) * f));
                }
            }
            File.Move(pcm + ".tmp", pcm, overwrite: true);
            string json = "{\"song\":" + System.Text.Json.JsonSerializer.Serialize(Path.GetFileName(Path.GetDirectoryName(song.Audio))) +
                ",\"set\":" + System.Text.Json.JsonSerializer.Serialize(song.Set) +
                string.Create(inv, $",\"rate\":{rate},\"samples\":{n},\"duration_ms\":{decoded.Mono.Length * 1000.0 / decoded.Rate:0.###}") +
                string.Create(inv, $",\"last_note_ms\":{(doc.LastObjectTime is { } last ? last.ToString("0.###", inv) : "null")}") +
                ",\"reds\":[" + string.Join(",", reds.Select(p => string.Create(inv,
                    $"[{p.Time:0.###},{p.BeatLength:R},{p.Meter},{(p.OmitsFirstBarline ? 1 : 0)}]"))) + "]}";
            File.WriteAllText(meta, json);
            Interlocked.Increment(ref done);
        }
        catch (Exception ex) when (ex is IOException or FormatException or ArgumentException or InvalidOperationException) { Interlocked.Increment(ref failed); }
        int total = done + skipped + failed;
        if (total % 100 == 0) Console.Error.WriteLine($"{total}/{songs.Count}");
    });
    Console.WriteLine($"dumped {done}, already there {skipped}, failed {failed} of {songs.Count} -> {args[2]}");
    return 0;
}
if (args.Length >= 3 && args[0] == "dump-audio")
{
    // timing-eval dump-audio <audio> <out>: our decoder's mono samples (int32 rate, int32 count, float32 samples),
    // so an outside tracker sees exactly the audio the envelope was computed from.
    var decoded = TimingAnalyzer.Audio.AudioDecoder.Read(args[1]);
    using var writer = new BinaryWriter(File.Create(args[2]));
    writer.Write(checked((int)decoded.Rate)); writer.Write(decoded.Mono.Length);
    foreach (double v in decoded.Mono) writer.Write((float)v);
    return 0;
}
if (args.Length >= 2 && args[0] == "ext-lag")
{
    // timing-eval ext-lag <songs.tsv>...: per song, the median distance from each outside beat to the nearest onset
    // peak within 40 ms (envelope index − audio ms): the lag to pass as lag=.
    var lags = new ConcurrentBag<(string Name, double Lag, double Share)>();
    Parallel.ForEach(args.Skip(1).Where(a => !a.Contains('=')).SelectMany(SongSelection.Read).DistinctBy(s => s.Audio), song =>
    {
        var beats = EnvelopeCache.ExternalBeats(song.Audio, cache);
        if (beats is null || beats.Length == 0) return;
        var (d, _) = EnvelopeCache.Get(song.Audio, cache);
        var diffs = new List<double>();
        foreach (double t in beats)
        {
            int lo = Math.Max(1, (int)(t - 40)), hi = Math.Min(d.Length - 2, (int)(t + 40));
            int best = -1;
            for (int j = lo; j <= hi; j++)
                if (d[j] > 0 && d[j] >= d[j - 1] && d[j] >= d[j + 1] && (best < 0 || Math.Abs(j - t) < Math.Abs(best - t))) best = j;
            if (best >= 0) diffs.Add(best - t);
        }
        if (diffs.Count > 0) lags.Add((song.Name, Median(diffs.ToArray()), diffs.Count / (double)beats.Length));
    });
    var all = lags.Select(l => l.Lag).Order().ToArray();
    foreach (var l in lags.OrderBy(l => l.Lag)) Console.WriteLine($"{l.Lag,7:0.0} ms  {l.Share,5:0.00} near a peak  {l.Name}");
    if (all.Length > 0)
        Console.WriteLine($"songs {all.Length}: median lag {all[all.Length / 2]:0.0} ms, quartiles {all[all.Length / 4]:0.0} .. {all[3 * all.Length / 4]:0.0}");
    return 0;
}
if (args.Length >= 2 && args[0] == "ext-octave")
{
    // timing-eval ext-octave <songs.tsv>...: per song, the outside tracker's tempo (60000 / median interval) over the
    // reference's main BPM (the red line covering the most time): 1 = same beat level, 0.5 / 2 = half / double.
    var rows = new ConcurrentBag<string>();
    Parallel.ForEach(args.Skip(1).Where(a => !a.Contains('=')).SelectMany(SongSelection.Read), song =>
    {
        var beats = EnvelopeCache.ExternalBeats(song.Audio, cache);
        if (beats is null || beats.Length < 3) return;
        var (_, duration) = EnvelopeCache.Get(song.Audio, cache);
        var reds = BeatGrid.Sort(OsuFile.Load(song.Reference).RedLines);
        double main = reds.Select((p, i) => (p.Bpm, Span: (i + 1 < reds.Length ? reds[i + 1].Time : duration) - p.Time)).GroupBy(x => Math.Round(x.Bpm, 1))
            .OrderByDescending(g => g.Sum(x => x.Span)).First().Key;
        double ml = 60000 / Median(beats.Zip(beats.Skip(1), (a, b) => b - a).ToArray());
        rows.Add($"{song.Set,-3} {song.Split,-5} {ml / main,6:0.000} {ml,8:0.0} {main,8:0.0}  {song.Name}");
    });
    foreach (string row in rows.Order(StringComparer.Ordinal)) Console.WriteLine(row);
    return 0;
}
if (args.Length >= 3 && args[0] == "show")
{
    // timing-eval show <songs.tsv> <name substring> [algorithm] [key=value...]: red lines side by side.
    var song = SongSelection.Read(args[1]).First(s => s.Name.Contains(args[2], StringComparison.OrdinalIgnoreCase));
    var algorithm = algorithms[args.Length > 3 ? args[3] : "multi"]();
    var reference = BeatGrid.Sort(OsuFile.Load(song.Reference).RedLines);
    var (envelope, duration) = EnvelopeCache.Get(song.Audio, cache);
    var predicted = RunAlgorithm(algorithm, song, envelope, duration);
    var analysis = TimingAnalyzer.Core.Analyzer.AnalyzeEnvelope(envelope);
    Console.WriteLine($"{song.Name}  ({duration / 1000:0} s)  original: {analysis.Bpm ?? analysis.ApproximateBpm:0.###} BPM status {analysis.Status} timeline [{analysis.Timeline}]");
    Console.WriteLine($"reference {reference.Length} red lines | {algorithm.Name}: {predicted.Count}");
    int rows = Math.Max(reference.Length, predicted.Count);
    for (int i = 0; i < rows; i++)
    {
        string Cell(IReadOnlyList<TimingPoint> list) => i < list.Count ? $"{list[i].Time,9:0} {OsuPrecision.FormatBpm(list[i].Bpm),9}" : new string(' ', 19);
        Console.WriteLine($"  {Cell(reference)}   |  {Cell(predicted)}");
    }
    if (predicted.Count > 0)
    {
        var score = GridAgreement.Score(reference, predicted, duration);
        Console.WriteLine($"F10 {score.F10:0.000}  aligned {score.AlignedF10:0.000}  shift {score.ShiftMs:0.0}  median {score.MedianMs:0.0}  p90 {score.P90Ms:0.0}");
    }
    return 0;
}
if (args.Length >= 3 && args[0] == "trace")
{
    // timing-eval trace <songs.tsv> <name substring> <out.csv> [key=value...]: reference beats and tracked beats as CSV
    // (kind,time,value: ref = beat with its period, beat = tracked beat with its onset weight, red = final red line BPM).
    var song = SongSelection.Read(args[1]).First(s => s.Name.Contains(args[2], StringComparison.OrdinalIgnoreCase));
    var reference = BeatGrid.Sort(OsuFile.Load(song.Reference).RedLines);
    var (envelope, duration) = EnvelopeCache.Get(song.Audio, cache);
    var stages = TimingAnalyzer.Core.MultiBpmAnalyzer.Diagnose(envelope, multiOptions);
    using var csv = new StreamWriter(args[3]);
    csv.WriteLine("kind,time,value");
    foreach (double t in GridAgreement.Beats(reference, reference[0].Time, duration))
        csv.WriteLine(string.Create(inv, $"ref,{t:0.###},{reference[SectionOf(reference, t)].BeatLength:0.###}"));
    foreach (var b in stages.Beats) csv.WriteLine(string.Create(inv, $"beat,{b.TimeMs:0.###},{b.Weight:0.####}"));
    foreach (var r in stages.Final) csv.WriteLine(string.Create(inv, $"red,{r.TimeMs:0.###},{r.Bpm:0.###}"));
    Console.WriteLine($"{song.Name}: {stages.Beats.Count} tracked beats, {stages.Final.Count} red lines -> {args[3]}");
    return 0;
}
if (args.Length >= 5 && args[0] == "accents")
{
    // timing-eval accents <songs.tsv> <name substring> <fromMs> <toMs> [ratio]: onset peaks (osu! time) in a range that
    // stand out against the song's typical beat (median tracked-beat peak), with the nearest reference red line.
    var song = SongSelection.Read(args[1]).First(s => s.Name.Contains(args[2], StringComparison.OrdinalIgnoreCase));
    double from = double.Parse(args[3], inv), to = double.Parse(args[4], inv), ratio = args.Length > 5 ? double.Parse(args[5], inv) : 2;
    var reference = BeatGrid.Sort(OsuFile.Load(song.Reference).RedLines);
    var (envelope, _) = EnvelopeCache.Get(song.Audio, cache);
    var stages = TimingAnalyzer.Core.MultiBpmAnalyzer.Diagnose(envelope, multiOptions);
    var weights = stages.Beats.Where(b => b.Weight > 0).Select(b => b.Weight).Order().ToArray();
    double typical = weights[weights.Length / 2];
    double lag = TimingAnalyzer.Core.MultiBpmAnalyzer.PeakToOsuMs;
    Console.WriteLine($"{song.Name}: typical beat peak {typical:0.00}; peaks >= {ratio}x in {from:0}-{to:0} ms");
    for (int j = (int)(from + lag) + 60; j < Math.Min(envelope.Length - 60, (int)(to + lag)); j++)
    {
        if (envelope[j] < ratio * typical) continue;
        bool isMax = true;
        for (int k = j - 60; k <= j + 60 && isMax; k++) if (envelope[k] > envelope[j]) isMax = false;
        if (!isMax) continue;
        double t = j - lag;
        var nearest = reference.MinBy(p => Math.Abs(p.Time - t))!;
        Console.WriteLine($"  {t,9:0}  x{envelope[j] / typical,5:0.0}   nearest red {nearest.Time,9:0} ({t - nearest.Time,6:+0;-0;0} ms) {OsuPrecision.FormatBpm(nearest.Bpm),8} BPM");
    }
    return 0;
}
if (args.Length >= 2 && args[0] == "anchors")
{
    // timing-eval anchors <songs.tsv> [set=..] [split=..] [ratio=1.3]: do ramp steps start on an accent? For red lines that
    // start a short section (< RampSectionMs), the share with an onset peak >= ratio x the typical beat within 15 ms,
    // and how many such peaks the ramp regions hold in total (an anchor method needs few false ones).
    string? set = args.Skip(2).FirstOrDefault(a => a.StartsWith("set=", StringComparison.Ordinal))?[4..];
    string? split = args.Skip(2).FirstOrDefault(a => a.StartsWith("split=", StringComparison.Ordinal))?[6..];
    double ratio = args.Skip(2).FirstOrDefault(a => a.StartsWith("ratio=", StringComparison.Ordinal)) is { } rr ? double.Parse(rr[6..], inv) : 1.3;
    var songs = SongSelection.Read(args[1]).Where(s => (set is null || s.Set == set) && (split is null || s.Split == split)).ToList();
    var rows = new ConcurrentBag<(EvalSong Song, int Steps, int Anchored, int Peaks, double Seconds)>();
    Parallel.ForEach(songs, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2) }, song =>
    {
        var reference = BeatGrid.Sort(OsuFile.Load(song.Reference).RedLines);
        var (envelope, duration) = EnvelopeCache.Get(song.Audio, cache);
        var beats = TimingAnalyzer.Core.MultiBpmAnalyzer.TrackBeats(envelope, multiOptions);
        var w = beats.Where(b => b.Weight > 0).Select(b => b.Weight).Order().ToArray();
        if (w.Length == 0) return;
        double typical = w[w.Length / 2], lag = TimingAnalyzer.Core.MultiBpmAnalyzer.PeakToOsuMs;
        bool Peak(int j) { if (envelope[j] < ratio * typical) return false; for (int k = Math.Max(0, j - 60); k <= Math.Min(envelope.Length - 1, j + 60); k++) if (envelope[k] > envelope[j]) return false; return true; }
        int steps = 0, anchored = 0, peaks = 0; double seconds = 0;
        for (int i = 0; i < reference.Length; i++)
        {
            double end = i + 1 < reference.Length ? reference[i + 1].Time : duration;
            if (end - reference[i].Time >= RampSectionMs) continue;
            steps++; seconds += (end - reference[i].Time) / 1000;
            int c = (int)Math.Round(reference[i].Time + lag);
            if (Enumerable.Range(c - 15, 31).Any(j => j > 60 && j < envelope.Length - 60 && Peak(j))) anchored++;
            for (int j = Math.Max(60, c); j < Math.Min(envelope.Length - 60, (int)(end + lag)); j++) if (Peak(j)) peaks++;
        }
        if (steps > 0) rows.Add((song, steps, anchored, peaks, seconds));
    });
    foreach (var r in rows.OrderBy(r => r.Song.Set).ThenBy(r => r.Song.Name, StringComparer.Ordinal))
        Console.WriteLine($"{r.Song.Set,-2} {r.Song.Split,-5} steps {r.Steps,3}  anchored {r.Anchored,3} ({r.Anchored / (double)r.Steps,4:0%})  peaks/s {r.Peaks / Math.Max(1e-9, r.Seconds),5:0.0}  {(r.Song.Name.Length > 50 ? r.Song.Name[..50] : r.Song.Name)}");
    int s = rows.Sum(r => r.Steps), a = rows.Sum(r => r.Anchored);
    Console.WriteLine($"total: {rows.Count} songs, {s} ramp steps, anchored {a} ({a / (double)Math.Max(1, s):0%}), peaks/s {rows.Sum(r => r.Peaks) / Math.Max(1e-9, rows.Sum(r => r.Seconds)):0.0}");
    return 0;
}
if (args.Length >= 5 && args[0] == "ramp-test")
{
    // timing-eval ramp-test <songs.tsv> <name substring> <fromMs> <toMs>: is a ramp in the audio at all? For every
    // reference section in the range, the folded onset strength (best phase, so offset conventions do not matter) at
    // the section's own BPM vs at the BPM of the sections before and after the range. If the reference BPM does not
    // fold clearly stronger than its neighbours' BPMs, onsets cannot tell the ramp apart from either side.
    var song = SongSelection.Read(args[1]).First(s => s.Name.Contains(args[2], StringComparison.OrdinalIgnoreCase));
    double from = double.Parse(args[3], inv), to = double.Parse(args[4], inv);
    var reference = BeatGrid.Sort(OsuFile.Load(song.Reference).RedLines);
    var (envelope, duration) = EnvelopeCache.Get(song.Audio, cache);
    double lag = TimingAnalyzer.Core.MultiBpmAnalyzer.PeakToOsuMs;
    double Fold(double start, double end, double length)
    {
        int a = Math.Max(0, (int)Math.Round(start + lag)), b = Math.Min(envelope.Length, (int)Math.Round(end + lag));
        int c = (int)Math.Ceiling(length);
        var h = new double[c]; int folds = 0;
        for (int k = 0; a + (int)Math.Round(k * length) + c <= b; k++, folds++)
            for (int j = 0; j < c; j++) h[j] += Math.Max(0, envelope[a + (int)Math.Round(k * length) + j]);
        return folds == 0 ? 0 : h.Max() / folds;
    }
    double before = reference.Last(p => p.Time <= from).Bpm, after = reference.First(p => p.Time >= to).Bpm;
    Console.WriteLine($"{song.Name}: neighbours {OsuPrecision.FormatBpm(before)} before, {OsuPrecision.FormatBpm(after)} after");
    Console.WriteLine("   start   len ms   ref BPM  beats | fold@ref  fold@before  fold@after  best BPM (±12%)  best/ref");
    for (int i = 0; i < reference.Length; i++)
    {
        double start = reference[i].Time, end = i + 1 < reference.Length ? reference[i + 1].Time : duration;
        if (end <= from || start >= to) continue;
        double atRef = Fold(start, end, reference[i].BeatLength);
        double bestBpm = reference[i].Bpm, best = atRef;
        for (double bpm = reference[i].Bpm * 0.88; bpm <= reference[i].Bpm * 1.12; bpm += 0.25)
        {
            double v = Fold(start, end, 60000 / bpm);
            if (v > best) { best = v; bestBpm = bpm; }
        }
        Console.WriteLine($"  {start,7:0} {end - start,8:0} {OsuPrecision.FormatBpm(reference[i].Bpm),9} {(end - start) / reference[i].BeatLength,6:0.0} | " +
            $"{atRef,8:0.00} {Fold(start, end, 60000 / before),12:0.00} {Fold(start, end, 60000 / after),11:0.00}  {bestBpm,10:0.00}      {best / Math.Max(1e-9, atRef),6:0.00}");
    }
    return 0;
}
if (args.Length >= 2 && args[0] == "wobble")
{
    // timing-eval wobble <songs.tsv>: DrumWobble (the editor's live-drum hint) per song, decoded as the editor does.
    var songs = SongSelection.Read(args[1]).ToList();
    var lines = new ConcurrentBag<(EvalSong Song, string Text)>();
    Parallel.ForEach(songs, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2) }, song =>
    {
        try
        {
            var decoded = TimingAnalyzer.Audio.AudioDecoder.Read(song.Audio);
            var (envelope, _) = EnvelopeCache.Get(song.Audio, cache);
            var (_, beats) = TimingAnalyzer.Core.MultiBpmAnalyzer.AnalyzeWithBeats(envelope, multiOptions);
            var w = TimingAnalyzer.Core.DrumWobble.Measure(decoded.Mono, decoded.Rate, beats);
            lines.Add((song, $"{(w is null ? "     -" : w.MedianMs.ToString("0.00", inv).PadLeft(6))} {w?.Triples ?? 0,6} {(w?.SuggestsLiveDrums == true ? "HINT" : "    ")}  {song.Name}"));
        }
        catch (Exception ex) { lines.Add((song, $"error {ex.GetType().Name}: {ex.Message}  {song.Name}")); }
    });
    Console.WriteLine("median triples hint  song");
    foreach (var l in lines.OrderBy(l => l.Song.Name, StringComparer.Ordinal)) Console.WriteLine(l.Text);
    return 0;
}
if (args.Length >= 2 && args[0] == "beats")
{
    // timing-eval beats <songs.tsv> [set=..] [split=..] [key=value...]: where the multi-BPM pipeline loses accuracy.
    // Per song: tracked-beat recall vs the reference beats (within 20 ms, and at any quarter-beat phase), how
    // steady the tracked interval is against the reference period, and red lines before/after the merge passes.
    string? set = args.Skip(2).FirstOrDefault(a => a.StartsWith("set=", StringComparison.Ordinal))?[4..];
    string? split = args.Skip(2).FirstOrDefault(a => a.StartsWith("split=", StringComparison.Ordinal))?[6..];
    var songs = SongSelection.Read(args[1]).Where(s => (set is null || s.Set == set) && (split is null || s.Split == split)).ToList();
    var lines = new ConcurrentBag<(EvalSong Song, string Text)>();
    Parallel.ForEach(songs, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2) }, song =>
    {
        try
        {
            var reference = BeatGrid.Sort(OsuFile.Load(song.Reference).RedLines);
            var (envelope, duration) = EnvelopeCache.Get(song.Audio, cache);
            var stages = TimingAnalyzer.Core.MultiBpmAnalyzer.Diagnose(envelope, multiOptions);
            var refBeats = GridAgreement.Beats(reference, reference[0].Time, duration);
            var tracked = stages.Beats.Select(b => b.TimeMs).ToArray();
            double Recall(double limit) => refBeats.Count == 0 ? 0 : refBeats.Count(t => NearestTracked(tracked, t) <= limit) / (double)refBeats.Count;
            // Same, but any quarter-beat phase counts: separates "wrong beats" from "right tempo, off-beat phase".
            double quarter = refBeats.Count == 0 ? 0 : refBeats.Count(t =>
            {
                double step = reference[SectionOf(reference, t)].BeatLength / 4;
                return Enumerable.Range(0, 4).Any(q => NearestTracked(tracked, t + q * step) <= 20 || NearestTracked(tracked, t - q * step) <= 20);
            }) / (double)refBeats.Count;
            // Tracked interval vs reference period at the same time (1.0 = same tempo, 0.5/2.0 = octave error).
            var ratios = new List<double>();
            for (int k = 1; k < tracked.Length; k++)
            {
                double mid = (tracked[k] + tracked[k - 1]) / 2;
                if (mid < reference[0].Time) continue;
                ratios.Add((tracked[k] - tracked[k - 1]) / reference[SectionOf(reference, mid)].BeatLength);
            }
            double tempoOk = ratios.Count == 0 ? 0 : ratios.Count(r => Math.Abs(r - 1) < 0.03) / (double)ratios.Count;
            double octave = ratios.Count == 0 ? 0 : ratios.Count(r => Math.Abs(r - 0.5) < 0.03 || Math.Abs(r - 2) < 0.06) / (double)ratios.Count;
            // Ramp tracking: aligned recall (one constant shift removed, as offset conventions differ) on reference
            // beats inside sections shorter than RampSectionMs. This isolates the tracker from the segmentation.
            double shift = Median(refBeats.Select(t => SignedTracked(tracked, t)).Where(e => Math.Abs(e) < 60).ToArray());
            var shifted = tracked.Select(t => t - (double.IsNaN(shift) ? 0 : shift)).ToArray();
            var ramp = refBeats.Where(t => SectionMs(reference, t, duration) < RampSectionMs).ToArray();
            int rampHits = ramp.Count(t => NearestTracked(shifted, t) <= 20);
            double aligned = refBeats.Count == 0 ? 0 : refBeats.Count(t => NearestTracked(shifted, t) <= 20) / (double)refBeats.Count;
            lines.Add((song, $"{song.Set,-3} {song.Split,-5} {Recall(20),6:0.000} {aligned,6:0.000} {quarter,6:0.000} {tempoOk,6:0.000} {octave,6:0.000} " +
                $"{(ramp.Length == 0 ? "     -" : (rampHits / (double)ramp.Length).ToString("0.000", inv).PadLeft(6))} {ramp.Length,5} {rampHits,5}  " +
                $"{reference.Length,4} {stages.Spans,5} {stages.Unmerged.Count,5} {stages.Final.Count,5}  {(song.Name.Length > 50 ? song.Name[..50] : song.Name)}"));
        }
        catch (Exception ex) { lines.Add((song, $"{song.Set,-3} {song.Split,-5} error {ex.GetType().Name}: {ex.Message}  {song.Name}")); }
    });
    Console.WriteLine("set split  rec20  aln20  qrt20  tempo  octave  ramp20 rampN rampH   ref spans unmrg final  song");
    foreach (var l in lines.OrderBy(l => l.Song.Set).ThenBy(l => l.Song.Split).ThenBy(l => l.Song.Name, StringComparer.Ordinal)) Console.WriteLine(l.Text);
    return 0;
}
if (args.Length >= 2 && args[0] == "range")
{
    // timing-eval range <songs.tsv> [set=..] [split=..] [pad=ms]: the editor's "Analyze range" around each reference
    // tempo change (a reference red line with a BPM over 2 % from the one before, both sections at least 2 x pad long),
    // range = change ± pad. Scored on the reference beats inside the range: the range's own red lines against the
    // whole-song red lines there (aligned 10 ms recall, after removing each song's whole-song shift).
    string? set = args.Skip(2).FirstOrDefault(a => a.StartsWith("set=", StringComparison.Ordinal))?[4..];
    string? split = args.Skip(2).FirstOrDefault(a => a.StartsWith("split=", StringComparison.Ordinal))?[6..];
    double pad = Opt("pad", 15000);
    var songs = SongSelection.Read(args[1]).Where(s => (set is null || s.Set == set) && (split is null || s.Split == split)).ToList();
    var found = new ConcurrentBag<(EvalSong Song, double Change, int Beats, int Whole, int Range, string Lines)>();
    Parallel.ForEach(songs, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2) }, song =>
    {
        var reference = BeatGrid.Sort(OsuFile.Load(song.Reference).RedLines);
        var (envelope, duration) = EnvelopeCache.Get(song.Audio, cache);
        if (duration > 600000) return;
        var whole = new MultiAlgorithm(multiOptions).Run(envelope, duration);
        if (whole.Count == 0) return;
        double shift = GridAgreement.Score(reference, whole, duration).ShiftMs;
        // ranchor=1: the whole song's detector BPM as the range's octave anchor (MultiBpmOptions.AnchorBpm); rpad=ms: context
        // analyzed on each side of the range (RangeTiming.Analyze contextMs).
        var rangeOptions = Opt("ranchor", 0) != 0 ? multiOptions with { AnchorBpm = TimingAnalyzer.Core.RangeTiming.WholeSongAnchor(envelope) } : multiOptions;
        for (int i = 1; i < reference.Length; i++)
        {
            var (before, change) = (reference[i - 1], reference[i]);
            double next = i + 1 < reference.Length ? reference[i + 1].Time : duration;
            if (Math.Abs(change.Bpm / before.Bpm - 1) < 0.02 || change.Time - before.Time < 2 * pad || next - change.Time < 2 * pad) continue;
            double a = change.Time - pad, b = change.Time + pad;
            var lines = TimingAnalyzer.Core.RangeTiming.Analyze(envelope, a, b, rangeOptions, Opt("rpad", 0))
                .Select(s => new TimingPoint(OsuPrecision.Time(s.TimeMs), 60000 / OsuPrecision.Bpm(s.Bpm))).GroupBy(p => p.Time).Select(g => g.First()).ToArray();
            var beats = GridAgreement.Beats(reference, reference[0].Time, duration).Where(t => t >= a && t < b).ToArray();
            int Hits(IReadOnlyList<TimingPoint> predicted)
            {
                if (predicted.Count == 0) return 0;
                var aligned = predicted.Select(p => p with { Time = p.Time - shift }).ToArray();
                return beats.Count(t => GridAgreement.Nearest(aligned, t, 1) <= 10);
            }
            found.Add((song, change.Time, beats.Length, Hits(whole), Hits(lines),
                string.Join(" ", lines.Select(p => $"{p.Time:0}@{OsuPrecision.FormatBpm(p.Bpm)}"))));
        }
    });
    Console.WriteLine($"range ±{pad:0} ms around reference tempo changes: aligned 10 ms recall of the reference beats in the range");
    Console.WriteLine("set split  change ms  beats  whole  range  song / range red lines");
    foreach (var r in found.OrderBy(r => r.Song.Set).ThenBy(r => r.Song.Split).ThenBy(r => r.Song.Name, StringComparer.Ordinal).ThenBy(r => r.Change))
        Console.WriteLine($"{r.Song.Set,-3} {r.Song.Split,-5} {r.Change,9:0} {r.Beats,6} {r.Whole / (double)r.Beats,6:0.000} {r.Range / (double)r.Beats,6:0.000}  " +
            $"{(r.Song.Name.Length > 40 ? r.Song.Name[..40] : r.Song.Name)} | {r.Lines}");
    foreach (var g in found.GroupBy(r => (r.Song.Set, r.Song.Split)).OrderBy(g => g.Key.Set).ThenBy(g => g.Key.Split))
        Console.WriteLine($"  {g.Key.Set} {g.Key.Split,-5} changes {g.Count(),3}  whole {g.Sum(r => r.Whole) / (double)g.Sum(r => r.Beats):0.000}  " +
            $"range {g.Sum(r => r.Range) / (double)g.Sum(r => r.Beats):0.000}  range better {g.Count(r => r.Range > r.Whole + 0.1 * r.Beats),3}  " +
            $"worse {g.Count(r => r.Range < r.Whole - 0.1 * r.Beats),3}");
    return 0;
}
if (args.Length >= 3 && args[0] == "select-features")
{
    // timing-eval select-features <songs.tsv> <out.csv>: Phase 4 N4 per-song choice between the algorithm and the beat
    // model's own red lines. One row per song: both aligned F10 scores (the label), and signals known without the
    // answer: red-line counts, how far each timing's beats are from the other's (agreement), share of each timing's
    // beats on an onset peak, the algorithm's original detector status, the model's beat count per minute, the song
    // length, and the BPM spread of each. Model beats from the cache subfolder TIMING_EVAL_BEATS.
    var songs = SongSelection.Read(args[1]);
    var rows = new ConcurrentBag<string>();
    Parallel.ForEach(songs, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2) }, song =>
    {
        try
        {
            var reference = BeatGrid.Sort(OsuFile.Load(song.Reference).RedLines);
            var (envelope, duration) = EnvelopeCache.Get(song.Audio, cache);
            if (duration > 600000) return;
            var beats = EnvelopeCache.ExternalBeats(song.Audio, cache);
            if (beats is null || beats.Length < 8) return;
            var alg = new MultiAlgorithm(multiOptions).Run(envelope, duration);
            var model = new ModelOnlyAlgorithm(() => beats).Run(envelope, duration);
            if (alg.Count == 0 || model.Count == 0) return;
            double sa = GridAgreement.Score(reference, alg, duration).AlignedF10, sm = GridAgreement.Score(reference, model, duration).AlignedF10;
            double start = Math.Max(alg[0].Time, model[0].Time), end = duration;
            double Agree(IReadOnlyList<TimingPoint> from, IReadOnlyList<TimingPoint> to)
            {
                var bs = GridAgreement.Beats(from, start, end).ToArray();
                return bs.Length == 0 ? 0 : bs.Count(t => GridAgreement.Nearest(to, t, 1) <= 10) / (double)bs.Length;
            }
            // Onset support: share of a timing's beats with an envelope peak within 10 ms (envelope index = osu! time + 28.7).
            double OnPeaks(IReadOnlyList<TimingPoint> reds)
            {
                var bs = GridAgreement.Beats(reds, start, end).ToArray();
                if (bs.Length == 0) return 0;
                double mean = envelope.Average(), sd = Math.Sqrt(envelope.Average(x => (x - mean) * (x - mean)));
                int hits = 0;
                foreach (double t in bs)
                {
                    int c = (int)Math.Round(t + TimingAnalyzer.Core.MultiBpmAnalyzer.PeakToOsuMs);
                    double best = double.MinValue;
                    for (int j = Math.Max(0, c - 10); j <= Math.Min(envelope.Length - 1, c + 10); j++) best = Math.Max(best, envelope[j]);
                    if (best > mean + sd) hits++;
                }
                return hits / (double)bs.Length;
            }
            double Spread(IReadOnlyList<TimingPoint> reds) => reds.Count < 2 ? 0 : Math.Log(reds.Max(p => p.Bpm) / reds.Min(p => p.Bpm));
            var original = TimingAnalyzer.Core.Analyzer.AnalyzeEnvelope(envelope);
            rows.Add(string.Create(inv,
                $"{song.Set},{song.Split},{Path.GetFileName(Path.GetDirectoryName(song.Audio))?.Replace(',', ' ')},{sa:0.####},{sm:0.####}," +
                $"{alg.Count},{model.Count},{Agree(alg, model):0.####},{Agree(model, alg):0.####},{OnPeaks(alg):0.####},{OnPeaks(model):0.####}," +
                $"{original.Status},{beats.Length * 60000.0 / duration:0.###},{duration / 1000:0.#},{Spread(alg):0.####},{Spread(model):0.####}"));
        }
        catch (Exception ex) when (ex is IOException or FormatException or ArgumentException or InvalidOperationException) { }
    });
    using (var csv = new StreamWriter(args[2]))
    {
        csv.WriteLine("set,split,song,alg,model,alg_lines,model_lines,alg_in_model,model_in_alg,alg_on_peaks,model_on_peaks,orig_status,model_beats_per_min,seconds,alg_spread,model_spread");
        foreach (var r in rows.Order(StringComparer.Ordinal)) csv.WriteLine(r);
    }
    Console.WriteLine($"{rows.Count} songs -> {args[2]}");
    return 0;
}
if (args.Length >= 3 && args[0] == "features")
{
    // timing-eval features <songs.tsv> <out.csv>: training rows for the octave selector (docs/PHASE1-STATUS.md E36).
    // One row per multi-BPM red line whose section is >= 10 s and lies inside one reference section: the detected
    // BPM, onset features of candidate tempos (×½, ×⅔, ×¾, ×1, ×4/3, ×1.5, ×2 of it, each at its best phase), and the
    // label = the candidate whose BPM is within 1 % of the reference's (empty if none: an unlearnable row).
    var songs = SongSelection.Read(args[1]);
    double[] ratios = [0.5, 2.0 / 3, 0.75, 1, 4.0 / 3, 1.5, 2];
    var rows = new ConcurrentBag<string>();
    int done = 0;
    Parallel.ForEach(songs, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2) }, song =>
    {
        try
        {
            var reference = BeatGrid.Sort(OsuFile.Load(song.Reference).RedLines);
            var (envelope, duration) = EnvelopeCache.Get(song.Audio, cache);
            if (duration > 600000 || reference.Length == 0) return;
            var detected = TimingAnalyzer.Core.MultiBpmAnalyzer.Analyze(envelope, multiOptions);
            for (int i = 0; i < detected.Count; i++)
            {
                double a = detected[i].TimeMs, b = i + 1 < detected.Count ? detected[i + 1].TimeMs : duration;
                if (b - a < 10000) continue;
                // The reference section covering the middle 80 % of this one, if a single one does.
                int r0 = SectionOf(reference, a + 0.1 * (b - a)), r1 = SectionOf(reference, b - 0.1 * (b - a));
                if (r0 != r1) continue;
                double refBpm = reference[r0].Bpm, bpm = detected[i].Bpm;
                var features = TimingAnalyzer.Core.OctaveFeatures.Measure(envelope, bpm, a, b, ratios);
                int label = Array.FindIndex(ratios, r => Math.Abs(bpm * r / refBpm - 1) < 0.01);
                rows.Add(string.Create(inv, $"{song.Set},{Path.GetFileName(Path.GetDirectoryName(song.Audio))?.Replace(',', ' ')},{a:0},{b:0},{bpm:0.###},{refBpm:0.###},{(label < 0 ? "" : ratios[label].ToString("0.####", inv))},{string.Join(",", features.Select(f => f.ToString("0.####", inv)))}"));
            }
        }
        catch (Exception ex) when (ex is IOException or FormatException or ArgumentException or InvalidOperationException) { }
        int n = Interlocked.Increment(ref done);
        if (n % 50 == 0) Console.Error.WriteLine($"{n}/{songs.Count} songs");
    });
    using (var csv = new StreamWriter(args[2]))
    {
        csv.WriteLine("set,song,start,end,bpm,ref_bpm,label," + string.Join(",", TimingAnalyzer.Core.OctaveFeatures.Names(ratios)));
        foreach (var row in rows.Order(StringComparer.Ordinal)) csv.WriteLine(row);
    }
    Console.WriteLine($"{rows.Count} sections from {songs.Count} songs -> {args[2]}");
    return 0;
}
if (args.Length >= 2 && args[0] == "triplet")
{
    // timing-eval triplet <songs.tsv>: for each one-tempo Auto-time answer of at least 150 BPM, the triplet measure of
    // MultiBpmOptions.TripletRatio (other-beat peak / strongest-beat peak, folded at three beats) next to the map's
    // main BPM (the red line covering the most time) over the detected one: ratio ~0.333 means the map is at a third.
    var songs = SongSelection.Read(args[1]);
    var lines = new ConcurrentBag<(double Measure, string Text)>();
    var plain = multiOptions with { TripletRatio = 0 };
    Parallel.ForEach(songs, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2) }, song =>
    {
        try
        {
            var reference = BeatGrid.Sort(OsuFile.Load(song.Reference).RedLines);
            var (envelope, duration) = EnvelopeCache.Get(song.Audio, cache);
            var detected = TimingAnalyzer.Core.MultiBpmAnalyzer.Analyze(envelope, plain);
            if (detected.Count != 1 || detected[0].Bpm < 150 || reference.Length == 0) return;
            var main = reference.Select((p, i) => (p.Bpm, Span: (i + 1 < reference.Length ? reference[i + 1].Time : duration) - p.Time)).MaxBy(x => x.Span);
            var (beat, measure) = TimingAnalyzer.Core.MultiBpmAnalyzer.TripletPeaks(envelope, detected[0], duration);
            lines.Add((measure, string.Create(inv, $"{measure:0.000}  beat {beat}  {detected[0].Bpm,8:0.###} BPM  map {main.Bpm,8:0.###} (x{main.Bpm / detected[0].Bpm:0.###})  {song.Set,-3} {Path.GetFileName(Path.GetDirectoryName(song.Audio))}")));
        }
        catch (Exception ex) when (ex is IOException or FormatException or ArgumentException or InvalidOperationException) { }
    });
    foreach (var l in lines.OrderBy(l => l.Measure)) Console.WriteLine(l.Text);
    Console.WriteLine($"{lines.Count} one-tempo answers of at least 150 BPM in {songs.Count} songs");
    return 0;
}
if (args.Length >= 2 && args[0] == "octave-hint")
{
    // timing-eval octave-hint <songs.tsv> [omargin=..]: the octave selector run on the maps' OWN red lines (sections of
    // >= 10 s). The maps are mappers' timing, so every hint is a false alarm: the cost of showing the selector as a
    // hint in Check sections. Then on whole-song Auto-time's red lines, the share of hints that point at the map's
    // BPM (a useful hint) versus away from it.
    double margin = Opt("omargin", TimingAnalyzer.Core.OctaveSelector.Margin);
    var songs = SongSelection.Read(args[1]);
    int refSections = 0, refHints = 0, autoSections = 0, autoHints = 0, useful = 0, harmful = 0;
    var hintLines = new ConcurrentBag<string>();
    object gate = new();
    Parallel.ForEach(songs, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2) }, song =>
    {
        var reference = BeatGrid.Sort(OsuFile.Load(song.Reference).RedLines);
        var (envelope, duration) = EnvelopeCache.Get(song.Audio, cache);
        if (duration > 600000) return;
        int rs = 0, rh = 0, as_ = 0, ah = 0, u = 0, h = 0;
        for (int i = 0; i < reference.Length; i++)
        {
            double a = reference[i].Time, b = i + 1 < reference.Length ? reference[i + 1].Time : duration;
            if (b - a < TimingAnalyzer.Core.OctaveSelector.MinSectionMs) continue;
            rs++;
            double r = TimingAnalyzer.Core.OctaveSelector.Ratio(envelope, reference[i].Bpm, a, b, margin);
            if (r != 1) { rh++; hintLines.Add($"{song.Set} {Path.GetFileName(Path.GetDirectoryName(song.Audio))} {a:0}@{reference[i].Bpm:0.##} x{r:0.###}"); }
        }
        var detected = TimingAnalyzer.Core.MultiBpmAnalyzer.Analyze(envelope, multiOptions);
        for (int i = 0; i < detected.Count; i++)
        {
            double a = detected[i].TimeMs, b = i + 1 < detected.Count ? detected[i + 1].TimeMs : duration;
            if (b - a < TimingAnalyzer.Core.OctaveSelector.MinSectionMs) continue;
            int r0 = SectionOf(reference, a + 0.1 * (b - a)), r1 = SectionOf(reference, b - 0.1 * (b - a));
            if (r0 != r1) continue;
            as_++;
            double r = TimingAnalyzer.Core.OctaveSelector.Ratio(envelope, detected[i].Bpm, a, b, margin);
            if (r == 1) continue;
            ah++;
            if (Math.Abs(detected[i].Bpm * r / reference[r0].Bpm - 1) < 0.01) u++; else h++;
        }
        lock (gate) { refSections += rs; refHints += rh; autoSections += as_; autoHints += ah; useful += u; harmful += h; }
    });
    foreach (var l in hintLines.Order(StringComparer.Ordinal).Take(30)) Console.WriteLine("  " + l);
    Console.WriteLine($"margin {margin}: on the maps' own red lines {refHints} hints in {refSections} sections ({refHints / (double)Math.Max(1, refSections):P1} false alarms); " +
        $"on Auto-time's red lines {autoHints} hints in {autoSections} sections: {useful} point at the map's BPM, {harmful} away from it");
    return 0;
}
if (args.Length >= 2 && args[0] == "check")
{
    // timing-eval check <songs.tsv> [set=..] [split=..]: SectionCheck on each reference map's own red lines. The
    // references are mappers' timing, so every mark is a false alarm or a reference error: the rate of marks per
    // judged section is the cost of the editor's "check" list.
    string? set = args.Skip(2).FirstOrDefault(a => a.StartsWith("set=", StringComparison.Ordinal))?[4..];
    string? split = args.Skip(2).FirstOrDefault(a => a.StartsWith("split=", StringComparison.Ordinal))?[6..];
    var songs = SongSelection.Read(args[1]).Where(s => (set is null || s.Set == set) && (split is null || s.Split == split)).ToList();
    var found = new ConcurrentBag<(EvalSong Song, int Judged, int Weak, int Ratio, int Shift, string Marks)>();
    Parallel.ForEach(songs, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2) }, song =>
    {
        var reference = BeatGrid.Sort(OsuFile.Load(song.Reference).RedLines);
        var (envelope, duration) = EnvelopeCache.Get(song.Audio, cache);
        var sections = reference.Select(p => new TimingAnalyzer.Core.TimingSection(p.Time, p.Bpm)).ToList();
        var reports = TimingAnalyzer.Core.SectionCheck.Check(envelope, sections, duration);
        var marks = new List<string>();
        for (int i = 0; i < reports.Count; i++)
            if (reports[i] is { } r && (r.Weak || r.BestRatio is not null || r.ShiftMs is not null))
                marks.Add($"{sections[i].TimeMs:0}@{sections[i].Bpm:0.##}" + (r.Weak ? " weak" : "") +
                    (r.BestRatio is { } x ? $" x{x:0.###}" : "") + (r.ShiftMs is { } m ? $" {m:+0;-0}ms" : ""));
        found.Add((song, reports.Count(r => r is not null), reports.Count(r => r?.Weak == true),
            reports.Count(r => r?.BestRatio is not null), reports.Count(r => r?.ShiftMs is not null), string.Join(", ", marks)));
    });
    foreach (var r in found.OrderBy(r => r.Song.Set).ThenBy(r => r.Song.Name, StringComparer.Ordinal).Where(r => r.Marks.Length > 0))
        Console.WriteLine($"{r.Song.Set} {(r.Song.Name.Length > 44 ? r.Song.Name[..44] : r.Song.Name),-44} {r.Marks}");
    foreach (var g in found.GroupBy(r => (r.Song.Set, r.Song.Split)).OrderBy(g => g.Key.Set).ThenBy(g => g.Key.Split))
        Console.WriteLine($"  {g.Key.Set} {g.Key.Split,-5} songs {g.Count(),3}  judged sections {g.Sum(r => r.Judged),4}  weak {g.Sum(r => r.Weak),3}  " +
            $"ratio {g.Sum(r => r.Ratio),3}  shift {g.Sum(r => r.Shift),3}  songs with a mark {g.Count(r => r.Marks.Length > 0),3}");
    return 0;
}
if (args.Length >= 2 && args[0] == "range-section")
{
    // timing-eval range-section <songs.tsv> [set=..] [split=..] [min=ms] [trim=ms]: a range over one reference section
    // (its red line + trim to the next − trim, sections of at least min ms), as a user highlights a stretch with one
    // tempo. Aligned 10 ms recall of the reference beats in the range for: whole-song red lines, the multi-BPM
    // analyzer on the slice, and the single-BPM detector on the slice (Auto-time section).
    string? set = args.Skip(2).FirstOrDefault(a => a.StartsWith("set=", StringComparison.Ordinal))?[4..];
    string? split = args.Skip(2).FirstOrDefault(a => a.StartsWith("split=", StringComparison.Ordinal))?[6..];
    double min = Opt("min", 10000), trim = Opt("trim", 500);
    var songs = SongSelection.Read(args[1]).Where(s => (set is null || s.Set == set) && (split is null || s.Split == split)).ToList();
    var found = new ConcurrentBag<(EvalSong Song, double Start, int Beats, int Whole, int Multi, int Single)>();
    Parallel.ForEach(songs, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2) }, song =>
    {
        var reference = BeatGrid.Sort(OsuFile.Load(song.Reference).RedLines);
        if (reference.Length < 2) return;
        var (envelope, duration) = EnvelopeCache.Get(song.Audio, cache);
        if (duration > 600000) return;
        var whole = new MultiAlgorithm(multiOptions).Run(envelope, duration);
        if (whole.Count == 0) return;
        double shift = GridAgreement.Score(reference, whole, duration).ShiftMs;
        // ranchor=1: the whole song's detector BPM as the range's octave anchor (MultiBpmOptions.AnchorBpm); rpad=ms: context
        // analyzed on each side of the range (RangeTiming.Analyze contextMs).
        var rangeOptions = Opt("ranchor", 0) != 0 ? multiOptions with { AnchorBpm = TimingAnalyzer.Core.RangeTiming.WholeSongAnchor(envelope) } : multiOptions;
        for (int i = 0; i < reference.Length; i++)
        {
            double a = reference[i].Time + trim, b = (i + 1 < reference.Length ? reference[i + 1].Time : duration) - trim;
            if (b - a < min || a < 0) continue;
            var multi = TimingAnalyzer.Core.RangeTiming.Analyze(envelope, a, b, rangeOptions, Opt("rpad", 0))
                .Select(s => new TimingPoint(OsuPrecision.Time(s.TimeMs), 60000 / OsuPrecision.Bpm(s.Bpm))).GroupBy(p => p.Time).Select(g => g.First()).ToArray();
            var r = TimingAnalyzer.Core.Analyzer.AnalyzeEnvelope(envelope[(int)a..(int)b]);
            TimingPoint[] single = r.Bpm is { } bpm && r.Offset is { } offset ? [new TimingPoint(OsuPrecision.Time(offset + (int)a), 60000 / OsuPrecision.Bpm(bpm))] : [];
            var beats = GridAgreement.Beats(reference, reference[0].Time, duration).Where(t => t >= a && t < b).ToArray();
            int Hits(IReadOnlyList<TimingPoint> predicted)
            {
                if (predicted.Count == 0) return 0;
                var aligned = predicted.Select(p => p with { Time = p.Time - shift }).ToArray();
                return beats.Count(t => GridAgreement.Nearest(aligned, t, 1) <= 10);
            }
            found.Add((song, a, beats.Length, Hits(whole), Hits(multi), Hits(single)));
        }
    });
    Console.WriteLine($"one reference section per range (>= {min:0} ms, trimmed {trim:0} ms): aligned 10 ms recall in the range");
    foreach (var g in found.GroupBy(r => (r.Song.Set, r.Song.Split)).OrderBy(g => g.Key.Set).ThenBy(g => g.Key.Split))
    {
        double Rate(Func<(EvalSong Song, double Start, int Beats, int Whole, int Multi, int Single), int> hits) => g.Sum(hits) / (double)g.Sum(r => r.Beats);
        // Sections where whole-song timing is poor (< 50 %): the ones a user would re-analyze.
        var poor = g.Where(r => r.Whole < 0.5 * r.Beats).ToArray();
        double PoorRate(Func<(EvalSong Song, double Start, int Beats, int Whole, int Multi, int Single), int> hits) =>
            poor.Length == 0 ? double.NaN : poor.Sum(hits) / (double)poor.Sum(r => r.Beats);
        Console.WriteLine($"  {g.Key.Set} {g.Key.Split,-5} ranges {g.Count(),3}  whole {Rate(r => r.Whole):0.000}  multi {Rate(r => r.Multi):0.000}  " +
            $"single {Rate(r => r.Single):0.000}  | whole < 50 %: {poor.Length,3} ranges, whole {PoorRate(r => r.Whole):0.000}  multi {PoorRate(r => r.Multi):0.000}  " +
            $"single {PoorRate(r => r.Single):0.000}  multi fixes {poor.Count(r => r.Multi >= 0.8 * r.Beats),3}  single fixes {poor.Count(r => r.Single >= 0.8 * r.Beats),3}");
    }
    return 0;
}
if (args.Length >= 2 && args[0] is "edge-lag" or "phase" or "bar-start" or "octave-beat" or "normal-variants" or "auto-time")
{
    // The measurements behind docs/PHASE1-STATUS.md E43-E44 (AutoTimeChecks.cs): timing-eval <check> <songs.tsv>...
    var lists = args.Skip(1).ToList();
    return args[0] switch
    {
        "edge-lag" => AutoTimeChecks.EdgeLag(lists, cache),
        "phase" => AutoTimeChecks.Phase(lists, cache),
        "bar-start" => AutoTimeChecks.BarStartCheck(lists, cache),
        "octave-beat" => AutoTimeChecks.OctaveBeat(lists, cache),
        "auto-time" => AutoTimeChecks.AutoTime(lists),
        _ => AutoTimeChecks.NormalVariants(lists),
    };
}
if (args.Length >= 2 && args[0] == "run")
{
    var algorithm = algorithms[args.Length > 2 ? args[2] : "baseline"]();
    string? set = args.Skip(3).FirstOrDefault(a => a.StartsWith("set=", StringComparison.Ordinal))?[4..];
    string? split = args.Skip(3).FirstOrDefault(a => a.StartsWith("split=", StringComparison.Ordinal))?[6..];
    var songs = SongSelection.Read(args[1]).Where(s => (set is null || s.Set == set) && (split is null || s.Split == split)).ToList();
    var rows = new ConcurrentBag<(EvalSong Song, GridScore? Score, int Reference, int Predicted, string? Error)>();
    // Ramp beats: reference beats in sections shorter than RampSectionMs; hit = a predicted beat within 10 ms after
    // the song's constant shift is removed (the aligned view, as offset conventions differ).
    var ramps = new ConcurrentDictionary<EvalSong, (int Beats, int Hits)>();
    Parallel.ForEach(songs, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2) }, song =>
    {
        try
        {
            var reference = BeatGrid.Sort(OsuFile.Load(song.Reference).RedLines);
            var (envelope, duration) = EnvelopeCache.Get(song.Audio, cache);
            var predicted = RunAlgorithm(algorithm, song, envelope, duration);
            if (predicted.Count == 0) { rows.Add((song, null, reference.Length, 0, "no timing")); return; }
            var score = GridAgreement.Score(reference, predicted, duration);
            rows.Add((song, score, reference.Length, predicted.Count, null));
            var aligned = predicted.Select(p => p with { Time = p.Time - score.ShiftMs }).ToArray();
            var rampBeats = GridAgreement.Beats(reference, reference[0].Time, duration).Where(t => SectionMs(reference, t, duration) < RampSectionMs).ToArray();
            ramps[song] = (rampBeats.Length, rampBeats.Count(t => GridAgreement.Nearest(aligned, t, 1) <= 10));
        }
        catch (Exception ex) { rows.Add((song, null, 0, 0, ex.GetType().Name + ": " + ex.Message)); }
    });
    Console.WriteLine($"algorithm {algorithm.Name}, {songs.Count} songs");
    Console.WriteLine("set split  F10   rec10  prec10 half10  med ms  p90 ms  shift  alnF10  reds ref/pred  song");
    foreach (var r in rows.OrderBy(r => r.Song.Set).ThenBy(r => r.Song.Split).ThenBy(r => r.Song.Name, StringComparer.Ordinal))
    {
        string name = r.Song.Name.Length > 58 ? r.Song.Name[..58] : r.Song.Name;
        Console.WriteLine(r.Score is { } s
            ? $"{r.Song.Set,-3} {r.Song.Split,-5} {s.F10,5:0.000} {s.Within10,6:0.000} {s.Precision10,6:0.000} {s.HalfTempoWithin10,6:0.000} {s.MedianMs,7:0.0} {s.P90Ms,7:0.0} {s.ShiftMs,6:0.0} {s.AlignedF10,6:0.000}  {r.Reference,5}/{r.Predicted,-5}  {name}"
            : $"{r.Song.Set,-3} {r.Song.Split,-5} {"-",5} {"",6} {"",6} {"",6} {"",7} {"",7} {"",6} {"",6}  {r.Reference,5}/{r.Predicted,-5}  {name}  ({r.Error})");
    }
    Console.WriteLine();
    Console.WriteLine("summary (failed songs count as F10 = 0)");
    foreach (var g in rows.GroupBy(r => (r.Song.Set, r.Song.Split)).OrderBy(g => g.Key.Set).ThenBy(g => g.Key.Split))
    {
        var f = g.Select(r => r.Score?.F10 ?? 0).ToArray();
        var aligned = g.Select(r => r.Score?.AlignedF10 ?? 0).ToArray();
        var scored = g.Where(r => r.Score is not null).Select(r => r.Score!).ToArray();
        double medianOfMedians = scored.Length == 0 ? double.NaN : scored.Select(s => s.MedianMs).Order().ElementAt(scored.Length / 2);
        int rampBeats = g.Sum(r => ramps.TryGetValue(r.Song, out var v) ? v.Beats : 0), rampHits = g.Sum(r => ramps.TryGetValue(r.Song, out var v) ? v.Hits : 0);
        Console.WriteLine($"  {g.Key.Set} {g.Key.Split,-5} n={g.Count(),3}  mean F10 {f.Average():0.000}  aligned {aligned.Average():0.000}  " +
            $"F10>=0.9 {f.Count(x => x >= 0.9),3}  aligned>=0.9 {aligned.Count(x => x >= 0.9),3}  median med {medianOfMedians:0.0} ms  " +
            $"one red line {g.Count(r => r.Predicted == 1),3}  failed {g.Count(r => r.Score is null)}  " +
            $"ramp10 {(rampBeats == 0 ? "-" : (rampHits / (double)rampBeats).ToString("0.000", inv))} ({rampHits}/{rampBeats})");
    }
    return 0;
}
Console.Error.WriteLine("usage: timing-eval select <songsDir> <out.tsv> [multi] [single] | run <songs.tsv> [algorithm] [set=..] [split=..]");
return 2;

static double NearestTracked(double[] sorted, double time)
{
    int i = Array.BinarySearch(sorted, time);
    if (i >= 0) return 0;
    i = ~i;
    double best = double.PositiveInfinity;
    if (i < sorted.Length) best = sorted[i] - time;
    if (i > 0) best = Math.Min(best, time - sorted[i - 1]);
    return best;
}

static double SignedTracked(double[] sorted, double time)
{
    int i = Array.BinarySearch(sorted, time);
    if (i >= 0) return 0;
    i = ~i;
    double after = i < sorted.Length ? sorted[i] - time : double.PositiveInfinity;
    double before = i > 0 ? sorted[i - 1] - time : double.NegativeInfinity;
    return after <= -before ? after : before;
}

static double Median(double[] values)
{
    if (values.Length == 0) return double.NaN;
    var sorted = values.Order().ToArray();
    return sorted[sorted.Length / 2];
}

/// <summary>Length of the reference section containing <paramref name="time"/>.</summary>
static double SectionMs(IReadOnlyList<TimingPoint> reds, double time, double end)
{
    int s = SectionOf(reds, time);
    return (s + 1 < reds.Count ? reds[s + 1].Time : end) - reds[s].Time;
}

static int SectionOf(IReadOnlyList<TimingPoint> reds, double time)
{
    int s = 0;
    while (s + 1 < reds.Count && reds[s + 1].Time <= time) s++;
    return s;
}

static string FindRepo()
{
    for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        if (File.Exists(Path.Combine(dir.FullName, "TimingAnalyzer.sln"))) return dir.FullName;
    return Directory.GetCurrentDirectory();
}
