using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using TimingAnalyzer.App;
using TimingAnalyzer.Audio;
using TimingAnalyzer.Core;
using TimingAnalyzer.Editor;

namespace TimingAnalyzer.Eval;

/// <summary>
/// The measurements behind docs/PHASE1-STATUS.md E43 and E44 (Auto-time on the waveform's edge and on a bar line, the beat
/// head's phase, halving a high tempo), each run over the songs of one or more evaluation lists:
/// <c>timing-eval edge-lag</c>, <c>phase</c>, <c>bar-start</c>, <c>octave-beat</c>, <c>normal-variants</c>. The ones that
/// use the beat model need Models/beatnet.onnx beside the exe (the build copies it) and cache its two heads under
/// <c>.eval-cache/heads</c>.
/// </summary>
public static class AutoTimeChecks
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static string F(FormattableString text) => text.ToString(Inv);

    private static List<EvalSong> Songs(IEnumerable<string> lists)
    {
        var seen = new HashSet<string>();
        var songs = new List<EvalSong>();
        foreach (string list in lists)
            foreach (var song in SongSelection.Read(list))
                if (File.Exists(song.Audio) && File.Exists(song.Reference) && seen.Add(song.Audio)) songs.Add(song);
        return songs;
    }

    private static double Median(IEnumerable<double> values)
    {
        var sorted = values.Order().ToList();
        return sorted.Count == 0 ? double.NaN : sorted[sorted.Count / 2];
    }

    private static double Wrap(double x, double period) => x - period * Math.Round(x / period);

    private static TimingPoint[] Reds(EvalSong song) => BeatGrid.Sort(OsuFile.Load(song.Reference).RedLines.Where(r => r.BeatLength is > 150 and < 2000));

    /// <summary>The beat model's heads for a song, from the cache or from the model (one session; guarded by a lock).</summary>
    private sealed class Heads(string cacheDir)
    {
        private readonly object gate = new();
        private BeatModel? model;
        private bool tried;

        public (float[] Beat, float[] Down)? Get(string audio)
        {
            string file = Path.Combine(cacheDir, "heads", EnvelopeCache.Key(audio) + ".heads");
            if (File.Exists(file))
            {
                using var reader = new BinaryReader(File.OpenRead(file));
                int n = reader.ReadInt32();
                var beat = new float[n]; var down = new float[n];
                for (int i = 0; i < n; i++) beat[i] = reader.ReadSingle();
                for (int i = 0; i < n; i++) down[i] = reader.ReadSingle();
                return (beat, down);
            }
            lock (gate)
            {
                if (!tried) { model = BeatModel.TryLoad(); tried = true; }
                if (model is null) return null;
                var decoded = AudioDecoder.Read(audio);
                var (b, d) = model.Activations(decoded.Mono, decoded.Rate);
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                using (var writer = new BinaryWriter(File.Create(file + ".tmp")))
                {
                    writer.Write(b.Length);
                    foreach (float v in b) writer.Write(v);
                    foreach (float v in d) writer.Write(v);
                }
                File.Move(file + ".tmp", file, overwrite: true);
                return (b, d);
            }
        }
    }

    /// <summary>
    /// timing-eval edge-lag: per song, how much later than the onset peak's convention the leading edge sits
    /// (<see cref="EdgeAlign.SongLag"/> and the same measure on the reference map's own grid), and where the map's
    /// lines sit against the peak and against the edge.
    /// </summary>
    public static int EdgeLag(IEnumerable<string> lists, string cacheDir)
    {
        var rows = new ConcurrentBag<(string Name, double Song, double Grid, double PeakToLine, double EdgeToLine)>();
        Parallel.ForEach(Songs(lists), new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2) }, song =>
        {
            try
            {
                var (envelope, duration) = EnvelopeCache.Get(song.Audio, cacheDir);
                var decoded = AudioDecoder.Read(song.Audio);
                var rise = EdgeAlign.LogRise(decoded.Mono, decoded.Rate);
                var reds = Reds(song);
                var edges = new List<double>(); var peaks = new List<double>(); var lines = new List<double>();
                for (int i = 0; i < reds.Length; i++)
                {
                    double a = reds[i].Time + 1500, b = i + 1 < reds.Length ? reds[i + 1].Time : duration, length = reds[i].BeatLength;
                    if (b - a < 8000) continue;
                    var beats = new List<(double Beat, double Peak, double Edge, double Strength)>();
                    // The map's own beats: the first one 1.5 s into the section, then every beat.
                    for (double t = reds[i].Time + Math.Ceiling(1500 / length) * length; t < b - length; t += length)
                    {
                        int lo = (int)Math.Round(t + MultiBpmAnalyzer.PeakToOsuMs - 40), hi = (int)Math.Round(t + MultiBpmAnalyzer.PeakToOsuMs + 40);
                        if (lo < 2 || hi >= envelope.Length - 2) continue;
                        int best = -1; double strength = 0;
                        for (int j = lo; j <= hi; j++) if (envelope[j] > strength && envelope[j] >= envelope[j - 1] && envelope[j] >= envelope[j + 1]) { strength = envelope[j]; best = j; }
                        if (best < 0) continue;
                        double peak = best - MultiBpmAnalyzer.PeakToOsuMs;
                        int el = (int)Math.Round(peak - 30), eh = (int)Math.Round(peak + 50);
                        if (el < 4 || eh >= rise.Length) continue;
                        int top = el;
                        for (int j = el; j <= eh; j++) if (rise[j] > rise[top]) top = j;
                        beats.Add((t, peak, top - 2.0, strength));
                    }
                    if (beats.Count < 12) continue;
                    double median = Median(beats.Select(x => x.Strength));
                    var strong = beats.Where(x => x.Strength >= median).ToList();
                    // Only sections whose grid sits on the hits.
                    if (strong.Count < 12 || Math.Abs(Median(strong.Select(x => x.Peak - x.Beat))) > 25) continue;
                    edges.Add(Median(strong.Select(x => x.Edge - x.Peak)));
                    peaks.Add(Median(strong.Select(x => x.Peak - x.Beat)));
                    lines.Add(Median(strong.Select(x => x.Edge - x.Beat)));
                }
                if (edges.Count == 0) return;
                double songLag = EdgeAlign.SongLag(envelope, rise) ?? double.NaN;
                rows.Add((song.Name, songLag, Median(edges), Median(peaks), Median(lines)));
            }
            catch (Exception ex) when (ex is IOException or FormatException or ArgumentException) { }
        });
        foreach (var r in rows.OrderBy(r => r.Grid))
            Console.WriteLine(F($"song lag {r.Song,5:0.0}  grid lag {r.Grid,5:0.0}  peak - line {r.PeakToLine,6:0.0}  edge - line {r.EdgeToLine,6:0.0}  {r.Name}"));
        if (rows.IsEmpty) return 1;
        var grid = rows.Select(r => r.Grid).Order().ToList();
        Console.WriteLine(F($"{rows.Count} songs: edge - peak median {Median(grid):0.0} ms (quartiles {grid[grid.Count / 4]:0.0} / {grid[grid.Count * 3 / 4]:0.0}, min {grid[0]:0.0}, max {grid[^1]:0.0}); ") +
            F($"SongLag differs from the grid measure by {rows.Where(r => !double.IsNaN(r.Song)).Average(r => Math.Abs(r.Song - r.Grid)):0.0} ms on average (a constant {Median(grid):0.0}: {rows.Average(r => Math.Abs(r.Grid - Median(grid))):0.0}); ") +
            F($"the maps' lines sit {-Median(rows.Select(r => r.PeakToLine)):0.0} ms after the onset peak and {Median(rows.Select(r => r.EdgeToLine)):0.0} ms before the edge"));
        return 0;
    }

    /// <summary>
    /// timing-eval phase: each section of 16+ beats of the reference maps, with the map's BPM given: the phase from the
    /// original's fold of the onset function and from the beat head folded at the same period, against the map's grid.
    /// </summary>
    public static int Phase(IEnumerable<string> lists, string cacheDir)
    {
        var heads = new Heads(cacheDir);
        var fold = new ConcurrentBag<double>(); var head = new ConcurrentBag<double>();
        Parallel.ForEach(Songs(lists), new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2) }, song =>
        {
            try
            {
                var (envelope, duration) = EnvelopeCache.Get(song.Audio, cacheDir);
                if (heads.Get(song.Audio) is not { } h) return;
                var reds = Reds(song);
                for (int i = 0; i < reds.Length; i++)
                {
                    double a = reds[i].Time + 1500, b = i + 1 < reds.Length ? reds[i + 1].Time : duration, length = reds[i].BeatLength;
                    if ((b - a) / length < 16) continue;
                    double truth = Wrap((reds[i].Time - a) % length, length);
                    int bins = (int)Math.Ceiling(length);
                    var onset = new double[bins];
                    for (double t = a; t < b - length; t += length)
                    {
                        int j0 = (int)Math.Round(t + MultiBpmAnalyzer.PeakToOsuMs - length / 2);
                        for (int j = 0; j < bins && j0 + j >= 0 && j0 + j < envelope.Length; j++) onset[j] += envelope[j0 + j];
                    }
                    double foldPhase = Array.IndexOf(onset, onset.Max()) - length / 2;
                    // The head folds on the beat itself: first beat at or after a, as an offset from a.
                    double headPhase = Wrap(BeatPhase.FirstBeat(h.Beat, length, a, b) is { } first ? first - a : double.NaN, length);
                    fold.Add(Wrap(foldPhase - truth, length));
                    if (!double.IsNaN(headPhase)) head.Add(Wrap(headPhase - truth, length));
                }
            }
            catch (Exception ex) when (ex is IOException or FormatException or ArgumentException) { }
        });
        foreach (var (name, errors) in new[] { ("original fold of the onset function", fold.ToList()), ("beat head folded (BeatPhase)", head.ToList()) })
        {
            if (errors.Count == 0) continue;
            var abs = errors.Select(Math.Abs).Order().ToList();
            Console.WriteLine(F($"{name,-38} n {abs.Count}  median error {Median(errors):+0.0;-0.0} ms  median |error| {abs[abs.Count / 2]:0.0}  ") +
                F($"within 10 / 25 / 50 ms: {abs.Count(x => x <= 10)} / {abs.Count(x => x <= 25)} / {abs.Count(x => x <= 50)} ({100.0 * abs.Count(x => x <= 10) / abs.Count:0} / {100.0 * abs.Count(x => x <= 25) / abs.Count:0} / {100.0 * abs.Count(x => x <= 50) / abs.Count:0} %)"));
        }
        return 0;
    }

    /// <summary>
    /// timing-eval bar-start: the Auto-time offset is simulated on each reference map's first section (its grid folded into
    /// [35 ms, 35 ms + a beat), as the original does) and moved by <see cref="BarStart.BeatsToBarStart"/>; the count of
    /// maps whose first red line it lands on, within 0.2 beat, against the offset left as it is.
    /// </summary>
    public static int BarStartCheck(IEnumerable<string> lists, string cacheDir)
    {
        var heads = new Heads(cacheDir);
        int total = 0, asIs = 0, moved = 0, phaseRight = 0;
        var gate = new object();
        Parallel.ForEach(Songs(lists), new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2) }, song =>
        {
            try
            {
                var reds = Reds(song);
                if (reds.Length == 0 || reds[0].Meter != 4) return;
                double length = reds[0].BeatLength, first = reds[0].Time;
                var (envelope, duration) = EnvelopeCache.Get(song.Audio, cacheDir);
                double end = Math.Min(reds.Length > 1 ? reds[1].Time : duration, duration);
                if ((end - first) / length < 24 || heads.Get(song.Audio) is not { } h) return;
                double auto = first - Math.Floor((first - 35) / length) * length;
                int beats = BarStart.BeatsToBarStart(envelope, h.Down, new TimingSection(auto, 60000 / length), end);
                lock (gate)
                {
                    total++;
                    if (Math.Abs(auto - first) <= 0.2 * length) asIs++;
                    if (Math.Abs(auto + beats * length - first) <= 0.2 * length) moved++;
                    if ((Math.Abs(beats) > 0 || Math.Abs(auto - first) <= 0.2 * length) && ((int)Math.Round((first - auto) / length) - beats) % 4 == 0) phaseRight++;
                }
            }
            catch (Exception ex) when (ex is IOException or FormatException or ArgumentException) { }
        });
        Console.WriteLine($"{total} maps with a 4/4 first section of 24+ beats: the offset as Auto-time left it is the map's first red line in {asIs}; after BarStart in {moved}; " +
            $"on the map's bar phase (a whole number of bars from its first red line) in {phaseRight}");
        return 0;
    }

    /// <summary>
    /// timing-eval octave-beat: for every one-tempo Normal answer of the lists, the beat head's alternate-beat ratio
    /// (<see cref="BeatPhase.AlternateBeatRatio"/>) next to the map's main BPM over that answer.
    /// </summary>
    public static int OctaveBeat(IEnumerable<string> lists, string cacheDir)
    {
        var heads = new Heads(cacheDir);
        var rows = new ConcurrentBag<(double Bpm, double Ratio, double Map, string Set, string Name)>();
        Parallel.ForEach(Songs(lists), new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2) }, song =>
        {
            try
            {
                var (envelope, duration) = EnvelopeCache.Get(song.Audio, cacheDir);
                var sections = MultiBpmAnalyzer.Analyze(envelope);
                if (sections.Count != 1 || heads.Get(song.Audio) is not { } h) return;
                var reds = Reds(song);
                if (reds.Length == 0) return;
                var main = reds.Select((p, i) => (p, Span: (i + 1 < reds.Length ? reds[i + 1].Time : duration) - p.Time)).MaxBy(x => x.Span).p;
                if (BeatPhase.AlternateBeatRatio(h.Beat, 60000 / sections[0].Bpm, 0, duration) is { } ratio)
                    rows.Add((sections[0].Bpm, ratio, main.Bpm / sections[0].Bpm, song.Set, song.Name));
            }
            catch (Exception ex) when (ex is IOException or FormatException or ArgumentException) { }
        });
        foreach (var r in rows.OrderBy(r => r.Ratio))
            Console.WriteLine(F($"{r.Bpm,9:0.000} BPM  alternate beat {r.Ratio,4:0.00}  map x{r.Map,6:0.000}  {r.Set,-2} {r.Name}"));
        Console.WriteLine($"{rows.Count} one-tempo answers");
        return 0;
    }

    /// <summary>
    /// timing-eval auto-time: the editor's Normal Auto-time as it ships (<see cref="CoreTiming.DetectMulti"/> with the edge lag,
    /// bar start, phase and halving on) against the original detector's way (all four off), per song: whether the first red
    /// line lands on the reference map's first red line (within 0.2 beat), and, where Normal gives the map's one BPM, where
    /// its line sits against the map's grid.
    /// </summary>
    public static int AutoTime(IEnumerable<string> lists)
    {
        var rows = new ConcurrentBag<(string Name, bool OldFirst, bool NewFirst, bool OneTempo, double OldPhase, double NewPhase)>();
        Parallel.ForEach(Songs(lists), new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2) }, song =>
        {
            try
            {
                var reds = Reds(song);
                if (reds.Length == 0) return;
                var decoded = AudioDecoder.Read(song.Audio);
                var audio = AudioContent.Create(decoded.Mono, (int)decoded.Rate);
                var core = new CoreTiming { AlignToEdge = false, StartOnBar = false, HalveByModel = false, PhaseByModel = false };
                var old = core.DetectMulti(audio);
                core.AlignToEdge = true; core.StartOnBar = true; core.HalveByModel = true; core.PhaseByModel = true;
                var now = core.DetectMulti(audio);
                if (old.Sections.Count == 0 || now.Sections.Count == 0) return;
                double lag = core.LagMs(audio), length = reds[0].BeatLength;
                var main = reds.Select((p, i) => (p, Span: (i + 1 < reds.Length ? reds[i + 1].Time : audio.DurationMs) - p.Time)).MaxBy(x => x.Span).p;
                bool oneTempo = now.Sections.Count == 1 && Math.Abs(now.Sections[0].Bpm / main.Bpm - 1) < 0.003;
                double Phase(double time) => Wrap(time - main.Time, main.BeatLength);
                rows.Add((song.Name, Math.Abs(old.Sections[0].Time - reds[0].Time) <= 0.2 * length, Math.Abs(now.Sections[0].Time - lag - reds[0].Time) <= 0.2 * length,
                    oneTempo, Phase(old.Sections[0].Time), Phase(now.Sections[0].Time)));
            }
            catch (Exception ex) when (ex is IOException or FormatException or ArgumentException or InvalidOperationException) { }
        });
        Console.WriteLine($"{rows.Count} songs: the first red line is within 0.2 beat of the map's first red line: the original's way {rows.Count(r => r.OldFirst)}, now {rows.Count(r => r.NewFirst)} (the edge lag taken off)");
        var one = rows.Where(r => r.OneTempo).ToList();
        if (one.Count > 0)
        {
            var oldPhase = one.Select(r => r.OldPhase).Order().ToList(); var newPhase = one.Select(r => r.NewPhase).Order().ToList();
            Console.WriteLine(F($"{one.Count} songs where Normal gives the map's one BPM, the line against the map's grid (ms): original's way median {Median(oldPhase):+0.0;-0.0} (quartiles {oldPhase[oldPhase.Count / 4]:+0.0;-0.0} / {oldPhase[oldPhase.Count * 3 / 4]:+0.0;-0.0}), now {Median(newPhase):+0.0;-0.0} ({newPhase[newPhase.Count / 4]:+0.0;-0.0} / {newPhase[newPhase.Count * 3 / 4]:+0.0;-0.0})"));
        }
        return 0;
    }

    /// <summary>
    /// timing-eval normal-variants: the editor's Normal Auto-time (<see cref="CoreTiming.DetectMulti"/>, beat model rule
    /// included) without the edge lag and bar start, with and without the beat head's phase and the halving, scored as
    /// <c>run</c> does (F10 and aligned F10 per set and split, then the songs that changed).
    /// </summary>
    public static int NormalVariants(IEnumerable<string> lists)
    {
        var variants = new (string Name, bool Halve, bool Phase)[] { ("baseline", false, false), ("+halve", true, false), ("+phase", false, true), ("both", true, true) };
        var rows = new ConcurrentBag<(EvalSong Song, double[] F10, double[] Aligned, int[] Lines)>();
        Parallel.ForEach(Songs(lists), new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2) }, song =>
        {
            try
            {
                var reference = BeatGrid.Sort(OsuFile.Load(song.Reference).RedLines);
                var decoded = AudioDecoder.Read(song.Audio);
                var audio = AudioContent.Create(decoded.Mono, (int)decoded.Rate);
                var core = new CoreTiming { AlignToEdge = false, StartOnBar = false };
                var f10 = new double[variants.Length]; var aligned = new double[variants.Length]; var lines = new int[variants.Length];
                for (int v = 0; v < variants.Length; v++)
                {
                    core.HalveByModel = variants[v].Halve; core.PhaseByModel = variants[v].Phase;
                    var detection = core.DetectMulti(audio);
                    if (detection.Sections.Count == 0) continue;
                    var score = GridAgreement.Score(reference, BeatGrid.Sort(detection.Sections), audio.DurationMs);
                    f10[v] = score.F10; aligned[v] = score.AlignedF10; lines[v] = detection.Sections.Count;
                }
                rows.Add((song, f10, aligned, lines));
            }
            catch (Exception ex) when (ex is IOException or FormatException or ArgumentException or InvalidOperationException) { }
        });
        Console.WriteLine("set split   n   " + string.Join("   ", variants.Select(v => v.Name.PadRight(22))));
        foreach (var g in rows.GroupBy(r => (r.Song.Set, r.Song.Split)).OrderBy(g => g.Key.Set).ThenBy(g => g.Key.Split))
            Console.WriteLine(F($"{g.Key.Set,-3} {g.Key.Split,-5} {g.Count(),3}   ") +
                string.Join("   ", variants.Select((v, i) => F($"F10 {g.Average(r => r.F10[i]):0.000} aligned {g.Average(r => r.Aligned[i]):0.000}"))));
        for (int v = 1; v < variants.Length; v++)
        {
            var changed = rows.Where(r => Math.Abs(r.Aligned[v] - r.Aligned[0]) > 1e-9 || r.Lines[v] != r.Lines[0]).OrderBy(r => r.Aligned[v] - r.Aligned[0]).ToList();
            Console.WriteLine(F($"{variants[v].Name}: {changed.Count} songs changed, {changed.Count(r => r.Aligned[v] > r.Aligned[0] + 1e-9)} better, {changed.Count(r => r.Aligned[v] < r.Aligned[0] - 1e-9)} worse"));
            foreach (var r in changed.Take(5).Concat(changed.TakeLast(5)).Distinct())
                Console.WriteLine(F($"     {r.Song.Set} {r.Song.Split} {r.Aligned[0]:0.000} -> {r.Aligned[v]:0.000}  {r.Song.Name}"));
        }
        return 0;
    }
}
