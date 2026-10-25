using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using TimingAnalyzer.Editor;

namespace TimingAnalyzer.App;

public sealed class OverviewControl : Control
{
    private EditorViewModel? model;
    private bool dragging;
    private readonly CanvasPalette palette;
    public OverviewControl()
    {
        Focusable = true; ClipToBounds = true; palette = new(this);
        DataContextChanged += (_, _) =>
        {
            if (model is not null) model.PropertyChanged -= Changed;
            model = DataContext as EditorViewModel;
            if (model is not null) model.PropertyChanged += Changed;
            InvalidateVisual();
        };
    }
    private void Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(EditorViewModel.Audio) or nameof(EditorViewModel.PositionMs) or nameof(EditorViewModel.StartMs) or nameof(EditorViewModel.SpanMs) or nameof(EditorViewModel.Bookmarks)) InvalidateVisual();
    }
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (model?.Audio is not { } audio || Bounds.Width <= 0) return;
        var peaks = audio.Peaks.Query(0, audio.Mono.Length, Math.Max(1, (int)Bounds.Width));
        var pen = new Pen(palette["WaveformSignalBrush"]);
        // The song's loudest peak reaches 92% of the strip, so the frame and the playhead (the full height) always cover the wave.
        double scale = WaveformControl.PeakScale(audio, Bounds.Height, .46);
        for (int x = 0; x < peaks.Length; x++) context.DrawLine(pen, new(x, Bounds.Height / 2 - peaks[x].Max * scale), new(x, Bounds.Height / 2 - peaks[x].Min * scale));
        double X(double ms) => ms / model.DurationMs * Bounds.Width;
        // The viewport frame is drawn inside the control (half a pixel in), so its edges are not cut where it meets the bounds, and it
        // reaches the last pixel when the view is at the end of the song. The playhead keeps one pixel off the ends for the same reason.
        double left = Math.Clamp(X(model.StartMs), 0, Bounds.Width), right = Math.Clamp(X(model.StartMs + model.SpanMs), left, Bounds.Width);
        context.DrawRectangle(palette.Tint("WorkspaceAccent", .16), new Pen(palette["WorkspaceAccent"]),
            new Rect(left + .5, .5, Math.Max(2, right - left - 1), Math.Max(1, Bounds.Height - 1)), 3, 3);
        var bookmark = new Pen(palette["OverlayBookmarkBrush"]);
        foreach (var marker in model.Bookmarks) context.DrawLine(bookmark, new(X(marker.TimeMs), 0), new(X(marker.TimeMs), 8));
        double playhead = Math.Clamp(X(model.PositionMs), 1, Math.Max(1, Bounds.Width - 1));
        context.DrawLine(new Pen(palette["WaveformPlayheadBrush"], 2), new(playhead, 0), new(playhead, Bounds.Height));
    }
    private void SeekAt(PointerEventArgs e)
    {
        if (model is null) return;
        double time = Math.Clamp(e.GetPosition(this).X / Math.Max(1, Bounds.Width), 0, 1) * model.DurationMs;
        model.JumpTo(time);
    }
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e); if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        dragging = true; e.Pointer.Capture(this); SeekAt(e); e.Handled = true;
    }
    protected override void OnPointerMoved(PointerEventArgs e) { base.OnPointerMoved(e); if (dragging) SeekAt(e); }
    protected override void OnPointerReleased(PointerReleasedEventArgs e) { base.OnPointerReleased(e); dragging = false; e.Pointer.Capture(null); }
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e) { base.OnPointerCaptureLost(e); dragging = false; }
}
