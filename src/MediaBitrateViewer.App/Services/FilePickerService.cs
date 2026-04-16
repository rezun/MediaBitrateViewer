using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using MediaBitrateViewer.Core.Abstractions;

namespace MediaBitrateViewer.App.Services;

public sealed class FilePickerService : IFilePickerService
{
    public async ValueTask<string?> PickVideoFileAsync(CancellationToken cancellationToken)
    {
        var top = GetTopLevel();
        if (top is null) return null;

        var options = new FilePickerOpenOptions
        {
            Title = "Open Video File",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Video files")
                {
                    Patterns = VideoFileExtensions.Patterns
                },
                FilePickerFileTypes.All
            }
        };

        var result = await top.StorageProvider.OpenFilePickerAsync(options).ConfigureAwait(true);
        var first = result.FirstOrDefault();
        return first?.TryGetLocalPath();
    }

    private static TopLevel? GetTopLevel()
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            return null;
        var window = desktop.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
                     ?? desktop.MainWindow;
        return window;
    }
}
