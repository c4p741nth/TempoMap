using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using TimingAnalyzer.Editor;

namespace TimingAnalyzer.App;

public sealed class MeasureViewControl : Control
{
    private EditorViewModel? model;
    private MeasureRow[] layout = [];
    public MeasureViewControl()
    {
        ClipToBounds = true; Focusable = true;
        DataContextChanged += (_, _) =>
        {
            if (model is not null) model.PropertyChanged -= Changed;
            model = DataContext as EditorViewModel; if (model is not null) model.PropertyChanged += Changed;
        };
    }
    private void Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(EditorViewModel.StartMs) or nameof(EditorViewModel.PositionMs) or nameof(EditorViewModel.TableRows) or nameof(EditorViewModel.Audio)) InvalidateVisual();
    }
    public override void Render(DrawingContext context)
    {
        base.Render(context); if (model?.Audio is not { } audio || Bounds.Width < 1) return;
        layout = MeasureLayout.Rows(model.Edits.RedLines, model.StartMs);
        double rowHeight = Bounds.Height / 4;
        for (int row = 0; row < 4; row++)
        {
            double a = layout[row].StartMs, rowSpan = layout[row].EndMs - a, y = row * rowHeight;
            context.DrawLine(new Pen(Brushes.Gray, .5), new(0, y), new(Bounds.Width, y));
            WaveformControl.Text(context, TimeFormat.Osu(a), new(6, y + 3), Brushes.Gray, 10);
            int from = Math.Clamp((int)(a * audio.Rate / 1000), 0, audio.Mono.Length), to = Math.Clamp((int)((a + rowSpan) * audio.Rate / 1000), from, audio.Mono.Length);
            var peaks = audio.Peaks.Query(from, to, Math.Max(1, (int)Bounds.Width));
            for (int x = 0; x < peaks.Length; x++) context.DrawLine(new Pen(Brushes.Teal), new(x, y + rowHeight / 2 - peaks[x].Max * rowHeight * .35), new(x, y + rowHeight / 2 - peaks[x].Min * rowHeight * .35));
            foreach (var grid in BeatGrid.Query(model.Edits.RedLines, a, a + rowSpan, minimumBeatLength: 50).Take(1000))
            {
                double x = (grid.Time - a) / rowSpan * Bounds.Width;
                context.DrawLine(new Pen(grid.Kind == GridKind.RedLine ? Brushes.IndianRed : grid.Kind == GridKind.Measure ? Brushes.MediumPurple : Brushes.Gray), new(x, y + 15), new(x, y + rowHeight));
            }
            if (model.PositionMs >= a && model.PositionMs < a + rowSpan)
            { double x = (model.PositionMs - a) / rowSpan * Bounds.Width; context.DrawLine(new Pen(Brushes.Gold, 2), new(x, y), new(x, y + rowHeight)); }
        }
    }
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e); if (model is null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if (layout.Length == 0) return;
        var p = e.GetPosition(this); var row = layout[Math.Clamp((int)(p.Y / Math.Max(1, Bounds.Height / 4)), 0, layout.Length - 1)];
        model.Seek(row.StartMs + p.X / Math.Max(1, Bounds.Width) * (row.EndMs - row.StartMs)); Focus(); e.Handled = true;
    }
}
