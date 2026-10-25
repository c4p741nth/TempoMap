using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

namespace TimingAnalyzer.Editor;

/// <summary>Prepare the exact audio used for both the waveform and playback, without overwriting source files.</summary>
public sealed class AudioImport(string? ffmpeg = null, string? ffprobe = null)
{
    private const string QualityTag = "TEMPOMAP_VORBIS_QUALITY", SourceTag = "TEMPOMAP_SOURCE_SHA256";
    private static string Tool(string name) => File.Exists(Path.Combine(AppContext.BaseDirectory, name + ".exe"))
        ? Path.Combine(AppContext.BaseDirectory, name + ".exe") : name;
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

    public async Task<string> PrepareAsync(string source, CancellationToken cancellation = default)
    {
        source = Path.GetFullPath(source);
        var info = await ProbeAsync(source, cancellation).ConfigureAwait(false);
        if (info.IsQuality6 && Path.GetExtension(source).Equals(".ogg", StringComparison.OrdinalIgnoreCase)) return source;
        string hash;
        await using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, useAsync: true))
            hash = Convert.ToHexString(await SHA256.HashDataAsync(input, cancellation).ConfigureAwait(false));
        string directory = Path.GetDirectoryName(source)!, stem = Path.GetFileNameWithoutExtension(source);
        string Candidate(int index) => Path.Combine(directory, stem + (index == 0 ? "" : index == 1 ? ".q6" :
            ".q6." + index.ToString(CultureInfo.InvariantCulture)) + ".ogg");
        int first = Path.GetExtension(source).Equals(".ogg", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        int slot = first;
        for (; File.Exists(Candidate(slot)); slot++)
        {
            cancellation.ThrowIfCancellationRequested();
            try
            {
                var cached = await ProbeAsync(Candidate(slot), cancellation).ConfigureAwait(false);
                if (cached.IsQuality6 && cached.SourceHash == hash) return Candidate(slot);
            }
            catch (InvalidDataException) { /* An unrelated or broken sibling remains untouched. */ }
        }
        string temporary = Path.Combine(directory, ".tempomap-" + Guid.NewGuid().ToString("N") + ".ogg");
        try
        {
            await Run(encoder, ["-nostdin", "-hide_banner", "-loglevel", "error", "-n", "-i", source,
                "-map", "0:a:0", "-vn", "-c:a", "libvorbis", "-q:a", "6", "-metadata", QualityTag + "=6",
                "-metadata", SourceTag + "=" + hash, temporary], cancellation).ConfigureAwait(false);
            if (!(await ProbeAsync(temporary, cancellation).ConfigureAwait(false)).IsQuality6)
                throw new InvalidDataException("The converted audio is not Ogg Vorbis quality 6.");
            // Move only into an unused name; another import may have claimed the proposed name meanwhile.
            while (true)
            {
                cancellation.ThrowIfCancellationRequested();
                try { File.Move(temporary, Candidate(slot)); return Candidate(slot); }
                catch (IOException) when (File.Exists(Candidate(slot))) { slot++; }
            }
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
        catch (Win32Exception ex) { throw new IOException("Audio import needs ffmpeg and ffprobe beside Tempomap.exe or on PATH. " + ex.Message, ex); }
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
