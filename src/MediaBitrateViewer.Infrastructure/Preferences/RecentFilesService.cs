using System.Collections.ObjectModel;
using MediaBitrateViewer.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace MediaBitrateViewer.Infrastructure.Preferences;

public sealed class RecentFilesService : IRecentFilesService
{
    private const int MaxRecentFiles = 10;

    private readonly IUserPreferencesStore _store;
    private readonly ILogger<RecentFilesService> _logger;
    private readonly ObservableCollection<string> _files;

    public ReadOnlyObservableCollection<string> Files { get; }

    public RecentFilesService(IUserPreferencesStore store, ILogger<RecentFilesService> logger)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(logger);
        _store = store;
        _logger = logger;

        var prefs = store.Load();
        _files = new ObservableCollection<string>(prefs.RecentFiles);
        Files = new ReadOnlyObservableCollection<string>(_files);
    }

    public async Task AddAsync(string filePath)
    {
        ArgumentException.ThrowIfNullOrEmpty(filePath);

        // Move-to-front: remove any existing occurrence, then prepend.
        for (var i = _files.Count - 1; i >= 0; i--)
        {
            if (string.Equals(_files[i], filePath, StringComparison.Ordinal))
                _files.RemoveAt(i);
        }
        _files.Insert(0, filePath);
        while (_files.Count > MaxRecentFiles)
            _files.RemoveAt(_files.Count - 1);

        await PersistAsync().ConfigureAwait(false);
    }

    public async Task RemoveAsync(string filePath)
    {
        ArgumentException.ThrowIfNullOrEmpty(filePath);

        for (var i = _files.Count - 1; i >= 0; i--)
        {
            if (string.Equals(_files[i], filePath, StringComparison.Ordinal))
                _files.RemoveAt(i);
        }

        await PersistAsync().ConfigureAwait(false);
    }

    public async Task ClearAsync()
    {
        _files.Clear();
        await PersistAsync().ConfigureAwait(false);
    }

    private async Task PersistAsync()
    {
        try
        {
            var snapshot = _files.ToArray();
            await _store.UpdateAsync(
                current => current with { RecentFiles = snapshot },
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Persisting recent files failed");
        }
    }
}
