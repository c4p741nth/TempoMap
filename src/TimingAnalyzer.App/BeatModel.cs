using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using TimingAnalyzer.Core;
using TimingAnalyzer.Editor;

namespace TimingAnalyzer.App;

/// <summary>
/// The beat model (docs/PHASE4-PLAN.md): Models/beatnet.onnx beside the exe, run on the CPU with ONNX Runtime.
/// Input: the log-mel frames of <see cref="BeatMel"/>; output: per-frame beat probability (100 fps). Long songs run
/// in overlapping 60 s chunks so memory stays small; the overlap (10 s each side) covers the network's context.
/// </summary>
public sealed class BeatModel : IDisposable
{
    private const int ChunkFrames = 6000, Overlap = 1000;
    private readonly InferenceSession session;

    private BeatModel(string path)
    {
        // No CPU memory arena: it kept ~330 MB of activation buffers after a 5-minute song; without it each chunk's
        // buffers are freed after its run (same outputs, about the same speed).
        using var options = new SessionOptions { IntraOpNumThreads = Math.Max(1, Environment.ProcessorCount / 2), EnableCpuMemArena = false };
        // Idle worker threads sleep instead of spinning, so the UI thread keeps its frames while the model runs.
        options.AddSessionConfigEntry("session.intra_op.allow_spinning", "0");
        session = new InferenceSession(path, options);
    }

    /// <summary>The model next to the exe, or null when the file is missing (Auto-time then uses the analyzer alone).</summary>
    public static BeatModel? TryLoad()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Models", "beatnet.onnx");
        if (!File.Exists(path)) return null;
        try { return new BeatModel(path); }
        catch (OnnxRuntimeException) { return null; }
    }

    /// <summary>Per-frame beat probability for a song (mono at <paramref name="rate"/>).</summary>
    public float[] BeatProbability(double[] mono, double rate) => Activations(mono, rate).Beat;

    /// <summary>
    /// Both heads of the model, per 10 ms frame: the beat probability and the downbeat probability (where mappers start
    /// a bar; <see cref="BarStart"/> uses it to put the first red line on a bar's first beat).
    /// </summary>
    public (float[] Beat, float[] Downbeat) Activations(double[] mono, double rate, CancellationToken cancellationToken = default, IProgress<AnalysisProgress>? progress = null)
    {
        var mel = BeatMel.Compute(BeatMel.Resample(mono, rate), out int frames);
        var beat = new float[frames];
        var downbeat = new float[frames];
        for (int start = 0; start < frames; start += ChunkFrames)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new("Running beat model", 100.0 * start / Math.Max(1, frames)));
            int from = Math.Max(0, start - Overlap), to = Math.Min(frames, start + ChunkFrames + Overlap);
            var input = new DenseTensor<float>(new[] { 1, to - from, BeatMel.Mels });
            mel.AsSpan(from * BeatMel.Mels, (to - from) * BeatMel.Mels).CopyTo(input.Buffer.Span);
            using var results = session.Run([NamedOnnxValue.CreateFromTensor("mel", input)]);
            var logits = results[0].AsTensor<float>();
            for (int t = start; t < Math.Min(frames, start + ChunkFrames); t++)
            {
                beat[t] = 1f / (1f + MathF.Exp(-logits[0, t - from, 0]));
                downbeat[t] = 1f / (1f + MathF.Exp(-logits[0, t - from, 1]));
            }
        }
        return (beat, downbeat);
    }

    public void Dispose() => session.Dispose();
}
