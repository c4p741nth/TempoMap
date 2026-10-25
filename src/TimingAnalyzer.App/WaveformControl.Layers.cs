using System.Globalization;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using TimingAnalyzer.Editor;

namespace TimingAnalyzer.App;

public sealed partial class WaveformControl
{
    private AudioContent? visualAudio;
    private string? visualLayer;
    private VisualSignal? filtered, onsets;
    private CancellationTokenSource? visualCancel, spectrumCancel;
    private WriteableBitmap? spectrum;
    private double spectrumStart, spectrumSpan;
    private (double Start, double Span)? spectrumRequest;
    private double? panX;
    private double panStart;
    private double hoverTime = double.NaN;
    private TimingPoint[]? lanePoints;
    private void PrepareVisuals()
    {
        if (model?.Audio is not { } audio) return;
        string layer = model.WaveformLayer;
        if (!ReferenceEquals(audio, visualAudio) || layer != visualLayer)
        {
            visualCancel?.Cancel(); visualCancel?.Dispose(); visualCancel = new();
            var token = visualCancel.Token; visualAudio = audio; visualLayer = layer; filtered = null; onsets = null;
            spectrumCancel?.Cancel(); spectrumRequest = null; spectrum?.Dispose(); spectrum = null;
            if (layer is not ("Full" or "Spectrogram")) _ = BuildFilter(audio, layer, token, false);
            if (model.ShowOnsets) _ = BuildFilter(audio, "Onsets", token, true);
        }
        else if (model.ShowOnsets && onsets is null && !onsetPending)
            _ = BuildFilter(audio, "Onsets", visualCancel!.Token, true);
        if (layer == "Spectrogram")
        {
            double quantum = model.SpanMs / 8;
            var request = (Start: Math.Floor(model.StartMs / quantum) * quantum, Span: model.SpanMs * 1.125);
            if (spectrumRequest != request)
            {
                spectrumCancel?.Cancel(); spectrumCancel?.Dispose(); spectrumCancel = new(); spectrumRequest = request;
                _ = BuildSpectrum(audio, request.Start, request.Span, spectrumCancel.Token);
            }
        }
    }
    private bool onsetPending;
    private async Task BuildFilter(AudioContent audio, string layer, CancellationToken token, bool isOnsets)
    {
        if (isOnsets) onsetPending = true;
        try
        {
            var result = await Task.Run(() => SignalVisuals.Filter(audio, layer, token), token);
            if (token.IsCancellationRequested) return;
            if (isOnsets) onsets = result; else filtered = result;
            InvalidateVisual();
        }
        catch (OperationCanceledException) { }
        finally { if (isOnsets) onsetPending = false; }
    }
    private async Task BuildSpectrum(AudioContent audio, double start, double span, CancellationToken token)
    {
        try
        {
            var data = await Task.Run(() => SignalVisuals.Spectrogram(audio, start, span, 640, 80, token), token);
            if (token.IsCancellationRequested) return;
            const int w = 640, h = 80; var pixels = new byte[w * h * 4];
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
            {
                double v = data[x, h - y - 1] / 255.0; int at = (y * w + x) * 4;
                pixels[at] = (byte)(35 + 130 * v); pixels[at + 1] = (byte)(15 + 225 * v * v);
                pixels[at + 2] = (byte)(10 + 190 * Math.Pow(v, 4)); pixels[at + 3] = 255;
            }
            var bitmap = new WriteableBitmap(new(w, h), new(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
            using (var buffer = bitmap.Lock())
                for (int y = 0; y < h; y++) Marshal.Copy(pixels, y * w * 4, IntPtr.Add(buffer.Address, y * buffer.RowBytes), w * 4);
            spectrum?.Dispose(); spectrum = bitmap; spectrumStart = start; spectrumSpan = span; InvalidateVisual();
        }
        catch (OperationCanceledException) { }
    }
    private void DrawVisualLayers(DrawingContext context, Func<double, double> x)
    {
        if (model is null) return;
        double height = Bounds.Height;
        if (model.WaveformLayer == "Spectrogram" && spectrum is not null)
            context.DrawImage(spectrum, new Rect(x(spectrumStart), 22, spectrumSpan / model.SpanMs * Bounds.Width, Math.Max(1, height - 42)));
        IBrush label = palette["OverlayLabelBrush"], green = palette["TimingGreenBrush"], bookmark = palette["OverlayBookmarkBrush"];
        // Ruler: human-readable times, independent of beat subdivisions.
        double target = model.SpanMs / Math.Max(2, Bounds.Width / 100);
        double magnitude = Math.Pow(10, Math.Floor(Math.Log10(Math.Max(.001, target))));
        double step = new[] { 1.0, 2, 5, 10 }.First(v => v * magnitude >= target) * magnitude;
        for (double t = Math.Ceiling(model.StartMs / step) * step; t < model.StartMs + model.SpanMs; t += step)
        {
            context.DrawLine(gridPens[1], new(x(t), 0), new(x(t), 5));
            Text(context, TimeFormat.Osu(t), new(x(t) + 3, 3), label, 10);
        }
        if (model.GhostProposal)
        {
            Pen ghost = new(palette["OverlayGhostBrush"], 1.5, new DashStyle([4, 3], 0)), ghostBeat = new(palette["OverlayGhostBeatBrush"], .6, new DashStyle([2, 4], 0));
            foreach (var line in BeatGrid.Query(model.ProposedTiming, model.StartMs, model.StartMs + model.SpanMs, minimumBeatLength: 50).Take(2000))
                context.DrawLine(line.Kind == GridKind.RedLine ? ghost : ghostBeat, new(x(line.Time), 22), new(x(line.Time), height - 24));
        }
        if (model.ShowOnsets && onsets is { } onset)
        {
            int a = Math.Clamp((int)(model.StartMs * onset.Rate / 1000), 0, onset.Peaks.SampleCount), b = Math.Clamp((int)((model.StartMs + model.SpanMs) * onset.Rate / 1000), a, onset.Peaks.SampleCount);
            var peaks = onset.Peaks.Query(a, b, Math.Max(1, (int)Bounds.Width)); double max = Math.Max(1e-12, onset.Peaks.Range(0, onset.Peaks.SampleCount).Max);
            var onsetPen = new Pen(palette["OverlayOnsetBrush"], 1);
            for (int i = 0; i < peaks.Length; i++) context.DrawLine(onsetPen, new(i, height - 28), new(i, height - 28 - Math.Max(0, peaks[i].Max) / max * height * .35));
        }
        var points = lanePoints ??= model.AllRows.Select(r => r.Point).ToArray();
        if (model.ShowKiai)
        {
            for (int i = 0; i < points.Length; i++)
                if ((points[i].Effects & TimingPoint.Kiai) != 0)
                {
                    double left = Math.Max(0, x(points[i].Time)), right = Math.Min(Bounds.Width, x(i + 1 < points.Length ? points[i + 1].Time : model.DurationMs));
                    if (right > left) context.FillRectangle(palette["OverlayKiaiBrush"], new Rect(left, height - 8, right - left, 7));
                }
        }
        if (model.ShowSv)
        {
            var svPen = new Pen(green);
            foreach (var point in points.Where(p => !p.Uninherited && p.Time >= model.StartMs && p.Time < model.StartMs + model.SpanMs))
            {
                double px = x(point.Time); context.DrawLine(svPen, new(px, height - 24), new(px, height - 10));
                Text(context, point.Sv.ToString("0.##'×'", CultureInfo.InvariantCulture), new(px + 2, height - 25), green, 10);
            }
        }
        var bookmarkPen = new Pen(bookmark, 1, new DashStyle([2, 2], 0));
        foreach (var marker in model.Bookmarks.Where(b => b.TimeMs >= model.StartMs && b.TimeMs <= model.StartMs + model.SpanMs))
        {
            context.DrawLine(bookmarkPen, new(x(marker.TimeMs), 22), new(x(marker.TimeMs), height));
            Text(context, marker.Name, new(x(marker.TimeMs) + 3, 16), bookmark, 11);
        }
        if (double.IsFinite(hoverTime)) Text(context, TimeFormat.Osu(hoverTime), new(Math.Clamp(x(hoverTime) + 8, 0, Math.Max(0, Bounds.Width - 100)), 30), label, 11);
    }
    internal static void Text(DrawingContext context, string text, Point origin, IBrush color, double size) => context.DrawText(new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Typeface.Default, size, color), origin);
    private void Hover(PointerEventArgs e)
    {
        if (model is null) return;
        hoverTime = TimeAt(e.GetPosition(this).X);
        bool near = model.Edits.RedLines.Any(p => Math.Abs(p.Time - hoverTime) <= 6 * model.SpanMs / Math.Max(1, Bounds.Width));
        Cursor = new Cursor(panX is not null ? StandardCursorType.Hand : near || e.KeyModifiers.HasFlag(KeyModifiers.Control) ? StandardCursorType.SizeWestEast : StandardCursorType.Cross);
        InvalidateVisual();
    }
}
