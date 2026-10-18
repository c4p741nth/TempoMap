using System.Globalization;
using System.Runtime.InteropServices;
using ManagedBass;
using ManagedBass.Fx;
using ManagedBass.Mix;
using TimingAnalyzer.Editor;

namespace TimingAnalyzer.Audio;

/// <summary>One output owner. The mixer combines song and clicks before pitch-preserving tempo processing.</summary>
public sealed class PlaybackEngine : IPlaybackEngine
{
    private readonly string path;
    private readonly int device;
    private readonly double latencyMs;
    private readonly StreamProcedure clickProcedure;
    private TimingPoint[] reds = [];
    private int song, click, mixer, tempo, rate;
    private long clickFrame, endFrame;
    private double startMs, speed = 1, pausedMs;
    private double musicVolume = 1, clickVolume = 0.7;
    private bool enabled = true, disposed, started;
    private ClickTrack? track;
    private float[] buffer = [];
    private string? callbackError;
    public double DurationMs { get; private set; }
    public bool IsPlaying => !disposed && tempo != 0 &&
        Bass.ChannelIsActive(tempo) is PlaybackState.Playing or PlaybackState.Stalled;

    public PlaybackEngine(string audioPath)
    {
        path = audioPath;
        clickProcedure = ReadClicks; // Keep the delegate rooted for the entire native stream lifetime.
        if (!Bass.Init(-1, 44100, DeviceInitFlags.Latency)) throw Error("initialize default output device");
        device = Bass.CurrentDevice;
        latencyMs = Bass.Info.Latency;
        try { BuildGraph(0); }
        catch { Dispose(); throw; }
    }

