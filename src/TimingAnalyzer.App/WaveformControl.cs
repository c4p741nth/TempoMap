using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using TimingAnalyzer.Editor;

namespace TimingAnalyzer.App;

public sealed class WaveformControl : Control
{
    private EditorViewModel? model;
    // Box select: a left drag from empty space that travels more than BoxThreshold px. A shorter one is a click (seek).
    // With Shift held, a drag from anywhere (red lines included) highlights a range instead (Auto-time range).
    private const double BoxThreshold = 4;
    private double? pressX;
    private double boxX;
    private bool boxing, ranging;
    private static readonly Pen Wave = new(Brushes.Teal), Playhead = new(Brushes.Gold, 2);
    private static readonly Pen Selected = new(Brushes.OrangeRed, 4);
    private static readonly IBrush BoxFill = new SolidColorBrush(Color.FromArgb(48, 255, 140, 0));
    private static readonly Pen BoxEdge = new(new SolidColorBrush(Color.FromArgb(160, 255, 140, 0)));
    private static readonly IBrush RangeFill = new SolidColorBrush(Color.FromArgb(56, 120, 170, 255));
    private static readonly IBrush MarkFill = new SolidColorBrush(Color.FromArgb(40, 200, 90, 255));
    private static readonly Pen RangeEdge = new(new SolidColorBrush(Color.FromArgb(200, 120, 170, 255)));
    private static readonly Pen[] GridPens = [new(new SolidColorBrush(Color.Parse("#303741"))),
        new(Brushes.Gray), new(Brushes.MediumPurple), new(Brushes.IndianRed, 2)];
    public WaveformControl()
    {
        Focusable = true; ClipToBounds = true;
        DataContextChanged += (_, _) =>
        {
            if (model is not null) model.PropertyChanged -= ModelChanged;
            model = DataContext as EditorViewModel;
            if (model is not null) model.PropertyChanged += ModelChanged;
            InvalidateVisual();
        };
    }
    private void ModelChanged(object? sender, PropertyChangedEventArgs e) => InvalidateVisual();
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.FillRectangle(new SolidColorBrush(Color.Parse("#151A21")), new Rect(Bounds.Size));
        if (model is null || Bounds.Width < 1 || Bounds.Height < 1) return;
        double X(double time) => (time - model.StartMs) * Bounds.Width / model.SpanMs;
        if (model.Audio is { } audio)
        {
            int a = (int)Math.Clamp(Math.Floor(model.StartMs * audio.Rate / 1000), 0, audio.Peaks.SampleCount);
            int b = (int)Math.Clamp(Math.Ceiling((model.StartMs + model.SpanMs) * audio.Rate / 1000), a, audio.Peaks.SampleCount);
            var peaks = audio.Peaks.Query(a, b, Math.Max(1, (int)Bounds.Width));
            var full = audio.Peaks.Range(0, audio.Peaks.SampleCount);
            double scale = Bounds.Height * 0.4 / Math.Max(1, Math.Max(Math.Abs(full.Min), Math.Abs(full.Max)));
            for (int i = 0; i < peaks.Length; i++) context.DrawLine(Wave,
                new(i, Bounds.Height / 2 - peaks[i].Max * scale), new(i, Bounds.Height / 2 - peaks[i].Min * scale));
        }
        var reds = model.DisplayRedLines;
        // Beat-snap ticks closer than 4 px would only paint the background; fall back to whole beats.
        int division = model.SnapDivisor;
        if (reds.Count > 0 && reds.Min(p => p.BeatLength) / division * Bounds.Width / model.SpanMs < 4) division = 1;
        foreach (var line in BeatGrid.Query(reds, model.StartMs, model.StartMs + model.SpanMs, division).Take(5000))
        {
            double x = X(line.Time);
            context.DrawLine(GridPens[(int)line.Kind], new(x, 0), new(x, Bounds.Height));
        }
        // Sections worth a listen (Check sections): a violet band over the section.
        foreach (var mark in model.Marks)
        {
            if (mark.Index >= reds.Count) continue;
            double left = X(reds[mark.Index].Time), right = mark.Index + 1 < reds.Count ? X(reds[mark.Index + 1].Time) : X(model.DurationMs);
            if (right < 0 || left > Bounds.Width) continue;
            context.FillRectangle(MarkFill, new Rect(Math.Max(0, left), 0, Math.Max(1, Math.Min(Bounds.Width, right) - Math.Max(0, left)), Bounds.Height));
        }
        // The highlighted range (blue), and while dragging the band being drawn (blue for a range, orange for a box select).
        if (model.Range is { } range && !(ranging && boxing))
        {
            var band = new Rect(X(range.Start), 0, Math.Max(1, X(range.End) - X(range.Start)), Bounds.Height);
            context.FillRectangle(RangeFill, band);
            context.DrawRectangle(RangeEdge, band);
        }
        if (boxing && pressX is { } from)
        {
            var band = new Rect(Math.Min(from, boxX), 0, Math.Abs(boxX - from), Bounds.Height);
            context.FillRectangle(ranging ? RangeFill : BoxFill, band);
            context.DrawRectangle(ranging ? RangeEdge : BoxEdge, band);
        }
        double head = X(model.PositionMs);
        context.DrawLine(Playhead, new(head, 0), new(head, Bounds.Height));
        if (!model.IsDragging)
            foreach (int i in model.SelectedIndices)
            {
                if (i >= model.Edits.RedLines.Count) continue;
                double x = X(model.Edits.RedLines[i].Time);
                context.DrawLine(Selected, new(x, 0), new(x, Bounds.Height));
            }
    }
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (model is null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        Focus();
        double x = e.GetPosition(this).X;
        ranging = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        // Shift: a range drag even over a red line (so a range can start on one). Otherwise a red line is grabbed.
        if (ranging || !model.BeginDrag(TimeAt(x), 6 * model.SpanMs / Math.Max(1, Bounds.Width), e.KeyModifiers.HasFlag(KeyModifiers.Control)))
        {
            // Empty space (or Shift): seek on release if it stays a click, otherwise box-select (Shift: highlight a range).
            pressX = x; boxX = x; boxing = false;
        }
        e.Pointer.Capture(this);
        e.Handled = true;
    }
    private double TimeAt(double x) => model!.StartMs + x / Math.Max(1, Bounds.Width) * model.SpanMs;
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        double x = e.GetPosition(this).X;
        if (model?.IsDragging == true) model.UpdateDrag(TimeAt(x));
        else if (pressX is { } from && (boxing || Math.Abs(x - from) > BoxThreshold))
        {
            boxing = true; boxX = x;
            InvalidateVisual();
        }
    }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (model?.IsDragging == true) { model.CommitDrag(); e.Handled = true; }
        else if (model is not null && pressX is { } from)
        {
            if (boxing && ranging) model.SetRange(TimeAt(from), TimeAt(boxX));
            else if (boxing) model.SelectRange(TimeAt(from), TimeAt(boxX), false);
            else model.Seek(TimeAt(from));
            e.Handled = true;
        }
        EndBox();
        e.Pointer.Capture(null);
    }
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e); model?.CancelDrag(); EndBox();
    }
    private void EndBox()
    {
        bool repaint = boxing;
        pressX = null; boxing = false; ranging = false;
        if (repaint) InvalidateVisual();
    }
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (model is null) return;
        if (model.IsDragging) { e.Handled = true; return; }
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control)) model.Zoom(Math.Pow(0.8, e.Delta.Y), e.GetPosition(this).X / Math.Max(1, Bounds.Width));
        else model.Scroll(-(e.Delta.Y + e.Delta.X) * model.SpanMs * 0.1);
        e.Handled = true;
    }
}
