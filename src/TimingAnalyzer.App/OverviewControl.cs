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
    public OverviewControl()
    {
        Focusable = true;
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
        var pen = new Pen(Brushes.Teal);
        for (int x = 0; x < peaks.Length; x++) context.DrawLine(pen, new(x, Bounds.Height / 2 - peaks[x].Max * Bounds.Height * .42), new(x, Bounds.Height / 2 - peaks[x].Min * Bounds.Height * .42));
        double X(double ms) => ms / model.DurationMs * Bounds.Width;
        context.DrawRectangle(new SolidColorBrush(Color.FromArgb(35, 100, 200, 230)), new Pen(Brushes.CadetBlue), new Rect(X(model.StartMs), 0, Math.Max(1, X(model.SpanMs)), Bounds.Height));
        foreach (var marker in model.Bookmarks) context.DrawLine(new Pen(Brushes.Orchid), new(X(marker.TimeMs), 0), new(X(marker.TimeMs), 8));
        context.DrawLine(new Pen(Brushes.Gold, 2), new(X(model.PositionMs), 0), new(X(model.PositionMs), Bounds.Height));
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
