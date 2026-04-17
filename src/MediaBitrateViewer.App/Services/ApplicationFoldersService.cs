using System.Diagnostics;
using MediaBitrateViewer.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace MediaBitrateViewer.App.Services;

public sealed class ApplicationFoldersService : IApplicationFoldersService
{
    private readonly string _settingsFolder;
    private readonly string _cacheFolder;
    private readonly ILogger<ApplicationFoldersService> _logger;

    public ApplicationFoldersService(
        string settingsFolder,
        string cacheFolder,
        ILogger<ApplicationFoldersService> logger)
    {
        ArgumentException.ThrowIfNullOrEmpty(settingsFolder);
        ArgumentException.ThrowIfNullOrEmpty(cacheFolder);
        ArgumentNullException.ThrowIfNull(logger);
        _settingsFolder = settingsFolder;
        _cacheFolder = cacheFolder;
        _logger = logger;
    }

    public Task OpenSettingsFolderAsync() => OpenAsync(_settingsFolder);

    public Task OpenCacheFolderAsync() => OpenAsync(_cacheFolder);

    private Task OpenAsync(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            var psi = BuildStartInfo(path);
            using var process = Process.Start(psi);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to open folder {Folder}", path);
        }
        return Task.CompletedTask;
    }

    private static ProcessStartInfo BuildStartInfo(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true };
        }
        if (OperatingSystem.IsMacOS())
        {
            return new ProcessStartInfo("open", $"\"{path}\"") { UseShellExecute = false };
        }
        return new ProcessStartInfo("xdg-open", $"\"{path}\"") { UseShellExecute = false };
    }
}
