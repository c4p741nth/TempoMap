using System.Security.Cryptography;
using System.Text;
using TimingAnalyzer.Audio;
using TimingAnalyzer.Core;

namespace TimingAnalyzer.Eval;

/// <summary>Onset envelope (1 sample/ms) per audio file, cached on disk so tuning runs skip decoding.</summary>
public static class EnvelopeCache
{
    /// <summary>Cache file name stem of an audio file: path, size and write time.</summary>
    public static string Key(string audio)
    {
        var info = new FileInfo(audio);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            info.FullName + "|" + info.Length + "|" + info.LastWriteTimeUtc.Ticks)))[..24];
    }

    /// <summary>
    /// Outside beat times (ms of the decoded audio) written by tools/beat_probe.py as &lt;key&gt;.beats (one beat per
    /// line, seconds first); null when the file is missing.
    /// </summary>
    public static double[]? ExternalBeats(string audio, string cacheDir)
    {
        string file = Path.Combine(cacheDir, Key(audio) + ".beats");
        if (!File.Exists(file)) return null;
        return File.ReadLines(file).Where(l => l.Trim().Length > 0)
            .Select(l => double.Parse(l.Split('	', ' ')[0], System.Globalization.CultureInfo.InvariantCulture) * 1000).ToArray();
    }

    public static (double[] Envelope, double DurationMs) Get(string audio, string cacheDir)
    {
        string file = Path.Combine(cacheDir, Key(audio) + ".env");
        if (File.Exists(file))
        {
            using var reader = new BinaryReader(File.OpenRead(file));
            double duration = reader.ReadDouble();
            var d = new double[reader.ReadInt32()];
            for (int i = 0; i < d.Length; i++) d[i] = reader.ReadDouble();
            return (d, duration);
        }
        var decoded = AudioDecoder.Read(audio);
        var envelope = OnsetEnvelope.Compute(decoded.Mono, decoded.Rate);
        double durationMs = decoded.Mono.Length * 1000.0 / decoded.Rate;
        Directory.CreateDirectory(cacheDir);
        string temp = file + ".tmp";
        using (var writer = new BinaryWriter(File.Create(temp)))
        {
            writer.Write(durationMs); writer.Write(envelope.Length);
            foreach (double v in envelope) writer.Write(v);
        }
        File.Move(temp, file, overwrite: true);
        return (envelope, durationMs);
    }
}
