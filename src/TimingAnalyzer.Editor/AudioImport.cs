using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

namespace TimingAnalyzer.Editor;

/// <summary>Prepare the exact audio used for both the waveform and playback, without overwriting source files.</summary>
public sealed class AudioImport(string? ffmpeg = null, string? ffprobe = null)
{
    public sealed record ConversionOptions(int? SampleRate = null, double Quality = 6)
    {
        public void Validate()
        {
            if (SampleRate is < 8000 or > 192000 || !double.IsFinite(Quality) || Quality is < -1 or > 10)
                throw new ArgumentException("Sample rate must be 8000–192000 Hz (or Original); Vorbis quality must be -1–10.");
        }
    }
    public sealed record ToolStatus(string Encoder, bool EncoderAvailable, string Probe, bool ProbeAvailable);
    private const string QualityTag = "TEMPOMAP_VORBIS_QUALITY", SourceTag = "TEMPOMAP_SOURCE_SHA256";
    private static string Tool(string name)
    {
        string filename = name + ".exe", adjacent = Path.Combine(AppContext.BaseDirectory, filename);
        if (File.Exists(adjacent)) return adjacent;
        return (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
            .Select(directory => Path.Combine(directory.Trim('"'), filename)).FirstOrDefault(File.Exists) ?? name;
    }
    private readonly string encoder = ffmpeg ?? Tool("ffmpeg"), probe = ffprobe ?? Tool("ffprobe");

    public sealed record AudioInfo(bool IsOgg, string Codec, int Channels, long NominalBitrate, string? Quality, string? SourceHash)
    {
        // Vorbis is VBR: the container's average bitrate is not its encoding quality. Audacity q6 stereo writes
        // nominal 192000 (mono 110000); our own tag identifies q6 at other channel counts and sample rates.
        public bool IsQuality6 => IsOgg && Codec == "vorbis" &&
            (Quality == "6" || Quality is null && (Channels == 2 && NominalBitrate == 192000 || Channels == 1 && NominalBitrate == 110000));
    }

    public static AudioInfo ParseProbe(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var stream = root.GetProperty("streams").EnumerateArray().FirstOrDefault(s =>
            s.TryGetProperty("codec_type", out var type) && type.GetString() == "audio");
        if (stream.ValueKind == JsonValueKind.Undefined) throw new InvalidDataException("No audio stream found.");
        string? Field(string key) => stream.TryGetProperty(key, out var value) ? value.ToString() : null;
        string? Tag(string key) => stream.TryGetProperty("tags", out var tags) ? tags.EnumerateObject()
            .Where(p => p.Name.Equals(key, StringComparison.OrdinalIgnoreCase)).Select(p => p.Value.GetString()).FirstOrDefault() : null;
        bool ogg = root.TryGetProperty("format", out var format) && format.TryGetProperty("format_name", out var name) &&
            name.GetString()?.Split(',').Contains("ogg") == true;
        return new(ogg, Field("codec_name") ?? "", int.TryParse(Field("channels"), CultureInfo.InvariantCulture, out int channels) ? channels : 0,
            long.TryParse(Field("bit_rate"), CultureInfo.InvariantCulture, out long bitrate) ? bitrate : 0, Tag(QualityTag), Tag(SourceTag));
    }

    private async Task<AudioInfo> ProbeAsync(string path, CancellationToken cancellation) => ParseProbe(await Run(probe,
        ["-v", "error", "-show_streams", "-show_format", "-of", "json", path], cancellation).ConfigureAwait(false));

    /// <summary>Opening always uses the original file; conversion is an explicit separate command.</summary>
    public Task<string> PrepareAsync(string source, CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested();
        source = Path.GetFullPath(source);
        if (!File.Exists(source)) throw new FileNotFoundException("Audio file not found.", source);
        return Task.FromResult(source);
    }

    public ToolStatus Diagnose() => new(encoder, Available(encoder), probe, Available(probe));
    private static bool Available(string executable)
    {
        if (File.Exists(executable)) return true;
        string filename = Path.HasExtension(executable) ? executable : executable + ".exe";
        return (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
            .Any(directory => File.Exists(Path.Combine(directory.Trim('"'), filename)));
    }

    public async Task<string> ConvertAsync(string source, string destination, ConversionOptions? options = null,
        CancellationToken cancellation = default)
    {
        options ??= new(); options.Validate();
        source = Path.GetFullPath(source); destination = Path.GetFullPath(destination);
        if (source.Equals(destination, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Choose a different output file; conversion never overwrites the source audio.");
        if (!Path.GetExtension(destination).Equals(".ogg", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Choose an .ogg output filename.");
        string hash;
        await using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, useAsync: true))
            hash = Convert.ToHexString(await SHA256.HashDataAsync(input, cancellation).ConfigureAwait(false));
        string quality = options.Quality.ToString("0.##", CultureInfo.InvariantCulture);
        string temporary = FileSaveSession.TemporaryPath(destination) + ".ogg";
        try
        {
            List<string> arguments = ["-nostdin", "-hide_banner", "-loglevel", "error", "-n", "-i", source,
                "-map", "0:a:0", "-vn", "-c:a", "libvorbis", "-q:a", quality];
            if (options.SampleRate is { } rate) arguments.AddRange(["-ar", rate.ToString(CultureInfo.InvariantCulture)]);
            arguments.AddRange(["-metadata", QualityTag + "=" + quality, "-metadata", SourceTag + "=" + hash, temporary]);
            await Run(encoder, arguments.ToArray(), cancellation).ConfigureAwait(false);
            cancellation.ThrowIfCancellationRequested();
            using (var converted = new FileStream(temporary, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                if (converted.Length == 0) throw new InvalidDataException("The converter produced an empty file.");
                converted.Flush(flushToDisk: true);
            }
            new FileSaveSession().CommitPrepared(temporary, destination);
            return destination;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static async Task<string> Run(string executable, string[] arguments, CancellationToken cancellation)
    {
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        using var process = new Process { StartInfo = start };
        try { process.Start(); }
        catch (Win32Exception ex) { throw new IOException("Ogg conversion needs ffmpeg beside TempoMap.exe or on PATH. Opening the original audio does not need it. " + ex.Message, ex); }
        var output = process.StandardOutput.ReadToEndAsync(cancellation);
        var errors = process.StandardError.ReadToEndAsync(cancellation);
        try { await process.WaitForExitAsync(cancellation).ConfigureAwait(false); }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        string error = await errors.ConfigureAwait(false);
        string result = await output.ConfigureAwait(false);
        if (process.ExitCode != 0) throw new InvalidDataException(Path.GetFileName(executable) + ": " + error.Trim());
        return result;
    }
}
