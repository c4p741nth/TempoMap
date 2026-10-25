using Avalonia.Controls;
using Avalonia.Media;

namespace TimingAnalyzer.App;

/// <summary>
/// The brushes a canvas control draws with, read from the theme resources (Styles/Workspace.axaml) and read again when the
/// theme changes, so the waveform overlays, the overview and the measure view follow Dark and Light like the rest of the window.
/// </summary>
internal sealed class CanvasPalette
{
    private readonly Control owner;
    private readonly Dictionary<string, IBrush> cache = [];
    public CanvasPalette(Control owner)
    {
        this.owner = owner;
        owner.ActualThemeVariantChanged += (_, _) => Reset();
        owner.AttachedToVisualTree += (_, _) => Reset();
    }
    private void Reset() { cache.Clear(); owner.InvalidateVisual(); }
    public IBrush this[string key]
    {
        get
        {
            if (cache.TryGetValue(key, out var brush)) return brush;
            brush = owner.TryFindResource(key, owner.ActualThemeVariant, out var value) && value is IBrush found ? found : Brushes.Gray;
            return cache[key] = brush;
        }
    }
    /// <summary>The theme brush with its own alpha replaced by <paramref name="opacity"/> (a translucent fill over a waveform).</summary>
    public IBrush Tint(string key, double opacity) =>
        this[key] is ISolidColorBrush solid ? new SolidColorBrush(solid.Color, opacity) : this[key];
}
