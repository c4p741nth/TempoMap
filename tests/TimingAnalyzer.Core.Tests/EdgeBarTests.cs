using TimingAnalyzer.Core;

namespace TimingAnalyzer.Core.Tests;

/// <summary>Auto-time on the waveform's leading edge (<see cref="EdgeAlign"/>) and on a bar's first beat (<see cref="BarStart"/>), docs/PHASE1-STATUS.md E43.</summary>
public class EdgeBarTests
{
    private const int Rate = 44100;

    /// <summary>A kick per beat: an 80 Hz sine decaying over 60 ms that starts on one exact sample (a hit from digital silence), over faint noise.</summary>
    private static double[] Kicks(double firstMs, double bpm, double lengthMs, double noise = 1e-4)
    {
        var x = new double[(int)(lengthMs * Rate / 1000)];
        var random = new Random(3);
        for (int i = 0; i < x.Length; i++) x[i] = (random.NextDouble() - 0.5) * 2 * noise;
        double beat = 60000 / bpm;
        for (double t = firstMs; t < lengthMs - 200; t += beat)
        {
            int start = (int)Math.Round(t * Rate / 1000);
            for (int i = 0; i < Rate / 5 && start + i < x.Length; i++)
            {
                double s = i / (double)Rate;
                x[start + i] += 0.8 * Math.Sin(2 * Math.PI * 80 * s) * Math.Exp(-s / 0.06);
            }
        }
        return x;
    }

    /// <summary>x folded into [-period/2, period/2).</summary>
    private static double Wrap(double x, double period) => x - period * Math.Round(x / period);

    [Fact]
    public void TheDetectorsOffsetSitsBeforeASharpHitAndTheMeasuredLagPutsItOnTheEdge()
    {
        const double first = 437;
        var audio = Kicks(first, 120, 40000);
        var envelope = OnsetEnvelope.Compute(audio, Rate);
        var detected = Analyzer.AnalyzeEnvelope(envelope);
        Assert.Equal(120, detected.Bpm!.Value, 1);
        // The original's offset is on the onset peak's convention: 10-25 ms before the first sample of a sharp hit.
        Assert.InRange(Wrap(detected.Offset!.Value - first, 500), -26, -8);
        double lag = EdgeAlign.SongLag(envelope, EdgeAlign.LogRise(audio, Rate)) ?? double.NaN;
        Assert.InRange(lag, 8, 26);
        Assert.InRange(Wrap(detected.Offset.Value + lag - first, 500), -3, 3);
    }

    [Fact]
    public void TheEdgeIsFoundWhetherTheHitIsLoudOrQuietAndOnAFadeInItIsAboutTheOnsetPeak()
    {
        // Half the hits 20 dB quieter: the log rise treats them alike, the median stays on the loud ones' edge.
        var audio = Kicks(437, 120, 40000);
        for (int beat = 0; beat < 79; beat += 2)
            for (int i = (int)(437 * Rate / 1000.0) + beat * Rate / 4 - 10; i < (int)(437 * Rate / 1000.0) + beat * Rate / 4 + Rate / 5; i++)
                if (i >= 0 && i < audio.Length) audio[i] *= 0.1;
        var envelope = OnsetEnvelope.Compute(audio, Rate);
        double lag = EdgeAlign.SongLag(envelope, EdgeAlign.LogRise(audio, Rate)) ?? double.NaN;
        Assert.InRange(lag, 8, 26);
    }

    [Fact]
    public void ALagOfNothingFallsBackToTheDefaultAndIsClamped()
    {
        Assert.Equal(EdgeAlign.DefaultLagMs, EdgeAlign.Clamp(null));
        Assert.Equal(EdgeAlign.MaxLagMs, EdgeAlign.Clamp(80));
        Assert.Equal(EdgeAlign.MinLagMs, EdgeAlign.Clamp(-5));
        Assert.Equal(12.5, EdgeAlign.Clamp(12.5));
        // Silence has no hits to measure.
        var silent = new double[Rate * 10];
        Assert.Null(EdgeAlign.SongLag(OnsetEnvelope.Compute(silent, Rate), EdgeAlign.LogRise(silent, Rate)));
    }