    public double PositionMs
    {
        get
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (callbackError is not null) throw new IOException(callbackError);
            if (started && Bass.ChannelIsActive(tempo) == PlaybackState.Stopped)
            {
                pausedMs = DurationMs;
                started = false;
            }
            if (!IsPlaying) return pausedMs;
            // BASS_FX positions are in source bytes; its playback position already excludes its output buffer.
            long position = Bass.ChannelGetPosition(tempo);
            if (position < 0) throw Error("read playback position");
            double source = startMs + Bass.ChannelBytes2Seconds(mixer, position) * 1000;
            return PlaybackMath.AudiblePosition(source, latencyMs, speed, startMs, DurationMs);
        }
    }

    public bool ClickEnabled
    {
        get => enabled;
        set { if (enabled != value) Rebuild(() => enabled = value); }
    }
    public double Speed
    {
        get => speed;
        set { PlaybackMath.ValidateSpeed(value); if (speed != value) Rebuild(() => speed = value); }
    }
    public double MusicVolume
    {
        get => musicVolume;
        set
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            double volume = PlaybackMath.ClampVolume(value);
            // BASSmix MixerAddChannel remarks: source freq/volume/pan attributes are applied when mixing.
            Check(Bass.ChannelSetAttribute(song, ChannelAttribute.Volume, (float)volume), "set music volume");
            musicVolume = volume;
        }
    }
    public double ClickVolume
    {
        get => Volatile.Read(ref clickVolume);
        set
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            Volatile.Write(ref clickVolume, PlaybackMath.ClampVolume(value));
        }
    }
    public void SetTiming(IEnumerable<TimingPoint> redLines)
    {
        var sorted = BeatGrid.Sort(redLines);
        if (!reds.SequenceEqual(sorted)) Rebuild(() => reds = sorted);
    }
    public void Play()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (pausedMs >= DurationMs) Seek(0);
        Check(Bass.ChannelPlay(tempo), "play audio");
        started = true;
    }
    public void Pause()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!IsPlaying) return;
        double position = PositionMs;
        Check(Bass.ChannelPause(tempo), "pause audio");
        pausedMs = position;
        started = false;
    }
    public void Seek(double timeMs)
    {
        if (!double.IsFinite(timeMs)) throw new ArgumentOutOfRangeException(nameof(timeMs));
        Rebuild(null, Math.Clamp(timeMs, 0, DurationMs));
    }

    private void Rebuild(Action? change, double? seek = null)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        bool playing = IsPlaying;
        double position = seek ?? PositionMs;
        if (playing) Check(Bass.ChannelPause(tempo), "pause before seek");
        FreeGraph(); // Rebuild flushes all mixer and tempo lookahead, including click tails.
        change?.Invoke();
        BuildGraph(position);
        if (playing && position < DurationMs) Play();
    }

    private void BuildGraph(double position)
    {
        Bass.CurrentDevice = device;
        try
        {
            song = Handle(Bass.CreateStream(path, 0, 0, BassFlags.Decode | BassFlags.Float | BassFlags.Prescan), "open song");
            Check(Bass.ChannelGetInfo(song, out var info), "read song format");
            rate = info.Frequency;
            long bytes = Bass.ChannelGetLength(song);
            if (bytes < 0) throw Error("read song length");
            DurationMs = Bass.ChannelBytes2Seconds(song, bytes) * 1000;
            long seekBytes = Bass.ChannelSeconds2Bytes(song, position / 1000);
            Check(Bass.ChannelSetPosition(song, seekBytes), "seek song");
            startMs = pausedMs = Bass.ChannelBytes2Seconds(song, Bass.ChannelGetPosition(song)) * 1000;
            clickFrame = ClickSchedule.SampleAt(startMs, rate);
            endFrame = bytes / (sizeof(float) * info.Channels);
            track = new ClickTrack(reds, rate, endFrame);
            callbackError = null;
            click = Handle(Bass.CreateStream(rate, 2, BassFlags.Decode | BassFlags.Float, clickProcedure), "create click stream");
            mixer = Handle(BassMix.CreateMixerStream(rate, 2, BassFlags.Decode | BassFlags.Float | BassFlags.MixerEnd), "create mixer");
            Check(BassMix.MixerAddChannel(mixer, song, BassFlags.MixerChanNoRampin), "mix song");
            Check(Bass.ChannelSetAttribute(song, ChannelAttribute.Volume, (float)musicVolume), "set music volume");
            Check(BassMix.MixerAddChannel(mixer, click, BassFlags.MixerChanNoRampin), "mix clicks");
            tempo = Handle(BassFx.TempoCreate(mixer, BassFlags.Float), "create tempo stream");
            // Volume and click changes happen upstream of the output buffer, so they are heard only after the
            // buffered audio (BASS default 500 ms) plays out. Unbuffered output renders at mix time: changes are immediate.
            Check(Bass.ChannelSetAttribute(tempo, ChannelAttribute.Buffer, 0), "disable output buffering");
            Check(Bass.ChannelSetAttribute(tempo, ChannelAttribute.Tempo, (float)((speed - 1) * 100)), "set tempo");
        }
        catch { FreeGraph(); throw; }
    }

    private int ReadClicks(int handle, IntPtr destination, int length, IntPtr user)
    {
        try
        {
            int frames = (int)Math.Min(length / (2 * sizeof(float)), Math.Max(0, endFrame - clickFrame));
            int count = frames * 2;
            if (buffer.Length < count) buffer = new float[count];
            track!.Render(clickFrame, buffer.AsSpan(0, count), 2, enabled, ClickVolume);
            Marshal.Copy(buffer, 0, destination, count);
            clickFrame += frames;
            int written = count * sizeof(float);
            return clickFrame >= endFrame ? written | (int)StreamProcedureType.End : written;
        }
        catch (Exception ex)
        {
            callbackError = "Click stream failed: " + ex.Message;
            return (int)StreamProcedureType.End; // Exceptions must never cross a native callback boundary.
        }
    }

    private void FreeGraph()
    {
        started = false;
        if (tempo != 0) { Bass.StreamFree(tempo); tempo = 0; }
        if (mixer != 0) { Bass.StreamFree(mixer); mixer = 0; }
        if (click != 0) { Bass.StreamFree(click); click = 0; }
        if (song != 0) { Bass.StreamFree(song); song = 0; }
    }
    public void Dispose()
    {
        if (disposed) return;
        FreeGraph();
        Bass.CurrentDevice = device;
        Bass.Free();
        disposed = true;
    }
    private static int Handle(int value, string action) => value != 0 ? value : throw Error(action);
    private static void Check(bool success, string action) { if (!success) throw Error(action); }
    private static IOException Error(string action) => new("Cannot " + action + " (BASS error " +
        ((int)Bass.LastError).ToString(CultureInfo.InvariantCulture) + ").");
}
