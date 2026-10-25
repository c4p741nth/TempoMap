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
        var dialog = NewDialog("Convert audio to Ogg Vorbis", 540);
        var rates = new ComboBox { ItemsSource = new[] { "Original", "22050", "32000", "44100", "48000", "96000" }, SelectedIndex = 0, MinWidth = 140 };
        var quality = new NumericUpDown { Minimum = -1, Maximum = 10, Increment = .5m, Value = 6, Width = 130 };
        var status = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap, Classes = { "hint" } };
        var progress = new ProgressBar { IsIndeterminate = true, IsVisible = false, Height = 4 };
        var convert = Primary(new Button { Content = "Choose destination and convert…" }); var cancel = new Button { Content = "Close" };
        var heading = Heading(Path.GetFileName(source)); heading.TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis;
        dialog.Content = DialogBody(heading, Hint("Creates a separate file. The current project and beatmap audio reference stay as they are."),
            Card("Output", LabeledRow("Sample rate", rates, 250), LabeledRow("Vorbis quality (−1 to 10, default 6)", quality, 250)),
            status, progress, ButtonRow([convert, cancel]));
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
        var dialog = NewDialog("Audio diagnostics", 580);
        Control Tool(string name, string path, bool found)
        {
            var row = new Grid { ColumnDefinitions = new("68,*"), ColumnSpacing = 10 };
            var state = new TextBlock { Text = found ? "found" : "missing", FontWeight = Avalonia.Media.FontWeight.SemiBold };
            state.Bind(TextBlock.ForegroundProperty, dialog.GetResourceObservable(found ? "TimingGreenBrush" : "TimingRedBrush"));
            row.Children.Add(state);
            var text = new StackPanel { Spacing = 1 }; text.Children.Add(new TextBlock { Text = name });
            text.Children.Add(new TextBlock { Text = path, TextWrapping = Avalonia.Media.TextWrapping.Wrap, Classes = { "hint" } });
            Grid.SetColumn(text, 1); row.Children.Add(text); return row;
        }
        var close = Primary(new Button { Content = "Close" }); close.Click += (_, _) => dialog.Close();
        dialog.Content = DialogBody(
            Card("Current song", new TextBlock { Text = model.OriginalAudioPath ?? "No audio loaded", TextWrapping = Avalonia.Media.TextWrapping.Wrap }, Hint(audio)),
            Card("FFmpeg (optional)", Tool("Encoder (ffmpeg)", tools.Encoder, tools.EncoderAvailable), Tool("Probe (ffprobe)", tools.Probe, tools.ProbeAvailable)),
            Hint("Opening MP3, WAV, FLAC and Ogg uses the native decoder. FFmpeg is only needed for the explicit Ogg conversion."),
            ButtonRow([close]));
        CloseOnEscape(dialog); await dialog.ShowDialog(this);
    }
}