    // ---- BarStart ----

    /// <summary>An onset envelope with a peak at every beat of a 120 BPM grid from <paramref name="firstMs"/> (index = osu! time + 28.7).</summary>
    private static double[] Beats120(double firstMs, double lengthMs)
    {
        var d = new double[(int)lengthMs];
        for (double t = firstMs; t < lengthMs - 100; t += 500)
        {
            int centre = (int)Math.Round(t + MultiBpmAnalyzer.PeakToOsuMs);
            for (int i = centre - 6; i <= centre + 6; i++) if (i >= 0 && i < d.Length) d[i] += 8 * Math.Exp(-0.5 * Math.Pow((i - centre) / 2.0, 2));
        }
        return d;
    }

    /// <summary>A downbeat activation (100 fps) with a peak on every 4th beat of the 500 ms grid through <paramref name="barStartMs"/>.</summary>
    private static float[] Downbeats(double barStartMs, double lengthMs, float height = 0.9f)
    {
        var f = new float[(int)(lengthMs / 10)];
        for (double t = barStartMs; t < lengthMs - 100; t += 2000)
            for (int i = -1; i <= 1; i++) { int frame = (int)Math.Round(t / 10) + i; if (frame >= 0 && frame < f.Length) f[frame] = Math.Max(f[frame], i == 0 ? height : height / 2); }
        return f;
    }

    [Theory]
    [InlineData(1500, 3)] // The bars start at 1500 ms (beat 3 from the line at 0): +3 beats.
    [InlineData(2000, 4)]
    [InlineData(1000, 2)] // Where the music starts.
    [InlineData(500, 5)]  // A bar start before the music: the next one after it, beat 5.
    public void TheFirstLineMovesToTheEarliestSupportedBeatOnTheDownbeatPhase(double barStartMs, int expected)
    {
        // Music starts at 1000 ms (beat 2); the red line sits at 0 ms, two beats before it.
        var envelope = Beats120(1000, 60000);
        var downbeat = Downbeats(barStartMs, 60000);
        Assert.Equal(expected, BarStart.BeatsToBarStart(envelope, downbeat, new TimingSection(0, 120), 60000));
    }

    [Fact]
    public void AMusicalLineBeforeTheFirstSupportedBeatMovesBackOntoABarStartThatHasMusic()
    {
        // The detector's first beat after 35 ms was beat 2 of the bar (at 500 ms); the music and the bar start at 0.
        var envelope = Beats120(0, 60000);
        var downbeat = Downbeats(0, 60000);
        int moved = BarStart.BeatsToBarStart(envelope, downbeat, new TimingSection(500, 120), 60000);
        Assert.Equal(-1, moved);
        Assert.Equal(0, BarStart.Move(new TimingSection(500, 120), moved).TimeMs, 6);
    }

    [Fact]
    public void ALineAlreadyOnABarStartShortSectionsAndAMuteHeadAreLeftAlone()
    {
        var envelope = Beats120(0, 60000);
        Assert.Equal(0, BarStart.BeatsToBarStart(envelope, Downbeats(0, 60000), new TimingSection(0, 120), 60000));
        Assert.Equal(0, BarStart.BeatsToBarStart(envelope, Downbeats(1500, 60000), new TimingSection(0, 120), 7000)); // 14 beats: too short
        Assert.Equal(0, BarStart.BeatsToBarStart(envelope, new float[6000], new TimingSection(0, 120), 60000)); // no downbeat evidence
        Assert.Equal(0, BarStart.BeatsToBarStart(new double[60000], Downbeats(0, 60000), new TimingSection(0, 120), 60000)); // no onsets
    }
}
