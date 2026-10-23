namespace TimingAnalyzer.Core;

/// <summary>
/// How much played drums push and drag, as a hint for drift mode's red line on every beat. Around each tracked beat
/// the sharpest attack of the 1-4 kHz band (steepest 2 ms rise of its energy, the louder after it the better), and
/// over three tracked beats in a row |a_k − (a_{k−1} + a_{k+1}) / 2|, median over the song. In the 2026-10-18 probe
/// (docs/PHASE3-STATUS.md, "Live-drum check") Billie Jean gave 5.1 ms and sequenced songs 0.1-0.8 ms, two of them up to
/// 2.7. Only a hint: dense rock mixes, piano and violin give too few clean attacks to measure at all.
/// </summary>
public static class DrumWobble
{
    /// <summary>A song is worth the hint from this median wobble over at least <see cref="MinTriples"/> triples.</summary>
    public const double HintMs = 4;
    public const int MinTriples = 20;

    /// <param name="MedianMs">Median wobble.</param>
    /// <param name="Triples">Runs of three tracked beats, each with a clear attack, that were measured.</param>
    public sealed record Result(double MedianMs, int Triples)
    {
        public bool SuggestsLiveDrums => Triples >= MinTriples && MedianMs >= HintMs;
    }

    /// <summary>Tracked beats are osu! times; the attack is looked for 22 ms later (the trackers' lead on a drum
    /// attack, +21.5 to +23.2 ms on five ranked maps), within ±25 ms.</summary>
    public static Result? Measure(double[] mono, double rate, IReadOnlyList<TrackedBeat> beats)
    {
        var kept = beats.Where(b => b.Weight > 0).Select(b => b.TimeMs).ToArray();
        var scratch = new Scratch();
        var attack = kept.Select(t => Attack(mono, rate, t + 22, scratch)).ToArray();
        var wobble = new List<double>();
        for (int k = 1; k + 1 < kept.Length; k++)
        {
            if (attack[k - 1] is not { } a || attack[k] is not { } b || attack[k + 1] is not { } c) continue;
            // A tempo change or a skipped beat is not a wobble.
            if (Math.Abs((kept[k + 1] - kept[k]) / (kept[k] - kept[k - 1]) - 1) > 0.1) continue;
            wobble.Add(Math.Abs(b - (a + c) / 2));
        }
        if (wobble.Count == 0) return null;
        wobble.Sort();
        double median = wobble.Count % 2 == 1 ? wobble[wobble.Count / 2] : (wobble[wobble.Count / 2 - 1] + wobble[wobble.Count / 2]) / 2;
        return new(median, wobble.Count);
    }

    private const double Half = 25, MinRiseDb = 12, Pad = 100;

    /// <summary>The attack (ms) nearest <paramref name="centreMs"/>, or null without a clear one.</summary>
    internal static double? Attack(double[] mono, double rate, double centreMs) => Attack(mono, rate, centreMs, new Scratch());

    /// <summary>Buffers reused from beat to beat: a 250 ms window is just over the large object heap's 85 KB, so fresh
    /// arrays per beat were ~0.5 GB of large-object garbage on a 5-minute song.</summary>
    private sealed class Scratch
    {
        private double[] y = [], z = [], sum = [], db = [];
        private static double[] Fit(ref double[] a, int n) => a.Length >= n ? a : a = new double[n];
        public double[] Y(int n) => Fit(ref y, n);
        public double[] Z(int n) => Fit(ref z, n);
        public double[] Sum(int n) => Fit(ref sum, n);
        public double[] Db(int n) => Fit(ref db, n);
    }

    private static double? Attack(double[] mono, double rate, double centreMs, Scratch s)
    {
        // A zero-phase 1-4 kHz band of the audio around the beat, padded so the filters settle before the window.
        int from = (int)Math.Floor((centreMs - Half - Pad) * rate / 1000), to = (int)Math.Ceiling((centreMs + Half + Pad) * rate / 1000);
        if (from < 0 || to > mono.Length) return null;
        int length = to - from;
        var y = s.Y(length);
        Array.Copy(mono, from, y, 0, length);
        foreach (var f in BandFilters(rate)) { f.Run(y, length); Array.Reverse(y, 0, length); f.Run(y, length); Array.Reverse(y, 0, length); }
        for (int i = 0; i < length; i++) y[i] *= y[i];
        // Energy smoothed by two 2 ms moving averages (a 4 ms triangle), in dB every 0.1 ms.
        int w = Math.Max(1, (int)(rate * 0.002));
        var z = s.Z(length);
        Centred(y, z, s.Sum(length + 1), length, w);
        Centred(z, y, s.Sum(length + 1), length, w);
        int n = (int)((to - from) * 10000.0 / rate);
        var db = s.Db(n);
        for (int i = 0; i < n; i++) db[i] = 10 * Math.Log10(y[Math.Min(length - 1, (int)Math.Round(i * rate / 10000))] + 1e-12);
        // Candidates: 2 ms rises of at least 12 dB peaking in the window; the one followed by the loudest 15 ms wins.
        double best = double.NegativeInfinity; int at = -1;
        int lo = (int)(Pad * 10), hi = (int)((Pad + 2 * Half) * 10);
        for (int i = Math.Max(lo, 21); i < Math.Min(hi, n - 151); i++)
        {
            double rise = db[i] - db[i - 20];
            if (rise < MinRiseDb || rise < db[i - 1] - db[i - 21] || rise < db[i + 1] - db[i - 19]) continue;
            double level = double.NegativeInfinity;
            for (int j = i; j < i + 150; j++) level = Math.Max(level, db[j]);
            if (level + 0.5 * rise > best) { best = level + 0.5 * rise; at = i; }
        }
        return at < 0 ? null : from * 1000 / rate + at / 10.0 - 1;
    }

    /// <summary>The first <paramref name="length"/> values of <paramref name="x"/> under a centred moving average of
    /// <paramref name="w"/>, into <paramref name="y"/> (<paramref name="sum"/>: length + 1 of scratch).</summary>
    private static void Centred(double[] x, double[] y, double[] sum, int length, int w)
    {
        sum[0] = 0;
        for (int i = 0; i < length; i++) sum[i + 1] = sum[i] + x[i];
        for (int i = 0; i < length; i++)
        {
            int a = Math.Max(0, i - w / 2), b = Math.Min(length, a + w);
            y[i] = (sum[b] - sum[a]) / (b - a);
        }
    }

    private sealed class Section(double b0, double b1, double b2, double a1, double a2)
    {
        public void Run(double[] x, int length)
        {
            double z1 = 0, z2 = 0;
            for (int i = 0; i < length; i++)
            {
                double v = x[i], o = b0 * v + z1;
                z1 = b1 * v - a1 * o + z2;
                z2 = b2 * v - a2 * o;
                x[i] = o;
            }
        }
    }

    /// <summary>Two Butterworth (Q 0.7071) high-pass sections at 1 kHz and two low-pass at 4 kHz (RBJ cookbook).</summary>
    private static Section[] BandFilters(double rate)
    {
        Section Make(double f, bool high)
        {
            double w0 = 2 * Math.PI * f / rate, alpha = Math.Sin(w0) / (2 * Math.Sqrt(0.5)), cos = Math.Cos(w0), a0 = 1 + alpha;
            double b0 = high ? (1 + cos) / 2 : (1 - cos) / 2, b1 = high ? -(1 + cos) : 1 - cos;
            return new(b0 / a0, b1 / a0, b0 / a0, -2 * cos / a0, (1 - alpha) / a0);
        }
        return [Make(1000, true), Make(1000, true), Make(4000, false), Make(4000, false)];
    }
}
