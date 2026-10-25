using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using TimingAnalyzer.Editor;

namespace TimingAnalyzer.App;

public partial class MainWindow
{
    private async void ExportArchive(object? sender, RoutedEventArgs e)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "Export current difficulty with audio and local media", DefaultExtension = "osz",
            SuggestedFileName = Path.GetFileNameWithoutExtension(model.BeatmapPath ?? model.OriginalAudioPath ?? "Timing") + ".osz",
            SuggestedStartLocation = await LastFolder(fileSettings.LastMapDirectory), FileTypeChoices = [new FilePickerFileType("osu! beatmap archive") { Patterns = ["*.osz"] }] });
        if (file?.TryGetLocalPath() is { } path && SavePointFields()) await model.ExportArchiveAsync(path);
    }
    private async void ConvertToOgg(object? sender, RoutedEventArgs e)
    {
        string? source = model.OriginalAudioPath;
        if (source is null)
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Choose source audio to convert", FileTypeFilter =
                [new FilePickerFileType("Audio") { Patterns = [.. EditorViewModel.AudioExtensions.Select(x => "*" + x)] }] });
            source = files.FirstOrDefault()?.TryGetLocalPath();
        }
        if (source is null) return;
        var dialog = new Window { Title = "Convert audio to Ogg Vorbis", Width = 500, SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Icon = Icon };
        var panel = ToolPanel(); Label(panel, Path.GetFileName(source)); Label(panel, "Creates a separate file. The current project and beatmap audio reference stay as they are.");
        Label(panel, "Sample rate");
        var rates = new ComboBox { ItemsSource = new[] { "Original", "22050", "32000", "44100", "48000", "96000" }, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch }; panel.Children.Add(rates);
        Label(panel, "Vorbis quality (-1 to 10; default 6)"); var quality = new NumericUpDown { Minimum = -1, Maximum = 10, Increment = .5m, Value = 6 }; panel.Children.Add(quality);
        var status = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap }; panel.Children.Add(status);
        var progress = new ProgressBar { IsIndeterminate = true, IsVisible = false, Height = 4 }; panel.Children.Add(progress);
        var convert = new Button { Content = "Choose destination and convert…" }; var cancel = new Button { Content = "Close" };
        var buttons = new WrapPanel { ItemSpacing = 10 }; buttons.Children.Add(convert); buttons.Children.Add(cancel); panel.Children.Add(buttons); dialog.Content = panel;
        using var cancellation = new CancellationTokenSource(); bool running = false;
        cancel.Click += (_, _) => { if (running) cancellation.Cancel(); else dialog.Close(); };
        dialog.Closing += (_, args) => { if (running) { args.Cancel = true; cancellation.Cancel(); } };
        convert.Click += async (_, _) =>
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "Save converted Ogg", DefaultExtension = "ogg",
                SuggestedFileName = Path.GetFileNameWithoutExtension(source) + "-converted.ogg", FileTypeChoices = [new FilePickerFileType("Ogg Vorbis") { Patterns = ["*.ogg"] }] });
            if (file?.TryGetLocalPath() is not { } destination) return;
            int? rate = rates.SelectedIndex == 0 ? null : int.Parse((string)rates.SelectedItem!, CultureInfo.InvariantCulture);
            double q = (double)(quality.Value ?? 6); convert.IsEnabled = false; rates.IsEnabled = quality.IsEnabled = false; cancel.Content = "Cancel"; running = true; progress.IsVisible = true;
            try { await new AudioImport().ConvertAsync(source, destination, new(rate, q), cancellation.Token); status.Text = "Converted: " + destination; }
            catch (OperationCanceledException) { status.Text = "Conversion cancelled."; }
            catch (Exception ex) { status.Text = ex.Message; }
            finally { running = false; progress.IsVisible = false; cancel.Content = "Close"; }
        };
        CloseOnEscape(dialog); await dialog.ShowDialog(this);
    }
    private async void AudioDiagnostics(object? sender, RoutedEventArgs e)
    {
        var tools = new AudioImport().Diagnose();
        string audio = model.Audio is { } content ? string.Create(CultureInfo.InvariantCulture, $"{content.Rate} Hz · {content.Mono.Length} decoded samples · {content.DurationMs / 1000:0.###} s") : "No audio loaded";
        string message = "Source: " + (model.OriginalAudioPath ?? "none") + "\n" + audio + "\n\n" + tools +
            "\n\nOriginal MP3/WAV/FLAC/Ogg opening uses the native decoder. FFmpeg is optional for explicit Ogg conversion.";
        await Choose("Audio diagnostics", message, "Close");
    }
}
