using System.Globalization;
using ManagedBass;

namespace TimingAnalyzer.Audio;

public sealed record DecodedAudio(double[] Mono, double Rate);

public static class AudioDecoder
{
    private static readonly object Gate = new();

    public static DecodedAudio Read(string path)
    {
        // BASS device initialization is process-global. Serialize ownership of device 0.
        lock (Gate)
        {
            if (!Bass.Init(0, 44100)) throw BassError("initialize decoder");
            int stream = 0;
            try
            {
                stream = Bass.CreateStream(path, 0, 0, BassFlags.Decode | BassFlags.Prescan | BassFlags.Float);
                if (stream == 0) throw BassError("read audio file");
                if (!Bass.ChannelGetInfo(stream, out var info)) throw BassError("read channel info");
                long bytes = Bass.ChannelGetLength(stream);
                if (bytes < 0) throw BassError("read audio length");
                int channels = info.Channels;
                int frames = checked((int)(bytes / (sizeof(float) * channels)));
                var mono = new double[frames];
                var buffer = new float[16384 * channels];
                int written = 0;
                while (true)
                {
                    int read = Bass.ChannelGetData(stream, buffer, buffer.Length * sizeof(float));
                    if (read < 0)
                    {
                        if (Bass.LastError == Errors.Ended) break;
                        throw BassError("decode audio");
                    }
                    if (read == 0) break;
                    if (read % (sizeof(float) * channels) != 0) throw new IOException("Incomplete decoded audio frame.");
                    int count = read / (sizeof(float) * channels);
                    if (written + count > mono.Length) Array.Resize(ref mono, Math.Max(written + count, mono.Length * 2));
                    for (int i = 0; i < count; i++)
                    {
                        double sum = 0;
                        for (int ch = 0; ch < channels; ch++) sum += buffer[i * channels + ch];
                        mono[written++] = sum;
                    }
                }
                Array.Resize(ref mono, written);
                // BASS is the original decoder: do not apply the Python mpg123 delay shim.
                return new(mono, info.Frequency);
            }
            finally
            {
                if (stream != 0) Bass.StreamFree(stream);
                Bass.Free();
            }
        }
    }

    private static IOException BassError(string action)
    {
        var error = Bass.LastError;
        string hint = (int)error == 41 ? " This codec is unavailable in the native decoder. File → Convert audio to Ogg can create a separate compatible file using ffmpeg." : "";
        return new("Cannot " + action + " (BASS error " + ((int)error).ToString(CultureInfo.InvariantCulture) + ")." + hint);
    }
}
