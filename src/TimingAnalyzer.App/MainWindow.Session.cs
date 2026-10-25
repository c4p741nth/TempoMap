using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using TimingAnalyzer.Editor;

namespace TimingAnalyzer.App;

public partial class MainWindow
{
    private EditorSettings fileSettings = new();
    private RecoveryStore recovery = null!;
    private readonly DispatcherTimer recoveryTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private bool allowClose, checkingClose, closeAfterCancel;

    private void InitializeSession()
    {
        recovery = new RecoveryStore(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(SettingsPath))!, "Recovery"));
        recoveryTimer.Tick += (_, _) =>
        {
            recoveryTimer.Stop();
            try { if (model.HasWorkspace) recovery.Save(model.CaptureProject()); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { model.ReportError("Recovery snapshot failed: " + ex.Message); }
        };
        model.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(EditorViewModel.IsBusy) && !model.IsBusy && closeAfterCancel)
            { closeAfterCancel = false; Dispatcher.UIThread.Post(Close); }
            if (e.PropertyName is nameof(EditorViewModel.IsDirty) or nameof(EditorViewModel.HasProposal) or nameof(EditorViewModel.LockedBpmText) or nameof(EditorViewModel.ProposalOffsetText) or nameof(EditorViewModel.ReviewQueue))
            { recoveryTimer.Stop(); recoveryTimer.Start(); }
        };
        Closing += GuardClosing;
        Opened += async (_, _) => await OfferRecovery();
        RebuildRecent();
    }

    private async void GuardClosing(object? sender, WindowClosingEventArgs e)
    {
        if (allowClose) return;
        if (model.IsBusy) { e.Cancel = true; closeAfterCancel = true; model.CancelAnalysis(); return; }
        e.Cancel = true;
        if (checkingClose) return;
        checkingClose = true;
        try
        {
            if (!await GuardUnsaved()) return;
            recoveryTimer.Stop(); recovery.Clear(); SaveSettings(); allowClose = true; Close();
        }
        finally { checkingClose = false; }
    }

    private async Task<bool> GuardUnsaved()
    {
        if (!SavePointFields()) return false;
        if (!model.IsDirty) return true;
        bool project = model.ProjectPath is not null || model.Bookmarks.Count > 0;
        int choice = await Choose("Unsaved changes", "Save your changes before continuing? Projects also keep bookmarks and pending proposals.",
            project ? "Save project" : "Save .osu", "Save project as…", "Discard changes", "Cancel");
        if (choice == 2) return true;
        if (choice == 1) return await SaveProjectFile(true);
        if (choice != 0) return false;
        if (project) return await SaveProjectFile(false);
        await SaveOsuFile(false); return !model.IsDirty;
    }

    // Choose and OfferRecovery are in MainWindow.Dialogs.cs with the other dialogs.

    private async Task<IStorageFolder?> LastFolder(string? path) => path is not null && Directory.Exists(path)
        ? await StorageProvider.TryGetFolderFromPathAsync(new Uri(Path.GetFullPath(path))) : null;

    private void RememberFile(string path)
    {
        path = Path.GetFullPath(path);
        fileSettings = fileSettings with
        {
            RecentFiles = new[] { path }.Concat(fileSettings.RecentFiles.Where(x => !string.Equals(x, path, StringComparison.OrdinalIgnoreCase))).Take(12).ToArray(),
            LastAudioDirectory = EditorViewModel.AudioExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase) ? Path.GetDirectoryName(path) : fileSettings.LastAudioDirectory,
            LastMapDirectory = !EditorViewModel.AudioExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase) ? Path.GetDirectoryName(path) : fileSettings.LastMapDirectory
        };
        RebuildRecent(); SaveSettings();
    }
    private void RebuildRecent()
    {
        if (this.FindControl<MenuItem>("RecentMenu") is not { } menu) return;
        menu.Items.Clear();
        foreach (string path in fileSettings.RecentFiles)
        {
            var item = new MenuItem { Header = Path.GetFileName(path), IsEnabled = File.Exists(path) };
            ToolTip.SetTip(item, path); item.Click += async (_, _) => await OpenPath(path); menu.Items.Add(item);
        }
        menu.IsEnabled = menu.Items.Count > 0;
    }
    private async Task OpenPath(string path)
    {
        if (!await GuardUnsaved()) return;
        await model.LoadAsync(path);
        if (model.HasWorkspace) RememberFile(path);
    }
    private async void OpenProject(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Open TempoMap project",
            SuggestedStartLocation = await LastFolder(fileSettings.LastMapDirectory),
            FileTypeFilter = [new FilePickerFileType("TempoMap project") { Patterns = ["*.tmap"] }] });
        if (files.FirstOrDefault()?.TryGetLocalPath() is { } path) await OpenPath(path);
    }
    private async Task<bool> SaveProjectFile(bool saveAs)
    {
        if (!SavePointFields()) return false;
        string? path = saveAs ? null : model.ProjectPath;
        if (path is null)
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "Save TempoMap project", DefaultExtension = "tmap",
                SuggestedFileName = Path.GetFileNameWithoutExtension(model.OriginalAudioPath ?? "Untitled") + ".tmap",
                SuggestedStartLocation = await LastFolder(fileSettings.LastMapDirectory),
                FileTypeChoices = [new FilePickerFileType("TempoMap project") { Patterns = ["*.tmap"] }] });
            path = file?.TryGetLocalPath();
        }
        if (path is null) return false;
        model.SaveProject(path);
        if (model.ProjectPath != Path.GetFullPath(path) || model.IsDirty) return false;
        recovery.Clear(); RememberFile(path); return true;
    }
    private async void SaveProject(object? sender, RoutedEventArgs e) => await SaveProjectFile(false);
    private async void SaveProjectAs(object? sender, RoutedEventArgs e) => await SaveProjectFile(true);
    private async void SaveOsuAs(object? sender, RoutedEventArgs e) => await SaveOsuFile(true);
    private async void RevertFile(object? sender, RoutedEventArgs e)
    {
        if ((model.ProjectPath ?? model.BeatmapPath ?? model.OriginalAudioPath) is { } path) await OpenPath(path);
    }
}
